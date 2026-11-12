using System;
using System.Collections;
using System.Collections.Generic;

namespace HawChow.GTFOKillFeed;

internal static class EnemyNamePolicy
{
	internal static string Resolve(uint id, string rawName, System.Collections.Generic.IReadOnlyList<EnemyNameRule> rules, IReadOnlyDictionary<uint, string> defaults = null)
	{
		rawName = (string.IsNullOrWhiteSpace(rawName) ? "Unknown" : rawName);
		if (rules == null)
		{
			rules = System.Array.Empty<EnemyNameRule>();
		}
		System.Collections.Generic.IEnumerator<EnemyNameRule> enumerator = ((System.Collections.Generic.IEnumerable<EnemyNameRule>)rules).GetEnumerator();
		try
		{
			while (((System.Collections.IEnumerator)enumerator).MoveNext())
			{
				EnemyNameRule current = enumerator.Current;
				if (Valid(current) && current.DataBlockId == id && current.DataBlockId != 0 && string.Equals(current.RawName, rawName, (StringComparison)4))
				{
					return current.DisplayName;
				}
			}
		}
		finally
		{
			((System.IDisposable)enumerator)?.Dispose();
		}
		enumerator = ((System.Collections.Generic.IEnumerable<EnemyNameRule>)rules).GetEnumerator();
		try
		{
			while (((System.Collections.IEnumerator)enumerator).MoveNext())
			{
				EnemyNameRule current2 = enumerator.Current;
				if (Valid(current2) && current2.DataBlockId == 0 && string.Equals(current2.RawName, rawName, (StringComparison)4))
				{
					return current2.DisplayName;
				}
			}
		}
		finally
		{
			((System.IDisposable)enumerator)?.Dispose();
		}
		string text = default(string);
		if (defaults == null || !defaults.TryGetValue(id, out text) || string.IsNullOrWhiteSpace(text))
		{
			return rawName;
		}
		return text;
	}

	internal static bool Valid(EnemyNameRule rule)
	{
		if (rule != null && !string.IsNullOrWhiteSpace(rule.RawName) && !string.IsNullOrWhiteSpace(rule.DisplayName) && rule.RawName.Length <= 256 && rule.DisplayName.Length <= 96 && !rule.DisplayName.Contains('\n'))
		{
			return !rule.DisplayName.Contains('\r');
		}
		return false;
	}
}
