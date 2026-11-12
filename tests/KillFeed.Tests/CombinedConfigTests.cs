using System;
using System.IO;
using BepInEx.Configuration;
using HawChow.GTFOKillFeed;

namespace KillFeed.Tests;

internal static class CombinedConfigTests
{
	internal static void Run()
	{
		NewInstall();
		LegacyUpgrade();
		LegacyHotReloadBoth();
		ExplicitIndependent();
		ConflictPriority();
		NoCrossOverwrite();
	}

	private static void NewInstall()
	{
		using Joint j = Joint.Create(null);
		j.CheckVisual("joint.new", head: false, card: true, mode: "Explicit");
		j.CheckSound("joint.new.master", master: true);
		j.CheckChannel("joint.new.hit", enabled: false, volume: 0.4f);
		j.CheckChannel("joint.new.kill", enabled: false, volume: 0.4f);
		Harness.Check("joint.new.silent", !j.Current.AnyPlays);
		Harness.Eq("joint.new.display", "", j.Raw("KillIcon", "Display"));
		Harness.Eq("joint.new.viewmode", "Explicit", j.Raw("KillIcon", "ViewMode"));
		Harness.Eq("joint.new.sound-enabled", "true", j.Raw("HitSound", "Enabled"));
		Harness.Eq("joint.new.hit-enabled", "false", j.Raw("HitSound", "HitEnabled"));
		j.Restart();
		j.CheckVisual("joint.new.restart", head: false, card: true, mode: "Explicit");
		j.CheckChannel("joint.new.restart.hit", enabled: false, volume: 0.4f);
	}

	private static void LegacyUpgrade()
	{
		using Joint j = Joint.Create("[KillIcon]\r\nDisplay = Head\r\n[HitSound]\r\nEnabled = false\r\nVolume = 0.3\r\n");
		j.CheckVisual("joint.legacy", head: true, card: false, mode: "Legacy:Head");
		j.CheckSound("joint.legacy.master", master: false);
		j.CheckChannel("joint.legacy.hit", enabled: true, volume: 0.3f);
		j.CheckChannel("joint.legacy.kill", enabled: true, volume: 0.3f);
		Harness.Check("joint.legacy.silent", !j.Current.AnyPlays);
		Harness.Eq("joint.legacy.display-preserved", "Head", j.Raw("KillIcon", "Display"));
		Harness.Eq("joint.legacy.enabled-preserved", "false", j.Raw("HitSound", "Enabled"));
		Harness.Eq("joint.legacy.volume-preserved", "0.3", j.Raw("HitSound", "Volume"));
		Harness.Eq("joint.legacy.viewmode", "Legacy:Head", j.Raw("KillIcon", "ViewMode"));
		j.Edit("HitSound", "Enabled", "true");
		Harness.Check("joint.legacy.master-hot-on", j.Current.Master && j.Current.AnyPlays);
		Harness.Near("joint.legacy.master-hot-volume", 0.3f, j.Current.HitVolume);
		j.CheckVisual("joint.legacy.sound-edit-does-not-touch-visual", head: true, card: false, mode: "Legacy:Head");
		j.Edit("KillIcon", "Display", "Card");
		j.CheckVisual("joint.legacy.display-hot", head: false, card: true, mode: "Legacy:Card");
		Harness.Check("joint.legacy.sound-still-on", j.Current.AnyPlays);
		Harness.Near("joint.legacy.sound-volume-kept", 0.3f, j.Current.HitVolume);
		j.Restart();
		j.CheckVisual("joint.legacy.restart", head: false, card: true, mode: "Legacy:Card");
		j.CheckChannel("joint.legacy.restart.hit", enabled: true, volume: 0.3f);
	}

	private static void LegacyHotReloadBoth()
	{
		using Joint j = Joint.Create("[KillIcon]\r\nDisplay = Head\r\n[HitSound]\r\nEnabled = false\r\nVolume = 0.3\r\n");
		j.Write("KillIcon", "Display", "Card");
		j.Write("HitSound", "Enabled", "true");
		j.Write("HitSound", "Volume", "0.6");
		j.Reload();
		j.CheckVisual("joint.hotboth.visual", head: false, card: true, mode: "Legacy:Card");
		j.CheckSound("joint.hotboth.master", master: true);
		Harness.Near("joint.hotboth.hit", 0.6f, j.Current.HitVolume);
		Harness.Near("joint.hotboth.kill", 0.6f, j.Current.KillVolume);
		Harness.Eq("joint.hotboth.display", "Card", j.Raw("KillIcon", "Display"));
		Harness.Eq("joint.hotboth.old-enabled", "true", j.Raw("HitSound", "Enabled"));
		Harness.Eq("joint.hotboth.old-volume", "0.6", j.Raw("HitSound", "Volume"));
	}

