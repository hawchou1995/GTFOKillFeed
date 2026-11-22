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
    ZeroInstances, // an existing handle with three positively observed zero counts
    Exhausted,   // the pool exists but freeInstanceCount == 0
    Ready        // at least one free instance: a borrow is legal
}
// 1.2.22 (Survival FX.3 black scene, 2026-11-20 17:45): the pooled path makes the native Decay CLONE a
// dissolve copy of the corpse renderer it is armed with (Decay.CreateDecayRenderer -> a GameObject named
// "<source>_DecayRenderer"). A source that already carries a render-registration component, that already
// lives inside the game's own dissolve, or that is itself a dissolve copy therefore makes the clone
// duplicate a registration whose lifetime KillFeed does not own - and the 17:45 session's active,
// Renderer-less "*_DecayRenderer" entry then threw in ShadowEnemyRenderer.ShouldRender 822 times.
// These are observed facts about the INPUT, so a dangerous source is simply never armed: the corpse keeps
// the shipped instant hide at its existing deadline. Nothing is destroyed, no registry is rewritten.
internal readonly struct CorpseFadeInput
{
    internal readonly bool DecayCopy;              // the source GameObject (or an ancestor) is a dissolve copy
    internal readonly bool ShadowRegistered;       // the source GameObject itself carries the registration component
    internal readonly bool DecayOwnerArmed;        // an ancestor already owns/plays a native Decay for this body
    internal readonly bool ClonePresent;           // the body already holds a dissolve copy of itself
    internal readonly bool ShadowRegisteredSubtree; // a registration lives on an (even inactive) child of the source
    // 1.2.22 review #4: the bounded scan stopped before it had looked at every child/ancestor. That is NOT "nothing
    // found": an unvisited node may be the very registration (or the armed owner) we refuse to clone. Truncated
    // therefore means "cannot prove safe" and is a hazard of its own, so the candidate is skipped and the next one
    // (or the shipped hide at the deadline) is used. A tree that was visited exactly to its last node is complete
    // and never sets this flag.
    internal readonly bool Truncated;

    internal CorpseFadeInput(bool decayCopy, bool shadowRegistered, bool decayOwnerArmed, bool clonePresent,
        bool shadowRegisteredSubtree, bool truncated = false)
    {
        DecayCopy = decayCopy;
        ShadowRegistered = shadowRegistered;
        DecayOwnerArmed = decayOwnerArmed;
        ClonePresent = clonePresent;
        ShadowRegisteredSubtree = shadowRegisteredSubtree;
        Truncated = truncated;
    }

    internal static CorpseFadeInput Safe => new CorpseFadeInput(false, false, false, false, false);
    internal static CorpseFadeInput Opaque => new CorpseFadeInput(true, true, true, true, true, true);   // unreadable -> refuse
}

// The first observed reason an input must never be cloned. Named, so the receipt can say which one fired.
internal enum CorpseFadeHazard
{
    None,
    DecayCopy,
    ClonePresent,
    ShadowRegistered,
    ShadowRegisteredSubtree,
    DecayOwnerArmed,
    // The bounded scan ran out of depth or node budget while nodes were still unvisited: safety is unproven.
    HierarchyUnproven
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
    // 1.2.22: the observed hazard class of the object the pooled clone would be made from. Never consulted
    // by the corpse-own component path (that one plays an already-armed component and clones nothing).
    internal readonly CorpseFadeHazard Hazard;

    internal CorpseFadePlan(bool fadeEnabled, bool corpseEffects, bool inLevel, bool paused, bool playPatched,
        bool prefabSafe, CorpseFadePoolState pool, bool haveRenderer, bool offScene, int activeFades,
        CorpseFadeHazard hazard = CorpseFadeHazard.None)
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
        Hazard = hazard;
    }
}

