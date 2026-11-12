using System;
using Enemies;
using HarmonyLib;

namespace HawChow.GTFOKillFeed;

[HarmonyPatch(typeof(EnemyAgent), "OnDead")]
internal static class EnemyAgentDeathPatch
{
	[HarmonyPostfix]
	private static void Postfix(EnemyAgent __instance)
	{
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		//IL_001c: Expected O, but got Unknown
		KillTracker.GuardAction((Action)delegate
		{
			KillTracker.EnemyDied(__instance);
		});
	}
}
