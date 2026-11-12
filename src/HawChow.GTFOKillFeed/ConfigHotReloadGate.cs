using System;

namespace HawChow.GTFOKillFeed;

internal sealed class ConfigHotReloadGate
{
	internal const float StableSeconds = 0.4f;

	private System.DateTime _appliedWriteUtc = System.DateTime.MinValue;

	private long _appliedLength = -1L;

	private System.DateTime _candidateWriteUtc;

	private long _candidateLength = -1L;

	private float _candidateSince;

	private float _retryAfter;

	private bool _hasCandidate;

	internal void Seed(System.DateTime writeUtc, long length)
	{
		_appliedWriteUtc = writeUtc;
		_appliedLength = length;
		_candidateWriteUtc = System.DateTime.MinValue;
		_candidateLength = -1L;
		_candidateSince = 0f;
		_retryAfter = 0f;
		_hasCandidate = false;
	}

	internal bool ShouldReload(System.DateTime writeUtc, long length, float now)
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
		if (now - _candidateSince >= 0.4f)
		{
			return now >= _retryAfter;
		}
		return false;
	}

	internal void MarkApplied(System.DateTime writeUtc, long length)
	{
		Seed(writeUtc, length);
	}

	internal void MarkFailed(float now)
	{
		_retryAfter = now + 1f;
	}
}
