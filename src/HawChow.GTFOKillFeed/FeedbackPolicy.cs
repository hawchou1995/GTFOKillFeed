using System;
using System.Collections.Generic;
using System.Globalization;

namespace HawChow.GTFOKillFeed;

internal enum HitKind { Normal, Armor, Weakspot }
// Which rung of the corpse launch-heading fallback chain produced the heading (diagnostic only, printed as `heading=`).
internal enum LaunchHeadingSource { Damage = 0, CameraYaw = 1, WorldForward = 2 }

// 1.2.19 (pellet path proof): WHICH entry of the damage pipeline produced a hit. The pellet volley used to be
// decided by a gear-category wildcard plus a 0.30 s window, so a melee / explosion / DoT / push hit landing just
// after a shotgun blast could still be summed into it (and swallowed by ShowShotgunDamageNumbers=false). The
// entry itself is the only honest evidence, and it is recorded once on the shared damage root:
//   * ReceiveBulletDamage and Dam_EnemyDamageLimb.BulletDamage           -> Bullet
//   * Dam_EnemyDamageLimb.MeleeDamage                                    -> Melee
//   * ReceiveFireDamage / ReceivePushDamage (enclosing call scopes)      -> Fire / Push
//   * anything else, including an anonymous ProcessReceivedDamage root   -> Other
// `Other` is deliberately 0: it is the default value of the field, so a root nobody labelled can never be
// mistaken for a bullet. (A `Explosion` member is reserved for the explosive entry points; no patch assigns it
// yet, and an unlabelled / third-party explosion therefore stays `Other`, which is the conservative answer.)
internal enum DamageOrigin { Other = 0, Bullet = 1, Melee = 2, Fire = 3, Explosion = 4, Push = 5 }

internal static class FeedbackPolicy
{
    internal const float BurstWindow = 0.65f;
    internal const float NumberLifetime = 0.72f;
    internal const float CorpseBurstDelay = 0.1f;
    internal const float MinCorpseBurstDelay = 0.1f;
    internal const float MaxCorpseBurstDelay = 1.5f;
    internal const float CorpseSoundInterval = 0.12f;
    internal const float CorpsePostBurstHold = 0.75f;
    internal const float CorpseCleanupDelay = CorpseBurstDelay + CorpsePostBurstHold;
    internal const float CorpseOffSceneDistance = 100f;
    internal const int MaxNumbers = 64;
    internal const float SizeBaselineVolume = 1.4f;
    internal const float CorpseBurstSpreadBase = 1.6f;
    internal const float SizeFactorMin = 0.7f;
    internal const float SizeFactorMax = 2.4f;
    internal const float DefaultPanelOpacity = 0f;
    internal const float DefaultMinDistance = 12f;
    internal const float DefaultMaxDistance = 18f;
    // The configured launch budget is now the real one: 0.5-30 m are accepted as written (max >= min),
    // and the only remaining hard bound is the 40 m absolute ceiling used by the physics safety net.
    internal const float MinLaunchDistanceMetres = 0.5f;
    internal const float MaxLaunchDistanceMetres = 30f;
    internal const float AbsoluteDistanceCeiling = 40f;
    // The position clamp is a safety net (a body that tunnels through geometry), never the budget itself:
    // it sits a little above the budget so it cannot quietly shorten a small configured value.
    internal const float LaunchClampSlack = 1.25f;
    internal const float DefaultDeathStrength = 0.5f;
    internal static float Clamp(float value, float min, float max, float fallback)
        => !KillPolicy.IsFinite(value) ? fallback : Math.Max(min, Math.Min(max, value));
    internal static bool IsLeaseCurrent(int captured, int current) => captured > 0 && captured == current;
    internal static float Unit(float value) => Clamp(value, 0f, 1f, 0f);
    internal static float Strength(float burstDamage, float window = BurstWindow)
    {
        window = Clamp(window, 0.2f, 1.5f, BurstWindow);
        float dps = Math.Max(0f, Clamp(burstDamage, 0f, 100000f, 0f)) / window;
        return Unit((float)(Math.Log(1f + dps / 35f) / Math.Log(1f + 800f / 35f)));
    }
    internal static float SizeFactor(float volume)
        => !KillPolicy.IsFinite(volume) || volume <= 0f ? 1f
            : Clamp((float)Math.Pow(volume / SizeBaselineVolume, 1f / 3f), SizeFactorMin, SizeFactorMax, 1f);
    private static float BodySize(float sizeFactor) => Clamp(sizeFactor, SizeFactorMin, SizeFactorMax, 1f);
    internal static float PanelAlpha(float opacity) => Clamp(opacity, 0f, 0.9f, DefaultPanelOpacity);
    internal static float RuleAlpha(float panelAlpha) => Clamp(panelAlpha * 0.55f, 0.05f, 0.35f, 0.22f);
    internal static float TravelBias(float sizeFactor) => 0.75f + 0.25f * BodySize(sizeFactor);
    // 1.2.16: the budget is exactly what the player configured. 1.2.15 clamped the low end up to 12 m
    // (Clamp(min,12,20)) and the high end down to at least 12 m, so `MinLaunchDistance = 3.8` became
    // lo == hi == 12 and burst damage could no longer change anything. Accepted range is now 0.5-30 m
    // with max >= min; a value outside that range is clamped, never silently replaced by 12 m.
    internal static float LaunchBudget(float strength, float minDistance, float maxDistance)
    {
        float lo = Clamp(minDistance, MinLaunchDistanceMetres, MaxLaunchDistanceMetres, DefaultMinDistance);
        float hi = Clamp(maxDistance, lo, MaxLaunchDistanceMetres, Math.Max(lo, DefaultMaxDistance));
        return lo + (hi - lo) * Unit(strength);
    }
    // Safety net only: the ragdoll position is snapped back inside a radius slightly above the budget, so
    // one unlucky collider cannot leave a body 60 m away. It never lifts a small budget to a floor.
    // Review C2: the radius must never be smaller than the corpse itself, or a body that simply falls over
    // (a 0.6-1.5 m drop/slide) would be snapped back onto a sphere with its velocity zeroed - i.e. pinned
    // mid-air. So the floor scales with the measured body size and never drops below MinTravelClampMetres;
    // the clamp stays a position snap-back, it is not the launch force.
    internal const float MinTravelClampMetres = 2.5f;
    internal static float TravelClampFloor(float sizeFactor)
        => Math.Max(MinTravelClampMetres, MinTravelClampMetres * BodySize(sizeFactor));
    internal static float AppliedTravel(float travel) => AppliedTravel(travel, 1f);
    internal static float AppliedTravel(float travel, float sizeFactor)
        => Clamp(travel * LaunchClampSlack, TravelClampFloor(sizeFactor), AbsoluteDistanceCeiling, DefaultMinDistance);
    // One rule for "this body is outside the safety radius", so the smoke tests drive the production
    // decision instead of re-implementing the comparison.
    internal static bool ShouldClampTravel(float distanceSquared, float maxDistance)
        => KillPolicy.IsFinite(distanceSquared) && KillPolicy.IsFinite(maxDistance) && maxDistance > 0f
            && distanceSquared > maxDistance * maxDistance;
    internal static float GibScale(float sizeFactor) => BodySize(sizeFactor);
    internal static float GibScatter(float sizeFactor) => 0.6f + 0.4f * BodySize(sizeFactor);
    internal static int BurstCount(float sizeFactor, float strength = 0f)
    {
        float sizeUnit = Unit((BodySize(sizeFactor) - SizeFactorMin) / (SizeFactorMax - SizeFactorMin));
        return 3 + (int)Math.Round(sizeUnit * 2f, MidpointRounding.AwayFromZero)
            + (int)Math.Round(Unit(strength) * 2f, MidpointRounding.AwayFromZero);
    }
    internal static float BurstSpread(float strength, float sizeFactor)
        => CorpseBurstSpreadBase * GibScatter(sizeFactor) * (0.8f + 0.4f * Unit(strength));
    internal static float GibLaunchSpeed(float strength) => 5.5f + 6.5f * Unit(strength);
    // The burst point is only the *minimum* of the ragdoll-lookup window. Decoupling the window from the
    // burst was the second half of the 1.2.16 "0 applied launches" fix: with the shipped 0.1 s burst delay
    // the old window was [0.04 s, 0.1 s) - three frames at 60 fps and none at all once a frame takes longer
    // than 60 ms, which is exactly what the real log showed (`attempts=0` on 28 give-ups). The window now
    // ends at burst + grace, so a ragdoll the game spawns a few frames late is still picked up.
    internal const float RagdollLookupGrace = 0.30f;
    internal static bool ShouldWaitForRagdoll(float age, float burstAt)
        => KillPolicy.IsFinite(age) && KillPolicy.IsFinite(burstAt) && age < Math.Max(0f, burstAt);
    // Last moment a late lookup may still run: the (clamped) burst delay plus the grace window.
    internal static float RagdollLookupDeadline(float burstAt)
        => Clamp(burstAt, 0f, MaxCorpseBurstDelay, CorpseBurstDelay) + RagdollLookupGrace;
    // Review C1 + 1.2.16 ragdoll fix, reworked in 1.2.18 (R1): the late lookup is paced by TIME, not by a
    // frame count. 1.2.16 gated it on `attempts < 8` and incremented once per tick, so at 144 fps the whole
    // budget was spent in ~0.055 s - far before the only launch that ever worked (0.21-0.27 s in the real
    // log) and before the 0.40 s deadline. The schedule below spreads MaxRagdollLookupAttempts + 1 probes
    // evenly over [FirstLaunchAttemptAge, deadline], so window coverage is the same at 30/60/144 fps and
    // under stutter; one extra probe is still allowed inside RagdollLookupFinalProbeSlack AFTER the deadline,
    // which is the cross-deadline chance a slow tick would otherwise lose.
    internal const int MaxRagdollLookupAttempts = 16;
    internal const float RagdollLookupFinalProbeSlack = 0.05f;
    // Attempt k is due at this age; the last point IS the deadline, so the schedule always reaches it.
    internal static float RagdollLookupAttemptAge(int attempt, float burstAt)
    {
        float deadline = RagdollLookupDeadline(burstAt);
        float start = FirstLaunchAttemptAge;
        if (!KillPolicy.IsFinite(deadline) || deadline <= start) return start;
        int clamped = attempt < 0 ? 0 : attempt > MaxRagdollLookupAttempts ? MaxRagdollLookupAttempts : attempt;
        return start + (deadline - start) * ((float)clamped / MaxRagdollLookupAttempts);
    }
    internal static bool ShouldRetryRagdollLookup(bool haveRagdoll, int attempts, float age, float burstAt)
        => !haveRagdoll && attempts >= 0 && attempts <= MaxRagdollLookupAttempts
            && KillPolicy.IsFinite(age) && age >= RagdollLookupAttemptAge(attempts, burstAt)
            && age <= RagdollLookupDeadline(burstAt) + RagdollLookupFinalProbeSlack;
    // R4 (2nd review): a USABLE but never-activated lease must not lock the ladder. ShouldRetryRagdollLookup is asked
    // with `haveRagdoll == false`, so the old code never searched again while an inactive lease existed - a corpse
    // whose first ragdoll stayed inactive could never reach a live replacement. After the activation grace window the
    // ladder may run again, inside the SAME attempt budget (MaxRagdollLookupAttempts) and the same tick.
    internal static bool ShouldRetryInactiveLease(int attempts, float waitStart, float age)
        => attempts >= 0 && attempts <= MaxRagdollLookupAttempts
            && KillPolicy.IsFinite(age) && KillPolicy.IsFinite(waitStart) && waitStart >= 0f
            && age - waitStart > RagdollLookupGrace;
    // The give-up decision is a SEPARATE predicate: only a spent schedule (plus the final-probe slack) ends
    // the retries, so a corpse can never be reported as unavailable while its window is still open.
    internal static bool ShouldAbandonRagdollLookup(bool haveRagdoll, int attempts, float age, float burstAt)
        => !haveRagdoll && (!KillPolicy.IsFinite(age) || attempts > MaxRagdollLookupAttempts
            || age > RagdollLookupDeadline(burstAt) + RagdollLookupFinalProbeSlack);
    // R1b (1.2.18): the moment the corpse stops being visible, i.e. the last frame a lookup could still mean
    // something (EndAt = burst + 0.75 s in the corpse Tick).
    internal static float CorpseEndAt(float burstAt)
        => Clamp(burstAt, 0f, MaxCorpseBurstDelay, CorpseBurstDelay) + CorpsePostBurstHold;
    // One last OBSERVATION between the deadline and EndAt. A single frame that jumps from inside the window to
    // past deadline + slack (e.g. 0.39 s -> 0.60 s with a 0.40 s deadline) would otherwise close the corpse
    // without ever looking again, so the receipt could not tell "never appeared" from "appeared after a hitch".
    // This probe never applies an impulse: CanApplyLaunch keeps the impulse window at deadline + slack, so a
    // body found here is reported (haveRagdoll/bodies on launch=unavailable) and NOT pushed. Bounded: once per
    // corpse (finalProbeDone) and strictly before EndAt.
    internal static bool ShouldRunFinalRagdollProbe(bool haveRagdoll, int attempts, bool finalProbeDone, float age, float burstAt)
        => !haveRagdoll && !finalProbeDone && attempts <= MaxRagdollLookupAttempts
            && KillPolicy.IsFinite(age) && age >= RagdollLookupDeadline(burstAt)
            && age < CorpseEndAt(burstAt);
    // ---- 1.2.16 (second real-machine pass): one launch decision, one closed-out receipt ------------------
    // The real log had 47 accepted corpses, 19 of them with a ragdoll leased in the death call, and ZERO
    // launch= lines. Two independent holes produced that: a lease whose bodies had been taken away by the
    // game (BodyCount == 0) fell through both the "apply" and the "give up" branch, and the whole queue entry
    // was deleted 0.19 s after death (model-lease-stale) before the retry window even opened.
    //   * ClassifyRagdoll puts the three observable states in one place, so "have a lease" is never again
    //     confused with "have rigidbodies to push".
    //   * The first launch attempt happens at FirstLaunchAttemptAge (0.02 s = the second 60 fps frame)
    //     instead of 0.04 s, and it is retried whenever a NEW lease appears inside the same window
    //     (MaxLaunchApplications bounds it), because the game links the real ragdoll from a coroutine and
    //     the object leased in the death call is sometimes discarded a frame later.
    internal enum RagdollLease
    {
        Missing,   // no lease, or the lease was released/invalidated by the game (generation moved on)
        Empty,     // a lease object exists but owns no rigidbody any more: it must be dropped and searched again
        Usable     // the lease is current and has rigidbodies: the impulse can really be applied
    }
    internal const float FirstLaunchAttemptAge = 0.02f;
    internal const int MaxLaunchApplications = 2;
    internal static RagdollLease ClassifyRagdoll(bool haveLease, int bodyCount, bool leaseCurrent)
        => !haveLease || !leaseCurrent ? RagdollLease.Missing
            : bodyCount > 0 ? RagdollLease.Usable
            : RagdollLease.Empty;
    internal static bool ShouldApplyLaunch(RagdollLease lease) => lease == RagdollLease.Usable;

