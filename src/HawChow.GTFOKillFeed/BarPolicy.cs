using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

namespace HawChow.GTFOKillFeed;

internal static class BarPolicy
{
	internal enum HealthBand
	{
		Base,
		Yellow,
		Red
	}

	internal const float DefaultWidth = 200f;

	internal const float DefaultHeight = 5f;

	internal const float DefaultOpacity = 0.55f;

	internal const float DefaultNumberSize = 12f;

	internal const float DefaultHeadOffsetMetres = 0.12f;

	internal const float DefaultVerticalOffset = -8f;

	internal const float DefaultMinScreenFraction = 0.1f;

	internal const float DefaultMaxScreenFraction = 0.46f;

	internal const float DefaultScreenAnchorFraction = 0.3f;

	internal const float DefaultMaxDistance = 60f;

	internal const float DefaultYellowThresholdPercent = 66f;

	internal const float DefaultRedThresholdPercent = 33f;

	internal const float MinWidth = 90f;

	internal const float MaxWidth = 420f;

	internal const float MinHeight = 2f;

	internal const float MaxHeight = 16f;

	internal const float MinOpacity = 0.15f;

	internal const float MaxOpacity = 1f;

	internal const float MinNumberSize = 8f;

	internal const float MaxNumberSize = 22f;

	internal const float MinHeadOffsetMetres = 0f;

	internal const float MaxHeadOffsetMetres = 2f;

	internal const float MinVerticalOffset = -400f;

	internal const float MaxVerticalOffset = 400f;

	internal const float MinRange = 5f;

	internal const float MaxRange = 300f;

	internal const float HoldSeconds = 0.15f;

	internal const float FadeInSeconds = 0.1f;

	internal const float FadeOutSeconds = 0.2f;

	internal const float GhostDecayPerSecond = 2.6f;

	internal const float GhostAlpha = 0.22f;

	internal const float FillAlpha = 0.9f;

	internal const float TrackAlpha = 0.28f;

	internal const float BorderAlpha = 0.45f;

	internal const float TickAlpha = 0.35f;

	internal const int Segments = 4;

	internal const int MaxLabelNameChars = 28;

	internal const int MaxWeakspotTags = 4;

	internal const float CfoamUnitsPerBlob = 3f;

	internal const float CfoamImmuneTolerance = 1000000f;

	internal const float CfoamTagRed = 0.9490196f;

	internal const float CfoamTagGreen = 0.6509804f;

	internal const float CfoamTagBlue = 0.3529412f;

	internal static float DroppedLostSeconds => 0.35000002f;

	internal static float FillFraction(float health, float maxHealth)
	{
		if (!KillPolicy.IsFinite(health) || !KillPolicy.IsFinite(maxHealth) || maxHealth <= 0f)
		{
			return 0f;
		}
		if (health <= 0f)
		{
			return 0f;
		}
		float num = health / maxHealth;
		if (!(num >= 1f))
		{
			return num;
		}
		return 1f;
	}

	internal static HealthBand HealthColorBand(float fraction, float yellowThresholdPercent, float redThresholdPercent)
	{
		if (!KillPolicy.IsFinite(fraction))
		{
			return HealthBand.Base;
		}
		float num = FeedbackPolicy.Clamp(yellowThresholdPercent, 0f, 100f, 66f);
		float num2 = FeedbackPolicy.Clamp(redThresholdPercent, 0f, 100f, 33f);
		if (num2 > num)
		{
			num2 = num;
		}
		float num3 = FeedbackPolicy.Clamp(fraction, 0f, 1f, 0f) * 100f;
		if (num3 <= num2)
		{
			return HealthBand.Red;
		}
		if (num3 <= num)
		{
			return HealthBand.Yellow;
		}
		return HealthBand.Base;
	}

	internal static int DisplayValue(float value)
	{
		if (!KillPolicy.IsFinite(value) || value <= 0f)
		{
			return 0;
		}
		if (value >= 9999999f)
		{
			return 9999999;
		}
		return (int)Math.Round((double)value, (MidpointRounding)1);
	}

	internal static string Readout(float health, float maxHealth)
	{
		return DisplayValue(health).ToString((IFormatProvider)(object)CultureInfo.InvariantCulture) + " / " + DisplayValue(maxHealth).ToString((IFormatProvider)(object)CultureInfo.InvariantCulture);
	}

	internal static string Label(string name, float health, float maxHealth, bool showName, bool showNumbers)
	{
		string text = (showName ? TrimName(name) : null);
		string text2 = (showNumbers ? Readout(health, maxHealth) : null);
		if (text == null && text2 == null)
		{
			return string.Empty;
		}
		if (text == null)
		{
			return text2;
		}
		if (text2 == null)
		{
			return text;
		}
		return text + "   " + text2;
	}

