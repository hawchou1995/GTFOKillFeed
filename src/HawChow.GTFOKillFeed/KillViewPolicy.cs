using System;
using System.Collections.Generic;
using System.IO;

namespace HawChow.GTFOKillFeed;

internal enum KillDisplayLegacy
{
	Head,
	Card
}

internal enum SwitchOrigin
{
	Default,
	Legacy,
	Configured,
	LegacyFallback,
	InvalidFallback
}

internal enum ViewMode
{
	Unknown,
	Explicit,
	Legacy
}

internal readonly struct KillViewSwitches
{
	internal readonly bool HeadEnabled;

	internal readonly bool CardEnabled;

	internal readonly SwitchOrigin HeadOrigin;

	internal readonly SwitchOrigin CardOrigin;

	internal readonly bool HeadKeyPresent;

	internal readonly bool CardKeyPresent;

	internal readonly bool LegacyKeyPresent;

	internal readonly ViewMode Mode;

	internal readonly KillDisplayLegacy LegacyKind;

	internal readonly string ModeToken;

	internal readonly bool WriteHead;

	internal readonly bool WriteCard;

	internal readonly bool WriteMode;

	internal KillViewSwitches(bool headEnabled, bool cardEnabled, SwitchOrigin headOrigin, SwitchOrigin cardOrigin, bool headKeyPresent, bool cardKeyPresent, bool legacyKeyPresent, ViewMode mode, KillDisplayLegacy legacyKind, string modeToken, bool writeHead, bool writeCard, bool writeMode)
	{
		HeadEnabled = headEnabled;
		CardEnabled = cardEnabled;
		HeadOrigin = headOrigin;
		CardOrigin = cardOrigin;
		HeadKeyPresent = headKeyPresent;
		CardKeyPresent = cardKeyPresent;
		LegacyKeyPresent = legacyKeyPresent;
		Mode = mode;
		LegacyKind = legacyKind;
		ModeToken = modeToken;
		WriteHead = writeHead;
		WriteCard = writeCard;
		WriteMode = writeMode;
	}
}

internal sealed class KillViewConfigFile
{
	private readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	private KillViewConfigFile()
	{
	}

	internal int Count => _values.Count;

	internal static KillViewConfigFile Read(string path)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
			{
				return new KillViewConfigFile();
			}
			return Parse(File.ReadAllText(path));
		}
		catch (System.Exception)
		{
			return new KillViewConfigFile();
		}
	}

	internal static KillViewConfigFile Parse(string text)
	{
		KillViewConfigFile file = new KillViewConfigFile();
		if (text == null)
		{
			return file;
		}
		if (text.Length > 0 && text[0] == '\uFEFF')
		{
			text = text.Substring(1);
		}
		string section = string.Empty;
		string[] lines = text.Split('\n');
		for (int i = 0; i < lines.Length; i++)
		{
			string line = lines[i].TrimEnd('\r').Trim();
			if (line.Length == 0 || line[0] == '#' || line[0] == ';')
			{
				continue;
			}
			if (line[0] == '[')
			{
				int end = line.IndexOf(']');
				section = ((end > 0) ? line.Substring(1, end - 1).Trim() : string.Empty);
				continue;
			}
			int eq = line.IndexOf('=');
			if (eq <= 0)
			{
				continue;
			}
			string key = line.Substring(0, eq).Trim();
			if (key.Length == 0)
			{
				continue;
			}
			file._values[Compose(section, key)] = line.Substring(eq + 1).Trim();
		}
		return file;
	}

	internal bool Has(string section, string key)
	{
		return _values.ContainsKey(Compose(section, key));
	}

	internal string Raw(string section, string key)
	{
		if (_values.TryGetValue(Compose(section, key), out string value))
		{
			return value;
		}
		return null;
	}

	private static string Compose(string section, string key)
	{
		return (section ?? string.Empty) + "\u0001" + (key ?? string.Empty);
	}
}

internal enum PlacementReason
{
	Unchanged,
	Lifted,
	HiddenNoRoom,
	HiddenInvalidIcon,
	HiddenUnstable
}

