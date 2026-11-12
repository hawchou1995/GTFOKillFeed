using System;

namespace HawChow.GTFOKillFeed;

internal static class FocusOutlinePolicy
{
	internal const bool DefaultEnabled = true;

	internal const float BackR = 1f;

	internal const float BackG = 0.541f;

	internal const float BackB = 0.118f;

	internal const float WeakspotR = 0.898f;

	internal const float WeakspotG = 0.282f;

	internal const float WeakspotB = 0.302f;

	internal const float MinThicknessPixels = 1f;

	internal const float MaxThicknessPixels = 3f;

	internal const float ThicknessPerBarHeight = 0.3f;

	internal const float FoilThicknessPerBarHeight = 0.55f;

	internal const float FoilMinThicknessPixels = 2f;

	internal const float FoilMaxThicknessPixels = 5f;

	internal const int FoilFrameSegments = 5;

	internal static FocusOutlineKind Classify(bool onBack, bool onWeakspot)
	{
		if (onBack && onWeakspot)
		{
			return FocusOutlineKind.Prismatic;
		}
		if (onWeakspot)
		{
			return FocusOutlineKind.Weakspot;
		}
		if (onBack)
		{
			return FocusOutlineKind.Back;
		}
		return FocusOutlineKind.None;
	}

	internal static bool ShouldDraw(bool enabled, FocusOutlineKind kind)
	{
		if (enabled)
		{
			return kind != FocusOutlineKind.None;
		}
		return false;
	}

	internal static bool IsBehind(float dot)
	{
		if (float.IsNaN(dot) || float.IsInfinity(dot))
		{
			return false;
		}
		return dot < 0f;
	}

	internal static float DirectionDot(float forwardX, float forwardY, float forwardZ, float toHitX, float toHitY, float toHitZ)
	{
		float num = (float)Math.Sqrt((double)(toHitX * toHitX + toHitY * toHitY + toHitZ * toHitZ));
		if (float.IsNaN(num) || float.IsInfinity(num) || num <= 0.0001f)
		{
			return 0f / 0f;
		}
		float num2 = (forwardX * toHitX + forwardY * toHitY + forwardZ * toHitZ) / num;
		if (!float.IsNaN(num2) && !float.IsInfinity(num2))
		{
			return num2;
		}
		return 0f / 0f;
	}

	internal static float Thickness(float barHeightPixels)
	{
		if (float.IsNaN(barHeightPixels) || float.IsInfinity(barHeightPixels))
		{
			return 1f;
		}
		float num = barHeightPixels * 0.3f;
		if (num < 1f)
		{
			return 1f;
		}
		if (num > 3f)
		{
			return 3f;
		}
		return num;
	}

	internal static float FoilThickness(float barHeightPixels)
	{
		if (float.IsNaN(barHeightPixels) || float.IsInfinity(barHeightPixels))
		{
			return 2f;
		}
		float num = barHeightPixels * 0.55f;
		if (num < 2f)
		{
			return 2f;
		}
		if (num > 5f)
		{
			return 5f;
		}
		return num;
	}

	internal static void FoilFrameColor(int index, int total, float phase, out float red, out float green, out float blue)
	{
		int num = ((total <= 0) ? 1 : total);
		float num2 = (float)(index % num) / (float)num;
		DamageFxPolicy.HueColor(DamageFxPolicy.WrapPhase(phase + num2), 0.55f, 1f, out red, out green, out blue);
	}

	internal static void OutlineColor(FocusOutlineKind kind, float phase, out float red, out float green, out float blue)
	{
		switch (kind)
		{
		case FocusOutlineKind.Back:
			red = 1f;
			green = 0.541f;
			blue = 0.118f;
			break;
		case FocusOutlineKind.Weakspot:
			red = 0.898f;
			green = 0.282f;
			blue = 0.302f;
			break;
		case FocusOutlineKind.Prismatic:
			DamageFxPolicy.PrismaticColor(phase, out red, out green, out blue);
			break;
		default:
			red = 0f;
			green = 0f;
			blue = 0f;
			break;
		}
	}
}
