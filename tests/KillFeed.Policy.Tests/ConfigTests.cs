using System;

namespace HawChow.GTFOKillFeed.Tests;

internal static class ConfigTests
{
	internal static void Run()
	{
		NewInstallDefaults();
		LegacyHeadMapsToHeadOnly();
		LegacyCardMapsToCardOnly();
		LegacyCaseAndAliases();
		LegacyUnknownFallsBackToHead();
		NewKeysWinOverLegacy();
		PartialNewKeysKeepLegacyChoice();
		InvalidNewKeyFallsBackToLegacy();
		InvalidNewKeyWithoutLegacyUsesDefaults();
		ParserHandlesBomCommentsSpacesAndOrder();
		ParserIsSectionAndKeyCaseInsensitive();
		ExistingLegacyValueIsRetained();
	}

	private static void NewInstallDefaults()
	{
		KillViewSwitches view = KillViewPolicy.ResolveSwitches(KillViewConfigFile.Parse(string.Empty));
		Program.Eq("new-install head", expected: false, view.HeadEnabled);
		Program.Eq("new-install card", expected: true, view.CardEnabled);
		Program.Eq("new-install head-origin", SwitchOrigin.Default, view.HeadOrigin);
		Program.Eq("new-install card-origin", SwitchOrigin.Default, view.CardOrigin);
		Program.True("new-install no legacy", !view.LegacyKeyPresent);
	}

	private static void LegacyHeadMapsToHeadOnly()
	{
		KillViewSwitches view = KillViewPolicy.ResolveSwitches(KillViewConfigFile.Parse("[KillIcon]\nDisplay = Head\n"));
		Program.Eq("legacy-head head", expected: true, view.HeadEnabled);
		Program.Eq("legacy-head card", expected: false, view.CardEnabled);
		Program.Eq("legacy-head head-origin", SwitchOrigin.Legacy, view.HeadOrigin);
		Program.Eq("legacy-head card-origin", SwitchOrigin.Legacy, view.CardOrigin);
		Program.True("legacy-head key present", view.LegacyKeyPresent);
		Program.True("legacy-head new head absent", !view.HeadKeyPresent);
		Program.True("legacy-head new card absent", !view.CardKeyPresent);
	}

	private static void LegacyCardMapsToCardOnly()
	{
		KillViewSwitches view = KillViewPolicy.ResolveSwitches(KillViewConfigFile.Parse("[KillIcon]\nDisplay=Card\n"));
		Program.Eq("legacy-card head", expected: false, view.HeadEnabled);
		Program.Eq("legacy-card card", expected: true, view.CardEnabled);
		Program.Eq("legacy-card head-origin", SwitchOrigin.Legacy, view.HeadOrigin);
		Program.Eq("legacy-card card-origin", SwitchOrigin.Legacy, view.CardOrigin);
	}

	private static void LegacyCaseAndAliases()
	{
		Program.Eq("legacy-lower-card", KillDisplayLegacy.Card, KillViewPolicy.ParseLegacyDisplay("card"));
		Program.Eq("legacy-upper-card", KillDisplayLegacy.Card, KillViewPolicy.ParseLegacyDisplay("CARD"));
		Program.Eq("legacy-cards", KillDisplayLegacy.Card, KillViewPolicy.ParseLegacyDisplay(" Cards "));
		Program.Eq("legacy-head-lower", KillDisplayLegacy.Head, KillViewPolicy.ParseLegacyDisplay("head"));
	}

	private static void LegacyUnknownFallsBackToHead()
	{
		Program.Eq("legacy-empty", KillDisplayLegacy.Head, KillViewPolicy.ParseLegacyDisplay(string.Empty));
		Program.Eq("legacy-null", KillDisplayLegacy.Head, KillViewPolicy.ParseLegacyDisplay(null));
		Program.Eq("legacy-garbage", KillDisplayLegacy.Head, KillViewPolicy.ParseLegacyDisplay("sideways"));
		KillViewSwitches view = KillViewPolicy.ResolveSwitches(KillViewConfigFile.Parse("[KillIcon]\nDisplay = sideways\n"));
		Program.Eq("legacy-garbage head", expected: true, view.HeadEnabled);
		Program.Eq("legacy-garbage card", expected: false, view.CardEnabled);
	}

	private static void NewKeysWinOverLegacy()
	{
		KillViewSwitches view = KillViewPolicy.ResolveSwitches(KillViewConfigFile.Parse("[KillIcon]\nDisplay = Head\nHeadEnabled = false\n[Card]\nEnabled = true\n"));
		Program.Eq("new-wins head", expected: false, view.HeadEnabled);
		Program.Eq("new-wins card", expected: true, view.CardEnabled);
		Program.Eq("new-wins head-origin", SwitchOrigin.Configured, view.HeadOrigin);
		Program.Eq("new-wins card-origin", SwitchOrigin.Configured, view.CardOrigin);
		Program.True("new-wins head present", view.HeadKeyPresent);
		Program.True("new-wins card present", view.CardKeyPresent);
	}

