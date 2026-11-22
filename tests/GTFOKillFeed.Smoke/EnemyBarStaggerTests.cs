using System;
using System.Globalization;
using HawChow.GTFOKillFeed;

// 1.2.25 regression: the anatomy-classified stagger row under the focused-enemy bar shows ONE number per item - the
// theoretical MAXIMUM threshold - and OMITS an item it cannot confirm from positive evidence (never a dash, never a 0,
// no blank line). Everything asserted here is a production function the shipped read/draw path calls
// (BarPolicy.BodyGroup, LimbBreakIsHitreact, StaggerGroupBuilder/GroupValue, StaggerTokens/StaggerLine/
// StaggerTokenCount, ThresholdValue, IsUsableThreshold, StaggerFontPixels, StaggerRows, StaggerAvailableWidth,
// StaggerRowLeft, TextOriginX), so a range instead of a maximum, a swapped current/maximum value, a resurrected
// remaining/current combination, a fabricated 0, a clamped "ceiling" number, a centred row, a blank line or an
// off-screen row fails here instead of only looking right in a source grep. The compiled wiring itself (which
// style/content/colour/fields the row uses, that the accumulator is NOT read, and that a stale target cannot carry the
// row) is pinned by scripts/Test-EnemyBarStaggerIl.ps1 and scripts/Verify.ps1 on the real assembly/source.
internal static class EnemyBarStaggerTests
{
    // The focused-enemy bar's own weakspot default, duplicated on purpose so a change to either colour has to be a
    // deliberate, reviewed decision.
    private const float WeakspotRed = 0.937f;
    private const float WeakspotGreen = 0.298f;
    private const float WeakspotBlue = 0.298f;

    private static BarPolicy.StaggerValues Values(BarPolicy.StaggerGroupBuilder limbs, BarPolicy.StaggerGroupBuilder torso,
        bool hasNormal, float normalThreshold)
        => new BarPolicy.StaggerValues(limbs.Build(), torso.Build(), hasNormal, normalThreshold);

