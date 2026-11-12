using System;
using System.Collections.Generic;

namespace HawChow.GTFOKillFeed;

internal sealed class ShotgunDamageNumberBatch
{
	private sealed class Accumulator
	{
		internal readonly long EnemyIdentity;

		internal double PositionX;

		internal double PositionY;

		internal double PositionZ;

		internal double Damage;

		internal HitKind Kind;

		internal int PelletHits;

		internal bool MixedKinds;

		internal Accumulator(long enemyIdentity, float x, float y, float z, float damage, HitKind kind)
		{
			EnemyIdentity = enemyIdentity;
			PositionX = x;
			PositionY = y;
			PositionZ = z;
			Damage = damage;
			Kind = kind;
			PelletHits = 1;
		}
	}

	private readonly List<Accumulator> _entries = new List<Accumulator>();

	private readonly Dictionary<long, Accumulator> _byEnemy = new Dictionary<long, Accumulator>();

	internal int TargetCount => _entries.Count;

	internal bool Add(long enemyIdentity, float x, float y, float z, float damage, HitKind kind)
	{
		if (enemyIdentity == 0L || !KillPolicy.IsFinite(x) || !KillPolicy.IsFinite(y) || !KillPolicy.IsFinite(z) || !KillPolicy.IsFinite(damage) || damage < 0f)
		{
			return false;
		}
		Accumulator accumulator = default(Accumulator);
		if (!_byEnemy.TryGetValue(enemyIdentity, out accumulator))
		{
			accumulator = new Accumulator(enemyIdentity, x, y, z, damage, kind);
			_byEnemy.Add(enemyIdentity, accumulator);
			_entries.Add(accumulator);
			return true;
		}
		accumulator.PositionX += x;
		accumulator.PositionY += y;
		accumulator.PositionZ += z;
		accumulator.Damage += damage;
		accumulator.PelletHits++;
		if (accumulator.Kind != kind)
		{
			accumulator.MixedKinds = true;
		}
		if (accumulator.MixedKinds)
		{
			accumulator.Kind = HitKind.Normal;
		}
		return true;
	}

	internal MergedShotgunDamageNumber[] Drain()
	{
		if (_entries.Count == 0)
		{
			return System.Array.Empty<MergedShotgunDamageNumber>();
		}
		MergedShotgunDamageNumber[] array = new MergedShotgunDamageNumber[_entries.Count];
		for (int i = 0; i < _entries.Count; i++)
		{
			Accumulator accumulator = _entries[i];
			float damage = (float)Math.Min(accumulator.Damage, 3.4028234663852886E+38);
			array[i] = new MergedShotgunDamageNumber(accumulator.EnemyIdentity, (float)(accumulator.PositionX / (double)accumulator.PelletHits), (float)(accumulator.PositionY / (double)accumulator.PelletHits), (float)(accumulator.PositionZ / (double)accumulator.PelletHits), damage, accumulator.Kind, accumulator.PelletHits);
		}
		_entries.Clear();
		_byEnemy.Clear();
		return array;
	}
}
