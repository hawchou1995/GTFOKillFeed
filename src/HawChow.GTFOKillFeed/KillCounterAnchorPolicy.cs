using System;

namespace HawChow.GTFOKillFeed;

// A manual CellUI camera can be disabled: the owning canvas calls Render on demand.
// Texture pixels may be used as window pixels only after proving the whole compositor chain.
internal static class KillCounterAnchorPolicy
{
    internal static bool DrawsLayer(int mask, int layer) => (uint)layer < 32 && (mask & (1 << layer)) != 0;
    internal static string ManualRefusal(bool inside, int mask, int layer, int targetWidth, int targetHeight,
        float canvasWidth, float canvasHeight, float x, float y, float width, float height, int screenWidth, int screenHeight)
    {
        if (!inside) return "manual-canvas-wrong-root";
        if (!DrawsLayer(mask, layer)) return "manual-canvas-wrong-layer";
        if (screenWidth <= 0 || screenHeight <= 0 || targetWidth != screenWidth || targetHeight != screenHeight)
            return "manual-canvas-target-size-mismatch";
        if (!About(canvasWidth, screenWidth) || !About(canvasHeight, screenHeight)) return "manual-canvas-layout-size-mismatch";
        if (!About(x, 0f, 0.0001f) || !About(y, 0f, 0.0001f) || !About(width, 1f, 0.0001f) || !About(height, 1f, 0.0001f)) return "manual-canvas-partial-viewport";
        return null;
    }
    private static bool About(float a, float b, float tolerance = 0.01f) => KillPolicy.IsFinite(a) && Math.Abs(a - b) <= tolerance;
}
