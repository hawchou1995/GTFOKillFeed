using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using BepInEx;
using BepInEx.Core.Logging.Interpolation;
using BepInEx.Logging;

namespace HawChow.GTFOKillFeed;

internal static class EnemyNames
{
	private sealed class NameFile
	{
		public string Source
		{
			get;
			set;
		}

		public bool UseHikariaDefaults
		{
			get;
			set;
		} = true;


		public List<NameGroup> Names
		{
			get;
			set;
		}

		public List<EnemyNameRule> Rules
		{
			get;
			set;
		} = new List<EnemyNameRule>();

	}

	private sealed class NameGroup
	{
		public List<uint> IDs
		{
			get;
			set;
		}

		public string Name
		{
			get;
			set;
		}
	}

	private static List<EnemyNameRule> _rules = new List<EnemyNameRule>();

	private static Dictionary<uint, string> _defaults = new Dictionary<uint, string>();

	private static NameFile _bundled;

	private static System.DateTime _stamp;

	private static float _checkAt;

	private static bool _useDefaults = true;

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = (JsonCommentHandling)1,
		AllowTrailingCommas = true,
		WriteIndented = true,
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	};

	internal static int Revision
	{
		get;
		private set;
	}

	internal static string FilePath => Path.Combine(Paths.ConfigPath, "HawChow.KillFeed.EnemyNames.json");

	internal static void Initialize()
	{
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			Stream manifestResourceStream = typeof(Plugin).Assembly.GetManifestResourceStream("KillFeed.EnemyNames.defaults.json");
			try
			{
				if (manifestResourceStream == null)
				{
					throw new InvalidOperationException("Bundled Hikaria name table missing.");
				}
				_bundled = JsonSerializer.Deserialize<NameFile>(manifestResourceStream, JsonOptions);
				_defaults = BuildDefaults(_bundled.Names);
				if (!File.Exists(FilePath))
				{
					File.WriteAllText(FilePath, JsonSerializer.Serialize<NameFile>(_bundled, JsonOptions));
				}
				Reload();
			}
			finally
			{
				((System.IDisposable)manifestResourceStream)?.Dispose();
			}
		}
		catch (System.Exception ex)
		{
			Plugin.Instance.Log.LogWarning("[NAMES] initialization failed: " + ex.Message);
		}
	}

	internal static void Tick(float now)
	{
		if (now < _checkAt)
		{
			return;
		}
		_checkAt = now + 2f;
		try
		{
			if (File.GetLastWriteTimeUtc(FilePath) != _stamp)
			{
				Reload();
			}
		}
		catch (System.Exception ex)
		{
			Plugin.Instance.Log.LogWarning("[NAMES] file check failed: " + ex.Message);
		}
	}

	internal static string Resolve(uint id, string raw)
	{
		return EnemyNamePolicy.Resolve(id, raw, (System.Collections.Generic.IReadOnlyList<EnemyNameRule>)_rules, (IReadOnlyDictionary<uint, string>)(object)(_useDefaults ? _defaults : null));
	}

	private static void Reload()
	{
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Expected O, but got Unknown
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_0075: Unknown result type (might be due to invalid IL or missing references)
		//IL_0081: Unknown result type (might be due to invalid IL or missing references)
		//IL_0086: Unknown result type (might be due to invalid IL or missing references)
		//IL_009c: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			_stamp = File.GetLastWriteTimeUtc(FilePath);
			FileInfo val = new FileInfo(FilePath);
			if (!((FileSystemInfo)val).Exists || val.Length > 262144)
			{
				throw new FormatException("Name file missing or exceeds 256 KiB.");
			}
			NameFile nameFile = JsonSerializer.Deserialize<NameFile>(File.ReadAllText(FilePath), JsonOptions);
			if (nameFile?.Rules == null || nameFile.Rules.Count > 1024)
			{
				throw new FormatException("Rules must be an array with at most 1024 entries.");
			}
			var enumerator = nameFile.Rules.GetEnumerator();
			try
			{
				while (enumerator.MoveNext())
				{
					if (!EnemyNamePolicy.Valid(enumerator.Current))
					{
						throw new FormatException("Rules need RawName and a single-line DisplayName up to 96 characters.");
					}
				}
			}
			finally
			{
				((System.IDisposable)enumerator).Dispose();
			}
			Dictionary<uint, string> defaults = BuildDefaults(nameFile.Names ?? _bundled?.Names ?? new List<NameGroup>());
			_rules = nameFile.Rules;
			_defaults = defaults;
			_useDefaults = nameFile.UseHikariaDefaults;
			Revision++;
			ManualLogSource log = Plugin.Instance.Log;
			bool isEnabled;
			BepInExInfoLogInterpolatedStringHandler bepInExInfoLogInterpolatedStringHandler = new BepInExInfoLogInterpolatedStringHandler(103, 4, out isEnabled);
			if (isEnabled)
			{
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral("[NAMES] defaults=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(_defaults.Count);
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral(";enabled=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(_useDefaults);
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral(";overrides=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(_rules.Count);
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral(";file=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(FilePath);
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral(";custom-rundown IDs may override defaults by exact raw name.");
			}
			log.LogInfo(bepInExInfoLogInterpolatedStringHandler);
		}
		catch (System.Exception ex)
		{
			Plugin.Instance.Log.LogWarning("[NAMES] keeping previous valid mappings: " + ex.Message);
		}
	}

	private static Dictionary<uint, string> BuildDefaults(List<NameGroup> groups)
	{
		//IL_0015: Unknown result type (might be due to invalid IL or missing references)
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_008e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0104: Unknown result type (might be due to invalid IL or missing references)
		//IL_009a: Unknown result type (might be due to invalid IL or missing references)
		//IL_009f: Unknown result type (might be due to invalid IL or missing references)
		if (groups == null || groups.Count > 1024)
		{
			throw new FormatException("Invalid default name groups.");
		}
		Dictionary<uint, string> val = new Dictionary<uint, string>();
		var enumerator = groups.GetEnumerator();
		try
		{
			while (enumerator.MoveNext())
			{
				NameGroup current = enumerator.Current;
				if (current.IDs == null || current.IDs.Count > 1024 || string.IsNullOrWhiteSpace(current.Name) || current.Name.Length > 96 || current.Name.Contains('\n') || current.Name.Contains('\r'))
				{
					throw new FormatException("Invalid name group.");
				}
				var enumerator2 = current.IDs.GetEnumerator();
				try
				{
					while (enumerator2.MoveNext())
					{
						uint current2 = enumerator2.Current;
						if (current2 != 0)
						{
							val[current2] = current.Name;
						}
					}
				}
				finally
				{
					((System.IDisposable)enumerator2).Dispose();
				}
			}
		}
		finally
		{
			((System.IDisposable)enumerator).Dispose();
		}
		if (val.Count > 4096)
		{
			throw new FormatException("Default map exceeds 4096 IDs.");
		}
		return val;
	}
}