// 1.2.26: the real shape of the game's Decay pool, read once per attempt. ObjectPooler.Pool exposes exactly
// these members (verified against the Test profile's Modules-ASM.dll): m_freeInstances/m_usedInstances
// (LinkedList<GameObject>), m_minCount, dynamic, allowStealing plus the count/freeInstanceCount/
// usedInstanceCount getters. One CorpseFadePoolState could not answer "is the pool structurally empty or just
// busy": free==0 with used==count is a pool at its ceiling, free==0 with count==0 is a pool with no instances at
// all, and dynamic=false means no later borrow can ever grow it. -1 means "the field could not be read" and is
// never confused with the real count 0.
internal readonly struct CorpseFadePoolFacts
{
    internal readonly CorpseFadePoolState State;
    internal readonly int Free;
    internal readonly int Used;
    internal readonly int Count;
    internal readonly int MinCount;
    internal readonly bool Dynamic;
    internal readonly bool AllowStealing;

    internal CorpseFadePoolFacts(CorpseFadePoolState state, int free, int used, int count, int minCount,
        bool dynamic, bool allowStealing)
    {
        State = state;
        Free = free;
        Used = used;
        Count = count;
        MinCount = minCount;
        Dynamic = dynamic;
        AllowStealing = allowStealing;
    }

    // A pool nobody has built yet. Every number stays "unreadable" so a log line never implies a count of 0.
    internal static CorpseFadePoolFacts Missing
        => new CorpseFadePoolFacts(CorpseFadePoolState.Missing, -1, -1, -1, -1, false, false);

    internal bool IsReady => State == CorpseFadePoolState.Ready;

    internal static CorpseFadePoolState Classify(int free, int used, int count)
        => free < 0 || used < 0 || count < 0 ? CorpseFadePoolState.Missing
            : free == 0 && used == 0 && count == 0 ? CorpseFadePoolState.ZeroInstances
            : free > 0 ? CorpseFadePoolState.Ready : CorpseFadePoolState.Exhausted;

    internal string Describe()
        => "pool=" + (State == CorpseFadePoolState.Missing ? "missing"
                : State == CorpseFadePoolState.ZeroInstances ? "zero-instances" : State == CorpseFadePoolState.Exhausted ? "exhausted" : "ready")
            + ";free=" + Token(Free) + ";used=" + Token(Used) + ";count=" + Token(Count)
            + ";min=" + Token(MinCount) + ";dynamic=" + Dynamic + ";steal=" + AllowStealing;

    private static string Token(int value) => value < 0 ? "unreadable" : value.ToString();
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
    // 1.2.22: the object the pooled clone would be made from is dangerous (see CorpseFadeHazard). Named
    // separately from every other refusal, because "we refused this input" is a different receipt than
    // "the pool had nothing".
    HazardousInput,
    // 1.2.26: the pool answered "no free instance" while NONE of our own fades held a slot, often enough that we
    // stopped asking per corpse for a bounded while (see CorpseFadeBackoff). It is named apart from NoFreeInstance
    // because it is the only refusal that describes OUR behaviour instead of the game's state: "we asked and the
    // pool was empty" and "we deliberately did not ask" must never share a token in a host log.
    PoolStarved,
    ZeroInstances,
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
        if (plan.Pool == CorpseFadePoolState.ZeroInstances) return CorpseFadeSkip.ZeroInstances;
        if (plan.Pool != CorpseFadePoolState.Ready) return CorpseFadeSkip.NoFreeInstance;
        if (!plan.HaveRenderer) return CorpseFadeSkip.NoRenderer;    // not every enemy model exposes a usable renderer
        if (plan.OffScene) return CorpseFadeSkip.OffScene;           // already recycled off-scene: nothing visible to dissolve
        // 1.2.22: a clone of THIS object would duplicate a render registration whose lifetime we do not own
        // (the 17:45 session's active, Renderer-less "*_DecayRenderer" entry). The corpse keeps the shipped
        // instant hide at its existing deadline instead.
        if (plan.Hazard != CorpseFadeHazard.None) return CorpseFadeSkip.HazardousInput;
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
        // 1.2.26: a deliberate back-off is still an environment fact the operator has to explain ("why did the
        // dissolve stop for a while?"), so it keeps a terminal line of its own - bounded like every other gate.
        CorpseFadeSkip.PoolStarved => true,
        CorpseFadeSkip.ZeroInstances => true,
        CorpseFadeSkip.HazardousInput => true,
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
        // 1.2.26: "we declined to ask" is its own token - the operator must be able to tell a deliberate
        // back-off from the pool's own answer.
        CorpseFadeSkip.PoolStarved => "pool-starved",
        CorpseFadeSkip.ZeroInstances => "pool-zero-instances",
        CorpseFadeSkip.HazardousInput => "hazardous-input",
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
        CorpseFadeOutcome.PoolArmFailed => "pool-arm-failed",
        CorpseFadeOutcome.PlayThrew => "play-threw",
        CorpseFadeOutcome.PlayNotObserved => "play-not-observed",
        _ => "unexpected"
    };

    // Keeping a concurrency slot after an unreadable probe does not prove the effect started.
    internal static bool IsStarted(CorpseFadeOutcome outcome) => outcome == CorpseFadeOutcome.Started;

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
    // 1.2.20: one place decides what a pooled arm answer means. Only a normal return AND a bound identity counts as
    // Armed, so neither an exception nor a silently different renderer can ever reach Play.
    internal static CorpseFadeArm ArmVerdict(bool threw, bool targetBound)
        => threw ? CorpseFadeArm.Failed : targetBound ? CorpseFadeArm.Armed : CorpseFadeArm.Refused;
    // Identity alone can survive a partial Initialize. Both the old copy and the source must be excluded.
    internal static bool ArmComplete(bool targetBound, bool liveMesh, long copy, long source, long previous, bool playing)
        => targetBound && liveMesh && copy != 0 && source != 0 && copy != source && copy != previous && !playing;

    internal static bool MayReturn(bool sameBorrow, bool alive, CorpseFadeState state, bool sameUsedNode, bool inFree)
        => sameBorrow && alive && state == CorpseFadeState.Idle && sameUsedNode && !inFree;
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

    // ---- 1.2.22: is the object the pooled clone would be made from safe to clone? ------------------
    // The pooled path does not dissolve the corpse itself: it asks the native Decay to build a copy of the
    // corpse renderer (<source>_DecayRenderer) and animates that copy. The copy therefore inherits whatever
    // the source GameObject carries, and its teardown belongs to the native component - not to us. So an
    // input that already has a render registration, already belongs to a dissolve, or already holds a copy
    // is refused BEFORE the borrow: nothing is cloned, nothing has to be cleaned up, and the corpse falls
    // through to the shipped instant hide at its own deadline.
    // The order is the operator-facing order: "this is already a copy" beats "a copy already exists", which
    // beats "this object is a registered shadow caster", which beats "the game already owns this dissolve".
    // #4: an exhausted budget is last but is still a refusal - "we did not finish looking" is never "safe".
    internal static CorpseFadeHazard Hazard(in CorpseFadeInput input)
    {
        if (input.DecayCopy) return CorpseFadeHazard.DecayCopy;
        if (input.ClonePresent) return CorpseFadeHazard.ClonePresent;
        if (input.ShadowRegistered) return CorpseFadeHazard.ShadowRegistered;
        if (input.ShadowRegisteredSubtree) return CorpseFadeHazard.ShadowRegisteredSubtree;
        if (input.DecayOwnerArmed) return CorpseFadeHazard.DecayOwnerArmed;
        if (input.Truncated) return CorpseFadeHazard.HierarchyUnproven;
        return CorpseFadeHazard.None;
    }
    internal static bool IsHazardous(in CorpseFadeInput input) => Hazard(input) != CorpseFadeHazard.None;
    internal static string Reason(CorpseFadeHazard hazard) => hazard switch
    {
        CorpseFadeHazard.DecayCopy => "decay-copy",
        CorpseFadeHazard.ClonePresent => "clone-already-present",
        CorpseFadeHazard.ShadowRegistered => "shadow-registered",
        CorpseFadeHazard.ShadowRegisteredSubtree => "shadow-registered-subtree",
        CorpseFadeHazard.DecayOwnerArmed => "decay-owner-armed",
        CorpseFadeHazard.HierarchyUnproven => "hierarchy-unproven",
        _ => "none"
    };


    // ---- 1.2.22 review #4: bounded-scan budgets as pure decisions -------------------------------------
    // The Unity walk (FadeRendererCast) uses exactly these verdicts, so a test can drive the budget semantics with
    // synthetic trees instead of trusting a hand-counted loop.
    internal const int ScanMaxDepth = 6;
    internal const int ScanMaxNodes = 64;
    internal const int AncestorMaxDepth = 8;
    internal const int SiblingMaxScan = 32;

    internal enum SubtreeStep
    {
        Visit,       // this node has children and the budgets still allow visiting them
        Leaf,        // exactly finished: a node without children
        Truncated    // children exist but depth or node budget is spent -> safety unproven
    }

    internal static SubtreeStep SubtreeVerdict(int depth, int children, int visited)
    {
        if (children <= 0) return SubtreeStep.Leaf;
        if (depth >= ScanMaxDepth) return SubtreeStep.Truncated;
        if (visited >= ScanMaxNodes) return SubtreeStep.Truncated;
        return SubtreeStep.Visit;
    }

    // True only when the ancestor walk stopped on its budget while a parent was still left to inspect. A walk that
    // reached the root, or one that already concluded with a found hazard, is complete.
    internal static bool AncestryTruncated(bool concluded, bool parentRemaining)
        => !concluded && parentRemaining;

    // True only when the sibling scan did not look at every sibling.
    internal static bool SiblingScanTruncated(int children, int scanned)
        => children > scanned;
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
    // 1.2.19: the pooled path used to initialise nothing, so an unarmed borrow collapsed into PoolUnverifiable.
    // 1.2.20 arms the borrow the way the game does, so the two answers are kept apart: PoolUnverifiable = nothing was
    // armed (an instance that cannot be ours), PoolArmFailed = the arm call threw or bound a different renderer.
    PoolUnverifiable,
    PoolArmFailed,
    PlayThrew,
    PlayNotObserved,
    Unexpected
}

