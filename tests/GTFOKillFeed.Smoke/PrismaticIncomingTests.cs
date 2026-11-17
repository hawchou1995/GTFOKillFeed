using System;
using System.Globalization;
using HawChow.GTFOKillFeed;

// 1.2.16 prismatic incoming pass, measured without Unity.
//
// Two things are locked here:
//  * the plain number flight (normal / weakspot / armour) is pinned to the exact 1.2.15 coordinates,
//    glyph size and alpha. The table below was printed by a harness compiled against the 1.2.15
//    snapshot source, so a future change to a shared flight constant fails instead of silently
//    re-drawing every ordinary number;
//  * the prismatic numbers are compared against a local re-implementation of the 1.2.15 prismatic
//    flight (34 degree bearing, 2.6 ease, 1.10 growth, rotation only, no swoop). At every sample with
//    alpha >= 0.5 the new glyph must be larger and its signed random offset larger than that baseline,
//    the seeded side must never flip mid-flight, and the whole excursion must stay bounded.
internal static class PrismaticIncomingTests
{
    private const float Lifetime = FeedbackPolicy.NumberLifetime;
    private const float BaseSize = 27f;                       // Plugin's DamageNumberSize default
    private const float PrismaticTargetRise = 4f;             // 1.2.15 PrismaticApproachRisePixels
    private const float VisibleAlphaFloor = 0.5f;
    // Hard cap of the swoop + bearing excursion, in 1080p pixels before the resolution scale. The
    // measured worst case over seeds 0..63 on 16:9, ultrawide and 3440x1440 is asserted below.
    private const float MaxLateralPixels = 200f;

    // 1.2.15 prismatic constants, frozen on purpose: the live class now holds the 1.2.16 values, so the
    // baseline has to be stated here to keep the comparison honest.
    private const float Baseline153EasePower = 2.6f;
    private const float Baseline153Growth = 1.10f;
    private const float Baseline153JitterDegrees = 34f;

