using System;

namespace HawChow.GTFOKillFeed;

// Which outline the crosshair-focus bar draws. "Back" means the crosshair ray lands on the enemy's
// rear hemisphere (the shot direction and the enemy's facing disagree), "weakspot" means the collider
// under the crosshair belongs to a limb the game flags as a weakspot. Pure arithmetic + colours only.
internal enum FocusOutlineKind
{
    None = 0,
    Back = 1,
    Weakspot = 2,
    Prismatic = 3,
}

internal static class FocusOutlinePolicy
{
    internal const bool DefaultEnabled = true;

    // Orange for the back, red shared with the existing weakspot token (#E5484D, the shipped
    // DamageNumbers/WeakspotColor default) so the two reds in the HUD are the same red.
    internal const float BackR = 1.000f, BackG = 0.541f, BackB = 0.118f;
    internal const float WeakspotR = 0.898f, WeakspotG = 0.282f, WeakspotB = 0.302f;

    // Outline is one border-thickness on each edge, scaled with the bar so it stays proportional at 4K.
    internal const float MinThicknessPixels = 1.0f;
    internal const float MaxThicknessPixels = 3.0f;
    internal const float ThicknessPerBarHeight = 0.30f;

    internal static FocusOutlineKind Classify(bool onBack, bool onWeakspot)
    {
        if (onBack && onWeakspot) return FocusOutlineKind.Prismatic;
        if (onWeakspot) return FocusOutlineKind.Weakspot;
        if (onBack) return FocusOutlineKind.Back;
        return FocusOutlineKind.None;
    }

    internal static bool ShouldDraw(bool enabled, FocusOutlineKind kind)
        => enabled && kind != FocusOutlineKind.None;

    // Rear hemisphere test: the dot of the enemy's forward axis and the direction from the enemy to
    // the hit point. A NaN (degenerate transform) is never reported as "back".
    internal static bool IsBehind(float dot)
    {
        if (float.IsNaN(dot) || float.IsInfinity(dot)) return false;
        return dot < 0f;
    }

    internal static float DirectionDot(float forwardX, float forwardY, float forwardZ,
        float toHitX, float toHitY, float toHitZ)
    {
        float length = (float)Math.Sqrt(toHitX * toHitX + toHitY * toHitY + toHitZ * toHitZ);
        if (float.IsNaN(length) || float.IsInfinity(length) || length <= 0.0001f) return float.NaN;
        float dot = (forwardX * toHitX + forwardY * toHitY + forwardZ * toHitZ) / length;
        return float.IsNaN(dot) || float.IsInfinity(dot) ? float.NaN : dot;
    }

    internal static float Thickness(float barHeightPixels)
    {
        if (float.IsNaN(barHeightPixels) || float.IsInfinity(barHeightPixels)) return MinThicknessPixels;
        float thickness = barHeightPixels * ThicknessPerBarHeight;
        if (thickness < MinThicknessPixels) return MinThicknessPixels;
        if (thickness > MaxThicknessPixels) return MaxThicknessPixels;
        return thickness;
    }

    // The prismatic frame is a foil rim, so it gets a little more body than the flat orange/red one:
    // at 5 px bar height the flat ring is 1.5 px, which is too thin to show a hue gradient at all.
    internal const float FoilThicknessPerBarHeight = 0.55f;
    internal const float FoilMinThicknessPixels = 2f;
    internal const float FoilMaxThicknessPixels = 5f;

    internal static float FoilThickness(float barHeightPixels)
    {
        if (float.IsNaN(barHeightPixels) || float.IsInfinity(barHeightPixels)) return FoilMinThicknessPixels;
        float thickness = barHeightPixels * FoilThicknessPerBarHeight;
        if (thickness < FoilMinThicknessPixels) return FoilMinThicknessPixels;
        if (thickness > FoilMaxThicknessPixels) return FoilMaxThicknessPixels;
        return thickness;
    }

    // Prismatic frames are drawn as a foil rim: the hue advances around the perimeter in segments
    // instead of one flat colour, which is what makes the border read as holographic foil.
    internal const int FoilFrameSegments = 5; // per edge, 4 edges = 20 segments around the bar

    internal static void FoilFrameColor(int index, int total, float phase, out float red, out float green, out float blue)
    {
        int span = total <= 0 ? 1 : total;
        float t = (index % span) / (float)span;
        DamageFxPolicy.HueColor(DamageFxPolicy.WrapPhase(phase + t), 0.55f, 1f, out red, out green, out blue);
    }

    internal static void OutlineColor(FocusOutlineKind kind, float phase, out float red, out float green, out float blue)
    {
        switch (kind)
        {
            case FocusOutlineKind.Back: red = BackR; green = BackG; blue = BackB; return;
            case FocusOutlineKind.Weakspot: red = WeakspotR; green = WeakspotG; blue = WeakspotB; return;
            case FocusOutlineKind.Prismatic: DamageFxPolicy.PrismaticColor(phase, out red, out green, out blue); return;
            default: red = 0f; green = 0f; blue = 0f; return;
        }
    }
}
