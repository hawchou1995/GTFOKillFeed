using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace HawChow.GTFOKillFeed;

internal sealed class LogTally
{
	private readonly Dictionary<string, int> _counts = new Dictionary<string, int>((IEqualityComparer<string>)(object)StringComparer.Ordinal);

	internal int Total
	{
		get;
		private set;
	}

	internal int Count(string reason)
	{
		int result = default(int);
		if (reason == null || !_counts.TryGetValue(reason, out result))
		{
			return 0;
		}
		return result;
	}

	internal int Add(string reason)
	{
		string text = (string.IsNullOrEmpty(reason) ? "unknown" : reason);
		int num = default(int);
		_counts.TryGetValue(text, out num);
		num = (_counts[text] = num + 1);
		Total++;
		return num;
	}

	internal static bool ShouldLog(int seen, int loud, int stride)
	{
		if (seen > 0)
		{
			if (seen > loud)
			{
				if (stride > 0)
				{
					return seen % stride == 0;
				}
				return false;
			}
			return true;
		}
		return false;
	}

	internal void Reset()
	{
		_counts.Clear();
		Total = 0;
	}
}