    // seed, screen, hit, kind, age -> x, y, glyph size, alpha. Printed by the 1.2.15 snapshot harness.
    private static readonly string[] PlainNumbers153 =
    {
        "7,16:9,center,Normal,0f,948.45,540,27,1",
        "7,16:9,center,Normal,0.35f,948.2501,599.6261,28.862465,1",
        "7,16:9,center,Normal,0.6f,1050.9354,668.29156,34.56319,0.54545456",
        "7,16:9,center,Armor,0f,948.45,540,22.14,1",
        "7,16:9,center,Armor,0.35f,948.2501,599.6261,23.667221,1",
        "7,16:9,center,Armor,0.6f,1050.9354,668.29156,28.341816,0.54545456",
        "7,16:9,center,Weakspot,0f,948.45,540,31.859999,1",
        "7,16:9,center,Weakspot,0.35f,948.2501,599.6261,34.05771,1",
        "7,16:9,center,Weakspot,0.6f,1050.9354,668.29156,40.784565,0.54545456",
        "10,16:9,center,Normal,0f,960,540,27,1",
        "10,16:9,center,Normal,0.35f,960,599.6261,28.862465,1",
        "10,16:9,center,Normal,0.6f,1027.0972,653.49817,34.56319,0.54545456",
        "10,16:9,center,Armor,0f,960,540,22.14,1",
        "10,16:9,center,Armor,0.35f,960,599.6261,23.667221,1",
        "10,16:9,center,Armor,0.6f,1027.0972,653.49817,28.341816,0.54545456",
        "10,16:9,center,Weakspot,0f,960,540,31.859999,1",
        "10,16:9,center,Weakspot,0.35f,960,599.6261,34.05771,1",
        "10,16:9,center,Weakspot,0.6f,1027.0972,653.49817,40.784565,0.54545456",
        "7,16:9,right,Normal,0f,1188.45,505,27,1",
        "7,16:9,right,Normal,0.35f,1038.6775,586.4387,28.862465,1",
        "7,16:9,right,Normal,0.6f,1050.9354,668.29156,34.56319,0.54545456",
        "7,16:9,right,Armor,0f,1188.45,505,22.14,1",
        "7,16:9,right,Armor,0.35f,1038.6775,586.4387,23.667221,1",
        "7,16:9,right,Armor,0.6f,1050.9354,668.29156,28.341816,0.54545456",
        "7,16:9,right,Weakspot,0f,1188.45,505,31.859999,1",
        "7,16:9,right,Weakspot,0.35f,1038.6775,586.4387,34.05771,1",
        "7,16:9,right,Weakspot,0.6f,1050.9354,668.29156,40.784565,0.54545456",
        "10,16:9,right,Normal,0f,1200,505,27,1",
        "10,16:9,right,Normal,0.35f,1050.4274,586.4387,28.862465,1",
        "10,16:9,right,Normal,0.6f,1027.0972,653.49817,34.56319,0.54545456",
        "10,16:9,right,Armor,0f,1200,505,22.14,1",
        "10,16:9,right,Armor,0.35f,1050.4274,586.4387,23.667221,1",
        "10,16:9,right,Armor,0.6f,1027.0972,653.49817,28.341816,0.54545456",
        "10,16:9,right,Weakspot,0f,1200,505,31.859999,1",
        "10,16:9,right,Weakspot,0.35f,1050.4274,586.4387,34.05771,1",
        "10,16:9,right,Weakspot,0.6f,1027.0972,653.49817,40.784565,0.54545456",
        "7,ultrawide,center,Normal,0f,1268.45,540,27,1",
        "7,ultrawide,center,Normal,0.35f,1268.2501,599.6261,28.862465,1",
        "7,ultrawide,center,Normal,0.6f,1370.9354,668.29156,34.56319,0.54545456",
        "7,ultrawide,center,Armor,0f,1268.45,540,22.14,1",
        "7,ultrawide,center,Armor,0.35f,1268.2501,599.6261,23.667221,1",
        "7,ultrawide,center,Armor,0.6f,1370.9354,668.29156,28.341816,0.54545456",
        "7,ultrawide,center,Weakspot,0f,1268.45,540,31.859999,1",
        "7,ultrawide,center,Weakspot,0.35f,1268.2501,599.6261,34.05771,1",
        "7,ultrawide,center,Weakspot,0.6f,1370.9354,668.29156,40.784565,0.54545456",
        "10,ultrawide,center,Normal,0f,1280,540,27,1",
        "10,ultrawide,center,Normal,0.35f,1280,599.6261,28.862465,1",
        "10,ultrawide,center,Normal,0.6f,1347.0972,653.49817,34.56319,0.54545456",
        "10,ultrawide,center,Armor,0f,1280,540,22.14,1",
        "10,ultrawide,center,Armor,0.35f,1280,599.6261,23.667221,1",
        "10,ultrawide,center,Armor,0.6f,1347.0972,653.49817,28.341816,0.54545456",
        "10,ultrawide,center,Weakspot,0f,1280,540,31.859999,1",
        "10,ultrawide,center,Weakspot,0.35f,1280,599.6261,34.05771,1",
        "10,ultrawide,center,Weakspot,0.6f,1347.0972,653.49817,40.784565,0.54545456",
        "7,ultrawide,right,Normal,0f,1508.45,505,27,1",
        "7,ultrawide,right,Normal,0.35f,1358.6775,586.4387,28.862465,1",
        "7,ultrawide,right,Normal,0.6f,1370.9354,668.29156,34.56319,0.54545456",
        "7,ultrawide,right,Armor,0f,1508.45,505,22.14,1",
        "7,ultrawide,right,Armor,0.35f,1358.6775,586.4387,23.667221,1",
        "7,ultrawide,right,Armor,0.6f,1370.9354,668.29156,28.341816,0.54545456",
        "7,ultrawide,right,Weakspot,0f,1508.45,505,31.859999,1",
        "7,ultrawide,right,Weakspot,0.35f,1358.6775,586.4387,34.05771,1",
        "7,ultrawide,right,Weakspot,0.6f,1370.9354,668.29156,40.784565,0.54545456",
        "10,ultrawide,right,Normal,0f,1520,505,27,1",
        "10,ultrawide,right,Normal,0.35f,1370.4274,586.4387,28.862465,1",
        "10,ultrawide,right,Normal,0.6f,1347.0972,653.49817,34.56319,0.54545456",
        "10,ultrawide,right,Armor,0f,1520,505,22.14,1",
        "10,ultrawide,right,Armor,0.35f,1370.4274,586.4387,23.667221,1",
        "10,ultrawide,right,Armor,0.6f,1347.0972,653.49817,28.341816,0.54545456",
        "10,ultrawide,right,Weakspot,0f,1520,505,31.859999,1",
        "10,ultrawide,right,Weakspot,0.35f,1370.4274,586.4387,34.05771,1",
        "10,ultrawide,right,Weakspot,0.6f,1347.0972,653.49817,40.784565,0.54545456",
    };

