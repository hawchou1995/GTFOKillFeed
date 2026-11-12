using System;
using Gear;
using HarmonyLib;

namespace HawChow.GTFOKillFeed;

[HarmonyPatch(typeof(ShotgunSynced), "Fire", new System.Type[] { typeof(bool) })]
internal static class ShotgunSyncedFireDamageNumberPatch
{
	[HarmonyPrefix]
	private static void Prefix(ShotgunSynced __instance, out ShotgunDamageNumberScope __state)
	{
		__state = ShotgunDamageNumberCapture.BeginFire(__instance);
	}

	[HarmonyFinalizer]
	private static System.Exception Finalizer(ShotgunDamageNumberScope __state, System.Exception __exception)
	{
		return ShotgunDamageNumberCapture.EndFire(__state, __exception);
	}
}
