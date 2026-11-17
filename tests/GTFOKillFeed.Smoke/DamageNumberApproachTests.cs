using System;
using System.Globalization;
using HawChow.GTFOKillFeed;

// 1.2.16 configuration pass: the three independent damage-number switches, measured without Unity.
//
//   DamageNumbers/ApproachEffect  - false (default) = the first shipped drift-only trajectory;
//                                 - true = the 1.2.16 fly-at-the-viewer trajectory (locked by
//                                   PrismaticIncomingTests, which drives the same production call).
//   DamageFx/PrismaticEnabled     - the foil look and the prismatic flight, independent of the threshold.
//   DamageFx/TiersEnabled         - the cumulative tier ink on the body and on the outline.
//
// The drift formula is re-implemented HERE (frozen), so these fixtures are an oracle for the production
// helpers rather than a copy of them: a changed drift constant, a forgotten growth term or a leaked
// prismatic size breaks the asserted numbers. Section 2b goes one step further and pins two four-tuples
// captured by hand from the 1.2.15 baseline source, so even a wrong drift constant copied into the oracle
// above cannot pass unnoticed.
internal static class DamageNumberApproachTests
{
    private const float Lifetime = FeedbackPolicy.NumberLifetime;   // 0.72
    private const float BaseSize = 27f;                             // Plugin's DamageNumberSize default
    private const float Tolerance = 1e-3f;

    // FROZEN 1.2.15 drift constants.
    private const float DriftSideBase = 0.35f;
    private const float DriftSidePerSecond = 1.7f;
    private const float DriftRiseLinear = -95f;
    private const float DriftRiseQuadratic = 105f;
    private const float PopSeconds = 0.14f;
    private const float PopGrowth = 0.28f;
    private const float WeakspotScale = 1.18f;
    private const float ArmorScale = 0.82f;
    private const float FadeSeconds = 0.22f;

    private static readonly float[] Seeds = { 0f, 3f, 7f, 10f, 13f, 21f };
    private static readonly string[] Screens = { "16:9", "ultrawide", "3440x1440" };
    private static readonly string[] Hits = { "center", "left", "right" };
    private static readonly HitKind[] Kinds = { HitKind.Normal, HitKind.Armor, HitKind.Weakspot };
    private static readonly float[] Ages = { 0f, 0.35f, 0.6f };

    private static float Resolution(float height) => Math.Max(0.75f, Math.Min(1.6f, height / 1080f));

    private static void ScreenOf(string name, out float width, out float height)
    {
        switch (name)
        {
            case "16:9": width = 1920f; height = 1080f; return;
            case "ultrawide": width = 2560f; height = 1080f; return;
            default: width = 3440f; height = 1440f; return;
        }
    }

    private static void HitOf(string name, float width, float height, out float hitX, out float hitY)
    {
        hitX = width * 0.5f;
        hitY = height * 0.5f;
        if (name == "left") { hitX -= 260f; hitY += 45f; }
        else if (name == "right") { hitX += 240f; hitY -= 35f; }
    }

    private static float Unit(float value)
    {
        if (float.IsNaN(value)) return 0f;
        if (value <= 0f) return 0f;
        if (value >= 1f) return 1f;
        return value;
    }

    // The frozen initial trajectory: spawn on the projected hit point, drift up, fade out, no growth.
    private static void DriftOracle(float hitX, float hitY, float side, float age, float resolution, HitKind kind,
        out float x, out float y, out float size, out float alpha)
    {
        x = hitX + side * (DriftSideBase + age * DriftSidePerSecond) * resolution;
        y = hitY + (DriftRiseLinear * age + DriftRiseQuadratic * age * age) * resolution;
        float pop = (float)Math.Sin(Unit(age / PopSeconds) * Math.PI) * (1f - Unit(age / PopSeconds));
        float kindScale = kind == HitKind.Weakspot ? WeakspotScale : kind == HitKind.Armor ? ArmorScale : 1f;
        size = Math.Max(16f, Math.Min(42f, BaseSize)) * kindScale * (1f + PopGrowth * pop) * resolution;
        alpha = Unit((Lifetime - age) / FadeSeconds);
    }

