namespace HawChow.GTFOKillFeed;

internal sealed class CorpseSoundThrottle
{
	private float _nextAllowedAt;

	private bool _pending;

	internal bool HasPending => _pending;

	internal bool Request(float now)
	{
		if (!KillPolicy.IsFinite(now))
		{
			return false;
		}
		if (_pending || now < _nextAllowedAt)
		{
			_pending = true;
			return false;
		}
		_pending = false;
		_nextAllowedAt = now + 0.12f;
		return true;
	}

	internal bool TryFlush(float now)
	{
		if (!_pending || !KillPolicy.IsFinite(now) || now < _nextAllowedAt)
		{
			return false;
		}
		_pending = false;
		_nextAllowedAt = now + 0.12f;
		return true;
	}

	internal void Reset()
	{
		_nextAllowedAt = 0f;
		_pending = false;
	}
}
