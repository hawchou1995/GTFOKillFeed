using System;
using System.Collections.Generic;
using HawChow.GTFOKillFeed;

// Proves the optional native corpse dissolve without Unity: CorpseFadePolicy decides whether a native
// Decay instance may be borrowed at all, CorpseFadePrep is the exception barrier the corpse cleanup
// stage runs through, and CorpseFadeCoordinator is the only place that borrows and hands it back.
// Every refusal path must be silent and side-effect free, the hand-back must happen exactly once - and
// never through Object.Destroy, because the native pool owns the game object. Slots are held from the
// real native state (m_playing), never from elapsed time, so a long pause cannot exceed the cap.
// The diagnostics are asserted too: a real host log collapsed every failure into one reason, so each
// gate/step now has its own reason string and a bounded, per-level-reset budget.
internal static class CorpseFadeTests
{
    internal static void Run(Action<bool, string> check)
    {
        // ---- refusal policy: one veto is enough, always silent ------------------------------
        check(CorpseFadePolicy.ShouldAttempt(Plan()), "fade policy: a healthy corpse/renderer/pool pair may attempt a dissolve");
        check(!CorpseFadePolicy.ShouldAttempt(Plan(fadeEnabled: false)), "fade policy: Corpse/FadeEffect=false never touches the native pool");
        check(!CorpseFadePolicy.ShouldAttempt(Plan(corpseEffects: false)), "fade policy: the corpse-effect master switch still gates the dissolve");
        check(!CorpseFadePolicy.ShouldAttempt(Plan(inLevel: false)), "fade policy: lobby/off-level corpses never dissolve");
        check(!CorpseFadePolicy.ShouldAttempt(Plan(paused: true)), "fade policy: a paused game never holds a pooled instance");
        check(!CorpseFadePolicy.ShouldAttempt(Plan(playPatched: true)), "fade policy: any third-party patch on Decay.Play (e.g. DBO) refuses the borrow");
        check(!CorpseFadePolicy.ShouldAttempt(Plan(prefabSafe: false)), "fade policy: a prefab that does not self-return/self-clean refuses the borrow");
        check(!CorpseFadePolicy.ShouldAttempt(Plan(pool: CorpseFadePoolState.Missing)), "fade policy: a missing native pool is never created or borrowed");
        check(!CorpseFadePolicy.ShouldAttempt(Plan(pool: CorpseFadePoolState.Exhausted)), "fade policy: an exhausted native pool is never borrowed from");
        check(!CorpseFadePolicy.ShouldAttempt(Plan(haveRenderer: false)), "fade policy: an enemy without a usable renderer is skipped");
        check(!CorpseFadePolicy.ShouldAttempt(Plan(offScene: true)), "fade policy: an already recycled off-scene corpse has nothing visible to dissolve");
        check(!CorpseFadePolicy.ShouldAttempt(Plan(activeFades: CorpseFadePolicy.MaxConcurrentFades)),
            "fade policy: the global concurrent-fade cap is hard");
        check(CorpseFadePolicy.ShouldAttempt(Plan(activeFades: CorpseFadePolicy.MaxConcurrentFades - 1)),
            "fade policy: the last slot under the cap is still usable");

        // ---- R1: every refusal names its own reason (the 1.2.9 catch-all was undiagnosable) --
        check(Skip(Plan()) == CorpseFadeSkip.None, "fade reason: a fully healthy plan reports no skip");
        check(Skip(Plan(fadeEnabled: false)) == CorpseFadeSkip.Disabled, "fade reason: FadeEffect=false is reported as disabled");
        check(Skip(Plan(corpseEffects: false)) == CorpseFadeSkip.CorpseEffectsOff, "fade reason: the corpse master switch reports corpse-effects-off");
        check(Skip(Plan(inLevel: false)) == CorpseFadeSkip.OffLevel, "fade reason: lobby corpses report off-level");
        check(Skip(Plan(paused: true)) == CorpseFadeSkip.Paused, "fade reason: a paused game reports paused");
        check(Skip(Plan(playPatched: true)) == CorpseFadeSkip.PatchedByThirdParty, "fade reason: a patched Decay.Play reports patched-by-third-party");
        check(Skip(Plan(prefabSafe: false)) == CorpseFadeSkip.PrefabUnsafe, "fade reason: an unsafe prefab reports prefab-unsafe");
        check(Skip(Plan(pool: CorpseFadePoolState.Missing)) == CorpseFadeSkip.PoolMissing, "fade reason: a missing pool reports pool-missing, not a generic failure");
        check(Skip(Plan(pool: CorpseFadePoolState.Exhausted)) == CorpseFadeSkip.NoFreeInstance, "fade reason: an empty pool reports no-free-instance");
        check(Skip(Plan(haveRenderer: false)) == CorpseFadeSkip.NoRenderer, "fade reason: a renderer-less model reports no-renderer");
        check(Skip(Plan(offScene: true)) == CorpseFadeSkip.OffScene, "fade reason: a recycled corpse reports off-scene");
        check(Skip(Plan(activeFades: CorpseFadePolicy.MaxConcurrentFades)) == CorpseFadeSkip.ConcurrencyCap, "fade reason: a full slot table reports concurrency-cap");
        check(Reason(CorpseFadeSkip.PoolMissing) == "pool-missing" && Reason(CorpseFadeSkip.NoFreeInstance) == "no-free-instance"
            && Reason(CorpseFadeSkip.PatchedByThirdParty) == "patched-by-third-party",
            "fade reason: pool-missing, no-free-instance and patched-by-third-party are distinct log tokens");
        check(Reason(CorpseFadeOutcome.PoolUnverifiable) == "pool-unverifiable"
            && Reason(CorpseFadeOutcome.PlayNotObserved) == "play-not-observed"
            && Reason(CorpseFadeOutcome.PlayThrew) == "play-threw"
            && Reason(CorpseFadeOutcome.ProbeUnknown) == "probe-unknown"
            && Reason(CorpseFadeOutcome.BorrowError) == "borrow-error",
            "fade reason (1.2.19): pool-unverifiable/play-not-observed/play-threw/probe-unknown/borrow-error are distinct log tokens - the initialize-* and not-initialized tokens are gone (the pooled path is fail-closed, F2)");
        check(Reason(CorpseFadeOutcome.PoolArmFailed) == "pool-arm-failed",
            "fade reason (1.2.20): a native arm that throws or binds a different renderer is its own token, not a generic skip");
        check(CorpseFadePolicy.ArmVerdict(false, true) == CorpseFadeArm.Armed
            && CorpseFadePolicy.ArmVerdict(false, false) == CorpseFadeArm.Refused
            && CorpseFadePolicy.ArmVerdict(true, true) == CorpseFadeArm.Failed
            && CorpseFadePolicy.ArmVerdict(true, false) == CorpseFadeArm.Failed,
            "fade arm (1.2.20): only a normal return AND a renderer bound to this corpse may reach Play - an exception never does");
        check(Reason(CorpseFadeOutcome.StartedProbeUnknown) == CorpseFadePolicy.Reason(CorpseFadeOutcome.ProbeUnknown),
            "fade reason: an unreadable state after Play reuses the probe-unknown token");
        check(CorpseFadePolicy.IsReportable(CorpseFadeSkip.PoolMissing) && !CorpseFadePolicy.IsReportable(CorpseFadeSkip.Disabled)
            && !CorpseFadePolicy.IsReportable(CorpseFadeSkip.NoRenderer) && !CorpseFadePolicy.IsReportable(CorpseFadeSkip.OffLevel),
            "fade reason: normal user choices stay silent, environment surprises are reportable");
        check(CorpseFadePolicy.IsStarted(CorpseFadeOutcome.Started) && CorpseFadePolicy.IsStarted(CorpseFadeOutcome.StartedProbeUnknown)
            && !CorpseFadePolicy.IsStarted(CorpseFadeOutcome.PlayNotObserved),
            "fade reason: only a tracked dissolve counts as started");

        // ---- native facts the Unity side relies on ------------------------------------------
        check(CorpseFadePolicy.FadeSeconds == 0.5f, "fade policy: the dissolve lasts 0.5 s, not an instant hide");
        check(CorpseFadePolicy.MaxConcurrentFades == 2, "fade policy: at most two borrowed instances exist at once");
        check(CorpseFadePolicy.StopActionDeactivate == 1, "fade policy: the verified pool stop action is Deactivate (1)");
        check(CorpseFadePolicy.MaxBorrowPolls == 3, "fade policy: an empty pool is polled at most three times per corpse");
        check(CorpseFadePolicy.PrefabIsSafe(CorpseFadePolicy.StopActionDeactivate, true),
            "fade policy: the shipped DecayEffect prefab shape (Deactivate + autoCleanup) is safe");
        check(!CorpseFadePolicy.PrefabIsSafe(0, true), "fade policy: a prefab that does not return itself is refused");
        check(!CorpseFadePolicy.PrefabIsSafe(CorpseFadePolicy.StopActionDeactivate, false),
            "fade policy: autoCleanup=false would leak the cloned renderer, so refuse");
        check(!CorpseFadePolicy.PrefabIsSafe(2, true), "fade policy: any other stop action is refused");
        // ---- 1.2.16 (second real-machine pass): the corpse's OWN Decay component, no pool borrow ----------
        // The reference mod dissolves without borrowing an instance and without validating a renderer target,
        // which is why it also works on corpses that never got a ragdoll. These assertions pin that shape:
        // the native path must not depend on the pool state at all.
        check(CorpseFadePolicy.ShouldAttemptNative(CorpseFadePolicy.NativePath(Plan(), true, 0, true, false))
            && CorpseFadePolicy.ShouldAttemptNative(CorpseFadePolicy.NativePath(Plan(pool: CorpseFadePoolState.Missing), true, 0, true, false))
            && CorpseFadePolicy.ShouldAttemptNative(CorpseFadePolicy.NativePath(Plan(pool: CorpseFadePoolState.Exhausted), true, 0, true, false)),
            "fade native: a safe corpse-own Decay is used even when the pool is missing or exhausted, i.e. the native path does not borrow from it");
        check(!CorpseFadePolicy.ShouldAttemptNative(CorpseFadePolicy.NativePath(Plan(fadeEnabled: false), true, 0, true, false))
            && !CorpseFadePolicy.ShouldAttemptNative(CorpseFadePolicy.NativePath(Plan(corpseEffects: false), true, 0, true, false))
            && !CorpseFadePolicy.ShouldAttemptNative(CorpseFadePolicy.NativePath(Plan(inLevel: false), true, 0, true, false))
            && !CorpseFadePolicy.ShouldAttemptNative(CorpseFadePolicy.NativePath(Plan(paused: true), true, 0, true, false)),
            "fade native: Corpse/FadeEffect=false, the corpse master switch, the lobby and a paused game all mean ZERO native Decay calls");
        check(!CorpseFadePolicy.ShouldAttemptNative(CorpseFadePolicy.NativePath(Plan(offScene: true), true, 0, true, false))
            && !CorpseFadePolicy.ShouldAttemptNative(CorpseFadePolicy.NativePath(Plan(haveRenderer: false), true, 0, true, false))
            && !CorpseFadePolicy.ShouldAttemptNative(CorpseFadePolicy.NativePath(Plan(playPatched: true), true, 0, true, false)),
            "fade native: an off-scene corpse, a renderer-less model and a third-party Decay.Play patch all refuse the native path");
        check(CorpseFadePolicy.NativePath(Plan(), false, 0, true, false) == CorpseFadeNativePath.NoComponent
            && CorpseFadePolicy.NativePath(Plan(), true, 0, false, false) == CorpseFadeNativePath.UnsafeComponent
            && CorpseFadePolicy.NativePath(Plan(), true, CorpseFadePolicy.StopActionDeactivate, true, false) == CorpseFadeNativePath.UnsafeComponent
            && CorpseFadePolicy.NativePath(Plan(), true, CorpseFadePolicy.StopActionDestroy, true, false) == CorpseFadeNativePath.UnsafeComponent
            && CorpseFadePolicy.NativePath(Plan(), true, 0, true, true) == CorpseFadeNativePath.AlreadyPlaying,
            "fade native: a missing component, a stop action that would take the corpse with it (Deactivate/Destroy), autoCleanup=false and an already-running decay are each named");
        check(CorpseFadePolicy.NativeStopActionSafe(CorpseFadePolicy.StopActionNone)
            && !CorpseFadePolicy.NativeStopActionSafe(CorpseFadePolicy.StopActionDeactivate)
            && !CorpseFadePolicy.NativeStopActionSafe(CorpseFadePolicy.StopActionDestroy),
            "fade native: only StopAction.None (0) may be used on a component that lives on the corpse's own GameObject");
        check(CorpseFadePolicy.Reason(CorpseFadeNativePath.NoComponent) == "no-decay-component"
            && CorpseFadePolicy.Reason(CorpseFadeNativePath.UnsafeComponent) == "unsafe-decay-component"
            && CorpseFadePolicy.Reason(CorpseFadeNativePath.AlreadyPlaying) == "decay-already-playing"
            && CorpseFadePolicy.Reason(CorpseFadeNativePath.PatchedByThirdParty) == "patched-by-third-party",
            "fade native: every refusal has its own token, so fade=native-unavailable / fade=fallback never merge into one reason");
        check(CorpseFadePolicy.IsReportableNative(CorpseFadeNativePath.NoComponent)
            && CorpseFadePolicy.IsReportableNative(CorpseFadeNativePath.UnsafeComponent)
            && !CorpseFadePolicy.IsReportableNative(CorpseFadeNativePath.Disabled)
            && !CorpseFadePolicy.IsReportableNative(CorpseFadeNativePath.NoRenderer),
            "fade native: the user's own switches stay silent, the component facts are reported once");
        // R6: the receipt must be reachable without a component. 1.2.16 ANDed `native != null` onto this gate, so a
        // corpse whose hierarchy carries no Decay never printed `reason=no-decay-component` even though the policy
        // said it was reportable - the exact line an operator needs to explain the fallback.
        check(CorpseFadePolicy.ShouldLogNativeRefusal(CorpseFadeNativePath.NoComponent)
            && CorpseFadePolicy.ShouldLogNativeRefusal(CorpseFadeNativePath.UnsafeComponent)
            && CorpseFadePolicy.ShouldLogNativeRefusal(CorpseFadeNativePath.AlreadyPlaying)
            && !CorpseFadePolicy.ShouldLogNativeRefusal(CorpseFadeNativePath.Disabled)
            && !CorpseFadePolicy.ShouldLogNativeRefusal(CorpseFadeNativePath.CorpseEffectsOff),
            "fade native (R6): `no-decay-component` is a reportable refusal on its own - the gate never depends on having read a component, so that line really prints");

        // ---- F1: the EndAt exception barrier (the production code CorpseFeedback calls) ------
        int errors = 0;
        string afterCall = null;
        bool escaped = false;
        try
        {
            CorpseFadePrep.Attempt(() => throw new InvalidOperationException("renderer getter blew up"), _ => true, _ => errors++);
            afterCall = "cleanup-ran";
        }
        catch (Exception) { escaped = true; }
        check(!escaped && afterCall == "cleanup-ran" && errors == 1,
            "fade prep (F1): a throwing renderer getter is contained, so Hide/RecyclePhysics/queue cleanup still run");

        errors = 0; afterCall = null; escaped = false;
        try
        {
            CorpseFadePrep.Attempt(() => new object(), _ => throw new InvalidOperationException("fade begin blew up"), _ => errors++);
            afterCall = "cleanup-ran";
        }
        catch (Exception) { escaped = true; }
        check(!escaped && afterCall == "cleanup-ran" && errors == 1,
            "fade prep (F1): a throwing fade begin is contained, so the old corpse cleanup still runs");

        bool begun = false;
        errors = 0;
        check(!CorpseFadePrep.Attempt(() => null, _ => { begun = true; return true; }, _ => errors++)
            && !begun && errors == 0,
            "fade prep: no usable renderer means no borrow at all and no error report");
        object handed = null;
        check(handedTest(out handed) && ReferenceEquals(handed, "renderer"),
            "fade prep: the selected renderer is handed to the fade begin exactly once");
        check(!CorpseFadePrep.Attempt(() => "renderer", _ => false, _ => { }),
            "fade prep: a refused begin is reported as not started");

        escaped = false;
        try
        {
            CorpseFadePrep.Attempt(() => throw new InvalidOperationException("x"), _ => true,
                _ => throw new InvalidOperationException("logger blew up"));
        }
        catch (Exception) { escaped = true; }
        check(!escaped, "fade prep (F1): even a throwing error sink cannot break the corpse cleanup");

        // ---- R2: a readiness check that throws keeps the old cleanup alive (EndAt barrier) ---------
        var target = new MockFadeTarget();
        var coordinator = new CorpseFadeCoordinator();
        target.ThrowOnReady = true;
        bool prepared = false;
        escaped = false;
        string afterPrepare = null;
        try
        {
            CorpseFadePrep.Attempt(
                () => target.Renderer,
                r => { prepared = coordinator.TryStart(target, r, out _); return prepared; },
                _ => errors++);
            afterPrepare = "hide-recycle-queue-ran";
        }
        catch (Exception) { escaped = true; }
        check(!escaped && afterPrepare == "hide-recycle-queue-ran" && !prepared
            && target.Cleanups == 1 && target.Returns == 1 && coordinator.Active == 0
            && coordinator.LastOutcome == CorpseFadeOutcome.PoolArmFailed
            && coordinator.Failures == 1
            && target.Plays == 0,
            "fade (R2, 1.2.20): a throwing arm is contained by the EndAt barrier, released exactly once, never played, reported as pool-arm-failed, and still lets Hide/RecyclePhysics/queue cleanup run");
        target.ThrowOnReady = false;
        target.Reset();

        // ---- R3: pool-missing / no-free-instance are distinguishable and never leak -----------
        // A fresh coordinator: R2 deliberately spent Failure/Release counters.
        var fresh = new CorpseFadeCoordinator();
        target.Borrow = CorpseFadeBorrow.PoolMissing;
        check(!fresh.TryStart(target, target.Renderer, out CorpseFadeOutcome poolMissing)
            && poolMissing == CorpseFadeOutcome.PoolMissing
            && fresh.Active == 0 && fresh.Failures == 0 && fresh.ManualReleases == 0 && fresh.LastError == null
            && target.Cleanups == 0 && target.Returns == 0 && !target.Armed && !target.Played,
            "fade (R3): a missing native pool reports pool-missing and never initialises, plays or releases");

        target.Borrow = CorpseFadeBorrow.NoFreeInstance;
        check(!coordinator.TryStart(target, target.Renderer, out CorpseFadeOutcome noFree)
            && noFree == CorpseFadeOutcome.NoFreeInstance
            && coordinator.Active == 0 && target.Cleanups == 0 && target.Returns == 0 && !target.Played,
            "fade (R3): an exhausted native pool reports no-free-instance and never touches an instance");
        target.Reset();

        // ---- R4: bounded re-poll of the same corpse's borrow slot ---------------------------
        coordinator.ResetCounters();
        target.Borrow = CorpseFadeBorrow.NoFreeInstance;
        target.FreeAfterBorrows = 2;   // the game's Update returns an instance right after our first poll
        check(coordinator.TryStart(target, target.Renderer, out CorpseFadeOutcome polled) && polled == CorpseFadeOutcome.Started
            && coordinator.Active == 1 && coordinator.LastPolls == 2 && target.Borrows == 2
            && target.Cleanups == 0 && target.Returns == 0,
            "fade (R4): an empty pool that frees an instance within the poll budget still starts the dissolve");
        target.FreeAfterBorrows = 0;
        target.Playing = false;
        coordinator.Sweep(target);
        coordinator.ResetCounters();
        target.Reset();

        target.Borrow = CorpseFadeBorrow.NoFreeInstance;
        check(!coordinator.TryStart(target, target.Renderer, out CorpseFadeOutcome exhausted)
            && exhausted == CorpseFadeOutcome.NoFreeInstance
            && coordinator.LastPolls == CorpseFadePolicy.MaxBorrowPolls
            && target.Borrows == CorpseFadePolicy.MaxBorrowPolls
            && coordinator.Active == 0 && target.Cleanups == 0 && target.Returns == 0
            && target.Readies == 0 && target.Plays == 0,
            "fade (R4): an empty pool is polled exactly MaxBorrowPolls times, then given up without holding or releasing anything");
        target.Borrow = CorpseFadeBorrow.Ok;
        target.Reset();
        coordinator.ResetCounters();

        // ---- borrow/hand-back ownership, proven one case at a time --------------------------
        target.Playing = true;   // still animating for someone else
        check(!coordinator.TryStart(target, target.Renderer, out CorpseFadeOutcome busy) && busy == CorpseFadeOutcome.InstanceBusy
            && coordinator.Active == 0 && target.Cleanups == 0 && target.Returns == 0 && !target.Armed,
            "fade: an instance still animating for someone else is never readiness-checked, played or released");
        target.Playing = false;
        target.Reset();

        // 1.2.19 F2: the production adapter's readiness gate is a constant false (a pooled instance cannot prove it
        // belongs to this corpse), so a borrow is always handed back untouched (cleanup -> return, once, in that
        // order) and reported as one named skip - never played.
        target.ReadyFails = true;
        check(!coordinator.TryStart(target, target.Renderer, out CorpseFadeOutcome notReady) && notReady == CorpseFadeOutcome.PoolUnverifiable
            && coordinator.Active == 0
            && target.Cleanups == 1 && target.Returns == 1 && coordinator.ManualReleases == 1 && coordinator.Recovered == 1
            && target.Calls[target.Calls.IndexOf("cleanup") + 1] == "return"
            && target.Calls[target.Calls.Count - 1] == "return"
            && !target.Played && target.Plays == 0,
            "fade (1.2.19 F2): an unprovable borrowed instance is Cleanup'ed then Return'ed exactly once, in that order, and never Played");
        target.ReadyFails = false;
        target.Reset();
        coordinator.ResetCounters();

        // The armed branch below is UNREACHABLE from production (DecayFadeTarget.Ready is a constant false, pinned by
        // Verify + Test-CorpseFadeIl), but its bookkeeping is still asserted: if a future interop-verified identity
        // proof ever returns true, the hand-back path must already be exact.
        check(coordinator.TryStart(target, target.Renderer, out CorpseFadeOutcome armedOk) && armedOk == CorpseFadeOutcome.Started
            && target.Readies == 1 && target.Plays == 1 && coordinator.Active == 1,
            "fade (1.2.19 F2 bookkeeping): the coordinator's armed branch keeps one Play and one booked slot, so a future identity proof cannot introduce a double return");
        target.Playing = false;
        coordinator.Sweep(target);
        target.Reset();
        coordinator.ResetCounters();

        // ---- R5: a swallowed Play is released exactly once, and reported as such ------------
        target.SwallowPlay = true;   // e.g. a patched Play that never starts the dissolve
        check(!coordinator.TryStart(target, target.Renderer, out CorpseFadeOutcome swallowed) && swallowed == CorpseFadeOutcome.PlayNotObserved
            && coordinator.Active == 0
            && target.Played && target.Cleanups == 1 && target.Returns == 1 && !target.Playing
            && target.Calls[target.Calls.IndexOf("cleanup") + 1] == "return",
            "fade (R5): a swallowed Play is detected through m_playing and released exactly once");
        target.SwallowPlay = false;
        target.Reset();
        coordinator.ResetCounters();

        target.ThrowOnPlay = true;
        check(!coordinator.TryStart(target, target.Renderer, out CorpseFadeOutcome playThrew) && playThrew == CorpseFadeOutcome.PlayThrew
            && coordinator.Active == 0
            && target.Cleanups == 1 && target.Returns == 1,
            "fade: a throwing Play is released exactly once and never tracked");
        target.ThrowOnPlay = false;
        target.Reset();
        coordinator.ResetCounters();

        // A Play that throws *after* the native flag was set is owned by the game; releasing it here
        // would double-return it once the pool recycles the instance itself.
        target.ThrowOnPlay = true;
        target.SetPlayingOnThrow = true;
        check(coordinator.TryStart(target, target.Renderer) && coordinator.Active == 1
            && target.Cleanups == 0 && target.Returns == 0,
            "fade: once m_playing is true the game owns the instance, so a late-throwing Play is never returned by us");
        target.ThrowOnPlay = false;

        // Re-borrowing the very handle we are still tracking (a misbehaving pool) must be refused
        // instead of double-playing or double-returning the same object.
        target.RepeatHandle = true;
        int playsBefore = target.Plays;
        check(!coordinator.TryStart(target, target.Renderer, out CorpseFadeOutcome repeated) && repeated == CorpseFadeOutcome.InstanceTracked
            && coordinator.Active == 1
            && target.Plays == playsBefore && target.Returns == 0,
            "fade: the same instance can never be borrowed twice while it is tracked");
        target.RepeatHandle = false;

        // ---- F2: slots are held from the native state, never from elapsed time --------------
        coordinator.Sweep(target);
        check(coordinator.Active == 1 && coordinator.GameReleases == 0 && target.Returns == 0 && target.Cleanups == 0,
            "fade sweep (F2): a dissolve still reported as playing keeps its slot, however long ago it started");
        target.Playing = true;
        for (int i = 0; i < 8; i++) coordinator.Sweep(target);
        check(coordinator.Active == 1 && coordinator.GameReleases == 0,
            "fade sweep (F2): a long pause / huge frame gap never times out a still-playing instance");
        target.Reset();

        // The game finished and returned the instance itself: the bookkeeping is dropped, nothing is released.
        target.Playing = false;
        coordinator.Sweep(target);
        check(coordinator.Active == 0 && coordinator.GameReleases == 1 && target.Returns == 0 && target.Cleanups == 0,
            "fade sweep (F2): an instance the game finished and returned frees its slot without any release by us");

        // A destroyed instance (level unload) is dropped without touching it.
        check(coordinator.TryStart(target, target.Renderer) && coordinator.Active == 1, "fade sweep (F2): a fresh slot is available again");
        target.Alive = false;
        coordinator.Sweep(target);
        check(coordinator.Active == 0 && coordinator.DestroyedReleases >= 1 && target.Returns == 0 && target.Cleanups == 0,
            "fade sweep (F2): a destroyed native instance is forgotten without Return/Cleanup");
        target.Alive = true;
        target.Reset();

        // Unreadable native state: fail closed (keep the slot) and report for bounded logging.
        check(coordinator.TryStart(target, target.Renderer) && coordinator.Active == 1, "fade sweep (F2): a readable instance is tracked");
        target.ThrowOnProbe = true;
        int unknownHeld = coordinator.Sweep(target);
        check(unknownHeld == 1 && coordinator.Active == 1 && target.Returns == 0 && target.Cleanups == 0,
            "fade sweep (F2): an unreadable state fails closed - the slot stays booked and nothing is released");
        target.ThrowOnProbe = false;
        target.Playing = false;
        coordinator.Sweep(target);
        check(coordinator.Active == 0, "fade sweep (F2): that slot clears once the state is readable again and the instance is idle");
        target.Reset();

        // ---- R6: the concurrency cap is unchanged by polling and by failures ---------------
        coordinator.ResetCounters();
        target.Borrow = CorpseFadeBorrow.NoFreeInstance;
        check(!coordinator.TryStart(target, target.Renderer, out _) && coordinator.Active == 0,
            "fade (R6): a failed retry loop never books a slot");
        target.Borrow = CorpseFadeBorrow.Ok;
        check(coordinator.TryStart(target, target.Renderer) && coordinator.TryStart(target, target.Renderer),
            "fade: two corpses may dissolve at once");
        check(coordinator.Active == CorpseFadePolicy.MaxConcurrentFades, "fade: both dissolve slots are booked");
        int borrowsAtCap = target.Borrows;
        for (int i = 0; i < 5; i++) coordinator.Sweep(target);
        check(!coordinator.TryStart(target, target.Renderer, out CorpseFadeOutcome capped) && capped == CorpseFadeOutcome.ConcurrencyCap
            && target.Borrows == borrowsAtCap && coordinator.Active == 2,
            "fade sweep (F2): with both slots still playing, a third dissolve is refused before borrowing or polling anything");
        check(coordinator.Skips > 0, "fade sweep (F2): refusals are counted for the bounded diagnostics");
        target.Playing = false;
        coordinator.Sweep(target);
        check(coordinator.Active == 0 && target.Returns == 0 && target.Cleanups == 0,
            "fade sweep (F2): both finished slots are freed without any release by us");

        // Reset (level change / unload) keeps live tracking and only drops dead bookkeeping.
        check(coordinator.TryStart(target, target.Renderer) && coordinator.Active == 1, "fade reset (F2): a slot is available again");
        target.Alive = false;
        coordinator.Reset(target);
        check(coordinator.Active == 0 && target.Returns == 0 && target.Cleanups == 0,
            "fade reset (F2): a destroyed slot is cleared on level exit without touching it");
        target.Alive = true;

        target.Playing = false;
        check(coordinator.TryStart(target, target.Renderer) && coordinator.Active == 1, "fade reset (F2): a playing slot is booked again");
        coordinator.Reset(target);
        check(coordinator.Active == 1 && target.Returns == 0 && target.Cleanups == 0,
            "fade reset (F2): an instance the game still animates survives Reset, so re-entry cannot double-borrow it");
        target.ThrowOnProbe = true;
        coordinator.Reset(target);
        check(coordinator.Active == 1, "fade reset (F2): an unreadable slot is also kept (fail closed)");
        target.ThrowOnProbe = false;
        target.Playing = false;
        coordinator.Reset(target);
        check(coordinator.Active == 0 && target.Returns == 0 && target.Cleanups == 0,
            "fade reset (F2): the released slot is dropped and no native object is touched");
        target.Reset();

        // Play started but the state cannot be read afterwards: track the slot instead of guessing.
        target.ThrowOnProbeAfterPlay = true;
        bool tracked = coordinator.TryStart(target, target.Renderer, out CorpseFadeOutcome unknownState);
        target.ThrowOnProbeAfterPlay = false;
        check(tracked && unknownState == CorpseFadeOutcome.StartedProbeUnknown
            && coordinator.Active == 1 && coordinator.UnknownStates == 1
            && target.Cleanups == 0 && target.Returns == 0,
            "fade (F2): an unreadable state right after Play keeps the slot booked and never risks a double return");
        target.Alive = false;
        coordinator.Sweep(target);
        check(coordinator.Active == 0 && target.Returns == 0, "fade (F2): that held slot clears once the instance is gone");
        target.Alive = true;
        target.Reset();

        // Counter hygiene: the diagnostics the plugin logs stay truthful.
        coordinator.ResetCounters();
        check(coordinator.Skips == 0 && coordinator.Failures == 0 && coordinator.ManualReleases == 0
            && coordinator.GameReleases == 0 && coordinator.Recovered == 0 && coordinator.DestroyedReleases == 0
            && coordinator.UnknownStates == 0 && coordinator.BorrowPolls == 0 && coordinator.BorrowMisses == 0
            && coordinator.LastPolls == 0 && coordinator.LastError == null,
            "fade: diagnostics counters reset with the level");

        // ---- R7: the per-level diagnostic budget is bounded and resets with the level --------
        var diagnostics = new CorpseFadeDiagnostics();
        int lines = 0;
        for (int i = 0; i < 40; i++) if (diagnostics.AllowLine(CorpseFadePolicy.Reason(CorpseFadeSkip.PoolMissing))) lines++;
        check(lines == CorpseFadeDiagnostics.MaxPerReason && diagnostics.Lines == CorpseFadeDiagnostics.MaxPerReason,
            "fade budget (R7): one repeated reason can never spend more than MaxPerReason lines");
        int others = 0;
        for (int i = 0; i < 40; i++) if (diagnostics.AllowLine(CorpseFadePolicy.Reason(CorpseFadeSkip.NoFreeInstance))) others++;
        check(others == CorpseFadeDiagnostics.MaxPerReason, "fade budget (R7): a second reason gets its own small allowance");
        int flood = 0;
        for (int i = 0; i < 200; i++) if (diagnostics.AllowLine("r" + (i % 40))) flood++;
        check(diagnostics.Lines == CorpseFadeDiagnostics.MaxLinesPerLevel && flood <= CorpseFadeDiagnostics.MaxLinesPerLevel,
            "fade budget (R7): the total lines per level are hard-capped even across many reasons");
        int warnings = 0;
        for (int i = 0; i < 20; i++) if (diagnostics.AllowWarning()) warnings++;
        check(warnings == CorpseFadeDiagnostics.MaxWarningsPerLevel,
            "fade budget (R7): warnings per level are hard-capped");
        diagnostics.Reset();
        check(diagnostics.Lines == 0 && diagnostics.Warnings == 0 && diagnostics.Count("pool-missing") == 0
            && diagnostics.AllowLine("pool-missing") && diagnostics.AllowWarning(),
            "fade budget (R7): leaving the level restores the whole allowance and forgets the per-reason counts");

        // ---- F5 (1.2.19 review): the terminal fade= line has its OWN budget ------------------------
        // A `fade=fallback` receipt must never spend the terminal allowance, one corpse may claim exactly one
        // terminal line, and the first MaxTerminalPerLevel corpses of a level always print theirs.
        var terminal = new CorpseFadeDiagnostics();
        int fallbackLines = 0;
        for (int i = 0; i < 40; i++) if (terminal.AllowLine("native-not-initialized")) fallbackLines++;
        check(fallbackLines == CorpseFadeDiagnostics.MaxPerReason && terminal.TerminalLines == 0,
            "F5: fallback receipts spend the bounded reason budget only, never the terminal allowance");
        int terminals = 0;
        for (int id = 1; id <= 12; id++) if (terminal.ClaimTerminal(id)) terminals++;
        check(terminals == CorpseFadeDiagnostics.MaxTerminalPerLevel && CorpseFadeDiagnostics.MaxTerminalPerLevel >= 8,
            "F5: the first MaxTerminalPerLevel (>= 8) corpses of a level still print their terminal fade= line, even with the reason budget already exhausted");
        check(!terminal.ClaimTerminal(1) && terminal.TerminalLines == CorpseFadeDiagnostics.MaxTerminalPerLevel,
            "F5: one scheduleId can claim exactly one terminal fade= line - 'one terminal line per corpse' is structural, not luck");
        terminal.Reset();
        check(terminal.TerminalLines == 0 && terminal.ClaimTerminal(1) && terminal.AllowLine("native-not-initialized"),
            "F5: leaving the level restores the terminal allowance together with the reason budget");
    }

