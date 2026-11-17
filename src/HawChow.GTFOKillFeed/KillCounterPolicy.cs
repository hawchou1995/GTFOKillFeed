using System;
using System.Globalization;

namespace HawChow.GTFOKillFeed;

// Updated only after KillTracker's existing local lethal-credit ledger admits a kill.
internal sealed class KillCounterTotals
{
    private readonly object _gate = new();
    private readonly int[] _counts = new int[3];
    internal void Add(KillTier tier)
    {
        int i = (int)tier;
        if (i < 0 || i >= 3) return;
        lock (_gate) { if (_counts[i] < int.MaxValue) _counts[i]++; }
    }
    internal int Get(KillTier tier) { lock (_gate) { return (uint)(int)tier < 3 ? _counts[(int)tier] : 0; } }
    internal void Reset() { lock (_gate) { Array.Clear(_counts, 0, _counts.Length); } }
    internal static string Format(int value) => Math.Max(0, value).ToString(CultureInfo.InvariantCulture);
}

internal readonly struct KillCounterLayout
{
    internal readonly float X, Y, Scale, Width, Height;
    internal KillCounterLayout(float x, float y, float scale, float width, float height)
    { X = x; Y = y; Scale = scale; Width = width; Height = height; }
}

internal static class KillCounterPolicy
{
    internal const float Icon = 20f, Font = 14f, IconGap = 4f, GroupGap = 12f, BelowGap = 6f;
    internal static float GroupWidth(float textWidth) => Icon + IconGap + Math.Max(Font, textWidth);
    internal static bool TryLayout(float right, float bottom, float desiredScale, float contentWidth,
        float screenWidth, float screenHeight, out KillCounterLayout layout)
    {
        layout = default;
        if (!Finite(right) || !Finite(bottom) || !Finite(desiredScale) || !Finite(contentWidth)
            || !Finite(screenWidth) || !Finite(screenHeight) || screenWidth < 32 || screenHeight < 32
            || right < 24 || right > screenWidth - 2 || bottom < 0 || bottom >= screenHeight || contentWidth <= 0) return false;
        float scale = Math.Min(Math.Max(desiredScale, 0.7f), 2f);
        scale = Math.Min(scale, (right - 8f) / contentWidth);
        float y = bottom + BelowGap * scale;
        if (scale < 0.35f || y + Icon * scale > screenHeight - 4f) return false;
        layout = new KillCounterLayout(right - contentWidth * scale, y, scale, contentWidth * scale, Icon * scale);
        return true;
    }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
