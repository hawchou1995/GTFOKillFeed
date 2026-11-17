using System;
using System.Collections.Generic;
using HawChow.GTFOKillFeed;

// Prismatic flare and cumulative-damage tiers without Unity: every documented boundary is asserted
// exactly (60.0 stays plain, 60.01 flares; 2999/3000/5000/10000/20000 ladder), garbage input never
// flares and never corrupts the running total, and a reset really returns the state to "no tier".
internal static class DamageFxTests
{
    internal static void Run(Action<bool, string> check)
    {
        const float threshold = DamageFxPolicy.DefaultPrismaticThreshold;
        check(!DamageFxPolicy.IsPrismatic(60f, threshold), "damage-fx: exactly 60.0 with the default threshold stays plain");
        check(DamageFxPolicy.IsPrismatic(60.01f, threshold), "damage-fx: 60.01 flares with the default threshold");
        check(!DamageFxPolicy.IsPrismatic(59.99f, threshold), "damage-fx: 59.99 stays plain");
        check(DamageFxPolicy.IsPrismatic(120f, threshold), "damage-fx: a big hit flares");
        check(!DamageFxPolicy.IsPrismatic(float.NaN, threshold), "damage-fx: NaN damage never flares");
        check(!DamageFxPolicy.IsPrismatic(float.PositiveInfinity, threshold), "damage-fx: infinite damage never flares");
        check(!DamageFxPolicy.IsPrismatic(-5f, threshold), "damage-fx: negative damage never flares");
        check(!DamageFxPolicy.IsPrismatic(500f, 0f), "damage-fx: threshold 0 disables the flare entirely");
        check(Math.Abs(DamageFxPolicy.PrismaticThreshold(float.NaN) - 60f) < 0.001f, "damage-fx: NaN threshold falls back to 60");
        check(DamageFxPolicy.PrismaticThreshold(-3f) == 0f, "damage-fx: a negative threshold clamps to 0 (disabled)");
        check(Math.Abs(DamageFxPolicy.PrismaticThreshold(2e9f) - 1000000f) < 1f, "damage-fx: an over-range threshold clamps to the ceiling");
        check(Math.Abs(DamageFxPolicy.PrismaticThreshold(90f) - 90f) < 0.001f, "damage-fx: an in-range threshold is used as-is");

        check(DamageFxPolicy.TierFor(0, true) == DamageTier.None, "damage-fx: no damage means no tier");
        check(DamageFxPolicy.TierFor(2999, true) == DamageTier.None, "damage-fx: 2999 is still below the white tier");
        check(DamageFxPolicy.TierFor(3000, true) == DamageTier.White, "damage-fx: 3000 enters the white tier");
        check(DamageFxPolicy.TierFor(4999, true) == DamageTier.White, "damage-fx: 4999 is still white");
        check(DamageFxPolicy.TierFor(5000, true) == DamageTier.Silver, "damage-fx: 5000 enters the silver tier");
        check(DamageFxPolicy.TierFor(9999, true) == DamageTier.Silver, "damage-fx: 9999 is still silver");
        check(DamageFxPolicy.TierFor(10000, true) == DamageTier.Gold, "damage-fx: 10000 enters the gold tier");
        check(DamageFxPolicy.TierFor(19999, true) == DamageTier.Gold, "damage-fx: 19999 is still gold");
        check(DamageFxPolicy.TierFor(20000, true) == DamageTier.Diamond, "damage-fx: 20000 enters the diamond tier");
        check(DamageFxPolicy.TierFor(999999, true) == DamageTier.Diamond, "damage-fx: the highest tier is never exceeded");
        check(DamageFxPolicy.TierFor(20000, false) == DamageTier.None, "damage-fx: disabling tiers turns the whole ladder off");
        check(DamageFxPolicy.TierFor(-5, true) == DamageTier.None, "damage-fx: a negative total never reports a tier");

        // ---- configurable tier boundaries (DamageFx/*Threshold) ---------------------------
        var custom = DamageFxPolicy.ResolveTiers(500, 1500, 4000, 9000);
        check(custom.White == 500 && custom.Silver == 1500 && custom.Gold == 4000 && custom.Diamond == 9000,
            "damage-fx: ascending configured boundaries are used exactly as given");
        check(DamageFxPolicy.TierFor(500, true, custom) == DamageTier.White
            && DamageFxPolicy.TierFor(1499, true, custom) == DamageTier.White
            && DamageFxPolicy.TierFor(1500, true, custom) == DamageTier.Silver
            && DamageFxPolicy.TierFor(4000, true, custom) == DamageTier.Gold
            && DamageFxPolicy.TierFor(8999, true, custom) == DamageTier.Gold
            && DamageFxPolicy.TierFor(9000, true, custom) == DamageTier.Diamond,
            "damage-fx: a custom ladder moves every boundary, not just the first one");
        var fallback = DamageFxPolicy.ResolveTiers(0, -10, 0, 0);
        check(fallback.White == DamageFxPolicy.WhiteThreshold && fallback.Silver == DamageFxPolicy.SilverThreshold
            && fallback.Gold == DamageFxPolicy.GoldThreshold && fallback.Diamond == DamageFxPolicy.DiamondThreshold,
            "damage-fx: a threshold of 0 or below falls back to the shipped default for that slot");
        var inverted = DamageFxPolicy.ResolveTiers(9000, 100, 50, 10);
        check(inverted.White == 9000 && inverted.Silver == 9000 && inverted.Gold == 9000 && inverted.Diamond == 9000,
            "damage-fx: an inverted ladder is pushed up so no tier becomes unreachable");
        var overflowed = DamageFxPolicy.ResolveTiers(-4, 0, 0, 5000000000L);
        check(overflowed.White == DamageFxPolicy.WhiteThreshold
            && overflowed.Diamond == DamageFxPolicy.MaxTierThreshold,
            "damage-fx: an out-of-range threshold is clamped instead of wrapping");
        check(DamageFxPolicy.TierFor(8999, true, inverted) == DamageTier.None
            && DamageFxPolicy.TierFor(9000, true, inverted) == DamageTier.Diamond,
            "damage-fx: a collapsed ladder still resolves deterministically at its single boundary");
        check(DamageFxPolicy.TierFor(20000, false, custom) == DamageTier.None,
            "damage-fx: the enable switch still wins over custom boundaries");

        check(DamageFxPolicy.Accumulate(0, 120.7f) == 120, "damage-fx: fractional damage truncates into the total");
        check(DamageFxPolicy.Accumulate(120, float.NaN) == 120, "damage-fx: NaN damage leaves the total untouched");
        check(DamageFxPolicy.Accumulate(120, -40f) == 120, "damage-fx: negative damage leaves the total untouched");
        check(DamageFxPolicy.Accumulate(120, float.PositiveInfinity) == 120, "damage-fx: infinite damage leaves the total untouched");
        check(DamageFxPolicy.Accumulate(-10, 50f) == 50, "damage-fx: a negative stored total is repaired before adding");
        check(DamageFxPolicy.Accumulate(long.MaxValue - 1, 100f) == long.MaxValue, "damage-fx: the total saturates instead of wrapping");
        long running = 0;
        running = DamageFxPolicy.Accumulate(running, 2500f);
        check(DamageFxPolicy.TierFor(running, true) == DamageTier.None, "damage-fx: 2500 accumulated is still no tier");
        running = DamageFxPolicy.Accumulate(running, 600f);
        check(DamageFxPolicy.TierFor(running, true) == DamageTier.White, "damage-fx: crossing 3000 by accumulation promotes to white");
        check(DamageFxPolicy.TierFor(0, true) == DamageTier.None, "damage-fx: a level reset (total back to 0) drops back to no tier");

        DamageFxPolicy.PrismaticColor(0f, out float r0, out float g0, out float b0);
        DamageFxPolicy.PrismaticColor(1f / 3f, out float r1, out float g1, out float b1);
        DamageFxPolicy.PrismaticColor(2f / 3f, out float r2, out float g2, out float b2);
        check(Math.Abs(r0 - r1) + Math.Abs(g0 - g1) + Math.Abs(b0 - b1) > 0.5f
            && Math.Abs(r1 - r2) + Math.Abs(g1 - g2) + Math.Abs(b1 - b2) > 0.5f
            && Math.Abs(r0 - r2) + Math.Abs(g0 - g2) + Math.Abs(b0 - b2) > 0.5f,
            "damage-fx: three prismatic phase samples give three visibly different colours");
        check(r0 > 0.9f && g0 < 0.2f && b0 < 0.2f, "damage-fx: phase 0 is red");
        check(b2 > 0.9f, "damage-fx: phase 2/3 has a blue channel at full value");

        DamageFxPolicy.TierColor(DamageTier.None, out float nr, out float ng, out float nb);
        check(nr == 0f && ng == 0f && nb == 0f, "damage-fx: no tier has no colour");
        DamageFxPolicy.TierColor(DamageTier.White, out float wr, out float wg, out float wb);
        DamageFxPolicy.TierColor(DamageTier.Silver, out float sr, out float sg, out float sb);
        DamageFxPolicy.TierColor(DamageTier.Gold, out float gr, out float gg, out float gb);
        DamageFxPolicy.TierColor(DamageTier.Diamond, out float dr, out float dg, out float db);
        check(wr == 1f && wg == 1f && wb == 1f, "damage-fx: white is pure white");
        check(wr != sr || wb != sb, "damage-fx: silver differs from white");
        check(gr > gg && gg > gb, "damage-fx: gold is a warm colour");
        check(db > dr, "damage-fx: diamond is a cool colour");
        check(DamageFxPolicy.OutlineStrength(DamageTier.None) == 0f
            && DamageFxPolicy.OutlineStrength(DamageTier.Gold) > 0f,
            "damage-fx: the tier outline tint is only applied once a tier exists");

        check(DamageFxPolicy.PhaseFor(0f) == 0f, "damage-fx: phase starts at 0");
        check(Math.Abs(DamageFxPolicy.PhaseFor(1.8f)) < 0.0001f, "damage-fx: one cycle wraps back to 0");
        check(DamageFxPolicy.PhaseFor(float.NaN) == 0f, "damage-fx: a broken clock yields phase 0");
        check(Math.Abs(DamageFxPolicy.WrapPhase(-0.25f) - 0.75f) < 0.0001f, "damage-fx: a negative phase wraps into [0,1)");

        // ---- numbers fly at the viewer (FeedbackPolicy.Approach*) ---------------------------
        const float life = FeedbackPolicy.NumberLifetime;
        check(FeedbackPolicy.ApproachT(0f, life) == 0f, "number-motion: a fresh number starts at the hit point");
        check(Math.Abs(FeedbackPolicy.ApproachT(life * FeedbackPolicy.ApproachWindowFraction, life) - 1f) < 0.0001f,
            "number-motion: the flight finishes at the end of the approach window");
        check(Math.Abs(FeedbackPolicy.ApproachT(life * 2f, life) - 1f) < 0.0001f,
            "number-motion: the flight stays at the target instead of overshooting");
        float midTravel = FeedbackPolicy.ApproachT(life * FeedbackPolicy.ApproachWindowFraction * 0.5f, life);
        check(Math.Abs(midTravel - (float)Math.Pow(0.5, FeedbackPolicy.ApproachEasePower)) < 0.0005f,
            "number-motion: the approach is back-loaded (half the window is only ~17% of the travel)");
        check(FeedbackPolicy.ApproachWindowFraction == 1f
            && FeedbackPolicy.ApproachT(life * 0.95f, life) > 0.8f
            && FeedbackPolicy.ApproachT(life * 0.95f, life) < 1f,
            "number-motion: the flight fills the whole lifetime, so the number is still moving when it fades");
        check(FeedbackPolicy.ApproachT(life * 0.1f, life) < FeedbackPolicy.ApproachT(life * 0.3f, life)
            && FeedbackPolicy.ApproachT(life * 0.3f, life) < FeedbackPolicy.ApproachT(life * 0.6f, life),
            "number-motion: the approach is monotonic");
        check(FeedbackPolicy.ApproachAxis(100f, 500f, 0f) == 100f, "number-motion: t=0 keeps the projected position");
        check(FeedbackPolicy.ApproachAxis(100f, 500f, 1f) == 500f, "number-motion: t=1 lands on the target");
        check(Math.Abs(FeedbackPolicy.ApproachAxis(100f, 500f, 0.5f) - 300f) < 0.0001f, "number-motion: the axis lerp is linear in t");
        check(float.IsNaN(FeedbackPolicy.ApproachAxis(float.NaN, 500f, 0.5f)),
            "number-motion: a broken projection is left alone instead of being snapped to the centre");
        check(FeedbackPolicy.ApproachScale(0f) == 1f, "number-motion: the glyph starts at its normal size");
        check(Math.Abs(FeedbackPolicy.ApproachScale(1f) - (1f + FeedbackPolicy.ApproachGrowth)) < 0.0001f,
            "number-motion: the glyph grows to the documented scale at the target");
        check(Math.Abs(FeedbackPolicy.ApproachScale(5f) - (1f + FeedbackPolicy.ApproachGrowth)) < 0.0001f,
            "number-motion: the growth is clamped past the window");
        check(FeedbackPolicy.ApproachScale(float.NaN) == 1f, "number-motion: a broken clock never inflates the glyph");

        // ---- damage-number weight (粗细) ----------------------------------------------------
        check(FeedbackPolicy.NumberWeight(float.NaN) == 1f, "number-weight: NaN falls back to the shipped weight 1");
        check(FeedbackPolicy.NumberWeight(-1f) == 0f && FeedbackPolicy.NumberWeight(5f) == 2f
            && Math.Abs(FeedbackPolicy.NumberWeight(1.5f) - 1.5f) < 0.0001f,
            "number-weight: the configured weight clamps to 0-2 and passes through in range");
        check(!FeedbackPolicy.NumberUsesBold(0.49f) && FeedbackPolicy.NumberUsesBold(0.5f) && FeedbackPolicy.NumberUsesBold(1f),
            "number-weight: the face switches to Bold at 0.5 and stays bold upward");
        check(Math.Abs(FeedbackPolicy.NumberOutlineRadius(1f) - 1.6f) < 0.0001f,
            "number-weight: weight 1 keeps the shipped 1.6 px outline radius exactly");
        check(Math.Abs(FeedbackPolicy.NumberOutlineRadius(0f) - 0.9f) < 0.0001f
            && Math.Abs(FeedbackPolicy.NumberOutlineRadius(2f) - 2.3f) < 0.0001f,
            "number-weight: the outline radius scales from 0.9 to 2.3 px across the range");
        check(FeedbackPolicy.NumberThickenPasses(1f) == 0 && FeedbackPolicy.NumberThickenPasses(0.4f) == 0,
            "number-weight: weight 1 and below add no body stamps, so the shipped look is bit-identical");
        check(FeedbackPolicy.NumberThickenPasses(1.4f) == FeedbackPolicy.NumberThickenPassesMax
            && FeedbackPolicy.NumberThickenPasses(2f) == 4,
            "number-weight: anything above 1 stamps the body four times");
        check(FeedbackPolicy.NumberThickenRadius(1f) == 0f
            && Math.Abs(FeedbackPolicy.NumberThickenRadius(2f) - 0.55f) < 0.0001f,
            "number-weight: the stamp radius grows from 0 at weight 1 to 0.55 px at weight 2");
        check(Math.Abs(FeedbackPolicy.ApproachTargetX(500f, 22f, 1f) - (500f + 22f * FeedbackPolicy.ApproachFanFraction)) < 0.0001f,
            "number-motion: each number settles on its own lateral slot, not on the exact centre");
        check(Math.Abs(FeedbackPolicy.ApproachTargetY(300f, 22f, 1f)
            - (300f - (FeedbackPolicy.ApproachRisePixels + 22f * FeedbackPolicy.ApproachRiseFraction))) < 0.0001f,
            "number-motion: the settle point sits above the reticle and rises with the slot");
        check(FeedbackPolicy.PrismaticApproachScale(0f) == 1f
            && Math.Abs(FeedbackPolicy.PrismaticApproachScale(1f) - (1f + FeedbackPolicy.PrismaticApproachGrowth)) < 0.0001f
            && FeedbackPolicy.PrismaticApproachScale(1f) > FeedbackPolicy.ApproachScale(1f),
            "number-motion: a prismatic hit grows far more than a normal one (1.45x vs 2.45x)");
        float normalTargetX = FeedbackPolicy.ApproachTargetX(500f, 22f, 1f, false);
        float prismTargetX = FeedbackPolicy.ApproachTargetX(500f, 22f, 1f, true);
        float normalTargetY = FeedbackPolicy.ApproachTargetY(300f, 22f, 1f, false);
        float prismTargetY = FeedbackPolicy.ApproachTargetY(300f, 22f, 1f, true);
        check(Math.Abs(prismTargetX - 500f) < Math.Abs(normalTargetX - 500f)
            && Math.Abs(prismTargetY - 300f) < Math.Abs(normalTargetY - 300f),
            "number-motion: a prismatic hit lands closer to the reticle than a normal one");
        check(FeedbackPolicy.ApproachTargetX(500f, float.NaN, 1f) == 500f
            && FeedbackPolicy.ApproachTargetY(300f, float.NaN, 1f) == 300f,
            "number-motion: a broken side offset falls back to the centre instead of producing NaN targets");
        // ---- prismatic bearing: random approach direction, one shared destination -----------------
        float minJitter = 0f, maxJitter = 0f;
        var bearings = new HashSet<int>();
        bool sawNegative = false, sawPositive = false;
        float jitterLimit = FeedbackPolicy.PrismaticFlyJitterDegrees * (float)(Math.PI / 180.0);
        for (int seed = 0; seed < 64; seed++)
        {
            float angle = FeedbackPolicy.FlyJitterRadians(seed);
            check(Math.Abs(angle) <= jitterLimit + 0.0001f,
                "fly-jitter: seed " + seed + " stays inside the configured bearing range");
            if (angle < minJitter) minJitter = angle;
            if (angle > maxJitter) maxJitter = angle;
            if (angle < 0f) sawNegative = true;
            if (angle > 0f) sawPositive = true;
            bearings.Add((int)(angle * 10000f));
        }
        check(bearings.Count >= 40, "fly-jitter: 64 seeds give well spread bearings instead of a handful of values");
        check(sawNegative && sawPositive, "fly-jitter: the bearing goes both left and right");
        check(minJitter < -0.3f && maxJitter > 0.3f, "fly-jitter: the spread really covers both sides, not a tiny wobble");
        check(FeedbackPolicy.FlyJitterRadians(7) == FeedbackPolicy.FlyJitterRadians(7),
            "fly-jitter: the same seed always yields the same bearing, so a number never re-rolls mid-flight");
        float prismTargetLeft = FeedbackPolicy.ApproachTargetX(500f, -33f, 1f, true);
        float prismTargetRight = FeedbackPolicy.ApproachTargetX(500f, 33f, 1f, true);
        float prismTargetUp = FeedbackPolicy.ApproachTargetY(300f, -33f, 1f, true);
        float prismTargetDown = FeedbackPolicy.ApproachTargetY(300f, 33f, 1f, true);
        check(prismTargetLeft == 500f && prismTargetRight == 500f && prismTargetUp == prismTargetDown,
            "fly-jitter: every prismatic number shares ONE destination, whatever its side slot is");
        FeedbackPolicy.RotateAroundTarget(500f, 300f, 500f, 300f, 1.1f, out float sameX, out float sameY);
        check(sameX == 500f && sameY == 300f,
            "fly-jitter: arriving on the target is a fixed point of the rotation, so the destination cannot drift");
        FeedbackPolicy.RotateAroundTarget(600f, 300f, 500f, 300f, (float)(Math.PI / 2.0), out float rotX, out float rotY);
        check(Math.Abs(rotX - 500f) < 0.001f && Math.Abs(rotY - 400f) < 0.001f,
            "fly-jitter: a quarter turn swings the approach vector around the target and keeps its length");
        FeedbackPolicy.RotateAroundTarget(float.NaN, 300f, 500f, 300f, 0.5f, out float nanX, out float nanY);
        check(float.IsNaN(nanX) && nanY == 300f,
            "fly-jitter: a broken position is passed through untouched instead of producing a second NaN");
        // ---- holographic foil (棱彩 = material, not a hue swap) ----------------------------
        var offsets = new HashSet<string>();
        for (int direction = 0; direction < 8; direction++)
        {
            DamageFxPolicy.FoilDirection(direction, out int dx, out int dy);
            check(dx != 0 || dy != 0, "foil: rim direction " + direction + " is a real outline offset");
            offsets.Add(dx + "," + dy);
        }
        check(offsets.Count == 8, "foil: the eight rim passes are eight distinct directions");
        var rimHues = new HashSet<int>();
        for (int direction = 0; direction < 8; direction++)
        {
            DamageFxPolicy.FoilRimColor(direction, 0.1f, out float rr, out float rg, out float rb);
            rimHues.Add(((int)(rr * 100) << 16) | ((int)(rg * 100) << 8) | (int)(rb * 100));
        }
        check(rimHues.Count == 8, "foil: every rim direction gets its own pastel hue");
        check(DamageFxPolicy.FoilSheen(0f) > 0f && DamageFxPolicy.FoilSheen(0f) < 1f
            && DamageFxPolicy.FoilSheen(0.13f) != DamageFxPolicy.FoilSheen(0.4f),
            "foil: the shimmer pulses inside (0,1) and moves with the phase");
        DamageFxPolicy.FoilBodyColor(0.2f, out float bRed, out float bGreen, out float bBlue);
        float bodyMax = Math.Max(bRed, Math.Max(bGreen, bBlue));
        float bodyMin = Math.Min(bRed, Math.Min(bGreen, bBlue));
        check(bodyMin > 0.5f && bodyMax - bodyMin < 0.5f,
            "foil: the body is a near-white pastel core, not a saturated hue");
        DamageFxPolicy.FoilEmbossOffset(0.25f, out float ex, out float ey);
        check(Math.Abs(Math.Sqrt(ex * ex + ey * ey) - DamageFxPolicy.FoilEmbossRadius) < 0.02f,
            "foil: the emboss highlight stays a small fixed-radius offset");
        DamageFxPolicy.FoilEmbossOffset(0.6f, out float ex2, out float ey2);
        check(Math.Abs(ex - ex2) + Math.Abs(ey - ey2) > 0.05f, "foil: the emboss highlight sweeps around the glyph");
        DamageFxPolicy.HueColor(0f, 1f, 1f, out float h0r, out float h0g, out float h0b);
        DamageFxPolicy.HueColor(1f / 3f, 1f, 1f, out float h1r, out float h1g, out float h1b);
        check(h0r > 0.99f && h0g < 0.01f && h0b < 0.01f && h1g > 0.99f && h1r < 0.01f,
            "foil: the shared hue conversion keeps phase 0 red and phase 1/3 green");

    }
}
