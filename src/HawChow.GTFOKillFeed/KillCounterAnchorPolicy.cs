using System;

namespace HawChow.GTFOKillFeed;

// A manual CellUI camera can be disabled: the owning canvas calls Render on demand.
// Texture pixels may be used as window pixels only after proving the whole compositor chain.
internal static class KillCounterAnchorPolicy
{
    internal const int MinimumPixels = 32;
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
    // Texture pixels are not window pixels, but a normalised viewport coordinate is: when the manual camera renders
    // into a real render texture that the canvas itself describes, and that texture has the window's aspect ratio,
    // the same bar edge can be projected with WorldToViewportPoint and scaled by the current window size. Everything
    // else stays refused - there is no window-corner destination to fall back on.
    internal static bool CanScale(bool inside, int mask, int layer, int targetWidth, int targetHeight,
        float canvasWidth, float canvasHeight, float x, float y, float width, float height, int screenWidth, int screenHeight)
    {
        if (targetWidth < MinimumPixels || targetHeight < MinimumPixels) return false;
        if (screenWidth < MinimumPixels || screenHeight < MinimumPixels) return false;
        // The canvas must describe the same internal resolution the camera renders into.
        if (!About(canvasWidth, targetWidth) || !About(canvasHeight, targetHeight)) return false;
        if (!SameAspect(targetWidth, targetHeight, screenWidth, screenHeight)) return false;
        // The same root/layer/full-viewport proof, asked in the texture's own space so a size mismatch cannot hide it.
        return ManualRefusal(inside, mask, layer, targetWidth, targetHeight, canvasWidth, canvasHeight,
            x, y, width, height, targetWidth, targetHeight) == null;
    }
    // Long cross product, so 1920x1080 against 2560x1440 cancels exactly while 1920x1080 against 2560x1080 cannot.
    private static bool SameAspect(int aWidth, int aHeight, int bWidth, int bHeight)
        => (long)aWidth * bHeight == (long)bWidth * aHeight;
    private static bool About(float a, float b, float tolerance = 0.01f) => KillPolicy.IsFinite(a) && Math.Abs(a - b) <= tolerance;
}
