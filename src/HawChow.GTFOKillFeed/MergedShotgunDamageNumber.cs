namespace HawChow.GTFOKillFeed;

internal readonly struct MergedShotgunDamageNumber
{
	internal readonly long EnemyIdentity;

	internal readonly float X;

	internal readonly float Y;

	internal readonly float Z;

	internal readonly float Damage;

	internal readonly HitKind Kind;

	internal readonly int PelletHits;

	internal MergedShotgunDamageNumber(long enemyIdentity, float x, float y, float z, float damage, HitKind kind, int pelletHits)
	{
		EnemyIdentity = enemyIdentity;
		X = x;
		Y = y;
		Z = z;
		Damage = damage;
		Kind = kind;
		PelletHits = pelletHits;
	}
}
