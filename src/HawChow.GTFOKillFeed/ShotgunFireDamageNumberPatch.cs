using System;
using Gear;
using HarmonyLib;

namespace HawChow.GTFOKillFeed;

[HarmonyPatch(typeof(Shotgun), "Fire", new System.Type[] { typeof(bool) })]
internal static class ShotgunFireDamageNumberPatch
{
	[HarmonyPrefix]
	private static void Prefix(Shotgun __instance, out ShotgunDamageNumberScope __state)
	{
		__state = ShotgunDamageNumberCapture.BeginFire(__instance);
	}

	[HarmonyFinalizer]
	private static System.Exception Finalizer(ShotgunDamageNumberScope __state, System.Exception __exception)
	{
		return ShotgunDamageNumberCapture.EndFire(__state, __exception);
	}
}
