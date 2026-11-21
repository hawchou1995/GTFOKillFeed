using System;
using System.Globalization;
using System.Collections.Generic;

namespace HawChow.GTFOKillFeed;

// Pure rules for the focused-enemy health bar. No Unity or GTFO types, so the smoke test project
// compiles this file on its own and asserts every branch offline.
internal readonly struct WeakspotPartMultiplier
{
    internal readonly string Part;
    internal readonly float Multiplier;

    internal WeakspotPartMultiplier(string part, float multiplier)
    {
        Part = part;
        Multiplier = multiplier;
    }
}

internal static class BarPolicy
{
    internal enum HealthBand { Base, Yellow, Red }

    internal const float DefaultWidth = 200f;
    internal const float DefaultHeight = 5f;
    internal const float DefaultOpacity = 0.55f;
    internal const float DefaultNumberSize = 12f;
    internal const float DefaultHeadOffsetMetres = 0.12f;
    internal const float DefaultVerticalOffset = -8f;
    internal const float DefaultMinScreenFraction = 0.10f;
    internal const float DefaultMaxScreenFraction = 0.46f;
    internal const float DefaultScreenAnchorFraction = 0.30f;
    internal const float DefaultMaxDistance = 60f;
    internal const float DefaultYellowThresholdPercent = 66f;
    internal const float DefaultRedThresholdPercent = 33f;
    internal const float MinWidth = 90f;
    internal const float MaxWidth = 420f;
    internal const float MinHeight = 2f;
    internal const float MaxHeight = 16f;
    internal const float MinOpacity = 0.15f;
    internal const float MaxOpacity = 1f;
    internal const float MinNumberSize = 8f;
    internal const float MaxNumberSize = 22f;
    internal const float MinHeadOffsetMetres = 0f;
    internal const float MaxHeadOffsetMetres = 2f;
    internal const float MinVerticalOffset = -400f;
    internal const float MaxVerticalOffset = 400f;
    internal const float MinRange = 5f;
    internal const float MaxRange = 300f;

    internal const float HoldSeconds = 0.15f;
    internal const float FadeInSeconds = 0.10f;
    internal const float FadeOutSeconds = 0.20f;
    internal const float GhostDecayPerSecond = 2.6f;
    internal const float GhostAlpha = 0.22f;
    internal const float FillAlpha = 0.90f;
    internal const float TrackAlpha = 0.28f;
    internal const float BorderAlpha = 0.45f;
    internal const float TickAlpha = 0.35f;
    internal const int Segments = 4;
    internal const int MaxLabelNameChars = 28;

    // 0..1 health ratio; anything unreadable or non-positive reads as empty.
    internal static float FillFraction(float health, float maxHealth)
    {
        if (!KillPolicy.IsFinite(health) || !KillPolicy.IsFinite(maxHealth) || maxHealth <= 0f) return 0f;
        if (health <= 0f) return 0f;
        float fraction = health / maxHealth;
        return fraction >= 1f ? 1f : fraction;
    }

    // Exact configured boundary belongs to the lower-color band: 66% is yellow; 33% is red.
    internal static HealthBand HealthColorBand(float fraction, float yellowThresholdPercent, float redThresholdPercent)
    {
        if (!KillPolicy.IsFinite(fraction)) return HealthBand.Base;
        float yellow = FeedbackPolicy.Clamp(yellowThresholdPercent, 0f, 100f, DefaultYellowThresholdPercent);
        float red = FeedbackPolicy.Clamp(redThresholdPercent, 0f, 100f, DefaultRedThresholdPercent);
        if (red > yellow) red = yellow;
        float percent = FeedbackPolicy.Clamp(fraction, 0f, 1f, 0f) * 100f;
        if (percent <= red) return HealthBand.Red;
        if (percent <= yellow) return HealthBand.Yellow;
        return HealthBand.Base;
    }

    internal static int DisplayValue(float value)
    {
        if (!KillPolicy.IsFinite(value) || value <= 0f) return 0;
        if (value >= 9999999f) return 9999999;
        return (int)Math.Round(value, MidpointRounding.AwayFromZero);
    }

