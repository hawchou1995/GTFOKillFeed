using System;
using System.Collections.Generic;
using System.IO;

namespace KillFeed.Tests;

// Authoring-tree (1.2.21) source gates, kept verbatim from the plugin's own tree, with two layout
// adaptations only:
//   - Root() anchors the public repo layout (src/GTFOKillFeed.csproj instead of GTFOKillFeed.csproj);
//   - Read("src/HawChow.GTFOKillFeed/X.cs") resolves X.cs under src/HawChow.GTFOKillFeed, because the
//     authoring tree keeps every source file at its repo root while this repo keeps them under src/.
// Every behavioural assertion (scan-before-bind, registration keys, production wiring, end-of-life
// invariants, master-switch literals) is the authoring version, unchanged.
internal static class SourceGateTests
{
	internal static void Run()
	{
		ScanRunsBeforeBind();
		RegistrationKeys();
		ProductionWiring();
		ReleaseInvariants();
		FeatureDocCoversEveryKey();
		MasterSwitchUntouched();
	}

	private static string Root()
	{
		DirectoryInfo directoryInfo = new DirectoryInfo(AppContext.BaseDirectory);
		while (directoryInfo != null)
		{
			if (File.Exists(Path.Combine(directoryInfo.FullName, "src", "GTFOKillFeed.csproj")))
			{
				return directoryInfo.FullName;
			}
			directoryInfo = directoryInfo.Parent;
		}
		throw new DirectoryNotFoundException("repo root not found from " + AppContext.BaseDirectory);
	}

	private static string Read(string relative)
	{
		// "src/HawChow.GTFOKillFeed/X.cs" -> src/HawChow.GTFOKillFeed/X.cs (public layout keeps the folder).
		return File.ReadAllText(Path.Combine(Root(), relative.Replace('/', Path.DirectorySeparatorChar)));
	}

	private static void ScanRunsBeforeBind()
	{
		string text = Read("src/HawChow.GTFOKillFeed/Plugin.cs");
		int num = text.IndexOf("SoundConfig = ScanSoundConfig();", StringComparison.Ordinal);
		int num2 = text.IndexOf("Config.Bind(", StringComparison.Ordinal);
		Harness.Check("gate.plugin.scan-present", num > 0);
		Harness.Check("gate.plugin.scan-before-first-bind", num > 0 && num2 > 0 && num < num2, "scan=" + num + ";firstBind=" + num2);
		Harness.Check("gate.plugin.unreadable-fallback", text.Contains("SoundConfigPlan.FromUnreadableFile()") && text.Contains("SoundConfigPlan.FromText(text, existed)"));
		int num3 = text.IndexOf("KillViewConfigFile.Read(Config.ConfigFilePath)", StringComparison.Ordinal);
		Harness.Check("gate.plugin.visual-scan-before-first-bind", num3 > 0 && num3 < num2, "visualScan=" + num3 + ";firstBind=" + num2);
		int num4 = text.IndexOf("ApplyKillViewSwitches(viewConfig)", StringComparison.Ordinal);
		int num5 = text.IndexOf("SoundBinding = SoundConfigBinding.Bind", StringComparison.Ordinal);
		Harness.Check("gate.plugin.visual-apply-after-bind", num4 > num2, "visualApply=" + num4);
		Harness.Check("gate.plugin.sound-apply-after-bind", num5 > num2, "soundBind=" + num5);
	}

