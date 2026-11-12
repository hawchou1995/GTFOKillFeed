namespace HawChow.GTFOKillFeed;

internal readonly struct KillTierBoundaries
{
	internal readonly float Minor;

	internal readonly float Boss;

	internal static KillTierBoundaries Default => new KillTierBoundaries(120f, 1000f);

	internal bool EliteBandEmpty => Boss <= Minor;

	internal KillTierBoundaries(float minor, float boss)
	{
		Minor = minor;
		Boss = boss;
	}
}
