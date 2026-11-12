using System;
using System.IO;

namespace HawChow.GTFOKillFeed;

internal static class TeammateMarkPolicy
{
	internal const string CaiFileName = "teammate-downed-cai.png";

	internal const string RuoFileName = "teammate-downed-ruo.png";

	internal const string SiFileName = "teammate-downed-si.png";

	internal const string ShaFileName = "teammate-downed-sha.png";

	internal const string CaiGlyph = "菜";

	internal const string RuoGlyph = "鶸";

	internal const string SiGlyph = "死";

	internal const string ShaGlyph = "殺";

	internal const string ShaSimpGlyph = "杀";

	internal const float DefaultRefDistance = 8f;

	internal const float DefaultRefPixels = 30f;

	internal const float DefaultMinPixels = 26f;

	internal const float DefaultMaxPixels = 42f;

	internal const float DefaultMaxDistance = 60f;

	internal const float DefaultHeadClearance = 0.05f;

	internal const float MinScale = 0.55f;

	internal const float MaxScale = 1.35f;

	internal const float MinRefDistance = 1f;

	internal const float MaxRefDistance = 30f;

	internal const float MinPixelFloor = 13f;

	internal const float MaxPixelCeiling = 96f;

	internal const float ScreenHeightFractionCap = 0.05f;

	internal const float EmpPollSeconds = 0.2f;

	internal const float EmpMaxRetrySeconds = 10f;

	internal const float ForgetSeconds = 0.5f;

	internal const float EmptyRosterSeconds = 0.75f;

