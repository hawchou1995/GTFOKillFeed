using System;
using HarmonyLib;

namespace HawChow.GTFOKillFeed;

[HarmonyPatch(typeof(GameStateManager), "DoChangeState")]
internal static class KillFeedStatePatch
{
	[HarmonyPrefix]
	private static void Prefix(eGameStateName nextState)
	{
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		//IL_001c: Expected O, but got Unknown
		KillTracker.GuardAction((Action)delegate
		{
			KillTracker.ObserveState(nextState);
		});
	}
}
