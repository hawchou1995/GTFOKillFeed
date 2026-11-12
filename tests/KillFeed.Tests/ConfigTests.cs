using System;
using System.IO;
using BepInEx.Configuration;
using HawChow.GTFOKillFeed;

namespace KillFeed.Tests;

internal static class ConfigTests
{
	internal static void Run()
	{
		ParsingKeepsPresence();
		LegacyConfigKeepsUserBehaviour();
		ModernConfigWins();
		NewInstallWritesOffDefaults();
		BindOrderHazardIsReal();
		UnreadableFileKeepsLegacy();
		HotReloadFlipsSwitches();
		LegacyLifecycle();
		NewInstallEnableAndPartial();
		PlayPathUsesEffectiveVolume();
	}

	private static string LegacyCfg(bool enabled, string volume)
	{
		return "[General]\r\n## 总开关\r\nEnabled = true\r\n\r\n[HitSound]\r\n## 1.2.16 起默认开启\r\nEnabled = " + (enabled ? "true" : "false") + "\r\n\r\n## 音效风格\r\nStyle = BF5\r\n\r\n## 音效音量\r\nVolume = " + volume + "\r\n\r\n## 静音原版\r\nMuteVanillaIndicator = false\r\n\r\n## 最小间隔\r\nMinIntervalSeconds = 0.05\r\n";
	}

	private static string NewFile(string tag, string body)
	{
		string text = Path.Combine(Scratch.NewDir(tag), "HawChow.GTFOKillFeed.cfg");
		if (body != null)
		{
			File.WriteAllText(text, body);
		}
		return text;
	}

	private static SoundConfigBinding Bind(string path)
	{
		string text = File.Exists(path) ? File.ReadAllText(path) : null;
		SoundConfigPlan soundConfigPlan = SoundConfigPlan.FromText(text, File.Exists(path));
		ConfigFile configFile = new ConfigFile(path, saveOnInit: true);
		return SoundConfigBinding.Bind(configFile, soundConfigPlan);
	}

	private static void ParsingKeepsPresence()
	{
		System.Collections.Generic.Dictionary<string, string> dictionary = CfgPresence.Parse("\uFEFF[General]\r\nEnabled = true\r\n[HitSound]\r\nEnabled = false\r\nVolume = 0.8\r\n# comment\r\n## doc\r\nBroken\r\n");
		Harness.Check("config.parse.section-key", dictionary.ContainsKey("HitSound/Enabled"));
		Harness.Check("config.parse.value", dictionary["General/Enabled"] == "true" && dictionary["HitSound/Enabled"] == "false");
		Harness.Check("config.parse.comment-ignored", !dictionary.ContainsKey("HitSound/# comment"));
		Harness.Check("config.parse.empty-is-empty", CfgPresence.Parse(null).Count == 0 && CfgPresence.Parse(string.Empty).Count == 0);
		Harness.Check("config.parse.bool-forms", CfgPresence.TryBool(dictionary, "General", "Enabled", out bool value) && value);
		Harness.Check("config.parse.float", CfgPresence.TryFloat(dictionary, "HitSound", "Volume", out float value2) && Math.Abs(value2 - 0.8f) < 1e-6f);
		Harness.Check("config.parse.nan-float-parses", CfgPresence.TryFloat(CfgPresence.Parse("[HitSound]\r\nHitVolume = NaN\r\n"), "HitSound", "HitVolume", out float value3) && float.IsNaN(value3));
	}