	private static void PartialNewKeysKeepLegacyChoice()
	{
		KillViewSwitches view = KillViewPolicy.ResolveSwitches(KillViewConfigFile.Parse("[KillIcon]\nDisplay = Card\nHeadEnabled = true\n"));
		Program.Eq("partial head configured", expected: true, view.HeadEnabled);
		Program.Eq("partial head-origin", SwitchOrigin.Configured, view.HeadOrigin);
		Program.Eq("partial card legacy", expected: true, view.CardEnabled);
		Program.Eq("partial card-origin", SwitchOrigin.LegacyFallback, view.CardOrigin);
		Program.True("partial head present", view.HeadKeyPresent);
		Program.True("partial card absent", !view.CardKeyPresent);
	}

	private static void InvalidNewKeyFallsBackToLegacy()
	{
		KillViewSwitches view = KillViewPolicy.ResolveSwitches(KillViewConfigFile.Parse("[KillIcon]\nDisplay = Head\nHeadEnabled = yes\n"));
		Program.Eq("invalid+legacy head", expected: true, view.HeadEnabled);
		Program.Eq("invalid+legacy head-origin", SwitchOrigin.LegacyFallback, view.HeadOrigin);
		Program.Eq("invalid+legacy card", expected: false, view.CardEnabled);
		Program.Eq("invalid+legacy card-origin", SwitchOrigin.LegacyFallback, view.CardOrigin);
		Program.True("invalid+legacy keeps key", view.HeadKeyPresent);
	}

	private static void InvalidNewKeyWithoutLegacyUsesDefaults()
	{
		KillViewSwitches view = KillViewPolicy.ResolveSwitches(KillViewConfigFile.Parse("[KillIcon]\nHeadEnabled = 2\n[Card]\nEnabled = on\n"));
		Program.Eq("invalid-default head", expected: false, view.HeadEnabled);
		Program.Eq("invalid-default card", expected: true, view.CardEnabled);
		Program.Eq("invalid-default head-origin", SwitchOrigin.InvalidFallback, view.HeadOrigin);
		Program.Eq("invalid-default card-origin", SwitchOrigin.InvalidFallback, view.CardOrigin);
		Program.True("invalid-default no legacy", !view.LegacyKeyPresent);
	}

	private static void ParserHandlesBomCommentsSpacesAndOrder()
	{
		string text = "\uFEFF# comment\r\n; other\r\n\r\n[KillIcon]\r\nDisplay = Head\r\nHeadEnabled   =   true   \r\n\r\n[Card]\r\nEnabled = false\r\n";
		KillViewConfigFile file = KillViewConfigFile.Parse(text);
		Program.True("parser legacy", file.Has("KillIcon", "Display"));
		Program.Eq("parser legacy raw", "Head", file.Raw("KillIcon", "Display"));
		Program.True("parser head raw", file.Has("KillIcon", "HeadEnabled"));
		Program.Eq("parser head raw value", "true", file.Raw("KillIcon", "HeadEnabled"));
		Program.Eq("parser card raw value", "false", file.Raw("Card", "Enabled"));
		KillViewSwitches view = KillViewPolicy.ResolveSwitches(file);
		Program.True("parser switches head", view.HeadEnabled);
		Program.True("parser switches card off", !view.CardEnabled);
	}

	private static void ParserIsSectionAndKeyCaseInsensitive()
	{
		KillViewConfigFile file = KillViewConfigFile.Parse("[killicon]\ndisplay = card\nHEADENABLED = TRUE\n[card]\nenabled = FALSE\n");
		Program.True("case has display", file.Has("KillIcon", "Display"));
		Program.True("case has head", file.Has("KillIcon", "HeadEnabled"));
		Program.True("case has card", file.Has("Card", "Enabled"));
		KillViewSwitches view = KillViewPolicy.ResolveSwitches(file);
		Program.True("case head true", view.HeadEnabled);
		Program.True("case card false", !view.CardEnabled);
	}

	private static void ExistingLegacyValueIsRetained()
	{
		KillViewConfigFile file = KillViewConfigFile.Parse("[KillIcon]\nDisplay = Head\n");
		KillViewSwitches view = KillViewPolicy.ResolveSwitches(file);
		Program.True("retain legacy present", view.LegacyKeyPresent);
		Program.Eq("retain legacy raw", "Head", file.Raw("KillIcon", "Display"));
		Program.True("retain head absent", !view.HeadKeyPresent);
		Program.True("retain card absent", !view.CardKeyPresent);
		Program.True("retain would write head", !view.HeadKeyPresent);
		Program.True("retain would write card", !view.CardKeyPresent);
	}
}
