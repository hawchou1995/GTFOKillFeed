using System;
using System.Collections.Generic;

namespace HawChow.GTFOKillFeed;

internal enum TeammateMarkIcon
{
    Cai,
    Ruo,
    Si,
    Sha
}

// Pure policy for the downed-teammate head marker. No UnityEngine or IL2CPP types so the smoke
// project can link this file directly; the Unity half lives in TeammateMarks.cs.
internal static class TeammateMarkPolicy
{
    internal const string CaiFileName = "teammate-downed-cai.png";
    internal const string RuoFileName = "teammate-downed-ruo.png";
    internal const string SiFileName = "teammate-downed-si.png";
    internal const string ShaFileName = "teammate-downed-sha.png";
    internal const string CaiGlyph = "菜";
    internal const string RuoGlyph = "鶸";
    internal const string SiGlyph = "死";
    internal const string ShaGlyph = "殺"; // Yuji Syuku carries U+6BBA; simplified 杀 is absent there
    internal const string ShaSimpGlyph = "杀"; // accepted as input, never used as a fallback glyph

    // Distance curve: the icon is RefPixels tall at RefDistance metres on a 1080p screen and
    // follows RefDistance / distance inside [MinScale, MaxScale]. MinPixels is the measured
    // readability floor for the dense "鶸" glyph: the 128 px source keeps 5 enclosed counters at
    // 32 px, 1 at 24 px and none at 20 px, so the 1080p floor is 26 px and scales with resolution.
    // MaxPixels keeps the mark from covering the teammate's own head.
    internal const float DefaultRefDistance = 8f;
    internal const float DefaultRefPixels = 30f;
    internal const float DefaultMinPixels = 26f;
    internal const float DefaultMaxPixels = 42f;
    internal const float DefaultMaxDistance = 60f;
    internal const float DefaultHeadClearance = 0.05f;
    internal const float MinScale = 0.55f;
    internal const float MaxScale = 1.35f;
    internal const float MinRefDistance = 1f;
    internal const float MaxRefDistance = 30f;
    internal const float MinPixelFloor = 13f;
    internal const float MaxPixelCeiling = 96f;
    internal const float ScreenHeightFractionCap = 0.05f;

    // EMP reflection chain: polled at most every 0.2 s while bound, re-resolved with a bounded
    // backoff when the optional EOSExt_EMP assembly/handler is missing or throws.
    internal const float EmpPollSeconds = 0.2f;
    internal const float EmpMaxRetrySeconds = 10f;

    internal const float ForgetSeconds = 0.5f;
    internal const float EmptyRosterSeconds = 0.75f;

