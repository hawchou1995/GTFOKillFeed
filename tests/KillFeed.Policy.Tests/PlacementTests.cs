using System;

namespace HawChow.GTFOKillFeed.Tests;

internal static class PlacementTests
{
	private static readonly ScreenBox Icon = new ScreenBox(100f, 100f, 146f, 146f);

	internal static void Run()
	{
		NoOccludersKeepsPosition();
		NonOverlappingOccludersAreIgnored();
		SingleOverlapLiftsAboveTop();
		MultipleOverlapsUseHighestBoundary();
		TouchDoesNotCount();
		NonOverlappingAboveDoesNotMoveIcon();
		TopOfScreenHidesIcon();
		IconAtTopWithoutOccluderIsFine();
		InvalidBoxesAreIgnored();
		InvalidIconIsHidden();
		NullAndOverlongBuffersAreSafe();
		WideBoxAndEqualTop();
		SecondaryOverlapLiftsTwice();
		VisualAreaIncludesFlightBadge();
		TooManyStackedBoxesHideSafely();
	}

	private static void NoOccludersKeepsPosition()
	{
		IconPlacement placement = KillViewPolicy.ResolvePlacement(Icon, new ScreenBox[0], 0, 0f, 1080f);
		Program.True("empty not hidden", !placement.Hidden);
		Program.Near("empty top", 100f, placement.Top);
		Program.Near("empty lift", 0f, placement.LiftPx);
		Program.Eq("empty reason", PlacementReason.Unchanged, placement.Reason);
		Program.Eq("empty occluders", 0, placement.Occluders);
	}

	private static void NonOverlappingOccludersAreIgnored()
	{
		ScreenBox[] boxes = new ScreenBox[3]
		{
			new ScreenBox(300f, 100f, 350f, 146f),
			new ScreenBox(100f, 200f, 146f, 260f),
			new ScreenBox(0f, 0f, 50f, 50f)
		};
		IconPlacement placement = KillViewPolicy.ResolvePlacement(Icon, boxes, 3, 0f, 1080f);
		Program.True("ignored not hidden", !placement.Hidden);
		Program.Near("ignored top", 100f, placement.Top);
		Program.Eq("ignored occluders", 0, placement.Occluders);
		Program.Eq("ignored reason", PlacementReason.Unchanged, placement.Reason);
	}

	private static void SingleOverlapLiftsAboveTop()
	{
		ScreenBox[] boxes = new ScreenBox[1] { new ScreenBox(90f, 120f, 160f, 200f) };
		IconPlacement placement = KillViewPolicy.ResolvePlacement(Icon, boxes, 1, 0f, 1080f);
		Program.True("single lifted", !placement.Hidden);
		Program.Near("single top", 72f, placement.Top);
		Program.Near("single lift", 28f, placement.LiftPx);
		Program.Eq("single occluders", 1, placement.Occluders);
		Program.Eq("single reason", PlacementReason.Lifted, placement.Reason);
	}

	private static void MultipleOverlapsUseHighestBoundary()
	{
		ScreenBox[] boxes = new ScreenBox[2]
		{
			new ScreenBox(90f, 120f, 160f, 200f),
			new ScreenBox(80f, 60f, 150f, 140f)
		};
		IconPlacement placement = KillViewPolicy.ResolvePlacement(Icon, boxes, 2, 0f, 1080f);
		Program.True("multiple lifted", !placement.Hidden);
		Program.Near("multiple top", 12f, placement.Top);
		Program.Near("multiple lift", 88f, placement.LiftPx);
		Program.Eq("multiple occluders", 2, placement.Occluders);
	}

	private static void TouchDoesNotCount()
	{
		ScreenBox[] boxes = new ScreenBox[2]
		{
			new ScreenBox(100f, 146f, 146f, 200f),
			new ScreenBox(146f, 100f, 200f, 146f)
		};
		IconPlacement placement = KillViewPolicy.ResolvePlacement(Icon, boxes, 2, 0f, 1080f);
		Program.True("touch not hidden", !placement.Hidden);
		Program.Near("touch top", 100f, placement.Top);
		Program.Eq("touch occluders", 0, placement.Occluders);
	}

	private static void NonOverlappingAboveDoesNotMoveIcon()
	{
		ScreenBox[] boxes = new ScreenBox[1] { new ScreenBox(100f, 20f, 146f, 60f) };
		IconPlacement placement = KillViewPolicy.ResolvePlacement(Icon, boxes, 1, 0f, 1080f);
		Program.Near("above non-overlap top", 100f, placement.Top);
		Program.Eq("above non-overlap reason", PlacementReason.Unchanged, placement.Reason);
	}

	private static void TopOfScreenHidesIcon()
	{
		ScreenBox icon = new ScreenBox(100f, 50f, 146f, 96f);
		ScreenBox[] boxes = new ScreenBox[1] { new ScreenBox(90f, 40f, 160f, 120f) };
		IconPlacement placement = KillViewPolicy.ResolvePlacement(icon, boxes, 1, 0f, 1080f);
		Program.True("no-room hidden", placement.Hidden);
		Program.Eq("no-room reason", PlacementReason.HiddenNoRoom, placement.Reason);
		Program.Near("no-room top stays", 50f, placement.Top);
		Program.Near("no-room lift diagnostic", 58f, placement.LiftPx);
		Program.Eq("no-room occluders", 1, placement.Occluders);
	}