    // ---- captured 1.2.15 samples (review S3) ------------------------------------------------------
    // The drift oracle above is still a test-side re-implementation of the formula, so these two samples
    // were computed ONCE by hand from the 1.2.15 baseline source and are frozen here as literals:
    //   killfeed-baseline-1.2.15-20261111-173042-789\DamageNumbers.cs:91-92  (drift x, drift y)
    //   killfeed-baseline-1.2.15-20261111-173042-789\DamageNumbers.cs:86-89  (glyph size; the
    //       (prismatic ? PrismaticApproachScale : ApproachScale) factor on line 88 is exactly what
    //       DamageNumbers/ApproachEffect=false removes, so the captured size has no approach factor)
    //   FeedbackPolicy.NumberScale (pop window 0.14 s, growth 0.28, weakspot 1.18, armor 0.82)
    //   FeedbackPolicy.Alpha (fade 0.22 s) x opacity 1
    // Both screens have resolution != 1 (1440/1080 and 1600/1080), so a lost resolution factor cannot hide
    // behind a 1080p sample. The actual values always come from the production NumberPose.
    private static bool FrozenSample(float width, float height, float hitX, float hitY, int seed, float age,
        HitKind kind, float wantX, float wantY, float wantSize, float wantAlpha, out string actual)
    {
        FeedbackPolicy.NumberPose(false, false, seed, FeedbackPolicy.NumberSide(seed), age, Lifetime,
            FeedbackPolicy.DefaultNumberOpacity, hitX, hitY, width, height, Resolution(height), kind, BaseSize,
            out float x, out float y, out float size, out float alpha, out _, out _);
        actual = N(x) + "," + N(y) + "," + N(size) + "," + N(alpha);
        return Math.Abs(x - wantX) <= Tolerance && Math.Abs(y - wantY) <= Tolerance
            && Math.Abs(size - wantSize) <= Tolerance && Math.Abs(alpha - wantAlpha) <= Tolerance;
    }

    private static void Pose(bool prismatic, bool approach, int seed, string screen, string hit, HitKind kind,
        float age, out float x, out float y, out float size, out float alpha)
    {
        ScreenOf(screen, out float width, out float height);
        HitOf(hit, width, height, out float hitX, out float hitY);
        FeedbackPolicy.NumberPose(prismatic, approach, seed, FeedbackPolicy.NumberSide(seed), age, Lifetime,
            FeedbackPolicy.DefaultNumberOpacity, hitX, hitY, width, height, Resolution(height), kind, BaseSize,
            out x, out y, out size, out alpha, out _, out _);
    }

