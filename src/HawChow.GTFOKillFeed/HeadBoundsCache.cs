using System;

namespace HawChow.GTFOKillFeed;

internal sealed class HeadBoundsCache
{
	private struct Slot
	{
		internal int Id;

		internal float At;

		internal float TopY;

		internal bool Filled;
	}

	internal const int MaxSlots = 16;

	internal const float RefreshSeconds = 0.35f;

	private readonly Slot[] _slots = new Slot[16];

	private int _slotCursor;

	private int _samples;

	private int _hits;

	internal int Samples => _samples;

	internal int Hits => _hits;

	internal int Filled
	{
		get
		{
			int num = 0;
			for (int i = 0; i < _slots.Length; i++)
			{
				if (_slots[i].Filled)
				{
					num++;
				}
			}
			return num;
		}
	}

	internal float Top(int id, float now, Func<float> measure)
	{
		if (measure == null)
		{
			return 0f;
		}
		if (id == 0 || id == -2147483648 || !KillPolicy.IsFinite(now))
		{
			return measure.Invoke();
		}
		int num = Find(id);
		if (num >= 0 && now >= _slots[num].At && now - _slots[num].At < 0.35f)
		{
			_hits++;
			return _slots[num].TopY;
		}
		float num2 = measure.Invoke();
		if (!KillPolicy.IsFinite(num2))
		{
			num2 = 0f;
		}
		_samples++;
		Store(id, now, num2);
		return num2;
	}

	internal bool HasFresh(int id, float now)
	{
		int num = Find(id);
		if (num >= 0 && KillPolicy.IsFinite(now) && now >= _slots[num].At)
		{
			return now - _slots[num].At < 0.35f;
		}
		return false;
	}

	internal void Clear()
	{
		for (int i = 0; i < _slots.Length; i++)
		{
			_slots[i] = default(Slot);
		}
		_slotCursor = 0;
		_samples = 0;
		_hits = 0;
	}

	private int Find(int id)
	{
		for (int i = 0; i < _slots.Length; i++)
		{
			if (_slots[i].Filled && _slots[i].Id == id)
			{
				return i;
			}
		}
		return -1;
	}

	private void Store(int id, float now, float value)
	{
		int num = Find(id);
		if (num < 0)
		{
			num = _slotCursor;
			_slotCursor = (_slotCursor + 1) % _slots.Length;
		}
		_slots[num] = new Slot
		{
			Id = id,
			At = now,
			TopY = value,
			Filled = true
		};
	}
}