internal readonly struct ScreenBox
{
	internal readonly float Left;

	internal readonly float Top;

	internal readonly float Right;

	internal readonly float Bottom;

	internal ScreenBox(float left, float top, float right, float bottom)
	{
		Left = left;
		Top = top;
		Right = right;
		Bottom = bottom;
	}

	internal bool IsUsable
	{
		get
		{
			if (!KillViewPolicy.IsFinite(Left) || !KillViewPolicy.IsFinite(Top) || !KillViewPolicy.IsFinite(Right) || !KillViewPolicy.IsFinite(Bottom))
			{
				return false;
			}
			if (!(Right > Left))
			{
				return false;
			}
			return Bottom > Top;
		}
	}

	internal float Width => Right - Left;

	internal float Height => Bottom - Top;

	internal float CentreX => (Left + Right) * 0.5f;
}

internal readonly struct IconPlacement
{
	internal readonly float Top;

	internal readonly bool Hidden;

	internal readonly float LiftPx;

	internal readonly int Occluders;

	internal readonly PlacementReason Reason;

	internal IconPlacement(float top, bool hidden, float liftPx, int occluders, PlacementReason reason)
	{
		Top = top;
		Hidden = hidden;
		LiftPx = liftPx;
		Occluders = occluders;
		Reason = reason;
	}
}

internal static class KillViewPolicy
{
	internal const bool DefaultHeadEnabled = false;

	internal const bool DefaultCardEnabled = true;

	internal const float DefaultOpacity = 0.5f;

	internal const float MinOpacity = 0f;

	internal const float MaxOpacity = 1f;

	internal const float AvoidanceMarginPixels = 2f;

	internal const string SectionCard = "Card";

	internal const string SectionIcon = "KillIcon";

	internal const string KeyCardEnabled = "Enabled";

	internal const string KeyHeadEnabled = "HeadEnabled";

	internal const string KeyHeadOpacity = "Opacity";

	internal const string KeyLegacyDisplay = "Display";

	internal const string KeyViewMode = "ViewMode";

	internal const string ModeExplicit = "Explicit";

	internal const string ModeLegacyPrefix = "Legacy:";

	internal const int MaxAvoidanceIterations = 4;

	internal static bool IsFinite(float value)
	{
		if (!float.IsNaN(value))
		{
			return !float.IsInfinity(value);
		}
		return false;
	}

	internal static float Opacity(float configured)
	{
		if (!IsFinite(configured))
		{
			return DefaultOpacity;
		}
		return Math.Max(MinOpacity, Math.Min(MaxOpacity, configured));
	}

	internal static float CompositeAlpha(float lifecycleAlpha, float configuredOpacity)
	{
		float fade = (IsFinite(lifecycleAlpha) ? Math.Max(0f, Math.Min(1f, lifecycleAlpha)) : 0f);
		return fade * Opacity(configuredOpacity);
	}

	internal static bool HeadVisible(bool headEnabled, bool gameVisible, bool cameraReady)
	{
		return headEnabled && gameVisible && cameraReady;
	}

	internal static bool CardVisible(bool cardEnabled, bool gameVisible)
	{
		return cardEnabled && gameVisible;
	}

	internal static KillDisplayLegacy ParseLegacyDisplay(string raw)
	{
		if (!string.IsNullOrWhiteSpace(raw))
		{
			string text = raw.Trim();
			if (string.Equals(text, "Card", StringComparison.OrdinalIgnoreCase) || string.Equals(text, "Cards", StringComparison.OrdinalIgnoreCase))
			{
				return KillDisplayLegacy.Card;
			}
		}
		return KillDisplayLegacy.Head;
	}

	internal static bool TryParseBool(string raw, out bool value)
	{
		value = false;
		if (raw == null)
		{
			return false;
		}
		string text = raw.Trim();
		if (text.Length == 0)
		{
			return false;
		}
		if (bool.TryParse(text, out value))
		{
			return true;
		}
		value = false;
		return false;
	}

