using System.Collections.Generic;

namespace HawChow.GTFOKillFeed;

internal sealed class CorpseDeathLedger
{
	private readonly HashSet<long> _scheduled = new HashSet<long>();

	private bool _inLevel;

	internal int Count => _scheduled.Count;

	internal bool InLevel => _inLevel;

	internal void EnterLevel()
	{
		_inLevel = true;
	}

	internal bool TrySchedule(long identity)
	{
		if (_inLevel && identity != 0L)
		{
			return _scheduled.Add(identity);
		}
		return false;
	}

	internal void Forget(long identity)
	{
		if (identity != 0L)
		{
			_scheduled.Remove(identity);
		}
	}

	internal void Reset()
	{
		_scheduled.Clear();
		_inLevel = false;
	}
}