    // B (1.2.19 real-machine regression): `launch=applied` on a body the game had not activated yet moves nothing -
    // a velocity written into an inactive hierarchy is discarded by the next physics step, and the 0.85 s Hide()
    // then turned that into "the corpse vanished where it stood". Only a lease whose GameObject is live can be
    // pushed; until it is, the impulse stays pending (retried by the existing corpse tick, no new coroutine).
    internal static bool IsRagdollLive(bool activeInHierarchy) => activeInHierarchy;
    // An empty lease is not "no ragdoll": the object we captured is gone, so the ladder has to run again.
    internal static bool ShouldReplaceRagdollLease(RagdollLease lease) => lease == RagdollLease.Empty;
    // ---- R3 (1.2.18): which search results may become a lease ---------------------------------------------
    // The 1.2.16 ladder accepted a RagdollScript whose `ragdolled` flag was FALSE (pass 0 explicitly matched
    // the non-ragdolled scripts) and an EnemyRagdollRef on nothing but a non-null lookup, and the raw-rung
    // host test only rejected the *exact* living model root - not its children/ancestors. On a pooled agent
    // that is a real path to pushing a LIVING enemy. The Unity side now reports evidence as plain booleans
    // and this pure predicate makes the decision, so the negative cases are testable without the game.
    internal enum RagdollRung
    {
        RagdollInstance,  // EnemyAgent.RagdollInstance - the game's own reference (1.2.19 classifies it with
                          // ClassifyRagdollInstance/ClassifyRagdollBodies, never with the living-host evidence below)
        RagdollScript,    // RagdollScript.ragdolled must be TRUE (a false flag is a living model)
        EnemyRagdollRef,  // the ragdoll's own body registry
        RawBodies         // last resort: real, non-kinematic rigidbodies under a non-living host
    }
    internal readonly struct RagdollCandidate
    {
        internal readonly bool LivingHost;       // the host is / contains the living enemy or its model
        internal readonly bool ScriptKnown;      // the rung could read the flag at all
        internal readonly bool Ragdolled;        // ... and the flag was true
        internal readonly bool NonKinematicBody; // at least one non-kinematic rigidbody under the host
        internal RagdollCandidate(bool livingHost, bool scriptKnown, bool ragdolled, bool nonKinematicBody)
        {
            LivingHost = livingHost;
            ScriptKnown = scriptKnown;
            Ragdolled = ragdolled;
            NonKinematicBody = nonKinematicBody;
        }
    }
    // Fail closed: an unreadable flag, a living host, or a purely kinematic hierarchy all mean "not a ragdoll".
    internal static bool AcceptRagdollCandidate(RagdollRung rung, RagdollCandidate evidence)
    {
        if (evidence.LivingHost) return false;
        return rung switch
        {
            RagdollRung.RagdollInstance => true,
            RagdollRung.RagdollScript => evidence.ScriptKnown && evidence.Ragdolled,
            RagdollRung.EnemyRagdollRef => true,
            RagdollRung.RawBodies => evidence.NonKinematicBody,
            _ => false,
        };
    }
    // ---- rung 1: the game's own RagdollInstance is trusted (1.2.19 real-machine fix) -----------------
    // The 1.2.18 R3 guard rejected `EnemyAgent.RagdollInstance` whenever it lived under (or contained) the
    // model root. After a death that is exactly where the game parents its ragdoll, so 3 of 9 corpses in the
    // real log became `candidate=rejected;rung=ragdoll-instance;reason=instance-under-living-model` and then
    // `launch=unavailable;attempts=17`. A hierarchy heuristic must never veto the reference the game itself
    // hands out - only provable nonsense is refused, and every refusal has its own name.
    internal enum RagdollInstanceVerdict
    {
        Accept,
        Missing,     // the wrapper / the reference is null (the normal coroutine state at death)
        ModelRoot,   // the reference IS the living model (or enemy) root itself
        NoBodies,    // the lease measured the instance: it carries no rigidbody at all
        Unreadable   // reading the reference threw, or the wrapper's native object is already gone
    }
    // The reference itself decides; hierarchy evidence is deliberately NOT an input, so a ragdoll that sits
    // under the living model (the pooled case) can no longer be vetoed here.
    internal static RagdollInstanceVerdict ClassifyRagdollInstance(bool present, bool isLivingModelRoot, bool unreadable)
    {
        if (unreadable) return RagdollInstanceVerdict.Unreadable;
        if (!present) return RagdollInstanceVerdict.Missing;
        if (isLivingModelRoot) return RagdollInstanceVerdict.ModelRoot;
        return RagdollInstanceVerdict.Accept;
    }
    // The lease is what proves the instance really carries bodies. bodies < 0 means "not measured" and keeps
    // the verdict unchanged, so an unreadable lease can never be reported as an empty one.
    internal static RagdollInstanceVerdict ClassifyRagdollBodies(RagdollInstanceVerdict verdict, int bodies)
        => verdict == RagdollInstanceVerdict.Accept && bodies >= 0 && bodies <= 0 ? RagdollInstanceVerdict.NoBodies : verdict;
    internal static bool AcceptRagdollInstance(RagdollInstanceVerdict verdict) => verdict == RagdollInstanceVerdict.Accept;
    internal static string Reason(RagdollInstanceVerdict verdict) => verdict switch
    {
        RagdollInstanceVerdict.Missing => "instance-missing",
        RagdollInstanceVerdict.ModelRoot => "instance-is-model-root",
        RagdollInstanceVerdict.NoBodies => "instance-no-bodies",
        RagdollInstanceVerdict.Unreadable => "instance-unreadable",
        _ => "instance-accepted"
    };
    // A second (and final) application is only ever spent on a DIFFERENT object inside the lookup window, so
    // the same ragdoll is never pushed twice and a corpse can never be launched forever.
    internal static bool ShouldReapplyLaunch(int applications, bool sameLease, float age, float burstAt)
        => applications > 0 && applications < MaxLaunchApplications && !sameLease
            && KillPolicy.IsFinite(age) && age <= RagdollLookupDeadline(burstAt) + RagdollLookupFinalProbeSlack;
    // R4 (1.2.18): the impulse identity is the OBJECT itself, not the capture generation. The game's
    // CorpseVisualLease.Capture() bumps the generation whenever it re-captures the same object, so comparing
    // generations let our own re-capture of the SAME ragdoll look like "a new corpse" and spend the second
    // impulse on the same physical instance. Generation still guards lease LIVENESS (IsLeaseCurrent), so a
    // pooled object handed to a different corpse is caught there; here pointer equality is the conservative
    // answer (a reused pointer can only cost an impulse, never apply one to a foreign body).
    internal static bool IsSameLaunchLease(System.IntPtr appliedPointer, int appliedGeneration, System.IntPtr leasePointer, int leaseGeneration)
        => appliedPointer != System.IntPtr.Zero && appliedPointer == leasePointer;
    // R1: the ONE gate every impulse passes, no matter which branch found the lease. `applications <= 0` is the
    // first usable lease; after that only ShouldReapplyLaunch may spend the second (final) impulse - so the
    // late-lookup branch can no longer push the same corpse on every retry (the review measured up to 8).
    // B (1.2.18 review): the ONE impulse gate now stays open until EndAt, not only until deadline + slack. The
    // window was 0.45 s while the corpse lives to 0.85 s, so a body the game hands over after a hitch was found by
    // the terminal probe and then thrown away ("launch=unavailable;haveRagdoll=yes"). The gate is still the single
    // one: at most MaxLaunchApplications impulses, never twice on the same object (IsSameLaunchLease), and nothing
    // at/after EndAt (no corpse left to push).
    // B (1.2.19 real-machine regression): the lease being usable is NOT enough any more - the body also has to be
    // live in the hierarchy (IsRagdollLive), because a velocity written into an inactive ragdoll is discarded. The
    // rest of the gate is unchanged: one impulse budget, one object identity, nothing at/after EndAt.
    internal static bool CanApplyLaunch(RagdollLease lease, bool live, int applications, bool sameLease, float age, float burstAt)
        => IsRagdollLive(live) && ShouldApplyLaunch(lease) && KillPolicy.IsFinite(age)
            && age < CorpseEndAt(burstAt)
            && (applications <= 0 || ShouldReapplyLaunch(applications, sameLease, age, burstAt));
    // The "nothing left to decide" case for an already-pushed, still-usable lease: the decision is final once the
    // impulse budget is spent or the lookup deadline has passed.
    internal static bool ShouldCloseAppliedLaunch(int applications, float age, float burstAt)
        => applications >= MaxLaunchApplications || !KillPolicy.IsFinite(age) || age > RagdollLookupDeadline(burstAt);
    // The model lease is only the visual/hide handle. The game disables the living model when the corpse
    // switches to its spawned ragdoll (~0.19 s after death), which must never cancel a pending launch. R3: the
    // keep is limited to a real despawn - an `enemy-setup` means the wrapper now belongs to a LIVING enemy, so
    // the corpse is closed out instead of being carried over.
    internal const string CorpseDespawnReason = "enemy-despawn";
    internal static bool ShouldKeepCorpseForPendingLaunch(RagdollLease lease, int applications, string reason)
        => applications == 0 && lease == RagdollLease.Usable && reason == CorpseDespawnReason;
    // A corpse entry may only leave the queue with its launch state closed: either it was applied, or an
    // explicit launch=unavailable receipt exists. Nothing is allowed to disappear silently.
    // A disabled launch channel must not print an "unavailable" receipt either: the corpse was never
    // supposed to fly, so silence is the correct, non-alarming terminal state.
    internal static bool ShouldReportUnavailableLaunch(int applications, bool alreadyReported, bool launchActive)
        => launchActive && applications == 0 && !alreadyReported;
    // B (1.2.19 real-machine): the EndAt stage hides and recycles the corpse leases as its last act. A corpse whose
    // ragdoll never became live was never launched (0 impulses), so hiding it is exactly the "body vanished where it
    // stood" the player reported - that one terminal state keeps the game's own corpse presentation. Everything else
    // (applied, launch channel off, no ragdoll at all, exceptions) still hides and recycles exactly as before.
    internal static bool ShouldHideCorpse(int applications, bool ragdollInactive)
        => applications > 0 || !ragdollInactive;
    // R2 (2nd review): a kept corpse must not be dissolved either - the optional native fade ran unconditionally and
    // started a Decay on a body whose presentation we deliberately left to the game. One fact decides both: the
    // corpse is either released (hide + fade + recycle) or kept untouched.
    internal static bool ShouldAttemptCorpseFade(int applications, bool ragdollInactive)
        => ShouldHideCorpse(applications, ragdollInactive);
    // R5 (2nd review): `reason=ragdoll-inactive` is only true while a USABLE lease exists and is still not live. With
    // the flag reset on every discard/new lease this also guarantees `active=False` and the reason can never
    // contradict the receipt (the review saw `launch=unavailable;reason=ragdoll-inactive;active=True`).
    internal static bool IsInactiveRagdollLaunch(int applications, bool ragdollInactive, bool live)
        => applications == 0 && ragdollInactive && !live;
    // R5: one launch state for every log line. Once the state is closed it can never print `pending` again (the
    // 1.2.16 review saw `launch=unavailable` immediately followed by `launch=pending` for the same entity).
    internal static string LaunchState(int applications, bool closed)
        => applications > 0 ? "applied#" + applications.ToString(CultureInfo.InvariantCulture)
            : closed ? "unavailable"
            : "pending";
    // R8: `ladder=` must never be empty. The last rung that was really tried wins; a corpse whose lease went stale
    // prints that reason instead of an empty field, and a corpse that never ran the ladder prints `none`.
    internal static string LadderText(string tried, string last)
        => !string.IsNullOrEmpty(tried) ? tried : !string.IsNullOrEmpty(last) ? last : "none";
    // R4: the EndAt stage closes the launch itself (CorpseFeedback.CloseLaunch), but the lookup window still has to
    // fit inside the post-burst hold - otherwise a corpse would be closed out before its last attempt. This used to
    // be an unasserted arithmetic coincidence (0.75 s hold vs 0.30 s grace).
    internal static bool EndAtCoversLaunchLookup(float holdSeconds, float grace) => holdSeconds > grace;
    internal static bool ShouldKeepShotgunDamageNumbers(bool showShotgunNumbers, bool isShotgun, bool isPreview)
        => !isShotgun || isPreview || showShotgunNumbers;
    internal static bool ShouldMergeShotgunDamageNumbers(bool isShotgun, bool mergePellets)
        => isShotgun && mergePellets;
    internal static bool ShouldDisplayDamageNumber(bool masterEnabled, bool showShotgunNumbers, bool isShotgun)
        => masterEnabled && (!isShotgun || showShotgunNumbers);
    // ---- pellet volley (1.2.19) --------------------------------------------------------------------
    // One shotgun shot is a VOLLEY, not a frame. The 1.2.17 real-machine log settled that: one Fire
    // printed 12 pellet numbers on 12 consecutive frames, because a scope requiring
    // `fireFrame == Time.frameCount` was released the moment Fire returned, so every late pellet drew
    // its own number. The window is now time-boxed, and a drained volley never takes another pellet.
    internal const float MaxVolleySeconds = 0.5f;
    internal const float VolleySilenceSeconds = 0.05f;
    // R1 (2nd review): the capture window is SHORTER than the volley lifetime. Real pellets settle 0.02-0.21 s after
    // the Fire, while the volley itself stays open up to MaxVolleySeconds (0.5 s) so a shot that hit nothing closes on
    // time. A hit that arrives after this lag is never a pellet of that shot - it draws immediately (a late melee /
    // explosion / DoT / EWC projectile hit cannot end up in the shotgun batch).
    internal const float MaxPelletCaptureLag = 0.30f;
    // The only "is this pellet part of the shot" predicate. No frame identity: pellets are settled over
    // later frames, so the shot survives Fire and ends on time instead.
    internal static bool IsShotgunFireScopeCurrent(float volleyStartTime, float now, bool drained)
        => !drained && KillPolicy.IsFinite(volleyStartTime) && KillPolicy.IsFinite(now)
            && now >= volleyStartTime && now - volleyStartTime <= MaxVolleySeconds;
    // R1 (2nd review): the CAPTURE window is the volley window narrowed by MaxPelletCaptureLag. Everything that
    // decides "is this hit a pellet" asks this predicate, never the raw volley window: the volley itself stays open
    // up to MaxVolleySeconds so a miss still closes on time, but a hit that lands later than the measured
    // 0.02-0.21 s pellet settling cannot be part of that shot any more and draws immediately.
    internal static bool IsPelletCaptureWindow(float volleyStartTime, float now, bool drained)
        => IsShotgunFireScopeCurrent(volleyStartTime, now, drained)
            && now - volleyStartTime <= MaxPelletCaptureLag;
    // F1 (1.2.19 review) + R1 (2nd review): the window alone is NOT enough. A shot that hit nothing keeps the window
    // open, and without a category proof any rifle/melee/other hit inside that window was buffered as a pellet
    // (delayed, mean position, damage summed) - and vanished entirely when ShowShotgunDamageNumbers was off. A hit
    // belongs to the volley only when BOTH hold:
    //   * the CAPTURE window is open (same volley, not drained, inside MaxPelletCaptureLag);
    //   * the hit is not proven to belong to another weapon. An UNKNOWN hit category (0) is accepted: that is the
    //     evidence the 1.2.19 real-machine log produced (`[DN] volley;weapon=Shotgun;bullets=6;category=6` /
    //     `category=16` / `category=272` with not a single `[DN] merged`), i.e. the pellet's own category is 0 on a
    //     real client while the weapon's category is known - rejecting it printed one number per pellet again.
    //   * two KNOWN and different categories still reject the hit (a rifle with a real category is never a pellet).
    // R1: an unknown VOLLEY category is NO LONGER a wildcard. It was the second hole the review found: a melee /
    // explosion / DoT / EWC hit that never passes a Fire prefix (so the identity gate is never asked) has an unknown
    // category too, and the old `volleyCategoryId == 0` branch accepted it inside the 0.5 s window. An unknown volley
    // category now only takes an unknown hit category (the same-weapon case), never a known foreign one.
    // The remaining risk - a non-Fire 0-category hit inside the first MaxPelletCaptureLag seconds of a shotgun shot -
    // is closed by the damage-root path proof below (DamageOrigin): this release does add that proof, so the lag is
    // no longer the only thing standing between a melee/explosion/DoT hit and the pellet batch.
    // 1.2.19: the path proof is now the FIRST condition. `hitOrigin` is the DamageOrigin of the damage root that
    // produced this number, i.e. the entry the game itself came through - a melee / explosion / DoT / push /
    // third-party custom hit can never be a pellet no matter which category or window it lands in, and it can
    // therefore never be delayed, summed, or swallowed by ShowShotgunDamageNumbers.
    //
    // 1.2.19 (Survival shotgun retest): the two numbers the capture used to compare are NOT one id space. A shot
    // reports the weapon's own GearCategoryData.persistentID (`[DN] volley;..;category=6` / `16`) while the damage
    // entry reports its own gearCategoryId (`[FEEDBACK] ..;hitCat=2`); BOTH shotguns reported the same 2, so the
    // equality arm could never be true and the `hitCategoryId == 0` wildcard never applied either - every pellet
    // drew its own number. Membership now rests on facts that can be proven for ONE shot, in this order:
    //   * the hit came through a bullet entry (damage-root path proof, unchanged: a melee / explosion / DoT / push
    //     hit is refused whatever its category or window);
    //   * the hit is INSIDE the Fire call that opened this volley (`nestedSameShot`: same thread, the volley's own
    //     Fire scope still on top of that thread's scope stack, that scope not settled). This is the strongest
    //     identity available and it needs no category at all - but only while the volley has not yet PROVEN a pellet
    //     category of its own (`learnedPelletCategoryId == 0`). Once one of its own hits taught it that category, a
    //     nested hit carrying a DIFFERENT known category is refused exactly like an asynchronous one, so the nested
    //     proof can never merge another weapon's hit into the batch. The Survival log proves the game delivers the
    //     rays' damage while the shot's own Fire is on the stack: `[DN] volley;frame=25601` is printed by the Fire
    //     PREFIX, the three pellet `[FEEDBACK]` lines follow it, and CombatPanel's `Fire captured: .. hit=3` for the
    //     same frame comes after them;
    //   * otherwise the hit's category must not prove another weapon: unknown (0), equal to the volley's own
    //     category, or equal to the PELLET category this very volley already took from one of its own nested hits.
    //     That learned value lives and dies with the volley (`ShotgunDamageNumberScope.PelletCategoryId`) and is
    //     never registered in ShotgunCategoryRegistry, so hitCat=2 never becomes a global "shotgun" category and an
    //     asynchronous hit of a shot that never nested anything stays an independent number;
    //   * and the shooter may not be proven to be someone else: the volley records the local player who owns the
    //     firing weapon, every damage number is the local player's own hit, so two KNOWN and different shooters are
    //     two different shots (another player / a bot / a sentry keeps its own numbers). An unreadable identity on
    //     either side decides nothing, exactly like an unknown category.
    internal static bool IsVolleyHit(bool windowOpen, uint volleyCategoryId, uint learnedPelletCategoryId,
        uint hitCategoryId, DamageOrigin hitOrigin, bool nestedSameShot, ulong volleyShooterLookup, ulong hitShooterLookup)
        => windowOpen && hitOrigin == DamageOrigin.Bullet
            && (volleyShooterLookup == 0UL || hitShooterLookup == 0UL || volleyShooterLookup == hitShooterLookup)
            && ((nestedSameShot && learnedPelletCategoryId == 0u)
                || hitCategoryId == 0u
                || hitCategoryId == volleyCategoryId
                || (learnedPelletCategoryId != 0u && hitCategoryId == learnedPelletCategoryId));
    // The origin a SHARED damage root carries. A root is created by one entry and then reused by everything that
    // entry calls (ReceiveBulletDamage -> ProcessReceivedDamage, the limb path, fire, push), so:
    //   * REUSE never downgrades: a root already marked Bullet stays Bullet even though the inner, anonymous
    //     ProcessReceivedDamage entry passes Other. That downgrade is exactly what would break every pellet, so
    //     a known origin always wins and Other may only ever be promoted by a real entry.
    //   * a FRESH root adopts its entry's origin; an anonymous entry (Other) may only inherit what an enclosing
    //     call already proved (`enclosing`), never a guess.
    internal static DamageOrigin ResolveOrigin(bool reused, DamageOrigin existing, DamageOrigin entry, DamageOrigin enclosing)
        => reused
            ? (existing == DamageOrigin.Other ? entry : existing)
            : (entry != DamageOrigin.Other ? entry : enclosing);
    // Only a BULLET hit may be classified as a shotgun number. The gear category is shared by unrelated weapons and
    // the window is time-based, so neither can prove the path; the damage root can, and this is the one gate the
    // damage-number classifier uses. `preview` keeps the F9 preview path exactly as before (never a shotgun hit).
    internal static bool IsShotgunDamageNumber(bool preview, DamageOrigin origin, bool knownShotgunCategory, bool activeFireCategory)
        => !preview && origin == DamageOrigin.Bullet && (knownShotgunCategory || activeFireCategory);
    // A (1.2.19 real-machine regression): the pellet category is unknown on a real client, so the category gate can
    // no longer separate "this shotgun" from "another weapon". The Fire identity does: a Fire from a DIFFERENT
    // weapon instance settles the open volley before it can open (or keep) its own window.
    internal static bool IsForeignVolleyOwner(long volleyOwnerHandle, long fireOwnerHandle)
        => volleyOwnerHandle != 0L && volleyOwnerHandle != fireOwnerHandle;
    // The only end-of-volley decision, shared by the per-frame tick and the next-fire flush:
    //   * the pellets went quiet for VolleySilenceSeconds (measured from the last pellet);
    //   * the hard cap MaxVolleySeconds passed - this is what closes a volley that hit nothing.
    // F3 (1.2.19 review): the expected pellet count is NOT an end condition. `expected` is the archetype's
    // declared ShotgunBulletCount, and a real shot can settle more pellet hits than that (one pellet can touch two
    // limbs), so ending on `pellets >= expected` split one shot into a merged number plus a tail of single ones -
    // exactly the symptom this release fixes. It stays a diagnostic field only ([DN] merged;expected=).
    // 1.2.19 fix D2: "the pellets went quiet" has to survive more than ONE end check. End checks run once per frame
    // (DamageNumbers.Tick <- KillHud.Update), so at any frame rate below 20 fps a single frame gap already exceeds
    // 50 ms and a shot that settles one pellet per frame was drained after its FIRST pellet - one merged number plus
    // a tail of single ones, or (when the drain landed before the first pellet) nothing merged at all. Requiring two
    // consecutive quiet end checks keeps a per-frame pellet stream together at any frame rate and costs one frame of
    // latency at 60 fps; the MaxVolleySeconds cap still bounds every volley, so a missed shot cannot hold a batch.
    internal const int VolleyQuietTicks = 2;
    // The silence itself, without the end decision: pellets arrived and nothing new came for VolleySilenceSeconds.
    internal static bool SilenceElapsed(int pellets, float lastPelletAt, float now)
        => pellets > 0 && KillPolicy.IsFinite(lastPelletAt) && KillPolicy.IsFinite(now)
            && now - lastPelletAt >= VolleySilenceSeconds;
    internal static bool ShouldDrainVolley(int pellets, float startTime, float lastPelletAt, float now, bool drained, int quietTicks)
    {
        if (drained) return false;
        if (!KillPolicy.IsFinite(now) || !KillPolicy.IsFinite(startTime)) return true;
        if (SilenceElapsed(pellets, lastPelletAt, now) && quietTicks >= VolleyQuietTicks) return true;
        return now - startTime >= MaxVolleySeconds;
    }
    // (The un-guarded 5-argument overload was removed with 1.2.19 fix D2: an end check that does not know how many
    // consecutive quiet checks it has seen is exactly the one that split one shot across a long frame.)
    // A drained volley draws nothing when the shotgun numbers are hidden or when it never took a pellet
    // (a miss still opens a volley - it just has nothing to show).
    internal static bool ShouldDrawDrainedVolley(bool showNumbers, int pellets) => showNumbers && pellets > 0;
    internal static bool ShouldSuppressShotgunDamageNumber(bool scopeMatches, bool preview, bool showShotgunNumbers)
        => scopeMatches && !preview && !showShotgunNumbers;
    internal static bool IsCorpseOffSceneDistanceSquared(float distanceSquared)
        => !KillPolicy.IsFinite(distanceSquared) || distanceSquared > CorpseOffSceneDistance * CorpseOffSceneDistance;
    internal static bool ShouldUseCapturedBurstOrigin(float enemyDistanceSquared, float bodyDistanceSquared)
        => IsCorpseOffSceneDistanceSquared(enemyDistanceSquared) || IsCorpseOffSceneDistanceSquared(bodyDistanceSquared);
    internal static bool ShouldBreakCorpseLimbs(bool offSceneCleanupApplied, bool dismemberActive)
        => dismemberActive && !offSceneCleanupApplied;
    // Corpse/ExplosionFx is still the master of the burst pair; BurstVisual and BurstSound only split
    // what used to be one flag, so with both at their defaults the 1.2.16 behaviour is reproduced exactly.
    internal static bool ShouldPlayCorpseBurstFx(CorpseSwitches switches)
        => switches.BurstVisualActive;
    internal static CorpseBurstPlan BurstPlan(CorpseSwitches switches, float sizeFactor, float strength)
        => ShouldPlayCorpseBurstFx(switches)
            ? new CorpseBurstPlan(BurstCount(sizeFactor, strength), switches.BurstSoundActive)
            : new CorpseBurstPlan(0, switches.BurstSoundActive);
    // Launch keeps its own switch: turning it off must not stop the model lease, the hidden map or the
    // gib/limb cleanup - only the velocity and the per-frame ragdoll limiting go away.
    internal static bool ShouldApplyLaunch(bool launchActive, bool launchClosed, float age)
        => launchActive && !launchClosed && age >= 0.02f;
    internal static bool ShouldLimitRagdoll(bool launchActive, bool offSceneCleanupApplied)
        => launchActive && !offSceneCleanupApplied;
    internal static bool ShouldTrackChunks(bool chunksActive) => chunksActive;
    internal static bool ShouldFlushCorpseSound(bool burstSoundActive) => burstSoundActive;
    internal static float Travel(float strength) => LaunchBudget(strength, DefaultMinDistance, DefaultMaxDistance);
    internal static float Travel(float strength, float sizeFactor) => Travel(strength) * TravelBias(sizeFactor);
    internal static float Travel(float strength, float minDistance, float maxDistance, float sizeFactor)
        => LaunchBudget(strength, minDistance, maxDistance) * TravelBias(sizeFactor);