    internal static TeammateMarkIcon ParseIcon(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return TeammateMarkIcon.Cai;
        string text = value.Trim();
        if (text == CaiGlyph) return TeammateMarkIcon.Cai;
        if (text == RuoGlyph) return TeammateMarkIcon.Ruo;
        if (text == SiGlyph) return TeammateMarkIcon.Si;
        if (text == ShaGlyph || text == ShaSimpGlyph) return TeammateMarkIcon.Sha;
        if (string.Equals(text, "cai", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "1", StringComparison.Ordinal)) return TeammateMarkIcon.Cai;
        if (string.Equals(text, "ruo", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "weak", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "2", StringComparison.Ordinal)) return TeammateMarkIcon.Ruo;
        if (string.Equals(text, "si", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "death", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "3", StringComparison.Ordinal)) return TeammateMarkIcon.Si;
        if (string.Equals(text, "sha", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "sha1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "kill", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "4", StringComparison.Ordinal)) return TeammateMarkIcon.Sha;
        return TeammateMarkIcon.Cai;
    }

    internal static string IconFileName(TeammateMarkIcon icon)
    {
        switch (icon)
        {
            case TeammateMarkIcon.Ruo: return RuoFileName;
            case TeammateMarkIcon.Si: return SiFileName;
            case TeammateMarkIcon.Sha: return ShaFileName;
            default: return CaiFileName;
        }
    }

    internal static string IconGlyph(TeammateMarkIcon icon)
    {
        switch (icon)
        {
            case TeammateMarkIcon.Ruo: return RuoGlyph;
            case TeammateMarkIcon.Si: return SiGlyph;
            case TeammateMarkIcon.Sha: return ShaGlyph;
            default: return CaiGlyph;
        }
    }

    // the bundled PNG that ships next to the DLL.
    // Returns the path the optional custom slot should probe. Dual-track: a rooted local path is
    // used as-is (absolute track); any other value is resolved against the plugin's Assets/icons
    // folder (relative track) so bundled names like teammate-downed-sha.png also work as marks.
    // UNC shares are refused before anything else, so a bad value never reaches the IO layer.
    internal static string CustomProbePath(string customPath, string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(customPath)) return null;
        if (IsNetworkPath(customPath)) return null;
        string trimmed = customPath.Trim();
        if (IsRootedPath(trimmed)) return trimmed;
        if (string.IsNullOrWhiteSpace(baseDirectory)) return null;
        try { return System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDirectory, trimmed)); }
        catch { return null; }
    }

    internal static bool IsRootedPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try { return System.IO.Path.IsPathRooted(path.Trim()); }
        catch { return false; }
    }

    // UNC shares can block for seconds on a dropped link, so they are refused explicitly instead of
    // being probed. Local absolute paths (including other drives) stay supported.
    internal static bool IsNetworkPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        string trimmed = path.Trim();
        return trimmed.StartsWith(@"\\", StringComparison.Ordinal)
            || trimmed.StartsWith("//", StringComparison.Ordinal);
    }

    // the bundled PNG that ships next to the DLL.
    // Returns the path the optional custom slot should probe: only a rooted, non-network path.
    // Relative paths and UNC shares are refused here, so a bad value can never reach the IO layer.
    internal static string CustomProbePath(string customPath)
    {
        if (!IsRootedPath(customPath) || IsNetworkPath(customPath)) return null;
        return customPath.Trim();
    }

    internal static float DistanceScale(float distance, float refDistance)
    {
        float reference = FeedbackPolicy.Clamp(refDistance, MinRefDistance, MaxRefDistance, DefaultRefDistance);
        float metres = KillPolicy.IsFinite(distance) && distance > 0f ? Math.Max(distance, 0.5f) : reference;
        return FeedbackPolicy.Clamp(reference / metres, MinScale, MaxScale, 1f);
    }

    // Resolution aware icon edge in physical pixels. Everything is configured in 1080p pixels,
    // multiplied by the same 0.75-1.6 resolution scale the health bar uses, then clamped to the
    // configured floor/ceiling and finally capped at 5% of the screen height.
    internal static int FontPixels(float referencePixels, float distance, float refDistance, int screenHeight,
        float minPixels, float maxPixels)
    {
        float resolution = BarPolicy.ResolutionScale(screenHeight);
        float basePixels = FeedbackPolicy.Clamp(referencePixels, 8f, MaxPixelCeiling, DefaultRefPixels);
        float pixels = basePixels * DistanceScale(distance, refDistance) * resolution;
        float floor = FeedbackPolicy.Clamp(minPixels, MinPixelFloor, MaxPixelCeiling, DefaultMinPixels) * resolution;
        float ceiling = FeedbackPolicy.Clamp(maxPixels, MinPixelFloor, MaxPixelCeiling, DefaultMaxPixels) * resolution;
        if (ceiling < floor) ceiling = floor;
        if (pixels < floor) pixels = floor;
        if (pixels > ceiling) pixels = ceiling;
        if (screenHeight > 0)
        {
            float cap = screenHeight * ScreenHeightFractionCap;
            if (pixels > cap) pixels = cap;
        }
        int rounded = (int)Math.Round(pixels, MidpointRounding.AwayFromZero);
        return Math.Max(8, rounded);
    }

    internal static bool OnScreen(float x, float y, int screenWidth, int screenHeight, float margin)
    {
        if (screenWidth <= 0 || screenHeight <= 0) return false;
        if (!KillPolicy.IsFinite(x) || !KillPolicy.IsFinite(y)) return false;
        float edge = FeedbackPolicy.Clamp(margin, 0f, Math.Min(screenWidth, screenHeight) * 0.5f, 0f);
        return x >= edge && x <= screenWidth - edge && y >= edge && y <= screenHeight - edge;
    }

    internal static bool ShouldDrawMark(bool featureEnabled, bool inLevel, bool hudVisible, bool isLocalPlayer,
        bool downed, bool empSuppressed, bool hasScreenPoint, float distance, float maxDistance)
    {
        if (!featureEnabled || !inLevel || !hudVisible) return false;
        if (isLocalPlayer || empSuppressed) return false;
        if (!downed) return false;
        if (!hasScreenPoint) return false;
        if (!KillPolicy.IsFinite(distance) || distance < 0f) return false;
        return distance <= FeedbackPolicy.Clamp(maxDistance, 1f, 500f, DefaultMaxDistance);
    }

    // EMP is opt-out: the default (HideDuringEMP = false) never probes the optional assembly.
    internal static bool ShouldProbeEmp(bool hideDuringEmp) => hideDuringEmp;

    internal static bool ShouldHideForEmp(bool hideDuringEmp, bool handlerAvailable, bool emped)
        => hideDuringEmp && handlerAvailable && emped;

    internal static bool IsProbeDue(float now, float nextProbeAt)
        => KillPolicy.IsFinite(now) && now >= nextProbeAt;

    internal static float NextProbeAt(float now, bool bound)
    {
        float clock = KillPolicy.IsFinite(now) ? now : 0f;
        return clock + (bound ? EmpPollSeconds : EmpMaxRetrySeconds);
    }

    // 0.25 s, 0.5 s, 1 s, 2 s, 4 s, 8 s, then a 10 s ceiling: bounded retries that never spin.
    internal static float RetryDelay(int failures)
    {
        int steps = Math.Max(0, Math.Min(8, failures));
        float delay = 0.25f * (1 << Math.Min(6, steps));
        return Math.Min(EmpMaxRetrySeconds, delay);
    }
}