	internal static string TrimName(string name)
	{
		return TrimName(name, 28);
	}

	internal static string TrimName(string name, int maxChars)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return "Unknown";
		}
		string text = name.Trim();
		int num = Math.Max(1, maxChars);
		if (text.Length <= num)
		{
			return text;
		}
		if (num == 1)
		{
			return "…";
		}
		return text.Substring(0, num - 1) + "…";
	}

	internal static string HeadPartLabel(string language)
	{
		if (!KillPolicy.IsChinese(language))
		{
			return "Head";
		}
		return "头";
	}

	internal static string GenericWeakspotPart(string language)
	{
		if (!KillPolicy.IsChinese(language))
		{
			return "Weakspot";
		}
		return "弱点";
	}

	internal static string PartLabel(string debugName, string language)
	{
		string value = debugName ?? string.Empty;
		if (ContainsAny(value, "head", "skull", "face", "cranial", "头", "頭"))
		{
			return HeadPartLabel(language);
		}
		if (ContainsAny(value, "back", "rear", "spine", "背"))
		{
			if (!KillPolicy.IsChinese(language))
			{
				return "Back";
			}
			return "背部";
		}
		if (ContainsAny(value, "tail", "尾"))
		{
			if (!KillPolicy.IsChinese(language))
			{
				return "Tail";
			}
			return "尾部";
		}
		if (ContainsAny(value, "tentacle", "触手"))
		{
			if (!KillPolicy.IsChinese(language))
			{
				return "Tentacle";
			}
			return "触手";
		}
		if (ContainsAny(value, "arm", "hand", "臂", "手"))
		{
			if (!KillPolicy.IsChinese(language))
			{
				return "Arm";
			}
			return "手臂";
		}
		if (ContainsAny(value, "leg", "foot", "腿", "足"))
		{
			if (!KillPolicy.IsChinese(language))
			{
				return "Leg";
			}
			return "腿部";
		}
		if (ContainsAny(value, "spore", "孢"))
		{
			if (!KillPolicy.IsChinese(language))
			{
				return "Spore";
			}
			return "孢子";
		}
		if (ContainsAny(value, "sac", "bladder", "pustule", "囊"))
		{
			if (!KillPolicy.IsChinese(language))
			{
				return "Sac";
			}
			return "囊体";
		}
		return GenericWeakspotPart(language);
	}

	internal static string WeakspotSuffix(string part, bool enabled)
	{
		if (!enabled || string.IsNullOrWhiteSpace(part))
		{
			return string.Empty;
		}
		return "[" + part.Trim() + "]";
	}

	internal static string WeakspotMultiplierTag(string part, float multiplier, string language)
	{
		if (string.IsNullOrWhiteSpace(part))
		{
			return string.Empty;
		}
		string text = ((KillPolicy.IsFinite(multiplier) && multiplier >= 0f) ? multiplier.ToString("0.##", (IFormatProvider)(object)CultureInfo.InvariantCulture) : "?");
		string text2 = (KillPolicy.IsChinese(language) ? string.Empty : " ");
		return string.Concat(new string[5]
		{
			"[",
			part.Trim(),
			text2,
			text,
			"×]"
		});
	}

	internal static string WeakspotTags(System.Collections.Generic.IEnumerable<WeakspotPartMultiplier> parts, string language)
	{
		if (parts == null)
		{
			return string.Empty;
		}
		List<string> val = new List<string>();
		HashSet<string> val2 = new HashSet<string>((IEqualityComparer<string>)(object)StringComparer.Ordinal);
		System.Collections.Generic.IEnumerator<WeakspotPartMultiplier> enumerator = parts.GetEnumerator();
		try
		{
			while (((System.Collections.IEnumerator)enumerator).MoveNext())
			{
				WeakspotPartMultiplier current = enumerator.Current;
				string text = WeakspotMultiplierTag(current.Part, current.Multiplier, language);
				if (text.Length > 0 && val2.Add(text))
				{
					val.Add(text);
				}
			}
		}
		finally
		{
			((System.IDisposable)enumerator)?.Dispose();
		}
		if (val.Count < 4)
		{
			return string.Concat((System.Collections.Generic.IEnumerable<string>)val);
		}
		List<string> val3 = new List<string>(4);
		for (int i = 0; i < 3; i++)
		{
			val3.Add(val[i]);
		}
		val3.Add("[+" + (val.Count - 3).ToString((IFormatProvider)(object)CultureInfo.InvariantCulture) + "]");
		return string.Concat((System.Collections.Generic.IEnumerable<string>)val3);
	}

	internal static string CfoamTag(float glueTolerance)
	{
		if (!KillPolicy.IsFinite(glueTolerance) || glueTolerance < 0f)
		{
			return string.Empty;
		}
		if (glueTolerance >= 1000000f)
		{
			return "[结沫*免疫]";
		}
		return "[结沫*" + ((int)Math.Ceiling((double)(glueTolerance / 3f))).ToString((IFormatProvider)(object)CultureInfo.InvariantCulture) + "]";
	}

	internal static string SelectEnemyTag(bool glueGunWielded, bool showWeakspotPart, string weakspotTag, string cfoamTag)
	{
		if (glueGunWielded)
		{
			return cfoamTag ?? string.Empty;
		}
		if (!showWeakspotPart)
		{
			return string.Empty;
		}
		return weakspotTag ?? string.Empty;
	}

	private static bool ContainsAny(string value, string first, string second, string third, string fourth, string fifth, string sixth)
	{
		if (!Contains(value, first) && !Contains(value, second) && !Contains(value, third) && !Contains(value, fourth) && !Contains(value, fifth))
		{
			return Contains(value, sixth);
		}
		return true;
	}

	private static bool ContainsAny(string value, string first, string second, string third, string fourth)
	{
		if (!Contains(value, first) && !Contains(value, second) && !Contains(value, third))
		{
			return Contains(value, fourth);
		}
		return true;
	}

	private static bool ContainsAny(string value, string first, string second, string third)
	{
		if (!Contains(value, first) && !Contains(value, second))
		{
			return Contains(value, third);
		}
		return true;
	}

	private static bool ContainsAny(string value, string first, string second)
	{
		if (!Contains(value, first))
		{
			return Contains(value, second);
		}
		return true;
	}

	private static bool Contains(string value, string token)
	{
		return value.IndexOf(token, (StringComparison)5) >= 0;
	}

	internal static bool TryParseHexColor(string value, out float red, out float green, out float blue, out float alpha)
	{
		red = (green = (blue = (alpha = 1f)));
		if (string.IsNullOrWhiteSpace(value))
		{
			return false;
		}
		string text = value.Trim();
		if (text.StartsWith("#", (StringComparison)4))
		{
			text = text.Substring(1);
		}
		if (text.Length != 6 && text.Length != 8)
		{
			return false;
		}
		if (!TryHexByte(text, 0, out var result) || !TryHexByte(text, 2, out var result2) || !TryHexByte(text, 4, out var result3))
		{
			return false;
		}
		byte result4 = 255;
		if (text.Length == 8 && !TryHexByte(text, 6, out result4))
		{
			return false;
		}
		red = (float)(int)result / 255f;
		green = (float)(int)result2 / 255f;
		blue = (float)(int)result3 / 255f;
		alpha = (float)(int)result4 / 255f;
		return true;
	}

	private static bool TryHexByte(string value, int offset, out byte result)
	{
		return byte.TryParse(value.Substring(offset, 2), (NumberStyles)515, (IFormatProvider)(object)CultureInfo.InvariantCulture, out result);
	}

	internal static float HeaderWidth(float barWidth, float nameWidth, float partWidth, float numberWidth, float padding, float spacing, float maxWidth)
	{
		int num = ((nameWidth > 0f) ? 1 : 0) + ((partWidth > 0f) ? 1 : 0) + ((numberWidth > 0f) ? 1 : 0);
		float num2 = Math.Max(0f, nameWidth) + Math.Max(0f, partWidth) + Math.Max(0f, numberWidth) + (float)Math.Max(0, num - 1) * Math.Max(0f, spacing) + Math.Max(0f, padding) * 2f;
		float num3 = Math.Max(Math.Max(0f, barWidth), num2);
		if (!KillPolicy.IsFinite(maxWidth) || !(maxWidth > 0f))
		{
			return num3;
		}
		return Math.Min(num3, maxWidth);
	}

	internal static float ResolutionScale(int screenHeight)
	{
		return FeedbackPolicy.Clamp((float)screenHeight / 1080f, 0.75f, 1.6f, 1f);
	}

	internal static float Width(float configured, int screenHeight, int screenWidth)
	{
		float num = FeedbackPolicy.Clamp(configured, 90f, 420f, 200f) * ResolutionScale(screenHeight);
		float num2 = ((screenWidth > 0) ? ((float)screenWidth * 0.9f) : num);
		if (num > num2)
		{
			num = num2;
		}
		if (!(num > 0f))
		{
			return 200f;
		}
		return num;
	}

	internal static float Height(float configured, int screenHeight)
	{
		return FeedbackPolicy.Clamp(configured, 2f, 16f, 5f) * ResolutionScale(screenHeight);
	}

	internal static float NumberSize(float configured, int screenHeight)
	{
		return FeedbackPolicy.Clamp(configured, 8f, 22f, 12f) * ResolutionScale(screenHeight);
	}

	internal static float HeadOffset(float configured)
	{
		return FeedbackPolicy.Clamp(configured, 0f, 2f, 0.12f);
	}

	internal static float VerticalOffset(float configured)
	{
		return FeedbackPolicy.Clamp(configured, -400f, 400f, -8f);
	}

	internal static float MaxDistance(float configured)
	{
		return FeedbackPolicy.Clamp(configured, 5f, 300f, 60f);
	}

	internal static float Opacity(float configured)
	{
		return FeedbackPolicy.Clamp(configured, 0.15f, 1f, 0.55f);
	}

	internal static float ScreenAnchorY(float fraction, int screenHeight)
	{
		return FeedbackPolicy.Clamp(fraction, 0.05f, 0.95f, 0.3f) * (float)screenHeight;
	}

	internal static float AnchorY(float anchoredY, int screenHeight, float minFraction, float maxFraction)
	{
		float num = FeedbackPolicy.Clamp(minFraction, 0.02f, 0.9f, 0.1f) * (float)screenHeight;
		float num2 = FeedbackPolicy.Clamp(maxFraction, 0.05f, 0.95f, 0.46f) * (float)screenHeight;
		if (num2 < num)
		{
			float num3 = num;
			num = num2;
			num2 = num3;
		}
		float num4 = (KillPolicy.IsFinite(anchoredY) ? anchoredY : num);
		if (num4 < num)
		{
			return num;
		}
		if (num4 > num2)
		{
			return num2;
		}
		return num4;
	}

	internal static float AnchorX(float centredX, float barWidth, int screenWidth, int screenHeight)
	{
		if (screenWidth <= 0)
		{
			return 0f;
		}
		float num = 12f * ResolutionScale(screenHeight);
		float num2 = barWidth * 0.5f;
		float num3 = num2 + num;
		float num4 = (float)screenWidth - num2 - num;
		float num5 = (KillPolicy.IsFinite(centredX) ? centredX : ((float)screenWidth * 0.5f));
		if (num4 < num3)
		{
			return (float)screenWidth * 0.5f;
		}
		if (num5 < num3)
		{
			return num3;
		}
		if (num5 > num4)
		{
			return num4;
		}
		return num5;
	}

	internal static float GhostDecay(float value, float floor, float deltaSeconds)
	{
		float num = (KillPolicy.IsFinite(floor) ? floor : 0f);
		if (!KillPolicy.IsFinite(value))
		{
			return num;
		}
		if (!KillPolicy.IsFinite(deltaSeconds) || deltaSeconds <= 0f)
		{
			if (!(value < num))
			{
				return value;
			}
			return num;
		}
		float num2 = value - 2.6f * deltaSeconds;
		if (!(num2 < num))
		{
			return num2;
		}
		return num;
	}

	internal static float AlphaFor(bool visible, float sinceShown, float sinceLost)
	{
		if (visible)
		{
			if (!KillPolicy.IsFinite(sinceShown) || sinceShown <= 0f)
			{
				return 0f;
			}
			float num = sinceShown / 0.1f;
			if (!(num >= 1f))
			{
				return num;
			}
			return 1f;
		}
		if (!KillPolicy.IsFinite(sinceLost) || sinceLost < 0f)
		{
			return 0f;
		}
		if (sinceLost <= 0.15f)
		{
			return 1f;
		}
		float num2 = (sinceLost - 0.15f) / 0.2f;
		if (!(num2 >= 1f))
		{
			return 1f - num2;
		}
		return 0f;
	}

	internal static bool ShouldDraw(bool haveTarget, bool hasAnchor, float alpha)
	{
		if (alpha > 0.002f)
		{
			return haveTarget || hasAnchor;
		}
		return false;
	}

	internal static float TickFraction(int index, int count)
	{
		if (count < 2 || index <= 0 || index >= count)
		{
			return -1f;
		}
		return (float)index / (float)count;
	}

	internal static bool ShouldShow(bool hasTarget, float distance, float maxDistance)
	{
		if (!hasTarget)
		{
			return false;
		}
		if (!KillPolicy.IsFinite(distance) || distance < 0f)
		{
			return false;
		}
		return distance <= MaxDistance(maxDistance) + 0.01f;
	}

	internal static bool IsHeadMode(string anchor)
	{
		return !string.Equals(anchor, "Screen", (StringComparison)5);
	}
}