	private static void RegistrationKeys()
	{
		string text = Read("src/HawChow.GTFOKillFeed/SoundConfigBinding.cs");
		string[] array = new string[6] { "KeyMasterEnabled", "KeyLegacyVolume", "KeyHitEnabled", "KeyKillEnabled", "KeyHitVolume", "KeyKillVolume" };
		for (int i = 0; i < array.Length; i++)
		{
			Harness.Check("gate.sound.registered." + array[i], text.Contains("config.Bind(\"HitSound\", SoundConfigPlan." + array[i] + ", plan.Bind"));
		}
		string text2 = Read("src/HawChow.GTFOKillFeed/Plugin.cs");
		string[] array2 = new string[5] { "Launch", "Dismember", "Chunks", "BurstVisual", "BurstSound" };
		for (int j = 0; j < array2.Length; j++)
		{
			Harness.Check("gate.corpse.registered." + array2[j], text2.Contains("Config.Bind(\"Corpse\", \"" + array2[j] + "\", true"));
		}
		string[] array3 = new string[2] { "HitEnabled", "KillEnabled" };
		for (int k = 0; k < array3.Length; k++)
		{
			Harness.Check("gate.marker.registered." + array3[k], text2.Contains("Config.Bind(\"HitMarker\", \"" + array3[k] + "\", true"));
		}
		Harness.Check("gate.sound.legacy-fields-kept", text2.Contains("HitSoundEnabled = SoundBinding.Master") && text2.Contains("HitSoundVolume = SoundBinding.LegacyVolume"));
	}

	private static void ProductionWiring()
	{
		string text = Read("src/HawChow.GTFOKillFeed/HitSound.cs");
		Harness.Check("gate.sound.hit-channel-wired", text.Contains("sound.HitPlays") && text.Contains("sound.HitVolume"));
		Harness.Check("gate.sound.kill-channel-wired", text.Contains("sound.KillPlays") && text.Contains("sound.KillVolume"));
		Harness.Check("gate.sound.cue-uses-channel-volume", text.Contains("PlayCue(kind, tier, flight, volume)"));
		Harness.Check("gate.sound.play-path-uses-effective-volume", text.Contains("CueWavePlayer.Play(samples, samples.Length, volume)"));
		Harness.Check("gate.sound.effective-volume-from-settings", text.Contains("PlayThrottled(false, HitSoundPolicy.KindOf(kind, false), KillTier.Minor, KillFlight.None, sound.HitVolume)"));
		Harness.Check("gate.sound.legacy-volume-read-per-call", Read("src/HawChow.GTFOKillFeed/SoundConfigBinding.cs").Contains("(LegacyVolume != null) ? LegacyVolume.Value"));
		Harness.Check("gate.sound.legacy-channels-follow-master", Read("src/HawChow.GTFOKillFeed/SoundConfigPlan.cs").Contains("bool flag4 = (flag || NewInstallChannelEnabled);"));
		Harness.Check("gate.sound.master-gain-reference", Read("src/HawChow.GTFOKillFeed/SoundSettings.cs").Contains("Volume(masterVolume, ReferenceMasterVolume) / ReferenceMasterVolume"));
		Harness.Check("gate.marker.channel-wired", Read("src/HawChow.GTFOKillFeed/HitMarkerHud.cs").Contains("Plugin.MarkerChannel(kind)"));
		string text2 = Read("src/HawChow.GTFOKillFeed/CorpseFeedback.cs");
		string[] array = new string[8] { "CorpseSwitches switches = Current();", "FeedbackPolicy.ShouldApplyLaunch(switches.LaunchActive", "FeedbackPolicy.ShouldLimitRagdoll(switches.LaunchActive", "FeedbackPolicy.ShouldTrackChunks(switches.ChunksActive)", "FeedbackPolicy.ShouldBreakCorpseLimbs(item.OffSceneCleanupApplied, switches.DismemberActive)", "FeedbackPolicy.BurstPlan(switches", "FeedbackPolicy.ShouldFlushCorpseSound(switches.BurstSoundActive)", "FeedbackPolicy.ShouldTrackChunks(Current().ChunksActive)" };
		for (int i = 0; i < array.Length; i++)
		{
			Harness.Check("gate.corpse.wired." + i, text2.Contains(array[i]));
		}
		Harness.Check("gate.corpse.inactive-retry-reaches-resolver",
			text2.Contains("TryLateRagdoll(item, inactiveLease);") &&
			text2.Contains("FeedbackPolicy.ShouldResolveRagdoll(") &&
			text2.Contains("FeedbackPolicy.CanCaptureRagdollCandidate(") &&
			text2.Contains("if (!CanCaptureRagdoll(item, host)) return false;") &&
			text2.Contains("!ReferenceEquals(item.Ragdoll, previous)") &&
			!text2.Contains("if (item.Ragdoll != null && item.Ragdoll.BodyCount > 0) return;"));
		Harness.Check("gate.corpse.gib-source-probe",
			text2.Contains("gibComponents={CountGibDataComponents(item)}") &&
			text2.Contains("GetComponentsInChildren<CD_GibData>(true)") &&
			text2.Contains("unknown-no-root") && text2.Contains("unknown-read-error") &&
			!text2.Contains("note=known-environment-no-GibData"));
	}

