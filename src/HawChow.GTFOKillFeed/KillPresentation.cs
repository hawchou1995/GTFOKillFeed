namespace HawChow.GTFOKillFeed;

internal readonly struct KillPresentation
{
	internal readonly KillKind Kind;

	internal readonly KillTier Tier;

	internal readonly KillFlight Flight;

	internal KillPresentation(KillKind kind, KillTier tier, KillFlight flight)
	{
		Kind = kind;
		Tier = tier;
		Flight = flight;
	}
}
