namespace HawChow.GTFOKillFeed;

internal static class KillPresentationPolicy
{
	internal static KillFlight ClassifyFlight(bool isFlyer, bool armored)
	{
		if (isFlyer)
		{
			if (!armored)
			{
				return KillFlight.Flying;
			}
			return KillFlight.ArmoredFlying;
		}
		return KillFlight.None;
	}

	internal static KillPresentation Build(KillKind kind, float healthMax, in KillTierBoundaries tiers, bool isFlyer, bool armored)
	{
		return new KillPresentation(kind, KillPolicy.ClassifyTier(healthMax, in tiers), ClassifyFlight(isFlyer, armored));
	}

	internal static KillTier PreviewTier(bool weakspot)
	{
		if (!weakspot)
		{
			return KillTier.Minor;
		}
		return KillTier.Elite;
	}
}
