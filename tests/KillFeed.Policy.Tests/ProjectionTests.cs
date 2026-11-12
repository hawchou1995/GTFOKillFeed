using System;

namespace HawChow.GTFOKillFeed.Tests;

internal static class ProjectionTests
{
	internal static void Run()
	{
		FullBox();
		BehindCameraIsRejected();
		MixedDepthUsesInFrontCornersOnly();
		NearClipPlaneIsStrict();
		OffscreenIsRejected();
		PartialOffscreenIsClamped();
		InvalidCornersAreIgnored();
		InvalidInputsAreRejected();
	}

	private static void FullBox()
	{
		float[] xs;
		float[] ys;
		float[] zs;
		Build(100f, 50f, 200f, 150f, 5f, out xs, out ys, out zs);
		Program.True("full box projectable", KillViewPolicy.ProjectBox(xs, ys, zs, 8, 0.1f, 1920f, 1080f, out ScreenBox box));
		Program.Near("full box left", 100f, box.Left);
		Program.Near("full box top", 50f, box.Top);
		Program.Near("full box right", 200f, box.Right);
		Program.Near("full box bottom", 150f, box.Bottom);
	}

	private static void BehindCameraIsRejected()
	{
		float[] xs;
		float[] ys;
		float[] zs;
		Build(100f, 50f, 200f, 150f, -5f, out xs, out ys, out zs);
		Program.True("behind rejected", !KillViewPolicy.ProjectBox(xs, ys, zs, 8, 0.1f, 1920f, 1080f, out ScreenBox _));
	}

	private static void MixedDepthUsesInFrontCornersOnly()
	{
		float[] xs = new float[8] { 100f, 200f, 100f, 200f, 900f, 1000f, 900f, 1000f };
		float[] ys = new float[8] { 100f, 100f, 200f, 200f, 100f, 100f, 200f, 200f };
		float[] zs = new float[8] { 5f, 5f, 5f, 5f, -1f, -1f, -1f, -1f };
		Program.True("mixed projectable", KillViewPolicy.ProjectBox(xs, ys, zs, 8, 0.1f, 1920f, 1080f, out ScreenBox box));
		Program.Near("mixed left", 100f, box.Left);
		Program.Near("mixed right", 200f, box.Right);
	}

	private static void NearClipPlaneIsStrict()
	{
		float[] xs;
		float[] ys;
		float[] zs;
		Build(100f, 50f, 200f, 150f, 0.1f, out xs, out ys, out zs);
		Program.True("near clip equal rejected", !KillViewPolicy.ProjectBox(xs, ys, zs, 8, 0.1f, 1920f, 1080f, out ScreenBox _));
		Build(100f, 50f, 200f, 150f, 0.1001f, out xs, out ys, out zs);
		Program.True("near clip beyond accepted", KillViewPolicy.ProjectBox(xs, ys, zs, 8, 0.1f, 1920f, 1080f, out ScreenBox _));
	}

	private static void OffscreenIsRejected()
	{
		float[] xs;
		float[] ys;
		float[] zs;
		Build(2000f, 50f, 2100f, 150f, 5f, out xs, out ys, out zs);
		Program.True("right offscreen rejected", !KillViewPolicy.ProjectBox(xs, ys, zs, 8, 0.1f, 1920f, 1080f, out ScreenBox _));
		Build(100f, 1100f, 200f, 1200f, 5f, out xs, out ys, out zs);
		Program.True("bottom offscreen rejected", !KillViewPolicy.ProjectBox(xs, ys, zs, 8, 0.1f, 1920f, 1080f, out ScreenBox _));
		Build(-200f, 50f, -100f, 150f, 5f, out xs, out ys, out zs);
		Program.True("left offscreen rejected", !KillViewPolicy.ProjectBox(xs, ys, zs, 8, 0.1f, 1920f, 1080f, out ScreenBox _));
	}

	private static void PartialOffscreenIsClamped()
	{
		float[] xs;
		float[] ys;
		float[] zs;
		Build(-50f, -20f, 100f, 150f, 5f, out xs, out ys, out zs);
		Program.True("partial projectable", KillViewPolicy.ProjectBox(xs, ys, zs, 8, 0.1f, 1920f, 1080f, out ScreenBox box));
		Program.Near("partial left clamp", 0f, box.Left);
		Program.Near("partial top clamp", 0f, box.Top);
		Program.Near("partial right", 100f, box.Right);
		Program.Near("partial bottom", 150f, box.Bottom);
	}

	private static void InvalidCornersAreIgnored()
	{
		float[] xs;
		float[] ys;
		float[] zs;
		Build(100f, 50f, 200f, 150f, 5f, out xs, out ys, out zs);
		xs[0] = float.NaN;
		zs[1] = float.PositiveInfinity;
		Program.True("invalid corner ignored", KillViewPolicy.ProjectBox(xs, ys, zs, 8, 0.1f, 1920f, 1080f, out ScreenBox box));
		Program.Near("invalid corner left", 100f, box.Left);
		Program.Near("invalid corner right", 200f, box.Right);
		for (int i = 0; i < 8; i++)
		{
			xs[i] = float.NaN;
		}
		Program.True("all invalid rejected", !KillViewPolicy.ProjectBox(xs, ys, zs, 8, 0.1f, 1920f, 1080f, out ScreenBox _));
	}

	private static void InvalidInputsAreRejected()
	{
		float[] xs = new float[1] { 100f };
		float[] ys = new float[1] { 50f };
		float[] zs = new float[1] { 5f };
		Program.True("null xs rejected", !KillViewPolicy.ProjectBox(null, ys, zs, 1, 0.1f, 1920f, 1080f, out ScreenBox _));
		Program.True("zero count rejected", !KillViewPolicy.ProjectBox(xs, ys, zs, 0, 0.1f, 1920f, 1080f, out ScreenBox _));
		Program.True("zero screen rejected", !KillViewPolicy.ProjectBox(xs, ys, zs, 1, 0.1f, 0f, 1080f, out ScreenBox _));
		float[] pairX = new float[2] { 100f, 200f };
		float[] pairY = new float[2] { 50f, 150f };
		float[] pairZ = new float[2] { 5f, 5f };
		Program.True("NaN near accepted as zero", KillViewPolicy.ProjectBox(pairX, pairY, pairZ, 2, float.NaN, 1920f, 1080f, out ScreenBox _));
		float[] tiny = new float[2] { 0.05f, 0.05f };
		Program.True("tiny depth accepted with NaN near", KillViewPolicy.ProjectBox(pairX, pairY, tiny, 2, float.NaN, 1920f, 1080f, out ScreenBox _));
	}

	private static void Build(float left, float top, float right, float bottom, float z, out float[] xs, out float[] ys, out float[] zs)
	{
		xs = new float[8] { left, right, left, right, left, right, left, right };
		ys = new float[8] { top, top, bottom, bottom, top, top, bottom, bottom };
		zs = new float[8] { z, z, z, z, z, z, z, z };
	}
}
