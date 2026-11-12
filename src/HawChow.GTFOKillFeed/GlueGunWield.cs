using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;

namespace HawChow.GTFOKillFeed;

internal static class GlueGunWield
{
	internal static bool IsHeld(FirstPersonItemHolder holder)
	{
		if ((Object)(object)holder == null)
		{
			return false;
		}
		try
		{
			ItemEquippable wieldedItem = holder.WieldedItem;
			if ((Object)(object)wieldedItem == null)
			{
				return false;
			}
			return (Object)(object)((Il2CppObjectBase)(object)wieldedItem).TryCast<GlueGun>() != null;
		}
		catch
		{
			return false;
		}
	}
}