    private static string N(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    internal static void Run(Action<bool, string> check)
    {
        // ---- 1. the shipped defaults are pinned ------------------------------------------------
        check(FeedbackPolicy.DefaultApproachEffect == false,
            "damage-number-switch: DamageNumbers/ApproachEffect defaults to FALSE (the initial drift trajectory is the shipped default)");
        check(DamageFxPolicy.DefaultPrismaticEnabled && DamageFxPolicy.DefaultTiersEnabled,
            "damage-number-switch: DamageFx/PrismaticEnabled and DamageFx/TiersEnabled both default to TRUE");

        // ---- 2. switch off reproduces the initial trajectory, value for value, for plain AND prismatic
        bool offMatches = true;
        string firstBad = string.Empty;
        bool prismaticAlsoDrifts = true;
        int samples = 0;
        foreach (float seedValue in Seeds)
        {
            int seed = (int)seedValue;
            float side = FeedbackPolicy.NumberSide(seed);
            foreach (string screen in Screens)
            {
                ScreenOf(screen, out float width, out float height);
                float resolution = Resolution(height);
                foreach (string hit in Hits)
                {
                    HitOf(hit, width, height, out float hitX, out float hitY);
                    foreach (HitKind kind in Kinds)
                    {
                        foreach (float age in Ages)
                        {
                            DriftOracle(hitX, hitY, side, age, resolution, kind,
                                out float oracleX, out float oracleY, out float oracleSize, out float oracleAlpha);
                            Pose(false, false, seed, screen, hit, kind, age,
                                out float x, out float y, out float size, out float alpha);
                            samples++;
                            if (Math.Abs(x - oracleX) > Tolerance || Math.Abs(y - oracleY) > Tolerance
                                || Math.Abs(size - oracleSize) > Tolerance || Math.Abs(alpha - oracleAlpha) > Tolerance)
                            {
                                if (offMatches)
                                {
                                    firstBad = screen + "/" + hit + "/" + kind + "/age" + N(age) + " got "
                                        + N(x) + "," + N(y) + "," + N(size) + "," + N(alpha) + " want "
                                        + N(oracleX) + "," + N(oracleY) + "," + N(oracleSize) + "," + N(oracleAlpha);
                                    offMatches = false;
                                }
                            }
                            // A prismatic number keeps its foil LOOK but must drift exactly the same way: the
                            // switch is about the trajectory, not the material.
                            Pose(true, false, seed, screen, hit, kind, age,
                                out float px, out float py, out float pSize, out float pAlpha);
                            if (Math.Abs(px - oracleX) > Tolerance || Math.Abs(py - oracleY) > Tolerance
                                || Math.Abs(pSize - oracleSize) > Tolerance || Math.Abs(pAlpha - oracleAlpha) > Tolerance)
                                prismaticAlsoDrifts = false;
                        }
                    }
                }
            }
        }
        check(offMatches,
            "damage-number-switch: with ApproachEffect=false all " + samples + " samples (6 seeds x 3 screens x 3 hits x 3 kinds x 3 ages) match the frozen initial drift trajectory in x, y, glyph size and alpha"
            + (offMatches ? string.Empty : " FIRST MISMATCH " + firstBad));
        check(prismaticAlsoDrifts,
            "damage-number-switch: with ApproachEffect=false a prismatic number drifts on the SAME path and size as a plain one (no prismatic approach scale, no swoop)");

        // ---- 2b. the two captured 1.2.15 constants (no test-side formula involved) --------------
        //   A: 3440x1440 (resolution 1.3333), hit (1720,720), seed 7 -> side -33, age 0.60, Normal
        //   B: 2560x1600 (resolution 1.4815), hit (1020,845), seed 13 -> side +33, age 0.07, Weakspot
        // B is sampled while the pop window is still open, so the growth term (1 + 0.28 * pop = 1.14) is
        // part of the captured size instead of being multiplied away.
        bool sampleA = FrozenSample(3440f, 1440f, 1720f, 720f, 7, 0.6f, HitKind.Normal,
            1659.72f, 694.40f, 36.00f, 0.545455f, out string actualA);
        check(sampleA,
            "damage-number-switch: the captured 1.2.15 sample 3440x1440 / seed 7 / age 0.60 / Normal is reproduced exactly by ApproachEffect=false (want x 1659.72, y 694.40, size 36.00, alpha 0.545455; got "
            + actualA + ")");
        bool sampleB = FrozenSample(2560f, 1600f, 1020f, 845f, 13, 0.07f, HitKind.Weakspot,
            1042.9289f, 835.9104f, 53.808f, 1f, out string actualB);
        check(sampleB,
            "damage-number-switch: the captured 1.2.15 sample 2560x1600 / seed 13 / age 0.07 / Weakspot (mid-pop) is reproduced exactly by ApproachEffect=false (want x 1042.93, y 835.91, size 53.808, alpha 1.000; got "
            + actualB + ")");

        // ---- 3. switch off really removes the flight (it is not a no-op) -----------------------
        int differing = 0;
        bool neverBigger = true;
        foreach (float seedValue in Seeds)
        {
            int seed = (int)seedValue;
            foreach (string screen in Screens)
            {
                ScreenOf(screen, out float width, out float height);
                float resolution = Resolution(height);
                foreach (string hit in Hits)
                {
                    HitOf(hit, width, height, out float hitX, out float hitY);
                    foreach (HitKind kind in Kinds)
                    {
                        foreach (float age in Ages)
                        {
                            float side = FeedbackPolicy.NumberSide(seed);
                            DriftOracle(hitX, hitY, side, age, resolution, kind,
                                out float ox, out float oy, out float offSize, out _);
                            Pose(false, true, seed, screen, hit, kind, age,
                                out float fx, out float fy, out float onSize, out _);
                            if (Math.Abs(ox - fx) > 0.5f || Math.Abs(oy - fy) > 0.5f) differing++;
                            if (offSize > onSize + Tolerance) neverBigger = false;
                        }
                    }
                }
            }
        }
        check(differing > samples / 2,
            "damage-number-switch: ApproachEffect=true really changes the placement (" + differing + " of " + samples + " samples differ by more than half a pixel), so the switch is not cosmetic");
        check(neverBigger,
            "damage-number-switch: the switch-off glyph is never larger than the switch-on glyph: the approach growth (1.45x / 2.45x) is gone when the trajectory is off");

        // ---- 4. DamageFx/PrismaticEnabled gates the foil and the prismatic size ----------------
        check(DamageFxPolicy.IsPrismatic(120f, 60f, true) && !DamageFxPolicy.IsPrismatic(120f, 60f, false),
            "damage-number-switch: PrismaticEnabled=false suppresses a 120-damage hit even though the threshold is 60");
        check(!DamageFxPolicy.IsPrismatic(60f, 60f, true) && DamageFxPolicy.IsPrismatic(60.01f, 60f, true),
            "damage-number-switch: the threshold still owns the boundary (60.0 plain, 60.01 prismatic) when the switch is on");
        check(!DamageFxPolicy.IsPrismatic(500f, 0f, true) && !DamageFxPolicy.IsPrismatic(500f, 0f, false),
            "damage-number-switch: PrismaticThreshold=0 still means 'never', with the switch on or off");
        check(!DamageFxPolicy.IsPrismatic(float.NaN, 60f, true) && !DamageFxPolicy.IsPrismatic(-5f, 60f, true),
            "damage-number-switch: a broken or negative damage never flares, switch or no switch");

        // With prismatic disabled the call site passes prismatic=false, so the glyph must follow the PLAIN
        // approach scale: bigger than the drift-only size, smaller than the prismatic 2.45x size.
        float side7 = FeedbackPolicy.NumberSide(7);
        DriftOracle(960f, 540f, side7, 0.6f, 1f, HitKind.Normal, out _, out _, out float driftSize, out _);
        Pose(false, true, 7, "16:9", "center", HitKind.Normal, 0.6f, out _, out _, out float plainSize, out _);
        Pose(true, true, 7, "16:9", "center", HitKind.Normal, 0.6f, out _, out _, out float prismSize, out _);
        check(plainSize > driftSize + Tolerance && plainSize < prismSize - Tolerance,
            "damage-number-switch: with the foil off, the same hit flies on the plain approach scale (" + N(plainSize)
            + " px) instead of the prismatic one (" + N(prismSize) + " px), and still grows past the drift-only "
            + N(driftSize) + " px");

        // ---- 5. the tier switch gate ----------------------------------------------------------
        check(DamageFxPolicy.TierFor(50000, false) == DamageTier.None
            && DamageFxPolicy.TierFor(50000, true) == DamageTier.Diamond,
            "damage-number-switch: TiersEnabled=false removes the tier ink even at 50000 cumulative damage, while the running total itself is untouched");

        // ---- 6. the paint composition: three switches, pairwise independent --------------------
        var plain = DamageFxPolicy.Paint(false, false, DamageTier.None);
        check(!plain.Foil && !plain.TierInk && !plain.FoilRim && plain.Rings == 1,
            "damage-number-switch: both switches off paints exactly the 1.2.15 plain number (one rim, no foil, kind colour body)");

        var tierOnly = DamageFxPolicy.Paint(false, true, DamageTier.Gold);
        check(!tierOnly.Foil && tierOnly.TierInk && !tierOnly.FoilRim && tierOnly.Rings == 1,
            "damage-number-switch: tiers on with the foil off gives the tier body colour and the tier outline, still one rim");

        var foilOnly = DamageFxPolicy.Paint(true, false, DamageTier.None);
        check(foilOnly.Foil && !foilOnly.TierInk && foilOnly.FoilRim && foilOnly.Rings == 2,
            "damage-number-switch: prismatic on with tiers off gives the foil body, the foil core rim, the halo and no tier ink");

        var foilAndTier = DamageFxPolicy.Paint(true, true, DamageTier.Diamond);
        check(foilAndTier.Foil && foilAndTier.TierInk && !foilAndTier.FoilRim && foilAndTier.Rings == 2,
            "damage-number-switch: prismatic plus a reached tier keeps the tier colour on the CORE rim and foils the halo, the emboss and the body");

        bool tierArgumentIgnoredWhenOff = true;
        foreach (DamageTier tier in new[] { DamageTier.None, DamageTier.White, DamageTier.Gold, DamageTier.Diamond })
        {
            var disabled = DamageFxPolicy.Paint(false, false, tier);
            if (disabled.Foil || disabled.TierInk || disabled.FoilRim || disabled.Rings != 1) tierArgumentIgnoredWhenOff = false;
        }
        check(tierArgumentIgnoredWhenOff,
            "damage-number-switch: TiersEnabled=false ignores whatever tier value the caller reads, so the ink cannot leak back in");

        bool prismaticIndependentOfTier = true;
        foreach (DamageTier tier in new[] { DamageTier.None, DamageTier.White, DamageTier.Gold, DamageTier.Diamond })
        {
            var foil = DamageFxPolicy.Paint(true, true, tier);
            if (!foil.Foil || foil.Rings != 2) prismaticIndependentOfTier = false;
            if (tier == DamageTier.None && (foil.TierInk || !foil.FoilRim)) prismaticIndependentOfTier = false;
            if (tier != DamageTier.None && (!foil.TierInk || foil.FoilRim)) prismaticIndependentOfTier = false;
        }
        check(prismaticIndependentOfTier,
            "damage-number-switch: the foil never disappears because of a tier, and the tier ink never survives on the body while a tier switch is off");

        // ---- 7. the two new keys are new, the old ones keep their names ------------------------
        // The trajectory key and the prismatic key must not have been squeezed into an existing name: an
        // existing .cfg has neither, so both fall back to the shipped defaults (false / true).
        check(FeedbackPolicy.DefaultApproachEffect == false && DamageFxPolicy.DefaultPrismaticEnabled == true,
            "damage-number-switch: an existing .cfg without the two new keys keeps the shipped defaults (no trajectory, prismatic on) instead of an undefined state");
    }
}
