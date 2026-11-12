namespace HawChow.GTFOKillFeed;

internal sealed class ShotgunDamageNumberScope
{
	internal readonly ShotgunDamageNumberScope Previous;

	internal readonly bool ShowNumbers;

	internal readonly bool MergePellets;

	internal readonly ShotgunDamageNumberBatch Batch = new ShotgunDamageNumberBatch();

	internal readonly int FireFrame;

	internal uint GearCategoryId;

	internal ShotgunDamageNumberScope(ShotgunDamageNumberScope previous, bool showNumbers, bool mergePellets, int fireFrame, uint gearCategoryId)
	{
		Previous = previous;
		ShowNumbers = showNumbers;
		MergePellets = mergePellets;
		FireFrame = fireFrame;
		GearCategoryId = gearCategoryId;
	}
}