	private static void IconAtTopWithoutOccluderIsFine()
	{
		ScreenBox icon = new ScreenBox(100f, 0f, 146f, 46f);
		IconPlacement placement = KillViewPolicy.ResolvePlacement(icon, null, 0, 0f, 1080f);
		Program.True("top icon visible", !placement.Hidden);
		Program.Near("top icon top", 0f, placement.Top);
	}

	private static void InvalidBoxesAreIgnored()
	{
		ScreenBox[] boxes = new ScreenBox[4]
		{
			new ScreenBox(float.NaN, 10f, 200f, 200f),
			new ScreenBox(100f, 100f, 100f, 146f),
			new ScreenBox(float.PositiveInfinity, 0f, 200f, 50f),
			new ScreenBox(90f, 120f, 160f, 200f)
		};
		IconPlacement placement = KillViewPolicy.ResolvePlacement(Icon, boxes, 4, 0f, 1080f);
		Program.Near("invalid ignored top", 72f, placement.Top);
		Program.Eq("invalid ignored occluders", 1, placement.Occluders);
	}

	private static void InvalidIconIsHidden()
	{
		ScreenBox icon = new ScreenBox(float.NaN, 100f, 146f, 146f);
		IconPlacement placement = KillViewPolicy.ResolvePlacement(icon, null, 0, 0f, 1080f);
		Program.True("invalid icon hidden", placement.Hidden);
		Program.Eq("invalid icon reason", PlacementReason.HiddenInvalidIcon, placement.Reason);
		Program.True("zero screen height hidden", KillViewPolicy.ResolvePlacement(Icon, null, 0, 0f, 0f).Hidden);
	}

	private static void NullAndOverlongBuffersAreSafe()
	{
		IconPlacement nullPlacement = KillViewPolicy.ResolvePlacement(Icon, null, 5, 0f, 1080f);
		Program.Eq("null buffer reason", PlacementReason.Unchanged, nullPlacement.Reason);
		ScreenBox[] one = new ScreenBox[1] { new ScreenBox(90f, 120f, 160f, 200f) };
		IconPlacement overlong = KillViewPolicy.ResolvePlacement(Icon, one, 99, 0f, 1080f);
		Program.Near("overlong top", 72f, overlong.Top);
		IconPlacement negative = KillViewPolicy.ResolvePlacement(Icon, one, -3, 0f, 1080f);
		Program.Eq("negative count reason", PlacementReason.Unchanged, negative.Reason);
	}

	private static void SecondaryOverlapLiftsTwice()
	{
		ScreenBox[] boxes = new ScreenBox[2]
		{
			new ScreenBox(90f, 120f, 160f, 200f),
			new ScreenBox(90f, 60f, 160f, 140f)
		};
		IconPlacement placement = KillViewPolicy.ResolvePlacement(Icon, boxes, 2, 0f, 1080f);
		Program.True("secondary not hidden", !placement.Hidden);
		Program.Near("secondary top", 12f, placement.Top);
		Program.Eq("secondary occluders", 2, placement.Occluders);
		Program.Eq("secondary reason", PlacementReason.Lifted, placement.Reason);
	}

	private static void VisualAreaIncludesFlightBadge()
	{
		ScreenBox visual = new ScreenBox(100f, 100f, 146f, 165f);
		ScreenBox[] boxes = new ScreenBox[1] { new ScreenBox(90f, 150f, 160f, 220f) };
		IconPlacement placement = KillViewPolicy.ResolvePlacement(visual, boxes, 1, 0f, 1080f);
		Program.True("badge area lifted", !placement.Hidden);
		Program.Near("badge area top", 83f, placement.Top);
		Program.Eq("badge area occluders", 1, placement.Occluders);
		IconPlacement iconOnly = KillViewPolicy.ResolvePlacement(new ScreenBox(100f, 100f, 146f, 146f), boxes, 1, 0f, 1080f);
		Program.Near("icon-only ignores badge box", 100f, iconOnly.Top);
	}

	private static void TooManyStackedBoxesHideSafely()
	{
		ScreenBox icon = new ScreenBox(100f, 1000f, 146f, 1046f);
		ScreenBox[] boxes = new ScreenBox[5]
		{
			new ScreenBox(90f, 1040f, 160f, 1060f),
			new ScreenBox(90f, 980f, 160f, 995f),
			new ScreenBox(90f, 920f, 160f, 935f),
			new ScreenBox(90f, 860f, 160f, 875f),
			new ScreenBox(90f, 800f, 160f, 815f)
		};
		IconPlacement placement = KillViewPolicy.ResolvePlacement(icon, boxes, 5, 0f, 2000f);
		Program.True("stacked hidden", placement.Hidden);
		Program.Eq("stacked reason", PlacementReason.HiddenUnstable, placement.Reason);
		Program.Near("stacked top stays", 1000f, placement.Top);
		Program.Eq("stacked occluders", 4, placement.Occluders);
	}

	private static void WideBoxAndEqualTop()
	{
		ScreenBox[] wide = new ScreenBox[1] { new ScreenBox(0f, 130f, 500f, 140f) };
		IconPlacement widePlacement = KillViewPolicy.ResolvePlacement(Icon, wide, 1, 0f, 1080f);
		Program.Near("wide top", 82f, widePlacement.Top);
		ScreenBox[] equal = new ScreenBox[1] { new ScreenBox(90f, 100f, 160f, 140f) };
		IconPlacement equalPlacement = KillViewPolicy.ResolvePlacement(Icon, equal, 1, 0f, 1080f);
		Program.Near("equal top", 52f, equalPlacement.Top);
	}
}