	internal static ViewMode ParseMode(string raw, out KillDisplayLegacy synced)
	{
		synced = KillDisplayLegacy.Head;
		if (string.IsNullOrWhiteSpace(raw))
		{
			return ViewMode.Unknown;
		}
		string text = raw.Trim();
		if (string.Equals(text, "Explicit", StringComparison.OrdinalIgnoreCase))
		{
			return ViewMode.Explicit;
		}
		if (text.StartsWith("Legacy:", StringComparison.OrdinalIgnoreCase))
		{
			string suffix = text.Substring(7).Trim();
			if (string.Equals(suffix, "Head", StringComparison.OrdinalIgnoreCase))
			{
				synced = KillDisplayLegacy.Head;
				return ViewMode.Legacy;
			}
			if (string.Equals(suffix, "Card", StringComparison.OrdinalIgnoreCase) || string.Equals(suffix, "Cards", StringComparison.OrdinalIgnoreCase))
			{
				synced = KillDisplayLegacy.Card;
				return ViewMode.Legacy;
			}
		}
		return ViewMode.Unknown;
	}

	internal static string LegacyToken(KillDisplayLegacy kind)
	{
		return "Legacy:" + ((kind == KillDisplayLegacy.Card) ? "Card" : "Head");
	}

	private static KillViewSwitches ExplicitResult(bool headPresent, bool headValid, bool headRaw, bool cardPresent, bool cardValid, bool cardRaw, bool legacyPresent, KillDisplayLegacy fallbackKind, KillDisplayLegacy legacyKind, bool modePresent, string modeRaw)
	{
		bool fallbackHead = fallbackKind == KillDisplayLegacy.Head;
		bool fallbackCard = fallbackKind == KillDisplayLegacy.Card;
		bool head = (headValid ? headRaw : (legacyPresent ? fallbackHead : DefaultHeadEnabled));
		bool card = (cardValid ? cardRaw : (legacyPresent ? fallbackCard : DefaultCardEnabled));
		SwitchOrigin headOrigin;
		if (headValid)
		{
			headOrigin = SwitchOrigin.Configured;
		}
		else if (headPresent)
		{
			headOrigin = (legacyPresent ? SwitchOrigin.LegacyFallback : SwitchOrigin.InvalidFallback);
		}
		else
		{
			headOrigin = (legacyPresent ? SwitchOrigin.LegacyFallback : SwitchOrigin.Default);
		}
		SwitchOrigin cardOrigin;
		if (cardValid)
		{
			cardOrigin = SwitchOrigin.Configured;
		}
		else if (cardPresent)
		{
			cardOrigin = (legacyPresent ? SwitchOrigin.LegacyFallback : SwitchOrigin.InvalidFallback);
		}
		else
		{
			cardOrigin = (legacyPresent ? SwitchOrigin.LegacyFallback : SwitchOrigin.Default);
		}
		bool writeMode = !modePresent || !string.Equals((modeRaw == null) ? null : modeRaw.Trim(), ModeExplicit, StringComparison.OrdinalIgnoreCase);
		return new KillViewSwitches(head, card, headOrigin, cardOrigin, headPresent, cardPresent, legacyPresent, ViewMode.Explicit, legacyKind, ModeExplicit, !headPresent, !cardPresent, writeMode);
	}

	private static KillViewSwitches LegacyResult(KillDisplayLegacy legacyKind, bool headPresent, bool headValid, bool headRaw, bool cardPresent, bool cardValid, bool cardRaw, bool modePresent, string modeRaw)
	{
		bool head = (legacyKind == KillDisplayLegacy.Head);
		bool card = (legacyKind == KillDisplayLegacy.Card);
		bool writeHead = !headPresent || !headValid || headRaw != head;
		bool writeCard = !cardPresent || !cardValid || cardRaw != card;
		string token = LegacyToken(legacyKind);
		bool writeMode = !modePresent || !string.Equals((modeRaw == null) ? null : modeRaw.Trim(), token, StringComparison.OrdinalIgnoreCase);
		return new KillViewSwitches(head, card, SwitchOrigin.Legacy, SwitchOrigin.Legacy, headPresent, cardPresent, true, ViewMode.Legacy, legacyKind, token, writeHead, writeCard, writeMode);
	}

