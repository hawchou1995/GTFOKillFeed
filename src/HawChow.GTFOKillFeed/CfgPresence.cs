using System;
using System.Collections.Generic;
using System.Globalization;

namespace HawChow.GTFOKillFeed;

internal static class CfgPresence
{
	internal static Dictionary<string, string> Parse(string text)
	{
		Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.Ordinal);
		if (string.IsNullOrEmpty(text))
		{
			return map;
		}
		string section = string.Empty;
		int index = ((text[0] == '\uFEFF') ? 1 : 0);
		while (index < text.Length)
		{
			int end = text.IndexOf('\n', index);
			if (end < 0)
			{
				end = text.Length;
			}
			string line = text.Substring(index, end - index).Trim();
			index = end + 1;
			if (line.Length == 0 || line[0] == '#')
			{
				continue;
			}
			if (line[0] == '[')
			{
				int close = line.IndexOf(']');
				if (close > 1)
				{
					section = line.Substring(1, close - 1).Trim();
				}
				continue;
			}
			int equals = line.IndexOf('=');
			if (equals <= 0)
			{
				continue;
			}
			string key = line.Substring(0, equals).Trim();
			if (key.Length == 0)
			{
				continue;
			}
			map[Key(section, key)] = line.Substring(equals + 1).Trim();
		}
		return map;
	}

	internal static string Key(string section, string key)
	{
		return section + "/" + key;
	}

	internal static bool TryRaw(IDictionary<string, string> map, string section, string key, out string raw)
	{
		raw = null;
		if (map == null)
		{
			return false;
		}
		return map.TryGetValue(Key(section, key), out raw) && raw != null;
	}

	internal static bool TryBool(IDictionary<string, string> map, string section, string key, out bool value)
	{
		value = false;
		if (!TryRaw(map, section, key, out string raw))
		{
			return false;
		}
		if (string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase) || raw == "1")
		{
			value = true;
			return true;
		}
		if (string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase) || raw == "0")
		{
			value = false;
			return true;
		}
		return false;
	}

	internal static bool TryFloat(IDictionary<string, string> map, string section, string key, out float value)
	{
		value = 0f;
		if (!TryRaw(map, section, key, out string raw))
		{
			return false;
		}
		if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
		{
			value = 0f;
			return false;
		}
		return true;
	}
}
