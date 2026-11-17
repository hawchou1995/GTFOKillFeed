using System;
using System.Collections.Generic;
using HawChow.GTFOKillFeed;


// BFV hit/kill lines, the heavy-kill classification and the new damage-number flight, all without Unity.
internal static class HitMarkerTests
{
    internal static void Run(Action<bool, string> check)
    {
        // ---- event -> line mapping ---------------------------------------------------------
        check(HitMarkerPolicy.KindFor(false, false, false) == HitMarkerKind.Normal,
            "hitmarker: a normal hit draws the plain line");
        check(HitMarkerPolicy.KindFor(true, false, false) == HitMarkerKind.Weakspot,
            "hitmarker: a weakspot hit draws the weakspot line");
        check(HitMarkerPolicy.KindFor(false, true, false) == HitMarkerKind.Armor,
            "hitmarker: an armour hit draws the armour brackets");
        check(HitMarkerPolicy.KindFor(true, true, false) == HitMarkerKind.Armor,
            "hitmarker: the local armour verdict wins over the weakspot flag of the same event");
        check(HitMarkerPolicy.KindFor(false, false, true) == HitMarkerKind.Kill
            && HitMarkerPolicy.KindFor(true, true, true) == HitMarkerKind.Kill,
            "hitmarker: a kill draws the kill line whatever the limb was");
        check(HitMarkerPolicy.IconKey(HitMarkerKind.Normal) == "hitmark-normal"
            && HitMarkerPolicy.IconKey(HitMarkerKind.Weakspot) == "hitmark-weakspot"
            && HitMarkerPolicy.IconKey(HitMarkerKind.Armor) == "hitmark-armor"
            && HitMarkerPolicy.IconKey(HitMarkerKind.Kill) == "hitmark-kill"
            && HitMarkerPolicy.IconKey(HitMarkerKind.None) == string.Empty,
            "hitmarker: each state maps to its own icon slot, none maps to nothing");
        check(HitMarkerPolicy.IconFile(HitMarkerKind.Kill) == "hitmark-kill.png"
            && HitMarkerPolicy.IconFile(HitMarkerKind.None) == string.Empty,
            "hitmarker: the kill line is a bundled PNG of its own, never the head-top kill icon");

        // ---- size / lifetime / alpha ------------------------------------------------------
        check(Math.Abs(HitMarkerPolicy.Size(64f) - 64f) < 0.0001f
            && Math.Abs(HitMarkerPolicy.Size(1f) - HitMarkerPolicy.MinSize) < 0.0001f
            && Math.Abs(HitMarkerPolicy.Size(9999f) - HitMarkerPolicy.MaxSize) < 0.0001f
            && Math.Abs(HitMarkerPolicy.Size(float.NaN) - HitMarkerPolicy.DefaultSize) < 0.0001f,
            "hitmarker: size clamps into range and NaN falls back to the 64 px default");
        check(HitMarkerPolicy.SizeScale(HitMarkerKind.Weakspot) > 1f
            && HitMarkerPolicy.SizeScale(HitMarkerKind.Armor) < 1f
            && Math.Abs(HitMarkerPolicy.SizeScale(HitMarkerKind.Normal) - 1f) < 0.0001f,
            "hitmarker: the three hit states also differ in size, so the state is never colour-only");
        check(HitMarkerPolicy.SizeScale(HitMarkerKind.Kill) > HitMarkerPolicy.SizeScale(HitMarkerKind.Weakspot),
            "hitmarker: the kill line is the largest line, so a kill reads differently from a weakspot hit");
        check(HitMarkerPolicy.Lifetime(HitMarkerKind.Kill) > HitMarkerPolicy.Lifetime(HitMarkerKind.Normal)
            && HitMarkerPolicy.Lifetime(HitMarkerKind.Kill) <= HitMarkerPolicy.MaxDurationSeconds
            && HitMarkerPolicy.Lifetime(HitMarkerKind.Armor) == HitMarkerPolicy.DefaultHitDurationSeconds,
            "hitmarker: the kill line stays up longer than a hit line but is still bounded");
        check(Math.Abs(HitMarkerPolicy.Alpha(0f, 0.18f) - 1f) < 0.0001f
            && Math.Abs(HitMarkerPolicy.Alpha(0.09f, 0.18f) - 0.5f) < 0.001f
            && HitMarkerPolicy.Alpha(0.18f, 0.18f) == 0f
            && HitMarkerPolicy.Alpha(0.5f, 0.18f) == 0f,
            "hitmarker: alpha fades linearly and reaches exactly zero at the end");
        check(HitMarkerPolicy.Alpha(float.NaN, 0.18f) == 0f
            && HitMarkerPolicy.Alpha(-1f, 0.18f) == 0f,
            "hitmarker: a broken age never draws");
        check(HitMarkerPolicy.Alpha(HitMarkerPolicy.Lifetime(HitMarkerKind.Kill) * 0.5f, HitMarkerPolicy.Lifetime(HitMarkerKind.Kill)) > 0.4f,
            "hitmarker: the kill line is still clearly visible half-way through its life");
        check(Math.Abs(HitMarkerPolicy.Duration(0.001f) - HitMarkerPolicy.MinDurationSeconds) < 0.0001f
            && Math.Abs(HitMarkerPolicy.Duration(9f) - HitMarkerPolicy.MaxDurationSeconds) < 0.0001f
            && Math.Abs(HitMarkerPolicy.Duration(float.NaN) - HitMarkerPolicy.DefaultHitDurationSeconds) < 0.0001f,
            "hitmarker: duration clamps into range and NaN falls back to the default");
        check(HitMarkerPolicy.Capacity(0) == HitMarkerPolicy.DefaultCapacity
            && HitMarkerPolicy.Capacity(-3) == HitMarkerPolicy.DefaultCapacity
            && HitMarkerPolicy.Capacity(999) == HitMarkerPolicy.MaxCapacity
            && HitMarkerPolicy.Capacity(5) == 5,
            "hitmarker: the marker queue is capped, so simultaneous targets can never grow it without bound");
        check(!HitMarkerPolicy.ShouldDraw(false, HitMarkerKind.Normal, 1f)
            && !HitMarkerPolicy.ShouldDraw(true, HitMarkerKind.None, 1f)
            && !HitMarkerPolicy.ShouldDraw(true, HitMarkerKind.Normal, 0f)
            && HitMarkerPolicy.ShouldDraw(true, HitMarkerKind.Kill, 0.5f),
            "hitmarker: nothing is drawn when disabled, unnamed or fully faded");

        // ---- world projection gating ------------------------------------------------------
        check(HitMarkerPolicy.IsProjectable(1f, 960f, 540f, 1920f, 1080f, 0f),
            "hitmarker: an impact in front of the camera and on screen is drawable");
        check(!HitMarkerPolicy.IsProjectable(-1f, 960f, 540f, 1920f, 1080f, 0f),
            "hitmarker: an impact behind the camera is never drawn (and never swallows the vanilla indicator)");
        check(!HitMarkerPolicy.IsProjectable(0.01f, 960f, 540f, 1920f, 1080f, 0f),
            "hitmarker: a point at the near plane is rejected instead of being drawn at a bogus spot");
        check(HitMarkerPolicy.IsProjectable(1f, -10f, 540f, 1920f, 1080f, 24f)
            && !HitMarkerPolicy.IsProjectable(1f, -40f, 540f, 1920f, 1080f, 24f),
            "hitmarker: off-screen impacts are clipped by the margin, and a generous margin still has a limit");
        check(!HitMarkerPolicy.IsProjectable(float.NaN, 960f, 540f, 1920f, 1080f, 0f)
            && !HitMarkerPolicy.IsProjectable(1f, float.PositiveInfinity, 540f, 1920f, 1080f, 0f)
            && !HitMarkerPolicy.IsProjectable(1f, 960f, 540f, 0f, 1080f, 0f),
            "hitmarker: NaN/Infinity or a zero-sized screen never projects");

        // ---- impact-scope association (armour verdict) -------------------------------------
        check(HitMarkerPolicy.ImpactMatches(10, 10, 0f) && HitMarkerPolicy.ImpactMatches(10, 9, 0f),
            "hitmarker: a scope from this frame (or the previous one) can describe the indicator event");
        check(!HitMarkerPolicy.ImpactMatches(12, 10, 0f) && !HitMarkerPolicy.ImpactMatches(9, 10, 0f),
            "hitmarker: a stale or out-of-order scope never matches, so a leaked context cannot eat another event");
        check(HitMarkerPolicy.ImpactMatches(10, 10, HitMarkerPolicy.ImpactMatchDistanceSquared)
            && !HitMarkerPolicy.ImpactMatches(10, 10, HitMarkerPolicy.ImpactMatchDistanceSquared + 0.01f),
            "hitmarker: the scope match distance is tight (0.25 m) and inclusive at its edge");
        check(HitMarkerPolicy.ImpactMatchDistanceMetres * 2f < 1f,
            "hitmarker: the impact association radius stays sub-metre, so two enemies standing next to each other cannot trade verdicts");
        check(Math.Abs(HitMarkerPolicy.DistanceSquared(0f, 0f, 0f, 1f, 0f, 0f) - 1f) < 0.0001f
            && float.IsPositiveInfinity(HitMarkerPolicy.DistanceSquared(float.NaN, 0f, 0f, 1f, 0f, 0f)),
            "hitmarker: distance is squared and a broken position never matches anything");

        // ---- one event, one line: merge ONLY on a proven event identity (review F3) --------
        check(HitMarkerPolicy.Resolve(HitMarkerKind.None, 0, HitMarkerKind.Normal, 7) == HitMarkerAction.Added,
            "hitmarker: an isolated hit adds a line");
        check(HitMarkerPolicy.Resolve(HitMarkerKind.Normal, 7, HitMarkerKind.Kill, 7) == HitMarkerAction.Upgraded,
            "hitmarker: the kill line of the SAME damage event upgrades its own hit line instead of drawing a second line");
        check(HitMarkerPolicy.Resolve(HitMarkerKind.Kill, 7, HitMarkerKind.Normal, 7) == HitMarkerAction.Merged
            && HitMarkerPolicy.Resolve(HitMarkerKind.Kill, 7, HitMarkerKind.Weakspot, 7) == HitMarkerAction.Merged,
            "hitmarker: a hit that belongs to the same event as an existing kill line is merged into it");
        check(HitMarkerPolicy.Resolve(HitMarkerKind.Normal, 7, HitMarkerKind.Weakspot, 7) == HitMarkerAction.Merged,
            "hitmarker: two events of the same identity refresh one line instead of stacking");
        check(HitMarkerPolicy.Resolve(HitMarkerKind.Kill, 7, HitMarkerKind.Normal, 8) == HitMarkerAction.Added,
            "hitmarker: a hit from a DIFFERENT event keeps its own line even next to an existing kill (enemy A dies, enemy B is hit)");
        check(HitMarkerPolicy.Resolve(HitMarkerKind.Normal, 7, HitMarkerKind.Kill, 8) == HitMarkerAction.Added,
            "hitmarker: a kill from a different event never upgrades another event's hit line");
        check(HitMarkerPolicy.Resolve(HitMarkerKind.Normal, 7, HitMarkerKind.Normal, 8) == HitMarkerAction.Added,
            "hitmarker: two hits on the same enemy are two events (two fresh lines), not one merged line");
        check(HitMarkerPolicy.Resolve(HitMarkerKind.Normal, 0, HitMarkerKind.Kill, 0) == HitMarkerAction.Added
            && HitMarkerPolicy.Resolve(HitMarkerKind.Kill, 0, HitMarkerKind.Normal, 0) == HitMarkerAction.Added,
            "hitmarker: without an identity nothing is merged - unknown sources are drawn separately rather than swallowed");
        check(HitMarkerPolicy.Resolve(HitMarkerKind.Kill, 7, HitMarkerKind.Normal, 0) == HitMarkerAction.Added
            && HitMarkerPolicy.Resolve(HitMarkerKind.Normal, 0, HitMarkerKind.Kill, 7) == HitMarkerAction.Added,
            "hitmarker: a half-known pair never merges either (identity must be proven on both sides)");
        check(HitMarkerPolicy.Resolve(HitMarkerKind.Normal, 7, HitMarkerKind.None, 7) == HitMarkerAction.Dropped,
            "hitmarker: a none-kind event is dropped");

        // ---- heavy kill classification -----------------------------------------------------
        check(KillPolicy.IsHeavy(1000f, KillPolicy.DefaultHeavyHealthThreshold)
            && !KillPolicy.IsHeavy(999.9f, KillPolicy.DefaultHeavyHealthThreshold),
            "kill-icon: the heavy icon starts exactly at the 1000 HP elite line");
        check(!KillPolicy.IsHeavy(float.NaN, 1000f) && !KillPolicy.IsHeavy(0f, 1000f) && !KillPolicy.IsHeavy(-5f, 1000f),
            "kill-icon: a broken or empty health pool is never heavy");
        check(KillPolicy.IsHeavy(500f, 400f) && !KillPolicy.IsHeavy(500f, 600f),
            "kill-icon: the threshold is configurable in both directions");
        check(Math.Abs(KillPolicy.HeavyHealthThreshold(float.NaN) - KillPolicy.DefaultHeavyHealthThreshold) < 0.0001f
            && Math.Abs(KillPolicy.HeavyHealthThreshold(0f) - KillPolicy.MinHeavyHealthThreshold) < 0.0001f
            && Math.Abs(KillPolicy.HeavyHealthThreshold(9e9f) - KillPolicy.MaxHeavyHealthThreshold) < 0.0001f,
            "kill-icon: the threshold clamps into range and NaN falls back to 1000");
        check(KillPolicy.IconKey(KillKind.Normal, true) == "kill-heavy.png"
            && KillPolicy.IconKey(KillKind.Weakspot, true) == "kill-heavy.png"
            && KillPolicy.IconKey(KillKind.Weakspot, false) == "kill-weakspot.png"
            && KillPolicy.IconKey(KillKind.Normal, false) == "kill-normal.png",
            "kill-icon: heavy wins over weakspot, and the other two keep the BFV light/medium icons");

        // ---- plain-number flight: random line through the body ------------------------------
        FeedbackPolicy.FlightTarget(960f, 540f, 1080f, 7, out float bodyX, out float bodyY, out float exitX, out float exitY);
        check(Math.Abs(bodyX - 960f) < 0.0001f
            && Math.Abs(bodyY - (540f + FeedbackPolicy.BodyDropFraction * 1080f)) < 0.0001f,
            "number-flight: the body point sits centred, slightly below the reticle");
        bool alwaysBelow = true;
        for (int seed = 0; seed < 128; seed++)
        {
            FeedbackPolicy.FlightTarget(960f, 540f, 1080f, seed, out float bx0, out float by0, out float ex0, out float ey0);
            if (!(ey0 > by0)) alwaysBelow = false;
        }
        check(alwaysBelow, "number-flight: every one of 128 seeds exits below the body point, never upward");
        check(Math.Abs(FeedbackPolicy.FlightProgress(FeedbackPolicy.BodyCrossFraction * 0.72f, 0.72f) - FeedbackPolicy.BodyCrossFraction) < 0.001f,
            "number-flight: the crossing lands at the configured fraction of the lifetime, not of the eased curve");
        check(exitY > bodyY && Math.Abs(exitY - bodyY) > 1f,
            "number-flight: the exit is always below the body point, so the glyph passes through");
        check(Math.Abs(FeedbackPolicy.SegmentedAxis(0f, 100f, 400f, FeedbackPolicy.BodyCrossFraction) - 100f) < 0.001f,
            "number-flight: the path crosses the body point exactly at the crossing fraction");
        check(Math.Abs(FeedbackPolicy.SegmentedAxis(0f, 100f, 400f, 0f) - 0f) < 0.0001f
            && Math.Abs(FeedbackPolicy.SegmentedAxis(0f, 100f, 400f, 1f) - 400f) < 0.0001f,
            "number-flight: the path starts where the number spawned and ends at the exit point");
        float justBefore = FeedbackPolicy.SegmentedAxis(0f, 100f, 400f, FeedbackPolicy.BodyCrossFraction - 0.001f);
        float justAfter = FeedbackPolicy.SegmentedAxis(0f, 100f, 400f, FeedbackPolicy.BodyCrossFraction + 0.001f);
        check(Math.Abs(justAfter - justBefore) < 2f,
            "number-flight: the two legs join without a jump");
        check(FeedbackPolicy.SegmentedAxis(float.NaN, 100f, 400f, 0.5f) is float broken && float.IsNaN(broken),
            "number-flight: a broken spawn coordinate is passed through instead of producing NaN mid-path");
        float firstAngle = FeedbackPolicy.NumberFlyRadians(11);
        float secondAngle = FeedbackPolicy.NumberFlyRadians(12);
        check(firstAngle != secondAngle
            && Math.Abs(firstAngle) <= FeedbackPolicy.NumberFlyJitterDegrees * (float)(Math.PI / 180.0) + 0.0001f
            && Math.Abs(secondAngle) <= FeedbackPolicy.NumberFlyJitterDegrees * (float)(Math.PI / 180.0) + 0.0001f,
            "number-flight: every number gets its own bounded bearing");
        FeedbackPolicy.FlightTarget(960f, 540f, 1080f, 11, out float ax, out float ay, out float aex, out float aey);
        FeedbackPolicy.FlightTarget(960f, 540f, 1080f, 12, out float bx, out float by, out float bex, out float bey);
        check(aex != bex || aey != bey,
            "number-flight: two different seeds do not draw the same line");
        FeedbackPolicy.FlightTarget(float.NaN, 540f, 1080f, 3, out float nx, out float ny, out float nex, out float ney);
        check(float.IsNaN(nx) && ny == 540f,
            "number-flight: a broken centre leaves the target alone instead of inventing coordinates");
    }

}
