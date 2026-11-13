using System;
using System.Reflection;
using CharacterDestruction;
using HarmonyLib;
using UnityEngine;

namespace HawChow.GTFOKillFeed;

// Unity/IL2CPP half of the optional corpse dissolve. It only ever touches the *native* Decay pool that
// the game itself uses for corpse decay:
//   * never Object.Destroy (that would recycle the global Decay sound player and break every later decay);
//   * never creates the pool (no asset loading, no pool initialisation) - if the pool is absent we skip;
//   * never patches Decay, never detours the death state machine, never touches shared materials (Decay
//     drives a per-instance MaterialPropertyBlock on its own cloned renderer).
// Everything it borrows is handed back exactly once: the game's own Update() returns it when
// stopAction==Deactivate, and if Play never started we Cleanup() the clone and pool.Return() once.
//
// Diagnostics (1.2.9 review): the reason vocabulary is split per gate/step (pool-missing,
// no-free-instance, borrow-error, not-initialized, play-not-observed, play-threw, probe-unknown,
// patched-by-third-party, ...) and every line carries the scheduleId, the poll count and the native
// exception type/message, because the shipped 1.2.9 line merged all of them into one catch-all reason and a
// real host session (4/4 skipped, 78 ragdoll-unavailable) could not be diagnosed.
// 1.2.19 (fade NRE root cause): the pooled path no longer calls Decay.Initialize at all. The real 1.2.18 log
// showed `fade=failed;reason=initialize-threw;message=System.NullReferenceException` because a borrowed
// instance is only an object, never an armed effect: the game initialises it itself. The reference
// implementation (zose DeadBodyOptimization) likewise only finds an existing Decay via
// GetComponentInChildren<Decay>(true) and takes over Play; readiness (m_targetRenderer) is the whole contract.
// 1.2.18 (fade root cause): a renderer that comes out of GetComponentsInChildren<Renderer>(true) is ALWAYS wrapped
// as UnityEngine.Renderer. Il2CppInterop fills a T[] from Il2CppArrayBase<T>::op_Implicit, which calls get_Item ->
// Il2CppReferenceArray<T>::WrapElement, and WrapElement constructs every element through typeof(T)'s (IntPtr)
// constructor (verified in the Test profile's Il2CppInterop.Runtime.dll). The runtime subclass is never used, so
// `renderer is SkinnedMeshRenderer` / `is MeshRenderer` can never be true - which is exactly why the 1.2.17 real log
// printed `fade=failed;reason=initialize-failed` on 3/3 corpses without ever calling the native Initialize.
// TryCast<T>() asks il2cpp_object_get_class + il2cpp_class_is_assignable_from and returns default(T) (null) when the
// object is not that type - the supported answer to this representation mismatch. Everything here fails closed.
internal static class FadeRendererCast
{
    // The ONE type decision, shared by the corpse-own component path and the pooled borrow path.
    internal static bool TryResolve(Renderer renderer, out SkinnedMeshRenderer skinned, out MeshRenderer mesh)
    {
        skinned = null;
        mesh = null;
        if (renderer == null) return false;                      // destroyed, or never captured (Unity fake-null)
        try
        {
            if (renderer.Pointer == IntPtr.Zero) return false;  // wrapper alive, native object already gone
            skinned = renderer.TryCast<SkinnedMeshRenderer>();
            if (skinned != null) return true;
            mesh = renderer.TryCast<MeshRenderer>();
            return mesh != null;
        }
        catch (Exception error)
        {
            // Fail closed: an unresolvable renderer must never become a native target.
            CorpseFade.Warn(error);
            skinned = null;
            mesh = null;
            return false;
        }
    }

    internal static bool IsSkinned(Renderer renderer)
        => TryResolve(renderer, out SkinnedMeshRenderer skinned, out _) && skinned != null;

    // 1.2.19: the ONE readiness fact, and the only thing KillFeed may ask of a Decay before playing it. A
    // component is usable only when the game itself already armed it (m_targetRenderer points at a live
    // renderer); KillFeed never calls Decay.Initialize any more. The 1.2.18 pooled path initialised a borrowed
    // instance whose renderer was already dead - exactly the native NullReferenceException in the real log
    // (`fade=failed;reason=initialize-threw` + `System.NullReferenceException`) - while the reference
    // implementation only ever takes over the Play of a component the game already wired.
    internal static bool HasTarget(Decay decay)
    {
        if (decay == null) return false;
        try
        {
            Renderer target = decay.m_targetRenderer;
            return target != null && target.Pointer != IntPtr.Zero;
        }
        catch (Exception error) { CorpseFade.Warn(error); return false; }
    }

