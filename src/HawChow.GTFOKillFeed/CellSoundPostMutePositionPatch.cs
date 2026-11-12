using System;
using HarmonyLib;
using UnityEngine;

namespace HawChow.GTFOKillFeed;

[HarmonyPatch(typeof(CellSound), "Post", new System.Type[]
{
	typeof(uint),
	typeof(Vector3)
})]
internal static class CellSoundPostMutePositionPatch
{
	[HarmonyPrefix]
	private static bool Prefix(uint eventID, Vector3 pos)
	{
		return HitSound.ShouldRunPost(eventID);
	}
}