    internal static void Run(Action<bool, string> check)
    {
        // ---- the requested shape: the theoretical maximum of each item, nothing else ----------------------------
        var limbs40 = new BarPolicy.StaggerGroupBuilder();
        limbs40.Add(40f);
        var torso50 = new BarPolicy.StaggerGroupBuilder();
        torso50.Add(50f);
        var full = Values(limbs40, torso50, true, 80f);
        check(BarPolicy.StaggerLine(full, "zh") == "[四肢：40][躯体：50][普通(阈值)：80]",
            "the row reads [四肢：max][躯体：max][普通(阈值)：threshold]");
        check(BarPolicy.StaggerLine(full, "en") == "[Limb 40][Torso 50][Hitreact(max) 80]",
            "the same values follow the configured language instead of hard-coding Chinese");
        check(BarPolicy.StaggerTokenCount(full) == 3, "a row with three confirmable items counts three tokens");

        // ---- ONE maximum per group: 40 and 60 print 60, never a range and never a minimum ------------------------
        var spread = new BarPolicy.StaggerGroupBuilder();
        spread.Add(40f);
        spread.Add(60f);
        check(BarPolicy.GroupValue(spread.Build()) == "60",
            "a group whose parts have 40 and 60 prints the maximum (60), never the range 40-60 or the minimum 40");
        var mirrored = new BarPolicy.StaggerGroupBuilder();
        mirrored.Add(60f);
        mirrored.Add(40f);
        check(BarPolicy.GroupValue(mirrored.Build()) == "60",
            "the maximum does not depend on the order the game reports the parts in");
        var torsoSpread = new BarPolicy.StaggerGroupBuilder();
        torsoSpread.Add(50f);
        torsoSpread.Add(80f);
        check(BarPolicy.GroupValue(torsoSpread.Build()) == "80",
            "the torso group keeps its own maximum");
        check(!BarPolicy.StaggerLine(Values(spread, torsoSpread, false, 0f), "zh").Contains("-"),
            "no item may render a 40-60 style range: the player asked for the maximum only");
        var half = new BarPolicy.StaggerGroupBuilder();
        half.Add(40f);
        check(BarPolicy.GroupValue(half.Build()) == "40",
            "a limb with capacity 40 shows 40 and never a current/capacity pair such as 15/40");
        check(!BarPolicy.StaggerLine(Values(half, new BarPolicy.StaggerGroupBuilder(), true, 80f), "zh").Contains("/"),
            "no item of the row may contain a current/remaining/ceiling combination");

        // ---- an item without confirmable evidence is OMITTED, never a dash and never a 0 ------------------------
        var noTorso = new BarPolicy.StaggerGroupBuilder();
        noTorso.Add(40f);
        string limbsOnly = BarPolicy.StaggerLine(Values(noTorso, new BarPolicy.StaggerGroupBuilder(), false, 0f), "zh");
        check(limbsOnly == "[四肢：40]",
            "a body without a confirmable torso/normal item prints only the limb group");
        check(BarPolicy.StaggerTokenCount(Values(noTorso, new BarPolicy.StaggerGroupBuilder(), false, 0f)) == 1,
            "one confirmable item counts one token, so the wrap plan cannot reserve a line for it");
        var torsoOnly = new BarPolicy.StaggerGroupBuilder();
        torsoOnly.Add(30f);
        check(BarPolicy.StaggerLine(Values(new BarPolicy.StaggerGroupBuilder(), torsoOnly, true, 12f), "zh")
            == "[躯体：30][普通(阈值)：12]",
            "a body without a confirmable limb item still shows the torso and normal items");
        check(BarPolicy.StaggerLine(Values(new BarPolicy.StaggerGroupBuilder(), new BarPolicy.StaggerGroupBuilder(), true, 80f), "zh")
            == "[普通(阈值)：80]",
            "with no part at all only the independent normal threshold is shown");
        check(BarPolicy.StaggerLine(Values(noTorso, torsoOnly, true, 80f), "en") == "[Limb 40][Torso 30][Hitreact(max) 80]",
            "all three items in the English row");

        // ---- nothing confirmable at all: an EMPTY row, which the caller must not draw or reserve space for ----
        var nothing = BarPolicy.StaggerValues.None;
        check(BarPolicy.StaggerLine(nothing, "zh") == string.Empty && BarPolicy.StaggerLine(nothing, "en") == string.Empty,
            "with no confirmable item the row is the empty string, so nothing is drawn and nothing is reserved");
        check(BarPolicy.StaggerTokenCount(nothing) == 0, "no confirmable item means zero tokens, i.e. no line at all");
        foreach (string empty in new[] { BarPolicy.StaggerLine(nothing, "zh"), limbsOnly })
        {
            check(!empty.Contains("\u2014") && !empty.Contains("：0]") && !empty.Contains("免疫"),
                "an omitted item is neither a dash, nor a fabricated 0, nor the C-Foam immune sentinel");
        }

        // ---- broken/unreadable values never build a group ------------------------------------------------------
        var broken = new BarPolicy.StaggerGroupBuilder();
        broken.Add(0f);
        broken.Add(-5f);
        broken.Add(float.NaN);
        broken.Add(float.PositiveInfinity);
        broken.Add(float.NegativeInfinity);
        check(!broken.Build().HasValue && BarPolicy.GroupValue(broken.Build()) == string.Empty,
            "a 0, a negative, NaN or Infinity capacity is not a group value, so the group is omitted");
        var mixedGroup = new BarPolicy.StaggerGroupBuilder();
        mixedGroup.Add(float.NaN);
        mixedGroup.Add(25f);
        mixedGroup.Add(0f);
        mixedGroup.Add(70f);
        check(BarPolicy.GroupValue(mixedGroup.Build()) == "70",
            "a mixed group keeps only the readable capacities and still prints the single maximum (70)");
        var mixedSmall = new BarPolicy.StaggerGroupBuilder();
        mixedSmall.Add(float.NaN);
        mixedSmall.Add(25f);
        mixedSmall.Add(0f);
        check(BarPolicy.GroupValue(mixedSmall.Build()) == "25",
            "an unreadable part cannot poison the confirmable ones: the readable maximum wins");

        // ---- threshold formatting: the stored float itself, never rounded up, clamped or converted -------------
        check(BarPolicy.ThresholdValue(40f) == "40" && BarPolicy.ThresholdValue(12.5f) == "12.5"
            && BarPolicy.ThresholdValue(0.375f) == "0.375",
            "a threshold keeps its precision: 40, 12.5 and 0.375 stay exactly that");
        check(BarPolicy.ThresholdValue(39.9999f) == "39.9999",
            "the stored 39.9999 is printed as 39.9999, never rounded up to 40");
        check(BarPolicy.ThresholdValue(0.0001f) == "0.0001",
            "a very small positive threshold keeps its real value instead of degenerating to 0");
        check(BarPolicy.ThresholdValue(1e-6f) == "1E-06" || BarPolicy.ThresholdValue(1e-6f) == "0.000001",
            "a sub-microscopic threshold still renders a real (round-trip) number");
        check(BarPolicy.ThresholdValue(12345678f) == "12345678",
            "a very large capacity keeps its real magnitude instead of being pinned to a printable ceiling");
        check(BarPolicy.ThresholdValue(float.MaxValue) == float.MaxValue.ToString("G", CultureInfo.InvariantCulture),
            "the largest float prints its own shortest round-trip form, not a fabricated 9999999");
        foreach (float probe in new[] { 0.0001f, 0.5f, 7f, 12.5f, 40f, 39.9999f, 12345.678f, 1e-6f, 1e12f, float.MaxValue })
        {
            string text = BarPolicy.ThresholdValue(probe);
            check(text.Length > 0
                && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float back)
                && back == probe,
                "every printed threshold round-trips to the SAME float (" + probe.ToString("G", CultureInfo.InvariantCulture) + ")");
        }
        check(BarPolicy.ThresholdValue(0f) == string.Empty && BarPolicy.ThresholdValue(-3f) == string.Empty
            && BarPolicy.ThresholdValue(float.NaN) == string.Empty
            && BarPolicy.ThresholdValue(float.PositiveInfinity) == string.Empty
            && BarPolicy.ThresholdValue(float.NegativeInfinity) == string.Empty,
            "0, negative, NaN and Infinity have no text at all (the item is omitted, never shown as 0)");
        check(BarPolicy.IsUsableThreshold(0.001f) && !BarPolicy.IsUsableThreshold(0f)
            && !BarPolicy.IsUsableThreshold(-1f) && !BarPolicy.IsUsableThreshold(float.NaN)
            && !BarPolicy.IsUsableThreshold(float.PositiveInfinity),
            "a threshold is usable only when it is finite and strictly positive");