    // For receipts: name the type the native call really receives, instead of the wrapper's `Renderer`.
    internal static string Describe(Renderer renderer)
    {
        if (renderer == null) return "none";
        try
        {
            if (renderer.Pointer == IntPtr.Zero) return "destroyed";
            if (renderer.TryCast<SkinnedMeshRenderer>() != null) return "SkinnedMeshRenderer";
            if (renderer.TryCast<MeshRenderer>() != null) return "MeshRenderer";
            return "Renderer/unresolved";
        }
        catch (Exception) { return "unreadable"; }
    }
}
internal static class CorpseFade
{
    private static readonly CorpseFadeCoordinator Coordinator = new();
    private static readonly DecayFadeTarget Target = new();
    private static readonly CorpseFadeDiagnostics Diagnostics = new();
    private static bool _patchChecked;
    private static bool _playPatched;

    internal static int Active => Coordinator.Active;

    // Called at the existing EndAt stage, immediately before the corpse is hidden/recycled.
    // Called at the existing EndAt stage, immediately before the corpse is hidden/recycled. `native` is the
    // Decay component the corpse's own model/hierarchy carries (resolved by CorpseFeedback from the snapshot
    // taken in the death call), or null when the hierarchy exposed none.
    internal static void TryBegin(Renderer renderer, bool offScene, bool inLevel, int scheduleId, Decay native = null)
    {
        try
        {
            if (!_patchChecked)
            {
                _playPatched = PlayIsPatched();
                _patchChecked = true;
                if (_playPatched && Diagnostics.AllowLine(CorpseFadePolicy.Reason(CorpseFadeSkip.PatchedByThirdParty)))
                    Plugin.Instance?.Log.LogInfo("[CORPSE] fade=disabled;reason=" + CorpseFadePolicy.Reason(CorpseFadeSkip.PatchedByThirdParty) + ";detail=Decay.Play has third-party patches");
            }
            var plan = new CorpseFadePlan(
                Plugin.CorpseFade != null && Plugin.CorpseFade.Value,
                Plugin.CorpseEffects == null || Plugin.CorpseEffects.Value,
                inLevel,
                Paused(),
                _playPatched,
                PrefabIsSafe(),
                PoolState(),
                renderer != null && !ReferenceEquals(renderer, null),
                offScene,
                Coordinator.Active);
            // 1) The corpse's own native Decay component first: no pool borrow and no renderer validation,
            //    so a corpse the game never gave a ragdoll still dissolves (1.2.16, parity with the
            //    reference mod). A refusal is a named line; a failure logs fade=fallback and the pooled
            //    path below still runs, so the dissolve can only get more likely, never silently less.
            if (TryNative(plan, native, renderer, scheduleId)) return;
            if (!CorpseFadePolicy.ShouldAttempt(plan))
            {
                LogSkip(CorpseFadePolicy.SkipReason(plan), scheduleId);
                return;
            }
            if (!Coordinator.TryStart(Target, renderer, out CorpseFadeOutcome outcome)) { LogFailure(outcome, scheduleId); return; }
            if (outcome == CorpseFadeOutcome.StartedProbeUnknown) LogStartedUnknown(scheduleId, renderer);
            else LogStarted(scheduleId, renderer);
        }
        catch (Exception error) { Warn(error); }
    }

