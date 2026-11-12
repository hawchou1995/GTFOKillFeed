using System;
using Agents;
using HarmonyLib;
using UnityEngine;

namespace HawChow.GTFOKillFeed;

[HarmonyPatch(typeof(Dam_EnemyDamageBase), "ProcessReceivedDamage")]
internal static class EnemyProcessedDamagePatch
{
	[HarmonyPrefix]
	private static void Prefix(Dam_EnemyDamageBase __instance, Agent damageSource, float damage, Vector3 position, Vector3 direction, int limbID, uint gearCategoryId, out DamageScope __state)
	{
		__state = KillTracker.Guard(() => KillTracker.BeginProcessedDamage(__instance, damageSource, damage, position, direction, limbID, gearCategoryId));
	}

	[HarmonyFinalizer]
	private static System.Exception Finalizer(Dam_EnemyDamageBase __instance, DamageScope __state, System.Exception __exception)
	{
		return KillTracker.End(__instance, __state, __exception);
	}
}
