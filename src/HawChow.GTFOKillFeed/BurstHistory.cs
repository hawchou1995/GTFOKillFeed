using System;
using System.Collections.Generic;

namespace HawChow.GTFOKillFeed;

internal sealed class BurstHistory
{
	private readonly Dictionary<long, Queue<ValueTuple<float, float>>> _entries = new Dictionary<long, Queue<ValueTuple<float, float>>>();

	internal void Add(long id, float now, float damage)
	{
		//IL_007f: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a5: Unknown result type (might be due to invalid IL or missing references)
		//IL_009c: Unknown result type (might be due to invalid IL or missing references)
		//IL_006e: Unknown result type (might be due to invalid IL or missing references)
		if (id == 0L || !KillPolicy.IsFinite(now) || !KillPolicy.IsFinite(damage) || damage <= 0f)
		{
			return;
		}
		Queue<ValueTuple<float, float>> val = default(Queue<ValueTuple<float, float>>);
		if (!_entries.TryGetValue(id, out val))
		{
			if (_entries.Count >= 256)
			{
				Prune(now);
			}
			if (_entries.Count >= 256)
			{
				return;
			}
			val = (_entries[id] = new Queue<ValueTuple<float, float>>());
		}
		while (val.Count > 0 && now - val.Peek().Item1 > 0.65f)
		{
			val.Dequeue();
		}
		if (val.Count >= 96)
		{
			val.Dequeue();
		}
		val.Enqueue(new ValueTuple<float, float>(now, damage));
	}

	internal float Sum(long id, float now)
	{
		//IL_0041: Unknown result type (might be due to invalid IL or missing references)
		//IL_0046: Unknown result type (might be due to invalid IL or missing references)
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		//IL_004b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0050: Unknown result type (might be due to invalid IL or missing references)
		//IL_0052: Unknown result type (might be due to invalid IL or missing references)
		Queue<ValueTuple<float, float>> val = default(Queue<ValueTuple<float, float>>);
		if (!_entries.TryGetValue(id, out val))
		{
			return 0f;
		}
		while (val.Count > 0 && now - val.Peek().Item1 > 0.65f)
		{
			val.Dequeue();
		}
		float num = 0f;
		var enumerator = val.GetEnumerator();
		try
		{
			while (enumerator.MoveNext())
			{
				num += enumerator.Current.Item2;
			}
			return num;
		}
		finally
		{
			((System.IDisposable)enumerator).Dispose();
		}
	}

	internal void Prune(float now)
	{
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_0016: Unknown result type (might be due to invalid IL or missing references)
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0079: Unknown result type (might be due to invalid IL or missing references)
		//IL_007e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0038: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		List<long> val = new List<long>();
		var enumerator = _entries.GetEnumerator();
		try
		{
			while (enumerator.MoveNext())
			{
				KeyValuePair<long, Queue<ValueTuple<float, float>>> current = enumerator.Current;
				Queue<ValueTuple<float, float>> value = current.Value;
				while (value.Count > 0 && now - value.Peek().Item1 > 0.65f)
				{
					value.Dequeue();
				}
				if (value.Count == 0)
				{
					val.Add(current.Key);
				}
			}
		}
		finally
		{
			((System.IDisposable)enumerator).Dispose();
		}
		var enumerator2 = val.GetEnumerator();
		try
		{
			while (enumerator2.MoveNext())
			{
				long current2 = enumerator2.Current;
				_entries.Remove(current2);
			}
		}
		finally
		{
			((System.IDisposable)enumerator2).Dispose();
		}
	}

	internal void Remove(long id)
	{
		_entries.Remove(id);
	}

	internal void Clear()
	{
		_entries.Clear();
	}
}