// 1.2.20: the three answers a pooled arm attempt can give. Refused = nothing was armed (the instance is handed
// back untouched), Armed = the component now reports the requested renderer, Failed = the native call threw or
// bound itself to a different renderer.
internal enum CorpseFadeArm
{
    Refused,
    Armed,
    Failed
}

// Thin, Unity-free surface over the native API so the borrow/hand-back decisions can be proven by the
// game-independent smoke tests.
internal interface ICorpseFadeTarget
{
    CorpseFadeBorrow TryBorrow(out object handle);
    // 1.2.20: a borrowed instance keeps the PREVIOUS corpse's m_targetRenderer until it is re-initialised, so the
    // adapter arms it exactly like ES_Dead does and then proves identity by reading that field back.
    CorpseFadeArm Ready(object handle, object renderer);
    void Play(object handle, float seconds);
    CorpseFadeState Probe(object handle);
    bool OwnsIdleBorrow(object handle);
    bool Cleanup(object handle);
    bool Return(object handle);
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

// 1.2.26: how long the pooled path is allowed to keep asking an empty pool. A three-level 1.2.25 session opened
// with `started=9/0/0` and then produced 4,401 deferred attempts and 2,200 skipped corpses in its last level with
// `active=0` throughout - i.e. every one of those retries happened while OUR OWN slot table was empty, so the pool
// was held by the game, not by us, and a second attempt a quarter of a second later could not have answered
// differently. The retry ladder itself is not wrong (MaxBorrowPolls covers a same-frame race, the two pre-EndAt
// preparations cover a pool that frees a slot in time), but it must not be paid per corpse for minutes on end.
// This is the bounded, per-level, purely observational back-off that decides when to stop asking:
//   * two consecutive "no free instance" answers while NONE of our fades holds a slot open the cooldown;
//   * one observation of a READY pool, or the cooldown running out, closes it again, so a pool that recovers is
//     used by the very next corpse and a pool that stays empty is not re-asked 2,200 times;
//   * nothing here borrows, creates, returns, expires or destroys a native instance - it only decides whether to
//     call the same read-only gates that already existed.
internal enum CorpseFadeBackoffStep
{
    None,
    // We just stopped asking. The corpse keeps the shipped instant hide at its existing deadline; the pooled
    // path is not retried for every later corpse until the gate opens again.
    Starved,
    // The cooldown ran out: the next corpse asks the pool exactly once more.
    CooldownExpired,
    // The pool answered "ready" while we were backing off: normal behaviour resumes immediately.
    Recovered
}

internal struct CorpseFadeBackoff
{
    // Two empty answers with no slot of our own are enough: the first corpse already showed the pool is held by
    // somebody else, and the second one only rules out "this frame was unlucky".
    internal const int StarvedMisses = 2;
    // Short on purpose: the longest a recovered-but-unnoticed pool can stay unused, and a window a reader of the
    // log can still reconcile with a fight.
    internal const float CooldownSeconds = 8f;

