using System;
using System.Collections.Generic;
using Gear;
using UnityEngine;

namespace HawChow.GTFOKillFeed;

internal static class ShotgunDamageNumberCapture
{
	[ThreadStatic]
	private static ShotgunDamageNumberScopeStack _scopeStack;

	[ThreadStatic]
	private static ShotgunDamageTargetScopeStack _targetStack;

	private static readonly object CategoryGate = new object();

	private static readonly HashSet<uint> ShotgunCategoryIds = new HashSet<uint>();

	private static uint ReadCategoryId(BulletWeapon weapon)
	{
		try
		{
			return (((object)(object)weapon != null) ? weapon.GearCategoryData : null)?.persistentID ?? 0;
		}
		catch
		{
			return 0u;
		}
	}

	private static void RegisterCategory(uint gearCategoryId)
	{
		if (gearCategoryId == 0)
		{
			return;
		}
		lock (CategoryGate)
		{
			ShotgunCategoryIds.Add(gearCategoryId);
		}
	}

	internal static bool IsShotgunCategory(uint gearCategoryId)
	{
		if (gearCategoryId == 0)
		{
			return false;
		}
		lock (CategoryGate)
		{
			return ShotgunCategoryIds.Contains(gearCategoryId);
		}
	}

	internal static bool IsActiveFireCategory(uint gearCategoryId)
	{
		ShotgunDamageNumberScope shotgunDamageNumberScope = _scopeStack?.Current;
		if (shotgunDamageNumberScope != null)
		{
			return FeedbackPolicy.IsShotgunFireScopeCurrent(shotgunDamageNumberScope.FireFrame, Time.frameCount, shotgunDamageNumberScope.GearCategoryId, gearCategoryId);
		}
		return false;
	}

	internal static ShotgunDamageNumberScope BeginFire(BulletWeapon weapon)
	{
		uint gearCategoryId = ReadCategoryId(weapon);
		RegisterCategory(gearCategoryId);
		if (_scopeStack == null)
		{
			_scopeStack = new ShotgunDamageNumberScopeStack();
		}
		return _scopeStack.Begin(Plugin.ShowShotgunDamageNumbers?.Value ?? true, Plugin.MergeShotgunPellets?.Value ?? true, Time.frameCount, gearCategoryId);
	}

	internal static long EnterTarget(long target)
	{
		if (_targetStack == null)
		{
			_targetStack = new ShotgunDamageTargetScopeStack();
		}
		return _targetStack.Enter(target);
	}

	internal static System.Exception ExitTarget(long previous, System.Exception error)
	{
		_targetStack?.Exit(previous);
		return error;
	}

	internal static bool TryCapture(Vector3 world, float damage, HitKind kind, bool preview, uint gearCategoryId)
	{
		ShotgunDamageNumberScope shotgunDamageNumberScope = _scopeStack?.Current;
		if (shotgunDamageNumberScope == null || preview || !FeedbackPolicy.IsShotgunFireScopeCurrent(shotgunDamageNumberScope.FireFrame, Time.frameCount, shotgunDamageNumberScope.GearCategoryId, gearCategoryId))
		{
			return false;
		}
		if (FeedbackPolicy.ShouldSuppressShotgunDamageNumber(scopeMatches: true, preview, shotgunDamageNumberScope.ShowNumbers))
		{
			return true;
		}
		if (!FeedbackPolicy.ShouldMergeShotgunDamageNumbers(isShotgun: true, shotgunDamageNumberScope.MergePellets))
		{
			return false;
		}
		if (shotgunDamageNumberScope.GearCategoryId == 0 && gearCategoryId != 0)
		{
			shotgunDamageNumberScope.GearCategoryId = gearCategoryId;
			RegisterCategory(gearCategoryId);
		}
		try
		{
			return shotgunDamageNumberScope.Batch.Add((_targetStack != null) ? _targetStack.Current : 0, world.x, world.y, world.z, damage, kind);
		}
		catch
		{
			return false;
		}
	}

	internal static System.Exception EndFire(ShotgunDamageNumberScope scope, System.Exception error)
	{
		if (scope == null || _scopeStack == null || !_scopeStack.TryEnd(scope))
		{
			return error;
		}
		if (!scope.ShowNumbers || !FeedbackPolicy.ShouldMergeShotgunDamageNumbers(isShotgun: true, scope.MergePellets))
		{
			return error;
		}
		try
		{
			MergedShotgunDamageNumber[] array = scope.Batch.Drain();
			for (int i = 0; i < array.Length; i++)
			{
				MergedShotgunDamageNumber mergedShotgunDamageNumber = array[i];
				DamageNumbers.AddMergedShotgun(new Vector3(mergedShotgunDamageNumber.X, mergedShotgunDamageNumber.Y, mergedShotgunDamageNumber.Z), mergedShotgunDamageNumber.Damage, mergedShotgunDamageNumber.Kind);
			}
		}
		catch
		{
		}
		return error;
	}
}
