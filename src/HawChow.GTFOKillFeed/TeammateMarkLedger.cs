using System;
using System.Collections.Generic;

namespace HawChow.GTFOKillFeed;

internal sealed class TeammateMarkLedger
{
	internal sealed class Entry
	{
		internal long Key;

		internal bool Downed;

		internal bool Reviving;

		internal float SeenAt;
	}

	private readonly Dictionary<long, Entry> _entries = new Dictionary<long, Entry>();

	internal int Count => _entries.Count;

	internal System.Collections.Generic.Dictionary<long, Entry>.ValueCollection Values => _entries.Values;

	internal Entry GetOrAdd(long key, float now)
	{
		Entry result = default(Entry);
		if (_entries.TryGetValue(key, out result))
		{
			return result;
		}
		result = new Entry
		{
			Key = key,
			SeenAt = now
		};
		_entries[key] = result;
		return result;
	}

	internal void MarkSeen(long key, bool downed, bool reviving, float now)
	{
		Entry orAdd = GetOrAdd(key, now);
		orAdd.Downed = downed;
		orAdd.Reviving = reviving;
		orAdd.SeenAt = now;
	}

	internal int ForgetUnseen(float now, List<long> removed)
	{
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_0018: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_006e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0073: Unknown result type (might be due to invalid IL or missing references)
		removed?.Clear();
		int num = 0;
		List<long> val = null;
		var enumerator = _entries.GetEnumerator();
		try
		{
			while (enumerator.MoveNext())
			{
				KeyValuePair<long, Entry> current = enumerator.Current;
				if (!(now - current.Value.SeenAt <= 0.5f))
				{
					if (val == null)
					{
						val = new List<long>(2);
					}
					val.Add(current.Key);
				}
			}
		}
		finally
		{
			((System.IDisposable)enumerator).Dispose();
		}
		if (val == null)
		{
			return 0;
		}
		var enumerator2 = val.GetEnumerator();
		try
		{
			while (enumerator2.MoveNext())
			{
				long current2 = enumerator2.Current;
				_entries.Remove(current2);
				removed?.Add(current2);
				num++;
			}
			return num;
		}
		finally
		{
			((System.IDisposable)enumerator2).Dispose();
		}
	}

	internal void Clear()
	{
		_entries.Clear();
	}
}
