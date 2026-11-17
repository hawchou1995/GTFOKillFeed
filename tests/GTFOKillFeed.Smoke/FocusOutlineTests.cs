using System;
using HawChow.GTFOKillFeed;

// Focus-outline classification without Unity: back only is orange, weakspot only is red, both is
// prismatic, neither draws nothing. The rear-hemisphere test and the thickness clamp are asserted too
// because a NaN transform must never paint an outline.
internal static class FocusOutlineTests
{
    internal static void Run(Action<bool, string> check)
    {
        check(FocusOutlinePolicy.Classify(false, false) == FocusOutlineKind.None, "focus-outline: neither back nor weakspot draws nothing");
        check(FocusOutlinePolicy.Classify(true, false) == FocusOutlineKind.Back, "focus-outline: a back hit uses the back outline");
        check(FocusOutlinePolicy.Classify(false, true) == FocusOutlineKind.Weakspot, "focus-outline: a weakspot hit uses the weakspot outline");
        check(FocusOutlinePolicy.Classify(true, true) == FocusOutlineKind.Prismatic, "focus-outline: back plus weakspot is prismatic");

        check(FocusOutlinePolicy.ShouldDraw(true, FocusOutlineKind.Back), "focus-outline: enabled draws a classified outline");
        check(!FocusOutlinePolicy.ShouldDraw(false, FocusOutlineKind.Back), "focus-outline: the option off draws nothing");
        check(!FocusOutlinePolicy.ShouldDraw(true, FocusOutlineKind.None), "focus-outline: an unclassified hit draws nothing");

        check(FocusOutlinePolicy.IsBehind(-0.2f), "focus-outline: a negative dot is behind the enemy");
        check(!FocusOutlinePolicy.IsBehind(0f), "focus-outline: a zero dot is not behind the enemy");
        check(!FocusOutlinePolicy.IsBehind(0.4f), "focus-outline: a positive dot is in front of the enemy");
        check(!FocusOutlinePolicy.IsBehind(float.NaN), "focus-outline: a NaN dot is never treated as behind");
        check(!FocusOutlinePolicy.IsBehind(float.PositiveInfinity), "focus-outline: an infinite dot is never treated as behind");

        check(Math.Abs(FocusOutlinePolicy.DirectionDot(0f, 0f, 1f, 0f, 0f, 2f) - 1f) < 0.0001f,
            "focus-outline: a hit in front of the enemy gives dot +1");
        check(Math.Abs(FocusOutlinePolicy.DirectionDot(0f, 0f, 1f, 0f, 0f, -2f) + 1f) < 0.0001f,
            "focus-outline: a hit behind the enemy gives dot -1");
        check(float.IsNaN(FocusOutlinePolicy.DirectionDot(0f, 0f, 1f, 0f, 0f, 0f)),
            "focus-outline: a zero-length direction yields NaN instead of a guess");

        check(FocusOutlinePolicy.Thickness(0f) == FocusOutlinePolicy.MinThicknessPixels, "focus-outline: a flat bar keeps the minimum thickness");
        check(FocusOutlinePolicy.Thickness(1000f) == FocusOutlinePolicy.MaxThicknessPixels, "focus-outline: a huge bar is capped");
        check(Math.Abs(FocusOutlinePolicy.Thickness(8f) - 2.4f) < 0.0001f, "focus-outline: thickness scales with the bar height");
        check(FocusOutlinePolicy.Thickness(float.NaN) == FocusOutlinePolicy.MinThicknessPixels, "focus-outline: NaN bar height falls back to the minimum");

        FocusOutlinePolicy.OutlineColor(FocusOutlineKind.Back, 0f, out float br, out float bg, out float bb);
        check(Math.Abs(br - 1f) < 0.001f && Math.Abs(bg - 0.541f) < 0.001f && Math.Abs(bb - 0.118f) < 0.001f,
            "focus-outline: the back outline is the documented orange");
        FocusOutlinePolicy.OutlineColor(FocusOutlineKind.Weakspot, 0f, out float wr, out float wg, out float wb);
        check(Math.Abs(wr - 0.898f) < 0.001f && Math.Abs(wg - 0.282f) < 0.001f && Math.Abs(wb - 0.302f) < 0.001f,
            "focus-outline: the weakspot outline is the shared weakspot red");
        FocusOutlinePolicy.OutlineColor(FocusOutlineKind.None, 0f, out float nr, out float ng, out float nb);
        check(nr == 0f && ng == 0f && nb == 0f, "focus-outline: no outline has no colour");
        FocusOutlinePolicy.OutlineColor(FocusOutlineKind.Prismatic, 0f, out float p0r, out float p0g, out float p0b);
        FocusOutlinePolicy.OutlineColor(FocusOutlineKind.Prismatic, 1f / 3f, out float p1r, out float p1g, out float p1b);
        FocusOutlinePolicy.OutlineColor(FocusOutlineKind.Prismatic, 2f / 3f, out float p2r, out float p2g, out float p2b);
        check(Math.Abs(p0r - p1r) + Math.Abs(p0g - p1g) + Math.Abs(p0b - p1b) > 0.5f
            && Math.Abs(p1r - p2r) + Math.Abs(p1g - p2g) + Math.Abs(p1b - p2b) > 0.5f
            && Math.Abs(p0r - p2r) + Math.Abs(p0g - p2g) + Math.Abs(p0b - p2b) > 0.5f,
            "focus-outline: the prismatic outline differs at three phase samples");
        FocusOutlinePolicy.FoilFrameColor(0, 20, 0.1f, out float f0r, out float f0g, out float f0b);
        FocusOutlinePolicy.FoilFrameColor(10, 20, 0.1f, out float f1r, out float f1g, out float f1b);
        check(Math.Abs(f0r - f1r) + Math.Abs(f0g - f1g) + Math.Abs(f0b - f1b) > 0.3f,
            "focus-outline: the foil frame hue advances around the perimeter");
        check(f0r >= 0f && f0r <= 1f && f0g >= 0f && f0g <= 1f && f0b >= 0f && f0b <= 1f && f1r <= 1f,
            "focus-outline: frame segment colours stay inside the displayable range");
        check(Math.Abs(FocusOutlinePolicy.FoilThickness(5f) - 2.75f) < 0.001f
            && FocusOutlinePolicy.FoilThickness(0f) == FocusOutlinePolicy.FoilMinThicknessPixels
            && FocusOutlinePolicy.FoilThickness(100f) == FocusOutlinePolicy.FoilMaxThicknessPixels,
            "focus-outline: the prismatic frame is thicker than the flat one and stays clamped");
        check(FocusOutlinePolicy.FoilThickness(5f) > FocusOutlinePolicy.Thickness(5f),
            "focus-outline: a 5 px bar shows a 2.75 px foil rim but only a 1.5 px flat rim");

    }
}
