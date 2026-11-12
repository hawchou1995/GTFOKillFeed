using System;

namespace HawChow.GTFOKillFeed.Tests;

internal static class OpacityTests
{
	internal static void Run()
	{
		DefaultsAndBounds();
		CompositeMultipliesExactlyOnce();
		LifecycleFadeStillReachesZero();
	}

	private static void DefaultsAndBounds()
	{
		Program.Near("opacity default", 0.5f, KillViewPolicy.Opacity(0.5f));
		Program.Near("opacity NaN fallback", 0.5f, KillViewPolicy.Opacity(float.NaN));
		Program.Near("opacity +Inf fallback", 0.5f, KillViewPolicy.Opacity(float.PositiveInfinity));
		Program.Near("opacity -Inf fallback", 0.5f, KillViewPolicy.Opacity(float.NegativeInfinity));
		Program.Near("opacity clamp low", 0f, KillViewPolicy.Opacity(-0.25f));
		Program.Near("opacity clamp high", 1f, KillViewPolicy.Opacity(1.25f));
		Program.Near("opacity zero", 0f, KillViewPolicy.Opacity(0f));
		Program.Near("opacity one", 1f, KillViewPolicy.Opacity(1f));
		Program.Near("opacity half", 0.5f, KillViewPolicy.Opacity(0.5f));
	}

	private static void CompositeMultipliesExactlyOnce()
	{
		Program.Near("composite full fade", 0.5f, KillViewPolicy.CompositeAlpha(1f, 0.5f));
		Program.Near("composite half fade half opacity", 0.25f, KillViewPolicy.CompositeAlpha(0.5f, 0.5f));
		Program.Near("composite half fade full opacity", 0.5f, KillViewPolicy.CompositeAlpha(0.5f, 1f));
		Program.Near("composite zero fade", 0f, KillViewPolicy.CompositeAlpha(0f, 0.5f));
		Program.Near("composite NaN fade", 0f, KillViewPolicy.CompositeAlpha(float.NaN, 0.5f));
		Program.Near("composite NaN opacity", 0.5f, KillViewPolicy.CompositeAlpha(1f, float.NaN));
		Program.Near("composite clamp fade", 0.5f, KillViewPolicy.CompositeAlpha(2f, 0.5f));
		Program.Near("composite clamp opacity", 1f, KillViewPolicy.CompositeAlpha(1f, 1.5f));
		Program.Near("composite negative opacity", 0f, KillViewPolicy.CompositeAlpha(1f, -1f));
	}

	private static void LifecycleFadeStillReachesZero()
	{
		float fade = 0.35f;
		Program.Near("fade tail composite", 0.175f, KillViewPolicy.CompositeAlpha(fade, 0.5f));
		Program.Near("fade expired composite", 0f, KillViewPolicy.CompositeAlpha(0f, 0.5f));
	}
}
