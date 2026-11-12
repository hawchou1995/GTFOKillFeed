using System;

namespace HawChow.GTFOKillFeed;

internal static class KillCardPolicy
{
	internal const float FallbackLifetime = 2.6f;

	internal static float Lifetime(float configured)
	{
		if (!KillPolicy.IsFinite(configured))
		{
			return 2.6f;
		}
		return Math.Max(0.1f, configured);
	}

	internal static float Fade(float configured, float lifetime)
	{
		if (!KillPolicy.IsFinite(configured))
		{
			return 0.45f;
		}
		return Math.Max(0f, Math.Min(configured, Lifetime(lifetime)));
	}

	internal static float ExpiresAt(float createdAt, int iconToken, float iconExpiresAt, float lifetime, float fadeSeconds)
	{
		float num = createdAt + Lifetime(lifetime);
		if (iconToken <= 0 || !KillPolicy.IsFinite(iconExpiresAt))
		{
			return num;
		}
		float num2 = iconExpiresAt + Math.Max(0f, Fade(fadeSeconds, lifetime));
		if (!(num2 > num))
		{
			return num;
		}
		return num2;
	}

	internal static float Alpha(float now, float expiresAt, float fadeSeconds, float lifetime)
	{
		if (!KillPolicy.IsFinite(now) || !KillPolicy.IsFinite(expiresAt))
		{
			return 0f;
		}
		float num = Fade(fadeSeconds, lifetime);
		float num2 = expiresAt - now;
		if (num2 <= 0f)
		{
			return 0f;
		}
		if (num <= 0f || num2 >= num)
		{
			return 1f;
		}
		return Math.Max(0f, Math.Min(1f, num2 / num));
	}
}