    private static readonly string[] Screens = { "16:9", "ultrawide", "3440x1440" };
    private static readonly string[] Hits = { "center", "left", "right" };

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

    private static float SideOf(int seed) => FeedbackPolicy.NumberSide(seed);

    private static float DriftX(float hitX, float side, float age, float resolution)
        => FeedbackPolicy.NumberDriftX(hitX, side, age, resolution);

    private static float DriftY(float hitY, float age, float resolution)
        => FeedbackPolicy.NumberDriftY(hitY, age, resolution);

    // The production placement, called with explicit inputs (review A5). DamageNumbers.Draw calls the very
    // same FeedbackPolicy.NumberPose, so these fixtures can no longer pass against a test-side copy of the
    // formula: a changed seed, hit point, opacity, resolution or base size changes the asserted numbers.
    // approach = true: this file locks the FLY-AT-THE-VIEWER trajectory (DamageNumbers/ApproachEffect = true).
    // The switch-off trajectory (the 1.2.15 drift, the shipped default) is locked by
    // DamageNumberApproachTests with its own fixtures.
    private static void Pose(int seed, string screen, string hit, HitKind kind, float age, bool prismatic,
        out float x, out float y, out float size, out float alpha, out float straightX, out float straightY)
    {
        ScreenOf(screen, out float width, out float height);
        HitOf(hit, width, height, out float hitX, out float hitY);
        FeedbackPolicy.NumberPose(prismatic, true, seed, FeedbackPolicy.NumberSide(seed), age, Lifetime,
            FeedbackPolicy.DefaultNumberOpacity, hitX, hitY, width, height, Resolution(height), kind, BaseSize,
            out x, out y, out size, out alpha, out straightX, out straightY);
    }

    private static void PlainSample(int seed, string screen, string hit, HitKind kind, float age,
        out float x, out float y, out float size, out float alpha)
        => Pose(seed, screen, hit, kind, age, false, out x, out y, out size, out alpha, out _, out _);

    private static float Approach153(float age)
    {
        float window = Math.Max(0.01f, Lifetime * 1f);
        float t = FeedbackPolicy.Unit(age / window);
        if (t <= 0f) return 0f;
        if (t >= 1f) return 1f;
        return (float)Math.Pow(t, Baseline153EasePower);
    }

    // 1.2.15 prismatic flight, frozen on purpose: straight slide to the shared target, then the seeded
    // 34 degree rotation. This is the BASELINE the new pass is measured against, so it stays a local
    // re-implementation (only the shared drift/side helpers are production, and those are pinned by the
    // 72-row table above).
    private static void Prismatic153(int seed, float age, string screen, string hit, float resolution,
        out float x, out float y, out float straightX, out float straightY, out float size)
    {
        ScreenOf(screen, out float width, out float height);
        HitOf(hit, width, height, out float hitX, out float hitY);
        float side = SideOf(seed);
        float approach = Approach153(age);
        float driftX = DriftX(hitX, side, age, resolution);
        float driftY = DriftY(hitY, age, resolution);
        float targetX = width * 0.5f;
        float targetY = height * 0.5f - PrismaticTargetRise * resolution;
        straightX = FeedbackPolicy.ApproachAxis(driftX, targetX, approach);
        straightY = FeedbackPolicy.ApproachAxis(driftY, targetY, approach);
        FeedbackPolicy.RotateAroundTarget(straightX, straightY, targetX, targetY,
            FeedbackPolicy.FlyJitter(seed) * (Baseline153JitterDegrees * (float)(Math.PI / 180.0)) * approach,
            out x, out y);
        size = FeedbackPolicy.Clamp(BaseSize, 16f, 42f, 27f)
            * FeedbackPolicy.NumberScale(age, HitKind.Normal)
            * (1f + Baseline153Growth * approach)
            * resolution;
    }

    // 1.2.16 prismatic flight: the production call, plus the straight line it deviates from.
    private static void PrismaticNew(int seed, float age, string screen, string hit, float resolution,
        out float x, out float y, out float straightX, out float straightY, out float size)
        => Pose(seed, screen, hit, HitKind.Normal, age, true, out x, out y, out size, out _, out straightX, out straightY);