    // The corpse's own Decay component: the same native dissolve the game runs from ES_Dead, reached without
    // patching a state machine. 1.2.19: it is only PLAYED, never initialised - KillFeed must never arm a Decay
    // itself (that call on an unprepared component is the real log's native NullReferenceException), so the one
    // readiness fact is the component's own m_targetRenderer, and ownership is confirmed from m_playing, the flag
    // the native Play writes synchronously. The component belongs to the corpse, so nothing is borrowed, tracked
    // or returned here - the Coordinator's pool bookkeeping stays untouched.
    private static bool TryNative(in CorpseFadePlan plan, Decay native, Renderer renderer, int scheduleId)
    {
        int stopAction = -1;
        bool autoCleanup = false, playing = true;
        if (native != null)
        {
            try
            {
                stopAction = (int)native.m_stopAction;
                autoCleanup = native.m_autoCleanup;
                playing = native.m_playing;
            }
            catch (Exception error) { Warn(error); native = null; }
        }
        CorpseFadeNativePath path = CorpseFadePolicy.NativePath(plan, native != null, stopAction, autoCleanup, playing);
        if (!CorpseFadePolicy.ShouldAttemptNative(path))
        {
            // Review R6: the receipt used to require `native != null` on top of the policy, so a corpse whose
            // hierarchy has NO Decay component could never print `reason=no-decay-component` - the one line an
            // operator needs to explain the fallback. The gate is the named refusal alone.
            LogNativeUnavailable(path, stopAction, autoCleanup, playing, scheduleId);
            return false;
        }
        try
        {
            // 1.2.19: play only what the game already armed. A component without a live m_targetRenderer cannot
            // dissolve anything, and initialising it here is exactly what the reference implementation never does.
            if (!FadeRendererCast.HasTarget(native)) { LogFallback("native-not-initialized", scheduleId); return false; }
            native.Play(CorpseFadePolicy.FadeSeconds);
        }
        catch (Exception error) { Warn(error); LogFallback("native-threw", scheduleId); return false; }
        bool started = false;
        try { started = native.m_playing; } catch (Exception error) { Warn(error); }
        if (!started) { LogFallback("native-not-playing", scheduleId); return false; }
        if (Diagnostics.ClaimTerminal(scheduleId))
            Plugin.Instance?.Log.LogInfo($"[CORPSE] fade=begun;path=native-decay;seconds={CorpseFadePolicy.FadeSeconds:0.00};renderer={FadeRendererCast.Describe(renderer)};stopAction={stopAction};autoCleanup={autoCleanup};scheduleId={scheduleId};note=corpse-own-component-no-borrow");
        return true;
    }
    // One explicit line when the native component could not take the dissolve and the pooled path takes over:
    // the real log's initialize-failed / no-free-instance only ever described the POOLED attempt, so the
    // fallback itself needs a name (bounded like every other line here).
    private static void LogFallback(string reason, int scheduleId)
    {
        if (!Diagnostics.AllowLine(reason)) return;
        Plugin.Instance?.Log.LogInfo($"[CORPSE] fade=fallback;reason={reason};scheduleId={scheduleId};note=pooled-path-or-hide");
    }
    // The named refusal of the corpse-own component. Split out of TryNative so nothing about "did we read a
    // component" can suppress it (review R6: `reason=no-decay-component` was unreachable in 1.2.16).
    private static void LogNativeUnavailable(CorpseFadeNativePath path, int stopAction, bool autoCleanup, bool playing, int scheduleId)
    {
        if (!CorpseFadePolicy.ShouldLogNativeRefusal(path)) return;
        if (!Diagnostics.AllowLine(CorpseFadePolicy.Reason(path))) return;
        Plugin.Instance?.Log.LogInfo($"[CORPSE] fade=native-unavailable;reason={CorpseFadePolicy.Reason(path)};stopAction={(stopAction < 0 ? "unreadable" : stopAction.ToString())};autoCleanup={autoCleanup};playing={playing};scheduleId={scheduleId}");
    }
    // Bookkeeping only, decided from the real native state: an instance the game still animates (or
    // whose state cannot be read) keeps its slot, so the concurrency cap stays true across pauses,
    // long frames and level re-entry. Nothing is ever returned or destroyed here.
    internal static void Sweep()
    {
        try
        {
            if (Coordinator.Sweep(Target) > 0 && Diagnostics.AllowWarning())
                Plugin.Instance?.Log.LogWarning($"[CORPSE] fade=held;reason=probe-unknown;active={Coordinator.Active}");
        }
        catch (Exception error) { Warn(error); }
    }

    internal static void Reset()
    {
        // Bookkeeping only: the native pool is owned by the game and is torn down with the level.
        try
        {
            if (Coordinator.Reset(Target) > 0 && Diagnostics.AllowWarning())
                Plugin.Instance?.Log.LogWarning($"[CORPSE] fade=reset-held;reason=probe-unknown;active={Coordinator.Active}");
        }
        catch (Exception error) { Warn(error); }
        _patchChecked = false;
        _playPatched = false;
        // A new level gets a fresh - still hard-capped - diagnostic allowance and honest counters.
        int suppressed = Diagnostics.TerminalSuppressed;
        // 1.2.19: the terminal allowance is 8 corpses per level; every corpse after that used to leave NO trace at all.
        // One line at the boundary says how many, so "the dissolve never ran" and "the log stopped recording it" can be
        // told apart without raising the per-corpse budget.
        if (suppressed > 0)
            Plugin.Instance?.Log.LogInfo($"[CORPSE] fade=summary;terminal-suppressed={suppressed};cap={CorpseFadeDiagnostics.MaxTerminalPerLevel};note=more-corpuses-than-the-terminal-allowance-this-level");
        Diagnostics.Reset();
        Coordinator.ResetCounters();
    }

