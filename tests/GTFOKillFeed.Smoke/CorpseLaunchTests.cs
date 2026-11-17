using System;
using HawChow.GTFOKillFeed;

// The corpse launch budget (1.2.15 clamps 3.8 -> 12, and the fixed 4.2-8.6 m/s speed meant the number was
// never observable). 1.2.16 accepts 0.5-30 m, inverts the budget into a horizontal speed through drag and
// air time, and keeps the 40 m absolute ceiling + a budget-derived velocity cap. Everything asserted here is
// production code: the same functions CorpseFeedback and CorpseVisualLease call.
internal static class CorpseLaunchTests
{
    private static readonly float[] Budgets = { 0.5f, 3.8f, 8.5f, 12f, 18f, 30f };
    private static readonly float[] Strengths = { 0f, 0.25f, 0.5f, 0.75f, 1f };

    internal static void Run(Action<bool, string> check)
    {
        // ---- the configured values are the budget, at every tier the report asked for -----------------
        bool budgetHonoured = true;
        foreach (float budget in Budgets)
            foreach (float strength in Strengths)
                budgetHonoured &= Math.Abs(FeedbackPolicy.LaunchBudget(strength, budget, budget) - budget) < 0.0001f;
        check(budgetHonoured,
            "corpse launch: a MinLaunchDistance of 0.5/3.8/8.5/12/18/30 m is used as written at every burst strength (no 12 m floor any more)");

        // The real-machine cfg: Min 3.8, Max 8.5. 1.2.15 produced lo == hi == 12 for it, i.e. the two
        // settings and the burst damage had no effect on the launch at all.
        check(Math.Abs(FeedbackPolicy.LaunchBudget(0f, 3.8f, 8.5f) - 3.8f) < 0.0001f
            && Math.Abs(FeedbackPolicy.LaunchBudget(1f, 3.8f, 8.5f) - 8.5f) < 0.0001f
            && Math.Abs(FeedbackPolicy.LaunchBudget(0.5f, 3.8f, 8.5f) - 6.15f) < 0.001f,
            "corpse launch: the reported cfg (3.8 / 8.5) spans 3.8-8.5 m and burst strength really interpolates");

        bool monotonic = true;
        foreach (float budget in Budgets)
            for (int i = 1; i < Strengths.Length; i++)
                monotonic &= FeedbackPolicy.LaunchBudget(Strengths[i], budget, budget + 4f)
                    >= FeedbackPolicy.LaunchBudget(Strengths[i - 1], budget, budget + 4f);
        check(monotonic, "corpse launch: the budget is weakly monotonic in burst strength at every configured tier");

        // ---- the budget drives the SPEED, and the forward model lands on the budget -------------------
        bool landsOnBudget = true;
        foreach (float budget in Budgets)
            foreach (float strength in Strengths)
            {
                float speed = FeedbackPolicy.LaunchSpeedFor(budget, strength);
                float distance = FeedbackPolicy.LaunchDistance(speed, strength);
                landsOnBudget &= Math.Abs(distance - budget) < 0.05f;
                landsOnBudget &= speed > 0f && speed <= FeedbackPolicy.MaxLaunchSpeed;
            }
        check(landsOnBudget,
            "corpse launch: the derived speed (drag + air time inverted) puts a standard body's landing estimate on the configured budget for all 6 tiers x 5 strengths");

        // Spot values, so a silent retune of the model shows up here: budget -> speed at mid strength.
        check(Math.Abs(FeedbackPolicy.LaunchSpeedFor(3.8f, 0.5f) - 4.80f) < 0.02f
            && Math.Abs(FeedbackPolicy.LaunchSpeedFor(8.5f, 0.5f) - 10.75f) < 0.03f
            && Math.Abs(FeedbackPolicy.LaunchSpeedFor(12f, 0.5f) - 15.17f) < 0.05f,
            "corpse launch: 3.8 / 8.5 / 12 m map onto 4.80 / 10.75 / 15.17 m/s at mid burst strength (the inverse of drag 0.8 and a 0.71 s air time)");

        // 1.2.15 was always 4.2-8.6 m/s: the new speed has to leave that band for budgets the old clamp
        // used to flatten, and it still has to be a physical launch, not a teleport.
        check(FeedbackPolicy.LaunchSpeedFor(3.8f, 0f) > 4.2f
            && FeedbackPolicy.LaunchSpeedFor(30f, 1f) < FeedbackPolicy.MaxLaunchSpeed + 0.001f,
            "corpse launch: a small budget is a gentler launch than 1.2.15's fixed 4.2 m/s and the largest one stays under the 42 m/s safety cap");

        // ---- big bodies keep their bias, and both ends stay bounded ---------------------------------
        check(FeedbackPolicy.LaunchSpeedFor(FeedbackPolicy.Travel(1f, 3.8f, 8.5f, 2.4f), 1f)
            > FeedbackPolicy.LaunchSpeedFor(FeedbackPolicy.Travel(1f, 3.8f, 8.5f, 1f), 1f),
            "corpse launch: a big body's TravelBias (1.35 at size factor 2.4) still launches it further than a standard body");
        check(FeedbackPolicy.Travel(100f, 0.5f, 0.5f, 0.7f) > 0.4f
            && FeedbackPolicy.Travel(100f, 30f, 30f, 2.4f) <= 40.5f,
            "corpse launch: both extremes stay sane (a 0.7 size factor biases a 0.5 m budget to 0.46 m, a 2.4 one lifts 30 m to 40.5 m before the ceiling clamp)");

        float largest = FeedbackPolicy.Travel(1f, 30f, 30f, 2.4f) * 1.5f;
        check(FeedbackPolicy.LaunchDistance(FeedbackPolicy.LaunchSpeedFor(largest, 1f), 1f) <= FeedbackPolicy.AbsoluteDistanceCeiling + 0.001f,
            "corpse launch: the 40 m absolute ceiling still bounds the very largest cfg (30 m x 1.35 bias x 1.5 TravelScale)");

        // ---- the 9 m/s hard cap no longer flattens the budget ---------------------------------------
        check(Math.Abs(FeedbackPolicy.LaunchVelocityCap(0f) - FeedbackPolicy.LegacyVelocityCap) < 0.0001f,
            "corpse launch: the per-body velocity cap never goes below the legacy 9 m/s (nothing got weaker)");
        check(FeedbackPolicy.LaunchVelocityCap(22.76f) > 9f && FeedbackPolicy.LaunchVelocityCap(22.76f) < 30f,
            "corpse launch: the velocity cap follows the applied speed, so a 18 m budget is not cut back to 9 m/s");
        check(FeedbackPolicy.LaunchVelocityCap(1000f) <= FeedbackPolicy.MaxLaunchSpeed * FeedbackPolicy.LaunchVelocitySlack + 0.001f,
            "corpse launch: the velocity cap itself stays bounded by the absolute speed cap");

        check(!FeedbackPolicy.LaunchSpeedCapped(30f, 0f) && !FeedbackPolicy.LaunchSpeedCapped(30f, 0.5f),
            "corpse launch: every configured budget up to 30 m is reachable, so the speed cap is not silently doing the clamping");
        check(FeedbackPolicy.LaunchSpeedCapped(40f, 0.5f)
            && Math.Abs(FeedbackPolicy.LaunchSpeedFor(40f, 0.5f) - FeedbackPolicy.MaxLaunchSpeed) < 0.001f,
            "corpse launch: only the 40 m absolute ceiling trips the safety cap, and it is reported (capped=speed) instead of being silent");
        check(FeedbackPolicy.Travel(100f, 0.5f, 0.5f, 0.7f) > 0.4f
            && FeedbackPolicy.Travel(100f, 30f, 30f, 2.4f) <= 41f,
            "corpse launch: even at both extremes the budget stays near 0.5-40 m (a 0.7 size factor biases a 0.5 m budget to 0.46 m, a 2.4 one lifts 30 m to 40.5 m)");
        check(FeedbackPolicy.Travel(100f, 30f, 30f, 2.4f) <= 40.5f
            && FeedbackPolicy.Travel(100f, 30f, 30f, 2.4f) > 40f,
            "corpse launch: the largest combination really does reach the absolute ceiling instead of being clamped elsewhere");

        // ---- C2: the position clamp is a safety net, never a leash on the body ------------------------
        // The radius must never be smaller than the corpse: a natural fall-over (0.6-1.5 m) inside a 0.625 m
        // sphere used to be snapped back onto the sphere with its velocity zeroed, i.e. pinned mid-air.
        check(Math.Abs(FeedbackPolicy.TravelClampFloor(0.7f) - FeedbackPolicy.MinTravelClampMetres) < 0.0001f
            && Math.Abs(FeedbackPolicy.TravelClampFloor(2.4f) - FeedbackPolicy.MinTravelClampMetres * 2.4f) < 0.0001f,
            "corpse clamp (C2): the safety radius floor is 2.5 m for a small body and scales to 6.0 m for a 2.4 body");
        check(FeedbackPolicy.AppliedTravel(0.35f, 0.7f) >= FeedbackPolicy.MinTravelClampMetres
            && FeedbackPolicy.AppliedTravel(0.5f, 2.4f) >= FeedbackPolicy.MinTravelClampMetres * 2.4f,
            "corpse clamp (C2): even the smallest legal budget keeps a radius of at least the body size, never 0.625 m");
        check(!FeedbackPolicy.ShouldClampTravel(1.5f * 1.5f, FeedbackPolicy.AppliedTravel(0.35f, 0.7f))
            && !FeedbackPolicy.ShouldClampTravel(1.9f * 1.9f, FeedbackPolicy.AppliedTravel(0.35f, 0.7f)),
            "corpse clamp (C2): a body that merely falls over (1.5-1.9 m from its captured origin) is NOT snapped back and its velocity is NOT zeroed");
        check(FeedbackPolicy.ShouldClampTravel(3.1f * 3.1f, FeedbackPolicy.AppliedTravel(0.35f, 0.7f)),
            "corpse clamp (C2): the snap-back still catches a body outside the (body-sized) safety radius");
        check(FeedbackPolicy.AppliedTravel(3.8f) > 3.8f && FeedbackPolicy.AppliedTravel(3.8f) < 6f
            && FeedbackPolicy.AppliedTravel(1000f) <= FeedbackPolicy.AbsoluteDistanceCeiling + 0.001f,
            "corpse clamp (C2): a 3.8 m budget keeps ~4.75 m of slack while the absolute ceiling stays 40 m");

        // ---- C3: a fast body must not tunnel through the level ---------------------------------------
        // One 20 ms step at the 30 m budget speed (36.7 m/s) is 0.73 m, i.e. thicker than a wall. Above 0.25 m
        // per step the flight switches to ContinuousDynamic; the legacy 4.2-8.6 m/s band stays Discrete.
        check(!FeedbackPolicy.NeedsContinuousCollision(4.2f) && !FeedbackPolicy.NeedsContinuousCollision(8.6f)
            && !FeedbackPolicy.NeedsContinuousCollision(12f),
            "corpse collision (C3): the legacy 4.2-8.6 m/s launches keep the stock Discrete mode (no behaviour change)");
        check(FeedbackPolicy.NeedsContinuousCollision(FeedbackPolicy.LaunchSpeedFor(30f, 0.5f))
            && FeedbackPolicy.NeedsContinuousCollision(FeedbackPolicy.LaunchSpeedFor(18f, 0.5f)),
            "corpse collision (C3): an 18 m / 30 m budget (22.8 / 37.9 m/s) really does need ContinuousDynamic");
        check(Math.Abs(FeedbackPolicy.LaunchSpeedFor(30f, 0.5f) * FeedbackPolicy.CorpseFixedTimeStep - 0.758f) < 0.02f
            && FeedbackPolicy.LaunchSpeedFor(30f, 0.5f) * FeedbackPolicy.CorpseFixedTimeStep > FeedbackPolicy.MaxDiscreteTravelPerStep,
            "corpse collision (C3): the measured step is ~0.76 m against a 0.25 m Discrete budget - the switch is not cosmetic");
        check(!FeedbackPolicy.NeedsContinuousCollision(float.NaN) && !FeedbackPolicy.NeedsContinuousCollision(-5f),
            "corpse collision (C3): a broken or negative speed never forces continuous collision");

        // ---- C1 + 1.2.16: the late ragdoll lookup outlives the burst point by a grace window ----------
        // The game spawns the ragdoll from a coroutine, so the whole point of the retry is to survive past
        // the burst; the first 1.2.16 build still ended it at burstAt, which is why the real machine logged
        // 28 give-ups with attempts=0 and applied nothing.
        check(!FeedbackPolicy.ShouldRetryRagdollLookup(true, 0, 0.05f, 0.1f),
            "corpse retry (C1): a corpse that already has its ragdoll never spends a lookup");
        check(FeedbackPolicy.ShouldRetryRagdollLookup(false, 0, 0.05f, 0.1f)
            && !FeedbackPolicy.ShouldRetryRagdollLookup(false, 3, 0.08f, 0.1f)
            && FeedbackPolicy.ShouldRetryRagdollLookup(false, 3, 0.1f, 0.1f)
            && FeedbackPolicy.ShouldRetryRagdollLookup(false, 4, 0.115f, 0.1f),
            "corpse retry (R1): a probe is refused until its own due age (so 60/144 fps cannot burn the budget), and the burst point itself is still inside the schedule");
        float deadline = FeedbackPolicy.RagdollLookupDeadline(0.1f);
        check(Math.Abs(deadline - (0.1f + FeedbackPolicy.RagdollLookupGrace)) < 0.0001f
            && Math.Abs(FeedbackPolicy.RagdollLookupGrace - 0.3f) < 0.0001f,
            "corpse retry (1.2.16): the lookup window really is burst + a pinned 0.30 s grace, so a ragdoll spawned a few frames late is caught");
        // R1 (1.2.18): the schedule reaches the deadline at every frame rate, and one cross-deadline probe
        // is still allowed. 1.2.16 spent a fixed 8-attempt budget per FRAME, which at 144 fps ended the whole
        // lookup in ~0.055 s - before the only launch the real log ever applied (0.21-0.27 s).
        check(!FeedbackPolicy.ShouldRetryRagdollLookup(false, FeedbackPolicy.MaxRagdollLookupAttempts, deadline - 0.001f, 0.1f)
            && FeedbackPolicy.ShouldRetryRagdollLookup(false, FeedbackPolicy.MaxRagdollLookupAttempts, deadline, 0.1f)
            && FeedbackPolicy.ShouldRetryRagdollLookup(false, FeedbackPolicy.MaxRagdollLookupAttempts, deadline + 0.001f, 0.1f)
            && !FeedbackPolicy.ShouldRetryRagdollLookup(false, FeedbackPolicy.MaxRagdollLookupAttempts + 1, deadline + 0.001f, 0.1f),
            "corpse retry (R1): the last scheduled probe sits exactly on burst + grace and one more probe is allowed just after it, so a hitch that lands on the deadline cannot swallow the final check");
        check(FeedbackPolicy.ShouldRetryRagdollLookup(false, 0, FeedbackPolicy.RagdollLookupAttemptAge(0, 0.1f) - 0.001f, 0.1f) == false
            && FeedbackPolicy.ShouldRetryRagdollLookup(false, 0, FeedbackPolicy.RagdollLookupAttemptAge(0, 0.1f), 0.1f)
            && Math.Abs(FeedbackPolicy.RagdollLookupAttemptAge(0, 0.1f) - FeedbackPolicy.FirstLaunchAttemptAge) < 0.0001f
            && Math.Abs(FeedbackPolicy.RagdollLookupAttemptAge(FeedbackPolicy.MaxRagdollLookupAttempts, 0.1f) - deadline) < 0.0001f,
            "corpse retry (R1): attempt k is paced by TIME - it is refused before its due age, the first point is 0.02 s and the last point is the deadline");
        check(!FeedbackPolicy.ShouldRetryRagdollLookup(false, 0, float.NaN, 0.1f)
            && !FeedbackPolicy.ShouldRetryRagdollLookup(false, -1, 0.05f, 0.1f)
            && !FeedbackPolicy.ShouldRetryRagdollLookup(true, 0, 0.05f, 0.1f),
            "corpse retry (R1): a broken clock, a negative attempt count or an existing ragdoll never starts a lookup");
        // The give-up decision is a separate predicate and can never fire while the schedule has probes left.
        check(!FeedbackPolicy.ShouldAbandonRagdollLookup(false, 0, 0.05f, 0.1f)
            && !FeedbackPolicy.ShouldAbandonRagdollLookup(false, FeedbackPolicy.MaxRagdollLookupAttempts, deadline + 0.04f, 0.1f)
            && FeedbackPolicy.ShouldAbandonRagdollLookup(false, FeedbackPolicy.MaxRagdollLookupAttempts, deadline + 0.06f, 0.1f)
            && FeedbackPolicy.ShouldAbandonRagdollLookup(false, FeedbackPolicy.MaxRagdollLookupAttempts + 1, deadline, 0.1f)
            && !FeedbackPolicy.ShouldAbandonRagdollLookup(true, 99, 0.1f, 0.1f),
            "corpse retry (R1): the give-up receipt is gated on the spent schedule plus the final-probe slack, never on the deadline alone");
        // Coverage at three frame rates plus one 90 ms hitch: every run must probe the END of the window.
        float ProbeWindow(int fps, float hitchAt, float hitchLength, out int probes, out bool abandonedMidWindow)
        {
            int spent = 0;
            float last = -1f;
            bool abandoned = false;
            float frame = 1f / fps;
            for (float age = 0f; age <= 0.9f; age += frame)
            {
                if (age >= hitchAt && age < hitchAt + hitchLength) continue;
                if (FeedbackPolicy.ShouldRetryRagdollLookup(false, spent, age, 0.1f))
                {
                    spent++;
                    last = age;
                }
                if (FeedbackPolicy.ShouldAbandonRagdollLookup(false, spent, age, 0.1f)
                    && age < FeedbackPolicy.RagdollLookupDeadline(0.1f)) abandoned = true;
            }
            probes = spent;
            abandonedMidWindow = abandoned;
            return last;
        }
        float last144 = ProbeWindow(144, -1f, 0f, out int probes144, out bool abandoned144);
        float last60 = ProbeWindow(60, -1f, 0f, out int probes60, out bool abandoned60);
        float last30 = ProbeWindow(30, -1f, 0f, out int probes30, out bool abandoned30);
        check(!abandoned144 && !abandoned60 && !abandoned30
            && probes144 <= FeedbackPolicy.MaxRagdollLookupAttempts + 1
            && probes60 <= FeedbackPolicy.MaxRagdollLookupAttempts + 1
            && probes30 <= FeedbackPolicy.MaxRagdollLookupAttempts + 1,
            "corpse retry (R1): no frame rate abandons inside the window and none exceeds the bounded schedule (144fps=" + probes144 + ", 60fps=" + probes60 + ", 30fps=" + probes30 + ")");
        check(last144 >= deadline - 0.03f && last60 >= deadline - 0.03f && last30 >= deadline - 0.03f,
            "corpse retry (R1): the last probe really lands at the end of the window at 144/60/30 fps (144fps=" + last144 + ", 60fps=" + last60 + ", 30fps=" + last30 + ")");
        float lastHitch = ProbeWindow(60, 0.05f, 0.09f, out int probesHitch, out bool abandonedHitch);
        check(!abandonedHitch && lastHitch >= deadline - 0.03f,
            "corpse retry (R1): a 90 ms hitch right after the first probe delays it, but the schedule still reaches the end of the window (last=" + lastHitch + ")");
        check(Math.Abs(FeedbackPolicy.FirstLaunchAttemptAge - 0.02f) < 0.0001f,
            "corpse retry (R1): the launch is still attempted immediately (0.02 s = the second 60 fps frame)");
        // The real-machine regression: one 90 ms hitch between two frames. The old burst-bound window had
        // already closed (age 0.13 > burstAt 0.1); the decoupled window still retries.
        check(!FeedbackPolicy.ShouldWaitForRagdoll(0.13f, 0.1f) && FeedbackPolicy.ShouldRetryRagdollLookup(false, 0, 0.13f, 0.1f),
            "corpse retry (1.2.16): a single 90 ms frame hitch no longer closes the lookup window (the 28 attempts=0 give-ups in the real log)");

        // ---- 1.2.16 (second real-machine pass): BodyCount == 0 must never fall through both branches ------
        check(FeedbackPolicy.ClassifyRagdoll(false, 0, true) == FeedbackPolicy.RagdollLease.Missing
            && FeedbackPolicy.ClassifyRagdoll(true, 0, true) == FeedbackPolicy.RagdollLease.Empty
            && FeedbackPolicy.ClassifyRagdoll(true, 3, true) == FeedbackPolicy.RagdollLease.Usable
            && FeedbackPolicy.ClassifyRagdoll(true, 3, false) == FeedbackPolicy.RagdollLease.Missing,
            "corpse launch (1.2.16 #2): the three lease states are told apart - no lease / lease with no bodies / usable - and a stale lease counts as missing");
        check(!FeedbackPolicy.ShouldApplyLaunch(FeedbackPolicy.RagdollLease.Empty)
            && !FeedbackPolicy.ShouldApplyLaunch(FeedbackPolicy.RagdollLease.Missing)
            && FeedbackPolicy.ShouldReplaceRagdollLease(FeedbackPolicy.RagdollLease.Empty)
            && !FeedbackPolicy.ShouldReplaceRagdollLease(FeedbackPolicy.RagdollLease.Usable),
            "corpse launch (1.2.16 #2): an empty lease is never launched but is always replaced, so the 19 silent `BodyCount == 0` corpses can no longer be skipped by both branches");
        check(FeedbackPolicy.ShouldReapplyLaunch(1, false, 0.1f, 0.1f)
            && !FeedbackPolicy.ShouldReapplyLaunch(1, true, 0.1f, 0.1f)
            && !FeedbackPolicy.ShouldReapplyLaunch(0, false, 0.1f, 0.1f)
            && !FeedbackPolicy.ShouldReapplyLaunch(FeedbackPolicy.MaxLaunchApplications, false, 0.1f, 0.1f)
            && FeedbackPolicy.ShouldReapplyLaunch(1, false, FeedbackPolicy.RagdollLookupDeadline(0.1f) + 0.01f, 0.1f)
            && !FeedbackPolicy.ShouldReapplyLaunch(1, false, FeedbackPolicy.RagdollLookupDeadline(0.1f) + FeedbackPolicy.RagdollLookupFinalProbeSlack + 0.01f, 0.1f),
            "corpse launch (1.2.16 #2): a replacement ragdoll gets exactly one second (and final) impulse inside the window, and the same lease is never pushed twice");
        check(FeedbackPolicy.ShouldReportUnavailableLaunch(0, false, true)
            && !FeedbackPolicy.ShouldReportUnavailableLaunch(0, true, true)
            && !FeedbackPolicy.ShouldReportUnavailableLaunch(1, false, true),
            "corpse launch (1.2.16 #2): a corpse that was never launched is reported exactly once, and an applied launch is never re-reported as unavailable");
        check(FeedbackPolicy.ShouldKeepCorpseForPendingLaunch(FeedbackPolicy.RagdollLease.Usable, 0, "enemy-despawn")
            && !FeedbackPolicy.ShouldKeepCorpseForPendingLaunch(FeedbackPolicy.RagdollLease.Usable, 1, "enemy-despawn")
            && !FeedbackPolicy.ShouldKeepCorpseForPendingLaunch(FeedbackPolicy.RagdollLease.Empty, 0, "enemy-despawn")
            && !FeedbackPolicy.ShouldKeepCorpseForPendingLaunch(FeedbackPolicy.RagdollLease.Missing, 0, "enemy-despawn")
            && !FeedbackPolicy.ShouldKeepCorpseForPendingLaunch(FeedbackPolicy.RagdollLease.Usable, 0, "enemy-setup"),
            "corpse launch (R3): only an unlaunched corpse with a usable ragdoll survives a real enemy despawn; an enemy-setup means the pooled wrapper belongs to a LIVING enemy, so it is always closed out");
        // ---- R1: ONE impulse gate, shared by the first tick and every late lookup ---------------------
        // 1.2.16's late-lookup branch called ApplyLaunch with no budget check at all, so a corpse whose lease kept
        // going stale could be pushed on every retry (MaxRagdollLookupAttempts = 8 times) while the log said
        // `application=#N`.
        // B (1.2.19 real-machine): an inactive lease can never be pushed, so the gate is asked with the LIVE fact too.
        check(FeedbackPolicy.CanApplyLaunch(FeedbackPolicy.RagdollLease.Usable, true, 0, false, 0.05f, 0.1f)
            && !FeedbackPolicy.CanApplyLaunch(FeedbackPolicy.RagdollLease.Usable, false, 0, false, 0.05f, 0.1f)
            && !FeedbackPolicy.CanApplyLaunch(FeedbackPolicy.RagdollLease.Usable, true, 1, true, 0.05f, 0.1f)
            && FeedbackPolicy.CanApplyLaunch(FeedbackPolicy.RagdollLease.Usable, true, 1, false, 0.05f, 0.1f)
            && !FeedbackPolicy.CanApplyLaunch(FeedbackPolicy.RagdollLease.Usable, true, FeedbackPolicy.MaxLaunchApplications, false, 0.05f, 0.1f)
            && !FeedbackPolicy.CanApplyLaunch(FeedbackPolicy.RagdollLease.Missing, true, 0, false, 0.05f, 0.1f)
            && !FeedbackPolicy.CanApplyLaunch(FeedbackPolicy.RagdollLease.Empty, true, 0, false, 0.05f, 0.1f)
            && FeedbackPolicy.CanApplyLaunch(FeedbackPolicy.RagdollLease.Usable, true, 1, false, FeedbackPolicy.RagdollLookupDeadline(0.1f) + 0.01f, 0.1f)
            && !FeedbackPolicy.CanApplyLaunch(FeedbackPolicy.RagdollLease.Usable, true, 1, false, FeedbackPolicy.RagdollLookupDeadline(0.1f) + FeedbackPolicy.RagdollLookupFinalProbeSlack + 0.01f, 0.1f),
            "corpse launch (R1/B): the single impulse gate refuses a repeat on the same lease, refuses past MaxLaunchApplications and outside the window, refuses anything the game has not activated (live=false), and only a NEW lease gets the second application");
        check(FeedbackPolicy.IsRagdollLive(true) && !FeedbackPolicy.IsRagdollLive(false),
            "corpse launch (B): IsRagdollLive is the one activation fact - true only for an object that is really active in the hierarchy");

        // Exhaustive lease-sequence property over six ticks: every pattern of (same capture / re-captured /
        // emptied) - 3^6 sequences - and the gate may never spend more than MaxLaunchApplications impulses.
        int worstSequence = 0;
        bool impulseBudgetHeld = true;
        for (int mask = 0; mask < 729; mask++)
        {
            int applications = 0, state = mask;
            IntPtr pointer = (IntPtr)1;
            int generation = 1;
            IntPtr appliedPointer = IntPtr.Zero;
            int appliedGeneration = 0;
            for (int tick = 0; tick < 6; tick++)
            {
                int leaseCase = state % 3;
                state /= 3;
                float age = FeedbackPolicy.FirstLaunchAttemptAge + tick / 60f;
                FeedbackPolicy.RagdollLease lease;
                if (leaseCase == 2) lease = FeedbackPolicy.RagdollLease.Empty;
                else
                {
                    if (leaseCase == 1) generation++;   // the game's Capture() re-took the same object
                    lease = FeedbackPolicy.RagdollLease.Usable;
                }
                bool sameLease = FeedbackPolicy.IsSameLaunchLease(appliedPointer, appliedGeneration, pointer, generation);
                if (!FeedbackPolicy.CanApplyLaunch(lease, true, applications, sameLease, age, 0.1f)) continue;
                applications++;
                appliedPointer = pointer;
                appliedGeneration = generation;
            }
            if (applications > worstSequence) worstSequence = applications;
            impulseBudgetHeld &= applications <= FeedbackPolicy.MaxLaunchApplications;
        }
        check(impulseBudgetHeld && worstSequence == 1,
            "corpse launch (R4): over all 729 lease sequences a corpse is pushed at most MaxLaunchApplications times, and a re-capture of the SAME object spends no further impulse (worst=" + worstSequence + ")");

        // ---- R2: pointer AND generation identify the lease that received the impulse -------------------
        check(FeedbackPolicy.IsSameLaunchLease((IntPtr)0x1234, 3, (IntPtr)0x1234, 3)
            && FeedbackPolicy.IsSameLaunchLease((IntPtr)0x1234, 3, (IntPtr)0x1234, 4)
            && !FeedbackPolicy.IsSameLaunchLease((IntPtr)0x1234, 3, (IntPtr)0x5678, 3)
            && !FeedbackPolicy.IsSameLaunchLease(IntPtr.Zero, 0, IntPtr.Zero, 0),
            "corpse launch (R4): the impulse addressee is the OBJECT, so our own re-capture of the same ragdoll (generation 3 -> 4) can never be pushed a second time; a different object is a different addressee and the null pointer is never a lease");

        // ---- R4/R5: closure is real, not arithmetic, and the closure state cannot contradict itself -----
        check(FeedbackPolicy.EndAtCoversLaunchLookup(FeedbackPolicy.CorpsePostBurstHold, FeedbackPolicy.RagdollLookupGrace)
            && !FeedbackPolicy.EndAtCoversLaunchLookup(FeedbackPolicy.CorpsePostBurstHold, FeedbackPolicy.CorpsePostBurstHold + 0.01f),
            "corpse launch (R4): the 0.75 s post-burst hold really covers the 0.30 s lookup window, and the check fails if a later edit raises the grace past it");
        check(FeedbackPolicy.LaunchState(0, false) == "pending"
            && FeedbackPolicy.LaunchState(0, true) == "unavailable"
            && FeedbackPolicy.LaunchState(2, true) == "applied#2"
            && FeedbackPolicy.LaunchState(0, true) != "pending",
            "corpse launch (R5): a closed-out corpse can never report launch=pending again (1.2.16 printed `unavailable` and then `pending` for the same entity)");
        check(FeedbackPolicy.LadderText(string.Empty, string.Empty) == "none"
            && FeedbackPolicy.LadderText(string.Empty, "lease-stale") == "lease-stale"
            && FeedbackPolicy.LadderText("ragdoll-instance", "ragdoll-script") == "ragdoll-instance",
            "corpse launch (R8): ladder= is never an empty field - a stale lease prints its reason, and every tried rung wins over the last one");

        // Whole-turn closure driven ONLY by production predicates (R9: the 1.2.16 test re-implemented the branch
        // - including the very `applications == 0 ||` shape that hid R1 - so it could not fail). Three fates:
        // no ragdoll at all, a ragdoll the game empties and then hands back as the SAME object (a re-capture),
        // and a ragdoll the game replaces with a DIFFERENT object.
        int RunTickMachine(bool haveRagdoll, bool secondIsSameObject, out int unavailableReceipts)
        {
            int applications = 0;
            unavailableReceipts = 0;
            bool closed = false, reported = false, replaced = false;
            FeedbackPolicy.RagdollLease lease = haveRagdoll ? FeedbackPolicy.RagdollLease.Usable : FeedbackPolicy.RagdollLease.Missing;
            IntPtr pointer = (IntPtr)7;
            int generation = 1;
            IntPtr appliedPointer = IntPtr.Zero;
            int appliedGeneration = 0;
            int attempts = 0;
            for (int frame = 0; frame <= 90 && !closed; frame++)
            {
                float age = frame / 60f;
                if (age < FeedbackPolicy.FirstLaunchAttemptAge) continue;
                // The real 0.19 s event: the object we captured is emptied by the game, and the ladder then
                // resolves a ragdoll one frame later (the discard + retry path).
                if (haveRagdoll && age >= 0.19f && !replaced) lease = FeedbackPolicy.RagdollLease.Empty;
                if (FeedbackPolicy.ShouldReplaceRagdollLease(lease))
                {
                    lease = haveRagdoll ? FeedbackPolicy.RagdollLease.Usable : FeedbackPolicy.RagdollLease.Missing;
                    generation++;
                    if (!secondIsSameObject) pointer = (IntPtr)8;
                    replaced = true;
                }
                bool sameLease = FeedbackPolicy.IsSameLaunchLease(appliedPointer, appliedGeneration, pointer, generation);
                if (FeedbackPolicy.CanApplyLaunch(lease, true, applications, sameLease, age, 0.1f))
                {
                    applications++;
                    appliedPointer = pointer;
                    appliedGeneration = generation;
                    continue;
                }
                if (FeedbackPolicy.ShouldApplyLaunch(lease))
                {
                    if (FeedbackPolicy.ShouldCloseAppliedLaunch(applications, age, 0.1f)) closed = true;
                    continue;
                }
                if (FeedbackPolicy.ShouldRetryRagdollLookup(false, attempts, age, 0.1f)) attempts++;
                if (!FeedbackPolicy.ShouldAbandonRagdollLookup(false, attempts, age, 0.1f)) continue;
                closed = true;
                if (FeedbackPolicy.ShouldReportUnavailableLaunch(applications, reported, true)) { reported = true; unavailableReceipts++; }
            }
            return applications;
        }
        int noRagdollApplications = RunTickMachine(false, false, out int noRagdollReceipts);
        check(noRagdollApplications == 0 && noRagdollReceipts == 1,
            "corpse launch (R1/R9): a corpse whose ragdoll never appears is closed out with exactly one unavailable receipt (applications=" + noRagdollApplications + ", receipts=" + noRagdollReceipts + ")");
        int sameObjectApplications = RunTickMachine(true, true, out int sameObjectReceipts);
        check(sameObjectApplications == 1 && sameObjectReceipts == 0,
            "corpse launch (R4): the game emptying and re-capturing the SAME ragdoll spends only the first impulse, and the corpse is never reported unavailable after a real launch (applications=" + sameObjectApplications + ")");
        int newObjectApplications = RunTickMachine(true, false, out int newObjectReceipts);
        check(newObjectApplications == FeedbackPolicy.MaxLaunchApplications && newObjectReceipts == 0,
            "corpse launch (R4 mutation control): a genuinely DIFFERENT replacement ragdoll still gets the second (final) impulse, so the same-object rule did not swallow the replacement path (applications=" + newObjectApplications + ")");
        check(FeedbackPolicy.MaxLaunchApplications == 2,
            "corpse launch (R4): the impulse cap stays at two - one per object, never per retry");
        // ---- B (1.2.19 real-machine): a lease the game has not activated is never pushed ---------------------
        // The player saw `launch=applied` on a body the game had not activated yet: the velocity went nowhere, and the
        // 0.85 s Hide() turned that into "the corpse vanished where it stood". The whole fate below is decided by the
        // production predicates only - IsRagdollLive / CanApplyLaunch / ShouldApplyLaunch / ShouldCloseAppliedLaunch /
        // ShouldReportUnavailableLaunch / ShouldHideCorpse - driven by the same tick order CorpseFeedback.Tick uses.
        (int Applications, int Receipts, bool Hidden, bool ReachedEndAt) RunLiveMachine(float liveAt)
        {
            int applications = 0, receipts = 0;
            var lease = FeedbackPolicy.RagdollLease.Usable;
            bool sameLease = false, launchClosed = false, reported = false, inactive = false;
            for (int frame = 0; frame <= 90; frame++)
            {
                float age = frame / 60f;
                if (age < FeedbackPolicy.FirstLaunchAttemptAge) continue;
                bool live = liveAt >= 0f && age >= liveAt;
                // TryLaunchOrAbandon is only entered while the launch state is open (the Tick-level gate).
                if (!launchClosed && FeedbackPolicy.ShouldApplyLaunch(true, launchClosed, age))
                {
                    if (FeedbackPolicy.CanApplyLaunch(lease, live, applications, sameLease, age, 0.1f))
                    {
                        applications++;
                        sameLease = true;
                    }
                    else if (FeedbackPolicy.ShouldApplyLaunch(lease))
                    {
                        // An inactive body keeps the impulse pending (no impulse, no close-out) ...
                        if (!live) inactive = true;
                        // ... an activated one is closed out as soon as the budget/deadline says so.
                        else if (FeedbackPolicy.ShouldCloseAppliedLaunch(applications, age, 0.1f)) launchClosed = true;
                    }
                }
                if (age < FeedbackPolicy.CorpseEndAt(0.1f)) continue;
                // The EndAt stage: close the launch (one receipt) and then decide whether this corpse may be hidden.
                if (FeedbackPolicy.ShouldReportUnavailableLaunch(applications, reported, true)) { reported = true; receipts++; }
                return (applications, receipts, FeedbackPolicy.ShouldHideCorpse(applications, inactive), true);
            }
            return (applications, receipts, false, false);
        }

        // R4 (2nd review): the same tick, driven by production predicates only. The FIRST lease is usable but never
        // activates; after the activation grace the ladder may run again (ShouldRetryInactiveLease AND the existing
        // time-paced schedule) and a replacement lease takes over the one impulse. The gate itself is untouched.
        (int Applications, int Receipts, bool Hidden, bool ReasonInactive, int Lookups) RunInactiveReplacement(float replacementAt, bool replacementLive)
        {
            int applications = 0, receipts = 0, lookups = 0;
            bool launchClosed = false, reported = false, inactive = false, live = false;
            float waitStart = -1f;
            var lease = FeedbackPolicy.RagdollLease.Usable;
            bool sameLease = false;
            for (int frame = 0; frame <= 90; frame++)
            {
                float age = frame / 60f;
                if (age < FeedbackPolicy.FirstLaunchAttemptAge) continue;
                // TryLaunchOrAbandon's retry block: "no usable lease" OR "usable but never activated past the grace".
                bool inactiveLease = FeedbackPolicy.ShouldApplyLaunch(lease) && !live
                    && FeedbackPolicy.ShouldRetryInactiveLease(lookups, waitStart, age);
                if ((!FeedbackPolicy.ShouldApplyLaunch(lease) || inactiveLease)
                    && FeedbackPolicy.ShouldRetryRagdollLookup(false, lookups, age, 0.1f))
                {
                    lookups++;
                    if (age >= replacementAt && FeedbackPolicy.ShouldResolveRagdoll(true, live, inactiveLease)
                        && FeedbackPolicy.CanCaptureRagdollCandidate(true, false, replacementLive))
                    {
                        // A new lease is a new activation question (R5 resets the wait on every new lease).
                        lease = FeedbackPolicy.RagdollLease.Usable;
                        sameLease = false;
                        live = replacementLive;
                        waitStart = -1f;
                        inactive = false;
                    }
                    continue;
                }
                if (!launchClosed && FeedbackPolicy.ShouldApplyLaunch(true, launchClosed, age))
                {
                    if (FeedbackPolicy.CanApplyLaunch(lease, live, applications, sameLease, age, 0.1f))
                    {
                        applications++;
                        sameLease = true;
                    }
                    else if (FeedbackPolicy.ShouldApplyLaunch(lease))
                    {
                        if (!live)
                        {
                            inactive = true;
                            if (waitStart < 0f) waitStart = age;
                        }
                        else if (FeedbackPolicy.ShouldCloseAppliedLaunch(applications, age, 0.1f)) launchClosed = true;
                    }
                }
                if (age < FeedbackPolicy.CorpseEndAt(0.1f)) continue;
                if (FeedbackPolicy.ShouldReportUnavailableLaunch(applications, reported, true)) { reported = true; receipts++; }
                bool reasonInactive = FeedbackPolicy.IsInactiveRagdollLaunch(applications, inactive, live);
                return (applications, receipts, FeedbackPolicy.ShouldHideCorpse(applications, inactive), reasonInactive, lookups);
            }
            return (applications, receipts, false, false, lookups);
        }
        var neverLive = RunLiveMachine(-1f);
        check(neverLive.Applications == 0 && neverLive.Receipts == 1 && !neverLive.Hidden && neverLive.ReachedEndAt,
            "corpse launch (B): a ragdoll the game never activates is never pushed, gets exactly ONE terminal receipt, and its corpse is NOT hidden/recycled at EndAt (the game keeps its own corpse)");
        var lateLive = RunLiveMachine(0.30f);
        check(lateLive.Applications == 1 && lateLive.Receipts == 0 && lateLive.Hidden && lateLive.ReachedEndAt,
            "corpse launch (B): the impulse waits while the body is inactive and is applied exactly ONCE on the first tick after the game activates it (same pointer, no second push, no receipt)");
        var liveNow = RunLiveMachine(0f);
        check(liveNow.Applications == 1 && liveNow.Receipts == 0 && liveNow.Hidden,
            "corpse launch (B): a body that is live from the very first tick behaves exactly as before (one impulse, hidden at EndAt)");
        check(FeedbackPolicy.ShouldHideCorpse(1, false) && FeedbackPolicy.ShouldHideCorpse(1, true)
            && FeedbackPolicy.ShouldHideCorpse(0, false) && !FeedbackPolicy.ShouldHideCorpse(0, true),
            "corpse hide (B): an applied corpse is always hidden (even if a lease had been inactive earlier), a corpse without any ragdoll is hidden, and only the '0 impulses + never active' corpse is kept");

        // ---- R2 (2nd review): a kept corpse is not dissolved either ------------------------------------------
        // The native fade ran unconditionally in the EndAt stage, so a corpse that was deliberately left to the game
        // (0 impulses + a ragdoll that never activated) still got a Decay. One fact decides both: the corpse is either
        // released (fade + hide + recycle) or kept untouched - and it still prints exactly one terminal fade= line.
        check(!FeedbackPolicy.ShouldAttemptCorpseFade(0, true) && FeedbackPolicy.ShouldAttemptCorpseFade(1, false)
            && FeedbackPolicy.ShouldAttemptCorpseFade(1, true) && FeedbackPolicy.ShouldAttemptCorpseFade(0, false),
            "corpse fade (R2): the dissolve is attempted exactly when the corpse is released - never for the kept '0 impulses + never active' corpse, and always for an applied one");

        // ---- R5 (2nd review): reason=ragdoll-inactive can never contradict the receipt ------------------------
        check(FeedbackPolicy.IsInactiveRagdollLaunch(0, true, false) && !FeedbackPolicy.IsInactiveRagdollLaunch(0, true, true)
            && !FeedbackPolicy.IsInactiveRagdollLaunch(1, true, false) && !FeedbackPolicy.IsInactiveRagdollLaunch(0, false, false),
            "corpse receipt (R5): reason=ragdoll-inactive is claimed only for 0 impulses + an inactive lease, so `reason=ragdoll-inactive;active=True` is impossible");

        // ---- R4 (2nd review): an inactive lease must not lock the ladder -------------------------------------
        // ShouldRetryRagdollLookup is asked with haveRagdoll=false, so a USABLE-but-never-activated lease blocked the
        // search for the rest of the corpse. The retry below is the production predicate pair: the new
        // ShouldRetryInactiveLease (grace + attempt budget) AND the existing time-paced schedule, in the same tick.
        check(FeedbackPolicy.ShouldResolveRagdoll(false, false, false)
            && !FeedbackPolicy.ShouldResolveRagdoll(true, true, true)
            && !FeedbackPolicy.ShouldResolveRagdoll(true, false, false)
            && FeedbackPolicy.ShouldResolveRagdoll(true, false, true),
            "corpse resolver: only an explicitly retried inactive usable lease reopens the real ladder");
        check(FeedbackPolicy.CanCaptureRagdollCandidate(false, false, false)
            && !FeedbackPolicy.CanCaptureRagdollCandidate(true, true, true)
            && !FeedbackPolicy.CanCaptureRagdollCandidate(true, false, false)
            && FeedbackPolicy.CanCaptureRagdollCandidate(true, false, true),
            "corpse resolver: keep an inactive lease until a different active candidate can replace it");
        var replacementMachine = RunInactiveReplacement(0.32f, true);
        check(replacementMachine.Applications == 1 && replacementMachine.Receipts == 0
            && replacementMachine.Hidden && replacementMachine.Lookups > 0
            && !replacementMachine.ReasonInactive,
            "corpse launch (R4): an inactive first lease is re-resolved inside the same budget and the live replacement ragdoll receives exactly ONE impulse, so the corpse still flies and is released normally (lookups=" + replacementMachine.Lookups + ")");
        var bothInactive = RunInactiveReplacement(0.32f, false);
        check(bothInactive.Applications == 0 && bothInactive.Receipts == 1 && !bothInactive.Hidden
            && bothInactive.ReasonInactive && bothInactive.Lookups > 0,
            "corpse launch (R4/R5): when the replacement is inactive as well the corpse ends as ragdoll-inactive - not flown, not hidden, one receipt (lookups=" + bothInactive.Lookups + ")");
        check(!FeedbackPolicy.ShouldRetryInactiveLease(0, -1f, 0.9f)
            && !FeedbackPolicy.ShouldRetryInactiveLease(0, 0.02f, 0.02f + FeedbackPolicy.RagdollLookupGrace - 0.01f)
            && FeedbackPolicy.ShouldRetryInactiveLease(0, 0.02f, 0.02f + FeedbackPolicy.RagdollLookupGrace + 0.01f)
            && !FeedbackPolicy.ShouldRetryInactiveLease(FeedbackPolicy.MaxRagdollLookupAttempts + 1, 0.02f, 0.9f)
            && !FeedbackPolicy.ShouldRetryInactiveLease(0, float.NaN, 0.9f),
            "corpse retry (R4): the inactive-lease retry needs a started wait, the 0.30 s activation grace and a live attempt budget, so it can never scan every tick");
        // ---- C5: a sub-0.5 m budget is not lifted to the configured minimum ---------------------------
        check(FeedbackPolicy.LaunchSpeedRaw(0.35f, 0.5f) < FeedbackPolicy.LaunchSpeedFor(0.5f, 0.5f)
            && Math.Abs(FeedbackPolicy.LaunchDistance(FeedbackPolicy.LaunchSpeedFor(0.35f, 0.5f), 0.5f) - 0.35f) < 0.02f,
            "corpse speed (C5): a 0.35 m frozen budget (0.5 m x 0.7 bias) flies 0.35 m, not the configured 0.5 m floor");
        check(Math.Abs(FeedbackPolicy.LaunchSpeedRaw(0f, 0.5f) - FeedbackPolicy.LaunchSpeedRaw(FeedbackPolicy.MinTravelBudgetMetres, 0.5f)) < 0.0001f
            && FeedbackPolicy.LaunchSpeedRaw(float.NaN, 0.5f) > 0f,
            "corpse speed (C5): zero/NaN travel only hits the small positivity guard, never a crash or a NaN speed");
        // ---- C7: an independent forward model must land on the budget, and the old model must not -----
        bool numericLandsOnBudget = true;
        foreach (float budget in new[] { 3.8f, 8.5f, 12f, 18f, 30f })
        {
            float speed = FeedbackPolicy.LaunchSpeedFor(budget, 0.5f);
            numericLandsOnBudget &= Math.Abs(SimulateDistance(speed, 0.5f) - budget) <= 0.03f * budget;
        }
        check(numericLandsOnBudget,
            "corpse launch (C7): an independent numeric integration of the same drag/air-time physics lands within 3% of the configured budget");
        bool speedEqualsBudgetFails = true, fixedLegacySpeedFails = true;
        foreach (float budget in new[] { 3.8f, 8.5f, 12f, 18f, 30f })
        {
            // Mistake 1: treat the budget as if it were the speed (a units mix-up the algebra cannot hide).
            speedEqualsBudgetFails &= Math.Abs(SimulateDistance(budget, 0.5f) - budget) > 0.12f * budget;
            // Mistake 2: the exact 1.2.15 behaviour - one fixed 6.4 m/s launch for every configuration.
            fixedLegacySpeedFails &= Math.Abs(SimulateDistance(6.4f, 0.5f) - budget) > 0.30f * budget;
        }
        check(speedEqualsBudgetFails,
            "corpse launch (C7): the independent model rejects speed==budget at every tier, so the assertion cannot pass by algebra alone");
        check(fixedLegacySpeedFails,
            "corpse launch (C7): the same model rejects the 1.2.15 fixed 6.4 m/s launch at every tier (3.8-30 m)");

        // The closed-form helper the plugin logs must agree with the numeric integration too.
        check(Math.Abs(FeedbackPolicy.LaunchDistance(FeedbackPolicy.LaunchSpeedFor(12f, 0.5f), 0.5f) - SimulateDistance(FeedbackPolicy.LaunchSpeedFor(12f, 0.5f), 0.5f)) < 0.02f,
            "corpse launch (C7): FeedbackPolicy.LaunchDistance matches the numeric integration (no drift between the log and the physics)");

        // ---- C6: the logged budget and the applied clamp must agree ----------------------------------
        check(Math.Abs(FeedbackPolicy.AppliedTravel(60.75f, 1f) - FeedbackPolicy.AbsoluteDistanceCeiling) < 0.0001f
            && FeedbackPolicy.AppliedTravel(40.5f, 1f) <= FeedbackPolicy.AbsoluteDistanceCeiling,
            "corpse log (C6): a 60.75 m biased budget prints appliedTravel=40 m, so budget= and appliedTravel= can never contradict each other");

        // ---- max is never below min, NaN falls back, and the defaults are unchanged ------------------
        check(Math.Abs(FeedbackPolicy.LaunchBudget(1f, 8.5f, 3.8f) - 8.5f) < 0.0001f,
            "corpse launch: an inverted pair (Max 3.8 < Min 8.5) is raised to the minimum instead of dropping the launch");
        check(Math.Abs(FeedbackPolicy.LaunchBudget(0f, float.NaN, float.NaN) - FeedbackPolicy.DefaultMinDistance) < 0.0001f
            && Math.Abs(FeedbackPolicy.Travel(0f) - FeedbackPolicy.DefaultMinDistance) < 0.0001f
            && Math.Abs(FeedbackPolicy.Travel(1f) - FeedbackPolicy.DefaultMaxDistance) < 0.0001f,
            "corpse launch: a broken cfg falls back to the shipped 12-18 m defaults");
        check(Math.Abs(FeedbackPolicy.LaunchBudget(0f, -20f, 0f) - FeedbackPolicy.MinLaunchDistanceMetres) < 0.0001f
            && Math.Abs(FeedbackPolicy.LaunchBudget(1f, 1f, 5000f) - FeedbackPolicy.MaxLaunchDistanceMetres) < 0.0001f,
            "corpse launch: out-of-range values are clamped to 0.5-30 m rather than silently replaced by 12 m");

        // ---- the physics constants the inverse depends on are pinned --------------------------------
        check(Math.Abs(FeedbackPolicy.CorpseDrag - 0.8f) < 0.0001f
            && Math.Abs(FeedbackPolicy.CorpseGravity - 9.81f) < 0.0001f
            && Math.Abs(FeedbackPolicy.CorpseLaunchHeightMetres - 1f) < 0.0001f
            && Math.Abs(FeedbackPolicy.SlideRecovery - 0.35f) < 0.0001f,
            "corpse launch: the inverse model is pinned (drag 0.8, gravity 9.81, 1 m launch height, 0.35 slide recovery)");
        check(Math.Abs(FeedbackPolicy.LaunchUpSpeed(0f) - 1.6f) < 0.0001f && Math.Abs(FeedbackPolicy.LaunchUpSpeed(1f) - 2.6f) < 0.0001f,
            "corpse launch: the vertical kick is still the historical 1.6 + strength");
        check(FeedbackPolicy.LaunchUpSpeed(float.NaN) > 0f && FeedbackPolicy.LaunchAirTime(0.5f) > 0.5f
            && FeedbackPolicy.LaunchAirTime(0.5f) < 1.5f,
            "corpse launch: the air time stays a plausible 0.6-0.8 s and a broken strength cannot produce NaN");

        // ---- the other corpse behaviour this touched must stay exactly as it was --------------------
        check(Math.Abs(FeedbackPolicy.CorpseOffSceneDistance - 100f) < 0.0001f
            && FeedbackPolicy.IsCorpseOffSceneDistanceSquared(100.1f * 100.1f)
            && !FeedbackPolicy.IsCorpseOffSceneDistanceSquared(99f * 99f),
            "corpse launch: the 100 m off-scene cleanup distance is untouched");
        check(!FeedbackPolicy.ShouldPlayCorpseBurstFx(new CorpseSwitches(true, true, true, true, false, true, true)) && FeedbackPolicy.ShouldPlayCorpseBurstFx(new CorpseSwitches(true, true, true, true, true, true, true)),
            "corpse launch: ExplosionFx keeps its opt-in semantics");
        check(FeedbackPolicy.ShouldWaitForRagdoll(0.04f, 0.1f) && !FeedbackPolicy.ShouldWaitForRagdoll(0.1f, 0.1f),
            "corpse launch: ShouldWaitForRagdoll still answers the pre-burst question (the lookup window itself is burst + grace now)");
        check(FeedbackPolicy.RagdollLookupDeadline(0.1f) - (0.1f + FeedbackPolicy.RagdollLookupGrace) < 0.0001f,
            "corpse launch: the ragdoll lookup window is burst + grace, so the retries survive the burst (1.2.16 real-machine fix)");

        // ---- log noise budgeting (163 duplicate callbacks / 43 pruned enemy-null in the real log) ----
        var tally = new LogTally();
        check(tally.Add("duplicate-death-callback") == 1 && tally.Add("duplicate-death-callback") == 2
            && tally.Add("other") == 1 && tally.Count("duplicate-death-callback") == 2 && tally.Total == 3,
            "corpse log budget: repeated skip reasons are counted per reason instead of printed every time");
        check(LogTally.ShouldLog(1, 3, 50) && LogTally.ShouldLog(3, 3, 50) && !LogTally.ShouldLog(4, 3, 50)
            && LogTally.ShouldLog(50, 3, 50) && !LogTally.ShouldLog(51, 3, 50) && !LogTally.ShouldLog(0, 3, 50),
            "corpse log budget: the first few occurrences and every stride-th one are logged, the rest is counted silently");
        tally.Reset();
        check(tally.Total == 0 && tally.Count("duplicate-death-callback") == 0,
            "corpse log budget: the tally resets with the level, so a new level starts its own counts");
    }

    // Independent forward model (review C7): integrate dv/dt = -drag*v over the air time with a fine fixed
    // step, then add only SlideRecovery of the remaining slide. Deliberately NOT the closed-form helper the
    // plugin uses, so a wrong inverse cannot pass by re-using its own algebra.
    private static float SimulateDistance(float speed, float strength)
    {
        const float step = 0.0005f;
        float air = FeedbackPolicy.LaunchAirTime(strength);
        float v = speed > 0f ? speed : 0f;
        float distance = 0f;
        for (float t = 0f; t < air; t += step)
        {
            distance += v * step;
            v -= FeedbackPolicy.CorpseDrag * v * step;
        }
        return distance + FeedbackPolicy.SlideRecovery * v / FeedbackPolicy.CorpseDrag;
    }
}