    // ---- launch heading: the ONE place the corpse's horizontal direction is decided ---------------------------
    // CorpseFeedback.Schedule used to flatten the death direction itself and, whenever the horizontal part
    // collapsed, replace it with world +Z - a heading with no relation to where the player was looking. The
    // collapse is not exotic: |pitch| > 84.26 deg (cos < 0.1) is what a point-blank kill at your own feet gives.
    // The heading is now a pure call with ONE fallback chain, and the reason is reported so the next log says
    // which rung was used:
    //   * the death direction's horizontal part, whenever it is long enough to name a heading (a shot that
    //     travels down at 84 deg still says which way the shooter faced);
    //   * otherwise the camera's YAW-projected forward, which stays horizontal even when the camera looks
    //     straight down, so the corpse still flies away from the player instead of to world +Z;
    //   * otherwise world +Z (no damage direction AND no camera), i.e. exactly the legacy value.
    // Review R2: the camera rung also needs an explicit AVAILABILITY fact, not just a vector. The adapter used to
    // initialise the yaw vector to +Z, so a null/throwing CameraManager produced a "camera-yaw" heading that was really
    // a constant. `cameraAvailable` is that fact; when it is false this function cannot return CameraYaw at all.
    // Pitch itself is still discarded on purpose: the vertical kick is LaunchUpSpeed's job, and a corpse driven
    // into the floor is not a better read. Only the heading changed here - no damage value, no velocity does.
    internal const float MinLaunchHeadingLength = 0.1f;
    internal static LaunchHeadingSource LaunchHeading(float dirX, float dirZ, bool cameraAvailable,
        float cameraYawX, float cameraYawZ, out float headingX, out float headingZ)
    {
        headingX = 0f;
        headingZ = 1f;
        if (TryNormalizeHeading(dirX, dirZ, ref headingX, ref headingZ)) return LaunchHeadingSource.Damage;
        if (cameraAvailable && TryNormalizeHeading(cameraYawX, cameraYawZ, ref headingX, ref headingZ))
            return LaunchHeadingSource.CameraYaw;
        headingX = 0f;
        headingZ = 1f;
        return LaunchHeadingSource.WorldForward;
    }
    private static bool TryNormalizeHeading(float x, float z, ref float headingX, ref float headingZ)
    {
        if (!KillPolicy.IsFinite(x) || !KillPolicy.IsFinite(z)) return false;
        float sqr = x * x + z * z;
        if (!(sqr >= MinLaunchHeadingLength * MinLaunchHeadingLength)) return false;
        float length = (float)Math.Sqrt(sqr);
        headingX = x / length;
        headingZ = z / length;
        return true;
    }
    // The pitch of the RAW death direction, in degrees, for the receipt: 0 = level, negative = looking down.
    // Review R5: this has to measure the vector the game actually handed us - never the camera fallback - and it has to
    // say "unavailable" instead of "0" when there is no usable vector. This is a MEASUREMENT, never an input.
    internal static bool TryLaunchPitchDegrees(float dirX, float dirY, float dirZ, out float pitch)
    {
        pitch = 0f;
        if (!KillPolicy.IsFinite(dirX) || !KillPolicy.IsFinite(dirY) || !KillPolicy.IsFinite(dirZ)) return false;
        float length = (float)Math.Sqrt(dirX * dirX + dirY * dirY + dirZ * dirZ);
        if (!(length > 0.0001f)) return false;
        float ratio = (float)Math.Max(-1.0, Math.Min(1.0, dirY / length));
        pitch = (float)(Math.Asin(ratio) * (180.0 / Math.PI));
        return true;
    }
    internal static float LaunchPitchDegrees(float dirX, float dirY, float dirZ)
        => TryLaunchPitchDegrees(dirX, dirY, dirZ, out float pitch) ? pitch : 0f;
    internal static string PitchText(bool available, float pitch)
        => available ? pitch.ToString("0", CultureInfo.InvariantCulture) : "unavailable";
    internal static string HeadingText(LaunchHeadingSource source) => source switch
    {
        LaunchHeadingSource.Damage => "damage",
        LaunchHeadingSource.CameraYaw => "camera-yaw",
        _ => "world-forward"
    };
    // ---- launch physics: the travel budget becomes a horizontal launch speed ---------------------
    // The body leaves the corpse CorpseLaunchHeightMetres above the ground with LaunchUpSpeed upwards,
    // decays with CorpseDrag while it is airborne, and then slides to a stop. The share of v0/drag that
    // survives that flight is LaunchTravelFraction (the air phase plus the part of the slide that ground
    // friction leaves behind). Inverting it is what makes a configured budget observable: 1.2.15 always
    // launched at 4.2-8.6 m/s (a fixed ~2-5 m) whatever the cfg said, and clamped 9 m/s on top.
    internal const float CorpseDrag = 0.8f;
    internal const float CorpseGravity = 9.81f;
    internal const float CorpseLaunchHeightMetres = 1f;
    internal const float SlideRecovery = 0.35f;
    // Review C5: the speed floor is a positivity guard only. The visible floor is the configured budget
    // (MinLaunchDistanceMetres); clamping the already-frozen travel up to 0.5 m made a sub-0.5 m budget
    // (e.g. 0.5 m x a 0.7 size bias = 0.35 m) fly farther than configured.
    internal const float MinLaunchSpeed = 0.1f;
    internal const float MinTravelBudgetMetres = 0.05f;
    internal const float MaxLaunchSpeed = 42f;
    internal const float LaunchVelocitySlack = 1.15f;
    internal const float LegacyVelocityCap = 9f;
    // Review C3: a rigidbody moved by Discrete collision jumps in whole fixed steps. At 36.7 m/s (a 30 m
    // budget) one 20 ms step is 0.73 m - thicker than most walls - so the corpse would tunnel through
    // geometry (and then be teleported back by the position clamp) or shove the player. Above
    // MaxDiscreteTravelPerStep the flight switches that body to ContinuousDynamic and restores the original
    // mode on release; everything at or below keeps the stock Discrete mode, so the legacy 4.2-8.6 m/s
    // launches, and every small budget, behave exactly as before.
    internal const float CorpseFixedTimeStep = 0.02f;
    internal const float MaxDiscreteTravelPerStep = 0.25f;
    internal static bool NeedsContinuousCollision(float speed)
        => KillPolicy.IsFinite(speed) && speed * CorpseFixedTimeStep > MaxDiscreteTravelPerStep;
    internal static float LaunchUpSpeed(float strength) => 1.6f + Unit(strength);
    internal static float LaunchAirTime(float strength)
    {
        float up = LaunchUpSpeed(strength);
        return (up + (float)Math.Sqrt(up * up + 2f * CorpseGravity * CorpseLaunchHeightMetres)) / CorpseGravity;
    }
    internal static float LaunchTravelFraction(float strength)
    {
        float decay = (float)Math.Exp(-CorpseDrag * LaunchAirTime(strength));
        return (1f - decay) + SlideRecovery * decay;
    }
    // Unclamped inverse: budget * drag / realised fraction. Used for the "did the speed cap bite" check.
    // Review C5: only a positivity/absolute guard here - the frozen travel is NOT lifted to the configured
    // minimum, so a sub-0.5 m budget really produces a sub-0.5 m flight.
    internal static float LaunchSpeedRaw(float travel, float strength)
    {
        float fraction = LaunchTravelFraction(strength);
        if (!KillPolicy.IsFinite(fraction) || fraction <= 0.05f) fraction = 0.05f;
        float budget = KillPolicy.IsFinite(travel) ? Math.Max(travel, MinTravelBudgetMetres) : MinTravelBudgetMetres;
        if (budget > AbsoluteDistanceCeiling) budget = AbsoluteDistanceCeiling;
        return budget * CorpseDrag / fraction;
    }
    internal static float LaunchSpeedFor(float travel, float strength)
        => Clamp(LaunchSpeedRaw(travel, strength), MinLaunchSpeed, MaxLaunchSpeed, MinLaunchSpeed);
    internal static bool LaunchSpeedCapped(float travel, float strength)
        => LaunchSpeedRaw(travel, strength) > MaxLaunchSpeed;
    // Forward model, same constants: what the applied speed should cover, for the [CORPSE] log.
    internal static float LaunchDistance(float speed, float strength)
        => Clamp(speed, 0f, MaxLaunchSpeed, 0f) / CorpseDrag * LaunchTravelFraction(strength);
    // The per-body velocity cap follows the budget instead of flattening it at 9 m/s: it is never below
    // the old 9 m/s (nothing got weaker) and otherwise a little above the speed that was just applied.
    internal static float LaunchVelocityCap(float speed)
        => Math.Max(LegacyVelocityCap, Clamp(speed, 0f, MaxLaunchSpeed, 0f) * LaunchVelocitySlack);
    internal static int Fragments(float strength) => 2 + (int)Math.Floor(4f * Unit(strength));
    internal static float FragmentRadius(float strength) => 0.65f + 1.35f * Unit(strength);
    internal static float FragmentRadius(float strength, float sizeFactor) => FragmentRadius(strength) * GibScatter(sizeFactor);
    internal static string DamageText(float value, HitKind kind)
    {
        if (!KillPolicy.IsFinite(value) || value < 0f) return string.Empty;
        string text = value > 0f && value < 0.01f ? value.ToString("0.####", CultureInfo.InvariantCulture) : value.ToString("0.##", CultureInfo.InvariantCulture);
        return kind == HitKind.Weakspot ? text + "!" : kind == HitKind.Armor ? "[" + text + "]" : text;
    }
    internal static float NumberScale(float age, HitKind kind)
    {
        float t = Unit(age / 0.14f);
        float pop = (float)Math.Sin(t * Math.PI) * (1f - t);
        return (kind == HitKind.Weakspot ? 1.18f : kind == HitKind.Armor ? 0.82f : 1f) * (1f + 0.28f * pop);
    }
    internal static float Alpha(float age, float lifetime)
        => Unit((lifetime - age) / 0.22f);
    // Damage-number opacity is one multiplier applied to the number's own fade: the eight outline
    // labels and the text label share it, and the weakspot colour's own alpha still multiplies once
    // on the text only (historical behaviour: outlines never followed the colour alpha). 0 means
    // nothing is submitted at all; 1 reproduces the previous algorithm exactly (fade * 1f is exact).
    internal const float DefaultNumberOpacity = 1f;
    internal static float NumberOpacity(float opacity) => Clamp(opacity, 0f, 1f, DefaultNumberOpacity);
    internal static bool ShowsNumber(float opacity) => NumberOpacity(opacity) > 0f;
    internal static float NumberAlpha(float age, float lifetime, float opacity)
        => Alpha(age, lifetime) * NumberOpacity(opacity);
    internal static float NumberTextAlpha(float colorAlpha, float alpha) => colorAlpha * alpha;