	internal static TeammateMarkIcon ParseIcon(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return TeammateMarkIcon.Cai;
		}
		string text = value.Trim();
		if (text == "菜")
		{
			return TeammateMarkIcon.Cai;
		}
		if (text == "鶸")
		{
			return TeammateMarkIcon.Ruo;
		}
		if (text == "死")
		{
			return TeammateMarkIcon.Si;
		}
		if (text == "殺" || text == "杀")
		{
			return TeammateMarkIcon.Sha;
		}
		if (string.Equals(text, "cai", (StringComparison)5) || string.Equals(text, "1", (StringComparison)4))
		{
			return TeammateMarkIcon.Cai;
		}
		if (string.Equals(text, "ruo", (StringComparison)5) || string.Equals(text, "weak", (StringComparison)5) || string.Equals(text, "2", (StringComparison)4))
		{
			return TeammateMarkIcon.Ruo;
		}
		if (string.Equals(text, "si", (StringComparison)5) || string.Equals(text, "death", (StringComparison)5) || string.Equals(text, "3", (StringComparison)4))
		{
			return TeammateMarkIcon.Si;
		}
		if (string.Equals(text, "sha", (StringComparison)5) || string.Equals(text, "sha1", (StringComparison)5) || string.Equals(text, "kill", (StringComparison)5) || string.Equals(text, "4", (StringComparison)4))
		{
			return TeammateMarkIcon.Sha;
		}
		return TeammateMarkIcon.Cai;
	}

	internal static string IconFileName(TeammateMarkIcon icon)
	{
		return icon switch
		{
			TeammateMarkIcon.Ruo => "teammate-downed-ruo.png", 
			TeammateMarkIcon.Si => "teammate-downed-si.png", 
			TeammateMarkIcon.Sha => "teammate-downed-sha.png", 
			_ => "teammate-downed-cai.png", 
		};
	}

	internal static string IconGlyph(TeammateMarkIcon icon)
	{
		return icon switch
		{
			TeammateMarkIcon.Ruo => "鶸", 
			TeammateMarkIcon.Si => "死", 
			TeammateMarkIcon.Sha => "殺", 
			_ => "菜", 
		};
	}

	internal static string CustomProbePath(string customPath, string baseDirectory)
	{
		if (string.IsNullOrWhiteSpace(customPath))
		{
			return null;
		}
		if (IsNetworkPath(customPath))
		{
			return null;
		}
		string text = customPath.Trim();
		if (IsRootedPath(text))
		{
			return text;
		}
		if (string.IsNullOrWhiteSpace(baseDirectory))
		{
			return null;
		}
		try
		{
			return Path.GetFullPath(Path.Combine(baseDirectory, text));
		}
		catch
		{
			return null;
		}
	}

	internal static bool IsRootedPath(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return false;
		}
		try
		{
			return Path.IsPathRooted(path.Trim());
		}
		catch
		{
			return false;
		}
	}

	internal static bool IsNetworkPath(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return false;
		}
		string text = path.Trim();
		if (!text.StartsWith("\\\\", (StringComparison)4))
		{
			return text.StartsWith("//", (StringComparison)4);
		}
		return true;
	}

	internal static string CustomProbePath(string customPath)
	{
		if (!IsRootedPath(customPath) || IsNetworkPath(customPath))
		{
			return null;
		}
		return customPath.Trim();
	}

	internal static float DistanceScale(float distance, float refDistance)
	{
		float num = FeedbackPolicy.Clamp(refDistance, 1f, 30f, 8f);
		float num2 = ((KillPolicy.IsFinite(distance) && distance > 0f) ? Math.Max(distance, 0.5f) : num);
		return FeedbackPolicy.Clamp(num / num2, 0.55f, 1.35f, 1f);
	}

	internal static int FontPixels(float referencePixels, float distance, float refDistance, int screenHeight, float minPixels, float maxPixels)
	{
		float num = BarPolicy.ResolutionScale(screenHeight);
		float num2 = FeedbackPolicy.Clamp(referencePixels, 8f, 96f, 30f) * DistanceScale(distance, refDistance) * num;
		float num3 = FeedbackPolicy.Clamp(minPixels, 13f, 96f, 26f) * num;
		float num4 = FeedbackPolicy.Clamp(maxPixels, 13f, 96f, 42f) * num;
		if (num4 < num3)
		{
			num4 = num3;
		}
		if (num2 < num3)
		{
			num2 = num3;
		}
		if (num2 > num4)
		{
			num2 = num4;
		}
		if (screenHeight > 0)
		{
			float num5 = (float)screenHeight * 0.05f;
			if (num2 > num5)
			{
				num2 = num5;
			}
		}
		int num6 = (int)Math.Round((double)num2, (MidpointRounding)1);
		return Math.Max(8, num6);
	}

	internal static bool OnScreen(float x, float y, int screenWidth, int screenHeight, float margin)
	{
		if (screenWidth <= 0 || screenHeight <= 0)
		{
			return false;
		}
		if (!KillPolicy.IsFinite(x) || !KillPolicy.IsFinite(y))
		{
			return false;
		}
		float num = FeedbackPolicy.Clamp(margin, 0f, (float)Math.Min(screenWidth, screenHeight) * 0.5f, 0f);
		if (x >= num && x <= (float)screenWidth - num && y >= num)
		{
			return y <= (float)screenHeight - num;
		}
		return false;
	}

	internal static bool ShouldDrawMark(bool featureEnabled, bool inLevel, bool hudVisible, bool isLocalPlayer, bool downed, bool empSuppressed, bool hasScreenPoint, float distance, float maxDistance)
	{
		if (!featureEnabled || !inLevel || !hudVisible)
		{
			return false;
		}
		if (isLocalPlayer || empSuppressed)
		{
			return false;
		}
		if (!downed)
		{
			return false;
		}
		if (!hasScreenPoint)
		{
			return false;
		}
		if (!KillPolicy.IsFinite(distance) || distance < 0f)
		{
			return false;
		}
		return distance <= FeedbackPolicy.Clamp(maxDistance, 1f, 500f, 60f);
	}

	internal static bool ShouldProbeEmp(bool hideDuringEmp)
	{
		return hideDuringEmp;
	}

	internal static bool ShouldHideForEmp(bool hideDuringEmp, bool handlerAvailable, bool emped)
	{
		return hideDuringEmp && handlerAvailable && emped;
	}

	internal static bool IsProbeDue(float now, float nextProbeAt)
	{
		if (KillPolicy.IsFinite(now))
		{
			return now >= nextProbeAt;
		}
		return false;
	}

	internal static float NextProbeAt(float now, bool bound)
	{
		return (KillPolicy.IsFinite(now) ? now : 0f) + (bound ? 0.2f : 10f);
	}

	internal static float RetryDelay(int failures)
	{
		int num = Math.Max(0, Math.Min(8, failures));
		float num2 = 0.25f * (float)(1 << Math.Min(6, num));
		return Math.Min(10f, num2);
	}
}
