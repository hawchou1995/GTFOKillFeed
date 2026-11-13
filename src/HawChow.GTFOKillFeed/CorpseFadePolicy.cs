using System;
using System.Collections.Generic;

namespace HawChow.GTFOKillFeed;

// Short native "dissolve" for a corpse that this plugin already finished handling. It is a purely
// visual extra: it starts at the existing EndAt stage (after the launch/dismemberment window) on one
// already-visible renderer, and never changes the Hidden/Restore/launch/dismemberment/dedupe order.
//
// Everything below is inert unless every conservation check passes (see CorpseFade.cs for the real
// Unity/IL2CPP side): no third-party patch on Decay.Play, prefab stopAction==Deactivate with
// autoCleanup==true, a live pool with a free instance, not paused, and a global concurrency cap.

// Read-only observation of the native pool. It is a *state*, not a boolean, because "the game never
// created the pool yet" (ES_Dead.Setup not dispatched, or a level with only flyer/squid-boss deaths)
// and "every instance is currently in use" are different diagnoses and need different log lines.
internal enum CorpseFadePoolState
{
    Missing,     // the native pool object is null: nothing may be borrowed, and it is never created here
    Exhausted,   // the pool exists but freeInstanceCount == 0
    Ready        // at least one free instance: a borrow is legal
}

internal readonly struct CorpseFadePlan
{
    internal readonly bool FadeEnabled;
    internal readonly bool CorpseEffects;
    internal readonly bool InLevel;
    internal readonly bool Paused;
    internal readonly bool PlayPatched;
    internal readonly bool PrefabSafe;
    internal readonly CorpseFadePoolState Pool;
    internal readonly bool HaveRenderer;
    internal readonly bool OffScene;
    internal readonly int ActiveFades;

    internal CorpseFadePlan(bool fadeEnabled, bool corpseEffects, bool inLevel, bool paused, bool playPatched,
        bool prefabSafe, CorpseFadePoolState pool, bool haveRenderer, bool offScene, int activeFades)
    {
        FadeEnabled = fadeEnabled;
        CorpseEffects = corpseEffects;
        InLevel = inLevel;
        Paused = paused;
        PlayPatched = playPatched;
        PrefabSafe = prefabSafe;
        Pool = pool;
        HaveRenderer = haveRenderer;
        OffScene = offScene;
        ActiveFades = activeFades;
    }
}

// The single gate that refused the dissolve. Every value is an observed fact of the current frame, so a
// log line can name the exact blocker instead of the previous single merged catch-all reason.
internal enum CorpseFadeSkip
{
    None,
    Disabled,
    CorpseEffectsOff,
    OffLevel,
    Paused,
    PatchedByThirdParty,
    PrefabUnsafe,
    PoolMissing,
    NoFreeInstance,
    NoRenderer,
    OffScene,
    ConcurrencyCap
}

// 1.2.16 (second real-machine pass): the corpse's OWN native Decay component path.
// The reference mod (zose DeadBodyOptimization) never borrows a pooled instance and never validates a
// renderer target: it reflects the game's own death-state dissolve, which is why it also works for corpses
// that never got a ragdoll. We do not patch any ES_* type (those detours are known to trip a native
// AccessViolation at load), so the same effect is reached by driving the `Decay` component the enemy prefab
// already carries - verified read-only against the Test interop:
//   CharacterDestruction.Decay: Initialize(SkinnedMeshRenderer|MeshRenderer, List<IRF.InstancedRenderFeature>),
//   Play(float), Play(), Cleanup(), m_playing, m_targetRenderer, m_stopAction, m_autoCleanup.
// Every refusal is a named state, exactly like CorpseFadeSkip, so a host log can say WHY the native path was
// not used instead of falling back silently.
internal enum CorpseFadeNativePath
{
    Started,
    Disabled,
    CorpseEffectsOff,
    OffLevel,
    Paused,
    OffScene,
    NoRenderer,
    NoComponent,
    UnsafeComponent,
    AlreadyPlaying,
    PatchedByThirdParty
}
internal static class CorpseFadePolicy
{
    internal const float FadeSeconds = 0.5f;
    internal const int MaxConcurrentFades = 2;
    internal const int StopActionDeactivate = 1;