    // Damage-number weight. IMGUI's legacy text has no variable font axis, so "weight" is three
    // cooperating knobs that together read as a thicker or thinner glyph:
    //   * FontStyle: Normal below 0.5, Bold at and above it (1.0 = the shipped Bold look),
    //   * the outline radius (the eight dark passes) scales with it,
    //   * above 1.0 the body is stamped four more times on a small circle, which genuinely thickens
    //     the stroke instead of just fattening its shadow.
    internal const float DefaultNumberWeight = 1f;
    internal const float MinNumberWeight = 0f;
    internal const float MaxNumberWeight = 2f;
    internal const float BoldWeightThreshold = 0.5f;
    internal const int NumberThickenPassesMax = 4;

    internal static float NumberWeight(float configured)
        => Clamp(configured, MinNumberWeight, MaxNumberWeight, DefaultNumberWeight);

    internal static bool NumberUsesBold(float configured) => NumberWeight(configured) >= BoldWeightThreshold;

    // 0 -> 0.9 px, 1 -> 1.6 px (the shipped value), 2 -> 2.3 px at 1080p.
    internal static float NumberOutlineRadius(float configured) => 0.9f + 0.7f * NumberWeight(configured);

    internal static int NumberThickenPasses(float configured)
        => NumberWeight(configured) <= 1f ? 0 : NumberThickenPassesMax;

