using System;
using Agents;
using HarmonyLib;
using UnityEngine;

namespace HawChow.GTFOKillFeed;

[HarmonyPatch(typeof(Dam_EnemyDamageLimb), "BulletDamage")]
internal static class LocalBulletPatch
{
	[HarmonyPrefix]
	private static void Prefix(Dam_EnemyDamageLimb __instance, float dam, Agent sourceAgent, Vector3 position, Vector3 direction, float precisionMulti, bool allowDirectionalBonus, uint gearCategoryId, out DamageScope __state)
	{
		__state = KillTracker.Guard(() => KillTracker.BeginLimb(__instance, dam, sourceAgent, position, direction, precisionMulti, allowDirectionalBonus, melee: false, 1f, 1f, gearCategoryId));
	}

	[HarmonyFinalizer]
	private static System.Exception Finalizer(Dam_EnemyDamageLimb __instance, DamageScope __state, System.Exception __exception)
	{
		return KillTracker.EndLimb(__instance, __state, __exception);
	}
}