// Per-frame roster reconciliation, kept free of Unity types so the revive/leave/level-change
// matrix is unit testable. The Unity half stores agent and anchor references under the same key.
internal sealed class TeammateMarkLedger
{
    internal sealed class Entry
    {
        internal long Key;
        internal bool Downed;
        internal bool Reviving;
        internal float SeenAt;
    }

    private readonly Dictionary<long, Entry> _entries = new();

    internal int Count => _entries.Count;
    internal Dictionary<long, Entry>.ValueCollection Values => _entries.Values;

    internal Entry GetOrAdd(long key, float now)
    {
        if (_entries.TryGetValue(key, out var entry)) return entry;
        entry = new Entry { Key = key, SeenAt = now };
        _entries[key] = entry;
        return entry;
    }

    internal void MarkSeen(long key, bool downed, bool reviving, float now)
    {
        var entry = GetOrAdd(key, now);
        entry.Downed = downed;
        entry.Reviving = reviving;
        entry.SeenAt = now;
    }

    // Entries that were not enumerated for ForgetSeconds are gone: the player left, died for good
    // or an unrelated agent took the slot. Revived players stay (they are still in the level), they
    // simply stop drawing.
    internal int ForgetUnseen(float now, List<long> removed)
    {
        removed?.Clear();
        int count = 0;
        List<long> dead = null;
        foreach (var pair in _entries)
        {
            if (now - pair.Value.SeenAt <= TeammateMarkPolicy.ForgetSeconds) continue;
            dead ??= new List<long>(2);
            dead.Add(pair.Key);
        }
        if (dead == null) return 0;
        foreach (long key in dead)
        {
            _entries.Remove(key);
            removed?.Add(key);
            count++;
        }
        return count;
    }

    internal void Clear()
    {
        _entries.Clear();
    }
}
