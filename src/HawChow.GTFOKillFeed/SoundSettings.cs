namespace HawChow.GTFOKillFeed;

internal readonly struct SoundSettings
{
	internal const float ReferenceMasterVolume = 0.8f;

	internal readonly bool Master;

	internal readonly float MasterGain;

	internal readonly bool HitEnabled;

	internal readonly bool KillEnabled;

	internal readonly float HitVolume;

	internal readonly float KillVolume;

	internal SoundSettings(bool master, float masterGain, bool hitEnabled, bool killEnabled, float hitVolume, float killVolume)
	{
		Master = master;
		MasterGain = masterGain;
		HitEnabled = hitEnabled;
		KillEnabled = killEnabled;
		HitVolume = hitVolume;
		KillVolume = killVolume;
	}

	internal bool HitPlays => Master && HitEnabled && HitVolume > 0f;

	internal bool KillPlays => Master && KillEnabled && KillVolume > 0f;

	internal bool AnyPlays => HitPlays || KillPlays;

	// HitVolume/KillVolume are the volumes the player actually receives: the per-channel
	// setting multiplied by the legacy HitSound/Volume master and clamped to 0-1.
	internal static SoundSettings Resolve(bool master, bool hitEnabled, bool killEnabled, float hitVolume, float killVolume, float masterVolume, float channelFallback)
	{
		float masterGain = Gain(masterVolume);
		return new SoundSettings(master, masterGain, hitEnabled, killEnabled, Effective(hitVolume, channelFallback, masterGain), Effective(killVolume, channelFallback, masterGain));
	}

	// 1.2.16 default (0.8) is unity: with the master untouched, a channel volume is the
	// output volume, so a new install at 0.4 really plays at 0.4 and a migrated 0.3 cfg
	// keeps playing at 0.3.
	internal static float Gain(float masterVolume)
	{
		return Volume(masterVolume, ReferenceMasterVolume) / ReferenceMasterVolume;
	}

	internal static float Effective(float configured, float channelFallback, float masterGain)
	{
		return Clamp01(Volume(configured, channelFallback) * masterGain);
	}

	internal static bool ChannelEnabled(bool master, bool channel)
	{
		return master && channel;
	}

	internal static float Clamp01(float value)
	{
		if (float.IsNaN(value) || value <= 0f)
		{
			return 0f;
		}
		if (value >= 1f)
		{
			return 1f;
		}
		return value;
	}

	internal static float Volume(float configured, float fallback)
	{
		float num = ((fallback < 0f) ? 0f : ((fallback > 1f) ? 1f : fallback));
		if (float.IsNaN(configured) || float.IsInfinity(configured))
		{
			return num;
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

	internal string Describe()
	{
		return "master=" + Master + ";gain=" + MasterGain.ToString("0.###") + ";hit=" + HitEnabled + "@" + HitVolume.ToString("0.###") + ";kill=" + KillEnabled + "@" + KillVolume.ToString("0.###");
	}
}