        // ---- the game's own limb-destruction switch: only POSITIVE evidence allows the part items ---------------
        check(BarPolicy.HitreactUnspecified == 0 && BarPolicy.HitreactNone == 1 && BarPolicy.HitreactMicro == 2
            && BarPolicy.HitreactLight == 3 && BarPolicy.HitreactHeavy == 4 && BarPolicy.HitreactToDeath == 5
            && BarPolicy.HitreactInstantRagdollDeath == 6,
            "the pinned ES_HitreactType values match the installed interop");
        check(BarPolicy.LimbBreakIsHitreact(BarPolicy.HitreactMicro)
            && BarPolicy.LimbBreakIsHitreact(BarPolicy.HitreactLight)
            && BarPolicy.LimbBreakIsHitreact(BarPolicy.HitreactHeavy),
            "an authored Micro/Light/Heavy limb-destruction reaction is the positive evidence the part items need");
        check(!BarPolicy.LimbBreakIsHitreact(BarPolicy.HitreactNone),
            "None means the game plays no hitreact when a limb breaks, so no part threshold is claimed");
        check(!BarPolicy.LimbBreakIsHitreact(BarPolicy.HitreactUnspecified),
            "Unspecified is unauthored/unknown: the part items are omitted (this is NOT a claim of immunity)");
        check(!BarPolicy.LimbBreakIsHitreact(BarPolicy.HitreactToDeath)
            && !BarPolicy.LimbBreakIsHitreact(BarPolicy.HitreactInstantRagdollDeath),
            "a death reaction is not a stagger threshold, so the part items are omitted");
        check(!BarPolicy.LimbBreakIsHitreact(7) && !BarPolicy.LimbBreakIsHitreact(-1) && !BarPolicy.LimbBreakIsHitreact(99),
            "an unknown/mod enum value is unproven and never enables the part items");
        check(!BarPolicy.LimbBreakIsHitreact(BarPolicy.HitreactMicro - 1)
            && BarPolicy.LimbBreakIsHitreact(BarPolicy.HitreactMicro + 1),
            "the gate is an exact whitelist, not a threshold on the raw value");