    internal static string Readout(float health, float maxHealth)
        => DisplayValue(health).ToString(CultureInfo.InvariantCulture) + " / " +
           DisplayValue(maxHealth).ToString(CultureInfo.InvariantCulture);

    // One compact line above the bar: "name   remaining / maximum".
    internal static string Label(string name, float health, float maxHealth, bool showName, bool showNumbers)
    {
        string title = showName ? TrimName(name) : null;
        string numbers = showNumbers ? Readout(health, maxHealth) : null;
        if (title == null && numbers == null) return string.Empty;
        if (title == null) return numbers;
        if (numbers == null) return title;
        return title + "   " + numbers;
    }

    internal static string TrimName(string name) => TrimName(name, MaxLabelNameChars);

    internal static string TrimName(string name, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Unknown";
        string trimmed = name.Trim();
        int limit = Math.Max(1, maxChars);
        if (trimmed.Length <= limit) return trimmed;
        if (limit == 1) return "…";
        return trimmed.Substring(0, limit - 1) + "…";
    }

    internal static string HeadPartLabel(string language)
        => KillPolicy.IsChinese(language) ? "头" : "Head";


    internal static string GenericWeakspotPart(string language)
        => KillPolicy.IsChinese(language) ? "弱点" : "Weakspot";

    internal static string PartLabel(string debugName, string language)
    {
        string value = debugName ?? string.Empty;
        if (ContainsAny(value, "head", "skull", "face", "cranial", "头", "頭")) return HeadPartLabel(language);
        if (ContainsAny(value, "back", "rear", "spine", "背")) return KillPolicy.IsChinese(language) ? "背部" : "Back";
        if (ContainsAny(value, "tail", "尾")) return KillPolicy.IsChinese(language) ? "尾部" : "Tail";
        if (ContainsAny(value, "tentacle", "触手")) return KillPolicy.IsChinese(language) ? "触手" : "Tentacle";
        if (ContainsAny(value, "arm", "hand", "臂", "手")) return KillPolicy.IsChinese(language) ? "手臂" : "Arm";
        if (ContainsAny(value, "leg", "foot", "腿", "足")) return KillPolicy.IsChinese(language) ? "腿部" : "Leg";
        if (ContainsAny(value, "spore", "孢")) return KillPolicy.IsChinese(language) ? "孢子" : "Spore";
        if (ContainsAny(value, "sac", "bladder", "pustule", "囊")) return KillPolicy.IsChinese(language) ? "囊体" : "Sac";
        return GenericWeakspotPart(language);
    }

    internal static string WeakspotSuffix(string part, bool enabled)
        => enabled && !string.IsNullOrWhiteSpace(part) ? "[" + part.Trim() + "]" : string.Empty;

    internal const int MaxWeakspotTags = 4;

    internal static string WeakspotMultiplierTag(string part, float multiplier, string language)
    {
        if (string.IsNullOrWhiteSpace(part)) return string.Empty;
        string value = KillPolicy.IsFinite(multiplier) && multiplier >= 0f
            ? multiplier.ToString("0.##", CultureInfo.InvariantCulture) : "?";
        string separator = KillPolicy.IsChinese(language) ? string.Empty : " ";
        return "[" + part.Trim() + separator + value + "×]";
    }

