namespace HawChow.GTFOKillFeed;

internal sealed class DiagnosticBudget
{
	private int _used;

	internal int Used => _used;

	internal bool TryUse(int limit)
	{
		if (limit > 0)
		{
			return _used++ < limit;
		}
		return false;
	}
}