    private static bool Paused()
    {
        try { return Time.timeScale <= 0.0001f || Time.deltaTime <= 0f; }
        catch { return true; }
    }

    // Any patch on Decay.Play can swallow/re-enter the call (DBO 1.5.0 does exactly that for unknown
    // durations), which would leave our borrowed instance playing=false and never returned.
    private static bool PlayIsPatched()
    {
        try
        {
            MethodInfo withDuration = typeof(Decay).GetMethod("Play", new[] { typeof(float) });
            MethodInfo parameterless = typeof(Decay).GetMethod("Play", Type.EmptyTypes);
            return HasPatches(withDuration) || HasPatches(parameterless);
        }
        catch { return true; }
    }

    private static bool HasPatches(MethodBase method)
    {
        if (method == null) return true;   // cannot verify -> refuse to touch the pool
        Patches info;
        try { info = Harmony.GetPatchInfo(method); }
        catch { return true; }
        if (info == null) return false;
        try
        {
            return info.Prefixes.Count > 0 || info.Postfixes.Count > 0
                || info.Transpilers.Count > 0 || info.Finalizers.Count > 0;
        }
        catch { return true; }
    }

    // REPORT4: the shipped DecayEffect prefab has stopAction=Deactivate + autoCleanup=true, i.e. the
    // instance returns itself and the cloned renderer is destroyed by Decay.Verify it at runtime.
    private static bool PrefabIsSafe()
    {
        try
        {
            GameObject prefab = Decay.prefab;
            if (prefab == null) return false;
            Decay prototype = prefab.GetComponent<Decay>();
            if (prototype == null) return false;
            return CorpseFadePolicy.PrefabIsSafe((int)prototype.m_stopAction, prototype.m_autoCleanup);
        }
        catch { return false; }
    }

    // Read-only pool observation: "the game never built the pool" and "every instance is in use" are
    // different diagnoses, and only the second one can change a moment later. Nothing here creates or
    // initialises a pool, so the fallback stays exactly the shipped instant hide.
    private static CorpseFadePoolState PoolState()
    {
        try
        {
            ObjectPooler.Pool pool = Decay.pool;
            if (pool == null) return CorpseFadePoolState.Missing;
            return pool.freeInstanceCount > 0 ? CorpseFadePoolState.Ready : CorpseFadePoolState.Exhausted;
        }
        catch { return CorpseFadePoolState.Missing; }
    }

    private static void LogSkip(CorpseFadeSkip skip, int scheduleId)
    {
        // Deliberate user intent (feature off, lobby, no renderer, off-scene) stays silent; only gates an
        // operator has to explain print a terminal line (F5: the terminal budget is its own allowance, so a
        // `fade=fallback` receipt can never spend it and the first MaxTerminalPerLevel corpses always print).
        if (!CorpseFadePolicy.IsReportable(skip)) return;
        string reason = CorpseFadePolicy.Reason(skip);
        if (!Diagnostics.ClaimTerminal(scheduleId)) return;
        Plugin.Instance?.Log.LogInfo($"[CORPSE] fade=skipped;reason={reason};scheduleId={scheduleId};active={Coordinator.Active}");
    }

    private static void LogFailure(CorpseFadeOutcome outcome, int scheduleId)
    {
        string reason = CorpseFadePolicy.Reason(outcome);
        // F2 (1.2.19 review): the pooled path is fail-closed, so "this borrowed instance cannot be proven to be
        // ours" is a SKIP - it was handed back untouched, nothing was broken. Either way a corpse gets exactly ONE
        // terminal fade= line (fade=begun | fade=skipped | fade=failed), claimed once per scheduleId (F5).
        if (outcome == CorpseFadeOutcome.PoolUnverifiable)
        {
            if (!Diagnostics.ClaimTerminal(scheduleId)) return;
            Plugin.Instance?.Log.LogInfo($"[CORPSE] fade=skipped;reason={reason};scheduleId={scheduleId};polls={Coordinator.LastPolls};note=pooled-instance-unverifiable;active={Coordinator.Active}");
            return;
        }
        if (Diagnostics.ClaimTerminal(scheduleId))
            Plugin.Instance?.Log.LogInfo($"[CORPSE] fade=failed;reason={reason};scheduleId={scheduleId};polls={Coordinator.LastPolls};failures={Coordinator.Failures};manualReleases={Coordinator.ManualReleases};active={Coordinator.Active}");
        // The native call that broke is the one fact 1.2.9 threw away: log its type and message once the
        // reason budget allows it, so a host log alone can identify the failing entry point.
        Exception error = Coordinator.LastError;
        if (error == null || !Diagnostics.AllowWarning()) return;
        Plugin.Instance?.Log.LogWarning($"[CORPSE] fade=error;reason={reason};type={error.GetType().Name};message={Short(error.Message)}");
    }

