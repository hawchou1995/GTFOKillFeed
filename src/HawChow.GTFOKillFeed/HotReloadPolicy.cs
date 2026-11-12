using System.IO;

namespace HawChow.GTFOKillFeed;

internal static class HotReloadPolicy
{
	internal const float FileProbeSeconds = 0.5f;

	internal static bool ShouldProbe(float now, float nextProbeAt)
	{
		if (KillPolicy.IsFinite(now))
		{
			return now >= nextProbeAt;
		}
		return false;
	}

	internal static float NextProbe(float now)
	{
		return (KillPolicy.IsFinite(now) ? now : 0f) + 0.5f;
	}

	internal static string SafeFileName(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return "<none>";
		}
		try
		{
			return Path.GetFileName(path);
		}
		catch
		{
			return "<invalid>";
		}
	}
}
