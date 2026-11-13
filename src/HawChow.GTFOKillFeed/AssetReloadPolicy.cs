using System;
using System.Collections.Generic;

namespace HawChow.GTFOKillFeed;

internal enum AssetReloadAction
{
    Wait,
    Reload
}

// Stamp/backoff bookkeeping for a hot-reloadable file. The Unity half owns the Texture2D; this
// half only decides when reading the file again is worth a try, and remembers how many attempts
// already failed so a locked or half-written PNG cannot be read 60 times per second.
internal sealed class AssetReloadState
{
    internal const int MaxFailedAttempts = 8;
    internal const float FailureRetryCeiling = 5f;

    internal string Path;
    internal DateTime AppliedWriteUtc;
    internal long AppliedLength = -1;
    internal bool HasApplied;

    internal int Failures;
    internal float NextTryAt;
    internal float LastAttemptAt = float.NegativeInfinity;

    internal static float RetryDelay(int failures)
    {
        int steps = Math.Max(0, Math.Min(8, failures));
        return Math.Min(FailureRetryCeiling, 0.25f * (1 << Math.Min(5, steps)));
    }

    internal AssetReloadAction Evaluate(string path, DateTime writeUtc, long length, float now)
    {
        if (string.IsNullOrWhiteSpace(path)) return AssetReloadAction.Wait;
        if (!string.Equals(path, Path, StringComparison.Ordinal))
        {
            // A different path is always a fresh candidate: the user pointed the marker at another file.
            Reset();
            Path = path;
            return AssetReloadAction.Reload;
        }
        if (!HasApplied) return RetryAllowed(now) ? AssetReloadAction.Reload : AssetReloadAction.Wait;
        if (writeUtc == AppliedWriteUtc && length == AppliedLength) return AssetReloadAction.Wait;
        return RetryAllowed(now) ? AssetReloadAction.Reload : AssetReloadAction.Wait;
    }

    private bool RetryAllowed(float now) => KillPolicy.IsFinite(now) && now >= NextTryAt;

    internal void MarkAttempt(float now) => LastAttemptAt = KillPolicy.IsFinite(now) ? now : 0f;

    internal void MarkLoaded(string path, DateTime writeUtc, long length, float now)
    {
        Path = path;
        AppliedWriteUtc = writeUtc;
        AppliedLength = length;
        HasApplied = true;
        Failures = 0;
        NextTryAt = KillPolicy.IsFinite(now) ? now : 0f;
    }

    internal void MarkFailed(float now)
    {
        Failures++;
        float clock = KillPolicy.IsFinite(now) ? now : 0f;
        NextTryAt = clock + RetryDelay(Failures);
    }

    internal bool IsRetryExhausted => Failures >= MaxFailedAttempts;

    internal void Reset()
    {
        Path = null;
        AppliedWriteUtc = DateTime.MinValue;
        AppliedLength = -1;
        HasApplied = false;
        Failures = 0;
        NextTryAt = 0f;
        LastAttemptAt = float.NegativeInfinity;
    }
}

// Hot-reloadable #RRGGBB / #RRGGBBAA style value. The consumer re-reads the ConfigEntry every
// frame; this cache only avoids re-parsing the same string, so an edited .cfg colour takes effect
// on the next frame and an invalid string falls back without flicker.
internal sealed class HexColorCache
{
    private string _source;
    private bool _has;
    private float _red, _green, _blue, _alpha;

    internal string Source => _source;
    internal float Red => _red;
    internal float Green => _green;
    internal float Blue => _blue;
    internal float Alpha => _alpha;
    internal bool IsFallback => !_parsed;
    private bool _parsed;

    internal void Resolve(string value, float fallbackRed, float fallbackGreen, float fallbackBlue, float fallbackAlpha)
    {
        if (_has && string.Equals(value, _source, StringComparison.Ordinal)) return;
        _has = true;
        _source = value;
        if (BarPolicy.TryParseHexColor(value, out float red, out float green, out float blue, out float alpha))
        {
            _red = red; _green = green; _blue = blue; _alpha = alpha;
            _parsed = true;
        }
        else
        {
            _red = fallbackRed; _green = fallbackGreen; _blue = fallbackBlue; _alpha = fallbackAlpha;
            _parsed = false;
        }
    }

    internal void Reset()
    {
        _source = null;
        _has = false;
        _parsed = false;
        _red = _green = _blue = _alpha = 1f;
    }
}

// Small helper shared by the marker icon and the damage-number colour so both can be asserted
// from the smoke project without an engine.
internal static class HotReloadPolicy
{
    internal const float FileProbeSeconds = 0.5f;

    internal static bool ShouldProbe(float now, float nextProbeAt)
        => KillPolicy.IsFinite(now) && now >= nextProbeAt;

    internal static float NextProbe(float now)
    {
        float clock = KillPolicy.IsFinite(now) ? now : 0f;
        return clock + FileProbeSeconds;
    }

    internal static string SafeFileName(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "<none>";
        try { return System.IO.Path.GetFileName(path); }
        catch { return "<invalid>"; }
    }
}
