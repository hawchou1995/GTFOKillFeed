namespace HawChow.GTFOKillFeed;

internal sealed class ShotgunDamageNumberScopeStack
{
	private ShotgunDamageNumberScope _current;

	internal ShotgunDamageNumberScope Current => _current;

	internal ShotgunDamageNumberScope Begin(bool showNumbers, bool mergePellets, int fireFrame, uint gearCategoryId)
	{
		return _current = new ShotgunDamageNumberScope(_current, showNumbers, mergePellets, fireFrame, gearCategoryId);
	}

	internal bool TryEnd(ShotgunDamageNumberScope scope)
	{
		if (scope == null || _current != scope)
		{
			return false;
		}
		_current = scope.Previous;
		return true;
	}
}