    internal static string WeakspotTags(IEnumerable<WeakspotPartMultiplier> parts, string language)
    {
        if (parts == null) return string.Empty;
        var unique = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in parts)
        {
            string tag = WeakspotMultiplierTag(item.Part, item.Multiplier, language);
            if (tag.Length > 0 && seen.Add(tag)) unique.Add(tag);
        }
        if (unique.Count < MaxWeakspotTags) return string.Concat(unique);
        var visible = new List<string>(MaxWeakspotTags);
        for (int i = 0; i < MaxWeakspotTags - 1; i++) visible.Add(unique[i]);
        visible.Add("[+" + (unique.Count - (MaxWeakspotTags - 1)).ToString(CultureInfo.InvariantCulture) + "]");
        return string.Concat(visible);
    }
    internal const float CfoamUnitsPerBlob = 3f;
    internal const float CfoamImmuneTolerance = 1000000f;
    // Complete C-Foam tag colour: a restrained warm amber (#F2A65A, 242/166/90) so glue mode reads as
    // its own state instead of blending into the neutral monster name. Fixed, not configured.
    internal const float CfoamTagRed = 242f / 255f;
    internal const float CfoamTagGreen = 166f / 255f;
    internal const float CfoamTagBlue = 90f / 255f;
    internal static string CfoamTag(float glueTolerance)
    {
        if (!KillPolicy.IsFinite(glueTolerance) || glueTolerance < 0f) return string.Empty;
        if (glueTolerance >= CfoamImmuneTolerance) return "[结沫*免疫]";
        int blobs = (int)Math.Ceiling(glueTolerance / CfoamUnitsPerBlob);
        return "[结沫*" + blobs.ToString(CultureInfo.InvariantCulture) + "]";
    }
    internal static string SelectEnemyTag(bool glueGunWielded, bool showWeakspotPart, string weakspotTag, string cfoamTag)
    {
        if (glueGunWielded) return cfoamTag ?? string.Empty;
        return showWeakspotPart ? (weakspotTag ?? string.Empty) : string.Empty;
    }


    private static bool ContainsAny(string value, string first, string second, string third, string fourth, string fifth, string sixth)
        => Contains(value, first) || Contains(value, second) || Contains(value, third) || Contains(value, fourth) || Contains(value, fifth) || Contains(value, sixth);

    private static bool ContainsAny(string value, string first, string second, string third, string fourth)
        => Contains(value, first) || Contains(value, second) || Contains(value, third) || Contains(value, fourth);

    private static bool ContainsAny(string value, string first, string second, string third)
        => Contains(value, first) || Contains(value, second) || Contains(value, third);

    private static bool ContainsAny(string value, string first, string second)
        => Contains(value, first) || Contains(value, second);

    private static bool Contains(string value, string token)
        => value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;

    internal static bool TryParseHexColor(string value, out float red, out float green, out float blue, out float alpha)
    {
        red = green = blue = alpha = 1f;
        if (string.IsNullOrWhiteSpace(value)) return false;
        string hex = value.Trim();
        if (hex.StartsWith("#", StringComparison.Ordinal)) hex = hex.Substring(1);
        if (hex.Length != 6 && hex.Length != 8) return false;
        if (!TryHexByte(hex, 0, out byte r) || !TryHexByte(hex, 2, out byte g) || !TryHexByte(hex, 4, out byte b)) return false;
        byte a = 255;
        if (hex.Length == 8 && !TryHexByte(hex, 6, out a)) return false;
        red = r / 255f; green = g / 255f; blue = b / 255f; alpha = a / 255f;
        return true;
    }

    private static bool TryHexByte(string value, int offset, out byte result)
        => byte.TryParse(value.Substring(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result);

    internal static float HeaderWidth(float barWidth, float nameWidth, float partWidth, float numberWidth,
        float padding, float spacing, float maxWidth)
    {
        int segments = (nameWidth > 0f ? 1 : 0) + (partWidth > 0f ? 1 : 0) + (numberWidth > 0f ? 1 : 0);
        float content = Math.Max(0f, nameWidth) + Math.Max(0f, partWidth) + Math.Max(0f, numberWidth)
            + Math.Max(0, segments - 1) * Math.Max(0f, spacing) + Math.Max(0f, padding) * 2f;
        float desired = Math.Max(Math.Max(0f, barWidth), content);
        return KillPolicy.IsFinite(maxWidth) && maxWidth > 0f ? Math.Min(desired, maxWidth) : desired;
    }


    internal static float ResolutionScale(int screenHeight)
        => FeedbackPolicy.Clamp(screenHeight / 1080f, 0.75f, 1.6f, 1f);

    internal static float Width(float configured, int screenHeight, int screenWidth)
    {
        float width = FeedbackPolicy.Clamp(configured, MinWidth, MaxWidth, DefaultWidth) * ResolutionScale(screenHeight);
        float limit = screenWidth > 0 ? screenWidth * 0.9f : width;
        if (width > limit) width = limit;
        return width > 0f ? width : DefaultWidth;
    }

    internal static float Height(float configured, int screenHeight)
        => FeedbackPolicy.Clamp(configured, MinHeight, MaxHeight, DefaultHeight) * ResolutionScale(screenHeight);

    internal static float NumberSize(float configured, int screenHeight)
        => FeedbackPolicy.Clamp(configured, MinNumberSize, MaxNumberSize, DefaultNumberSize) * ResolutionScale(screenHeight);

    internal static float HeadOffset(float configured)
        => FeedbackPolicy.Clamp(configured, MinHeadOffsetMetres, MaxHeadOffsetMetres, DefaultHeadOffsetMetres);

    internal static float VerticalOffset(float configured)
        => FeedbackPolicy.Clamp(configured, MinVerticalOffset, MaxVerticalOffset, DefaultVerticalOffset);

    internal static float MaxDistance(float configured)
        => FeedbackPolicy.Clamp(configured, MinRange, MaxRange, DefaultMaxDistance);

    internal static float Opacity(float configured)
        => FeedbackPolicy.Clamp(configured, MinOpacity, MaxOpacity, DefaultOpacity);

    internal static float ScreenAnchorY(float fraction, int screenHeight)
        => FeedbackPolicy.Clamp(fraction, 0.05f, 0.95f, DefaultScreenAnchorFraction) * screenHeight;

    // Keeps the bar inside the configured upper band so it never drifts onto the reticle.
    internal static float AnchorY(float anchoredY, int screenHeight, float minFraction, float maxFraction)
    {
        float top = FeedbackPolicy.Clamp(minFraction, 0.02f, 0.90f, DefaultMinScreenFraction) * screenHeight;
        float bottom = FeedbackPolicy.Clamp(maxFraction, 0.05f, 0.95f, DefaultMaxScreenFraction) * screenHeight;
        if (bottom < top)
        {
            float swap = top;
            top = bottom;
            bottom = swap;
        }
        float y = KillPolicy.IsFinite(anchoredY) ? anchoredY : top;
        if (y < top) return top;
        if (y > bottom) return bottom;
        return y;
    }

    // Horizontal centre of the bar, kept fully on screen with a margin.
    internal static float AnchorX(float centredX, float barWidth, int screenWidth, int screenHeight)
    {
        if (screenWidth <= 0) return 0f;
        float margin = 12f * ResolutionScale(screenHeight);
        float half = barWidth * 0.5f;
        float min = half + margin;
        float max = screenWidth - half - margin;
        float x = KillPolicy.IsFinite(centredX) ? centredX : screenWidth * 0.5f;
        if (max < min) return screenWidth * 0.5f;
        if (x < min) return min;
        if (x > max) return max;
        return x;
    }

    // Trailing damage ghost: decays toward the live fraction but never below it.
    internal static float GhostDecay(float value, float floor, float deltaSeconds)
    {
        float target = KillPolicy.IsFinite(floor) ? floor : 0f;
        if (!KillPolicy.IsFinite(value)) return target;
        if (!KillPolicy.IsFinite(deltaSeconds) || deltaSeconds <= 0f) return value < target ? target : value;
        float next = value - GhostDecayPerSecond * deltaSeconds;
        return next < target ? target : next;
    }

    // Fade in while a target is held, hold briefly, then fade out after loss.
    internal static float AlphaFor(bool visible, float sinceShown, float sinceLost)
    {
        if (visible)
        {
            if (!KillPolicy.IsFinite(sinceShown) || sinceShown <= 0f) return 0f;
            float t = sinceShown / FadeInSeconds;
            return t >= 1f ? 1f : t;
        }
        if (!KillPolicy.IsFinite(sinceLost) || sinceLost < 0f) return 0f;
        if (sinceLost <= HoldSeconds) return 1f;
        float u = (sinceLost - HoldSeconds) / FadeOutSeconds;
        return u >= 1f ? 0f : 1f - u;
    }

    // A hard drop finishes the fade immediately, so nothing can render afterwards.
    internal static float DroppedLostSeconds => HoldSeconds + FadeOutSeconds;

    // Draw only when something can actually be painted: a live target, or a cached anchor that is
    // still fading out. Without this, a dropped target keeps drawing at full opacity.
    internal static bool ShouldDraw(bool haveTarget, bool hasAnchor, float alpha)
        => alpha > 0.002f && (haveTarget || hasAnchor);

    // Tick positions as fractions of the bar; returns -1 for out-of-range indices.
    internal static float TickFraction(int index, int count)
    {
        if (count < 2 || index <= 0 || index >= count) return -1f;
        return (float)index / count;
    }

    internal static bool ShouldShow(bool hasTarget, float distance, float maxDistance)
    {
        if (!hasTarget) return false;
        if (!KillPolicy.IsFinite(distance) || distance < 0f) return false;
        return distance <= MaxDistance(maxDistance) + 0.01f;
    }

    internal static bool IsHeadMode(string anchor)
        => !string.Equals(anchor, "Screen", StringComparison.OrdinalIgnoreCase);

    // ---- 1.2.22: the stagger (hitreact) readout under the focused-enemy bar --------------------------
    // Fixed light green #78DCA0 (120/220/160). Deliberately NOT configurable and NOT shared with any other
    // label: the weakspot tags keep their configured red and the C-Foam tag keeps its amber, so a glance can
    // never confuse "how much damage until this enemy flinches" with "where the weakspot is".
    internal const float StaggerTextRed = 120f / 255f;
    internal const float StaggerTextGreen = 220f / 255f;
    internal const float StaggerTextBlue = 160f / 255f;

    // The ONE accessor of that green. EnemyBar (the row) and DamageNumbers (the digits a hit proved it staggered with,
    // 1.2.24) both call this, so the HUD cannot grow a second, slightly different green - the compiled gate asserts
    // that both call sites go through this method instead of writing their own triple.
    internal static void StaggerInk(out float red, out float green, out float blue)
    {
        red = StaggerTextRed;
        green = StaggerTextGreen;
        blue = StaggerTextBlue;
    }

    // Two different questions, kept apart on purpose:
    //   * limb destruction - the damage that still breaks ONE destructible limb, read from the live limb
    //     (m_health) for the limbs the game itself treats as destroyable; a REMAINING amount, per limb;
    //   * normal hitreact  - GameData.HealthData.DamageUntilHitreact minus the live accumulator
    //     (Dam_EnemyDamageBase.m_damBuildToHitreact). This is the game's own formula, proven on the shipped binary:
    //       Dam_EnemyDamageBase::ProcessReceivedDamage  RVA 0x137E570 (Il2CppDumper script.json)
    //         0x137E639  mulss  xmm0, xmm10                ; damage * multiplier
    //         0x137E63E  addss  xmm0, [rbx+0x150]          ; + m_damBuildToHitreact   (dump.cs: 0x150)
    //         0x137E646  movss  [rbx+0x150], xmm0         ; accumulator = the sum
    //         0x137E667  mov    rax, [rax+0x150]
    //         0x137E677  mov    rax, [rax+0x40]           ; the block that owns HealthData
    //         0x137E684  comiss xmm0, [rax+0x14]          ; accumulator vs DamageUntilHitreact (HealthData 0x14)
    //         0x137E688  jae    0x137E693                 ; trigger when accumulator >= threshold
    //         0x137E693  mov    [rbx+0x150], edi           ; edi == 0 (xor edi,edi @0x137E5CB): accumulator reset
    //     So the data field is an ABSOLUTE damage budget in the same units as incoming damage - no ratio, no
    //     HealthMax factor, no weapon-panel conversion - and the damage still needed is
    //     max(0, threshold - m_damBuildToHitreact). The row therefore shows a computed REMAINING value, never a
    //     guessed magnitude: only a readable, finite, strictly positive threshold plus a finite accumulator is
    //     shown; anything else is the explicit no-data dash (never "immune", never a fabricated 0, never the
    //     C-Foam sentinel).
    internal readonly struct StaggerGroupValues
    {
        internal readonly bool HasValue;
        internal readonly bool CapacityKnown;
        internal readonly float CurrentMin;
        internal readonly float CurrentMax;
        internal readonly float CapacityMin;
        internal readonly float CapacityMax;

        internal StaggerGroupValues(bool hasValue, bool capacityKnown, float currentMin, float currentMax,
            float capacityMin, float capacityMax)
        {
            HasValue = hasValue;
            CapacityKnown = capacityKnown;
            CurrentMin = currentMin;
            CurrentMax = currentMax;
            CapacityMin = capacityMin;
            CapacityMax = capacityMax;
        }

        internal static StaggerGroupValues None => new StaggerGroupValues(false, false, 0f, 0f, 0f, 0f);
    }

    // Mutable builder used by the live read path. It only accepts a readable, strictly positive CURRENT durability;
    // the fixed capacity (the game's own m_healthMax) is optional and is never substituted for the current value.
    internal struct StaggerGroupBuilder
    {
        private bool _has;
        private bool _capacityKnown;
        private float _currentMin, _currentMax, _capacityMin, _capacityMax;

        internal void Add(float current, float capacity)
        {
            if (!KillPolicy.IsFinite(current) || current <= 0f) return;
            bool capReadable = KillPolicy.IsFinite(capacity) && capacity > 0f;
            if (!_has)
            {
                _has = true;
                _capacityKnown = capReadable;
                _currentMin = current;
                _currentMax = current;
                _capacityMin = capReadable ? capacity : 0f;
                _capacityMax = capReadable ? capacity : 0f;
                return;
            }
            if (current < _currentMin) _currentMin = current;
            if (current > _currentMax) _currentMax = current;
            if (!capReadable) { _capacityKnown = false; return; }
            if (!_capacityKnown) return;
            if (capacity < _capacityMin) _capacityMin = capacity;
            if (capacity > _capacityMax) _capacityMax = capacity;
        }

        internal StaggerGroupValues Build()
            => _has
                ? new StaggerGroupValues(true, _capacityKnown, _currentMin, _currentMax, _capacityMin, _capacityMax)
                : StaggerGroupValues.None;
    }

    internal readonly struct StaggerValues
    {
        internal readonly StaggerGroupValues Limbs;
        internal readonly StaggerGroupValues Torso;
        internal readonly bool HasNormal;
        internal readonly float NormalRemaining;

        internal StaggerValues(StaggerGroupValues limbs, StaggerGroupValues torso, bool hasNormal, float normalRemaining)
        {
            Limbs = limbs;
            Torso = torso;
            HasNormal = hasNormal;
            NormalRemaining = normalRemaining;
        }

        internal static StaggerValues None => new StaggerValues(StaggerGroupValues.None, StaggerGroupValues.None, false, 0f);
    }

    // 1.2.23: the ANATOMICAL group. The source is CharacterDestruction.CD_BodypartType carried by the limb's own
    // destruction collider (Dam_EnemyDamageLimb.m_destruction.m_bodyPart), verified against the installed
    // Modules-ASM interop by scripts/Test-EnemyBarStaggerIl.ps1. It is deliberately NOT eLimbDamageType
    // (Normal/Weakspot/Armor): that enum classifies armor/weakspot and can never name a body part.
    internal enum StaggerBodyGroup { Unknown = 0, Limbs = 1, Torso = 2 }

    internal const int PartUnspecified = 0;
    internal const int PartHead = 1;
    internal const int PartArmUpperRight = 2;
    internal const int PartArmLowerRight = 3;
    internal const int PartArmUpperLeft = 4;
    internal const int PartArmLowerLeft = 5;
    internal const int PartChest = 6;
    internal const int PartStomach = 7;
    internal const int PartHips = 8;
    internal const int PartLegUpperRight = 9;
    internal const int PartLegLowerRight = 10;
    internal const int PartLegUpperLeft = 11;
    internal const int PartLegLowerLeft = 12;

    // Arms/hands (upper + lower arm) and legs/feet (upper + lower leg) are the two "limb" chains. Chest/stomach/
    // hips are the only torso parts the shipped enum carries; it has no Back/Waist value at all, so those are not
    // guessed from a name, index, magnitude or the separate BodyPartLocation model-location system. Head,
    // Unspecified and every future/mod value stay Unknown and are never folded into either group.
    internal static StaggerBodyGroup BodyGroup(int bodyPart)
    {
        switch (bodyPart)
        {
            case PartArmUpperRight:
            case PartArmLowerRight:
            case PartArmUpperLeft:
            case PartArmLowerLeft:
            case PartLegUpperRight:
            case PartLegLowerRight:
            case PartLegUpperLeft:
            case PartLegLowerLeft:
                return StaggerBodyGroup.Limbs;
            case PartChest:
            case PartStomach:
            case PartHips:
                return StaggerBodyGroup.Torso;
            default:
                return StaggerBodyGroup.Unknown;
        }
    }

    internal const string NoStaggerValue = "—";

    // Damage still required for ONE part, rounded UP: a part with 0.4 HP left needs one more point of damage,
    // never "0". Only a finite, strictly positive value is rendered; everything else is the no-data dash.
    internal static string RequiredDamage(float value)
    {
        if (!KillPolicy.IsFinite(value) || value <= 0f) return NoStaggerValue;
        double rounded = Math.Ceiling(value);
        return rounded >= 9999999d ? "9999999" : rounded.ToString(CultureInfo.InvariantCulture);
    }

    // The game's own normal-hitreact formula: remaining = max(0, threshold - accumulator). Both inputs must be
    // readable and finite, and the threshold must be strictly positive; a spent budget is a real 0 (the next hit
    // triggers the hitreact), which is different from "no data".
    internal static bool StaggerRemaining(float threshold, float accumulated, out float remaining)
    {
        remaining = 0f;
        if (!KillPolicy.IsFinite(threshold) || threshold <= 0f) return false;
        if (!KillPolicy.IsFinite(accumulated) || accumulated < 0f) return false;
        float left = threshold - accumulated;
        remaining = left > 0f ? left : 0f;
        return true;
    }

    // A live damage amount, never ceiling-rounded: up to three decimals with trailing zeros trimmed, so 0.5 stays
    // 0.5 and 77.25 stays 77.25. No multiplier, no unit conversion.
    internal static string DamageAmount(float value)
    {
        if (!KillPolicy.IsFinite(value) || value < 0f) return NoStaggerValue;
        double rounded = Math.Round((double)value, 3, MidpointRounding.AwayFromZero);
        if (rounded >= 9999999d) return "9999999";
        string text = rounded.ToString("0.###", CultureInfo.InvariantCulture);
        return text == "-0" ? "0" : text;
    }

    private static string DamageSpan(float min, float max)
    {
        if (!KillPolicy.IsFinite(min) || !KillPolicy.IsFinite(max)) return NoStaggerValue;
        if (Math.Abs(max - min) < 0.01f) return RequiredDamage(min);
        return RequiredDamage(min) + "-" + RequiredDamage(max);
    }

    // current/capacity is deliberate: the CURRENT durability is the damage the next break still needs, while the
    // CAPACITY is the fixed maximum the game gave that part. Showing both means 15 (current) and 40 (capacity)
    // can never be mistaken for one another. A group with no live part is the explicit dash; an unreadable
    // capacity keeps the current value and dashes only the capacity side.
    internal static string GroupValue(StaggerGroupValues group)
    {
        if (!group.HasValue) return NoStaggerValue;
        string current = DamageSpan(group.CurrentMin, group.CurrentMax);
        string capacity = group.CapacityKnown ? DamageSpan(group.CapacityMin, group.CapacityMax) : NoStaggerValue;
        return current + "/" + capacity;
    }

    internal static void StaggerTokens(StaggerValues values, string language, out string limbs, out string torso, out string normal)
    {
        bool chinese = KillPolicy.IsChinese(language);
        string limbValue = GroupValue(values.Limbs);
        string torsoValue = GroupValue(values.Torso);
        string normalValue = values.HasNormal ? DamageAmount(values.NormalRemaining) : NoStaggerValue;
        limbs = chinese ? "[四肢：" + limbValue + "]" : "[Limb " + limbValue + "]";
        torso = chinese ? "[躯体：" + torsoValue + "]" : "[Torso " + torsoValue + "]";
        normal = chinese ? "[普通(剩余)：" + normalValue + "]" : "[Hitreact(left) " + normalValue + "]";
    }

    internal static string StaggerLine(StaggerValues values, string language)
    {
        StaggerTokens(values, language, out string limbs, out string torso, out string normal);
        return limbs + torso + normal;
    }

    // ---- 1.2.23: shared text origin and left-anchored row layout ------------------------------------
    // The row must start where the monster name's FIRST GLYPH really starts, which is the name rect origin plus the
    // style's padding and content offset after alignment - not the centre of a centred rect and not a measured text
    // width. The same helper is used for the name and the row, so the two cannot drift apart.
    internal const int TextAlignLeft = 0;
    internal const int TextAlignCenter = 1;
    internal const int TextAlignRight = 2;

    internal static float TextOriginX(float rectX, float rectWidth, float contentWidth,
        float paddingLeft, float paddingRight, float contentOffsetX, int alignment)
    {
        if (!KillPolicy.IsFinite(rectX)) rectX = 0f;
        if (!KillPolicy.IsFinite(rectWidth) || rectWidth < 0f) rectWidth = 0f;
        if (!KillPolicy.IsFinite(contentWidth) || contentWidth < 0f) contentWidth = 0f;
        float leftPad = KillPolicy.IsFinite(paddingLeft) && paddingLeft > 0f ? paddingLeft : 0f;
        float rightPad = KillPolicy.IsFinite(paddingRight) && paddingRight > 0f ? paddingRight : 0f;
        float offset = KillPolicy.IsFinite(contentOffsetX) ? contentOffsetX : 0f;
        float inner = rectWidth - leftPad - rightPad;
        if (inner < 0f) inner = 0f;
        float left = rectX + leftPad + offset;
        if (alignment == TextAlignCenter) return left + Math.Max(0f, (inner - contentWidth) * 0.5f);
        if (alignment == TextAlignRight) return left + inner - contentWidth;
        return left;
    }

    internal static int StaggerFontPixels(float configured, int screenHeight)
    {
        float size = NumberSize(configured, screenHeight);
        int pixels = (int)Math.Round(size, MidpointRounding.AwayFromZero);
        int minPixels = StaggerMinFontPixels(screenHeight);
        return pixels < minPixels ? minPixels : pixels;
    }

    internal static int StaggerMinFontPixels(int screenHeight)
        => Math.Max(7, (int)Math.Round(8f * ResolutionScale(screenHeight), MidpointRounding.AwayFromZero));

    internal static float StaggerRowMargin(int screenHeight) => 6f * ResolutionScale(screenHeight);

    // Width still available to the right of the aligned first glyph. A bar/name left of the margin is treated as
    // the margin (the row is clamped there), never as extra room that runs off-screen.
    internal static float StaggerAvailableWidth(float left, int screenWidth, int screenHeight)
    {
        if (screenWidth <= 0) return 0f;
        float margin = StaggerRowMargin(screenHeight);
        float start = KillPolicy.IsFinite(left) ? Math.Max(left, margin) : margin;
        return Math.Max(0f, screenWidth - margin - start);
    }

    // One line when the whole row fits, two when limbs+torso and the normal slot fit separately, otherwise three.
    // A row is never dropped merely because it is wide: it is scaled first and wrapped here, so no value silently
    // disappears on a narrow screen.
    internal static int StaggerRows(float available, float fullWidth, float limbsAndTorsoWidth, float widestTokenWidth)
    {
        if (!KillPolicy.IsFinite(available) || available <= 0f) return 3;
        if (KillPolicy.IsFinite(fullWidth) && fullWidth <= available) return 1;
        if (KillPolicy.IsFinite(limbsAndTorsoWidth) && limbsAndTorsoWidth <= available &&
            KillPolicy.IsFinite(widestTokenWidth) && widestTokenWidth <= available) return 2;
        return 3;
    }

    // Left anchor first, then clamp inside the screen margins. It never centres the row: 960 stays 960 on a 1920
    // screen instead of becoming 860 for a 200 px row.
    internal static float StaggerRowLeft(float desiredLeft, float width, int screenWidth, int screenHeight)
    {
        float margin = StaggerRowMargin(screenHeight);
        float w = KillPolicy.IsFinite(width) && width > 0f ? width : 0f;
        float max = screenWidth - margin - w;
        if (max < margin) return margin;
        float x = KillPolicy.IsFinite(desiredLeft) ? desiredLeft : margin;
        if (x < margin) return margin;
        if (x > max) return max;
        return x;
    }

    // The row hangs directly under the bar; the caller still owns the final on-screen check.
    internal static float StaggerRowY(float barBottom, int screenHeight) => barBottom + 2f * ResolutionScale(screenHeight);
}