    private static CorpseFadeSkip Skip(in CorpseFadePlan plan) => CorpseFadePolicy.SkipReason(plan);

    private static string Reason(CorpseFadeSkip skip) => CorpseFadePolicy.Reason(skip);

    private static string Reason(CorpseFadeOutcome outcome) => CorpseFadePolicy.Reason(outcome);

    private static bool handedTest(out object handed)
    {
        object captured = null;
        bool ok = CorpseFadePrep.Attempt(() => "renderer", r => { captured = r; return true; }, _ => { });
        handed = captured;
        return ok;
    }

    private static CorpseFadePlan Plan(bool fadeEnabled = true, bool corpseEffects = true, bool inLevel = true,
        bool paused = false, bool playPatched = false, bool prefabSafe = true, CorpseFadePoolState pool = CorpseFadePoolState.Ready,
        bool haveRenderer = true, bool offScene = false, int activeFades = 0)
        => new CorpseFadePlan(fadeEnabled, corpseEffects, inLevel, paused, playPatched, prefabSafe, pool,
            haveRenderer, offScene, activeFades);

    // Stand-in for the native Decay instance: it records the exact order of every touched entry point
    // so a double release or a missing release fails an assertion instead of hiding in a log.
    private sealed class MockFadeTarget : ICorpseFadeTarget
    {
        internal readonly object Renderer = new object();
        internal readonly List<string> Calls = new();
        internal object LastHandle;

