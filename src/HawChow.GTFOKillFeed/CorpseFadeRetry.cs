using System;

namespace HawChow.GTFOKillFeed;

internal enum CorpseFadeAttempt { Finished, Started, Retry, AtDeadline }

// Two preparation opportunities; the third is reserved for the unchanged EndAt cleanup.
internal struct CorpseFadeRetry
{
    internal const int MaxAttempts = 3;
    internal const float LeadSeconds = 0.5f;
    internal const float IntervalSeconds = 0.25f;
    internal int Attempts;
    internal bool Resolved, Started;
    private bool _deadlineOnly;
    private float _nextAge;
    internal bool CanPrepare(float age, float endAt)
        => !Resolved && !_deadlineOnly && Attempts < MaxAttempts - 1 && KillPolicy.IsFinite(age) && KillPolicy.IsFinite(endAt)
            && age >= endAt - LeadSeconds && age < endAt && age >= _nextAge;
    internal void Observe(float age, CorpseFadeAttempt result)
    {
        Attempts++;
        _nextAge = age + IntervalSeconds;
        Started |= result == CorpseFadeAttempt.Started;
        _deadlineOnly |= result == CorpseFadeAttempt.AtDeadline;
        Resolved |= result == CorpseFadeAttempt.Started || result == CorpseFadeAttempt.Finished;
    }
    internal static bool IsTransient(CorpseFadeSkip reason)
        => reason == CorpseFadeSkip.ConcurrencyCap || reason == CorpseFadeSkip.NoFreeInstance;
    internal static bool IsTransient(CorpseFadeOutcome reason)
        => reason == CorpseFadeOutcome.ConcurrencyCap || reason == CorpseFadeOutcome.NoFreeInstance;
}