    // Signed lateral deviation of the glyph from its own unrotated incoming line, measured in the fixed
    // perpendicular frame of the spawn direction. Positive is counter-clockwise of the incoming line.
    private static float SignedOffset(float x, float y, float straightX, float straightY,
        string screen, string hit, int seed, float resolution)
    {
        ScreenOf(screen, out float width, out float height);
        HitOf(hit, width, height, out float hitX, out float hitY);
        float side = SideOf(seed);
        float spawnX = DriftX(hitX, side, 0f, resolution);
        float targetX = width * 0.5f;
        float targetY = height * 0.5f - PrismaticTargetRise * resolution;
        float dirX = targetX - spawnX;
        float dirY = targetY - hitY;
        float length = (float)Math.Sqrt(dirX * dirX + dirY * dirY);
        if (!(length > 0.0001f)) return 0f;
        float perpX = -dirY / length;
        float perpY = dirX / length;
        return (x - straightX) * perpX + (y - straightY) * perpY;
    }

    private static void Peaks(int seed, string screen, string hit, bool baseline,
        out float peakSize, out float peakOffset)
    {
        float resolution = Resolution(screen == "3440x1440" ? 1440f : 1080f);
        peakSize = 0f;
        peakOffset = 0f;
        for (int step = 0; step <= 610; step++)
        {
            float age = step * 0.001f;
            if (FeedbackPolicy.NumberAlpha(age, Lifetime, 1f) < VisibleAlphaFloor) continue;
            float x, y, straightX, straightY, size;
            if (baseline) Prismatic153(seed, age, screen, hit, resolution, out x, out y, out straightX, out straightY, out size);
            else PrismaticNew(seed, age, screen, hit, resolution, out x, out y, out straightX, out straightY, out size);
            float offset = SignedOffset(x, y, straightX, straightY, screen, hit, seed, resolution);
            if (size > peakSize) peakSize = size;
            if (Math.Abs(offset) > Math.Abs(peakOffset)) peakOffset = offset;
        }
    }

