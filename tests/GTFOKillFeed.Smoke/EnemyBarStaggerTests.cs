using System;
using HawChow.GTFOKillFeed;

// 1.2.23 regression: the anatomy-classified stagger row under the focused-enemy bar. Everything asserted here is a
// production function the shipped read/draw path calls (BarPolicy.BodyGroup, StaggerGroupBuilder/GroupValue,
// StaggerTokens/StaggerLine, StaggerRemaining, DamageAmount, StaggerFontPixels, StaggerRows, StaggerAvailableWidth,
// StaggerRowLeft, TextOriginX), so a wrong body part, a max-for-current swap, a fabricated 0/immune, a centred row,
// a lost pad or an off-screen row fails here instead of only looking right in a source grep. The compiled wiring
// itself (which style/content/colour/fields the row uses) is pinned by scripts/Test-EnemyBarStaggerIl.ps1 on the
// real assembly.
internal static class EnemyBarStaggerTests
{
    // The focused-enemy bar's own weakspot default, duplicated on purpose so a change to either colour has to be a
    // deliberate, reviewed decision.
    private const float WeakspotRed = 0.937f;
    private const float WeakspotGreen = 0.298f;
    private const float WeakspotBlue = 0.298f;

    private static BarPolicy.StaggerValues Values(BarPolicy.StaggerGroupBuilder limbs, BarPolicy.StaggerGroupBuilder torso,
        bool hasNormal, float normalRemaining)
        => new BarPolicy.StaggerValues(limbs.Build(), torso.Build(), hasNormal, normalRemaining);

