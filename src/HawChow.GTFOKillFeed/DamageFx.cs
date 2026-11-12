using System;
using UnityEngine;

namespace HawChow.GTFOKillFeed;

internal static class DamageFx
{
	private static long _cumulative;

	internal static long Cumulative => _cumulative;

	internal static void NoteDamage(float damage)
	{
		_cumulative = DamageFxPolicy.Accumulate(_cumulative, damage);
	}

	internal static void Reset(string reason)
	{
		_cumulative = 0L;
	}

	internal static bool TiersEnabled()
	{
		if (Plugin.DamageFxTiersEnabled != null)
		{
			return Plugin.DamageFxTiersEnabled.Value;
		}
		return true;
	}

	internal static bool PrismaticEnabled()
	{
		if (Plugin.DamageFxPrismaticEnabled != null)
		{
			return Plugin.DamageFxPrismaticEnabled.Value;
		}
		return true;
	}

	internal static float PrismaticThreshold()
	{
		return DamageFxPolicy.PrismaticThreshold((Plugin.DamageFxPrismaticThreshold != null) ? Plugin.DamageFxPrismaticThreshold.Value : 60f);
	}

	internal static bool IsPrismatic(float damage)
	{
		return DamageFxPolicy.IsPrismatic(damage, PrismaticThreshold(), PrismaticEnabled());
	}

	internal static DamageFxPolicy.TierThresholds Thresholds()
	{
		return DamageFxPolicy.ResolveTiers((Plugin.DamageFxWhiteThreshold != null) ? Plugin.DamageFxWhiteThreshold.Value : 3000, (Plugin.DamageFxSilverThreshold != null) ? Plugin.DamageFxSilverThreshold.Value : 5000, (Plugin.DamageFxGoldThreshold != null) ? Plugin.DamageFxGoldThreshold.Value : 10000, (Plugin.DamageFxDiamondThreshold != null) ? Plugin.DamageFxDiamondThreshold.Value : 20000);
	}

	internal static DamageTier Tier()
	{
		long cumulative = _cumulative;
		bool tiersEnabled = TiersEnabled();
		DamageFxPolicy.TierThresholds tiers = Thresholds();
		return DamageFxPolicy.TierFor(cumulative, tiersEnabled, in tiers);
	}

	internal static float Phase()
	{
		float unscaledTime;
		try
		{
			unscaledTime = Time.unscaledTime;
		}
		catch (System.Exception)
		{
			return 0f;
		}
		return DamageFxPolicy.PhaseFor(unscaledTime);
	}

	internal static Color PrismaticColor(float phase)
	{
		DamageFxPolicy.PrismaticColor(phase, out var red, out var green, out var blue);
		return new Color(red, green, blue);
	}

	internal static Color TierColor(DamageTier tier)
	{
		DamageFxPolicy.TierColor(tier, out var red, out var green, out var blue);
		return new Color(red, green, blue);
	}
}
