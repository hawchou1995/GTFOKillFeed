namespace HawChow.GTFOKillFeed;

internal static class HitMarkerPolicy
{
	internal const bool DefaultEnabled = true;

	internal const float DefaultSize = 64f;

	internal const float MinSize = 16f;

	internal const float MaxSize = 200f;

	internal const float DefaultHitDurationSeconds = 0.18f;

	internal const float DefaultKillDurationSeconds = 0.34f;

	internal const float MinDurationSeconds = 0.05f;

	internal const float MaxDurationSeconds = 1f;

	internal const int DefaultCapacity = 12;

	internal const int MaxCapacity = 24;

	internal const float ImpactMatchDistanceMetres = 0.25f;

	internal const float ImpactMatchDistanceSquared = 0.0625f;

	internal const int ImpactFrameWindow = 1;

	internal const int MaxImpactDepth = 8;

	internal const float WeakspotSizeScale = 1.18f;

	internal const float ArmorSizeScale = 0.92f;

	internal const float KillSizeScale = 1.35f;

	internal const string DefaultNormalColor = "#FFFFFF";

	internal const string DefaultWeakspotColor = "#FF8C00";

	internal const string DefaultArmorColor = "#49ADC9";

	internal const string DefaultKillColor = "#B92828";

	internal static float Size(float configured)
	{
		if (float.IsNaN(configured) || float.IsInfinity(configured))
		{
			return 64f;
		}
		if (configured < 16f)
		{
			return 16f;
		}
		if (configured > 200f)
		{
			return 200f;
		}
		return configured;
	}

	internal static float Duration(float configured)
	{
		if (float.IsNaN(configured) || float.IsInfinity(configured))
		{
			return 0.18f;
		}
		if (configured < 0.05f)
		{
			return 0.05f;
		}
		if (configured > 1f)
		{
			return 1f;
		}
		return configured;
	}

	internal static float Lifetime(HitMarkerKind kind)
	{
		if (kind != HitMarkerKind.Kill)
		{
			return 0.18f;
		}
		return 0.34f;
	}

	internal static int Capacity(int configured)
	{
		if (configured <= 0)
		{
			return 12;
		}
		if (configured <= 24)
		{
			return configured;
		}
		return 24;
	}

	internal static float Alpha(float age, float lifetime)
	{
		if (float.IsNaN(age) || float.IsInfinity(age))
		{
			return 0f;
		}
		float num = Duration(lifetime);
		if (age < 0f)
		{
			return 0f;
		}
		if (age >= num)
		{
			return 0f;
		}
		float num2 = 1f - age / num;
		if (!(num2 < 0f))
		{
			if (!(num2 > 1f))
			{
				return num2;
			}
			return 1f;
		}
		return 0f;
	}

	internal static bool ShouldDraw(bool enabled, HitMarkerKind kind, float alpha)
	{
		if (enabled && kind != 0 && !float.IsNaN(alpha))
		{
			return alpha > 0f;
		}
		return false;
	}

	internal static HitMarkerKind KindFor(bool weakspot, bool armor, bool kill)
	{
		if (!kill)
		{
			if (!armor)
			{
				if (!weakspot)
				{
					return HitMarkerKind.Normal;
				}
				return HitMarkerKind.Weakspot;
			}
			return HitMarkerKind.Armor;
		}
		return HitMarkerKind.Kill;
	}

	internal static string IconKey(HitMarkerKind kind)
	{
		return kind switch
		{
			HitMarkerKind.Normal => "hitmark-normal", 
			HitMarkerKind.Kill => "hitmark-kill", 
			HitMarkerKind.Armor => "hitmark-armor", 
			HitMarkerKind.Weakspot => "hitmark-weakspot", 
			_ => string.Empty, 
		};
	}

	internal static string IconFile(HitMarkerKind kind)
	{
		if (kind != 0)
		{
			return IconKey(kind) + ".png";
		}
		return string.Empty;
	}

	internal static float SizeScale(HitMarkerKind kind)
	{
		return kind switch
		{
			HitMarkerKind.Kill => 1.35f, 
			HitMarkerKind.Armor => 0.92f, 
			HitMarkerKind.Weakspot => 1.18f, 
			_ => 1f, 
		};
	}

	internal static bool IsProjectable(float z, float x, float y, float screenWidth, float screenHeight, float margin)
	{
		if (!IsFinite(z) || !IsFinite(x) || !IsFinite(y))
		{
			return false;
		}
		if (!IsFinite(screenWidth) || !IsFinite(screenHeight) || screenWidth <= 0f || screenHeight <= 0f)
		{
			return false;
		}
		if (margin < 0f || float.IsNaN(margin))
		{
			margin = 0f;
		}
		if (z <= 0.05f)
		{
			return false;
		}
		if (x >= 0f - margin && x <= screenWidth + margin && y >= 0f - margin)
		{
			return y <= screenHeight + margin;
		}
		return false;
	}

	internal static bool IsFinite(float value)
	{
		if (!float.IsNaN(value))
		{
			return !float.IsInfinity(value);
		}
		return false;
	}

	internal static float DistanceSquared(float ax, float ay, float az, float bx, float by, float bz)
	{
		if (!IsFinite(ax) || !IsFinite(ay) || !IsFinite(az) || !IsFinite(bx) || !IsFinite(by) || !IsFinite(bz))
		{
			return 1f / 0f;
		}
		float num = ax - bx;
		float num2 = ay - by;
		float num3 = az - bz;
		return num * num + num2 * num2 + num3 * num3;
	}

	internal static bool ImpactMatches(int currentFrame, int scopeFrame, float distanceSquared)
	{
		if (ImpactFrameCurrent(currentFrame, scopeFrame))
		{
			return distanceSquared <= 0.0625f;
		}
		return false;
	}

	internal static bool ImpactFrameCurrent(int currentFrame, int scopeFrame)
	{
		if (currentFrame < scopeFrame)
		{
			return false;
		}
		return currentFrame - scopeFrame <= 1;
	}

	internal static bool TryDecide(bool enabled, bool friendly, bool noDamage, bool scopeMatched, HitKind scopeKind, bool weakspot, out HitMarkerKind kind)
	{
		kind = HitMarkerKind.None;
		if (!enabled || friendly)
		{
			return false;
		}
		bool flag = scopeMatched && scopeKind == HitKind.Armor;
		if (noDamage)
		{
			if (!flag)
			{
				return false;
			}
			kind = HitMarkerKind.Armor;
			return true;
		}
		kind = KindFor(weakspot, flag, kill: false);
		return true;
	}

	internal static HitMarkerAction Resolve(HitMarkerKind existing, int existingIdentity, HitMarkerKind incoming, int incomingIdentity)
	{
		if (incoming == HitMarkerKind.None)
		{
			return HitMarkerAction.Dropped;
		}
		if (existing == HitMarkerKind.None)
		{
			return HitMarkerAction.Added;
		}
		if (incomingIdentity <= 0 || incomingIdentity != existingIdentity)
		{
			return HitMarkerAction.Added;
		}
		if (incoming == HitMarkerKind.Kill && existing != HitMarkerKind.Kill)
		{
			return HitMarkerAction.Upgraded;
		}
		return HitMarkerAction.Merged;
	}
}