	private static void LegacyConfigKeepsUserBehaviour()
	{
		string text = NewFile("legacy-on", LegacyCfg(enabled: true, volume: "0.8"));
		SoundConfigPlan soundConfigPlan = SoundConfigPlan.FromText(File.ReadAllText(text), fileExisted: true);
		Harness.Eq("config.legacy.mode", SoundConfigMode.Legacy, soundConfigPlan.Mode);
		SoundSettings soundSettings = soundConfigPlan.Defaults();
		Harness.Check("config.legacy.seeds-channels-on", soundSettings.HitEnabled && soundSettings.KillEnabled);
		Harness.Near("config.legacy.seeds-volume-hit", 0.8f, soundSettings.HitVolume);
		Harness.Near("config.legacy.seeds-volume-kill", 0.8f, soundSettings.KillVolume);
		Harness.Check("config.legacy.same-behaviour-as-before", soundSettings.HitPlays && soundSettings.KillPlays);
		SoundConfigBinding soundConfigBinding = Bind(text);
		string text2 = File.ReadAllText(text);
		Harness.Check("config.legacy.old-enabled-line-preserved", CountLine(text2, "Enabled = true") == 2);
		Harness.Check("config.legacy.old-volume-line-preserved", CountLine(text2, "Volume = 0.8") == 1);
		Harness.Check("config.legacy.old-mute-line-preserved", CountLine(text2, "MuteVanillaIndicator = false") == 1);
		Harness.Check("config.legacy.new-hit-enabled-true", CountLine(text2, "HitEnabled = true") == 1);
		Harness.Check("config.legacy.new-kill-enabled-true", CountLine(text2, "KillEnabled = true") == 1);
		Harness.Check("config.legacy.new-volumes-seeded", CountLine(text2, "HitVolume = 0.8") == 1 && CountLine(text2, "KillVolume = 0.8") == 1);
		SoundSettings soundSettings2 = soundConfigBinding.Current();
		Harness.Check("config.legacy.bound-behaviour-unchanged", soundSettings2.HitPlays && soundSettings2.KillPlays);
		Harness.Near("config.legacy.bound-volume", 0.8f, soundSettings2.HitVolume);

		string text3 = NewFile("legacy-off", LegacyCfg(enabled: false, volume: "0.8"));
		SoundConfigPlan soundConfigPlan2 = SoundConfigPlan.FromText(File.ReadAllText(text3), fileExisted: true);
		SoundSettings soundSettings3 = soundConfigPlan2.Defaults();
		Harness.Check("config.legacy-off.seeds-channels-on", soundSettings3.HitEnabled && soundSettings3.KillEnabled);
		Harness.Check("config.legacy-off.parent-keeps-silent", !soundSettings3.AnyPlays);
		Harness.Near("config.legacy-off.seeds-volume", 0.8f, soundSettings3.HitVolume);

		string text4 = NewFile("legacy-volume-only", "[HitSound]\r\nVolume = 0.3\r\n");
		SoundConfigPlan soundConfigPlan3 = SoundConfigPlan.FromText(File.ReadAllText(text4), fileExisted: true);
		SoundSettings soundSettings4 = soundConfigPlan3.Defaults();
		Harness.Check("config.legacy-volume-only.channels-on", soundSettings4.HitEnabled && soundSettings4.KillEnabled);
		Harness.Near("config.legacy-volume-only.volume", 0.3f, soundSettings4.HitVolume);
		Harness.Near("config.legacy-volume-only.kill-volume", 0.3f, soundSettings4.KillVolume);
	}

	private static int CountLine(string text, string line)
	{
		int num = 0;
		string[] array = text.Replace("\r\n", "\n").Split('\n');
		for (int i = 0; i < array.Length; i++)
		{
			if (array[i].Trim() == line)
			{
				num++;
			}
		}
		return num;
	}