    internal int Misses;
    internal bool Starved;
    internal float RetryAt;

    // Level change / plugin unload: the pool belongs to the game and is rebuilt with the level, so the whole
    // observation is dropped. Nothing native is touched.
    internal void Reset()
    {
        Misses = 0;
        Starved = false;
        RetryAt = 0f;
    }

    // Called once per attempt with the pool as it is right now; returns the transition so the host can log it
    // once per transition instead of once per corpse.
    internal CorpseFadeBackoffStep Observe(bool ready, int activeFades, float now)
    {
        if (ready)
        {
            // Only a pool we had actually stopped asking about "recovers"; a single miss the very next attempt
            // disproved stays silent, so the receipt keeps meaning "the gate was closed and is open again".
            bool wasBackingOff = Starved;
            Reset();
            return wasBackingOff ? CorpseFadeBackoffStep.Recovered : CorpseFadeBackoffStep.None;
        }
        if (Starved)
        {
            if (now < RetryAt) return CorpseFadeBackoffStep.None;
            Reset();
            return CorpseFadeBackoffStep.CooldownExpired;
        }
        // A slot of OUR OWN is not the game's starvation: our own dissolve returns its instance through the game's
        // Update, so the next attempt can legitimately succeed and must stay retryable.
        if (activeFades > 0)
        {
            Misses = 0;
            return CorpseFadeBackoffStep.None;
        }
        if (Misses < StarvedMisses) Misses++;
        if (Misses < StarvedMisses) return CorpseFadeBackoffStep.None;
        Starved = true;
        RetryAt = now + CooldownSeconds;
        return CorpseFadeBackoffStep.Starved;
    }