    // A pool that answered "no free instance" is re-asked a small, fixed number of times inside the very
    // same frame. A poll only reads the pool and calls GetInstance(): nothing is created, cloned,
    // initialised or destroyed, so a poll cannot leak an instance, cannot bring a hidden corpse back and
    // can never hold a slot. Expensive failures (Initialize/Play) are never retried: they are
    // deterministic within one frame and a retry would burn a second model clone for nothing.
    internal const int MaxBorrowPolls = 3;

    // "Should we even try?" - all inputs are observable facts, never guesses. ShouldAttempt is exactly
    // "no gate fired"; SkipReason tells the caller which one did.
    internal static CorpseFadeSkip SkipReason(in CorpseFadePlan plan)
    {
        if (!plan.FadeEnabled) return CorpseFadeSkip.Disabled;
        if (!plan.CorpseEffects) return CorpseFadeSkip.CorpseEffectsOff;
        if (!plan.InLevel) return CorpseFadeSkip.OffLevel;
        if (plan.Paused) return CorpseFadeSkip.Paused;              // a paused game would hold a pooled instance indefinitely
        if (plan.PlayPatched) return CorpseFadeSkip.PatchedByThirdParty; // another mod could swallow Play -> instance would leak
        if (!plan.PrefabSafe) return CorpseFadeSkip.PrefabUnsafe;   // stopAction must return the instance, autoCleanup must free the clone
        if (plan.Pool == CorpseFadePoolState.Missing) return CorpseFadeSkip.PoolMissing;
        if (plan.Pool != CorpseFadePoolState.Ready) return CorpseFadeSkip.NoFreeInstance;
        if (!plan.HaveRenderer) return CorpseFadeSkip.NoRenderer;    // not every enemy model exposes a usable renderer
        if (plan.OffScene) return CorpseFadeSkip.OffScene;           // already recycled off-scene: nothing visible to dissolve
        if (plan.ActiveFades >= MaxConcurrentFades) return CorpseFadeSkip.ConcurrencyCap;
        return CorpseFadeSkip.None;
    }

    internal static bool ShouldAttempt(in CorpseFadePlan plan) => SkipReason(plan) == CorpseFadeSkip.None;

    // Only environment surprises are worth a log line. "The user turned the feature off", "we are in the
    // lobby" or "this model has no renderer" are normal and stay silent, so the bounded budget is spent
    // on the cases an operator actually has to explain.
    internal static bool IsReportable(CorpseFadeSkip skip) => skip switch
    {
        CorpseFadeSkip.Paused => true,
        CorpseFadeSkip.PatchedByThirdParty => true,
        CorpseFadeSkip.PrefabUnsafe => true,
        CorpseFadeSkip.PoolMissing => true,
        CorpseFadeSkip.NoFreeInstance => true,
        CorpseFadeSkip.ConcurrencyCap => true,
        _ => false
    };

    internal static string Reason(CorpseFadeSkip skip) => skip switch
    {
        CorpseFadeSkip.Disabled => "disabled",
        CorpseFadeSkip.CorpseEffectsOff => "corpse-effects-off",
        CorpseFadeSkip.OffLevel => "off-level",
        CorpseFadeSkip.Paused => "paused",
        CorpseFadeSkip.PatchedByThirdParty => "patched-by-third-party",
        CorpseFadeSkip.PrefabUnsafe => "prefab-unsafe",
        CorpseFadeSkip.PoolMissing => "pool-missing",
        CorpseFadeSkip.NoFreeInstance => "no-free-instance",
        CorpseFadeSkip.NoRenderer => "no-renderer",
        CorpseFadeSkip.OffScene => "off-scene",
        CorpseFadeSkip.ConcurrencyCap => "concurrency-cap",
        _ => "none"
    };

