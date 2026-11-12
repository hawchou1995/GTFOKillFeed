using System;
using System.Globalization;

namespace HawChow.GTFOKillFeed;

internal static class KillPolicy
{
	internal const float MinTierHealthThreshold = 1f;

	internal const float MaxTierHealthThreshold = 1E+09f;

	internal const float DefaultHeavyHealthThreshold = 1000f;

	internal const float MinHeavyHealthThreshold = 1f;

	internal const float MaxHeavyHealthThreshold = 1E+09f;

	internal static KillDecision ClassifyLethalHit(bool localPlayerSource, float healthBefore, float healthAfter, bool limbObserved, bool limbIsWeakspot)
	{
		bool flag = CrossedLethalHealth(healthBefore, healthAfter);
		if (!localPlayerSource || !flag)
		{
			return new KillDecision(credited: false, KillKind.Normal);
		}
		return new KillDecision(credited: true, (limbObserved && limbIsWeakspot) ? KillKind.Weakspot : KillKind.Normal);
	}

	internal static bool CrossedLethalHealth(float healthBefore, float healthAfter)
	{
		if (IsFinite(healthBefore) && IsFinite(healthAfter) && healthBefore > 0f)
		{
			return healthAfter <= 0f;
		}
		return false;
	}

	internal static float TierHealthThreshold(float configured, float fallback)
	{
		if (!IsFinite(configured) || configured <= 0f)
		{
			return fallback;
		}
		if (configured < 1f)
		{
			return 1f;
		}
		if (configured > 1E+09f)
		{
			return 1E+09f;
		}
		return configured;
	}

	internal static KillTierBoundaries ResolveTierBoundaries(float minorThreshold, float bossThreshold)
	{
		float minor = TierHealthThreshold(minorThreshold, 120f);
		float boss = HeavyHealthThreshold(bossThreshold);
		return new KillTierBoundaries(minor, boss);
	}

	internal static KillTier ClassifyTier(float healthMax, in KillTierBoundaries boundaries)
	{
		if (!IsFinite(healthMax) || healthMax < 0f)
		{
			return KillTier.Minor;
		}
		if (healthMax >= boundaries.Boss)
		{
			return KillTier.Boss;
		}
		if (healthMax < boundaries.Minor)
		{
			return KillTier.Minor;
		}
		return KillTier.Elite;
	}

	internal static KillTier ClassifyTier(float healthMax, float minorThreshold = 120f, float eliteThreshold = 1000f)
	{
		KillTierBoundaries boundaries = ResolveTierBoundaries(minorThreshold, eliteThreshold);
		return ClassifyTier(healthMax, in boundaries);
	}

	internal static string FormatDistance(float meters, float maximum = 999f)
	{
		if (!IsFinite(meters) || meters < 0f)
		{
			return "—";
		}
		if (!IsFinite(maximum) || maximum < 1f)
		{
			maximum = 999f;
		}
		if (meters > maximum)
		{
			return ((int)Math.Floor((double)(maximum + 0.5f))).ToString((IFormatProvider)(object)CultureInfo.InvariantCulture) + "m+";
		}
		return ((int)Math.Floor((double)(meters + 0.5f))).ToString((IFormatProvider)(object)CultureInfo.InvariantCulture) + "m";
	}

	internal static string FormatHealth(float healthMax)
	{
		if (!IsFinite(healthMax) || healthMax < 0f)
		{
			return "— HP";
		}
		return healthMax.ToString("0.#", (IFormatProvider)(object)CultureInfo.InvariantCulture) + " HP";
	}

	internal static float HeavyHealthThreshold(float configured)
	{
		if (float.IsNaN(configured) || float.IsInfinity(configured))
		{
			return 1000f;
		}
		if (configured < 1f)
		{
			return 1f;
		}
		if (configured > 1E+09f)
		{
			return 1E+09f;
		}
		return configured;
	}

	internal static bool IsHeavy(float healthMax, float threshold)
	{
		if (IsFinite(healthMax) && healthMax > 0f)
		{
			return healthMax >= HeavyHealthThreshold(threshold);
		}
		return false;
	}

	internal static string IconKey(KillKind kind, bool heavy)
	{
		if (!heavy)
		{
			if (kind != KillKind.Weakspot)
			{
				return "kill-normal.png";
			}
			return "kill-weakspot.png";
		}
		return "kill-heavy.png";
	}

	internal static string IconKey(KillKind kind)
	{
		return IconKey(kind, heavy: false);
	}

	internal static bool IsChinese(string language)
	{
		if (!string.Equals(language, "zh", (StringComparison)5) && !string.Equals(language, "zh-CN", (StringComparison)5))
		{
			return string.Equals(language, "cn", (StringComparison)5);
		}
		return true;
	}

	internal static string TierLabel(KillTier tier, string language)
	{
		if (!IsChinese(language))
		{
			return tier switch
			{
				KillTier.Elite => "ELITE", 
				KillTier.Minor => "MINOR", 
				_ => "BOSS", 
			};
		}
		return tier switch
		{
			KillTier.Elite => "精英", 
			KillTier.Minor => "普通", 
			_ => "BOSS", 
		};
	}

	internal static bool IsFinite(float value)
	{
		if (!float.IsNaN(value))
		{
			return !float.IsInfinity(value);
		}
		return false;
	}
}
