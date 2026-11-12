namespace HawChow.GTFOKillFeed;

internal sealed class KillCard
{
	internal readonly KillKind Kind;

	internal readonly KillTier Tier;

	internal readonly KillFlight Flight;

	internal readonly string EnemyName;

	internal readonly string DistanceText;

	internal readonly string HealthText;

	internal readonly float CreatedAt;

	internal readonly uint EnemyDataId;

	internal readonly string RawName;

	internal readonly float DistanceMeters;

	internal readonly float HealthMax;

	internal readonly int IconToken;

	internal readonly float IconExpiresAt;

	internal KillCard(KillKind kind, KillTier tier, string enemyName, float distanceMeters, float healthMax, float createdAt, float maxDistance = 999f, uint enemyDataId = 0u, KillFlight flight = KillFlight.None, int iconToken = 0, float iconExpiresAt = 0f)
	{
		Kind = kind;
		Tier = tier;
		Flight = flight;
		EnemyName = (string.IsNullOrWhiteSpace(enemyName) ? "Unknown" : enemyName.Trim());
		DistanceText = KillPolicy.FormatDistance(distanceMeters, maxDistance);
		HealthText = KillPolicy.FormatHealth(healthMax);
		CreatedAt = createdAt;
		EnemyDataId = enemyDataId;
		RawName = (string.IsNullOrWhiteSpace(enemyName) ? null : enemyName.Trim());
		DistanceMeters = distanceMeters;
		HealthMax = healthMax;
		IconToken = iconToken;
		IconExpiresAt = iconExpiresAt;
	}

	internal KillCard WithPresentation(string displayName, float maxDistance)
	{
		return new KillCard(Kind, Tier, displayName, DistanceMeters, HealthMax, CreatedAt, maxDistance, EnemyDataId, Flight, IconToken, IconExpiresAt);
	}
}