    private static string N(float value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    internal static void Run(Action<bool, string> check)
    {
        // ---- the shared plain-number flight is still 1.2.15, constant for constant -----------------
        check(FeedbackPolicy.ApproachWindowFraction == 1f
            && FeedbackPolicy.ApproachEasePower == 2.6f
            && FeedbackPolicy.ApproachGrowth == 0.45f
            && FeedbackPolicy.BodyDropFraction == 0.10f
            && FeedbackPolicy.BodyCrossFraction == 0.78f
            && FeedbackPolicy.ExitOvershootFraction == 0.42f
            && FeedbackPolicy.NumberFlyJitterDegrees == 58f
            && FeedbackPolicy.PrismaticApproachFan == 0f
            && FeedbackPolicy.PrismaticApproachRisePixels == PrismaticTargetRise
            && FeedbackPolicy.PrismaticApproachRiseFraction == 0f
            && Lifetime == 0.72f
            && FeedbackPolicy.DefaultNumberOpacity == 1f
            && FeedbackPolicy.MaxNumbers == 64,
            "prismatic-incoming: every shared plain-number constant (2.6 ease, 0.45 growth, 0.10H body drop, 0.78 cross, 0.42H exit, 58 deg, 0.72 s life, opacity 1, 64 slots) is unchanged");

        // ---- the plain numbers reproduce the 1.2.15 table ----------------------------------------
        bool tableOk = true;
        string firstBad = string.Empty;
        int rows = 0;
        foreach (string row in PlainNumbers153)
        {
            string[] field = row.Split(',');
            int seed = int.Parse(field[0], CultureInfo.InvariantCulture);
            string screen = field[1];
            string hit = field[2];
            HitKind kind = field[3] == "Armor" ? HitKind.Armor : field[3] == "Weakspot" ? HitKind.Weakspot : HitKind.Normal;
            float age = float.Parse(field[4].TrimEnd('f'), CultureInfo.InvariantCulture);
            float expectedX = float.Parse(field[5], CultureInfo.InvariantCulture);
            float expectedY = float.Parse(field[6], CultureInfo.InvariantCulture);
            float expectedSize = float.Parse(field[7], CultureInfo.InvariantCulture);
            float expectedAlpha = float.Parse(field[8], CultureInfo.InvariantCulture);
            PlainSample(seed, screen, hit, kind, age, out float x, out float y, out float size, out float alpha);
            rows++;
            if (Math.Abs(x - expectedX) > 0.001f || Math.Abs(y - expectedY) > 0.001f
                || Math.Abs(size - expectedSize) > 0.001f || Math.Abs(alpha - expectedAlpha) > 0.000001f)
            {
                if (tableOk) firstBad = row + " -> " + N(x) + "/" + N(y) + "/" + N(size) + "/" + N(alpha);
                tableOk = false;
            }
        }
        check(tableOk && rows == PlainNumbers153.Length && rows == 72,
            "prismatic-incoming: all " + rows + " locked 1.2.15 plain samples reproduce x, y, glyph size and alpha bit for bit (16:9 + ultrawide, normal/armor/weakspot, centre and right hits)"
            + (tableOk ? string.Empty : " FIRST MISMATCH " + firstBad));

        // ---- a prismatic hit is now bigger and swings wider than 1.2.15 --------------------------
        int comparisons = 0;
        int sizeWins = 0, offsetWins = 0;
        var summary = new System.Text.StringBuilder();
        foreach (string screen in Screens)
        {
            foreach (string hit in Hits)
            {
                foreach (int seed in new[] { 0, 3, 7, 10, 13, 21 })
                {
                    Peaks(seed, screen, hit, true, out float baseSize, out float baseOffset);
                    Peaks(seed, screen, hit, false, out float newSize, out float newOffset);
                    comparisons++;
                    if (newSize > baseSize) sizeWins++;
                    if (Math.Abs(newOffset) > Math.Abs(baseOffset)) offsetWins++;
                    if (screen == "16:9" && (hit == "center" || hit == "left"))
                    {
                        summary.Append(hit).Append("/s").Append(seed)
                            .Append(" size ").Append(N(baseSize)).Append("->").Append(N(newSize))
                            .Append(" off ").Append(N(baseOffset)).Append("->").Append(N(newOffset)).Append("; ");
                    }
                }
            }
        }
        check(comparisons == 54 && sizeWins == comparisons,
            "prismatic-incoming: the visible peak glyph size beats 1.2.15 on all " + comparisons + " samples (16:9, ultrawide, 3440x1440 x centre/left/right x 6 seeds)");
        check(offsetWins == comparisons,
            "prismatic-incoming: the signed random offset is larger than 1.2.15 on all " + comparisons + " samples; 16:9 sample table ["
            + summary.ToString().TrimEnd() + "]");

        // ---- representative numbers, printed for the report -------------------------------------
        Peaks(10, "16:9", "center", true, out float c153Size, out float c153Offset);
        Peaks(10, "16:9", "center", false, out float cNewSize, out float cNewOffset);
        check(cNewSize > c153Size * 1.15f && Math.Abs(cNewOffset) > Math.Abs(c153Offset) * 4f,
            "prismatic-incoming: centred 16:9 hit, seed 10 - peak glyph " + N(c153Size) + " px -> " + N(cNewSize)
            + " px (+" + N((cNewSize / c153Size - 1f) * 100f) + "%), signed offset " + N(c153Offset) + " px -> "
            + N(cNewOffset) + " px at alpha >= 0.5");

        // ---- per-frame values of the same sample, for a human-readable table --------------------
        var trace = new System.Text.StringBuilder();
        bool monotoneSize = true;
        float previousSize = 0f;
        foreach (float age in new[] { 0.3f, 0.45f, 0.55f, 0.61f })
        {
            Prismatic153(10, age, "16:9", "center", 1f, out _, out _, out _, out _, out float baseSize);
            PrismaticNew(10, age, "16:9", "center", 1f, out _, out _, out _, out _, out float size);
            if (size < baseSize || size < previousSize) monotoneSize = false;
            previousSize = size;
            trace.Append("age ").Append(N(age)).Append(": ").Append(N(baseSize)).Append("->").Append(N(size)).Append(" px; ");
        }
        check(monotoneSize && previousSize > 0f,
            "prismatic-incoming: the glyph keeps growing while it fades and is bigger than 1.2.15 at every opaque sample [" + trace.ToString().TrimEnd() + "]");

        // ---- one seeded side per hit, no mid-flight re-roll ------------------------------------
        bool stable = true;
        bool bothSides = false, sawLeft = false, sawRight = false;
        for (int seed = 0; seed < 64; seed++)
        {
            float first = FeedbackPolicy.PrismaticSwing(seed, 0.37f, 1f);
            float second = FeedbackPolicy.PrismaticSwing(seed, 0.37f, 1f);
            float third = FeedbackPolicy.PrismaticSwing(seed, 0.81f, 1f);
            if (first != second || Math.Sign(first) != Math.Sign(third)) stable = false;
            if (first < 0f) sawLeft = true;
            if (first > 0f) sawRight = true;
            PrismaticNew(seed, 0.42f, "16:9", "center", 1f, out float x1, out float y1, out _, out _, out _);
            PrismaticNew(seed, 0.42f, "16:9", "center", 1f, out float x2, out float y2, out _, out _, out _);
            if (x1 != x2 || y1 != y2) stable = false;
        }
        bothSides = sawLeft && sawRight;
        check(stable && bothSides,
            "prismatic-incoming: each of 64 seeds picks one side once, never re-rolls inside the flight, and both sides occur");

        // ---- different seeds really do fly differently -----------------------------------------
        var directions = new System.Collections.Generic.HashSet<int>();
        for (int seed = 0; seed < 16; seed++)
        {
            Peaks(seed, "16:9", "center", false, out _, out float offset);
            directions.Add((int)Math.Round(offset * 10f));
        }
        check(directions.Count >= 14,
            "prismatic-incoming: 16 consecutive seeds produce " + directions.Count + " distinct seeded excursions, not a handful of repeats");

        // ---- bounded excursion, exact spawn, exact arrival --------------------------------------
        float worst = 0f;
        string worstAt = string.Empty;
        bool arrivalExact = true;
        foreach (string screen in Screens)
        {
            float resolution = Resolution(screen == "3440x1440" ? 1440f : 1080f);
            ScreenOf(screen, out float width, out float height);
            foreach (string hit in Hits)
            {
                HitOf(hit, width, height, out float hitX, out float hitY);
                for (int seed = 0; seed < 64; seed++)
                {
                    float side = SideOf(seed);
                    float targetX = width * 0.5f;
                    float targetY = height * 0.5f - PrismaticTargetRise * resolution;
                    for (int step = 0; step <= 720; step++)
                    {
                        float age = step * 0.001f;
                        PrismaticNew(seed, age, screen, hit, resolution, out float x, out float y,
                            out float straightX, out float straightY, out _);
                        if (float.IsNaN(x) || float.IsNaN(y) || float.IsInfinity(x) || float.IsInfinity(y)) worst = float.NaN;
                        float offset = SignedOffset(x, y, straightX, straightY, screen, hit, seed, resolution);
                        if (Math.Abs(offset) > worst)
                        {
                            worst = Math.Abs(offset);
                            worstAt = screen + "/" + hit + "/seed" + seed + "/age" + N(age);
                        }
                    }
                    FeedbackPolicy.PrismaticFlightPoint(seed, 0f, DriftX(hitX, side, 0f, resolution), hitY,
                        targetX, targetY, resolution, out float startX, out float startY);
                    FeedbackPolicy.PrismaticFlightPoint(seed, 1f, DriftX(hitX, side, 0f, resolution), hitY,
                        targetX, targetY, resolution, out float endX, out float endY);
                    if (Math.Abs(startX - DriftX(hitX, side, 0f, resolution)) > 0.0001f || Math.Abs(startY - hitY) > 0.0001f) arrivalExact = false;
                    if (Math.Abs(endX - targetX) > 0.0001f || Math.Abs(endY - targetY) > 0.0001f) arrivalExact = false;
                }
            }
        }
        check(!float.IsNaN(worst) && worst <= MaxLateralPixels * Resolution(1440f),
            "prismatic-incoming: the excursion never exceeds " + N(MaxLateralPixels) + " px x resolution (1080p scale); measured worst " + N(worst) + " px at " + worstAt);
        check(arrivalExact,
            "prismatic-incoming: spawn sits on the projected hit point and arrival lands exactly on the shared target, so the swoop can never drift the destination");

        // ---- caps, lifecycle and count boundaries ----------------------------------------------
        bool swingBounded = Math.Abs(FeedbackPolicy.PrismaticSwingPixels - 72f) < 0.0001f;
        for (int seed = 0; seed < 64; seed++)
        {
            for (float approach = 0f; approach <= 1f; approach += 0.02f)
            {
                float swing = FeedbackPolicy.PrismaticSwing(seed, approach, 1.6f);
                if (Math.Abs(swing) > FeedbackPolicy.PrismaticSwingPixels * 1.6f + 0.0001f) swingBounded = false;
            }
            if (FeedbackPolicy.PrismaticSwing(seed, 0f, 1f) != 0f || FeedbackPolicy.PrismaticSwing(seed, 1f, 1f) != 0f) swingBounded = false;
        }
        check(swingBounded,
            "prismatic-incoming: the swoop is zero at spawn and at arrival and never exceeds " + N(FeedbackPolicy.PrismaticSwingPixels) + " px x resolution");
        check(FeedbackPolicy.Clamp(0.01f, 0.35f, 1.2f, Lifetime) == 0.35f
            && FeedbackPolicy.Clamp(9f, 0.35f, 1.2f, Lifetime) == 1.2f
            && FeedbackPolicy.NumberAlpha(Lifetime, Lifetime, 1f) == 0f
            && FeedbackPolicy.NumberAlpha(Lifetime - 0.001f, Lifetime, 1f) > 0f
            && Math.Abs(FeedbackPolicy.NumberAlpha(Lifetime - 0.11f, Lifetime, 1f) - VisibleAlphaFloor) < 0.0001f
            && FeedbackPolicy.MaxNumbers == 64
            && FeedbackPolicy.MaxNumbers * 3 > 0,
            "prismatic-incoming: lifetime stays clamped to 0.35-1.2 s, alpha hits exactly 0 at the end (0.5 at 0.61 s) and the queue stays capped at " + FeedbackPolicy.MaxNumbers + " numbers");

        // ---- broken input and opacity 0 draw nothing new ---------------------------------------
        FeedbackPolicy.PrismaticFlightPoint(5, float.NaN, 400f, 300f, 960f, 536f, 1f, out float nanApproachX, out float nanApproachY);
        FeedbackPolicy.PrismaticFlightPoint(5, 0.5f, float.NaN, 300f, 960f, 536f, 1f, out float nanStartX, out float nanStartY);
        FeedbackPolicy.PrismaticFlightPoint(5, 0.5f, 400f, 300f, float.NaN, 536f, 1f, out float nanTargetX, out float nanTargetY);
        check(nanApproachX == 400f && nanApproachY == 300f
            && float.IsNaN(nanStartX) && nanStartY == 418f
            && nanTargetX == 400f && nanTargetY == 418f
            && FeedbackPolicy.PrismaticSwing(3, 0.5f, float.NaN) == 0f
            && FeedbackPolicy.PrismaticSwing(3, 0.5f, 0f) == 0f,
            "prismatic-incoming: a broken age falls back to the spawn point, broken coordinates pass through and a broken resolution cancels the swoop instead of producing NaN");
        check(!FeedbackPolicy.ShowsNumber(0f)
            && FeedbackPolicy.NumberAlpha(0.3f, Lifetime, 0f) == 0f
            && FeedbackPolicy.NumberTextAlpha(1f, FeedbackPolicy.NumberAlpha(0.3f, Lifetime, 0f)) == 0f
            && FeedbackPolicy.NumberAlpha(float.NaN, Lifetime, 1f) == 0f,
            "prismatic-incoming: opacity 0 and a broken age still submit no outline and no text, so nothing new is painted");

        // ---- edge of the screen: still finite, still bounded -----------------------------------
        bool edgesFinite = true;
        foreach (float[] corner in new[] { new[] { 0f, 0f }, new[] { 1920f, 1080f }, new[] { -400f, 1400f } })
        {
            for (int seed = 0; seed < 32; seed++)
            {
                for (float age = 0f; age < Lifetime; age += 0.03f)
                {
                    float approach = FeedbackPolicy.PrismaticApproachT(age, Lifetime);
                    FeedbackPolicy.PrismaticFlightPoint(seed, approach, corner[0], corner[1], 960f, 536f, 1f,
                        out float x, out float y);
                    if (float.IsNaN(x) || float.IsNaN(y) || float.IsInfinity(x) || float.IsInfinity(y)) edgesFinite = false;
                    float swing = FeedbackPolicy.PrismaticSwing(seed, approach, 1f);
                    if (Math.Abs(swing) > FeedbackPolicy.PrismaticSwingPixels + 0.0001f) edgesFinite = false;
                }
            }
        }
        check(edgesFinite,
            "prismatic-incoming: hit points at, inside and outside the screen edges stay finite and inside the swoop cap for 32 seeds over the whole lifetime");
    }
}
