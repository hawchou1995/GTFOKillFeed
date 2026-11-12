using System.Collections.Generic;

namespace HawChow.GTFOKillFeed;

internal sealed class KillLedger
{
	private readonly HashSet<long> _credited = new HashSet<long>();

	internal int Count => _credited.Count;

	internal bool TryCredit(long enemyIdentity)
	{
		if (enemyIdentity != 0L)
		{
			return _credited.Add(enemyIdentity);
		}
		return false;
	}

	internal void Reset()
	{
		_credited.Clear();
	}

	internal void Forget(long enemyIdentity)
	{
		_credited.Remove(enemyIdentity);
	}
}
