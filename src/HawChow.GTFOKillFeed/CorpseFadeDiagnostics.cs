using System.Collections.Generic;

namespace HawChow.GTFOKillFeed;

internal sealed class CorpseFadeDiagnostics
{
	internal const int MaxLinesPerLevel = 24;

	internal const int MaxWarningsPerLevel = 4;

	internal const int MaxPerReason = 3;

	private readonly Dictionary<string, int> _reasons = new Dictionary<string, int>();

	private int _lines;

	private int _warnings;

	internal int Lines => _lines;

	internal int Warnings => _warnings;

	internal int Count(string reason)
	{
		int result = default(int);
		if (reason == null || !_reasons.TryGetValue(reason, out result))
		{
			return 0;
		}
		return result;
	}

	internal bool AllowLine(string reason)
	{
		if (_lines >= 24)
		{
			return false;
		}
		string text = (string.IsNullOrEmpty(reason) ? "unknown" : reason);
		int num = Count(text);
		if (num >= 3)
		{
			return false;
		}
		_reasons[text] = num + 1;
		_lines++;
		return true;
	}

	internal bool AllowWarning()
	{
		if (_warnings >= 4)
		{
			return false;
		}
		_warnings++;
		return true;
	}

	internal void Reset()
	{
		_reasons.Clear();
		_lines = 0;
		_warnings = 0;
	}
}
