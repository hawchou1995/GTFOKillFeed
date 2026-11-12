using System;
using System.Collections.Generic;

namespace HawChow.GTFOKillFeed;

internal static class HitSoundPolicy
{
	internal const bool DefaultEnabled = true;

	internal const string DefaultStyle = "BF5";

	internal const float DefaultVolume = 0.8f;

	internal const bool DefaultMuteVanillaIndicator = false;

	internal const double DefaultMinIntervalSeconds = 0.05;

	internal const float MinVolume = 0f;

	internal const float MaxVolume = 1f;

	internal const double MinIntervalFloor = 0.02;

	internal const double MinIntervalCeiling = 3.0;

	internal static readonly string[] MutedEventNames = new string[3] { "BULLETHITENEMYINDICATOR", "BULLETHITENEMYLOCALINDICATOR", "BULLETHITENEMYWEAKSPOTLOCALINDICATOR" };

	internal const int ShippedCueNames = 25;

	internal const string MissingCueReason = "cue-missing";

	internal const string ResourcePrefix = "KillFeed.Audio.";

	private const string FallbackBodyCue = "hit-bf5-hit.wav";

	internal static HitSoundStyle ParseStyle(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return HitSoundStyle.Bf5;
		}
		string text = value.Trim();
		if (string.Equals(text, "BF1", (StringComparison)5))
		{
			return HitSoundStyle.Bf1;
		}
		if (string.Equals(text, "BF6", (StringComparison)5))
		{
			return HitSoundStyle.Bf6;
		}
		return HitSoundStyle.Bf5;
	}

	internal static string StyleToken(HitSoundStyle style)
	{
		return style switch
		{
			HitSoundStyle.Bf6 => "bf6", 
			HitSoundStyle.Bf1 => "bf1", 
			_ => "bf5", 
		};
	}

	internal static string TierToken(KillTier tier)
	{
		return tier switch
		{
			KillTier.Elite => "elite", 
			KillTier.Minor => "minor", 
			_ => "boss", 
		};
	}

	internal static string FlightToken(KillFlight flight)
	{
		return flight switch
		{
			KillFlight.Flying => "flyer", 
			KillFlight.ArmoredFlying => "armor-flyer", 
			_ => string.Empty, 
		};
	}

	internal static bool HasStyleVariant(HitSoundStyle style, HitCueKind kind, KillTier tier, KillFlight flight)
	{
		if (style == HitSoundStyle.Bf5)
		{
			return true;
		}
		switch (kind)
		{
		case HitCueKind.Hit:
			return style == HitSoundStyle.Bf1;
		case HitCueKind.Kill:
			if (tier == KillTier.Boss)
			{
				return false;
			}
			return true;
		default:
			return false;
		}
	}

	internal static string CueToken(HitSoundStyle style, HitCueKind kind, KillTier tier, KillFlight flight)
	{
		if (!HasStyleVariant(style, kind, tier, flight))
		{
			return "bf5";
		}
		return StyleToken(style);
	}

	internal static string CueFile(HitSoundStyle style, HitCueKind kind, KillTier tier, KillFlight flight)
	{
		string text = CueToken(style, kind, tier, flight);
		switch (kind)
		{
		case HitCueKind.WeakspotHit:
			return "hit-bf5-weakspot.wav";
		case HitCueKind.ArmorHit:
			return "hit-bf5-armor.wav";
		case HitCueKind.Kill:
		{
			string text2 = FlightToken(flight);
			string text3 = ((text2.Length == 0) ? "kill" : ("kill-" + text2));
			return string.Concat(new string[7]
			{
				"hit-",
				text,
				"-",
				text3,
				"-",
				TierToken(tier),
				".wav"
			});
		}
		default:
			return "hit-" + text + "-hit.wav";
		}
	}

	internal static string ResourceName(HitSoundStyle style, HitCueKind kind, KillTier tier, KillFlight flight)
	{
		return "KillFeed.Audio." + CueFile(style, kind, tier, flight);
	}

	internal static string ResourceName(HitSoundStyle style, HitCueKind kind)
	{
		return ResourceName(style, kind, KillTier.Minor, KillFlight.None);
	}

	internal static string[] CueChain(HitSoundStyle style, HitCueKind kind, KillTier tier, KillFlight flight)
	{
		List<string> val = new List<string>(4);
		AddUnique(val, ResourceName(style, kind, tier, flight));
		AddUnique(val, ResourceName(HitSoundStyle.Bf5, kind, tier, flight));
		if (kind == HitCueKind.Kill)
		{
			AddUnique(val, ResourceName(HitSoundStyle.Bf5, kind, tier, KillFlight.None));
		}
		AddUnique(val, "KillFeed.Audio.hit-bf5-hit.wav");
		return val.ToArray();
	}

	internal static string[] CueChain(HitSoundStyle style, HitCueKind kind)
	{
		return CueChain(style, kind, KillTier.Minor, KillFlight.None);
	}

	private static void AddUnique(List<string> chain, string value)
	{
		if (!chain.Contains(value))
		{
			chain.Add(value);
		}
	}

	internal static float Volume(float configured)
	{
		if (float.IsNaN(configured) || float.IsInfinity(configured))
		{
			return 0.8f;
		}
		if (configured < 0f)
		{
			return 0f;
		}
		if (configured > 1f)
		{
			return 1f;
		}
		return configured;
	}

	internal static double MinInterval(double configured)
	{
		if (double.IsNaN(configured) || double.IsInfinity(configured))
		{
			return 0.05;
		}
		if (configured < 0.02)
		{
			return 0.02;
		}
		if (configured > 3.0)
		{
			return 3.0;
		}
		return configured;
	}

	internal static HitCueKind KindOf(HitKind kind, bool kill)
	{
		if (!kill)
		{
			return kind switch
			{
				HitKind.Armor => HitCueKind.ArmorHit, 
				HitKind.Weakspot => HitCueKind.WeakspotHit, 
				_ => HitCueKind.Hit, 
			};
		}
		return HitCueKind.Kill;
	}

	internal static bool ShouldPlay(bool enabled, float volume)
	{
		if (enabled)
		{
			return volume > 0f;
		}
		return false;
	}

	internal static bool IntervalAllows(double now, double lastPlayed, double interval)
	{
		if (double.IsNaN(now) || double.IsInfinity(now))
		{
			return false;
		}
		if (double.IsNaN(lastPlayed) || double.IsInfinity(lastPlayed) || lastPlayed < 0.0)
		{
			return true;
		}
		double num = MinInterval(interval);
		return now - lastPlayed >= num;
	}

	internal static bool ShouldMute(bool muteVanillaIndicator, System.Collections.Generic.ICollection<uint> mutedIds, uint eventId)
	{
		if (!muteVanillaIndicator || mutedIds == null || mutedIds.Count == 0)
		{
			return false;
		}
		return mutedIds.Contains(eventId);
	}

	internal static bool MuteRequested(bool? configured)
	{
		if (configured.HasValue)
		{
			return configured.Value;
		}
		return false;
	}
}