	private static void CollectKeys(string source, HashSet<string> keys)
	{
		string text = "Bind(\"";
		int num = 0;
		while (num < source.Length)
		{
			int num2 = source.IndexOf(text, num, StringComparison.Ordinal);
			if (num2 < 0)
			{
				return;
			}
			int num3 = num2 + text.Length;
			int num4 = source.IndexOf('"', num3);
			if (num4 < 0)
			{
				return;
			}
			string text2 = source.Substring(num3, num4 - num3);
			int num5 = num4 + 1;
			while (num5 < source.Length && (source[num5] == ' ' || source[num5] == ','))
			{
				num5++;
			}
			if (num5 < source.Length && source[num5] == '"')
			{
				int num6 = source.IndexOf('"', num5 + 1);
				if (num6 > num5)
				{
					keys.Add(text2 + "/" + source.Substring(num5 + 1, num6 - num5 - 1));
				}
			}
			num = num4 + 1;
		}
	}

	// Index of the matching closing brace for the block that starts at `open`, honouring nesting.
	private static int BlockEnd(string text, int open)
	{
		int depth = 0;
		for (int i = open; i < text.Length; i++)
		{
			if (text[i] == '{') depth++;
			else if (text[i] == '}')
			{
				depth--;
				if (depth == 0) return i;
			}
		}
		return -1;
	}

	private static void ReleaseInvariants()
	{
		string text = Read("src/HawChow.GTFOKillFeed/CorpseFeedback.cs");
		string[] array = text.Replace("\r\n", "\n").Split('\n');
		int num = -1;
		for (int i = 0; i < array.Length; i++)
		{
			if (array[i].Contains("if (age >= item.EndAt)"))
			{
				num = i;
			}
		}
		Harness.Check("gate.corpse.end-of-life-block-found", num >= 0);
		Harness.Check("gate.corpse.end-of-life-not-switch-gated", num >= 0 && !array[num].Contains("switches"));
		int num2 = -1;
		int num3 = -1;
		if (num >= 0)
		{
			for (int j = num; j < Math.Min(array.Length, num + 20); j++)
			{
				if (array[j].Contains("ReleaseGibs(item);") && num2 < 0)
				{
					num2 = j;
				}
				if (array[j].Contains("CorpseFade.TryBegin(") && num3 < 0)
				{
					num3 = j;
				}
			}
		}
		Harness.Check("gate.corpse.releases-gibs-at-end", num2 > num, "endAt=" + num + ";release=" + num2);
		Harness.Check("gate.corpse.native-dissolve-at-end", num3 > num, "endAt=" + num + ";fade=" + num3);

		int releaseAt = text.IndexOf("private static void ReleaseGibs(Corpse item)", StringComparison.Ordinal);
		int releaseOpen = releaseAt < 0 ? -1 : text.IndexOf('{', releaseAt);
		int releaseEnd = releaseOpen < 0 ? -1 : BlockEnd(text, releaseOpen);
		string releaseBody = (releaseAt > 0 && releaseEnd > releaseOpen) ? text.Substring(releaseAt, releaseEnd - releaseAt) : string.Empty;
		Harness.Check("gate.corpse.release-gibs-body-found", releaseBody.Length > 0);
		Harness.Check("gate.corpse.release-gibs-not-gated-by-chunks", releaseBody.Length > 0 && !releaseBody.Contains("Chunks") && !releaseBody.Contains("switches"));

		int captureAt = text.IndexOf("internal static void CaptureGib(CD_Gib gib)", StringComparison.Ordinal);
		int captureOpen = captureAt < 0 ? -1 : text.IndexOf('{', captureAt);
		int captureEnd = captureOpen < 0 ? -1 : BlockEnd(text, captureOpen);
		string captureBody = (captureAt > 0 && captureEnd > captureOpen) ? text.Substring(captureAt, captureEnd - captureAt) : string.Empty;
		Harness.Check("gate.corpse.capture-gated-by-chunks", captureBody.Contains("ShouldTrackChunks(Current().ChunksActive)"));

		Harness.Check("gate.corpse.queue-removal-still-parent-only",
			text.Contains("if (!Plugin.Enabled.Value || !Plugin.CorpseEffects.Value) { RemoveQueued(item, \"effects-disabled\", now);"));
		Harness.Check("gate.corpse.schedule-still-parent-only",
			text.Contains("if (!Plugin.Enabled.Value || !Plugin.CorpseEffects.Value) return;"));
		Harness.Check("gate.corpse.gib-capture-callback-kept", text.Contains("CorpseFeedback.CaptureGib(") || text.Contains("CaptureGib(gib)"));
	}

