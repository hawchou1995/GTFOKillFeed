using System;
using HarmonyLib;

namespace HawChow.GTFOKillFeed;

[HarmonyPatch(typeof(Dam_EnemyDamageBase), "InstantDead")]
internal static class EnemyDamageDeathPatch
{
	[HarmonyPostfix]
	private static void Postfix(Dam_EnemyDamageBase __instance)
	{
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		//IL_001c: Expected O, but got Unknown
		KillTracker.GuardAction((Action)delegate
		{
			KillTracker.CompleteFromActive(__instance);
		});
	}
}
