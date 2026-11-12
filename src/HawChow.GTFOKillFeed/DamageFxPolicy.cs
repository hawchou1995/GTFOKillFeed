using System;

namespace HawChow.GTFOKillFeed;

internal static class DamageFxPolicy
{
	internal readonly struct TierThresholds
	{
		internal readonly long White;

		internal readonly long Silver;

		internal readonly long Gold;

		internal readonly long Diamond;

		internal TierThresholds(long white, long silver, long gold, long diamond)
		{
			White = white;
			Silver = silver;
			Gold = gold;
			Diamond = diamond;
		}
	}

	internal readonly struct NumberPaintPlan
	{
		internal readonly bool Foil;

		internal readonly bool TierInk;

		internal readonly bool FoilRim;

		internal readonly int Rings;

		internal NumberPaintPlan(bool foil, bool tierInk, bool foilRim, int rings)
		{
			Foil = foil;
			TierInk = tierInk;
			FoilRim = foilRim;
			Rings = rings;
		}
	}

	internal const bool DefaultTiersEnabled = true;

	internal const bool DefaultPrismaticEnabled = true;

	internal const float DefaultPrismaticThreshold = 60f;

	internal const float MinPrismaticThreshold = 0f;

	internal const float MaxPrismaticThreshold = 1000000f;

	internal const long WhiteThreshold = 3000L;

	internal const long SilverThreshold = 5000L;

	internal const long GoldThreshold = 10000L;

	internal const long DiamondThreshold = 20000L;

	internal const long MinTierThreshold = 1L;

	internal const long MaxTierThreshold = 1000000000L;

	internal static readonly TierThresholds DefaultTiers = new TierThresholds(3000L, 5000L, 10000L, 20000L);

	internal const float WhiteR = 1f;

	internal const float WhiteG = 1f;

	internal const float WhiteB = 1f;

	internal const float SilverR = 0.784f;

	internal const float SilverG = 0.816f;

	internal const float SilverB = 0.847f;

	internal const float GoldR = 0.941f;

	internal const float GoldG = 0.761f;

	internal const float GoldB = 0.29f;

	internal const float DiamondR = 0.498f;

	internal const float DiamondG = 0.914f;

	internal const float DiamondB = 1f;

	internal const float PrismaticCycleSeconds = 1.8f;

	internal const float PrismaticSaturation = 0.85f;

	internal const float PrismaticValue = 1f;

	internal const float TierOutlineAlpha = 0.55f;

	internal const float FoilSaturation = 0.55f;

	internal const float FoilSheenCycles = 2f;

	internal const float FoilRimAlpha = 0.95f;

	internal const float FoilRimHaloAlpha = 0.42f;

	internal const float FoilRimHaloRadius = 3.1f;

	internal const float FoilEmbossAlpha = 0.45f;

	internal const float FoilEmbossRadius = 1.6f;

	internal const float FoilCoreBlend = 0.62f;

	internal static long ClampTierThreshold(long configured, long fallback)
	{
		if (configured <= 0)
		{
			if (fallback >= 1)
			{
				return fallback;
			}
			return 1L;
		}
		if (configured < 1)
		{
			return 1L;
		}
		if (configured > 1000000000)
		{
			return 1000000000L;
		}
		return configured;
	}

	internal static TierThresholds ResolveTiers(long white, long silver, long gold, long diamond)
	{
		long num = ClampTierThreshold(white, 3000L);
		long num2 = Math.Max(ClampTierThreshold(silver, 5000L), num);
		long num3 = Math.Max(ClampTierThreshold(gold, 10000L), num2);
		long diamond2 = Math.Max(ClampTierThreshold(diamond, 20000L), num3);
		return new TierThresholds(num, num2, num3, diamond2);
	}

	internal static float PrismaticThreshold(float configured)
	{
		if (float.IsNaN(configured) || float.IsInfinity(configured))
		{
			return 60f;
		}
		if (configured < 0f)
		{
			return 0f;
		}
		if (configured > 1000000f)
		{
			return 1000000f;
		}
		return configured;
	}

	internal static bool IsPrismatic(float damage, float threshold)
	{
		if (float.IsNaN(damage) || float.IsInfinity(damage) || damage < 0f)
		{
			return false;
		}
		float num = PrismaticThreshold(threshold);
		if (num <= 0f)
		{
			return false;
		}
		return damage > num;
	}

	internal static bool IsPrismatic(float damage, float threshold, bool prismaticEnabled)
	{
		if (prismaticEnabled)
		{
			return IsPrismatic(damage, threshold);
		}
		return false;
	}

	internal static NumberPaintPlan Paint(bool prismatic, bool tiersEnabled, DamageTier tier)
	{
		bool flag = tiersEnabled && tier != DamageTier.None;
		return new NumberPaintPlan(prismatic, flag, prismatic && !flag, (!prismatic) ? 1 : 2);
	}

	internal static DamageTier TierFor(long cumulative, bool tiersEnabled)
	{
		return TierFor(cumulative, tiersEnabled, in DefaultTiers);
	}