        // ---- the normal item is independent of the parts -------------------------------------------------------
        check(BarPolicy.StaggerLine(Values(new BarPolicy.StaggerGroupBuilder(), new BarPolicy.StaggerGroupBuilder(), true, 0.5f), "zh")
            == "[普通(阈值)：0.5]",
            "the normal threshold is shown on its own even when no part is confirmable");
        check(BarPolicy.StaggerLine(Values(limbs40, torso50, false, 0f), "zh") == "[四肢：40][躯体：50]",
            "the parts are shown on their own when the normal threshold is unusable: the two are independent");
        check(!BarPolicy.IsUsableThreshold(0f) && !BarPolicy.IsUsableThreshold(float.NaN),
            "an unusable normal threshold removes only the normal item");

        // ---- anatomical classification: the shipped CD_BodypartType values, never magnitude/index/name --------
        check(BarPolicy.BodyGroup(BarPolicy.PartArmUpperRight) == BarPolicy.StaggerBodyGroup.Limbs
            && BarPolicy.BodyGroup(BarPolicy.PartArmLowerRight) == BarPolicy.StaggerBodyGroup.Limbs
            && BarPolicy.BodyGroup(BarPolicy.PartArmUpperLeft) == BarPolicy.StaggerBodyGroup.Limbs
            && BarPolicy.BodyGroup(BarPolicy.PartArmLowerLeft) == BarPolicy.StaggerBodyGroup.Limbs,
            "both upper/lower arms (and their hands) are 四肢");
        check(BarPolicy.BodyGroup(BarPolicy.PartLegUpperRight) == BarPolicy.StaggerBodyGroup.Limbs
            && BarPolicy.BodyGroup(BarPolicy.PartLegLowerRight) == BarPolicy.StaggerBodyGroup.Limbs
            && BarPolicy.BodyGroup(BarPolicy.PartLegUpperLeft) == BarPolicy.StaggerBodyGroup.Limbs
            && BarPolicy.BodyGroup(BarPolicy.PartLegLowerLeft) == BarPolicy.StaggerBodyGroup.Limbs,
            "both upper/lower legs (and their feet) are 四肢");
        check(BarPolicy.BodyGroup(BarPolicy.PartChest) == BarPolicy.StaggerBodyGroup.Torso
            && BarPolicy.BodyGroup(BarPolicy.PartStomach) == BarPolicy.StaggerBodyGroup.Torso
            && BarPolicy.BodyGroup(BarPolicy.PartHips) == BarPolicy.StaggerBodyGroup.Torso,
            "chest/stomach/hips are 躯体");
        check(BarPolicy.BodyGroup(BarPolicy.PartHead) == BarPolicy.StaggerBodyGroup.Unknown
            && BarPolicy.BodyGroup(BarPolicy.PartUnspecified) == BarPolicy.StaggerBodyGroup.Unknown
            && BarPolicy.BodyGroup(13) == BarPolicy.StaggerBodyGroup.Unknown
            && BarPolicy.BodyGroup(99) == BarPolicy.StaggerBodyGroup.Unknown,
            "head, Unspecified and every future/mod enum value are Unknown and never folded into 四肢 or 躯体");
        check(BarPolicy.BodyGroup(15) == BarPolicy.StaggerBodyGroup.Unknown
            && BarPolicy.BodyGroup(40) == BarPolicy.StaggerBodyGroup.Unknown,
            "a numeric value such as 15 or 40 is never treated as a body part just because it looks like a limb HP");
        check(BarPolicy.BodyGroup(BarPolicy.PartHead) != BarPolicy.StaggerBodyGroup.Limbs
            && BarPolicy.BodyGroup(BarPolicy.PartHead) != BarPolicy.StaggerBodyGroup.Torso,
            "the head (a kill, not a limb break) cannot enter either group");

