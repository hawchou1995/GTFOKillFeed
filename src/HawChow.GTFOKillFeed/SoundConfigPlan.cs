using System.Collections.Generic;

namespace HawChow.GTFOKillFeed;

internal enum SoundConfigMode
{
	NewInstall,
	Legacy,
	Modern,
	Unreadable
}

internal sealed class SoundConfigPlan
{
	internal const string Section = "HitSound";

	internal const string KeyMasterEnabled = "Enabled";

	internal const string KeyLegacyVolume = "Volume";

	internal const string KeyStyle = "Style";

	internal const string KeyMinInterval = "MinIntervalSeconds";

	internal const string KeyMuteVanilla = "MuteVanillaIndicator";

	internal const string KeyHitEnabled = "HitEnabled";

	internal const string KeyKillEnabled = "KillEnabled";

	internal const string KeyHitVolume = "HitVolume";

	internal const string KeyKillVolume = "KillVolume";

	internal const bool LegacyMasterDefault = true;

	internal const float LegacyVolumeDefault = 0.8f;

	internal const bool NewInstallChannelEnabled = false;

	internal const float DefaultChannelVolume = 0.4f;

	private static readonly string[] LegacyKeys = new string[5] { KeyMasterEnabled, KeyLegacyVolume, KeyStyle, KeyMinInterval, KeyMuteVanilla };

	private static readonly string[] ChannelKeys = new string[4] { KeyHitEnabled, KeyKillEnabled, KeyHitVolume, KeyKillVolume };

	internal SoundConfigMode Mode;

	internal bool SourceFileExisted;

	internal bool LegacyPresent;

	internal bool ChannelKeysPresent;

	internal bool BindMasterEnabled;

	internal float BindLegacyVolume;

	internal bool BindHitEnabled;

	internal bool BindKillEnabled;

	internal float BindHitVolume;

	internal float BindKillVolume;

	internal bool LegacyEnabledValue;

	internal float ChannelFallback;

	internal float LegacyVolumeValue;

	private SoundConfigPlan()
	{
	}

	internal static SoundConfigPlan FromText(string text, bool fileExisted)
	{
		Dictionary<string, string> map = CfgPresence.Parse(text);
		SoundConfigPlan soundConfigPlan = new SoundConfigPlan();
		soundConfigPlan.SourceFileExisted = fileExisted;
		soundConfigPlan.LegacyEnabledValue = LegacyMasterDefault;
		soundConfigPlan.LegacyVolumeValue = LegacyVolumeDefault;
		bool flag = false;
		bool flag2 = false;
		if (CfgPresence.TryBool(map, "HitSound", KeyMasterEnabled, out var value))
		{
			flag = true;
			soundConfigPlan.LegacyEnabledValue = value;
		}
		if (CfgPresence.TryFloat(map, "HitSound", KeyLegacyVolume, out var value2))
		{
			flag = true;
			soundConfigPlan.LegacyVolumeValue = SoundSettings.Volume(value2, LegacyVolumeDefault);
		}
		for (int i = 0; i < LegacyKeys.Length; i++)
		{
			if (CfgPresence.TryRaw(map, "HitSound", LegacyKeys[i], out string _))
			{
				flag = true;
			}
		}
		for (int j = 0; j < ChannelKeys.Length; j++)
		{
			if (CfgPresence.TryRaw(map, "HitSound", ChannelKeys[j], out string _))
			{
				flag2 = true;
			}
		}
		soundConfigPlan.LegacyPresent = flag;
		soundConfigPlan.ChannelKeysPresent = flag2;
		soundConfigPlan.Mode = (flag2 ? SoundConfigMode.Modern : (flag ? SoundConfigMode.Legacy : SoundConfigMode.NewInstall));
		soundConfigPlan.BindMasterEnabled = LegacyMasterDefault;
		soundConfigPlan.BindLegacyVolume = LegacyVolumeDefault;
		// A cfg that already carries 1.2.16 keys gets channels that follow the legacy master:
		// enabling HitSound/Enabled later must bring both sounds back, and the legacy Volume
		// stays the master gain. The new channel keys are seeded at the unity reference, so a
		// migrated output equals the old Volume exactly.
		bool flag4 = (flag || NewInstallChannelEnabled);
		float channelFallback = (flag ? LegacyVolumeDefault : DefaultChannelVolume);
		soundConfigPlan.ChannelFallback = channelFallback;
		soundConfigPlan.BindHitEnabled = (CfgPresence.TryBool(map, "HitSound", KeyHitEnabled, out var value3) ? value3 : flag4);
		soundConfigPlan.BindKillEnabled = (CfgPresence.TryBool(map, "HitSound", KeyKillEnabled, out var value4) ? value4 : flag4);
		soundConfigPlan.BindHitVolume = (CfgPresence.TryFloat(map, "HitSound", KeyHitVolume, out var value5) ? SoundSettings.Volume(value5, channelFallback) : channelFallback);
		soundConfigPlan.BindKillVolume = (CfgPresence.TryFloat(map, "HitSound", KeyKillVolume, out var value6) ? SoundSettings.Volume(value6, channelFallback) : channelFallback);
		return soundConfigPlan;
	}

	internal static SoundConfigPlan FromUnreadableFile()
	{
		SoundConfigPlan soundConfigPlan = new SoundConfigPlan();
		soundConfigPlan.Mode = SoundConfigMode.Unreadable;
		soundConfigPlan.SourceFileExisted = true;
		soundConfigPlan.LegacyPresent = true;
		soundConfigPlan.ChannelKeysPresent = false;
		soundConfigPlan.BindMasterEnabled = LegacyMasterDefault;
		soundConfigPlan.BindLegacyVolume = LegacyVolumeDefault;
		soundConfigPlan.BindHitEnabled = true;
		soundConfigPlan.BindKillEnabled = true;
		soundConfigPlan.BindHitVolume = LegacyVolumeDefault;
		soundConfigPlan.BindKillVolume = LegacyVolumeDefault;
		soundConfigPlan.ChannelFallback = LegacyVolumeDefault;
		soundConfigPlan.LegacyEnabledValue = LegacyMasterDefault;
		soundConfigPlan.LegacyVolumeValue = LegacyVolumeDefault;
		return soundConfigPlan;
	}

	internal SoundSettings Defaults()
	{
		// Defaults() mirrors the post-bind state, so it must use the values parsed out of the
		// file (LegacyEnabledValue/LegacyVolumeValue), not the Bind defaults, which only ever
		// apply to a key that is absent.
		return SoundSettings.Resolve(LegacyEnabledValue, BindHitEnabled, BindKillEnabled, BindHitVolume, BindKillVolume, LegacyVolumeValue, ChannelFallback);
	}

	internal string Describe()
	{
		return "mode=" + Mode + ";fileExisted=" + SourceFileExisted + ";legacyKeys=" + LegacyPresent + ";channelKeys=" + ChannelKeysPresent + ";legacyEnabled=" + LegacyEnabledValue + ";legacyVolume=" + LegacyVolumeValue.ToString("0.##") + ";seeded=" + Defaults().Describe();
	}
}