    internal static string Reason(CorpseFadeOutcome outcome) => outcome switch
    {
        CorpseFadeOutcome.Started => "started",
        CorpseFadeOutcome.StartedProbeUnknown => "probe-unknown",
        CorpseFadeOutcome.TargetMissing => "target-missing",
        CorpseFadeOutcome.ConcurrencyCap => "concurrency-cap",
        CorpseFadeOutcome.InstanceBusy => "instance-busy",
        CorpseFadeOutcome.InstanceTracked => "instance-tracked",
        CorpseFadeOutcome.InstanceGone => "instance-gone",
        CorpseFadeOutcome.PoolMissing => "pool-missing",
        CorpseFadeOutcome.NoFreeInstance => "no-free-instance",
        CorpseFadeOutcome.BorrowError => "borrow-error",
        CorpseFadeOutcome.ProbeUnknown => "probe-unknown",
        CorpseFadeOutcome.PoolUnverifiable => "pool-unverifiable",
        CorpseFadeOutcome.PlayThrew => "play-threw",
        CorpseFadeOutcome.PlayNotObserved => "play-not-observed",
        _ => "unexpected"
    };

    // A dissolve that the game is now animating (or whose state cannot be read, which keeps the slot).
    internal static bool IsStarted(CorpseFadeOutcome outcome)
        => outcome == CorpseFadeOutcome.Started || outcome == CorpseFadeOutcome.StartedProbeUnknown;

    // ---- the corpse's own Decay component (1.2.16, second real-machine pass) --------------------------
    // StopAction is the native enum { None = 0, Deactivate = 1, Destroy = 2 }. The pooled effect prefab
    // returns itself with Deactivate; the component that lives ON the enemy must never do that, because
    // "its own GameObject" is the corpse (and, while the model is still alive, the enemy). So only None is
    // accepted: a Deactivate/Destroy stop action is an immediate, named refusal - never a silent fallback.
    internal const int StopActionNone = 0;
    internal const int StopActionDestroy = 2;
    internal static bool NativeStopActionSafe(int stopAction) => stopAction == StopActionNone;
    // Same idea as SkipReason: the first gate that fires names the refusal. Order matters - the user's own
    // switches first, then the frame state, then the component's own facts.
    internal static CorpseFadeNativePath NativePath(in CorpseFadePlan plan, bool haveComponent, int stopAction, bool autoCleanup, bool playing)
    {
        if (!plan.FadeEnabled) return CorpseFadeNativePath.Disabled;
        if (!plan.CorpseEffects) return CorpseFadeNativePath.CorpseEffectsOff;
        if (!plan.InLevel) return CorpseFadeNativePath.OffLevel;
        if (plan.Paused) return CorpseFadeNativePath.Paused;
        if (!plan.HaveRenderer) return CorpseFadeNativePath.NoRenderer;
        if (plan.OffScene) return CorpseFadeNativePath.OffScene;
        if (plan.PlayPatched) return CorpseFadeNativePath.PatchedByThirdParty;
        if (!haveComponent) return CorpseFadeNativePath.NoComponent;
        if (!NativeStopActionSafe(stopAction) || !autoCleanup) return CorpseFadeNativePath.UnsafeComponent;
        if (playing) return CorpseFadeNativePath.AlreadyPlaying;
        return CorpseFadeNativePath.Started;
    }
    internal static bool ShouldAttemptNative(CorpseFadeNativePath path) => path == CorpseFadeNativePath.Started;
    // "No component on this model" and "already playing" are the two facts an operator needs once, because
    // they explain why a corpse fell back to the pooled path. The user's own switches stay silent.
    internal static bool IsReportableNative(CorpseFadeNativePath path) => path switch
    {
        CorpseFadeNativePath.NoComponent => true,
        CorpseFadeNativePath.UnsafeComponent => true,
        CorpseFadeNativePath.AlreadyPlaying => true,
        _ => false
    };
    // R6: the receipt for a refused native dissolve depends on the NAMED refusal only - never on having read a
    // component. 1.2.16 wrapped the log in `native != null`, which made `reason=no-decay-component` unreachable
    // while this predicate already said it was reportable.
    internal static bool ShouldLogNativeRefusal(CorpseFadeNativePath path) => IsReportableNative(path);
    internal static string Reason(CorpseFadeNativePath path) => path switch
    {
        CorpseFadeNativePath.Started => "started",
        CorpseFadeNativePath.Disabled => "disabled",
        CorpseFadeNativePath.CorpseEffectsOff => "corpse-effects-off",
        CorpseFadeNativePath.OffLevel => "off-level",
        CorpseFadeNativePath.Paused => "paused",
        CorpseFadeNativePath.OffScene => "off-scene",
        CorpseFadeNativePath.NoRenderer => "no-renderer",
        CorpseFadeNativePath.NoComponent => "no-decay-component",
        CorpseFadeNativePath.UnsafeComponent => "unsafe-decay-component",
        CorpseFadeNativePath.AlreadyPlaying => "decay-already-playing",
        CorpseFadeNativePath.PatchedByThirdParty => "patched-by-third-party",
        _ => "unexpected"
    };
    internal static bool PrefabIsSafe(int stopAction, bool autoCleanup)
        => stopAction == StopActionDeactivate && autoCleanup;
}

