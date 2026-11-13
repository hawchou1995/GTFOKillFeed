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
}
