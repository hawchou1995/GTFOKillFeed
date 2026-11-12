namespace HawChow.GTFOKillFeed.Tests;

internal static class SwitchTests
{
	internal static void Run()
	{
		FourCombinations();
		SimultaneousWhenBothEnabled();
		GameToggleAndCamera();
		CardFallbackNeedsCardSwitch();
	}

	private static void FourCombinations()
	{
		Program.True("head off/card off head", !KillViewPolicy.HeadVisible(headEnabled: false, gameVisible: true, cameraReady: true));
		Program.True("head off/card off card", !KillViewPolicy.CardVisible(cardEnabled: false, gameVisible: true));
		Program.True("head on/card off head", KillViewPolicy.HeadVisible(headEnabled: true, gameVisible: true, cameraReady: true));
		Program.True("head on/card off card", !KillViewPolicy.CardVisible(cardEnabled: false, gameVisible: true));
		Program.True("head off/card on head", !KillViewPolicy.HeadVisible(headEnabled: false, gameVisible: true, cameraReady: true));
		Program.True("head off/card on card", KillViewPolicy.CardVisible(cardEnabled: true, gameVisible: true));
	}

	private static void SimultaneousWhenBothEnabled()
	{
		Program.True("both head", KillViewPolicy.HeadVisible(headEnabled: true, gameVisible: true, cameraReady: true));
		Program.True("both card", KillViewPolicy.CardVisible(cardEnabled: true, gameVisible: true));
	}

	private static void GameToggleAndCamera()
	{
		Program.True("F7 hides head", !KillViewPolicy.HeadVisible(headEnabled: true, gameVisible: false, cameraReady: true));
		Program.True("F7 hides card", !KillViewPolicy.CardVisible(cardEnabled: true, gameVisible: false));
		Program.True("no camera hides head", !KillViewPolicy.HeadVisible(headEnabled: true, gameVisible: true, cameraReady: false));
		Program.True("no camera keeps card", KillViewPolicy.CardVisible(cardEnabled: true, gameVisible: true));
	}

	private static void CardFallbackNeedsCardSwitch()
	{
		IconPlacement hidden = KillViewPolicy.ResolvePlacement(new ScreenBox(100f, 50f, 146f, 96f), new ScreenBox[1] { new ScreenBox(90f, 40f, 160f, 120f) }, 1, 0f, 1080f);
		Program.True("fallback icon hidden", hidden.Hidden);
		Program.True("fallback card off stays off", !KillViewPolicy.CardVisible(cardEnabled: false, gameVisible: true));
		Program.True("fallback card on shows", KillViewPolicy.CardVisible(cardEnabled: true, gameVisible: true));
	}
}
