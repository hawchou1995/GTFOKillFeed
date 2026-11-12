using System;
using UnityEngine;

namespace HawChow.GTFOKillFeed;

internal sealed class DamageRoot
{
	internal System.IntPtr EnemyPointer;

	internal float HealthBefore;

	internal bool LocalSource;

	internal bool LimbObserved;

	internal bool Weakspot;

	internal bool Credited;

	internal bool HitRecorded;

	internal bool HasValue;

	internal bool Processed;

	internal ulong PlayerLookup;

	internal HitKind Kind;

	internal float Damage;

	internal Vector3 Position;

	internal Vector3 Direction;

	internal uint GearCategoryId;
}