	internal static KillViewSwitches ResolveSwitches(KillViewConfigFile file)
	{
		bool legacyPresent = file != null && file.Has(SectionIcon, KeyLegacyDisplay);
		KillDisplayLegacy legacyKind = (legacyPresent ? ParseLegacyDisplay(file.Raw(SectionIcon, KeyLegacyDisplay)) : KillDisplayLegacy.Head);
		bool legacyHead = legacyKind == KillDisplayLegacy.Head;
		bool legacyCard = legacyKind == KillDisplayLegacy.Card;
		bool headPresent = file != null && file.Has(SectionIcon, KeyHeadEnabled);
		bool cardPresent = file != null && file.Has(SectionCard, KeyCardEnabled);
		bool headRaw = false;
		bool cardRaw = false;
		bool headValid = headPresent && TryParseBool(file.Raw(SectionIcon, KeyHeadEnabled), out headRaw);
		bool cardValid = cardPresent && TryParseBool(file.Raw(SectionCard, KeyCardEnabled), out cardRaw);
		bool modePresent = file != null && file.Has(SectionIcon, KeyViewMode);
		string modeRaw = (modePresent ? file.Raw(SectionIcon, KeyViewMode) : null);
		ViewMode mode = ParseMode(modeRaw, out KillDisplayLegacy synced);
		if (mode == ViewMode.Legacy)
		{
			bool syncedHead = synced == KillDisplayLegacy.Head;
			bool headMatches = !headPresent || (headValid && headRaw == syncedHead);
			bool cardMatches = !cardPresent || (cardValid && cardRaw == !syncedHead);
			if (headMatches && cardMatches)
			{
				return LegacyResult(legacyKind, headPresent, headValid, headRaw, cardPresent, cardValid, cardRaw, modePresent, modeRaw);
			}
			return ExplicitResult(headPresent, headValid, headRaw, cardPresent, cardValid, cardRaw, true, synced, legacyKind, modePresent, modeRaw);
		}
		if (mode == ViewMode.Explicit)
		{
			return ExplicitResult(headPresent, headValid, headRaw, cardPresent, cardValid, cardRaw, legacyPresent, legacyKind, legacyKind, modePresent, modeRaw);
		}
		if (!legacyPresent)
		{
			return ExplicitResult(headPresent, headValid, headRaw, cardPresent, cardValid, cardRaw, false, KillDisplayLegacy.Head, legacyKind, modePresent, modeRaw);
		}
		bool bothAbsent = !headPresent && !cardPresent;
		bool matchesLegacy = headPresent && cardPresent && headValid && cardValid && headRaw == legacyHead && cardRaw == legacyCard;
		if (bothAbsent || matchesLegacy)
		{
			return LegacyResult(legacyKind, headPresent, headValid, headRaw, cardPresent, cardValid, cardRaw, modePresent, modeRaw);
		}
		return ExplicitResult(headPresent, headValid, headRaw, cardPresent, cardValid, cardRaw, legacyPresent, legacyKind, legacyKind, modePresent, modeRaw);
	}

	internal static bool Overlaps(ScreenBox left, ScreenBox right)
	{
		if (!left.IsUsable || !right.IsUsable)
		{
			return false;
		}
		if (left.Left < right.Right && right.Left < left.Right)
		{
			return left.Top < right.Bottom && right.Top < left.Bottom;
		}
		return false;
	}

	internal static bool ShouldConsiderCandidate(bool isEnemy, bool hasUsableBounds)
	{
		return isEnemy && hasUsableBounds;
	}

	internal static bool TryAppendCandidate(ScreenBox[] into, ref int count, bool isEnemy, ScreenBox box)
	{
		if (!ShouldConsiderCandidate(isEnemy, box.IsUsable))
		{
			return true;
		}
		if (into == null || count < 0 || count >= into.Length)
		{
			return false;
		}
		into[count++] = box;
		return true;
	}

