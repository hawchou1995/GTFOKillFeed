using System;
using System.IO;
using BepInEx.Configuration;

namespace HawChow.GTFOKillFeed.Tests;

internal static class ConfigIntegrationTests
{
	internal static void Run()
	{
		NewInstallDefaults();
		LegacyHeadHotSwitchesToCardAndBack();
		UpgradeRestartKeepsLegacy();
		ExplicitBothOnSurvivesDisplayAndRestart();
		OnlyOneNewSwitchBecomesExplicit();
		LegacyCardThenHeadConflict();
		OldNewConflictAtFirstLoad();
		ExplicitDoubleOnAfterRestart();
	}

	private static void NewInstallDefaults()
	{
		using Harness h = Harness.Create(null);
		h.Assert("new head off", expectedHead: false);
		h.AssertCard("new card on", expectedCard: true);
		h.AssertMode("new mode", "Explicit");
		Program.True("new install no legacy", !h.Snapshot.Has("KillIcon", "Display"));
		h.Restart();
		h.Assert("new restart head off", expectedHead: false);
		h.AssertCard("new restart card on", expectedCard: true);
		h.AssertMode("new restart mode", "Explicit");
	}

	private static void LegacyHeadHotSwitchesToCardAndBack()
	{
		using Harness h = Harness.Create("[KillIcon]\nDisplay = Head\n");
		Program.Eq("legacy head file keeps display", "Head", h.ReadValue("KillIcon", "Display"));
		h.Assert("legacy head head on", expectedHead: true);
		h.AssertCard("legacy head card off", expectedCard: false);
		h.AssertMode("legacy head mode", "Legacy:Head");
		h.SetDisplay("Card");
		h.Assert("legacy card hot head off", expectedHead: false);
		h.AssertCard("legacy card hot card on", expectedCard: true);
		h.AssertMode("legacy card hot mode", "Legacy:Card");
		Program.Eq("legacy card file display", "Card", h.ReadValue("KillIcon", "Display"));
		h.Restart();
		h.Assert("legacy card restart head off", expectedHead: false);
		h.AssertCard("legacy card restart card on", expectedCard: true);
		h.SetDisplay("Head");
		h.Assert("legacy head back head on", expectedHead: true);
		h.AssertCard("legacy head back card off", expectedCard: false);
		h.AssertMode("legacy head back mode", "Legacy:Head");
		h.Restart();
		h.Assert("legacy head restart head on", expectedHead: true);
		h.AssertCard("legacy head restart card off", expectedCard: false);
	}

	private static void UpgradeRestartKeepsLegacy()
	{
		using Harness h = Harness.Create("[KillIcon]\nDisplay = Head\n");
		h.Restart();
		h.Assert("restart legacy head on", expectedHead: true);
		h.AssertCard("restart legacy card off", expectedCard: false);
		h.AssertMode("restart legacy mode", "Legacy:Head");
		h.SetDisplay("Card");
		h.AssertCard("restart legacy card on", expectedCard: true);
		h.Assert("restart legacy head off", expectedHead: false);
		h.AssertMode("restart legacy card mode", "Legacy:Card");
		h.Restart();
		h.AssertCard("restart2 card stays on", expectedCard: true);
		h.Assert("restart2 head stays off", expectedHead: false);
	}

	private static void ExplicitBothOnSurvivesDisplayAndRestart()
	{
		using Harness h = Harness.Create("[KillIcon]\nDisplay = Head\n");
		h.SetNewKey("Card", "Enabled", "true");
		h.Assert("both on head", expectedHead: true);
		h.AssertCard("both on card", expectedCard: true);
		h.AssertMode("both on mode", "Explicit");
		h.SetDisplay("Card");
		h.Assert("both on ignores display card head", expectedHead: true);
		h.AssertCard("both on ignores display card card", expectedCard: true);
		h.Restart();
		h.Assert("both on restart head", expectedHead: true);
		h.AssertCard("both on restart card", expectedCard: true);
		h.AssertMode("both on restart mode", "Explicit");
		h.SetDisplay("Head");
		h.Assert("both on ignores display head head", expectedHead: true);
		h.AssertCard("both on ignores display head card", expectedCard: true);
	}

