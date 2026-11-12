using System;
using HarmonyLib;

namespace HawChow.GTFOKillFeed;

[HarmonyPatch(typeof(KillTracker), "Complete", new System.Type[]
{
	typeof(Dam_EnemyDamageBase),
	typeof(DamageScope)
})]
internal static class DamageNumberTargetScopePatch
{
	[HarmonyPrefix]
	private static void Prefix(Dam_EnemyDamageBase __0, out long __state)
	{
		__state = ShotgunDamageNumberCapture.EnterTarget((__0 != null) ? __0.Pointer.ToInt64() : 0);
	}

	[HarmonyFinalizer]
	private static System.Exception Finalizer(long __state, System.Exception __exception)
	{
		return ShotgunDamageNumberCapture.ExitTarget(__state, __exception);
	}
}
