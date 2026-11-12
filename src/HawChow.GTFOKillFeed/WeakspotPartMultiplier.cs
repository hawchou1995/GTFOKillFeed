namespace HawChow.GTFOKillFeed;

internal readonly struct WeakspotPartMultiplier
{
	internal readonly string Part;

	internal readonly float Multiplier;

	internal WeakspotPartMultiplier(string part, float multiplier)
	{
		Part = part;
		Multiplier = multiplier;
	}
}