	private static void ModernConfigWins()
	{
		string text = NewFile("modern", LegacyCfg(enabled: true, volume: "0.8") + "\r\nHitEnabled = false\r\nKillEnabled = true\r\nHitVolume = 0.25\r\nKillVolume = 0.6\r\n");
		SoundConfigPlan soundConfigPlan = SoundConfigPlan.FromText(File.ReadAllText(text), fileExisted: true);
		Harness.Eq("config.modern.mode", SoundConfigMode.Modern, soundConfigPlan.Mode);
		SoundSettings soundSettings = soundConfigPlan.Defaults();
		Harness.Check("config.modern.new-keys-win-over-legacy", !soundSettings.HitEnabled && soundSettings.KillEnabled);
		Harness.Near("config.modern.hit-volume", 0.25f, soundSettings.HitVolume);
		Harness.Near("config.modern.kill-volume", 0.6f, soundSettings.KillVolume);
		SoundConfigBinding soundConfigBinding = Bind(text);
		string text2 = File.ReadAllText(text);
		Harness.Check("config.modern.values-not-overwritten", CountLine(text2, "HitVolume = 0.25") == 1 && CountLine(text2, "KillVolume = 0.6") == 1 && CountLine(text2, "KillEnabled = true") == 1);
		Harness.Check("config.modern.behaviour", !soundConfigBinding.Current().HitPlays && soundConfigBinding.Current().KillPlays);

		string text5 = NewFile("modern-parent-off", LegacyCfg(enabled: false, volume: "0.8") + "\r\nKillEnabled = true\r\nKillVolume = 0.6\r\n");
		SoundConfigBinding soundConfigBinding2 = Bind(text5);
		Harness.Check("config.modern.parent-off-still-silent", !soundConfigBinding2.Current().KillPlays);
		Harness.Check("config.modern.parent-off-keeps-new-value", File.ReadAllText(text5).Contains("KillEnabled = true"));

		string text3 = NewFile("modern-partial", LegacyCfg(enabled: false, volume: "0.8") + "\r\nHitVolume = 0.6\r\n");
		SoundConfigPlan soundConfigPlan2 = SoundConfigPlan.FromText(File.ReadAllText(text3), fileExisted: true);
		SoundSettings soundSettings2 = soundConfigPlan2.Defaults();
		Harness.Check("config.modern-partial.missing-switch-follows-legacy", soundSettings2.HitEnabled && soundSettings2.KillEnabled);
		Harness.Check("config.modern-partial.parent-off-silent", !soundSettings2.AnyPlays);
		Harness.Near("config.modern-partial.explicit-volume", 0.6f, soundSettings2.HitVolume);
		Harness.Near("config.modern-partial.missing-volume-inherits-legacy", 0.8f, soundSettings2.KillVolume);

		string text4 = NewFile("modern-nan", "[HitSound]\r\nHitEnabled = true\r\nKillEnabled = true\r\nHitVolume = NaN\r\nKillVolume = 2\r\n");
		SoundConfigPlan soundConfigPlan3 = SoundConfigPlan.FromText(File.ReadAllText(text4), fileExisted: true);
		SoundSettings soundSettings3 = soundConfigPlan3.Defaults();
		Harness.Near("config.modern.nan-volume-falls-back", 0.4f, soundSettings3.HitVolume);
		Harness.Near("config.modern.over-range-volume-clamped", 1f, soundSettings3.KillVolume);
	}

	private static void NewInstallWritesOffDefaults()
	{
		string text = NewFile("fresh", null);
		SoundConfigBinding soundConfigBinding = Bind(text);
		string text2 = File.ReadAllText(text);
		Harness.Check("config.fresh.hit-off-written", CountLine(text2, "HitEnabled = false") == 1);
		Harness.Check("config.fresh.kill-off-written", CountLine(text2, "KillEnabled = false") == 1);
		Harness.Check("config.fresh.volumes-written", CountLine(text2, "HitVolume = 0.4") == 1 && CountLine(text2, "KillVolume = 0.4") == 1);
		Harness.Check("config.fresh.master-legacy-default", CountLine(text2, "Enabled = true") == 1 && CountLine(text2, "Volume = 0.8") == 1);
		Harness.Check("config.fresh.silent", !soundConfigBinding.Current().AnyPlays);
		SoundConfigPlan soundConfigPlan = SoundConfigPlan.FromText(File.ReadAllText(text), fileExisted: true);
		Harness.Eq("config.fresh.reload-is-modern", SoundConfigMode.Modern, soundConfigPlan.Mode);
		Harness.Near("config.fresh.reload-keeps-volume", 0.4f, soundConfigPlan.Defaults().HitVolume);
		Harness.Check("config.fresh.reload-keeps-off", !soundConfigPlan.Defaults().AnyPlays);
	}