    // 1 -> 0 px, 2 -> 0.55 px at 1080p.
    internal static float NumberThickenRadius(float configured) => (NumberWeight(configured) - 1f) * 0.55f;

    // DamageNumbers/ApproachEffect (1.2.16): the fly-at-the-viewer trajectory is OPTIONAL and off by
    // default. false reproduces the first shipped trajectory bit for bit - the number appears on the
    // projected hit point and drifts up while it fades (NumberDriftX/NumberDriftY, no growth, no
    // through-body leg). true is the 1.2.16 flight: the prismatic dive and the plain body-crossing line.
    // The switch lives in one constant so the locked 1.2.15 fixtures, the ConfigSmoke key default and
    // every gate name the same value.
    internal const bool DefaultApproachEffect = false;
    // DamageNumbers/ApproachEffect = true: the projected hit point slides toward where the camera looks
    // (the screen centre, fanned a little by the number's own side offset so simultaneous hits stay
    // readable) while the glyph grows, so the number reads as coming at the player. The ease-in makes it
    // accelerate as it closes in, like being pulled toward you. Those constants only apply when the
    // switch is on; the off branch never reads them.
    internal const float ApproachWindowFraction = 1f;    // fly for the WHOLE lifetime, never "arrive then float"
    internal const float ApproachEasePower = 2.6f;       // back-loaded: slow start, hard finish = it charges at you
    internal const float ApproachGrowth = 0.45f;         // final glyph size is 1.45x the spawn size
    internal const float ApproachFanFraction = 1.5f;     // lateral settle scale at the target
    internal const float ApproachRiseFraction = 0.25f;   // outer slots settle a little higher
    internal const float ApproachRisePixels = 10f;       // every slot settles above the reticle
    // A prismatic hit is the big one: it travels much further into your face instead of settling on the
    // arc - much more growth than a plain number and one shared, exact target, so it reads as a slab
    // coming at you. 1.2.16 grows it further (1.0 -> 2.45x) and charges earlier (its own 2.2 ease), but
    // only the prismatic numbers use these constants: ApproachGrowth/ApproachEasePower stay untouched,
    // so a plain, weakspot or armour number keeps the shipped 1.2.15 flight bit for bit.
    internal const float PrismaticApproachGrowth = 1.45f;
    internal const float PrismaticApproachEasePower = 2.2f;  // flatter than the shared 2.6: charges sooner
    internal const float PrismaticApproachFan = 0f;          // every prismatic number lands on ONE point
    internal const float PrismaticApproachRisePixels = 4f;
    internal const float PrismaticApproachRiseFraction = 0f; // the slot no longer moves the destination
    // The bearing it comes FROM is randomised instead of the destination: the residual offset from that
    // single target is rotated by a per-number angle that grows with the flight (0 at spawn, full at
    // arrival), so each prismatic hit dives in from a different side and still lands in the same place.
    internal const float PrismaticFlyJitterDegrees = 52f;
    // The rotation above can only move the residual offset, so on a centred hit it reads as a few pixels
    // of wobble. The swoop adds a real lateral excursion on top: a seeded, bounded sideways push
    // (perpendicular to the incoming line) whose envelope is zero at spawn and zero at arrival, so the
    // number swings out while it is still opaque, then still lands exactly on the shared target. The
    // amplitude is capped by PrismaticSwingPixels * resolution, which is what keeps a prismatic hit from
    // wandering across the screen or parking over the reticle.
    internal const float PrismaticSwingPixels = 72f;
    internal const float PrismaticSwingFloor = 0.55f;        // every seed swings at least 55% of the cap
    // Plain numbers fly a random line that passes THROUGH the player: the crossing point sits below the
    // reticle (the body is under the eyes), and the number keeps going past it instead of parking.
    internal const float BodyDropFraction = 0.10f;        // body point = screen centre + 10% of the height
    internal const float BodyCrossFraction = 0.78f;        // fraction of the flight where the body is crossed
    internal const float NumberFlyJitterDegrees = 58f;     // wider than the prismatic fan: every hit differs
    internal const float ExitOvershootFraction = 0.42f;    // exit point this far past the body, x screen height

