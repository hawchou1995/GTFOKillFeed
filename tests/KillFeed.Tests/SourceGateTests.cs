using System;
using System.Collections.Generic;
using System.IO;

namespace KillFeed.Tests;

// 1.2.19 tree: this file keeps the one layout-independent source gate — every Config.Bind key under
// src/ must be documented in docs/FEATURE-SWITCHES.md. The gates that used to live here
// (scan-before-first-bind, registration keys, production wiring, decompiled corpse invariants,
// master-switch literals) matched the exact text of the decompiled 1.2.16 units ("base.Config.Bind(",
// "num3 >= item.EndAt", GibActivatePatch.cs ...). Those units are merged into the authoring files now,
// so matching their decompiled spelling here would only produce false answers. The 1.2.19 behaviour
// gates are the policy suites in this project plus the plugin's own scripts/Verify.ps1 (that script is
// part of the authoring tree, not of this reconstruction repo).
internal static class SourceGateTests
{
	internal static void Run()
	{
		FeatureDocCoversEveryKey();
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
		return File.ReadAllText(Path.Combine(Root(), relative.Replace('/', Path.DirectorySeparatorChar)));
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
}