	private static void ExplicitIndependent()
	{
		using Joint j = Joint.Create("[KillIcon]\r\nDisplay = Head\r\n[HitSound]\r\nEnabled = false\r\nVolume = 0.3\r\n");
		j.Write("Card", "Enabled", "true");
		j.Write("KillIcon", "HeadEnabled", "true");
		j.Write("HitSound", "Enabled", "true");
		j.Write("HitSound", "HitEnabled", "true");
		j.Write("HitSound", "KillEnabled", "false");
		j.Write("HitSound", "HitVolume", "0.5");
		j.Reload();
		j.CheckVisual("joint.explicit.visual", head: true, card: true, mode: "Explicit");
		j.CheckSound("joint.explicit.master", master: true);
		Harness.Check("joint.explicit.hit", j.Current.HitPlays);
		Harness.Check("joint.explicit.kill-off", !j.Current.KillPlays);
		Harness.Near("joint.explicit.hit-volume", 0.1875f, j.Current.HitVolume);
		j.Edit("KillIcon", "Display", "Card");
		j.CheckVisual("joint.explicit.display-ignored", head: true, card: true, mode: "Explicit");
		Harness.Check("joint.explicit.sound-unchanged", j.Current.HitPlays && !j.Current.KillPlays);
		j.Restart();
		j.CheckVisual("joint.explicit.restart", head: true, card: true, mode: "Explicit");
		Harness.Check("joint.explicit.restart.hit", j.Current.HitPlays);
		Harness.Check("joint.explicit.restart.kill-off", !j.Current.KillPlays);
	}

	private static void ConflictPriority()
	{
		using Joint j = Joint.Create("[KillIcon]\r\nDisplay = Card\r\nHeadEnabled = true\r\n[Card]\r\nEnabled = false\r\n[HitSound]\r\nEnabled = false\r\nHitEnabled = true\r\n");
		j.CheckVisual("joint.conflict.visual", head: true, card: false, mode: "Explicit");
		j.CheckSound("joint.conflict.master", master: false);
		Harness.Check("joint.conflict.hit-channel", j.Current.HitEnabled);
		Harness.Check("joint.conflict.kill-channel-fallback", j.Current.KillEnabled);
		Harness.Check("joint.conflict.parent-silent", !j.Current.AnyPlays);
		j.Edit("KillIcon", "Display", "Head");
		j.CheckVisual("joint.conflict.display-ignored", head: true, card: false, mode: "Explicit");
		j.Edit("HitSound", "Enabled", "true");
		Harness.Check("joint.conflict.parent-on", j.Current.AnyPlays);
	}

	private static void NoCrossOverwrite()
	{
		using Joint j = Joint.Create("[KillIcon]\r\nDisplay = Head\r\n[HitSound]\r\nEnabled = false\r\nVolume = 0.3\r\n");
		Harness.Eq("joint.overwrite.display", "Head", j.Raw("KillIcon", "Display"));
		Harness.Eq("joint.overwrite.sound-enabled", "false", j.Raw("HitSound", "Enabled"));
		Harness.Eq("joint.overwrite.sound-volume", "0.3", j.Raw("HitSound", "Volume"));
		j.Edit("KillIcon", "HeadEnabled", "true");
		Harness.Eq("joint.overwrite.after-visual.display", "Head", j.Raw("KillIcon", "Display"));
		Harness.Eq("joint.overwrite.after-visual.sound-enabled", "false", j.Raw("HitSound", "Enabled"));
		Harness.Eq("joint.overwrite.after-visual.sound-volume", "0.3", j.Raw("HitSound", "Volume"));
		j.Edit("HitSound", "HitEnabled", "true");
		j.CheckVisual("joint.overwrite.after-sound.visual", head: true, card: false, mode: "Legacy:Head");
		Harness.Eq("joint.overwrite.after-sound.display", "Head", j.Raw("KillIcon", "Display"));
	}

	private sealed class Joint : IDisposable
	{
		private string _dir;

		private string _path;

		private ConfigFile _cfg;

		private KillViewConfigFile _viewSnapshot;

