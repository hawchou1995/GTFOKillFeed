using System;

namespace HawChow.GTFOKillFeed;

internal static class KillIconPolicy
{
	internal const string DisplayHead = "Head";

	internal const string DisplayCard = "Card";

	internal const KillIconDisplay DefaultDisplay = KillIconDisplay.Head;

	internal const float DefaultLifetimeSeconds = 2.2f;

	internal const float MinLifetimeSeconds = 0.4f;

	internal const float MaxLifetimeSeconds = 6f;

	internal const int DefaultMaxIcons = 6;

	internal const int MaxIconsCeiling = 12;

	internal const float DefaultGapMetres = 0.3f;

	internal const float MinGapMetres = 0f;

	internal const float MaxGapMetres = 2f;

	internal const float MinIconSize = 26f;

	internal const float MaxIconSize = 76f;

	internal const string AnchorSourceHead = "head-world";

	internal const string AnchorSourceLethal = "lethal-position";

	internal const float FadeInSeconds = 0.1f;

	internal const float FadeOutSeconds = 0.35f;

	internal const string DefaultWingsColor = "#8FD3E8";

	internal const string DefaultArmorPlateColor = "#E6AB57";

	internal const int PreviewCombos = 9;

	internal static KillIconDisplay ParseDisplay(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return KillIconDisplay.Head;
		}
		string text = value.Trim();
		if (string.Equals(text, "Card", (StringComparison)5))
		{
			return KillIconDisplay.Card;
		}
		if (string.Equals(text, "Cards", (StringComparison)5))
		{
			return KillIconDisplay.Card;
		}
		return KillIconDisplay.Head;
	}

	internal static string DisplayToken(KillIconDisplay display)
	{
		if (display != KillIconDisplay.Card)
		{
			return "Head";
		}
		return "Card";
	}

	internal static bool UsesHeadIcons(KillIconDisplay display)
	{
		return display == KillIconDisplay.Head;
	}

	internal static float Lifetime(float configured)
	{
		return FeedbackPolicy.Clamp(configured, 0.4f, 6f, 2.2f);
	}

	internal static int MaxIcons(int configured)
	{
		if (configured <= 0)
		{
			return 6;
		}
		if (configured <= 12)
		{
			return configured;
		}
		return 12;
	}

	internal static float Gap(float configured)
	{
		return FeedbackPolicy.Clamp(configured, 0f, 2f, 0.3f);
	}

	internal static float Size(float configured)
	{
		return FeedbackPolicy.Clamp(configured, 26f, 76f, 46f);
	}

	internal static float HeadAnchorOffset(bool barEnabled, bool barHeadMode, float barHeadOffsetMetres, float gapMetres)
	{
		float num = Gap(gapMetres);
		if (!barEnabled || !barHeadMode)
		{
			return num;
		}
		return FeedbackPolicy.Clamp(barHeadOffsetMetres, 0f, 2f, 0.12f) + num;
	}

	internal static string TierIconKey(KillTier tier)
	{
		return tier switch
		{
			KillTier.Boss => "kill-heavy", 
			KillTier.Elite => "kill-weakspot", 
			_ => "kill-normal", 
		};
	}

	internal static string TierIconFile(KillTier tier)
	{
		return TierIconKey(tier) + ".png";
	}

	internal static FlightMark MarkFor(KillFlight flight)
	{
		return flight switch
		{
			KillFlight.Flying => FlightMark.Wings, 
			KillFlight.ArmoredFlying => FlightMark.ArmorPlate, 
			_ => FlightMark.None, 
		};
	}

	internal static float PixelSize(float iconSize)
	{
		if (!KillPolicy.IsFinite(iconSize) || !(iconSize > 0f))
		{
			return 46f;
		}
		return Math.Min(iconSize, 152f);
	}

	internal static float BadgeSize(float iconSize)
	{
		return PixelSize(iconSize) * 0.3f;
	}

	internal static float BadgeGap(float iconSize)
	{
		return PixelSize(iconSize) * 0.1f;
	}

	internal static float Alpha(float age, float lifetime)
	{
		if (float.IsNaN(age) || float.IsInfinity(age))
		{
			return 0f;
		}
		float num = Lifetime(lifetime);
		if (age < 0f || age >= num)
		{
			return 0f;
		}
		float num2 = Math.Min(0.1f, num * 0.25f);
		float num3 = Math.Min(0.35f, num * 0.5f);
		float num4 = 1f;
		if (num2 > 0f && age < num2)
		{
			num4 = age / num2;
		}
		float num5 = num - age;
		if (num3 > 0f && num5 < num3)
		{
			num4 = Math.Min(num4, num5 / num3);
		}
		if (!(num4 < 0f))
		{
			if (!(num4 > 1f))
			{
				return num4;
			}
			return 1f;
		}
		return 0f;
	}

	internal static bool ShouldDrawIcons(bool visible, KillIconDisplay display)
	{
		if (visible)
		{
			return UsesHeadIcons(display);
		}
		return false;
	}

	internal static bool ShouldQueueCardPreviews(KillIconDisplay display)
	{
		return !UsesHeadIcons(display);
	}

	internal static void PreviewCombo(int index, out KillTier tier, out KillFlight flight)
	{
		int num = ((index > 0) ? (index % 9) : 0);
		tier = ((num >= 3) ? ((num < 6) ? KillTier.Elite : KillTier.Boss) : KillTier.Minor);
		flight = (KillFlight)(num % 3);
	}

	internal static bool ShouldDrawCard(KillIconDisplay display, int iconToken, float iconExpiresAt, float now, bool iconPresent)
	{
		if (iconToken < 0)
		{
			return !UsesHeadIcons(display);
		}
		if (!UsesHeadIcons(display))
		{
			return true;
		}
		if (iconToken == 0)
		{
			return true;
		}
		if (!KillPolicy.IsFinite(now) || !KillPolicy.IsFinite(iconExpiresAt))
		{
			return true;
		}
		if (now >= iconExpiresAt)
		{
			return false;
		}
		return !iconPresent;
	}
}