// Observed state of one borrowed native instance. Ownership is decided from this alone, never from
// elapsed time: a dissolve the game still reports as Playing keeps its slot even after a long pause or
// a frame that took seconds, so the real concurrency cap can never be exceeded.
internal enum CorpseFadeState
{
    Playing,   // the native instance is animating: the game's Update() will return it
    Idle,      // not animating (Play was swallowed or the game already finished and returned it)
    Gone,      // the native object was destroyed (level unload): nothing left to track
    Unknown,   // the probe failed: ownership unproven, so fail closed and keep the slot booked
}

// What the pool answered when the adapter was asked for an instance. Splitting this out of a bool is
// what makes "pool-missing" and "no-free-instance" distinguishable in the log.
internal enum CorpseFadeBorrow
{
    Ok,
    PoolMissing,
    NoFreeInstance,
    Error
}

// Result of one borrow attempt, kept fine-grained on purpose: the shipped 1.2.9 log collapsed
// "borrow failed", "Initialize failed", "Play swallowed" and "probe not idle" into one reason, which
// made a 4/4-skipped session impossible to diagnose.
internal enum CorpseFadeOutcome
{
    Started,
    StartedProbeUnknown,
    TargetMissing,
    ConcurrencyCap,
    InstanceBusy,
    InstanceTracked,
    InstanceGone,
    PoolMissing,
    NoFreeInstance,
    BorrowError,
    ProbeUnknown,
    // 1.2.19: the pooled path no longer initialises anything, so the two initialize-* outcomes collapse into one
    // honest state: the borrowed instance was never armed by the game and is handed straight back.
    // 1.2.19 F2: the pooled path is fail-closed - a borrowed instance can never prove it belongs to this corpse,
    // so it is borrowed, probed and handed back, never played.
    PoolUnverifiable,
    PlayThrew,
    PlayNotObserved,
    Unexpected
}

// Thin, Unity-free surface over the native API so the borrow/hand-back decisions can be proven by the
// game-independent smoke tests.
internal interface ICorpseFadeTarget
{
    CorpseFadeBorrow TryBorrow(out object handle);
    // 1.2.19: readiness only - the adapter never initialises a native Decay (that is what threw the native
    // NullReferenceException on real machines). A borrowed instance is usable only when the game armed it.
    bool Ready(object handle);
    void Play(object handle, float seconds);
    CorpseFadeState Probe(object handle);
    void Cleanup(object handle);
    void Return(object handle);
}

// Exception barrier for the optional dissolve at the existing EndAt stage. The corpse's own cleanup
// (ReleaseGibs/Hide/RecyclePhysics/queue removal) must run exactly as before, even when a native
// renderer accessor or the borrow itself throws, so nothing is allowed to escape from here.
internal static class CorpseFadePrep
{
    internal static bool Attempt(Func<object> selectRenderer, Func<object, bool> begin, Action<Exception> onError)
    {
        object renderer;
        try
        {
            renderer = selectRenderer == null ? null : selectRenderer();
        }
        catch (Exception error)
        {
            Report(onError, error);
            return false;
        }
        if (renderer == null) return false;
        try
        {
            return begin != null && begin(renderer);
        }
        catch (Exception error)
        {
            Report(onError, error);
            return false;
        }
    }

    private static void Report(Action<Exception> onError, Exception error)
    {
        if (onError == null) return;
        try { onError(error); }
        catch (Exception) { /* a broken logger must not break the corpse cleanup either */ }
    }
}

