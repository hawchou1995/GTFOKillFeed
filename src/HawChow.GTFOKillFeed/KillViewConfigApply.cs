using System;

namespace HawChow.GTFOKillFeed;

internal static class KillViewConfigApply
{
	internal static bool Apply(KillViewSwitches view, Action<bool> setHead, Action<bool> setCard, Action<string> setMode)
	{
		bool wrote = false;
		if (view.WriteHead && setHead != null)
		{
			setHead(view.HeadEnabled);
			wrote = true;
		}
		if (view.WriteCard && setCard != null)
		{
			setCard(view.CardEnabled);
			wrote = true;
		}
		if (view.WriteMode && setMode != null)
		{
			setMode(view.ModeToken);
			wrote = true;
		}
		return wrote;
	}
}