    // Deterministic hash -> [-1, 1]. No System.Random instance, no allocation, and the same seed always
    // yields the same angle, so a number never re-rolls its bearing mid-flight.
    internal static float FlyJitter(int seed)
    {
        uint x = unchecked((uint)seed * 2654435761u);
        x ^= x >> 15;
        x = unchecked(x * 2246822519u);
        x ^= x >> 13;
        float unit = (x & 0xFFFFu) / 65535f;
        return unit * 2f - 1f;
    }

    internal static float FlyJitterRadians(int seed)
        => FlyJitter(seed) * (PrismaticFlyJitterDegrees * (float)(Math.PI / 180.0));

    // Rotates the offset of (x, y) around the target. A zero offset stays exactly on the target, which is
    // what makes "random direction, unified destination" hold at the end of the flight.
    internal static void RotateAroundTarget(float x, float y, float targetX, float targetY, float radians, out float rotatedX, out float rotatedY)
    {
        rotatedX = x;
        rotatedY = y;
        if (!KillPolicy.IsFinite(x) || !KillPolicy.IsFinite(y) || !KillPolicy.IsFinite(targetX)
            || !KillPolicy.IsFinite(targetY) || !KillPolicy.IsFinite(radians)) return;
        float dx = x - targetX;
        float dy = y - targetY;
        float cos = (float)Math.Cos(radians);
        float sin = (float)Math.Sin(radians);
        rotatedX = targetX + dx * cos - dy * sin;
        rotatedY = targetY + dx * sin + dy * cos;
    }

    // The whole prismatic incoming flight in one place: straight slide from the hit point to the one
    // shared target, the seeded bearing rotation on top of it, then the bounded lateral swoop. Spawn
    // lands on the projected hit point (t = 0) and arrival lands exactly on the target (offset zero and
    // swing zero), so the extra amplitude can never drift the destination. NaN inputs pass through the
    // untouched approach rather than inventing coordinates.
    // The straightX/straightY outputs are the unrotated line the glyph deviates from: they exist so the
    // locked tests measure the excursion against the PRODUCTION path instead of re-implementing it
    // (review A5). NumberPose below is the single caller in the plugin and discards them.
    internal static void PrismaticFlightPoint(int seed, float approach, float startX, float startY,
        float targetX, float targetY, float resolution, out float x, out float y,
        out float straightX, out float straightY)
    {
        float t = Unit(approach);
        straightX = ApproachAxis(startX, targetX, t);
        straightY = ApproachAxis(startY, targetY, t);
        x = straightX;
        y = straightY;
        if (!KillPolicy.IsFinite(x) || !KillPolicy.IsFinite(y)
            || !KillPolicy.IsFinite(targetX) || !KillPolicy.IsFinite(targetY)) return;
        RotateAroundTarget(x, y, targetX, targetY, FlyJitterRadians(seed) * t, out x, out y);
        float swing = PrismaticSwing(seed, t, resolution);
        if (swing == 0f) return;
        float dirX = targetX - startX;
        float dirY = targetY - startY;
        float length = (float)Math.Sqrt(dirX * dirX + dirY * dirY);
        if (!KillPolicy.IsFinite(length) || length <= 0.0001f) return;
        // Perpendicular of the incoming line, so the swoop is lateral (never toward or away from the
        // reticle) and its length is exactly the bounded swing.
        x += dirY / length * swing;
        y += -dirX / length * swing;
    }

    internal static void PrismaticFlightPoint(int seed, float approach, float startX, float startY,
        float targetX, float targetY, float resolution, out float x, out float y)
        => PrismaticFlightPoint(seed, approach, startX, startY, targetX, targetY, resolution, out x, out y, out _, out _);


    internal static float NumberFlyRadians(int seed)
        => FlyJitter(seed) * (NumberFlyJitterDegrees * (float)(Math.PI / 180.0));

