using System;
using System.Reflection;
using Enemies;
using CharacterDestruction;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;

namespace HawChow.GTFOKillFeed;

// Unity/IL2CPP half of the optional corpse dissolve. It only ever touches the *native* Decay pool that
// the game itself uses for corpse decay:
//   * never Object.Destroy (that would recycle the global Decay sound player and break every later decay);
//   * never creates the pool (no asset loading, no pool initialisation) - if the pool is absent we skip;
//   * never patches Decay, never detours the death state machine, never touches shared materials (Decay
//     drives a per-instance MaterialPropertyBlock on its own cloned renderer).
// Playing instances are left to the game. A failed borrow is only returned after independent proof before
// both writes; an idle observation by itself says nothing about the free list.
//
// Diagnostics (1.2.9 review): the reason vocabulary is split per gate/step (pool-missing,
// no-free-instance, borrow-error, not-initialized, play-not-observed, play-threw, probe-unknown,
// patched-by-third-party, ...) and every line carries the scheduleId, the poll count and the native
// exception type/message, because the shipped 1.2.9 line merged all of them into one catch-all reason and a
// real host session (4/4 skipped, 78 ragdoll-unavailable) could not be diagnosed.
// Pooled arming is isolated in DecayFadeTarget.Ready: TryCast the renderer, pass a real empty IRF list,
// then verify m_targetRenderer before Play. Earlier logs showed an Initialize NRE, not its native cause.
// The corpse-own path only plays an already-armed component; the pool path may arm a borrowed one.
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
    // Skin/model replacements can leave an enabled wrapper with no drawable mesh. Do not send it to native Initialize.
    internal static bool HasDrawableMesh(Renderer renderer)
    {
        if (renderer == null) return false;
        try
        {
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) return false;
            if (!TryResolve(renderer, out SkinnedMeshRenderer skinned, out MeshRenderer mesh)) return false;
            Mesh data = skinned != null ? skinned.sharedMesh : mesh.GetComponent<MeshFilter>()?.sharedMesh;
            return data != null && data.vertexCount > 0;
        }
        catch { return false; }
    }

    // Readiness of the corpse-own component only. Borrowed instances need Ready's target-identity proof.
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

    // ---- 1.2.22 (Survival FX.3 black scene): classify the object a pooled clone would be made from ------
    // The pooled path does not dissolve the corpse itself - the native Decay builds a COPY of the corpse
    // renderer's GameObject ("<name>_DecayRenderer") and animates the copy. The copy inherits the source
    // GameObject's components and its teardown is the native component's, so a source that already carries a
    // render registration (ShadowEnemyRenderer owns a static Instances list and registers on enable) or that
    // already belongs to a dissolve must never be cloned: that is the duplicate whose leftover entry threw in
    // ShadowEnemyRenderer.ShouldRender for 3.74 s in the 17:45 session. Nothing is assumed about WHO removed
    // the renderer; an input that could produce such a copy is simply not used, and the corpse keeps the
    // shipped instant hide at its own deadline. Read-only: no destroy, no registry write, no object created.
    internal const string DecayCopySuffix = "_DecayRenderer";

    internal static CorpseFadeInput Observe(Renderer renderer)
    {
        if (renderer == null) return CorpseFadeInput.Safe;                  // never captured: no clone either
        try
        {
            if (renderer.Pointer == IntPtr.Zero) return CorpseFadeInput.Opaque;
            GameObject source = renderer.gameObject;
            if (source == null) return CorpseFadeInput.Opaque;              // Unity fake-null wrapper
            Transform node = source.transform;
            string ownName = source.name ?? string.Empty;
            bool decayCopy = IsDecayCopy(ownName);
            bool shadowRegistered = HasShadowRegistration(source);
            // 1.2.22 review: whether the native copy is shallow (this GameObject) or deep (its children too) was
            // not provable offline (the shipped global-metadata.dat is a 768-byte stub, so no method body can be
            // bound to CreateDecayRenderer). The scan therefore covers BOTH readings: the source GameObject and
            // its subtree, including inactive children. Conservative on purpose: it can only refuse more, and a
            // refused corpse keeps the shipped instant hide.
            bool clonePresent = false, truncated = false, shadowBelow = false;
            int visited = 0;
            ScanForRegistrations(node, 0, ref visited, ref shadowBelow, ref truncated, ref clonePresent);
            // Ancestors: an armed/playing Decay above the source means the game already owns this dissolve. If the
            // walk stops on its budget while a parent is still left, the owner is unknown -> unproven (review #4).
            bool ownerArmed = false, concluded = false;
            Transform ancestor = node;
            for (int depth = 0; depth < CorpseFadePolicy.AncestorMaxDepth; depth++)
            {
                if (ancestor == null) break;
                // A dissolve copy anywhere above the source means the source belongs to that copy's hierarchy.
                if (depth > 0 && IsDecayCopy(ancestor.name)) { decayCopy = true; concluded = true; break; }
                if (OwnsDissolve(ancestor)) { ownerArmed = true; concluded = true; break; }
                ancestor = ancestor.parent;
            }
            if (CorpseFadePolicy.AncestryTruncated(concluded, ancestor != null)) truncated = true;
            // An existing copy of THIS object (the native clone keeps the source's name and adds the suffix).
            Transform parent = node.parent;
            if (parent != null)
            {
                int children = 0, scanned = 0;
                bool readable = true;
                try { children = parent.childCount; }
                catch (Exception error) { CorpseFade.Warn(error); readable = false; truncated = true; }
                string expected = ownName + DecayCopySuffix;
                for (int i = 0; readable && i < children && i < CorpseFadePolicy.SiblingMaxScan; i++)
                {
                    scanned++;
                    Transform sibling;
                    try { sibling = parent.GetChild(i); }
                    catch (Exception error) { CorpseFade.Warn(error); truncated = true; continue; }
                    if (sibling == null || sibling == node) continue;
                    if (string.Equals(sibling.name, expected, StringComparison.Ordinal)) { clonePresent = true; break; }
                }
                if (!clonePresent && CorpseFadePolicy.SiblingScanTruncated(children, scanned)) truncated = true;
            }
            return new CorpseFadeInput(decayCopy, shadowRegistered, ownerArmed, clonePresent, shadowBelow, truncated);
        }
        catch (Exception error)
        {
            // Fail closed: an input that cannot be classified is never cloned.
            CorpseFade.Warn(error);
            return CorpseFadeInput.Opaque;
        }
    }

    private static bool IsDecayCopy(string name)
        => !string.IsNullOrEmpty(name) && name.EndsWith(DecayCopySuffix, StringComparison.Ordinal);


    private static bool HasShadowRegistration(GameObject go)
        => go != null && go.GetComponent<ShadowEnemyRenderer>() != null;

    // Depth-first over the CHILDREN of the source node (the node itself is reported apart, so a receipt can say
    // "self" or "subtree"). Sets `found` when a shadow registration lives below the source and lifts "a dissolve
    // copy already exists here" into clonePresent. 1.2.22 review #4: the walk reports `truncated` whenever it
    // stopped on a depth/node budget with children still unvisited (or could not read a child), because "we did
    // not finish looking" must never read as "nothing there". An exactly-finished subtree - including a leaf at
    // the very last node - stays complete, and a found hazard returns immediately.
    private static void ScanForRegistrations(Transform node, int depth, ref int visited, ref bool found,
        ref bool truncated, ref bool clonePresent)
    {
        if (node == null || found) return;
        int children;
        try { children = node.childCount; }
        catch (Exception error) { CorpseFade.Warn(error); truncated = true; return; }
        CorpseFadePolicy.SubtreeStep step = CorpseFadePolicy.SubtreeVerdict(depth, children, visited);
        if (step == CorpseFadePolicy.SubtreeStep.Leaf) return;
        if (step == CorpseFadePolicy.SubtreeStep.Truncated) { truncated = true; return; }
        for (int i = 0; i < children; i++)
        {
            if (found) return;                                     // hazard already proven: stop, do not extend
            if (visited >= CorpseFadePolicy.ScanMaxNodes) { truncated = true; return; }
            Transform child;
            try { child = node.GetChild(i); }
            catch (Exception error) { CorpseFade.Warn(error); truncated = true; continue; }
            if (child == null) { truncated = true; continue; }      // destroyed slot: cannot be inspected
            visited++;
            try
            {
                if (IsDecayCopy(child.name)) clonePresent = true;
                if (child.GetComponent<ShadowEnemyRenderer>() != null) found = true;
            }
            catch (Exception error) { CorpseFade.Warn(error); truncated = true; continue; }
            if (!found) ScanForRegistrations(child, depth + 1, ref visited, ref found, ref truncated, ref clonePresent);
        }
    }

    // 1.2.22 review: the copy the game already created for this corpse (Decay.m_decayRenderer). A copy whose
    // Renderer was destroyed, or which has no mesh, cannot be animated - starting Play on it would dissolve
    // nothing and keep whatever registered that dead Renderer. Only a live, mesh-backed copy counts. Read-only:
    // this never creates, initialises or destroys anything.
    internal static bool HasLiveCopy(Renderer copy)
    {
        if (copy == null || copy.Pointer == IntPtr.Zero) return false;
        try
        {
            if (!TryResolve(copy, out SkinnedMeshRenderer skinned, out MeshRenderer mesh)) return false;
            Mesh data = skinned != null ? skinned.sharedMesh : mesh.GetComponent<MeshFilter>()?.sharedMesh;
            return data != null && data.vertexCount > 0;
        }
        catch (Exception error) { CorpseFade.Warn(error); return false; }
    }
    // "The game already owns a dissolve for this hierarchy": the component is playing, or it is armed with a
    // renderer. An unreadable component counts as owned, so a broken read can never authorise a second clone.
    private static bool OwnsDissolve(Transform node)
    {
        if (node == null) return false;
        Decay decay = node.GetComponent<Decay>();
        if (decay == null) return false;
        try
        {
            if (decay.m_playing) return true;
            Renderer armed = decay.m_targetRenderer;
            return armed != null && armed.Pointer != IntPtr.Zero;
        }
        catch (Exception error) { CorpseFade.Warn(error); return true; }
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
    private static MethodInfo _skinnedInitialize;
    private const string PatchReadFailed = "init-patches-unreadable";
    private static readonly Func<string> ReadCallbackRisk = ReadDelayedCallbackRisk;
    private static CorpseFadeFrameRisk _frameRisk;
    private static int _compatBeforeScan;
    private static int _compatSkipped;
    private static int _started, _deferred, _skipped, _failed;
    private static int _gameReused, _gameRetimed;
    private static int _gameCoroutines;
    private static MethodInfo _decayDelayed;
    private const float GameCoroutineDelay = 0.1f;
    // 1.2.26: the pooled path's own back-off (see CorpseFadeBackoff). A struct on purpose: it is per-level state
    // that is reset with the level, and it never touches a native object.
    private static CorpseFadeBackoff _backoff;
    // How many corpses this level declined to even ask the pool about, and the last pool shape we read - both
    // printed once at the level boundary, so "the dissolve stopped" and "the log stopped recording it" differ.
    private static int _backedOff;
    private static CorpseFadePoolFacts? _lastPoolFacts;
    private static int _zeroSkipped, _deadlineWaits;
    // A cached zero alone is never enough: a newly available slot must restore normal preparation.
    internal static bool StillWithoutInstances() => _lastPoolFacts?.State == CorpseFadePoolState.ZeroInstances
        && _patchChecked && !_playPatched && Plugin.CorpseFade?.Value == true && Plugin.CorpseEffects?.Value != false
        && !Paused() && SamplePool().State == CorpseFadePoolState.ZeroInstances;
    internal static CorpseFadeAttempt WaitAtDeadline() { _deadlineWaits++; return CorpseFadeAttempt.AtDeadline; }

    internal static int Active => Coordinator.Active;

    // The compatibility decision needs no corpse object. Reject before crossing into the renderer hierarchy.
    internal static bool SkipBeforeSelection(bool offScene, bool inLevel, int scheduleId)
    {
        try
        {
            if (offScene || !inLevel || Plugin.CorpseFade?.Value != true || Plugin.CorpseEffects?.Value == false || Paused())
                return false;
            CheckPlayPatches();
            string risk = _playPatched ? CorpseFadePolicy.Reason(CorpseFadeSkip.PatchedByThirdParty) : DelayedCallbackRisk();
            if (risk == null) return false;
            _compatBeforeScan++;
            _compatSkipped++;
            LogSkip(CorpseFadeSkip.PatchedByThirdParty, CorpseFadeHazard.None, scheduleId, risk);
        }
        catch (Exception) { /* Fail closed without letting optional fade interrupt the cleanup. */ }
        return true;
    }

    // Read-only query used when the corpse picks WHICH renderer to dissolve (CorpseFeedback.FadeRenderer). It runs
    // the very same classification the pooled gate uses, so "selected" and "armed" can never disagree: a renderer
    // that would be refused here is simply not offered to TryBegin.
    internal static bool IsSafeInput(object renderer)
    {
        if (renderer is not Renderer candidate || candidate == null) return false;
        return !CorpseFadePolicy.IsHazardous(FadeRendererCast.Observe(candidate));
    }

    // Bounded pre-cleanup reservation or the unresolved EndAt fallback, both before Hide/Recycle.
    // `native` is the corpse-own component; null means the pooled path must supply its own proof.
    internal static CorpseFadeAttempt TryBegin(Renderer renderer, bool offScene, bool inLevel, int scheduleId, Decay native = null, bool deferTransient = false)
    {
        try
        {
            CheckPlayPatches();
            // 1.2.26: the cheap, side-effect-free gates are answered ONCE, in the order the policy asks them, so the
            // back-off can decide whether the hierarchy walk below is worth anything at all. Before this, every
            // corpse in a swarm paid a subtree/ancestor/sibling scan (1.2.22 hazard) and up to three attempts to
            // learn the same unchanged fact: the pool is held by the game.
            bool fadeEnabled = Plugin.CorpseFade != null && Plugin.CorpseFade.Value;
            bool corpseEffects = Plugin.CorpseEffects == null || Plugin.CorpseEffects.Value;
            bool paused = Paused();
            bool prefabSafe = PrefabIsSafe();
            CorpseFadePoolFacts poolFacts = SamplePool();
            // A delayed writer can wake the SAME pool object after it is returned and reused, even by a mesh arm.
            // Do not add our own playback. Preserve the dedicated zero-inventory accounting if neither route exists.
            string risk = fadeEnabled && corpseEffects && inLevel && !paused && !_playPatched && !offScene
                && renderer != null && (native != null || poolFacts.State != CorpseFadePoolState.ZeroInstances)
                ? DelayedCallbackRisk() : null;
            if (risk != null)
            {
                _compatSkipped++;
                LogSkip(CorpseFadeSkip.PatchedByThirdParty, CorpseFadeHazard.None, scheduleId, risk);
                return CorpseFadeAttempt.Finished;
            }
            // Disabled, paused and lobby corpses must not arm a cooldown.
            bool gateOpen = fadeEnabled && corpseEffects && inLevel && !paused && !_playPatched && prefabSafe;
            if (poolFacts.State == CorpseFadePoolState.Missing || poolFacts.State == CorpseFadePoolState.ZeroInstances) _backoff.Reset();
            else if (gateOpen) LogBackoff(_backoff.Observe(poolFacts.IsReady, Coordinator.Active, Now()), poolFacts);
            // The pooled path is the ONLY path a starved pool can refuse, so a corpse whose own Decay component can
            // take the dissolve must still be tried: `native == null` is what makes this gate safe.
            if (gateOpen && native == null && !poolFacts.IsReady && poolFacts.State == CorpseFadePoolState.Exhausted
                && !_backoff.MayAttempt(false))
            {
                // 主动退避: we deliberately do not ask. One bounded terminal line per corpse says so; there is no
                // retry ladder, no borrow, no hazard scan and no native call - the corpse keeps the shipped
                // instant hide at its own deadline, and a pool that recovers is used by the next corpse.
                _backedOff++;
                LogSkip(CorpseFadeSkip.PoolStarved, CorpseFadeHazard.None, scheduleId);
                return CorpseFadeAttempt.Finished;
            }
            // 1.2.22: the input is classified once per attempt. A dangerous source is refused by the POLICY
            // gate below (reason=hazardous-input) - never borrowed, never armed, never cloned. The corpse-own
            // component path is untouched: it plays an already-armed component and creates no copy.
            CorpseFadeHazard hazard = CorpseFadePolicy.Hazard(FadeRendererCast.Observe(renderer));
            var plan = new CorpseFadePlan(
                fadeEnabled, corpseEffects, inLevel, paused, _playPatched, prefabSafe, poolFacts.State,
                renderer != null && !ReferenceEquals(renderer, null), offScene, Coordinator.Active, hazard);
            if (TryNative(plan, native, renderer, scheduleId)) { _started++; return CorpseFadeAttempt.Started; }
            if (!CorpseFadePolicy.ShouldAttempt(plan))
            {
                var skip = CorpseFadePolicy.SkipReason(plan);
                if (skip == CorpseFadeSkip.ZeroInstances)
                {
                    if (deferTransient) return WaitAtDeadline();
                    _zeroSkipped++;
                }
                // 1.2.26: "the pool is empty" is only transient while we are still willing to ask again. Once the
                // back-off is open the same fact is a deliberate refusal, so the corpse must not spend a second
                // and a third preparation attempt on it (that is the 4,401-deferred ladder).
                if (skip == CorpseFadeSkip.NoFreeInstance && !_backoff.MayAttempt(poolFacts.IsReady))
                    skip = CorpseFadeSkip.PoolStarved;
                if (skip == CorpseFadeSkip.PoolStarved) _backedOff++;
                if (deferTransient && CorpseFadeRetry.IsTransient(skip)) return Defer(CorpseFadePolicy.Reason(skip));
                LogSkip(skip, hazard, scheduleId);
                return CorpseFadeAttempt.Finished;
            }
            Target.BeginAttempt(scheduleId);
            if (!Coordinator.TryStart(Target, renderer, out CorpseFadeOutcome outcome))
            {
                if (deferTransient && CorpseFadeRetry.IsTransient(outcome)) return Defer(CorpseFadePolicy.Reason(outcome));
                LogFailure(outcome, scheduleId);
                return CorpseFadeAttempt.Finished;
            }
            if (outcome == CorpseFadeOutcome.StartedProbeUnknown)
            {
                LogStartedUnknown(scheduleId, renderer);
                return CorpseFadeAttempt.Finished;
            }
            _started++;
            LogStarted(scheduleId, renderer);
            return CorpseFadeAttempt.Started;
        }
        catch (Exception error) { _failed++; Warn(error); return CorpseFadeAttempt.Finished; }
    }
    private static CorpseFadeAttempt Defer(string reason)
    {
        _deferred++;
        if (Diagnostics.AllowLine("waiting:" + reason))
            Plugin.Instance?.Log.LogInfo("[CORPSE] fade=waiting;reason=" + reason + ";note=bounded-pre-cleanup-retry");
        return CorpseFadeAttempt.Retry;
    }

    // DBO's short native dissolve without replaying Play or driving the death coroutine. The source is proven
    // by the caller and checked again here; the game still owns all callbacks and cleanup of this instance.
    internal static bool TryReusePlaying(Decay decay, Renderer target, int scheduleId)
    {
        if (Plugin.CorpseFade?.Value != true || Plugin.CorpseEffects?.Value == false || Paused()
            || PlayIsPatched() || DelayedCallbackRisk() != null) return false;
        try
        {
            if (decay == null || !decay.isActiveAndEnabled || !decay.m_playing || target == null) return false;
            var bound = decay.m_targetRenderer;
            var copy = decay.m_decayRenderer;
            if (bound == null || bound.Pointer != target.Pointer || copy == null || copy.gameObject == target.gameObject
                || copy.gameObject == decay.gameObject || !copy.enabled || !copy.gameObject.activeInHierarchy
                || copy.forceRenderingOff || !FadeRendererCast.HasLiveCopy(copy)) return false;
            float previous = decay.m_duration;
            if (!CorpseFadePolicy.TryGameDuration(previous, decay.m_time, out float duration)) return false;
            bool retimed = duration < previous;
            if (retimed)
            {
                decay.m_duration = duration;
                _gameRetimed++;
            }
            _gameReused++;
            if (Diagnostics.ClaimTerminal(scheduleId))
                Plugin.Instance?.Log.LogInfo($"[CORPSE] fade=reused;path=game-reuse;scheduleId={scheduleId};retimed={retimed};durationBefore={previous:0.000};duration={duration:0.000};note=no-new-play-or-borrow");
            return true;
        }
        catch (Exception error) { Warn(error); return false; }
    }

    // DeadBodyOptimization's route without its detours: start the dissolve the GAME owns, on the corpse's own
    // death state. No Harmony patch, no ES_* hook, no state-machine rewrite - one bounded reflection call that
    // hands an IEnumerator to the enemy's own StartCoroutine. The game borrows, plays and cleans up; KillFeed
    // only decides that a starved pool must not leave this corpse without a dissolve. Once per scheduleId.
    internal static bool StartGameCoroutine(ES_Dead dead, EnemyAgent enemy, int scheduleId)
    {
        if (Plugin.CorpseFade?.Value != true || Plugin.CorpseEffects?.Value == false || Paused()) return false;
        CheckPlayPatches();
        if (_playPatched || DelayedCallbackRisk() != null) return false;
        try
        {
            // A healthy pool keeps the corpse-own / pooled paths; this route exists for the starved case only.
            if (SamplePool().IsReady) return false;
            if (dead == null || enemy == null) return false;
            var current = dead.m_decay;
            if (current != null && current.m_playing) return false;
            var method = ResolveDecayDelayed();
            if (method == null) { LogFallback("game-coroutine-unavailable", scheduleId); return false; }
            object coroutine = method.Invoke(dead, new object[] { GameCoroutineDelay, CorpseFadePolicy.FadeSeconds });
            if (coroutine is not Il2CppObjectBase boxed || boxed.Pointer == IntPtr.Zero)
            { LogFallback("game-coroutine-unavailable", scheduleId); return false; }
            enemy.StartCoroutine(new Il2CppSystem.Collections.IEnumerator(boxed.Pointer));
        }
        catch (Exception error) { Warn(error); LogFallback("game-coroutine-threw", scheduleId); return false; }
        // Review S3: the coroutine is ALREADY running here. Bookkeeping and the receipt are best effort, so a
        // throwing log line can never make the caller fall through and borrow a second instance.
        try
        {
            _gameCoroutines++;
            if (Diagnostics.ClaimTerminal(scheduleId))
                Plugin.Instance?.Log.LogInfo($"[CORPSE] fade=begun;path=game-coroutine;seconds={CorpseFadePolicy.FadeSeconds:0.00};delay={GameCoroutineDelay:0.00};scheduleId={scheduleId};note=game-owned-decaydelayed-no-patch");
        }
        catch (Exception receipt) { Warn(receipt); }
        return true;
    }
    // Review S5: a private method declared on a base type is not returned by GetMethod on the derived type
    // (FlattenHierarchy only affects statics), so walk the chain instead of accepting a silent null.
    private static MethodInfo ResolveDecayDelayed()
    {
        if (_decayDelayed != null) return _decayDelayed;
        try
        {
            for (Type type = typeof(ES_Dead); type != null && type != typeof(object); type = type.BaseType)
            {
                var found = type.GetMethod("DecayDelayed",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                    null, new[] { typeof(float), typeof(float) }, null);
                if (found != null) { _decayDelayed = found; return found; }
            }
        }
        catch (Exception error) { Warn(error); }
        return null;
    }
    // Corpse-own Decay: play only an already-armed, safe component. This path never borrows or returns.
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
            catch (Exception error) { Warn(error); LogFallback("native-state-unreadable", scheduleId); return false; }
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
            // Do not re-initialize the corpse-own component; the game already owns its target binding.
            if (!FadeRendererCast.HasTarget(native)) { LogFallback("native-not-initialized", scheduleId); return false; }
            // 1.2.22 review: prove what this Play would start. Play does not build a copy - CreateDecayRenderer is
            // reached through InitializeInternal, and the IL gate pins the only two Decay.Initialize calls to the
            // pooled arm - so a corpse-own component with a target but no live copy dissolves nothing, and an
            // existing dead copy must not be started at all.
            Renderer copy;
            try { copy = native.m_decayRenderer; }
            catch (Exception error) { Warn(error); LogFallback("native-clone-unreadable", scheduleId); return false; }
            if (copy == null || copy.Pointer == IntPtr.Zero) { LogFallback("native-no-clone", scheduleId); return false; }
            if (!FadeRendererCast.HasLiveCopy(copy)) { LogFallback("native-bad-clone", scheduleId); return false; }
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
        string facts = CorpseFadePolicy.NativeStateReceipt(path, stopAction, autoCleanup, playing);
        Plugin.Instance?.Log.LogInfo($"[CORPSE] fade=native-unavailable;reason={CorpseFadePolicy.Reason(path)};{facts};scheduleId={scheduleId}");
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
        if (_gameReused + _gameCoroutines > 0) Plugin.Instance?.Log.LogInfo($"[CORPSE] fade=game-reuse-totals;reused={_gameReused};retimed={_gameRetimed};coroutines={_gameCoroutines};note=starts-only-not-completions-or-pool-returns");
        _gameReused = _gameRetimed = _gameCoroutines = 0;
        if (_started + _deferred + _skipped + _failed + _backedOff + _deadlineWaits + _gameReused + _gameCoroutines + Coordinator.UnknownStates > 0)
            Plugin.Instance?.Log.LogInfo($"[CORPSE] fade=totals;started={_started};deferredAttempts={_deferred};skipped={_skipped};failed={_failed};backedOff={_backedOff};zeroAtDeadline={_zeroSkipped};deadlineWaits={_deadlineWaits};compatSkipped={_compatSkipped};compatBeforeScan={_compatBeforeScan};active={Coordinator.Active};idleObserved={Coordinator.GameReleases};playUnconfirmed={Coordinator.UnknownStates};manualReleases={Coordinator.ManualReleases};returnUnconfirmed={Coordinator.ReleaseUnconfirmed};arms={Target.ArmsAttempted};initSkinned={Target.SkinnedCalls};initMesh={Target.MeshCalls};armThrew={Target.ArmsThrew};armIncomplete={Target.ArmsIncomplete};{CorpseFadePoolFacts.DescribeLast(_lastPoolFacts)};note=full-counts-not-log-samples");
        _started = _deferred = _skipped = _failed = 0;
        // 1.2.26: the pool is rebuilt with the level, so the observation of the old one is worthless - and a new
        // level must be allowed to ask again on its very first corpse.
        _backoff.Reset();
        _backedOff = 0;
        _lastPoolFacts = null;
        _compatSkipped = _zeroSkipped = _deadlineWaits = 0;
        _compatBeforeScan = 0;
        _frameRisk = default;
        Target.ResetCounters();
        Coordinator.ResetCounters();
    }

    private static bool Paused()
    {
        try { return Time.timeScale <= 0.0001f || Time.deltaTime <= 0f; }
        catch { return true; }
    }

    private static void CheckPlayPatches()
    {
        if (_patchChecked) return;
        _playPatched = PlayIsPatched();
        _patchChecked = true;
        if (_playPatched && Diagnostics.AllowLine(CorpseFadePolicy.Reason(CorpseFadeSkip.PatchedByThirdParty)))
            Plugin.Instance?.Log.LogInfo("[CORPSE] fade=disabled;reason=" + CorpseFadePolicy.Reason(CorpseFadeSkip.PatchedByThirdParty) + ";detail=Decay.Play has third-party patches");
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

    // Inspect the active postfix, not the installed package name. No patch or dependency is added.
    private static string DelayedCallbackRisk()
    {
        try { return _frameRisk.Get(Time.frameCount, ReadCallbackRisk); }
        catch { return PatchReadFailed; }
    }

    private static string ReadDelayedCallbackRisk()
    {
        try
        {
            var method = _skinnedInitialize ??= typeof(Decay).GetMethod(nameof(Decay.Initialize), new[] { typeof(SkinnedMeshRenderer),
                typeof(Il2CppSystem.Collections.Generic.List<IRF.InstancedRenderFeature>) });
            if (method == null) return PatchReadFailed;
            var patches = Harmony.GetPatchInfo(method);
            if (patches != null) foreach (var patch in patches.Postfixes)
            {
                var callback = patch.PatchMethod;
                if (callback == null) return PatchReadFailed;
                var type = callback.DeclaringType;
                if (CorpseFadePolicy.IsDelayedDecayCallback(type?.Assembly.GetName().Name, type?.FullName, callback.Name))
                    return "external-delayed-decay";
            }
            return null;
        }
        catch { return PatchReadFailed; }
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

    // 1.2.26: monotonic clock for the pooled back-off only. Unscaled on purpose: a paused game still advances it,
    // so a long pause can neither stretch nor freeze the cooldown, and no native state depends on it.
    private static float Now()
    {
        try { return Time.unscaledTime; } catch { return 0f; }
    }

    private static CorpseFadePoolFacts SamplePool()
    {
        CorpseFadePoolFacts facts = PoolFacts();
        _lastPoolFacts = facts;
        return facts;
    }

    // Read-only pool observation: "the game never built the pool", "every instance is in use" and "the pool is at
    // its ceiling and cannot grow" are different diagnoses, and only the second one can change a moment later.
    // Nothing here creates, initialises or grows a pool, so the fallback stays exactly the shipped instant hide.
    private static CorpseFadePoolFacts PoolFacts()
    {
        try
        {
            ObjectPooler.Pool pool = Decay.pool;
            // A pool the game has not built yet. Every number stays "unreadable": a log line must never imply a count
            // of 0 it did not observe.
            if (pool == null)
                return new CorpseFadePoolFacts(CorpseFadePoolState.Missing, -1, -1, -1, -1, false, false);
            int free = pool.freeInstanceCount, used = pool.usedInstanceCount, count = pool.count;
            return new CorpseFadePoolFacts(CorpseFadePoolFacts.Classify(free, used, count),
                free, used, count, pool.m_minCount, pool.dynamic, pool.allowStealing);
        }
        catch { return CorpseFadePoolFacts.Missing; }
    }

    // One line per transition, never per corpse: `state=starved` says we stopped asking (our own choice, with the
    // real pool shape behind it), `recovered`/`cooldown-expired` say the gate is open again. Bounded by the same
    // per-level allowance as every other receipt, so a swarm cannot turn this into a log flood.
    private static void LogBackoff(CorpseFadeBackoffStep step, in CorpseFadePoolFacts facts)
    {
        if (step == CorpseFadeBackoffStep.None) return;
        string state = step == CorpseFadeBackoffStep.Starved ? "starved"
            : step == CorpseFadeBackoffStep.Recovered ? "recovered" : "cooldown-expired";
        if (!Diagnostics.AllowLine("backoff:" + state)) return;
        Plugin.Instance?.Log.LogInfo($"[CORPSE] fade=backoff;state={state};reason={CorpseFadePolicy.Reason(CorpseFadeSkip.PoolStarved)};{facts.Describe()};active={Coordinator.Active};misses={_backoff.Misses};cooldown={CorpseFadeBackoff.CooldownSeconds:0.0};note=pooled-path-not-asked-per-corpse");
    }


    private static void LogSkip(CorpseFadeSkip skip, CorpseFadeHazard hazard, int scheduleId, string specificReason = null)
    {
        _skipped++;
        // Deliberate user intent (feature off, lobby, no renderer, off-scene) stays silent; only gates an
        // operator has to explain print a terminal line (F5: the terminal budget is its own allowance, so a
        // `fade=fallback` receipt can never spend it and the first MaxTerminalPerLevel corpses always print).
        if (!CorpseFadePolicy.IsReportable(skip)) return;
        string reason = specificReason ?? CorpseFadePolicy.Reason(skip);
        if (!Diagnostics.ClaimTerminal(scheduleId)) return;
        // 1.2.22: a refused input names the hazard, so "we never cloned this corpse" is one bounded line an
        // operator can reconcile with the instant hide that followed.
        string detail = hazard == CorpseFadeHazard.None ? string.Empty : ";hazard=" + CorpseFadePolicy.Reason(hazard);
        Plugin.Instance?.Log.LogInfo($"[CORPSE] fade=skipped;reason={reason}{detail};scheduleId={scheduleId};active={Coordinator.Active}");
    }

    private static void LogFailure(CorpseFadeOutcome outcome, int scheduleId)
    {
        if (outcome == CorpseFadeOutcome.PoolUnverifiable) _skipped++;
        else _failed++;
        string reason = CorpseFadePolicy.Reason(outcome);
        // An unverifiable arm never plays. Release is a separate, independently checked operation.
        if (outcome == CorpseFadeOutcome.PoolUnverifiable)
        {
            if (!Diagnostics.ClaimTerminal(scheduleId)) return;
            // 1.2.26: `arm=` names WHICH arm answer produced the fail-closed skip (complete / incomplete-no-copy /
            // target-mismatch / threw). "We handed it back untouched" and "the native arm stopped halfway" used to
            // share one line; the second is the one that leaves a half-initialised instance behind.
            Plugin.Instance?.Log.LogInfo($"[CORPSE] fade=skipped;reason={reason};scheduleId={scheduleId};polls={Coordinator.LastPolls};note=pooled-instance-unverifiable;arm={Target.LastArmDetail};active={Coordinator.Active}");
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
        // 1.2.26: `path=` makes "the native dissolve really started" comparable with the corpse-own
        // `path=native-decay` line and with the deliberate `fade=backoff` receipt - three different facts.
        Plugin.Instance?.Log.LogInfo($"[CORPSE] fade=begun;path=pooled-arm;seconds={CorpseFadePolicy.FadeSeconds:0.00};renderer={FadeRendererCast.Describe(renderer)};concurrent={Coordinator.Active};scheduleId={scheduleId}");
    }

    private static void LogStartedUnknown(int scheduleId, Renderer renderer)
    {
        string reason = CorpseFadePolicy.Reason(CorpseFadeOutcome.StartedProbeUnknown);
        if (!Diagnostics.ClaimTerminal(scheduleId)) return;
        Plugin.Instance?.Log.LogInfo($"[CORPSE] fade=held;path=pooled-arm;reason={reason};renderer={FadeRendererCast.Describe(renderer)};active={Coordinator.Active};scheduleId={scheduleId};note=play-unconfirmed");
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
        // 1.2.26: the borrow records WHICH pool object handed the instance out, so the hand-back can prove it is
        // returning into the same generation (a level change rebuilds the pool). Only one borrow is ever in flight
        // on this thread - the coordinator releases a failed attempt before it borrows again - so one slot suffices.
        private object _borrowedHandle;
        private IntPtr _borrowedPool, _usedNode, _previousCopy;
        private bool _previousKnown;
        private Renderer _source;
        // Attempt identity for the arm diagnostics: the corpse the pooled path is currently working for, and the
        // detailed answer of its last arm (read by CorpseFade.LogFailure, never by the policy).
        private int _scheduleId;
        internal string LastArmDetail = "unset";
        internal int ArmsAttempted, ArmsThrew, ArmsIncomplete, ArmsBound;
        internal int SkinnedCalls, MeshCalls;

        internal void BeginAttempt(int scheduleId)
        {
            _scheduleId = scheduleId;
            _borrowedHandle = null;
            _borrowedPool = _usedNode = _previousCopy = IntPtr.Zero;
            _source = null;
            _previousKnown = false;
            LastArmDetail = "unset";
        }

        internal void ResetCounters()
        {
            SkinnedCalls = MeshCalls = 0;
            ArmsAttempted = ArmsThrew = ArmsIncomplete = ArmsBound = 0;
            _borrowedHandle = null;
            _borrowedPool = _usedNode = _previousCopy = IntPtr.Zero;
            _source = null;
            _previousKnown = false;
            LastArmDetail = "unset";
        }

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
            _borrowedHandle = instance;
            _borrowedPool = pool.Pointer;
            _usedNode = pool.m_usedInstances.Find(instance.gameObject)?.Pointer ?? IntPtr.Zero;
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

        // Only a normal Initialize followed by a complete, independently observed binding may reach Play.
        public CorpseFadeArm Ready(object handle, object renderer)
        {
            if (handle is not Decay decay || decay == null) return CorpseFadeArm.Refused;
            if (renderer is not Renderer target) return CorpseFadeArm.Refused;
            if (!FadeRendererCast.HasDrawableMesh(target)) return CorpseFadeArm.Refused;
            if (!FadeRendererCast.TryResolve(target, out SkinnedMeshRenderer skinned, out MeshRenderer mesh))
                return CorpseFadeArm.Refused;
            bool threw = false, bound = false;
            string overload = skinned != null ? "skinned" : "mesh";
            // These bounded receipts correlate this managed attempt only; a nearby native frame has no caller ID.
            int serial = ++ArmSerial;
            BeginArm(target, overload, serial);
            try
            {
                _source = target;
                Renderer previous = decay.m_decayRenderer;
                _previousCopy = previous == null ? IntPtr.Zero : previous.Pointer;
                _previousKnown = true;
                if (!OwnsIdleBorrow(handle)) { EndArm(target, overload, serial, false, false, false, false, false, false); return CorpseFadeArm.Refused; }
                var irfs = new Il2CppSystem.Collections.Generic.List<IRF.InstancedRenderFeature>();
                if (skinned != null) { SkinnedCalls++; decay.Initialize(skinned, irfs); }
                else { MeshCalls++; decay.Initialize(mesh, irfs); }
                Renderer boundTarget = decay.m_targetRenderer;
                bool targetBound = boundTarget != null && boundTarget.Pointer == target.Pointer;
                // A bound target alone survives partial initialization. Require a distinct live mesh copy.
                Renderer copy = decay.m_decayRenderer;
                bool copyPresent = copy != null && copy.Pointer != IntPtr.Zero;
                bool copyLive = copyPresent && FadeRendererCast.HasLiveCopy(copy);
                bool playing = decay.m_playing;
                bound = CorpseFadePolicy.ArmComplete(targetBound, copyLive && copy.gameObject != target.gameObject
                    && copy.gameObject != decay.gameObject, copyPresent ? copy.Pointer.ToInt64() : 0L,
                    target.Pointer.ToInt64(), _previousCopy.ToInt64(), playing);
                EndArm(target, overload, serial, false, targetBound, copyPresent, copyLive, playing, bound);
            }
            catch (Exception error)
            {
                threw = true;
                Warn(error);
                EndArm(target, overload, serial, true, false, false, false, false, false);
            }
            return CorpseFadePolicy.ArmVerdict(threw, bound);
        }

        // Bounded, attributable arm receipts. `fade=arm;phase=begin` prints for the first few arms of a level (an
        // operator needs the shape once), `phase=end` prints for those and for EVERY anomalous arm - the real counts
        // travel in the level totals, so a swarm cannot flood the log.
        private static int ArmSerial;

        private void BeginArm(Renderer target, string overload, int serial)
        {
            if (!Diagnostics.AllowLine("arm-begin")) return;
            Plugin.Instance?.Log.LogInfo($"[CORPSE] fade=arm;phase=begin;arm={serial};scheduleId={_scheduleId};overload={overload};renderer={Pointer(target)};{PoolFacts().Describe()};note=attempt-context-not-native-caller-proof");
        }

        private void EndArm(Renderer target, string overload, int serial, bool threw,
            bool targetBound, bool copyPresent, bool copyLive, bool playing, bool bound)
        {
            ArmsAttempted++;
            if (threw) ArmsThrew++;
            if (!threw && !bound) ArmsIncomplete++;
            if (bound) ArmsBound++;
            LastArmDetail = threw ? "exception" : !targetBound ? "target-mismatch"
                : !copyPresent ? "incomplete-no-copy" : !copyLive ? "incomplete-no-mesh"
                : playing ? "still-playing" : !bound ? "copy-not-distinct" : "complete";
            string note = LastArmDetail;
            if (!Diagnostics.AllowLine(bound ? "arm-end" : "arm-anomaly")) return;
            Plugin.Instance?.Log.LogInfo($"[CORPSE] fade=arm;phase=end;arm={serial};scheduleId={_scheduleId};overload={overload};renderer={Pointer(target)};threw={threw};targetBound={targetBound};copy={(copyPresent ? "present" : "missing")};copyLive={copyLive};playing={playing};bound={bound};{PoolFacts().Describe()};arms={ArmsAttempted};incomplete={ArmsIncomplete};note={note}");
        }

        private static string Pointer(Renderer renderer)
        {
            try { return renderer == null ? "none" : "0x" + renderer.Pointer.ToInt64().ToString("X"); }
            catch (Exception) { return "unreadable"; }
        }

        public void Play(object handle, float seconds)
        {
            if (handle is Decay decay) decay.Play(seconds);
        }
        public bool OwnsIdleBorrow(object handle)
        {
            if (handle is not Decay decay || decay == null || !ReferenceEquals(handle, _borrowedHandle)) return false;
            var pool = Decay.pool;
            var go = decay.gameObject;
            if (pool == null || pool.Pointer != _borrowedPool || go == null || _usedNode == IntPtr.Zero) return false;
            var used = pool.m_usedInstances.Find(go);
            return CorpseFadePolicy.MayReturn(true, true, Probe(handle), used != null && used.Pointer == _usedNode,
                pool.m_freeInstances.Contains(go));
        }

        public bool Cleanup(object handle)
        {
            var decay = (Decay)handle;
            Renderer copy = decay.m_decayRenderer;
            if (copy != null)
            {
                Renderer bound = decay.m_targetRenderer;
                if (_source == null || bound == null || bound.Pointer != _source.Pointer || !_previousKnown
                    || copy.Pointer == _previousCopy || copy.Pointer == _source.Pointer
                    || copy.gameObject == _source.gameObject || copy.gameObject == decay.gameObject) return false;
            }
            decay.Cleanup();
            // Do not repool a partially cleaned instance if interop returned without a managed exception.
            return decay.m_decayRenderer == null;
        }

        public bool Return(object handle)
        {
            var decay = (Decay)handle;
            var pool = Decay.pool;
            var go = decay.gameObject;
            _borrowedHandle = null; // consume before the native call, including its exception path
            pool.Return(go);
            var current = Decay.pool;
            return current != null && current.Pointer == pool.Pointer && !pool.m_usedInstances.Contains(go)
                && pool.m_freeInstances.Contains(go);
        }
    }
}
