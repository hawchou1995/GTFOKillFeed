using System;
using Enemies;
using HarmonyLib;

namespace HawChow.GTFOKillFeed;

[HarmonyPatch(typeof(EnemyAgent), "Setup")]
internal static class EnemySpawnPatch
{
	[HarmonyPrefix]
	private static void Prefix(EnemyAgent __instance)
	{
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		//IL_001c: Expected O, but got Unknown
		KillTracker.GuardAction((Action)delegate
		{
			CorpseFeedback.Restore(__instance, "enemy-setup");
			KillIconOccluders.Register(__instance);
			KillTracker.Spawned(__instance.Damage);
		});
	}
}
