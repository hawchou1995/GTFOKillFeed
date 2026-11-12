using System;
using CharacterDestruction;
using HarmonyLib;

namespace HawChow.GTFOKillFeed;

[HarmonyPatch(typeof(CD_Gib), "Deactivate")]
internal static class GibDeactivatePatch
{
	[HarmonyPrefix]
	private static void Prefix(CD_Gib __instance)
	{
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		//IL_001c: Expected O, but got Unknown
		KillTracker.GuardAction((Action)delegate
		{
			CorpseFeedback.GibReused(__instance);
		});
	}
}