        // ---- target switching cannot leave a stale row ---------------------------------------------------------
        var switchA = new BarPolicy.StaggerGroupBuilder();
        switchA.Add(40f);
        string firstTarget = BarPolicy.StaggerLine(Values(switchA, new BarPolicy.StaggerGroupBuilder(), true, 80f), "zh");
        string cleared = BarPolicy.StaggerLine(BarPolicy.StaggerValues.None, "zh");
        var switchB = new BarPolicy.StaggerGroupBuilder();
        switchB.Add(30f);
        string secondTarget = BarPolicy.StaggerLine(Values(switchB, new BarPolicy.StaggerGroupBuilder(), true, 12f), "zh");
        string switchToNothing = BarPolicy.StaggerLine(
            Values(new BarPolicy.StaggerGroupBuilder(), new BarPolicy.StaggerGroupBuilder(), false, 0f), "zh");
        check(firstTarget == "[四肢：40][普通(阈值)：80]" && cleared == string.Empty
            && secondTarget == "[四肢：30][普通(阈值)：12]",
            "switching targets rebuilds every item from the new enemy; the cleared state is an empty row, not the old values");
        check(switchToNothing == string.Empty,
            "switching to a target with no confirmable item yields an empty row, so the old numbers cannot be reused");

        // ---- row layout is computed independently of DrawLabel ------------------------------------------------
        int small = BarPolicy.StaggerFontPixels(BarPolicy.DefaultNumberSize, 720);
        int large = BarPolicy.StaggerFontPixels(BarPolicy.DefaultNumberSize, 2160);
        check(small >= BarPolicy.StaggerMinFontPixels(720) && large >= small,
            "the row derives its own font pixels from the configured size and screen height (never from DrawLabel)");
        check(BarPolicy.StaggerFontPixels(1f, 1080) >= BarPolicy.StaggerMinFontPixels(1080),
            "an out-of-range configured size still yields a legible minimum instead of a zero-size row");

        // The old centred helper turned 960 into 860 for a 200 px row. The left anchor must keep the name/bar x.
        float margin = BarPolicy.StaggerRowMargin(1080);
        check(Math.Abs(BarPolicy.StaggerRowLeft(960f, 200f, 1920, 1080) - 960f) < 0.01f,
            "the row keeps the monster name's x (960) instead of centring itself at 860 under a 200 px row");
        check(Math.Abs(BarPolicy.StaggerRowLeft(0f, 200f, 1920, 1080) - margin) < 0.01f
            && BarPolicy.StaggerRowLeft(1919f, 200f, 1920, 1080) <= 1920f - margin - 200f + 0.01f,
            "the left anchor is clamped inside the screen margins at both edges");
        check(Math.Abs(BarPolicy.StaggerAvailableWidth(100f, 1920, 1080) - (1920f - margin - 100f)) < 0.01f
            && Math.Abs(BarPolicy.StaggerAvailableWidth(0f, 1920, 1080) - (1920f - margin * 2f)) < 0.01f
            && BarPolicy.StaggerAvailableWidth(0f, 0, 1080) == 0f,
            "the available width starts at the aligned glyph and never leaves the screen margin");

