using System;

namespace HawChow.GTFOKillFeed;

// Main-thread debounce for external BepInEx config writes. It observes file metadata only;
// ConfigFile.Reload is called by Plugin after the same timestamp and length stay stable.
internal sealed class ConfigHotReloadGate
{
    internal const float StableSeconds = 0.40f;
    private DateTime _appliedWriteUtc = DateTime.MinValue;
    private long _appliedLength = -1;
    private DateTime _candidateWriteUtc;
    private long _candidateLength = -1;
    private float _candidateSince;
    private float _retryAfter;
    private bool _hasCandidate;

    internal void Seed(DateTime writeUtc, long length)
    {
        _appliedWriteUtc = writeUtc;
        _appliedLength = length;
        _candidateWriteUtc = DateTime.MinValue;
        _candidateLength = -1;
        _candidateSince = 0f;
        _retryAfter = 0f;
        _hasCandidate = false;
    }

    internal bool ShouldReload(DateTime writeUtc, long length, float now)
    {
        if (writeUtc == _appliedWriteUtc && length == _appliedLength)
        {
            _hasCandidate = false;
            return false;
        }

        if (!_hasCandidate || writeUtc != _candidateWriteUtc || length != _candidateLength)
        {
            _candidateWriteUtc = writeUtc;
            _candidateLength = length;
            _candidateSince = now;
            _hasCandidate = true;
            return false;
        }

        return now - _candidateSince >= StableSeconds && now >= _retryAfter;
    }

    internal void MarkApplied(DateTime writeUtc, long length)
        => Seed(writeUtc, length);

    internal void MarkFailed(float now)
        => _retryAfter = now + 1f;
}