	internal static DamageTier TierFor(long cumulative, bool tiersEnabled, in TierThresholds tiers)
	{
		if (!tiersEnabled || cumulative <= 0)
		{
			return DamageTier.None;
		}
		if (cumulative >= tiers.Diamond)
		{
			return DamageTier.Diamond;
		}
		if (cumulative >= tiers.Gold)
		{
			return DamageTier.Gold;
		}
		if (cumulative >= tiers.Silver)
		{
			return DamageTier.Silver;
		}
		if (cumulative >= tiers.White)
		{
			return DamageTier.White;
		}
		return DamageTier.None;
	}

	internal static long Accumulate(long cumulative, float damage)
	{
		if (cumulative < 0)
		{
			cumulative = 0L;
		}
		if (float.IsNaN(damage) || float.IsInfinity(damage) || damage <= 0f)
		{
			return cumulative;
		}
		long num = (long)damage;
		if (num <= 0)
		{
			return cumulative;
		}
		if (cumulative > 9223372036854775807L - num)
		{
			return 9223372036854775807L;
		}
		return cumulative + num;
	}

	internal static void TierColor(DamageTier tier, out float red, out float green, out float blue)
	{
		switch (tier)
		{
		case DamageTier.Diamond:
			red = 0.498f;
			green = 0.914f;
			blue = 1f;
			break;
		case DamageTier.Gold:
			red = 0.941f;
			green = 0.761f;
			blue = 0.29f;
			break;
		case DamageTier.Silver:
			red = 0.784f;
			green = 0.816f;
			blue = 0.847f;
			break;
		case DamageTier.White:
			red = 1f;
			green = 1f;
			blue = 1f;
			break;
		default:
			red = 0f;
			green = 0f;
			blue = 0f;
			break;
		}
	}

	internal static float WrapPhase(float phase)
	{
		if (float.IsNaN(phase) || float.IsInfinity(phase))
		{
			return 0f;
		}
		float num = phase - (float)Math.Floor((double)phase);
		if (num < 0f)
		{
			num += 1f;
		}
		if (!(num >= 1f))
		{
			return num;
		}
		return 0f;
	}

	internal static float PhaseFor(float unscaledSeconds)
	{
		if (float.IsNaN(unscaledSeconds) || float.IsInfinity(unscaledSeconds))
		{
			return 0f;
		}
		return WrapPhase(unscaledSeconds / 1.8f);
	}

	internal static void PrismaticColor(float phase, out float red, out float green, out float blue)
	{
		HueColor(WrapPhase(phase), 0.85f, 1f, out red, out green, out blue);
	}

	internal static void HueColor(float hue, float saturation, float value, out float red, out float green, out float blue)
	{
		float num = WrapPhase(hue) * 6f;
		int num2 = (int)num;
		if (num2 > 5)
		{
			num2 = 5;
		}
		float num3 = num - (float)num2;
		float num4 = value * (1f - saturation);
		float num5 = value * (1f - saturation * num3);
		float num6 = value * (1f - saturation * (1f - num3));
		switch (num2)
		{
		case 0:
			red = value;
			green = num6;
			blue = num4;
			break;
		case 1:
			red = num5;
			green = value;
			blue = num4;
			break;
		case 2:
			red = num4;
			green = value;
			blue = num6;
			break;
		case 3:
			red = num4;
			green = num5;
			blue = value;
			break;
		case 4:
			red = num6;
			green = num4;
			blue = value;
			break;
		default:
			red = value;
			green = num4;
			blue = num5;
			break;
		}
	}

	internal static void FoilDirection(int direction, out int dx, out int dy)
	{
		switch (direction & 7)
		{
		case 0:
			dx = -1;
			dy = -1;
			break;
		case 1:
			dx = 0;
			dy = -1;
			break;
		case 2:
			dx = 1;
			dy = -1;
			break;
		case 3:
			dx = 1;
			dy = 0;
			break;
		case 4:
			dx = 1;
			dy = 1;
			break;
		case 5:
			dx = 0;
			dy = 1;
			break;
		case 6:
			dx = -1;
			dy = 1;
			break;
		default:
			dx = -1;
			dy = 0;
			break;
		}
	}

	internal static void FoilRimColor(int direction, float phase, out float red, out float green, out float blue)
	{
		HueColor(WrapPhase(phase + (float)(direction & 7) / 8f), 0.55f, 1f, out red, out green, out blue);
	}

	internal static float FoilSheen(float phase)
	{
		return 0.5f + 0.5f * (float)Math.Sin(Math.PI * 4.0 * (double)WrapPhase(phase));
	}

	internal static void FoilBodyColor(float phase, out float red, out float green, out float blue)
	{
		HueColor(WrapPhase(phase), 0.33f, 1f, out var red2, out var green2, out var blue2);
		float num = FoilSheen(phase);
		float num2 = 0.62f + 0.3f * num;
		red = red2 + (1f - red2) * num2;
		green = green2 + (1f - green2) * num2;
		blue = blue2 + (1f - blue2) * num2;
	}

	internal static void FoilEmbossOffset(float phase, out float dx, out float dy)
	{
		double num = Math.PI * 2.0 * (double)WrapPhase(phase);
		dx = (float)Math.Sin(num) * 1.6f;
		dy = (float)(0.0 - Math.Cos(num)) * 1.6f;
	}

	internal static float OutlineStrength(DamageTier tier)
	{
		if (tier != 0)
		{
			return 0.55f;
		}
		return 0f;
	}
}