    // The straight line a plain number takes: it crosses the body point at BodyCrossFraction of the flight
    // and carries on past it, so the glyph sweeps through the player instead of stopping in mid air.
    internal static void FlightTarget(float centreX, float centreY, float screenHeight, int seed,
        out float bodyX, out float bodyY, out float exitX, out float exitY)
    {
        bodyX = centreX;
        bodyY = centreY;
        exitX = centreX;
        exitY = centreY;
        if (!KillPolicy.IsFinite(centreX) || !KillPolicy.IsFinite(centreY) || !KillPolicy.IsFinite(screenHeight)) return;
        float height = Math.Max(1f, screenHeight);
        bodyY = centreY + BodyDropFraction * height;
        float angle = NumberFlyRadians(seed);
        float dx = (float)Math.Cos(angle);
        // Always biased downwards: whatever the random bearing, the exit is below the body point.
        float dy = Math.Max(0.05f, 0.55f + (float)Math.Sin(angle) * 0.65f);
        float length = ExitOvershootFraction * height;
        exitX = bodyX + dx * length;
        exitY = bodyY + dy * length;
    }

    // The flight progress without the ease: the body crossing has to land while the glyph is still fully
    // opaque, so the path uses this while the size keeps using ApproachT.
    internal static float FlightProgress(float age, float lifetime)
    {
        float window = Math.Max(0.01f, lifetime * ApproachWindowFraction);
        return Unit(age / window);
    }

    // Two-leg path: start -> body point -> exit. Continuous at the crossing, NaN safe.
    internal static float SegmentedAxis(float start, float middle, float end, float t)
    {
        if (!KillPolicy.IsFinite(start) || !KillPolicy.IsFinite(middle) || !KillPolicy.IsFinite(end)) return start;
        float progress = Unit(t);
        if (progress <= BodyCrossFraction)
        {
            float first = BodyCrossFraction <= 0f ? 1f : progress / BodyCrossFraction;
            return start + (middle - start) * first;
        }
        float second = (progress - BodyCrossFraction) / Math.Max(0.0001f, 1f - BodyCrossFraction);
        return middle + (end - middle) * second;
    }

    internal static float ApproachT(float age, float lifetime)
    {
        float window = Math.Max(0.01f, lifetime * ApproachWindowFraction);
        float t = Unit(age / window);
        if (t <= 0f) return 0f;
        if (t >= 1f) return 1f;
        return (float)Math.Pow(t, ApproachEasePower);
    }

    internal static float ApproachAxis(float start, float target, float t)
    {
        if (!KillPolicy.IsFinite(start) || !KillPolicy.IsFinite(target)) return start;
        return start + (target - start) * Unit(t);
    }

    internal static float ApproachScale(float t) => 1f + ApproachGrowth * Unit(t);

    // Prismatic numbers grow much more on the way in (1.0 -> 2.45x instead of 1.45x).
    internal static float PrismaticApproachScale(float t) => 1f + PrismaticApproachGrowth * Unit(t);

    // The prismatic charge uses its own (flatter) ease over the same lifetime window, so at every opaque
    // sample a prismatic number is further along its flight than 1.2.15: a bigger glyph and a wider
    // bearing. ApproachT stays the shared plain-number curve - nothing else calls this one.
    internal static float PrismaticApproachT(float age, float lifetime)
    {
        float window = Math.Max(0.01f, lifetime * ApproachWindowFraction);
        float t = Unit(age / window);
        if (t <= 0f) return 0f;
        if (t >= 1f) return 1f;
        return (float)Math.Pow(t, PrismaticApproachEasePower);
    }

    // Prismatic-only lateral swoop, in pixels: the seeded bearing picks the side, the magnitude is between
    // PrismaticSwingFloor and 1.0 of the cap, and sin(pi * t) makes it exactly zero at spawn and at
    // arrival. Bounded by construction, so a prismatic number can never accumulate an unbounded offset.
    internal static float PrismaticSwing(int seed, float approach, float resolution)
    {
        float t = Unit(approach);
        if (t <= 0f || t >= 1f) return 0f;
        if (!KillPolicy.IsFinite(resolution) || resolution <= 0f) return 0f;
        float jitter = FlyJitter(seed);
        float magnitude = PrismaticSwingFloor + (1f - PrismaticSwingFloor) * Math.Abs(jitter);
        float sign = jitter < 0f ? -1f : 1f;
        return sign * magnitude * PrismaticSwingPixels * resolution * (float)Math.Sin(Math.PI * t);
    }

    // Numbers settle on a shallow arc around the reticle instead of one single point: a stack of
    // simultaneous hits would otherwise pile up into an unreadable blob once they grow. The slot comes
    // from the number's own Side offset, so the layout is stable for the whole flight.
    internal static float ApproachTargetX(float centreX, float side, float resolution, bool prismatic = false)
    {
        if (!KillPolicy.IsFinite(centreX) || !KillPolicy.IsFinite(side) || !KillPolicy.IsFinite(resolution)) return centreX;
        float fan = prismatic ? PrismaticApproachFan : ApproachFanFraction;
        return centreX + side * fan * resolution;
    }

    internal static float ApproachTargetY(float centreY, float side, float resolution, bool prismatic = false)
    {
        if (!KillPolicy.IsFinite(centreY) || !KillPolicy.IsFinite(side) || !KillPolicy.IsFinite(resolution)) return centreY;
        float risePixels = prismatic ? PrismaticApproachRisePixels : ApproachRisePixels;
        float riseFraction = prismatic ? PrismaticApproachRiseFraction : ApproachRiseFraction;
        return centreY - (risePixels + Math.Abs(side) * riseFraction) * resolution;
    }
    // ---- one damage number, placed (review A5) ------------------------------------------------
    // The whole screen placement of a single number lives here, and DamageNumbers.Draw is its only caller.
    // The locked tests drive this same function with explicit inputs (seed, hit point, opacity, resolution,
    // base size), so a wrong or forgotten argument in the production call changes the asserted numbers
    // instead of silently passing against a test-side copy of the formula.
    //   side  - the number's own lateral slot, derived from its sequence by NumberSide (stable per number);
    //   seed  - the number's flight seed (different seeds must fly differently);
    //   width/height - the screen the number is drawn on; resolution - the 1080p scale factor.
    internal static float NumberSide(int sequence) => ((sequence % 7) - 3) * 11f;

    internal static float NumberDriftX(float hitX, float side, float age, float resolution)
        => hitX + side * (0.35f + age * 1.7f) * resolution;

    internal static float NumberDriftY(float hitY, float age, float resolution)
        => hitY + (-95f * age + 105f * age * age) * resolution;

    //   approach - DamageNumbers/ApproachEffect: false (the 1.2.15 default) keeps the number on its drift
    //              path and never grows it; true flies it at the viewer (prismatic dive or body crossing).
    //   prismatic - the prismatic decision including DamageFx/PrismaticEnabled, so the foil and the flight
    //              shape both disappear when the switch is off.
    internal static void NumberPose(bool prismatic, bool approach, int seed, float side, float age, float lifetime, float opacity,
        float hitX, float hitY, float width, float height, float resolution, HitKind kind, float baseSize,
        out float x, out float y, out float size, out float alpha, out float straightX, out float straightY)
    {
        float driftX = NumberDriftX(hitX, side, age, resolution);
        float driftY = NumberDriftY(hitY, age, resolution);
        // Alpha is the existing fade multiplied by the opacity exactly once, on every path (review A5).
        alpha = NumberAlpha(age, lifetime, opacity);
        // DamageNumbers/ApproachEffect = false (default): the first shipped trajectory, value for value.
        // The glyph spawns on the projected hit point, drifts up with NumberDriftX/Y and fades; it neither
        // flies at the viewer nor grows, and a prismatic number only keeps its foil LOOK (the material is
        // decided by the caller), never the prismatic approach scale or the seeded swoop.
        if (!approach)
        {
            x = driftX;
            y = driftY;
            size = Clamp(baseSize, 16f, 42f, 27f) * NumberScale(age, kind) * resolution;
            // There is no separate reference line when nothing flies along one: the glyph IS its own point.
            straightX = driftX;
            straightY = driftY;
            return;
        }
        // Prismatic numbers charge on their own (flatter) ease and a stronger growth curve; every other
        // number keeps the shared ApproachT curve, so its flight is untouched.
        float flight = prismatic ? PrismaticApproachT(age, lifetime) : ApproachT(age, lifetime);
        size = Clamp(baseSize, 16f, 42f, 27f)
            * NumberScale(age, kind)
            * (prismatic ? PrismaticApproachScale(flight) : ApproachScale(flight))
            * resolution;
        if (prismatic)
        {
            // One shared destination, a seeded bearing that opens with the flight, and a seeded, bounded
            // lateral swoop on top of it.
            float targetX = ApproachTargetX(width * 0.5f, side, resolution, true);
            float targetY = ApproachTargetY(height * 0.5f, side, resolution, true);
            PrismaticFlightPoint(seed, flight, driftX, driftY, targetX, targetY, resolution,
                out x, out y, out straightX, out straightY);
        }
        else
        {
            // A random line that crosses the body slightly below the eyes and keeps going past it.
            FlightTarget(width * 0.5f, height * 0.5f, height, seed,
                out float bodyX, out float bodyY, out float exitX, out float exitY);
            float progress = FlightProgress(age, lifetime);
            x = SegmentedAxis(driftX, bodyX, exitX, progress);
            y = SegmentedAxis(driftY, bodyY, exitY, progress);
            // A plain number has no extra lateral deviation: its reference line is the body point it
            // was aimed through, which keeps the measurement honest for both branches.
            straightX = bodyX;
            straightY = bodyY;
        }
    }

    internal static float CardWidth(float iconWidth, float textWidth, float scale, float maximumWidth)
    {
        float max = Clamp(maximumWidth, 0f, 10000f, 0f);
        float fixedWidth = Math.Max(0f, iconWidth) + 30f * Clamp(scale, 0.5f, 2f, 1f);
        float requested = fixedWidth + Math.Max(0f, Clamp(textWidth, 0f, 10000f, 0f));
        return Math.Max(0f, Math.Min(max, requested));
    }
    internal static float CenteredCardX(float screenWidth, float cardWidth, float offset, float margin = 12f)
    {
        float screen = Clamp(screenWidth, 1f, 100000f, 1f);
        float edge = Clamp(margin, 0f, screen * 0.5f, 0f);
        float width = Clamp(cardWidth, 0f, Math.Max(0f, screen - 2f * edge), Math.Max(0f, screen - 2f * edge));
        float centered = (screen - width) * 0.5f;
        return Clamp(centered + offset, edge, Math.Max(edge, screen - width - edge), centered);
    }
    internal static string CardDetail(KillCard card, bool distance, bool health)
    {
        string prefix = distance ? card.DistanceText + "   /   " : string.Empty;
        if (health) prefix += card.HealthText + "   /   ";
        return prefix + card.EnemyName;
    }
}

