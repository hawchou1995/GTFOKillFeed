using System;

namespace HawChow.GTFOKillFeed.Tests;

internal static class ViewModeTests
{
	internal static void Run()
	{
		ParseTokens();
		LegacyMarkerFollowsDisplay();
		LegacyMarkerBecomesExplicitOnNewKeyChange();
		ExplicitMarkerIgnoresLegacy();
		UnknownMarkerWithMatchingKeysStaysLegacy();
		UnknownMarkerWithConflictBecomesExplicit();
		MissingNewKeyIsWritten();
	}

	private static void ParseTokens()
	{
		Program.Eq("mode explicit", ViewMode.Explicit, KillViewPolicy.ParseMode("Explicit", out KillDisplayLegacy _));
		Program.Eq("mode legacy head", ViewMode.Legacy, KillViewPolicy.ParseMode("Legacy:Head", out KillDisplayLegacy head));
		Program.Eq("mode legacy head kind", KillDisplayLegacy.Head, head);
		Program.Eq("mode legacy card", ViewMode.Legacy, KillViewPolicy.ParseMode("legacy:Card", out KillDisplayLegacy card));
		Program.Eq("mode legacy card kind", KillDisplayLegacy.Card, card);
		Program.Eq("mode cards alias", ViewMode.Legacy, KillViewPolicy.ParseMode("Legacy:Cards", out KillDisplayLegacy cards));
		Program.Eq("mode cards alias kind", KillDisplayLegacy.Card, cards);
		Program.Eq("mode blank", ViewMode.Unknown, KillViewPolicy.ParseMode("", out KillDisplayLegacy _));
		Program.Eq("mode garbage", ViewMode.Unknown, KillViewPolicy.ParseMode("sideways", out KillDisplayLegacy _));
		Program.Eq("mode bad suffix", ViewMode.Unknown, KillViewPolicy.ParseMode("Legacy:Sideways", out KillDisplayLegacy _));
	}

	private static void LegacyMarkerFollowsDisplay()
	{
		KillViewSwitches view = KillViewPolicy.ResolveSwitches(KillViewConfigFile.Parse("[KillIcon]\nDisplay = Card\nHeadEnabled = true\nCardEnabled = false\nViewMode = Legacy:Head\n[Card]\nEnabled = false\n"));
		Program.Eq("legacy marker mode", ViewMode.Legacy, view.Mode);
		Program.Eq("legacy marker head", expected: false, view.HeadEnabled);
		Program.Eq("legacy marker card", expected: true, view.CardEnabled);
		Program.True("legacy marker writes head", view.WriteHead);
		Program.True("legacy marker writes card", view.WriteCard);
		Program.True("legacy marker writes mode", view.WriteMode);
		Program.Eq("legacy marker token", "Legacy:Card", view.ModeToken);
	}

	private static void LegacyMarkerBecomesExplicitOnNewKeyChange()
	{
		KillViewSwitches view = KillViewPolicy.ResolveSwitches(KillViewConfigFile.Parse("[KillIcon]\nDisplay = Head\nHeadEnabled = true\nViewMode = Legacy:Head\n[Card]\nEnabled = true\n"));
		Program.Eq("changed key mode", ViewMode.Explicit, view.Mode);
		Program.Eq("changed key head", expected: true, view.HeadEnabled);
		Program.Eq("changed key card", expected: true, view.CardEnabled);
		Program.True("changed key no head rewrite", !view.WriteHead);
		Program.True("changed key no card rewrite", !view.WriteCard);
		Program.True("changed key mode rewrite", view.WriteMode);
	}

	private static void ExplicitMarkerIgnoresLegacy()
	{
		KillViewSwitches view = KillViewPolicy.ResolveSwitches(KillViewConfigFile.Parse("[KillIcon]\nDisplay = Head\nHeadEnabled = false\nViewMode = Explicit\n[Card]\nEnabled = false\n"));
		Program.Eq("explicit marker mode", ViewMode.Explicit, view.Mode);
		Program.Eq("explicit marker head", expected: false, view.HeadEnabled);
		Program.Eq("explicit marker card", expected: false, view.CardEnabled);
		Program.True("explicit marker no mode write", !view.WriteMode);
	}

	private static void UnknownMarkerWithMatchingKeysStaysLegacy()
	{
		KillViewSwitches view = KillViewPolicy.ResolveSwitches(KillViewConfigFile.Parse("[KillIcon]\nDisplay = Card\nHeadEnabled = false\n[Card]\nEnabled = true\n"));
		Program.Eq("unknown match mode", ViewMode.Legacy, view.Mode);
		Program.Eq("unknown match head", expected: false, view.HeadEnabled);
		Program.Eq("unknown match card", expected: true, view.CardEnabled);
		Program.Eq("unknown match token", "Legacy:Card", view.ModeToken);
	}

	private static void UnknownMarkerWithConflictBecomesExplicit()
	{
		KillViewSwitches view = KillViewPolicy.ResolveSwitches(KillViewConfigFile.Parse("[KillIcon]\nDisplay = Head\nHeadEnabled = true\n[Card]\nEnabled = true\n"));
		Program.Eq("unknown conflict mode", ViewMode.Explicit, view.Mode);
		Program.True("unknown conflict head", view.HeadEnabled);
		Program.True("unknown conflict card", view.CardEnabled);
	}

	private static void MissingNewKeyIsWritten()
	{
		KillViewSwitches view = KillViewPolicy.ResolveSwitches(KillViewConfigFile.Parse("[KillIcon]\nDisplay = Head\nViewMode = Explicit\n"));
		Program.Eq("missing explicit mode", ViewMode.Explicit, view.Mode);
		Program.True("missing explicit head", view.HeadEnabled);
		Program.True("missing explicit card off", !view.CardEnabled);
		Program.True("missing explicit writes head", view.WriteHead);
		Program.True("missing explicit writes card", view.WriteCard);
	}
}