	private static void OnlyOneNewSwitchBecomesExplicit()
	{
		using Harness h = Harness.Create("[KillIcon]\nDisplay = Head\n");
		h.SetNewKey("KillIcon", "HeadEnabled", "false");
		h.Assert("one switch head off", expectedHead: false);
		h.AssertCard("one switch legacy card off", expectedCard: false);
		h.AssertMode("one switch mode", "Explicit");
		h.SetDisplay("Card");
		h.Assert("one switch ignores display head", expectedHead: false);
		h.AssertCard("one switch ignores display card", expectedCard: false);
		h.Restart();
		h.Assert("one switch restart head", expectedHead: false);
		h.AssertCard("one switch restart card", expectedCard: false);
	}

	private static void LegacyCardThenHeadConflict()
	{
		using Harness h = Harness.Create("[KillIcon]\nDisplay = Card\n");
		h.AssertCard("legacy card card on", expectedCard: true);
		h.Assert("legacy card head off", expectedHead: false);
		h.SetNewKey("KillIcon", "HeadEnabled", "true");
		h.Assert("conflict head on", expectedHead: true);
		h.AssertCard("conflict card on", expectedCard: true);
		h.AssertMode("conflict mode", "Explicit");
		h.SetDisplay("Head");
		h.Assert("conflict ignores display head", expectedHead: true);
		h.AssertCard("conflict ignores display card", expectedCard: true);
	}

	private static void OldNewConflictAtFirstLoad()
	{
		using Harness h = Harness.Create("[KillIcon]\nDisplay = Card\nHeadEnabled = true\n");
		h.Assert("first conflict head", expectedHead: true);
		h.AssertCard("first conflict card legacy", expectedCard: true);
		h.AssertMode("first conflict mode", "Explicit");
		h.SetDisplay("Head");
		h.Assert("first conflict ignores display head", expectedHead: true);
		h.AssertCard("first conflict ignores display card", expectedCard: true);
	}

	private static void ExplicitDoubleOnAfterRestart()
	{
		using Harness h = Harness.Create("[Card]\nEnabled = false\n[KillIcon]\nHeadEnabled = true\n");
		h.Assert("explicit double head", expectedHead: true);
		h.AssertCard("explicit double card off", expectedCard: false);
		h.AssertMode("explicit double mode", "Explicit");
		h.Restart();
		h.Assert("explicit double restart head", expectedHead: true);
		h.AssertCard("explicit double restart card", expectedCard: false);
	}

	private sealed class Harness : IDisposable
	{
		private string _dir;

		private string _path;

		private ConfigFile _cfg;

		private ConfigEntry<bool> _head;

		private ConfigEntry<bool> _card;

		private ConfigEntry<string> _mode;

		internal KillViewConfigFile Snapshot;

		internal static Harness Create(string content)
		{
			Harness h = new Harness();
			h._dir = Path.Combine(AppContext.BaseDirectory, "cfg-tmp", Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(h._dir);
			h._path = Path.Combine(h._dir, "HawChow.GTFOKillFeed.cfg");
			if (content != null)
			{
				File.WriteAllText(h._path, content);
			}
			h.Open();
			return h;
		}

		private void Open()
		{
			_cfg = new ConfigFile(_path, true);
			Snapshot = KillViewConfigFile.Read(_path);
			_cfg.Bind("KillIcon", "Display", "", "legacy");
			_card = _cfg.Bind("Card", "Enabled", true, "card");
			_head = _cfg.Bind("KillIcon", "HeadEnabled", false, "head");
			_mode = _cfg.Bind("KillIcon", "ViewMode", "", "mode");
			Apply(Snapshot);
		}

		private void Apply(KillViewConfigFile snapshot)
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

		internal void Restart()
		{
			Open();
		}

		internal void SetDisplay(string value)
		{
			WriteValue("KillIcon", "Display", value);
			Reload();
		}

		internal void SetNewKey(string section, string key, string value)
		{
			WriteValue(section, key, value);
			Reload();
		}

		internal string ReadValue(string section, string key)
		{
			return KillViewConfigFile.Read(_path).Raw(section, key);
		}

		private void Reload()
		{
			bool save = _cfg.SaveOnConfigSet;
			try
			{
				_cfg.SaveOnConfigSet = false;
				_cfg.Reload();
				Apply(KillViewConfigFile.Read(_path));
			}
			finally
			{
				_cfg.SaveOnConfigSet = save;
			}
		}

		private void WriteValue(string section, string key, string value)
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

		internal void Assert(string name, bool expectedHead)
		{
			Program.Eq(name, expectedHead, _head.Value);
		}

		internal void AssertCard(string name, bool expectedCard)
		{
			Program.Eq(name, expectedCard, _card.Value);
		}

		internal void AssertMode(string name, string expected)
		{
			Program.Eq(name, expected, _mode.Value);
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