internal readonly struct CorpseBurstPlan
{
    internal readonly int Bursts;
    internal readonly bool Sound;

    internal CorpseBurstPlan(int bursts, bool sound)
    {
        Bursts = bursts;
        Sound = sound;
    }
}

internal sealed class CorpseSoundThrottle
{
    private float _nextAllowedAt;
    private bool _pending;
    internal bool HasPending => _pending;
    internal bool Request(float now)
    {
        if (!KillPolicy.IsFinite(now)) return false;
        if (_pending || now < _nextAllowedAt) { _pending = true; return false; }
        _pending = false;
        _nextAllowedAt = now + FeedbackPolicy.CorpseSoundInterval;
        return true;
    }
    internal bool TryFlush(float now)
    {
        if (!_pending || !KillPolicy.IsFinite(now) || now < _nextAllowedAt) return false;
        _pending = false;
        _nextAllowedAt = now + FeedbackPolicy.CorpseSoundInterval;
        return true;
    }
    internal void Reset() { _nextAllowedAt = 0f; _pending = false; }
}
// One ledger per level: `_scheduled` is "this death is being handled right now", `_completed` is "this death
// was already handled to the end". 1.2.18 (R2): the real log showed sid1 @377.12 s -> hidden=restored
// (enemy-despawn) -> sid3 with the SAME identity @385.48 s from EnemyAgent.OnDead. Restore used to call
// Forget() unconditionally, which erased the identity the moment the corpse was released, so the pooled
// wrapper's second death callback started a whole new schedule (burst + dismember + cleanup) for a corpse
// that had already been handled. A finished death now stays finished until the wrapper is really REUSED
// (EnemyAgent.Setup = a new living enemy on the same object) or the level ends - not for an arbitrary TTL.
internal sealed class CorpseDeathLedger
{
    // Bounded so a long level cannot grow this without limit; the oldest finished identity is dropped first.
    internal const int MaxCompleted = 512;
    private readonly HashSet<long> _scheduled = new();
    // identity -> the close stamp that is still valid for it. Closing the same identity again REPLACES its stamp,
    // so the queue entry of the previous close becomes provably stale instead of lingering as a second live copy
    // (1.2.18 shipped a HashSet + Queue of bare ids: after Unlock the old id stayed in the queue, and a later
    // eviction could drop the CURRENT entry of that identity while the queue kept growing).
    private readonly Dictionary<long, long> _completed = new();
    private readonly Queue<(long Identity, long Stamp)> _order = new();
    private long _stamp;
    private bool _inLevel;
    internal int Count => _scheduled.Count;
    internal int CompletedCount => _completed.Count;
    internal int OrderCount => _order.Count;
    internal bool HasCompleted(long identity) => identity != 0 && _completed.ContainsKey(identity);
    internal bool InLevel => _inLevel;
    internal void EnterLevel() => _inLevel = true;
    internal bool TrySchedule(long identity)
        => _inLevel && identity != 0 && !_scheduled.Contains(identity) && !_completed.ContainsKey(identity)
            && _scheduled.Add(identity);
    // The death has been seen through (applied, unavailable, or removed): block a re-schedule until Unlock.
    internal void Close(long identity)
    {
        if (identity == 0) return;
        _scheduled.Remove(identity);
        long stamp = ++_stamp;
        _completed[identity] = stamp;
        _order.Enqueue((identity, stamp));
        while (_completed.Count > MaxCompleted && _order.Count > 0)
        {
            var oldest = _order.Dequeue();
            // A stale entry must never evict the CURRENT entry of that identity.
            if (_completed.TryGetValue(oldest.Identity, out long current) && current == oldest.Stamp)
                _completed.Remove(oldest.Identity);
        }
        Compact();
    }
    // Every Close enqueues, so an identity that is closed and unlocked hundreds of times would grow the queue
    // while `_completed` stays bounded. Rebuild from the live stamps once the queue passes twice the cap: at that
    // point the queue holds at most MaxCompleted * 2 + 1 entries, i.e. a stable bound per corpse lifecycle.
    private void Compact()
    {
        if (_order.Count <= MaxCompleted * 2) return;
        var live = new List<(long Identity, long Stamp)>(_completed.Count);
        foreach (var pair in _completed) live.Add((pair.Key, pair.Value));
        live.Sort((left, right) => left.Stamp.CompareTo(right.Stamp));
        _order.Clear();
        for (int i = 0; i < live.Count; i++) _order.Enqueue(live[i]);
    }
    // The identity may die again: a genuinely reused wrapper (EnemyAgent.Setup) or a schedule that failed
    // before it ever became a corpse. This is the ONLY way back into TrySchedule.
    internal void Unlock(long identity)
    {
        if (identity == 0) return;
        _scheduled.Remove(identity);
        _completed.Remove(identity);
    }
    internal void Reset() { _scheduled.Clear(); _completed.Clear(); _order.Clear(); _stamp = 0; _inLevel = false; }
}

internal sealed class BurstHistory
{
    private readonly Dictionary<long, Queue<(float Time, float Damage)>> _entries = new();
    internal void Add(long id, float now, float damage)
    {
        if (id == 0 || !KillPolicy.IsFinite(now) || !KillPolicy.IsFinite(damage) || damage <= 0) return;
        if (!_entries.TryGetValue(id, out var q))
        {
            if (_entries.Count >= 256) Prune(now);
            if (_entries.Count >= 256) return;
            _entries[id] = q = new Queue<(float, float)>();
        }
        while (q.Count > 0 && now - q.Peek().Time > FeedbackPolicy.BurstWindow) q.Dequeue();
        if (q.Count >= 96) q.Dequeue();
        q.Enqueue((now, damage));
    }
    internal float Sum(long id, float now)
    {
        if (!_entries.TryGetValue(id, out var q)) return 0;
        while (q.Count > 0 && now - q.Peek().Time > FeedbackPolicy.BurstWindow) q.Dequeue();
        float sum = 0; foreach (var hit in q) sum += hit.Damage;
        return sum;
    }
    internal void Prune(float now)
    {
        var dead = new List<long>();
        foreach (var pair in _entries)
        {
            var q = pair.Value;
            while (q.Count > 0 && now - q.Peek().Time > FeedbackPolicy.BurstWindow) q.Dequeue();
            if (q.Count == 0) dead.Add(pair.Key);
        }
        foreach (var id in dead) _entries.Remove(id);
    }
    internal void Remove(long id) => _entries.Remove(id);
    internal void Clear() => _entries.Clear();
}

public sealed class EnemyNameRule
{
    public uint DataBlockId { get; set; }
    public string RawName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

internal static class EnemyNamePolicy
{
    internal static string Resolve(uint id, string rawName, IReadOnlyList<EnemyNameRule> rules, IReadOnlyDictionary<uint, string> defaults = null)
    {
        rawName = string.IsNullOrWhiteSpace(rawName) ? "Unknown" : rawName;
        rules ??= Array.Empty<EnemyNameRule>();
        foreach (var rule in rules)
            if (Valid(rule) && rule.DataBlockId == id && rule.DataBlockId != 0
                && string.Equals(rule.RawName, rawName, StringComparison.Ordinal)) return rule.DisplayName;
        foreach (var rule in rules)
            if (Valid(rule) && rule.DataBlockId == 0 && string.Equals(rule.RawName, rawName, StringComparison.Ordinal)) return rule.DisplayName;
        return defaults != null && defaults.TryGetValue(id, out string name) && !string.IsNullOrWhiteSpace(name) ? name : rawName;
    }
    internal static bool Valid(EnemyNameRule rule)
        => rule != null && !string.IsNullOrWhiteSpace(rule.RawName) && !string.IsNullOrWhiteSpace(rule.DisplayName)
            && rule.RawName.Length <= 256 && rule.DisplayName.Length <= 96 && !rule.DisplayName.Contains('\n') && !rule.DisplayName.Contains('\r');
}

// Bounded diagnostic counter. Focus logging and error warnings must never share one budget: enabling the
// LogFocus debug switch previously consumed _warnings, which silently disabled every [BAR] exception warning
// after five focus transitions. Each call site keeps its own instance.
internal sealed class DiagnosticBudget
{
    private int _used;
    internal int Used => _used;
    internal bool TryUse(int limit) => limit > 0 && _used++ < limit;
    // F6 (1.2.19 review): a level change / plugin reload must restore the allowance, otherwise one long session
    // silently loses every later receipt. The volley diagnostics depend on this (see ShotgunDamageNumberCapture).
    internal void Reset() => _used = 0;
}

// Per-reason log tally. The real logs contained 163 `schedule=skipped;reason=duplicate-death-callback`
// and 43 `hidden=pruned;reason=enemy-null` lines per session: both are EXPECTED (InstantDead + OnDead +
// lethal-damage all schedule the same death, and a pooled enemy is destroyed after its corpse finished),
// so the first few are logged verbatim and the rest are folded into a running count instead of being
// re-printed forever. Nothing about the de-duplication itself changes - only how loud it is.
internal sealed class LogTally
{
    private readonly Dictionary<string, int> _counts = new Dictionary<string, int>(StringComparer.Ordinal);
    internal int Total { get; private set; }
    internal int Count(string reason) => reason != null && _counts.TryGetValue(reason, out int count) ? count : 0;
    // Records one occurrence and returns how many times this reason has been seen so far (>= 1).
    internal int Add(string reason)
    {
        string key = string.IsNullOrEmpty(reason) ? "unknown" : reason;
        _counts.TryGetValue(key, out int count);
        _counts[key] = ++count;
        Total++;
        return count;
    }
    // The first `loud` occurrences and every `stride`-th one after that are logged; everything between is
    // counted silently. `loud`/`stride` come from the caller so each call site keeps its own policy.
    internal static bool ShouldLog(int seen, int loud, int stride)
        => seen > 0 && (seen <= loud || (stride > 0 && seen % stride == 0));
    internal void Reset() { _counts.Clear(); Total = 0; }
}