	private static void BindOrderHazardIsReal()
	{
		string text = NewFile("order-wrong", LegacyCfg(enabled: true, volume: "0.8"));
		SoundConfigPlan soundConfigPlan = SoundConfigPlan.FromText(null, fileExisted: false);
		ConfigFile configFile = new ConfigFile(text, saveOnInit: true);
		SoundConfigBinding.Bind(configFile, soundConfigPlan);
		SoundConfigPlan soundConfigPlan2 = SoundConfigPlan.FromText(File.ReadAllText(text), fileExisted: true);
		Harness.Eq("config.order.wrong-order-looks-modern", SoundConfigMode.Modern, soundConfigPlan2.Mode);
		Harness.Check("config.order.wrong-order-loses-user-sound", !soundConfigPlan2.Defaults().HitPlays && !soundConfigPlan2.Defaults().KillPlays);
		Harness.Near("config.order.wrong-order-loses-user-volume", 0.4f, soundConfigPlan2.Defaults().HitVolume);
		string text2 = NewFile("order-right", LegacyCfg(enabled: true, volume: "0.8"));
		SoundConfigBinding soundConfigBinding = Bind(text2);
		Harness.Check("config.order.scan-first-keeps-user-sound", soundConfigBinding.Current().HitPlays && soundConfigBinding.Current().KillPlays);
	}

	private static void UnreadableFileKeepsLegacy()
	{
		SoundConfigPlan soundConfigPlan = SoundConfigPlan.FromUnreadableFile();
		Harness.Eq("config.unreadable.mode", SoundConfigMode.Unreadable, soundConfigPlan.Mode);
		SoundSettings soundSettings = soundConfigPlan.Defaults();
		Harness.Check("config.unreadable.keeps-sound-on", soundSettings.HitPlays && soundSettings.KillPlays);
		Harness.Near("config.unreadable.keeps-old-volume", 0.8f, soundSettings.HitVolume);
	}

	private static void ReloadWith(ConfigFile configFile)
	{
		bool saveOnConfigSet = configFile.SaveOnConfigSet;
		try
		{
			configFile.SaveOnConfigSet = false;
			configFile.Reload();
		}
		finally
		{
			configFile.SaveOnConfigSet = saveOnConfigSet;
		}
	}