    internal static void Run(Action<bool, string> check)
    {
        // ---- the requested shape: current durability / fixed capacity for each anatomical group -----------------
        var bothLimbs = new BarPolicy.StaggerGroupBuilder();
        bothLimbs.Add(40f, 40f);
        var chest = new BarPolicy.StaggerGroupBuilder();
        chest.Add(30f, 50f);
        var both = Values(bothLimbs, chest, true, 80f);
        check(BarPolicy.StaggerLine(both, "zh") == "[四肢：40/40][躯体：30/50][普通(剩余)：80]",
            "the row reads [四肢：current/capacity][躯体：current/capacity][普通(剩余)：remaining]");
        check(BarPolicy.StaggerLine(both, "en") == "[Limb 40/40][Torso 30/50][Hitreact(left) 80]",
            "the same values follow the configured language instead of hard-coding Chinese");

        // ---- 15 vs 40: the current value and the fixed capacity are different facts ------------------------------
        var fifteenOfForty = new BarPolicy.StaggerGroupBuilder();
        fifteenOfForty.Add(15f, 40f);
        var torsoOnly = new BarPolicy.StaggerGroupBuilder();
        torsoOnly.Add(25f, 80f);
        check(BarPolicy.StaggerLine(Values(fifteenOfForty, torsoOnly, true, 80f), "zh")
            == "[四肢：15/40][躯体：25/80][普通(剩余)：80]",
            "a limb with 15 current and 40 capacity is 15/40, never 40/40 or 40/15: current and fixed max stay apart");

        // ---- a spread inside one group is a real range, never an average ---------------------------------------
        var limbs = new BarPolicy.StaggerGroupBuilder();
        limbs.Add(15f, 40f);
        limbs.Add(20f, 60f);
        var torso = new BarPolicy.StaggerGroupBuilder();
        torso.Add(25f, 80f);
        torso.Add(30f, 50f);
        check(BarPolicy.GroupValue(limbs.Build()) == "15-20/40-60",
            "different arms/legs report the real current and capacity ranges (15-20/40-60)");
        check(BarPolicy.GroupValue(torso.Build()) == "25-30/50-80",
            "different chest/stomach/hips report their own independent ranges");
        check(BarPolicy.StaggerLine(Values(limbs, torso, true, 80f), "zh")
            == "[四肢：15-20/40-60][躯体：25-30/50-80][普通(剩余)：80]",
            "the two groups aggregate independently in one row");

        // ---- absent data is a dash, never 0 and never "immune" -------------------------------------------------
        var noTorso = new BarPolicy.StaggerGroupBuilder();
        noTorso.Add(15f, 40f);
        var noLimbs = new BarPolicy.StaggerGroupBuilder();
        noLimbs.Add(25f, 80f);
        var nothing = BarPolicy.StaggerValues.None;
        check(BarPolicy.StaggerLine(Values(noTorso, new BarPolicy.StaggerGroupBuilder(), true, 80f), "zh")
            == "[四肢：15/40][躯体：—][普通(剩余)：80]",
            "a body with no breakable torso part prints — instead of borrowing its whole-body hitreact");
        check(BarPolicy.StaggerLine(Values(new BarPolicy.StaggerGroupBuilder(), noLimbs, true, 80f), "zh")
            == "[四肢：—][躯体：25/80][普通(剩余)：80]",
            "a body with no breakable limb prints — while the torso value still shows");
        check(BarPolicy.StaggerLine(nothing, "zh") == "[四肢：—][躯体：—][普通(剩余)：—]",
            "with no data at all every slot is explicitly empty, not 0");
        check(!BarPolicy.StaggerLine(nothing, "zh").Contains("：0") && !BarPolicy.StaggerLine(nothing, "zh").Contains("免疫"),
            "an absent value is a dash: neither a fake 0 nor an invented immunity");

        // ---- broken/unreadable current and a missing capacity stay honest --------------------------------------
        var broken = new BarPolicy.StaggerGroupBuilder();
        broken.Add(0f, 40f);
        broken.Add(-5f, 40f);
        broken.Add(float.NaN, 40f);
        broken.Add(float.PositiveInfinity, 40f);
        check(!broken.Build().HasValue && BarPolicy.GroupValue(broken.Build()) == "—",
            "a destroyed/broken limb (0), a negative, NaN or Infinity current value is not a live group value");
        var unknownCapacity = new BarPolicy.StaggerGroupBuilder();
        unknownCapacity.Add(15f, float.NaN);
        check(BarPolicy.GroupValue(unknownCapacity.Build()) == "15/—",
            "an unreadable fixed capacity keeps the current value and dashes only the capacity side");
        check(BarPolicy.RequiredDamage(0f) == "—" && BarPolicy.RequiredDamage(-5f) == "—"
            && BarPolicy.RequiredDamage(float.NaN) == "—" && BarPolicy.RequiredDamage(float.PositiveInfinity) == "—",
            "0, negative, NaN and Infinity never render as a part number (and never as immunity)");
        check(BarPolicy.RequiredDamage(0.4f) == "1" && BarPolicy.RequiredDamage(39.5f) == "40"
            && BarPolicy.RequiredDamage(40f) == "40",
            "current durability is rounded up, so a part with 0.4 left asks for 1 and not for 0");

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

        // ---- the game's normal-hitreact formula: remaining = max(0, threshold - accumulator) ------------------
        check(BarPolicy.StaggerRemaining(80f, 0f, out float left0) && Math.Abs(left0 - 80f) < 0.0001f,
            "an untouched enemy still needs the full configured threshold (80 - 0)");
        check(BarPolicy.StaggerRemaining(80f, 12.5f, out float left1) && Math.Abs(left1 - 67.5f) < 0.0001f,
            "accumulated damage is subtracted exactly: 80 - 12.5 = 67.5, never rounded up");
        check(BarPolicy.StaggerRemaining(0.5f, 0.125f, out float left2) && Math.Abs(left2 - 0.375f) < 0.0001f
            && BarPolicy.DamageAmount(left2) == "0.375",
            "fractional thresholds keep their precision: 0.5 - 0.125 = 0.375 is shown as 0.375");
        check(BarPolicy.StaggerRemaining(5000f, 100f, out float bigLeft) && Math.Abs(bigLeft - 4900f) < 0.01f
            && BarPolicy.DamageAmount(bigLeft) == "4900",
            "a configured threshold above the enemy's HealthMax is still the real threshold, not hidden");
        check(BarPolicy.StaggerRemaining(80f, 80f, out float spent) && spent == 0f && BarPolicy.DamageAmount(spent) == "0",
            "a spent budget is a real 0 (the next hit triggers), which is not the same as no data");
        check(BarPolicy.StaggerRemaining(80f, 400f, out float clamped) && clamped == 0f,
            "an accumulator already past the threshold clamps at 0 instead of printing a negative requirement");
        check(!BarPolicy.StaggerRemaining(0f, 0f, out _) && !BarPolicy.StaggerRemaining(-3f, 0f, out _)
            && !BarPolicy.StaggerRemaining(float.NaN, 0f, out _) && !BarPolicy.StaggerRemaining(float.PositiveInfinity, 0f, out _),
            "0/negative/NaN/Infinity thresholds are unusable and never turned into a number or an immunity");
        check(!BarPolicy.StaggerRemaining(80f, float.NaN, out _) && !BarPolicy.StaggerRemaining(80f, -1f, out _),
            "an unreadable or negative accumulator leaves the row a dash instead of guessing the remaining damage");
        check(BarPolicy.StaggerLine(Values(bothLimbs, chest, false, 0f), "zh")
            == "[四肢：40/40][躯体：30/50][普通(剩余)：—]",
            "an unproven normal hitreact leaves the dash while the proven part values still show");

        // ---- target switching cannot leave a stale group ------------------------------------------------------
        var switchA = new BarPolicy.StaggerGroupBuilder();
        switchA.Add(15f, 40f);
        string firstTarget = BarPolicy.StaggerLine(Values(switchA, new BarPolicy.StaggerGroupBuilder(), true, 80f), "zh");
        string cleared = BarPolicy.StaggerLine(BarPolicy.StaggerValues.None, "zh");
        var switchB = new BarPolicy.StaggerGroupBuilder();
        switchB.Add(30f, 30f);
        string secondTarget = BarPolicy.StaggerLine(Values(switchB, new BarPolicy.StaggerGroupBuilder(), true, 12f), "zh");
        check(firstTarget == "[四肢：15/40][躯体：—][普通(剩余)：80]" && cleared == "[四肢：—][躯体：—][普通(剩余)：—]"
            && secondTarget == "[四肢：30/30][躯体：—][普通(剩余)：12]",
            "switching targets rebuilds both groups from the new enemy; the cleared state is all dashes, not the old values");

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
        check(BarPolicy.StaggerRows(300f, 200f, 250f, 150f) == 1
            && BarPolicy.StaggerRows(300f, 320f, 250f, 150f) == 2
            && BarPolicy.StaggerRows(300f, 320f, 320f, 150f) == 3
            && BarPolicy.StaggerRows(0f, 200f, 250f, 150f) == 3,
            "a full row fits on one line; a narrow screen wraps at group boundaries (2/3 lines) instead of hiding");
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
        check(BarPolicy.NoStaggerValue != "免疫" && BarPolicy.NoStaggerValue != "0",
            "the stagger no-data marker never borrows the C-Foam immune sentinel or a numeric 0");
    }
}