// Bounded diagnostics for the dissolve. Two independent limits keep a 30-kill fight from flooding the
// log: a hard per-level line/warning budget (the shipped WarnBudget idea, kept) and a per-reason cap so
// a single broken native state cannot spend the whole budget on one line. Everything resets with the
// level, so every level gets a fresh - still bounded - allowance.
internal sealed class CorpseFadeDiagnostics
{
    internal const int MaxLinesPerLevel = 24;
    internal const int MaxWarningsPerLevel = 4;
    internal const int MaxPerReason = 3;

    private readonly Dictionary<string, int> _reasons = new();
    private readonly HashSet<int> _terminalIds = new();
    private int _lines;
    private int _terminal;
    private int _warnings;

    internal int Lines => _lines;
    internal int TerminalLines => _terminal;
    internal int Warnings => _warnings;
    internal int Count(string reason)
        => reason != null && _reasons.TryGetValue(reason, out int used) ? used : 0;

    internal bool AllowLine(string reason)
    {
        if (_lines >= MaxLinesPerLevel) return false;
        string key = string.IsNullOrEmpty(reason) ? "unknown" : reason;
        int used = Count(key);
        if (used >= MaxPerReason) return false;
        _reasons[key] = used + 1;
        _lines++;
        return true;
    }

    // F5 (1.2.19 review): the terminal `fade=` line of ONE corpse is a different budget than the bounded receipts.
    // A `fade=fallback` receipt (the corpse's own Decay could not dissolve it) must never spend the terminal
    // allowance, and the per-reason cap must never swallow the terminal line of a corpse: the first
    // MaxTerminalPerLevel corpses of a level always print theirs, and one scheduleId can claim exactly one (a
    // second attempt for the same corpse is refused, so "one terminal line per corpse" is structural, not luck).
    internal const int MaxTerminalPerLevel = 8;

    // 1.2.19 fix: a refusal because the LEVEL allowance is spent is counted, so the level boundary can report how
    // many corpses got no terminal `fade=` line at all. Without it the Survival log's "8 skipped" looked like 8
    // corpses while 13 more shared the same silence (the cap is exactly 8), and "dissolve never happened" could not
    // be told apart from "the log stopped recording it".
    private int _terminalSuppressed;
    internal int TerminalSuppressed => _terminalSuppressed;

    internal bool ClaimTerminal(int scheduleId)
    {
        if (scheduleId != 0 && _terminalIds.Contains(scheduleId)) return false;   // this corpse already has its line
        if (_terminal >= MaxTerminalPerLevel) { _terminalSuppressed++; return false; }
        if (scheduleId != 0) _terminalIds.Add(scheduleId);
        _terminal++;
        return true;
    }

    internal bool AllowWarning()
    {
        if (_warnings >= MaxWarningsPerLevel) return false;
        _warnings++;
        return true;
    }

    internal void Reset()
    {
        _reasons.Clear();
        _terminalIds.Clear();
        _lines = 0;
        _terminal = 0;
        _terminalSuppressed = 0;
        _warnings = 0;
    }
}

// Borrow/hand-back state machine. The native Decay instance is only ever borrowed from the game pool
// and handed back exactly once:
//   * Play started and m_playing is true  -> the game owns it; Update() returns it (stopAction=Deactivate).
//   * Play did not start / threw          -> we own it, so we Cleanup() the clone and pool.Return() once.
//   * the state cannot be read            -> ownership is unproven: keep the slot and touch nothing.
// There is no Object.Destroy call anywhere, so the shared Decay sound player is never recycled.
internal sealed class CorpseFadeCoordinator
{
    private sealed class Entry
    {
        internal object Handle;
    }

    private readonly List<Entry> _entries = new();

    internal int Active => _entries.Count;
    internal int ManualReleases { get; private set; }
    internal int GameReleases { get; private set; }
    internal int DestroyedReleases { get; private set; }
    internal int Skips { get; private set; }
    internal int Failures { get; private set; }
    internal int Recovered { get; private set; }
    internal int UnknownStates { get; private set; }
    internal int BorrowPolls { get; private set; }
    internal int BorrowMisses { get; private set; }
    internal int LastPolls { get; private set; }
    internal CorpseFadeOutcome LastOutcome { get; private set; }
    internal Exception LastError { get; private set; }