    private static void LogStarted(int scheduleId, Renderer renderer)
    {
        if (!Diagnostics.ClaimTerminal(scheduleId)) return;
        Plugin.Instance?.Log.LogInfo($"[CORPSE] fade=begun;seconds={CorpseFadePolicy.FadeSeconds:0.00};renderer={FadeRendererCast.Describe(renderer)};concurrent={Coordinator.Active};scheduleId={scheduleId}");
    }

    private static void LogStartedUnknown(int scheduleId, Renderer renderer)
    {
        string reason = CorpseFadePolicy.Reason(CorpseFadeOutcome.StartedProbeUnknown);
        if (!Diagnostics.ClaimTerminal(scheduleId)) return;
        Plugin.Instance?.Log.LogInfo($"[CORPSE] fade=begun;reason={reason};seconds={CorpseFadePolicy.FadeSeconds:0.00};renderer={FadeRendererCast.Describe(renderer)};concurrent={Coordinator.Active};scheduleId={scheduleId}");
    }

    private static string Short(string text)
    {
        if (string.IsNullOrEmpty(text)) return "-";
        string flat = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return flat.Length <= 140 ? flat : flat.Substring(0, 140);
    }

    internal static void Warn(Exception error)
    {
        if (!Diagnostics.AllowWarning()) return;
        Plugin.Instance?.Log.LogWarning($"[CORPSE] fade=error;reason=unexpected;type={error.GetType().Name};message={Short(error.Message)}");
    }

    // Thin adapter over the native API; the borrow/release decisions live in CorpseFadeCoordinator.
    private sealed class DecayFadeTarget : ICorpseFadeTarget
    {
        // The pool is only ever *read* here. "Missing" and "no free instance" used to collapse into a
        // single null handle, which is why the host log could not tell them apart.
        public CorpseFadeBorrow TryBorrow(out object handle)
        {
            handle = null;
            ObjectPooler.Pool pool = Decay.pool;
            if (pool == null) return CorpseFadeBorrow.PoolMissing;
            if (pool.freeInstanceCount <= 0) return CorpseFadeBorrow.NoFreeInstance;
            Decay instance = Decay.GetInstance();
            if (instance == null) return CorpseFadeBorrow.NoFreeInstance;   // raced with the game's own borrow
            handle = instance;
            return CorpseFadeBorrow.Ok;
        }

        // The only ownership signal: m_playing. A destroyed native object (level unload) is Gone, and
        // an unreadable one is Unknown -> the coordinator then keeps the slot instead of guessing.
        public CorpseFadeState Probe(object handle)
        {
            if (handle is not Decay decay) return CorpseFadeState.Gone;
            if (decay == null) return CorpseFadeState.Gone;
            return decay.m_playing ? CorpseFadeState.Playing : CorpseFadeState.Idle;
        }

        // 1.2.19: the borrowed instance is never initialised. The native Initialize() has no exception path of its
        // own (it *logs* when the renderer is a destroyed Unity object, 0x162fdd9) and on a pooled instance the
        // game has not armed it throws a native NullReferenceException - the real log's
        // F2 (1.2.19 review): FAIL-CLOSED, and deliberately not pretending otherwise. A pooled Decay carries the
        // renderer of the corpse it dissolved LAST time (Decay clones its own renderer), so m_targetRenderer can
        // never prove it points at THIS corpse; the only way to bind it would be Decay.Initialize, which is
        // forbidden (native NullReferenceException on an unprepared instance, gated to 0 calls). KillFeed therefore
        // NEVER plays a borrowed instance: the pool is only borrowed, probed and handed back, and this constant
        // false is the single gate that enforces it (pinned by Verify + Test-CorpseFadeIl). Re-enabling the pooled
        // path requires an interop-verified identity proof here - nothing else.
        public bool Ready(object handle) => false;

        public void Play(object handle, float seconds)
        {
            if (handle is Decay decay) decay.Play(seconds);
        }


        public void Cleanup(object handle)
        {
            if (handle is Decay decay) decay.Cleanup();
        }

        public void Return(object handle)
        {
            if (handle is not Decay decay) return;
            ObjectPooler.Pool pool = Decay.pool;
            pool?.Return(decay.gameObject);
        }
    }
}
