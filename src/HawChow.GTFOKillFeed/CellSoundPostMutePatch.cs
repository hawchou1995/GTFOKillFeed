using System;
using HarmonyLib;

namespace HawChow.GTFOKillFeed;

[HarmonyPatch(typeof(CellSound), "Post", new System.Type[] { typeof(uint) })]
internal static class CellSoundPostMutePatch
{
	[HarmonyPrefix]
	private static bool Prefix(uint eventID)
	{
		return HitSound.ShouldRunPost(eventID);
	}
}
