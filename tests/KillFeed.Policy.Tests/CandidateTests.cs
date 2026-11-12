using System;

namespace HawChow.GTFOKillFeed.Tests;

internal static class CandidateTests
{
	internal static void Run()
	{
		NonEnemiesDoNotConsumeCapacity();
		SaturationIsReported();
		InvalidBoxesAreIgnored();
		NullBufferIsRejected();
		DepthDoesNotChangeScreenBox();
	}

	private static void NonEnemiesDoNotConsumeCapacity()
	{
		ScreenBox[] buffer = new ScreenBox[32];
		int count = 0;
		for (int i = 0; i < buffer.Length; i++)
		{
			Program.True("non-enemy not considered", !KillViewPolicy.ShouldConsiderCandidate(isEnemy: false, hasUsableBounds: true));
		}
		Program.True("enemy considered", KillViewPolicy.ShouldConsiderCandidate(isEnemy: true, hasUsableBounds: true));
		Program.True("enemy without bounds rejected", !KillViewPolicy.ShouldConsiderCandidate(isEnemy: true, hasUsableBounds: false));
		for (int i = 0; i < buffer.Length; i++)
		{
			Program.True("append non-enemy", KillViewPolicy.TryAppendCandidate(buffer, ref count, isEnemy: false, new ScreenBox(100f, 100f, 146f, 146f)));
		}
		Program.True("append enemy after 32 non-enemies", KillViewPolicy.TryAppendCandidate(buffer, ref count, true, new ScreenBox(100f, 100f, 146f, 146f)));
		Program.Eq("non-enemy count", 1, count);
	}

	private static void SaturationIsReported()
	{
		ScreenBox[] buffer = new ScreenBox[2];
		int count = 0;
		Program.True("append 1", KillViewPolicy.TryAppendCandidate(buffer, ref count, true, new ScreenBox(1f, 1f, 2f, 2f)));
		Program.True("append 2", KillViewPolicy.TryAppendCandidate(buffer, ref count, true, new ScreenBox(3f, 3f, 4f, 4f)));
		Program.True("append 3 rejected", !KillViewPolicy.TryAppendCandidate(buffer, ref count, true, new ScreenBox(5f, 5f, 6f, 6f)));
		Program.Eq("saturation count", 2, count);
	}

	private static void InvalidBoxesAreIgnored()
	{
		ScreenBox[] buffer = new ScreenBox[4];
		int count = 0;
		Program.True("invalid append accepted", KillViewPolicy.TryAppendCandidate(buffer, ref count, true, default(ScreenBox)));
		Program.Eq("invalid append count", 0, count);
		Program.True("valid after invalid", KillViewPolicy.TryAppendCandidate(buffer, ref count, true, new ScreenBox(1f, 1f, 2f, 2f)));
		Program.Eq("valid after invalid count", 1, count);
	}

	private static void NullBufferIsRejected()
	{
		int count = 0;
		Program.True("null buffer rejected", !KillViewPolicy.TryAppendCandidate(null, ref count, true, new ScreenBox(1f, 1f, 2f, 2f)));
	}

	private static void DepthDoesNotChangeScreenBox()
	{
		float[] xs = new float[2] { 100f, 200f };
		float[] ys = new float[2] { 50f, 150f };
		float[] near = new float[2] { 10f, 10f };
		float[] far = new float[2] { 30f, 30f };
		Program.True("near projects", KillViewPolicy.ProjectBox(xs, ys, near, 2, 0.1f, 1920f, 1080f, out ScreenBox nearBox));
		Program.True("far projects", KillViewPolicy.ProjectBox(xs, ys, far, 2, 0.1f, 1920f, 1080f, out ScreenBox farBox));
		IconPlacement a = KillViewPolicy.ResolvePlacement(new ScreenBox(100f, 100f, 146f, 146f), new ScreenBox[1] { nearBox }, 1, 0f, 1080f);
		IconPlacement b = KillViewPolicy.ResolvePlacement(new ScreenBox(100f, 100f, 146f, 146f), new ScreenBox[1] { farBox }, 1, 0f, 1080f);
		Program.Near("near top equals far top", a.Top, b.Top);
		Program.Near("near lift", 2f, a.Top);
	}
}
