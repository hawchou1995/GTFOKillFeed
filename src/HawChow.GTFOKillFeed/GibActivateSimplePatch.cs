using System;
using CharacterDestruction;
using HarmonyLib;
using UnityEngine;

namespace HawChow.GTFOKillFeed;

[HarmonyPatch(typeof(CD_Gib), "Activate", new System.Type[] { typeof(Vector3) })]
internal static class GibActivateSimplePatch
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

	[HarmonyPostfix]
	private static void Postfix(CD_Gib __instance)
	{
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		//IL_001c: Expected O, but got Unknown
		KillTracker.GuardAction((Action)delegate
		{
			CorpseFeedback.CaptureGib(__instance);
		});
	}
}