    internal bool TryStart(ICorpseFadeTarget target, object renderer)
        => TryStart(target, renderer, out _);

    internal bool TryStart(ICorpseFadeTarget target, object renderer, out CorpseFadeOutcome outcome)
    {
        outcome = CorpseFadeOutcome.TargetMissing;
        LastPolls = 0;
        LastError = null;
        if (target == null)
        {
            Skips++;
            LastOutcome = outcome;
            return false;
        }
        if (_entries.Count >= CorpseFadePolicy.MaxConcurrentFades)
        {
            Skips++;
            outcome = CorpseFadeOutcome.ConcurrencyCap;
            LastOutcome = outcome;
            return false;
        }
        for (int poll = 1; ; poll++)
        {
            LastPolls = poll;
            if (TryOnce(target, renderer, out outcome))
            {
                LastOutcome = outcome;
                return true;
            }
            LastOutcome = outcome;
            // Only an empty pool is polled again: nothing was borrowed, so the retry can neither
            // double-play nor double-return nor hold an extra instance.
            if (outcome != CorpseFadeOutcome.NoFreeInstance || poll >= CorpseFadePolicy.MaxBorrowPolls) return false;
        }
    }

    private bool TryOnce(ICorpseFadeTarget target, object renderer, out CorpseFadeOutcome outcome)
    {
        outcome = CorpseFadeOutcome.Unexpected;
        object handle = null;
        try
        {
            BorrowPolls++;
            CorpseFadeBorrow borrow;
            try { borrow = target.TryBorrow(out handle); }
            catch (Exception error)
            {
                LastError = error;
                Skips++;
                outcome = CorpseFadeOutcome.BorrowError;
                return false;
            }
            if (borrow == CorpseFadeBorrow.PoolMissing)
            {
                Skips++;
                outcome = CorpseFadeOutcome.PoolMissing;
                return false;
            }
            if (borrow == CorpseFadeBorrow.NoFreeInstance || handle == null)
            {
                Skips++;
                BorrowMisses++;
                outcome = CorpseFadeOutcome.NoFreeInstance;
                return false;
            }
            if (borrow != CorpseFadeBorrow.Ok)
            {
                Skips++;
                outcome = CorpseFadeOutcome.BorrowError;
                return false;
            }
            // Only a pooled instance the game reports as idle is ours to (re)use: a playing one belongs
            // to somebody else, a destroyed one is a broken pool, and an unreadable one is unprovable.
            // None of these is ours, so none of them is ever released here.
            if (Contains(handle))
            {
                Skips++;
                outcome = CorpseFadeOutcome.InstanceTracked;
                return false;
            }
            CorpseFadeState before = Probe(target, handle);
            if (before == CorpseFadeState.Unknown)
            {
                Skips++;
                outcome = CorpseFadeOutcome.ProbeUnknown;
                return false;
            }
            if (before == CorpseFadeState.Gone)
            {
                Skips++;
                outcome = CorpseFadeOutcome.InstanceGone;
                return false;
            }
            if (before != CorpseFadeState.Idle)
            {
                Skips++;
                outcome = CorpseFadeOutcome.InstanceBusy;
                return false;
            }
            // 1.2.19 (F2 review): FAIL-CLOSED. A borrowed instance can never prove it belongs to THIS corpse (its
            // m_targetRenderer still points at the previous corpse's cloned renderer) and the only way to bind it,
            // Decay.Initialize, is forbidden (native NRE, gated to 0 calls). So the pool is borrowed, probed and
            // handed back - never played - and the refusal is named. The Play call below therefore stays unreachable
            // in production; the two gates on DecayFadeTarget.Ready keep it that way.
            bool ready;
            try { ready = target.Ready(handle); }
            catch (Exception error)
            {
                // Reading the readiness fact threw: fail closed, hand the instance back and name the state.
                LastError = error;
                Skips++;
                ReleaseManual(target, handle);
                outcome = CorpseFadeOutcome.PoolUnverifiable;
                return false;
            }
            if (!ready)
            {
                Skips++;
                ReleaseManual(target, handle);
                outcome = CorpseFadeOutcome.PoolUnverifiable;
                return false;
            }
            // Play may throw or be swallowed by a third-party patch; the ONLY ownership proof is the
            // native m_playing flag afterwards (the native Play writes it synchronously, see
            // kf128-ro-investigation/native/disasm-decay-esdead.txt 0x162ff47):
            //   m_playing == true  -> the game's Update() returns it (stopAction=Deactivate); never Return.
            //   m_playing == false -> nobody else will return it, so Cleanup+Return exactly once here.
            bool playThrew = false;
            try { target.Play(handle, CorpseFadePolicy.FadeSeconds); }
            catch (Exception error) { playThrew = true; LastError = error; }
            CorpseFadeState after = Probe(target, handle);
            if (after == CorpseFadeState.Playing)
            {
                _entries.Add(new Entry { Handle = handle });
                outcome = CorpseFadeOutcome.Started;
                return true;
            }
            if (after == CorpseFadeState.Unknown)
            {
                // Unknown keeps the slot booked: a double return would corrupt the shared native pool
                // for every later decay, while a lost slot only costs a dissolve.
                UnknownStates++;
                _entries.Add(new Entry { Handle = handle });
                outcome = CorpseFadeOutcome.StartedProbeUnknown;
                return true;
            }
            Failures++;
            ReleaseManual(target, handle);
            outcome = playThrew ? CorpseFadeOutcome.PlayThrew : CorpseFadeOutcome.PlayNotObserved;
            return false;
        }
        catch (Exception error)
        {
            LastError = error;
            Failures++;
            ReleaseManual(target, handle);
            outcome = CorpseFadeOutcome.Unexpected;
            return false;
        }
    }