	private static void HotReloadFlipsSwitches()
	{
		string text = NewFile("hot", "[HitSound]\r\nEnabled = true\r\nVolume = 0.8\r\nHitEnabled = false\r\nKillEnabled = false\r\nHitVolume = 0.4\r\nKillVolume = 0.4\r\n");
		SoundConfigPlan soundConfigPlan = SoundConfigPlan.FromText(File.ReadAllText(text), fileExisted: true);
		ConfigFile configFile = new ConfigFile(text, saveOnInit: true);
		SoundConfigBinding soundConfigBinding = SoundConfigBinding.Bind(configFile, soundConfigPlan);
		Harness.Check("config.hot.initial-silent", !soundConfigBinding.Current().AnyPlays);
		ConfigHotReloadGate configHotReloadGate = new ConfigHotReloadGate();
		FileInfo fileInfo = new FileInfo(text);
		configHotReloadGate.Seed(fileInfo.LastWriteTimeUtc, fileInfo.Length);
		Harness.Check("config.hot.unchanged-probe", !configHotReloadGate.ShouldReload(fileInfo.LastWriteTimeUtc, fileInfo.Length, 1f));
		File.WriteAllText(text, "[HitSound]\r\nEnabled = true\r\nVolume = 0.8\r\nHitEnabled = true\r\nKillEnabled = false\r\nHitVolume = 0.6\r\nKillVolume = 0.4\r\n\r\n# pad\r\n");
		fileInfo = new FileInfo(text);
		Harness.Check("config.hot.first-probe-is-candidate", !configHotReloadGate.ShouldReload(fileInfo.LastWriteTimeUtc, fileInfo.Length, 2f));
		Harness.Check("config.hot.stable-probe-applies", configHotReloadGate.ShouldReload(fileInfo.LastWriteTimeUtc, fileInfo.Length, 2.5f));
		ReloadWith(configFile);
		SoundSettings soundSettings = soundConfigBinding.Current();
		Harness.Check("config.hot.after-reload-hit-on", soundSettings.HitPlays && !soundSettings.KillPlays);
		Harness.Near("config.hot.after-reload-volume", 0.6f, soundSettings.HitVolume);
		configHotReloadGate.MarkApplied(fileInfo.LastWriteTimeUtc, fileInfo.Length);
		Harness.Check("config.hot.applied-stops-probing", !configHotReloadGate.ShouldReload(fileInfo.LastWriteTimeUtc, fileInfo.Length, 3f));
		File.WriteAllText(text, "[HitSound]\r\nHitEnabled = true\r\nKillEnabled = true\r\nHitVolume = NaN\r\nKillVolume = 0\r\n\r\n## pad2\r\n");
		fileInfo = new FileInfo(text);
		Harness.Check("config.hot.second-edit-candidate", !configHotReloadGate.ShouldReload(fileInfo.LastWriteTimeUtc, fileInfo.Length, 4f));
		Harness.Check("config.hot.second-edit-applies", configHotReloadGate.ShouldReload(fileInfo.LastWriteTimeUtc, fileInfo.Length, 4.5f));
		ReloadWith(configFile);
		SoundSettings soundSettings2 = soundConfigBinding.Current();
		// the second edit dropped HitVolume entirely; BepInEx resets an absent key to the
		// binding default seeded at load (legacy 0.8), and the master gain stays unity
		Harness.Near("config.hot.missing-key-falls-back-to-migrated-default", 0.8f, soundSettings2.HitVolume);
		Harness.Check("config.hot.zero-volume-mutes-kill", soundSettings2.HitPlays && !soundSettings2.KillPlays);
		ConfigHotReloadGate configHotReloadGate2 = new ConfigHotReloadGate();
		configHotReloadGate2.Seed(fileInfo.LastWriteTimeUtc, fileInfo.Length);
		configHotReloadGate2.MarkFailed(10f);
		DateTime dateTime = new DateTime(2030, 1, 1);
		Harness.Check("config.hot.failed-reload-candidate", !configHotReloadGate2.ShouldReload(dateTime, 500L, 10.0f));
		Harness.Check("config.hot.failed-reload-retry-blocked", !configHotReloadGate2.ShouldReload(dateTime, 500L, 10.5f));
		Harness.Check("config.hot.failed-reload-retry-allowed", configHotReloadGate2.ShouldReload(dateTime, 500L, 11.1f));
	}
	private static string Lines(params string[] lines)
	{
		return string.Join(Environment.NewLine, lines) + Environment.NewLine;
	}

	private static void Edit(string path, string oldLine, string newLine, string tag)
	{
		string text = File.ReadAllText(path);
		int num = text.IndexOf(oldLine, StringComparison.Ordinal);
		if (num < 0)
		{
			throw new InvalidOperationException("missing line: " + oldLine);
		}
		File.WriteAllText(path, text.Substring(0, num) + newLine + text.Substring(num + oldLine.Length) + Environment.NewLine + "# " + tag + Environment.NewLine);
	}