        internal CorpseFadeBorrow Borrow = CorpseFadeBorrow.Ok;
        internal int FreeAfterBorrows;               // 0 = never; n = the pool answers Ok from the n-th poll on
        internal bool ReadyFails, ThrowOnReady;
        internal bool SwallowPlay, ThrowOnPlay, SetPlayingOnThrow, RepeatHandle;
        internal bool Alive = true, ThrowOnProbe, ThrowOnProbeAfterPlay;

        // Per-handle truth, because two pooled instances can be alive at the same time.
        private readonly HashSet<object> _playing = new();
        private bool _forcePlaying;

        internal bool Playing
        {
            get { return _forcePlaying || _playing.Count > 0; }
            set { _forcePlaying = value; if (!value) _playing.Clear(); }
        }

        internal int Borrows, Plays, Cleanups, Returns, Readies;
        internal bool Armed => Readies > 0;
        internal bool Played => Plays > 0;

        internal void Reset()
        {
            Calls.Clear();
            Borrows = Plays = Cleanups = Returns = Readies = 0;
            _forcePlaying = false;
            _playing.Clear();
        }

        public CorpseFadeBorrow TryBorrow(out object handle)
        {
            Borrows++;
            Calls.Add("borrow");
            handle = null;
            if (FreeAfterBorrows > 0 && Borrows >= FreeAfterBorrows) Borrow = CorpseFadeBorrow.Ok;
            if (Borrow == CorpseFadeBorrow.PoolMissing) return CorpseFadeBorrow.PoolMissing;
            if (Borrow == CorpseFadeBorrow.NoFreeInstance) return CorpseFadeBorrow.NoFreeInstance;
            if (Borrow != CorpseFadeBorrow.Ok) return CorpseFadeBorrow.Error;
            if (RepeatHandle && LastHandle != null) { handle = LastHandle; return CorpseFadeBorrow.Ok; }
            handle = new object();   // every borrow hands out a distinct pooled instance
            LastHandle = handle;
            return CorpseFadeBorrow.Ok;
        }