        // 1920/300/320 are 1/2/3 rows: the wide row is scaled or wrapped, never silently dropped.
        check(BarPolicy.StaggerRows(300f, 200f, 250f, 150f, 3) == 1
            && BarPolicy.StaggerRows(300f, 320f, 250f, 150f, 3) == 2
            && BarPolicy.StaggerRows(300f, 320f, 320f, 150f, 3) == 3
            && BarPolicy.StaggerRows(0f, 200f, 250f, 150f, 3) == 3,
            "a full row fits on one line; a narrow screen wraps at group boundaries (2/3 lines) instead of hiding");
        // A row that lost an item must never be planned as an empty line.
        check(BarPolicy.StaggerRows(300f, 200f, 200f, 200f, 1) == 1
            && BarPolicy.StaggerRows(300f, 400f, 400f, 200f, 1) == 1,
            "a single-item row is always one line, even when it has to be scaled or clamped");
        check(BarPolicy.StaggerRows(300f, 400f, 200f, 200f, 2) == 2,
            "a two-item row that does not fit becomes exactly two lines (never two lines plus an empty one)");
        check(BarPolicy.StaggerRows(300f, 200f, 200f, 200f, 0) == 0
            && BarPolicy.StaggerRows(0f, 200f, 250f, 150f, 0) == 0,
            "a row with no item plans zero lines, so the caller draws nothing at all");
        check(BarPolicy.StaggerRows(300f, 200f, 200f, 200f, 9) == 1
            && BarPolicy.StaggerRows(0f, 200f, 250f, 150f, 9) == BarPolicy.MaxStaggerTokens,
            "an impossible token count is clamped to the three shipped items");
        check(BarPolicy.StaggerRowY(100f, 1080) > 100f,
            "the row hangs below the bar instead of overlapping it");

        // ---- the real first-glyph origin includes rect + alignment + padding/offset ---------------------------
        float leftOrigin = BarPolicy.TextOriginX(100f, 300f, 120f, 7f, 5f, 2f, BarPolicy.TextAlignLeft);
        float centreOrigin = BarPolicy.TextOriginX(100f, 300f, 120f, 7f, 5f, 2f, BarPolicy.TextAlignCenter);
        float rightOrigin = BarPolicy.TextOriginX(100f, 300f, 120f, 7f, 5f, 2f, BarPolicy.TextAlignRight);
        check(Math.Abs(leftOrigin - 109f) < 0.01f,
            "left-aligned text starts at rect.x + padding.left + contentOffset (100 + 7 + 2 = 109), not at rect.x");
        check(Math.Abs(centreOrigin - 193f) < 0.01f,
            "centred text adds half of the inner width after padding (109 + (288-120)/2 = 193)");
        check(Math.Abs(rightOrigin - 277f) < 0.01f,
            "right-aligned text ends at the padded right edge (109 + 288 - 120 = 277)");
        check(BarPolicy.TextOriginX(float.NaN, -1f, float.NaN, float.NaN, float.NaN, float.NaN, BarPolicy.TextAlignLeft) == 0f,
            "unreadable rect/padding values fall back to a finite origin instead of NaN/Infinity");

        // ---- the fixed light green is independent of the weakspot and C-Foam colours --------------------------
        check(Math.Abs(BarPolicy.StaggerTextRed - 120f / 255f) < 0.0001f
            && Math.Abs(BarPolicy.StaggerTextGreen - 220f / 255f) < 0.0001f
            && Math.Abs(BarPolicy.StaggerTextBlue - 160f / 255f) < 0.0001f,
            "the stagger row is the fixed light green #78DCA0 (120/220/160)");
        check(BarPolicy.StaggerTextRed != WeakspotRed || BarPolicy.StaggerTextGreen != WeakspotGreen
            || BarPolicy.StaggerTextBlue != WeakspotBlue,
            "the stagger green is a different colour from the weakspot red it sits next to");
        check(Math.Abs(BarPolicy.StaggerTextRed - BarPolicy.CfoamTagRed) > 0.0001f
            || Math.Abs(BarPolicy.StaggerTextGreen - BarPolicy.CfoamTagGreen) > 0.0001f
            || Math.Abs(BarPolicy.StaggerTextBlue - BarPolicy.CfoamTagBlue) > 0.0001f,
            "the stagger green is a different colour from the C-Foam amber, so glue mode cannot recolour it");
        check(BarPolicy.SelectEnemyTag(true, true, "[头 1.5×]", "[结沫*3]") == "[结沫*3]"
            && BarPolicy.SelectEnemyTag(false, true, "[头 1.5×]", "[结沫*3]") == "[头 1.5×]",
            "the existing weakspot/C-Foam tag selector still decides the line above the bar, untouched by the green row");
        check(BarPolicy.CfoamTag(9f) == "[结沫*3]" && BarPolicy.CfoamTag(BarPolicy.CfoamImmuneTolerance) == "[结沫*免疫]",
            "the C-Foam tag keeps its own amber wording and immune sentinel");
    }
}