	private static void HotReload(ConfigFile configFile, ConfigHotReloadGate gate, string path, float now)
	{
		FileInfo fileInfo = new FileInfo(path);
		if (gate.ShouldReload(fileInfo.LastWriteTimeUtc, fileInfo.Length, now))
		{
			throw new InvalidOperationException("probe fired before it became stable");
		}
		if (!gate.ShouldReload(fileInfo.LastWriteTimeUtc, fileInfo.Length, now + ConfigHotReloadGate.StableSeconds + 0.01f))
		{
			throw new InvalidOperationException("edit not detected: " + path);
		}
		ReloadWith(configFile);
		gate.MarkApplied(fileInfo.LastWriteTimeUtc, fileInfo.Length);
	}
	// 1.2.16 user with Enabled = false and Volume = 0.3 upgrades, then keeps using the old keys.
	private static void LegacyLifecycle()
	{
		string text = NewFile("lifecycle", LegacyCfg(enabled: false, volume: "0.3"));
		SoundConfigPlan soundConfigPlan = SoundConfigPlan.FromText(File.ReadAllText(text), fileExisted: true);
		ConfigFile configFile = new ConfigFile(text, saveOnInit: true);
		SoundConfigBinding soundConfigBinding = SoundConfigBinding.Bind(configFile, soundConfigPlan);
		SoundSettings soundSettings = soundConfigBinding.Current();
		Harness.Check("lifecycle.upgrade.old-master-keeps-silent", !soundSettings.AnyPlays);
		Harness.Near("lifecycle.upgrade.same-volume-hit", 0.3f, soundSettings.HitVolume);
		Harness.Near("lifecycle.upgrade.same-volume-kill", 0.3f, soundSettings.KillVolume);
		string text2 = File.ReadAllText(text);
		Harness.Check("lifecycle.upgrade.old-lines-untouched", CountLine(text2, "Enabled = false") == 1 && CountLine(text2, "Volume = 0.3") == 1);
		Harness.Check("lifecycle.upgrade.channels-written-on", CountLine(text2, "HitEnabled = true") == 1 && CountLine(text2, "KillEnabled = true") == 1);
		Harness.Check("lifecycle.upgrade.channels-seeded-unity", CountLine(text2, "HitVolume = 0.8") == 1 && CountLine(text2, "KillVolume = 0.8") == 1);
		ConfigHotReloadGate configHotReloadGate = new ConfigHotReloadGate();
		FileInfo fileInfo = new FileInfo(text);
		configHotReloadGate.Seed(fileInfo.LastWriteTimeUtc, fileInfo.Length);
		Edit(text, "Enabled = false", "Enabled = true", "e1");
		HotReload(configFile, configHotReloadGate, text, 1f);
		SoundSettings soundSettings2 = soundConfigBinding.Current();
		Harness.Check("lifecycle.old-master-on.restores-both", soundSettings2.HitPlays && soundSettings2.KillPlays);
		Harness.Near("lifecycle.old-master-on.same-volume-hit", 0.3f, soundSettings2.HitVolume);
		Harness.Near("lifecycle.old-master-on.same-volume-kill", 0.3f, soundSettings2.KillVolume);
		Edit(text, "Volume = 0.3", "Volume = 0.6", "e2");
		HotReload(configFile, configHotReloadGate, text, 10f);
		SoundSettings soundSettings3 = soundConfigBinding.Current();
		Harness.Near("lifecycle.old-volume-hot.hit", 0.6f, soundSettings3.HitVolume);
		Harness.Near("lifecycle.old-volume-hot.kill", 0.6f, soundSettings3.KillVolume);
		Harness.Check("lifecycle.old-volume-hot.still-plays", soundSettings3.HitPlays && soundSettings3.KillPlays);
		Edit(text, "HitVolume = 0.8", "HitVolume = 0.4", "e3");
		HotReload(configFile, configHotReloadGate, text, 20f);
		SoundSettings soundSettings4 = soundConfigBinding.Current();
		Harness.Near("lifecycle.channel-hit-own-volume", 0.3f, soundSettings4.HitVolume);
		Harness.Near("lifecycle.channel-kill-unaffected", 0.6f, soundSettings4.KillVolume);
		Harness.Check("lifecycle.channel-both-still-play", soundSettings4.HitPlays && soundSettings4.KillPlays);
		Edit(text, "HitEnabled = true", "HitEnabled = false", "e4");
		HotReload(configFile, configHotReloadGate, text, 30f);
		SoundSettings soundSettings5 = soundConfigBinding.Current();
		Harness.Check("lifecycle.channel-off.hit-silent-kill-plays", !soundSettings5.HitPlays && soundSettings5.KillPlays);
		Harness.Near("lifecycle.channel-off.kill-volume", 0.6f, soundSettings5.KillVolume);
		SoundSettings soundSettings6 = Bind(text).Current();
		Harness.Near("lifecycle.restart.hit-volume", 0.3f, soundSettings6.HitVolume);
		Harness.Near("lifecycle.restart.kill-volume", 0.6f, soundSettings6.KillVolume);
		Harness.Check("lifecycle.restart.switches", soundSettings6.Master && !soundSettings6.HitEnabled && soundSettings6.KillEnabled);
		Harness.Check("lifecycle.restart.user-master-kept", File.ReadAllText(text).Contains("Volume = 0.6") && File.ReadAllText(text).Contains("Enabled = true"));
	}
	// New install: silent by default, then one channel at exactly 0.4 with the master untouched.
	private static void NewInstallEnableAndPartial()
	{
		string text = NewFile("fresh-enable", null);
		ConfigFile configFile = new ConfigFile(text, saveOnInit: true);
		SoundConfigBinding soundConfigBinding = SoundConfigBinding.Bind(configFile, SoundConfigPlan.FromText(File.ReadAllText(text), fileExisted: true));
		Harness.Check("newinstall.both-channels-off", !soundConfigBinding.Current().HitPlays && !soundConfigBinding.Current().KillPlays);
		Harness.Near("newinstall.master-default-is-unity", 1f, SoundSettings.Gain(soundConfigBinding.LegacyVolume.Value));
		Harness.Near("newinstall.master-default-value", 0.8f, soundConfigBinding.LegacyVolume.Value);
		ConfigHotReloadGate configHotReloadGate = new ConfigHotReloadGate();
		FileInfo fileInfo = new FileInfo(text);
		configHotReloadGate.Seed(fileInfo.LastWriteTimeUtc, fileInfo.Length);
		Edit(text, "HitEnabled = false", "HitEnabled = true", "n1");
		HotReload(configFile, configHotReloadGate, text, 1f);
		SoundSettings soundSettings = soundConfigBinding.Current();
		Harness.Check("newinstall.hit-on-kill-off", soundSettings.HitPlays && !soundSettings.KillPlays);
		Harness.Near("newinstall.actual-hit-volume-0.4", 0.4f, soundSettings.HitVolume);
		Edit(text, "KillEnabled = false", "KillEnabled = true", "n2");
		SoundSettings soundSettings2 = Bind(text).Current();
		Harness.Check("newinstall.both-on", soundSettings2.HitPlays && soundSettings2.KillPlays);
		Harness.Near("newinstall.actual-kill-volume-0.4", 0.4f, soundSettings2.KillVolume);

		string text2 = NewFile("partial-killvolume", Lines("[HitSound]", "Enabled = true", "Volume = 0.8", "KillVolume = 0.2"));
		SoundSettings soundSettings3 = Bind(text2).Current();
		Harness.Check("partial.missing-switches-follow-legacy", soundSettings3.HitEnabled && soundSettings3.KillEnabled);
		Harness.Near("partial.hit-seeded-unity", 0.8f, soundSettings3.HitVolume);
		Harness.Near("partial.kill-explicit-volume", 0.2f, soundSettings3.KillVolume);
		string text3 = NewFile("partial-hitenabled", Lines("[HitSound]", "Enabled = true", "Volume = 0.8", "HitEnabled = false"));
		SoundSettings soundSettings4 = Bind(text3).Current();
		Harness.Check("partial.explicit-hit-off", !soundSettings4.HitPlays && soundSettings4.KillPlays);
		Harness.Near("partial.other-channel-keeps-unity", 0.8f, soundSettings4.KillVolume);
		string text4 = NewFile("partial-nan", Lines("[HitSound]", "Enabled = true", "Volume = NaN", "HitVolume = NaN", "KillVolume = 5"));
		SoundSettings soundSettings5 = Bind(text4).Current();
		Harness.Near("partial.nan-master-is-unity", 0.8f, soundSettings5.HitVolume);
		Harness.Near("partial.over-range-clamped", 1f, soundSettings5.KillVolume);
		string text5 = NewFile("partial-volume-only-zero", Lines("[HitSound]", "Volume = 0"));
		SoundSettings soundSettings6 = Bind(text5).Current();
		Harness.Check("partial.legacy-zero-volume-silent", !soundSettings6.HitPlays && !soundSettings6.KillPlays);
		Harness.Check("partial.legacy-zero-volume-keeps-switches", soundSettings6.HitEnabled && soundSettings6.KillEnabled);
	}
	// Mirrors HitSound.OnLocalHit/OnLocalKill: gate on the channel decision, hand the
	// effective volume to the throttle, record what would reach CueWavePlayer.Play.
	private static bool Play(bool kill, SoundSettings settings, HitSoundState state, System.Collections.Generic.List<float> recorded)
	{
		if (kill ? !settings.KillPlays : !settings.HitPlays)
		{
			return false;
		}
		float volume = (kill ? settings.KillVolume : settings.HitVolume);
		return state.TryPlay(kill, 0.0, 0.05, delegate
		{
			recorded.Add(volume);
			return true;
		});
	}

