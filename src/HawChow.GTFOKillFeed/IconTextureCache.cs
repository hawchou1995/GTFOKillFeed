using System;
using System.IO;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace HawChow.GTFOKillFeed;

internal sealed class IconTextureCache
{
	private readonly IconPipeline _pipeline;

	private readonly DiagnosticBudget _warnings = new DiagnosticBudget();

	internal IconPipeline Pipeline => _pipeline;

	internal IconTextureCache()
		: this(new FileIconIo(), new UnityIconSink(), ThreadPoolIconJobRunner.Instance)
	{
	}

	internal IconTextureCache(IIconIo io, IIconSink sink, IIconJobRunner runner)
	{
		_pipeline = new IconPipeline(io, sink, runner, (string message) =>
		{
			if (_warnings.TryUse(8))
			{
				Plugin.Instance?.Log.LogWarning("[HUD] " + message);
			}
		});
	}

	internal void Tick(string key, string path, float now)
	{
		_pipeline.Tick(key, path, now);
	}

	internal Texture2D Texture(string key)
	{
		return _pipeline.Texture(key) as Texture2D;
	}

	internal static string IconRoot()
	{
		try
		{
			string directoryName = Path.GetDirectoryName(typeof(Plugin).Assembly.Location);
			return Path.Combine(string.IsNullOrEmpty(directoryName) ? string.Empty : directoryName, "Assets", "icons");
		}
		catch
		{
			return "Assets/icons";
		}
	}

	internal static bool TryStamp(string path, out System.DateTime stamp, out long length)
	{
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0021: Expected O, but got Unknown
		stamp = System.DateTime.MinValue;
		length = -1L;
		if (string.IsNullOrWhiteSpace(path))
		{
			return false;
		}
		try
		{
			FileInfo val = new FileInfo(path);
			if (!((FileSystemInfo)val).Exists)
			{
				return false;
			}
			stamp = ((FileSystemInfo)val).LastWriteTimeUtc;
			length = val.Length;
			return true;
		}
		catch
		{
			return false;
		}
	}
}