    private bool Contains(object handle)
    {
        for (int i = 0; i < _entries.Count; i++)
            if (ReferenceEquals(_entries[i].Handle, handle)) return true;
        return false;
    }

    private static CorpseFadeState Probe(ICorpseFadeTarget target, object handle)
    {
        if (target == null || handle == null) return CorpseFadeState.Gone;
        try { return target.Probe(handle); }
        catch (Exception) { return CorpseFadeState.Unknown; }
    }

    private void ReleaseManual(ICorpseFadeTarget target, object handle)
    {
        try { target.Cleanup(handle); } catch (Exception) { }
        try { target.Return(handle); } catch (Exception) { }
        ManualReleases++;
        Recovered++;
    }

    // Called once per tick. Drops bookkeeping only, based on the real native state:
    //   Playing -> keep the slot (never timed out, never returned here);
    //   Idle    -> the game already returned the instance, so forget it;
    //   Gone    -> the object died with the level, so forget it;
    //   Unknown -> fail closed: keep the slot, because ownership cannot be proven.
    // Returns the number of entries held only because their state is unknown (for bounded logging).
    internal int Sweep(ICorpseFadeTarget target)
    {
        return Prune(target);
    }

    // Level change / plugin unload: drop dead bookkeeping, never touch native objects. Instances the
    // game still animates (or whose state is unknown) stay tracked, so a re-entered level can neither
    // double-borrow them nor exceed the concurrency cap.
    internal int Reset(ICorpseFadeTarget target)
    {
        return Prune(target);
    }

    private int Prune(ICorpseFadeTarget target)
    {
        if (_entries.Count == 0) return 0;
        int unknown = 0;
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            CorpseFadeState state = Probe(target, _entries[i].Handle);
            if (state == CorpseFadeState.Playing) continue;
            if (state == CorpseFadeState.Unknown) { unknown++; continue; }
            if (state == CorpseFadeState.Idle) GameReleases++;
            else DestroyedReleases++;
            _entries.RemoveAt(i);
        }
        return unknown;
    }

    internal void ResetCounters()
    {
        ManualReleases = 0;
        GameReleases = 0;
        DestroyedReleases = 0;
        Skips = 0;
        Failures = 0;
        Recovered = 0;
        UnknownStates = 0;
        BorrowPolls = 0;
        BorrowMisses = 0;
        LastPolls = 0;
        LastOutcome = CorpseFadeOutcome.Started;
        LastError = null;
    }
}
