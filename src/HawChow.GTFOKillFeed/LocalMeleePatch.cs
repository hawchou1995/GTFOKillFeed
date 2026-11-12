using System;
using Agents;
using HarmonyLib;
using UnityEngine;

namespace HawChow.GTFOKillFeed;

[HarmonyPatch(typeof(Dam_EnemyDamageLimb), "MeleeDamage")]
internal static class LocalMeleePatch
{
	[HarmonyPrefix]
	private static void Prefix(Dam_EnemyDamageLimb __instance, float dam, Agent sourceAgent, Vector3 position, Vector3 direction, float precisionMulti, float backstabberMulti, float sleeperMulti, uint gearCategoryId, out DamageScope __state)
	{
		__state = KillTracker.Guard(() => KillTracker.BeginLimb(__instance, dam, sourceAgent, position, direction, precisionMulti, behind: true, melee: true, backstabberMulti, sleeperMulti, gearCategoryId));
	}

	[HarmonyFinalizer]
	private static System.Exception Finalizer(Dam_EnemyDamageLimb __instance, DamageScope __state, System.Exception __exception)
	{
		return KillTracker.EndLimb(__instance, __state, __exception);
	}
}