	private static void PlayPathUsesEffectiveVolume()
	{
		string text = NewFile("playpath", LegacyCfg(enabled: true, volume: "0.3"));
		SoundConfigPlan soundConfigPlan = SoundConfigPlan.FromText(File.ReadAllText(text), fileExisted: true);
		ConfigFile configFile = new ConfigFile(text, saveOnInit: true);
		SoundConfigBinding soundConfigBinding = SoundConfigBinding.Bind(configFile, soundConfigPlan);
		System.Collections.Generic.List<float> recorded = new System.Collections.Generic.List<float>();
		HitSoundState hitSoundState = new HitSoundState();
		Harness.Check("playpath.hit-plays", Play(kill: false, soundConfigBinding.Current(), hitSoundState, recorded));
		Harness.Check("playpath.kill-plays", Play(kill: true, soundConfigBinding.Current(), hitSoundState, recorded));
		Harness.Near("playpath.hit-volume-is-effective", 0.3f, recorded[0]);
		Harness.Near("playpath.kill-volume-is-effective", 0.3f, recorded[1]);
		ConfigHotReloadGate configHotReloadGate = new ConfigHotReloadGate();
		FileInfo fileInfo = new FileInfo(text);
		configHotReloadGate.Seed(fileInfo.LastWriteTimeUtc, fileInfo.Length);
		Edit(text, "Volume = 0.3", "Volume = 0.6", "p1");
		HotReload(configFile, configHotReloadGate, text, 1f);
		recorded.Clear();
		hitSoundState.Reset();
		Play(kill: false, soundConfigBinding.Current(), hitSoundState, recorded);
		Play(kill: true, soundConfigBinding.Current(), hitSoundState, recorded);
		Harness.Near("playpath.hot-master-hit", 0.6f, recorded[0]);
		Harness.Near("playpath.hot-master-kill", 0.6f, recorded[1]);
		Edit(text, "KillEnabled = true", "KillEnabled = false", "p2");
		HotReload(configFile, configHotReloadGate, text, 10f);
		recorded.Clear();
		hitSoundState.Reset();
		Harness.Check("playpath.kill-switched-off", !Play(kill: true, soundConfigBinding.Current(), hitSoundState, recorded));
		Harness.Check("playpath.hit-still-plays", Play(kill: false, soundConfigBinding.Current(), hitSoundState, recorded));
		Harness.Near("playpath.hit-volume-after-kill-off", 0.6f, recorded[0]);
	}
}
