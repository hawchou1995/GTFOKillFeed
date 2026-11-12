namespace HawChow.GTFOKillFeed;

internal sealed class ShotgunDamageTargetScopeStack
{
	private long _current;

	internal long Current => _current;

	internal long Enter(long target)
	{
		long current = _current;
		_current = target;
		return current;
	}

	internal void Exit(long previous)
	{
		_current = previous;
	}
}
