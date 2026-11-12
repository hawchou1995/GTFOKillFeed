using System;

namespace HawChow.GTFOKillFeed;

internal sealed class AssetReloadState
{
	internal const int MaxFailedAttempts = 8;

	internal const float FailureRetryCeiling = 5f;

	internal string Path;

	internal System.DateTime AppliedWriteUtc;

	internal long AppliedLength = -1L;

	internal bool HasApplied;

	internal int Failures;

	internal float NextTryAt;

	internal float LastAttemptAt = -1f / 0f;

	internal bool IsRetryExhausted => Failures >= 8;

	internal static float RetryDelay(int failures)
	{
		int num = Math.Max(0, Math.Min(8, failures));
		return Math.Min(5f, 0.25f * (float)(1 << Math.Min(5, num)));
	}

	internal AssetReloadAction Evaluate(string path, System.DateTime writeUtc, long length, float now)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return AssetReloadAction.Wait;
		}
		if (!string.Equals(path, Path, (StringComparison)4))
		{
			Reset();
			Path = path;
			return AssetReloadAction.Reload;
		}
		if (!HasApplied)
		{
			if (!RetryAllowed(now))
			{
				return AssetReloadAction.Wait;
			}
			return AssetReloadAction.Reload;
		}
		if (writeUtc == AppliedWriteUtc && length == AppliedLength)
		{
			return AssetReloadAction.Wait;
		}
		if (!RetryAllowed(now))
		{
			return AssetReloadAction.Wait;
		}
		return AssetReloadAction.Reload;
	}

	private bool RetryAllowed(float now)
	{
		if (KillPolicy.IsFinite(now))
		{
			return now >= NextTryAt;
		}
		return false;
	}

	internal void MarkAttempt(float now)
	{
		LastAttemptAt = (KillPolicy.IsFinite(now) ? now : 0f);
	}

	internal void MarkLoaded(string path, System.DateTime writeUtc, long length, float now)
	{
		Path = path;
		AppliedWriteUtc = writeUtc;
		AppliedLength = length;
		HasApplied = true;
		Failures = 0;
		NextTryAt = (KillPolicy.IsFinite(now) ? now : 0f);
	}

	internal void MarkFailed(float now)
	{
		Failures++;
		float num = (KillPolicy.IsFinite(now) ? now : 0f);
		NextTryAt = num + RetryDelay(Failures);
	}

	internal void Reset()
	{
		Path = null;
		AppliedWriteUtc = System.DateTime.MinValue;
		AppliedLength = -1L;
		HasApplied = false;
		Failures = 0;
		NextTryAt = 0f;
		LastAttemptAt = -1f / 0f;
	}
}