	// Layout-independent gate: every `Config.Bind` key under src/HawChow.GTFOKillFeed must be
	// documented as a backticked `Section/Key` in docs/FEATURE-SWITCHES.md (§9 is generated by
	// tools/feature_switch_inventory.py from the same sources).
	private static void FeatureDocCoversEveryKey()
	{
		string text = Read("docs/FEATURE-SWITCHES.md");
		string[] array = Directory.GetFiles(Path.Combine(Root(), "src", "HawChow.GTFOKillFeed"), "*.cs");
		HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
		for (int i = 0; i < array.Length; i++)
		{
			CollectKeys(File.ReadAllText(array[i]), hashSet);
		}
		string[] array2 = new string[6] { "HitSound/Enabled", "HitSound/Volume", "HitSound/HitEnabled", "HitSound/KillEnabled", "HitSound/HitVolume", "HitSound/KillVolume" };
		for (int k = 0; k < array2.Length; k++)
		{
			hashSet.Add(array2[k]);
		}
		Harness.Check("gate.doc.key-count-covers-source", hashSet.Count >= 50, "keys=" + hashSet.Count);
		List<string> list = new List<string>();
		foreach (string item in hashSet)
		{
			if (!text.Contains("`" + item + "`"))
			{
				list.Add(item);
			}
		}
		Harness.Check("gate.doc.every-key-documented", list.Count == 0, "missing=" + string.Join(",", list));
		string[] array3 = new string[14] { "General/Enabled", "Input/ToggleKey", "Card/ShowEnemyMaxHealth", "KillIcon/Display", "HitMarker/Enabled", "HitSound/MuteVanillaIndicator", "DamageNumbers/Enabled", "DamageFx/PrismaticEnabled", "DamageFx/TiersEnabled", "DamageNumbers/ApproachEffect", "EnemyBar/Enabled", "EnemyBar/FocusOutlineEnabled", "TeammateMark/Enabled", "Corpse/FadeEffect" };
		for (int l = 0; l < array3.Length; l++)
		{
			Harness.Check("gate.doc.named-feature." + array3[l].Replace('/', '.'), text.Contains("`" + array3[l] + "`"));
		}
	}

	private static void MasterSwitchUntouched()
	{
		string text = Read("src/HawChow.GTFOKillFeed/Plugin.cs");
		Harness.Check("gate.master.general-enabled", text.Contains("Config.Bind(\"General\", \"Enabled\", KillDefaults.Enabled"));
		Harness.Check("gate.master.f7", text.Contains("Config.Bind(\"Input\", \"ToggleKey\", KeyCode.F7"));
		Harness.Check("gate.master.f9-preview", text.Contains("Config.Bind(\"Input\", \"PreviewKey\", KeyCode.F9"));
		Harness.Check("gate.mute.independent-path", Read("src/HawChow.GTFOKillFeed/HitSound.cs").Contains("HitSoundPolicy.MuteRequested"));
	}
}