        // 1.2.20: the adapter arms the borrowed instance (Decay.Initialize) and proves identity by reading
        // m_targetRenderer back; this mock answers the same three-way contract without Unity.
        public CorpseFadeArm Ready(object handle, object renderer)
        {
            Readies++;
            Calls.Add("ready");
            if (ThrowOnReady) throw new InvalidOperationException("readiness unreadable");
            return ReadyFails ? CorpseFadeArm.Refused : CorpseFadeArm.Armed;
        }

        public void Play(object handle, float seconds)
        {
            Plays++;
            Calls.Add("play");
            if (ThrowOnPlay)
            {
                if (SetPlayingOnThrow) Playing = true;
                throw new InvalidOperationException("patched Play threw");
            }
            if (!SwallowPlay) _playing.Add(handle);
        }

        public CorpseFadeState Probe(object handle)
        {
            Calls.Add("probe");
            if (ThrowOnProbe || (ThrowOnProbeAfterPlay && Plays > 0)) throw new InvalidOperationException("native state unreadable");
            if (!Alive) return CorpseFadeState.Gone;
            return (_forcePlaying || _playing.Contains(handle)) ? CorpseFadeState.Playing : CorpseFadeState.Idle;
        }

        public void Cleanup(object handle)
        {
            Cleanups++;
            Calls.Add("cleanup");
        }

        public void Return(object handle)
        {
            Returns++;
            Calls.Add("return");
        }
    }
}