    // "May the pooled path ask the pool at all?" Only the pooled path consults this; the corpse-own Decay
    // component never borrows and is untouched by a starved pool.
    internal bool MayAttempt(bool ready) => ready || !Starved;
}

// A playing handle is observed only; an unreadable handle stays booked. Only a failed, positively owned
// borrow can be cleaned and returned. Idle does not mean that the game's free list contains it.
internal sealed class CorpseFadeCoordinator
{
    private sealed class Entry
    {
        internal object Handle;
    }

    private readonly List<Entry> _entries = new();

    internal int Active => _entries.Count;
    internal int ManualReleases { get; private set; }
    internal int ReleaseUnconfirmed { get; private set; }
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
            // 1.2.20: the borrowed instance is ARMED the way the game arms its own (ES_Dead.DecayDelayed:
            // GetInstance -> Initialize(renderer, irfs) -> Play) and accepted only when the component then reports
            // THIS renderer and a new live copy. A failed arm never plays; release requires separate proof.
            CorpseFadeArm arm;
            try { arm = target.Ready(handle, renderer); }
            catch (Exception error)
            {
                LastError = error;
                Failures++;
                ReleaseManual(target, handle);
                outcome = CorpseFadeOutcome.PoolArmFailed;
                return false;
            }
            if (arm != CorpseFadeArm.Armed)
            {
                if (arm == CorpseFadeArm.Failed) Failures++;
                else Skips++;
                ReleaseManual(target, handle);
                outcome = arm == CorpseFadeArm.Failed ? CorpseFadeOutcome.PoolArmFailed : CorpseFadeOutcome.PoolUnverifiable;
                return false;
            }
            // Play may throw or be swallowed by a third-party patch; the ONLY ownership proof is the
            // native m_playing flag afterwards (the native Play writes it synchronously, see
            // kf128-ro-investigation/native/disasm-decay-esdead.txt 0x162ff47):
            // Playing transfers control to the game. Idle only permits a separate ownership check.
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
        try
        {
            if (handle != null && target.OwnsIdleBorrow(handle) && target.Cleanup(handle)
                && target.OwnsIdleBorrow(handle) && target.Return(handle))
            {
                ManualReleases++;
                Recovered++;
                return;
            }
        }
        catch (Exception error) { LastError ??= error; }
        ReleaseUnconfirmed++;
    }

    // Called once per tick. Drops bookkeeping only, based on the real native state:
    //   Playing -> keep the slot (never timed out, never returned here);
    //   Idle    -> no longer observed playing; this is not proof of freelist membership;
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
        ReleaseUnconfirmed = 0;
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