		private ConfigEntry<string> _display;

		private ConfigEntry<bool> _head;

		private ConfigEntry<bool> _card;

		private ConfigEntry<string> _mode;

		internal SoundConfigBinding Sound;

		internal SoundSettings Current => Sound.Current();

		internal static Joint Create(string body)
		{
			Joint joint = new Joint();
			joint._dir = Scratch.NewDir("joint");
			joint._path = Path.Combine(joint._dir, "HawChow.GTFOKillFeed.cfg");
			if (body != null)
			{
				File.WriteAllText(joint._path, body);
			}
			joint.Host();
			return joint;
		}

		private void Host()
		{
			_cfg = new ConfigFile(_path, saveOnInit: true);
			_viewSnapshot = KillViewConfigFile.Read(_path);
			string text = File.Exists(_path) ? File.ReadAllText(_path) : null;
			SoundConfigPlan plan = SoundConfigPlan.FromText(text, File.Exists(_path));
			_display = _cfg.Bind("KillIcon", "Display", "", "legacy display");
			_card = _cfg.Bind("Card", "Enabled", true, "card");
			_head = _cfg.Bind("KillIcon", "HeadEnabled", false, "head");
			_mode = _cfg.Bind("KillIcon", "ViewMode", "", "view mode");
			Sound = SoundConfigBinding.Bind(_cfg, plan);
			ApplyView(_viewSnapshot);
		}

		private void ApplyView(KillViewConfigFile snapshot)
		{
			KillViewSwitches view = KillViewPolicy.ResolveSwitches(snapshot);
			KillViewConfigApply.Apply(view, delegate(bool value)
			{
				_head.Value = value;
			}, delegate(bool value)
			{
				_card.Value = value;
			}, delegate(string value)
			{
				_mode.Value = value;
			});
		}

		internal void Reload()
		{
			bool save = _cfg.SaveOnConfigSet;
			try
			{
				_cfg.SaveOnConfigSet = false;
				_cfg.Reload();
				ApplyView(KillViewConfigFile.Read(_path));
			}
			finally
			{
				_cfg.SaveOnConfigSet = save;
			}
		}

		internal void Restart()
		{
			Host();
		}

		internal void Edit(string section, string key, string value)
		{
			Write(section, key, value);
			Reload();
		}

		internal void Write(string section, string key, string value)
		{
			string text = File.ReadAllText(_path);
			string[] lines = text.Replace("\r\n", "\n").Split('\n');
			string wanted = section ?? string.Empty;
			bool inSection = false;
			bool wrote = false;
			for (int i = 0; i < lines.Length; i++)
			{
				string line = lines[i].Trim();
				if (line.StartsWith("[") && line.EndsWith("]"))
				{
					inSection = string.Equals(line.Substring(1, line.Length - 2).Trim(), wanted, StringComparison.OrdinalIgnoreCase);
					continue;
				}
				if (!inSection || line.Length == 0 || line[0] == '#' || line[0] == ';')
				{
					continue;
				}
				int eq = line.IndexOf('=');
				if (eq <= 0 || !string.Equals(line.Substring(0, eq).Trim(), key, StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				lines[i] = key + " = " + value;
				wrote = true;
				break;
			}
			if (!wrote)
			{
				throw new InvalidOperationException("key not found: " + section + "/" + key);
			}
			File.WriteAllText(_path, string.Join("\n", lines));
		}

		internal string Raw(string section, string key)
		{
			return KillViewConfigFile.Read(_path).Raw(section, key);
		}

		internal void CheckVisual(string name, bool head, bool card, string mode)
		{
			Harness.Eq(name + ".head", head, _head.Value);
			Harness.Eq(name + ".card", card, _card.Value);
			Harness.Eq(name + ".mode", mode, _mode.Value);
		}

		internal void CheckSound(string name, bool master)
		{
			Harness.Eq(name, master, Current.Master);
		}

		internal void CheckChannel(string name, bool enabled, float volume)
		{
			bool flag = name.EndsWith(".kill", StringComparison.Ordinal);
			Harness.Eq(name + ".enabled", enabled, flag ? Current.KillEnabled : Current.HitEnabled);
			Harness.Near(name + ".volume", volume, flag ? Current.KillVolume : Current.HitVolume, 0.0001f);
		}

		public void Dispose()
		{
			try
			{
				Directory.Delete(_dir, true);
			}
			catch (System.Exception)
			{
			}
		}
	}
}
