using System;
using HarmonyLib;

namespace HawChow.GTFOKillFeed;

[HarmonyPatch(typeof(Dam_EnemyDamageBase), "ReceiveBulletDamage")]
internal static class EnemyBulletDamagePatch
{
	[HarmonyPrefix]
	private static void Prefix(Dam_EnemyDamageBase __instance, pBulletDamageData data, out DamageScope __state)
	{
		__state = KillTracker.Guard(() => KillTracker.BeginBullet(__instance, data));
	}

	[HarmonyFinalizer]
	private static System.Exception Finalizer(Dam_EnemyDamageBase __instance, DamageScope __state, System.Exception __exception)
	{
		return KillTracker.End(__instance, __state, __exception);
	}
}