	private static bool TryHighestOverlap(ScreenBox icon, ScreenBox[] occluders, int count, out float highestTop, out int relevant)
	{
		highestTop = 1f / 0f;
		relevant = 0;
		if (occluders == null || count <= 0)
		{
			return false;
		}
		int limit = Math.Min(count, occluders.Length);
		for (int i = 0; i < limit; i++)
		{
			ScreenBox box = occluders[i];
			if (Overlaps(icon, box))
			{
				relevant++;
				if (box.Top < highestTop)
				{
					highestTop = box.Top;
				}
			}
		}
		return relevant > 0;
	}

	internal static IconPlacement ResolvePlacement(ScreenBox icon, ScreenBox[] occluders, int count, float screenTop, float screenHeight)
	{
		if (!icon.IsUsable)
		{
			return new IconPlacement(icon.Top, hidden: true, 0f, 0, PlacementReason.HiddenInvalidIcon);
		}
		if (!IsFinite(screenHeight) || !(screenHeight > 0f))
		{
			return new IconPlacement(icon.Top, hidden: true, 0f, 0, PlacementReason.HiddenInvalidIcon);
		}
		float topLimit = (IsFinite(screenTop) ? screenTop : 0f);
		ScreenBox current = icon;
		int total = 0;
		for (int step = 0; step <= MaxAvoidanceIterations; step++)
		{
			if (!TryHighestOverlap(current, occluders, count, out float highestTop, out int relevant))
			{
				bool lifted = current.Top < icon.Top;
				return new IconPlacement(current.Top, hidden: false, icon.Top - current.Top, total, lifted ? PlacementReason.Lifted : PlacementReason.Unchanged);
			}
			if (step == MaxAvoidanceIterations)
			{
				return new IconPlacement(icon.Top, hidden: true, 0f, total, PlacementReason.HiddenUnstable);
			}
			total += relevant;
			float top = highestTop - AvoidanceMarginPixels - current.Height;
			if (!IsFinite(top))
			{
				return new IconPlacement(icon.Top, hidden: true, 0f, total, PlacementReason.HiddenInvalidIcon);
			}
			if (top < topLimit)
			{
				return new IconPlacement(icon.Top, hidden: true, icon.Top - top, total, PlacementReason.HiddenNoRoom);
			}
			current = new ScreenBox(current.Left, top, current.Right, top + current.Height);
		}
		return new IconPlacement(icon.Top, hidden: true, 0f, total, PlacementReason.HiddenUnstable);
	}

	internal static bool ProjectBox(float[] xs, float[] ys, float[] zs, int count, float nearClip, float screenWidth, float screenHeight, out ScreenBox box)
	{
		box = default(ScreenBox);
		if (xs == null || ys == null || zs == null)
		{
			return false;
		}
		if (!IsFinite(screenWidth) || !IsFinite(screenHeight) || !(screenWidth > 0f) || !(screenHeight > 0f))
		{
			return false;
		}
		float clip = (IsFinite(nearClip) && nearClip > 0f) ? nearClip : 0f;
		int limit = Math.Min(count, Math.Min(xs.Length, Math.Min(ys.Length, zs.Length)));
		float left = 1f / 0f;
		float top = 1f / 0f;
		float right = -1f / 0f;
		float bottom = -1f / 0f;
		int valid = 0;
		for (int i = 0; i < limit; i++)
		{
			float z = zs[i];
			if (!IsFinite(z) || !(z > clip))
			{
				continue;
			}
			float x = xs[i];
			float y = ys[i];
			if (!IsFinite(x) || !IsFinite(y))
			{
				continue;
			}
			valid++;
			if (x < left)
			{
				left = x;
			}
			if (x > right)
			{
				right = x;
			}
			if (y < top)
			{
				top = y;
			}
			if (y > bottom)
			{
				bottom = y;
			}
		}
		if (valid == 0)
		{
			return false;
		}
		left = Math.Max(0f, Math.Min(screenWidth, left));
		right = Math.Max(0f, Math.Min(screenWidth, right));
		top = Math.Max(0f, Math.Min(screenHeight, top));
		bottom = Math.Max(0f, Math.Min(screenHeight, bottom));
		if (!(right > left) || !(bottom > top))
		{
			return false;
		}
		box = new ScreenBox(left, top, right, bottom);
		return true;
	}
}
