using System;

namespace HawChow.GTFOKillFeed;

internal sealed class KillIconQueue
{
	internal const int PreviewSlots = 9;

	private readonly KillIconSlot[] _real;

	private readonly KillIconSlot[] _preview = new KillIconSlot[9];

	private int _realCount;

	private int _previewCount;

	private int _nextToken;

	internal int Capacity => _real.Length;

	internal int RealCount => _realCount;

	internal int PreviewCount => _previewCount;

	internal int Count => _realCount + _previewCount;

	internal KillIconQueue(int capacity)
	{
		_real = new KillIconSlot[Math.Max(1, capacity)];
	}

	internal int NextToken()
	{
		int num = ++_nextToken;
		if (num <= 0)
		{
			num = ++_nextToken;
		}
		return num;
	}

	internal KillIconSlot RealAt(int index)
	{
		return _real[index];
	}

	internal KillIconSlot PreviewAt(int index)
	{
		return _preview[index];
	}

	internal void PushReal(in KillIconSlot slot, int configuredMax)
	{
		int num = Math.Min(Math.Max(1, configuredMax), _real.Length);
		if (_realCount >= num)
		{
			EvictOldestReal();
		}
		if (_realCount < num)
		{
			_real[_realCount++] = slot;
		}
	}

	internal void RemoveRealAt(int index)
	{
		if (index >= 0 && index < _realCount)
		{
			for (int i = index; i < _realCount - 1; i++)
			{
				_real[i] = _real[i + 1];
			}
			_realCount--;
		}
	}

	internal void ResetPreviews()
	{
		_previewCount = 0;
	}

	internal bool PushPreview(in KillIconSlot slot)
	{
		if (_previewCount >= 9)
		{
			return false;
		}
		_preview[_previewCount++] = slot;
		return true;
	}

	internal void RemovePreviewAt(int index)
	{
		if (index >= 0 && index < _previewCount)
		{
			for (int i = index; i < _previewCount - 1; i++)
			{
				_preview[i] = _preview[i + 1];
			}
			_previewCount--;
		}
	}

	internal int TickReal(float now, float lifetime)
	{
		if (!KillPolicy.IsFinite(now))
		{
			return 0;
		}
		float num = KillIconPolicy.Lifetime(lifetime);
		int num2 = 0;
		for (int num3 = _realCount - 1; num3 >= 0; num3--)
		{
			if (!(now - _real[num3].At < num))
			{
				RemoveRealAt(num3);
				num2++;
			}
		}
		return num2;
	}

	internal int TickPreview(float now, float lifetime)
	{
		if (!KillPolicy.IsFinite(now))
		{
			return 0;
		}
		float num = KillIconPolicy.Lifetime(lifetime);
		int num2 = 0;
		for (int num3 = _previewCount - 1; num3 >= 0; num3--)
		{
			if (!(now - _preview[num3].At < num))
			{
				RemovePreviewAt(num3);
				num2++;
			}
		}
		return num2;
	}

	internal bool TryFind(int token, out int index, out bool preview)
	{
		index = -1;
		preview = false;
		if (token <= 0)
		{
			return false;
		}
		for (int i = 0; i < _realCount; i++)
		{
			if (_real[i].Token == token)
			{
				index = i;
				return true;
			}
		}
		for (int j = 0; j < _previewCount; j++)
		{
			if (_preview[j].Token == token)
			{
				index = j;
				preview = true;
				return true;
			}
		}
		return false;
	}

	internal void Clear()
	{
		_realCount = 0;
		_previewCount = 0;
	}

	private void EvictOldestReal()
	{
		if (_realCount == 0)
		{
			return;
		}
		int num = 0;
		for (int i = 1; i < _realCount; i++)
		{
			if (_real[i].At < _real[num].At)
			{
				num = i;
			}
		}
		RemoveRealAt(num);
	}
}
