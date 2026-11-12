using System;
using System.Globalization;

namespace HawChow.GTFOKillFeed;

internal static class FeedbackPolicy
{
	internal enum RagdollLease
	{
		Missing,
		Empty,
		Usable
	}

	internal const float BurstWindow = 0.65f;

	internal const float NumberLifetime = 0.72f;

	internal const float CorpseBurstDelay = 0.1f;

	internal const float MinCorpseBurstDelay = 0.1f;

	internal const float MaxCorpseBurstDelay = 1.5f;

	internal const float CorpseSoundInterval = 0.12f;

	internal const float CorpsePostBurstHold = 0.75f;

	internal const float CorpseCleanupDelay = 0.85f;

	internal const float CorpseOffSceneDistance = 100f;

	internal const int MaxNumbers = 64;

	internal const float SizeBaselineVolume = 1.4f;

	internal const float CorpseBurstSpreadBase = 1.6f;

	internal const float SizeFactorMin = 0.7f;

	internal const float SizeFactorMax = 2.4f;

	internal const float DefaultPanelOpacity = 0f;

	internal const float DefaultMinDistance = 12f;

	internal const float DefaultMaxDistance = 18f;

	internal const float MinLaunchDistanceMetres = 0.5f;

	internal const float MaxLaunchDistanceMetres = 30f;

	internal const float AbsoluteDistanceCeiling = 40f;

	internal const float LaunchClampSlack = 1.25f;

	internal const float DefaultDeathStrength = 0.5f;

	internal const float MinTravelClampMetres = 2.5f;

	internal const float RagdollLookupGrace = 0.3f;

	internal const int MaxRagdollLookupAttempts = 8;

	internal const float FirstLaunchAttemptAge = 0.02f;

	internal const int MaxLaunchApplications = 2;

	internal const string CorpseDespawnReason = "enemy-despawn";

	internal const float CorpseDrag = 0.8f;

	internal const float CorpseGravity = 9.81f;

	internal const float CorpseLaunchHeightMetres = 1f;

	internal const float SlideRecovery = 0.35f;

	internal const float MinLaunchSpeed = 0.1f;

	internal const float MinTravelBudgetMetres = 0.05f;

	internal const float MaxLaunchSpeed = 42f;

	internal const float LaunchVelocitySlack = 1.15f;

	internal const float LegacyVelocityCap = 9f;

	internal const float CorpseFixedTimeStep = 0.02f;

	internal const float MaxDiscreteTravelPerStep = 0.25f;

	internal const float DefaultNumberOpacity = 1f;

	internal const float DefaultNumberWeight = 1f;

	internal const float MinNumberWeight = 0f;

	internal const float MaxNumberWeight = 2f;

	internal const float BoldWeightThreshold = 0.5f;

	internal const int NumberThickenPassesMax = 4;

	internal const bool DefaultApproachEffect = false;

	internal const float ApproachWindowFraction = 1f;

	internal const float ApproachEasePower = 2.6f;

	internal const float ApproachGrowth = 0.45f;

	internal const float ApproachFanFraction = 1.5f;

	internal const float ApproachRiseFraction = 0.25f;

	internal const float ApproachRisePixels = 10f;

	internal const float PrismaticApproachGrowth = 1.45f;

	internal const float PrismaticApproachEasePower = 2.2f;

	internal const float PrismaticApproachFan = 0f;

	internal const float PrismaticApproachRisePixels = 4f;

	internal const float PrismaticApproachRiseFraction = 0f;

	internal const float PrismaticFlyJitterDegrees = 52f;

	internal const float PrismaticSwingPixels = 72f;

	internal const float PrismaticSwingFloor = 0.55f;

	internal const float BodyDropFraction = 0.1f;

	internal const float BodyCrossFraction = 0.78f;

	internal const float NumberFlyJitterDegrees = 58f;

	internal const float ExitOvershootFraction = 0.42f;

	internal static float Clamp(float value, float min, float max, float fallback)
	{
		if (KillPolicy.IsFinite(value))
		{
			return Math.Max(min, Math.Min(max, value));
		}
		return fallback;
	}

	internal static bool IsLeaseCurrent(int captured, int current)
	{
		if (captured > 0)
		{
			return captured == current;
		}
		return false;
	}

	internal static float Unit(float value)
	{
		return Clamp(value, 0f, 1f, 0f);
	}

	internal static float Strength(float burstDamage, float window = 0.65f)
	{
		window = Clamp(window, 0.2f, 1.5f, 0.65f);
		float num = Math.Max(0f, Clamp(burstDamage, 0f, 100000f, 0f)) / window;
		return Unit((float)(Math.Log((double)(1f + num / 35f)) / Math.Log(23.85714340209961)));
	}

	internal static float SizeFactor(float volume)
	{
		if (KillPolicy.IsFinite(volume) && !(volume <= 0f))
		{
			return Clamp((float)Math.Pow((double)(volume / 1.4f), 0.3333333432674408), 0.7f, 2.4f, 1f);
		}
		return 1f;
	}

	private static float BodySize(float sizeFactor)
	{
		return Clamp(sizeFactor, 0.7f, 2.4f, 1f);
	}

	internal static float PanelAlpha(float opacity)
	{
		return Clamp(opacity, 0f, 0.9f, 0f);
	}

	internal static float RuleAlpha(float panelAlpha)
	{
		return Clamp(panelAlpha * 0.55f, 0.05f, 0.35f, 0.22f);
	}

	internal static float TravelBias(float sizeFactor)
	{
		return 0.75f + 0.25f * BodySize(sizeFactor);
	}

	internal static float LaunchBudget(float strength, float minDistance, float maxDistance)
	{
		float num = Clamp(minDistance, 0.5f, 30f, 12f);
		float num2 = Clamp(maxDistance, num, 30f, Math.Max(num, 18f));
		return num + (num2 - num) * Unit(strength);
	}

	internal static float TravelClampFloor(float sizeFactor)
	{
		return Math.Max(2.5f, 2.5f * BodySize(sizeFactor));
	}

	internal static float AppliedTravel(float travel)
	{
		return AppliedTravel(travel, 1f);
	}

	internal static float AppliedTravel(float travel, float sizeFactor)
	{
		return Clamp(travel * 1.25f, TravelClampFloor(sizeFactor), 40f, 12f);
	}

	internal static bool ShouldClampTravel(float distanceSquared, float maxDistance)
	{
		if (KillPolicy.IsFinite(distanceSquared) && KillPolicy.IsFinite(maxDistance) && maxDistance > 0f)
		{
			return distanceSquared > maxDistance * maxDistance;
		}
		return false;
	}

	internal static float GibScale(float sizeFactor)
	{
		return BodySize(sizeFactor);
	}

	internal static float GibScatter(float sizeFactor)
	{
		return 0.6f + 0.4f * BodySize(sizeFactor);
	}

	internal static int BurstCount(float sizeFactor, float strength = 0f)
	{
		float num = Unit((BodySize(sizeFactor) - 0.7f) / 1.7f);
		return 3 + (int)Math.Round((double)(num * 2f), (MidpointRounding)1) + (int)Math.Round((double)(Unit(strength) * 2f), (MidpointRounding)1);
	}

	internal static float BurstSpread(float strength, float sizeFactor)
	{
		return 1.6f * GibScatter(sizeFactor) * (0.8f + 0.4f * Unit(strength));
	}

	internal static float GibLaunchSpeed(float strength)
	{
		return 5.5f + 6.5f * Unit(strength);
	}

	internal static bool ShouldWaitForRagdoll(float age, float burstAt)
	{
		if (KillPolicy.IsFinite(age) && KillPolicy.IsFinite(burstAt))
		{
			return age < Math.Max(0f, burstAt);
		}
		return false;
	}

	internal static float RagdollLookupDeadline(float burstAt)
	{
		return Clamp(burstAt, 0f, 1.5f, 0.1f) + 0.3f;
	}

	internal static bool ShouldRetryRagdollLookup(bool haveRagdoll, int attempts, float age, float burstAt)
	{
		if (!haveRagdoll && attempts >= 0 && attempts < 8 && KillPolicy.IsFinite(age))
		{
			return age <= RagdollLookupDeadline(burstAt);
		}
		return false;
	}

	internal static bool ShouldAbandonRagdollLookup(bool haveRagdoll, float age, float burstAt)
	{
		if (!haveRagdoll)
		{
			if (KillPolicy.IsFinite(age))
			{
				return age > RagdollLookupDeadline(burstAt);
			}
			return true;
		}
		return false;
	}

	internal static RagdollLease ClassifyRagdoll(bool haveLease, int bodyCount, bool leaseCurrent)
	{
		if (haveLease && leaseCurrent)
		{
			if (bodyCount <= 0)
			{
				return RagdollLease.Empty;
			}
			return RagdollLease.Usable;
		}
		return RagdollLease.Missing;
	}

	internal static bool ShouldApplyLaunch(RagdollLease lease)
	{
		return lease == RagdollLease.Usable;
	}

	internal static bool ShouldReplaceRagdollLease(RagdollLease lease)
	{
		return lease == RagdollLease.Empty;
	}

	internal static bool ShouldReapplyLaunch(int applications, bool sameLease, float age, float burstAt)
	{
		if (applications > 0 && applications < 2 && !sameLease && KillPolicy.IsFinite(age))
		{
			return age <= RagdollLookupDeadline(burstAt);
		}
		return false;
	}

	internal static bool IsSameLaunchLease(System.IntPtr appliedPointer, int appliedGeneration, System.IntPtr leasePointer, int leaseGeneration)
	{
		if (appliedPointer != System.IntPtr.Zero && appliedPointer == leasePointer)
		{
			return appliedGeneration == leaseGeneration;
		}
		return false;
	}

	internal static bool CanApplyLaunch(RagdollLease lease, int applications, bool sameLease, float age, float burstAt)
	{
		if (ShouldApplyLaunch(lease))
		{
			if (applications > 0)
			{
				return ShouldReapplyLaunch(applications, sameLease, age, burstAt);
			}
			return true;
		}
		return false;
	}

	internal static bool ShouldCloseAppliedLaunch(int applications, float age, float burstAt)
	{
		if (applications < 2 && KillPolicy.IsFinite(age))
		{
			return age > RagdollLookupDeadline(burstAt);
		}
		return true;
	}

	internal static bool ShouldKeepCorpseForPendingLaunch(RagdollLease lease, int applications, string reason)
	{
		if (applications == 0 && lease == RagdollLease.Usable)
		{
			return reason == "enemy-despawn";
		}
		return false;
	}

	internal static bool ShouldReportUnavailableLaunch(int applications, bool alreadyReported)
	{
		if (applications == 0)
		{
			return !alreadyReported;
		}
		return false;
	}

	internal static string LaunchState(int applications, bool closed)
	{
		if (applications <= 0)
		{
			if (!closed)
			{
				return "pending";
			}
			return "unavailable";
		}
		return "applied#" + applications.ToString((IFormatProvider)(object)CultureInfo.InvariantCulture);
	}

	internal static string LadderText(string tried, string last)
	{
		if (string.IsNullOrEmpty(tried))
		{
			if (string.IsNullOrEmpty(last))
			{
				return "none";
			}
			return last;
		}
		return tried;
	}

	internal static bool EndAtCoversLaunchLookup(float holdSeconds, float grace)
	{
		return holdSeconds > grace;
	}

	internal static bool ShouldKeepShotgunDamageNumbers(bool showShotgunNumbers, bool isShotgun, bool isPreview)
	{
		return !isShotgun || isPreview || showShotgunNumbers;
	}

	internal static bool ShouldMergeShotgunDamageNumbers(bool isShotgun, bool mergePellets)
	{
		return isShotgun && mergePellets;
	}

	internal static bool ShouldDisplayDamageNumber(bool masterEnabled, bool showShotgunNumbers, bool isShotgun)
	{
		if (masterEnabled)
		{
			return !isShotgun || showShotgunNumbers;
		}
		return false;
	}

	internal static bool IsShotgunFireScopeCurrent(int fireFrame, int currentFrame, uint fireCategoryId, uint hitCategoryId)
	{
		if (fireFrame == currentFrame)
		{
			if (fireCategoryId != 0 && hitCategoryId != 0)
			{
				return fireCategoryId == hitCategoryId;
			}
			return true;
		}
		return false;
	}

	internal static bool ShouldSuppressShotgunDamageNumber(bool scopeMatches, bool preview, bool showShotgunNumbers)
	{
		if (scopeMatches && !preview)
		{
			return !showShotgunNumbers;
		}
		return false;
	}

	internal static bool IsCorpseOffSceneDistanceSquared(float distanceSquared)
	{
		if (KillPolicy.IsFinite(distanceSquared))
		{
			return distanceSquared > 10000f;
		}
		return true;
	}

	internal static bool ShouldUseCapturedBurstOrigin(float enemyDistanceSquared, float bodyDistanceSquared)
	{
		if (!IsCorpseOffSceneDistanceSquared(enemyDistanceSquared))
		{
			return IsCorpseOffSceneDistanceSquared(bodyDistanceSquared);
		}
		return true;
	}

	internal static bool ShouldBreakCorpseLimbs(bool offSceneCleanupApplied)
	{
		return !offSceneCleanupApplied;
	}

	internal static bool ShouldPlayCorpseBurstFx(bool corpseEffects, bool explosionFx)
	{
		return corpseEffects && explosionFx;
	}

	internal static CorpseBurstPlan BurstPlan(bool corpseEffects, bool explosionFx, float sizeFactor, float strength)
	{
		if (!ShouldPlayCorpseBurstFx(corpseEffects, explosionFx))
		{
			return new CorpseBurstPlan(0, sound: false);
		}
		return new CorpseBurstPlan(BurstCount(sizeFactor, strength), sound: true);
	}

	internal static float Travel(float strength)
	{
		return LaunchBudget(strength, 12f, 18f);
	}

	internal static float Travel(float strength, float sizeFactor)
	{
		return Travel(strength) * TravelBias(sizeFactor);
	}

	internal static float Travel(float strength, float minDistance, float maxDistance, float sizeFactor)
	{
		return LaunchBudget(strength, minDistance, maxDistance) * TravelBias(sizeFactor);
	}

	internal static bool NeedsContinuousCollision(float speed)
	{
		if (KillPolicy.IsFinite(speed))
		{
			return speed * 0.02f > 0.25f;
		}
		return false;
	}

	internal static float LaunchUpSpeed(float strength)
	{
		return 1.6f + Unit(strength);
	}

	internal static float LaunchAirTime(float strength)
	{
		float num = LaunchUpSpeed(strength);
		return (num + (float)Math.Sqrt((double)(num * num + 19.62f))) / 9.81f;
	}

	internal static float LaunchTravelFraction(float strength)
	{
		float num = (float)Math.Exp((double)(-0.8f * LaunchAirTime(strength)));
		return 1f - num + 0.35f * num;
	}

	internal static float LaunchSpeedRaw(float travel, float strength)
	{
		float num = LaunchTravelFraction(strength);
		if (!KillPolicy.IsFinite(num) || num <= 0.05f)
		{
			num = 0.05f;
		}
		float num2 = (KillPolicy.IsFinite(travel) ? Math.Max(travel, 0.05f) : 0.05f);
		if (num2 > 40f)
		{
			num2 = 40f;
		}
		return num2 * 0.8f / num;
	}

	internal static float LaunchSpeedFor(float travel, float strength)
	{
		return Clamp(LaunchSpeedRaw(travel, strength), 0.1f, 42f, 0.1f);
	}

	internal static bool LaunchSpeedCapped(float travel, float strength)
	{
		return LaunchSpeedRaw(travel, strength) > 42f;
	}

	internal static float LaunchDistance(float speed, float strength)
	{
		return Clamp(speed, 0f, 42f, 0f) / 0.8f * LaunchTravelFraction(strength);
	}

	internal static float LaunchVelocityCap(float speed)
	{
		return Math.Max(9f, Clamp(speed, 0f, 42f, 0f) * 1.15f);
	}

	internal static int Fragments(float strength)
	{
		return 2 + (int)Math.Floor((double)(4f * Unit(strength)));
	}

	internal static float FragmentRadius(float strength)
	{
		return 0.65f + 1.35f * Unit(strength);
	}

	internal static float FragmentRadius(float strength, float sizeFactor)
	{
		return FragmentRadius(strength) * GibScatter(sizeFactor);
	}

	internal static string DamageText(float value, HitKind kind)
	{
		if (!KillPolicy.IsFinite(value) || value < 0f)
		{
			return string.Empty;
		}
		string text = ((value > 0f && value < 0.01f) ? value.ToString("0.####", (IFormatProvider)(object)CultureInfo.InvariantCulture) : value.ToString("0.##", (IFormatProvider)(object)CultureInfo.InvariantCulture));
		return kind switch
		{
			HitKind.Armor => "[" + text + "]", 
			HitKind.Weakspot => text + "!", 
			_ => text, 
		};
	}

	internal static float NumberScale(float age, HitKind kind)
	{
		float num = Unit(age / 0.14f);
		float num2 = (float)Math.Sin((double)num * Math.PI) * (1f - num);
		return kind switch
		{
			HitKind.Armor => 0.82f, 
			HitKind.Weakspot => 1.18f, 
			_ => 1f, 
		} * (1f + 0.28f * num2);
	}

	internal static float Alpha(float age, float lifetime)
	{
		return Unit((lifetime - age) / 0.22f);
	}

	internal static float NumberOpacity(float opacity)
	{
		return Clamp(opacity, 0f, 1f, 1f);
	}

	internal static bool ShowsNumber(float opacity)
	{
		return NumberOpacity(opacity) > 0f;
	}

	internal static float NumberAlpha(float age, float lifetime, float opacity)
	{
		return Alpha(age, lifetime) * NumberOpacity(opacity);
	}

	internal static float NumberTextAlpha(float colorAlpha, float alpha)
	{
		return colorAlpha * alpha;
	}

	internal static float NumberWeight(float configured)
	{
		return Clamp(configured, 0f, 2f, 1f);
	}

	internal static bool NumberUsesBold(float configured)
	{
		return NumberWeight(configured) >= 0.5f;
	}

	internal static float NumberOutlineRadius(float configured)
	{
		return 0.9f + 0.7f * NumberWeight(configured);
	}

	internal static int NumberThickenPasses(float configured)
	{
		if (!(NumberWeight(configured) <= 1f))
		{
			return 4;
		}
		return 0;
	}

	internal static float NumberThickenRadius(float configured)
	{
		return (NumberWeight(configured) - 1f) * 0.55f;
	}

	internal static float FlyJitter(int seed)
	{
		int num = seed * -1640531535;
		int num2 = (int)((uint)num ^ ((uint)num >> 15)) * -2048144777;
		return (float)(((uint)num2 ^ ((uint)num2 >> 13)) & 0xFFFFu) / 65535f * 2f - 1f;
	}

	internal static float FlyJitterRadians(int seed)
	{
		return FlyJitter(seed) * 0.9075712f;
	}

	internal static void RotateAroundTarget(float x, float y, float targetX, float targetY, float radians, out float rotatedX, out float rotatedY)
	{
		rotatedX = x;
		rotatedY = y;
		if (KillPolicy.IsFinite(x) && KillPolicy.IsFinite(y) && KillPolicy.IsFinite(targetX) && KillPolicy.IsFinite(targetY) && KillPolicy.IsFinite(radians))
		{
			float num = x - targetX;
			float num2 = y - targetY;
			float num3 = (float)Math.Cos((double)radians);
			float num4 = (float)Math.Sin((double)radians);
			rotatedX = targetX + num * num3 - num2 * num4;
			rotatedY = targetY + num * num4 + num2 * num3;
		}
	}

	internal static void PrismaticFlightPoint(int seed, float approach, float startX, float startY, float targetX, float targetY, float resolution, out float x, out float y, out float straightX, out float straightY)
	{
		float num = Unit(approach);
		straightX = ApproachAxis(startX, targetX, num);
		straightY = ApproachAxis(startY, targetY, num);
		x = straightX;
		y = straightY;
		if (!KillPolicy.IsFinite(x) || !KillPolicy.IsFinite(y) || !KillPolicy.IsFinite(targetX) || !KillPolicy.IsFinite(targetY))
		{
			return;
		}
		RotateAroundTarget(x, y, targetX, targetY, FlyJitterRadians(seed) * num, out x, out y);
		float num2 = PrismaticSwing(seed, num, resolution);
		if (num2 != 0f)
		{
			float num3 = targetX - startX;
			float num4 = targetY - startY;
			float num5 = (float)Math.Sqrt((double)(num3 * num3 + num4 * num4));
			if (KillPolicy.IsFinite(num5) && !(num5 <= 0.0001f))
			{
				x += num4 / num5 * num2;
				y += (0f - num3) / num5 * num2;
			}
		}
	}

	internal static void PrismaticFlightPoint(int seed, float approach, float startX, float startY, float targetX, float targetY, float resolution, out float x, out float y)
	{
		PrismaticFlightPoint(seed, approach, startX, startY, targetX, targetY, resolution, out x, out y, out var _, out var _);
	}

	internal static float NumberFlyRadians(int seed)
	{
		return FlyJitter(seed) * ((float)Math.PI * 29f / 90f);
	}

	internal static void FlightTarget(float centreX, float centreY, float screenHeight, int seed, out float bodyX, out float bodyY, out float exitX, out float exitY)
	{
		bodyX = centreX;
		bodyY = centreY;
		exitX = centreX;
		exitY = centreY;
		if (KillPolicy.IsFinite(centreX) && KillPolicy.IsFinite(centreY) && KillPolicy.IsFinite(screenHeight))
		{
			float num = Math.Max(1f, screenHeight);
			bodyY = centreY + 0.1f * num;
			float num2 = NumberFlyRadians(seed);
			float num3 = (float)Math.Cos((double)num2);
			float num4 = Math.Max(0.05f, 0.55f + (float)Math.Sin((double)num2) * 0.65f);
			float num5 = 0.42f * num;
			exitX = bodyX + num3 * num5;
			exitY = bodyY + num4 * num5;
		}
	}

	internal static float FlightProgress(float age, float lifetime)
	{
		float num = Math.Max(0.01f, lifetime * 1f);
		return Unit(age / num);
	}

	internal static float SegmentedAxis(float start, float middle, float end, float t)
	{
		if (!KillPolicy.IsFinite(start) || !KillPolicy.IsFinite(middle) || !KillPolicy.IsFinite(end))
		{
			return start;
		}
		float num = Unit(t);
		if (num <= 0.78f)
		{
			float num2 = num / 0.78f;
			return start + (middle - start) * num2;
		}
		float num3 = (num - 0.78f) / Math.Max(0.0001f, 0.22000003f);
		return middle + (end - middle) * num3;
	}

	internal static float ApproachT(float age, float lifetime)
	{
		float num = Math.Max(0.01f, lifetime * 1f);
		float num2 = Unit(age / num);
		if (num2 <= 0f)
		{
			return 0f;
		}
		if (num2 >= 1f)
		{
			return 1f;
		}
		return (float)Math.Pow((double)num2, 2.5999999046325684);
	}

	internal static float ApproachAxis(float start, float target, float t)
	{
		if (!KillPolicy.IsFinite(start) || !KillPolicy.IsFinite(target))
		{
			return start;
		}
		return start + (target - start) * Unit(t);
	}

	internal static float ApproachScale(float t)
	{
		return 1f + 0.45f * Unit(t);
	}

	internal static float PrismaticApproachScale(float t)
	{
		return 1f + 1.45f * Unit(t);
	}

	internal static float PrismaticApproachT(float age, float lifetime)
	{
		float num = Math.Max(0.01f, lifetime * 1f);
		float num2 = Unit(age / num);
		if (num2 <= 0f)
		{
			return 0f;
		}
		if (num2 >= 1f)
		{
			return 1f;
		}
		return (float)Math.Pow((double)num2, 2.200000047683716);
	}

	internal static float PrismaticSwing(int seed, float approach, float resolution)
	{
		float num = Unit(approach);
		if (num <= 0f || num >= 1f)
		{
			return 0f;
		}
		if (!KillPolicy.IsFinite(resolution) || resolution <= 0f)
		{
			return 0f;
		}
		float num2 = FlyJitter(seed);
		float num3 = 0.55f + 0.45f * Math.Abs(num2);
		return ((num2 < 0f) ? (-1f) : 1f) * num3 * 72f * resolution * (float)Math.Sin(Math.PI * (double)num);
	}

	internal static float ApproachTargetX(float centreX, float side, float resolution, bool prismatic = false)
	{
		if (!KillPolicy.IsFinite(centreX) || !KillPolicy.IsFinite(side) || !KillPolicy.IsFinite(resolution))
		{
			return centreX;
		}
		float num = (prismatic ? 0f : 1.5f);
		return centreX + side * num * resolution;
	}

	internal static float ApproachTargetY(float centreY, float side, float resolution, bool prismatic = false)
	{
		if (!KillPolicy.IsFinite(centreY) || !KillPolicy.IsFinite(side) || !KillPolicy.IsFinite(resolution))
		{
			return centreY;
		}
		float num = (prismatic ? 4f : 10f);
		float num2 = (prismatic ? 0f : 0.25f);
		return centreY - (num + Math.Abs(side) * num2) * resolution;
	}

	internal static float NumberSide(int sequence)
	{
		return (float)(sequence % 7 - 3) * 11f;
	}

	internal static float NumberDriftX(float hitX, float side, float age, float resolution)
	{
		return hitX + side * (0.35f + age * 1.7f) * resolution;
	}

	internal static float NumberDriftY(float hitY, float age, float resolution)
	{
		return hitY + (-95f * age + 105f * age * age) * resolution;
	}

	internal static void NumberPose(bool prismatic, bool approach, int seed, float side, float age, float lifetime, float opacity, float hitX, float hitY, float width, float height, float resolution, HitKind kind, float baseSize, out float x, out float y, out float size, out float alpha, out float straightX, out float straightY)
	{
		float num = NumberDriftX(hitX, side, age, resolution);
		float num2 = NumberDriftY(hitY, age, resolution);
		alpha = NumberAlpha(age, lifetime, opacity);
		if (!approach)
		{
			x = num;
			y = num2;
			size = Clamp(baseSize, 16f, 42f, 27f) * NumberScale(age, kind) * resolution;
			straightX = num;
			straightY = num2;
			return;
		}
		float num3 = (prismatic ? PrismaticApproachT(age, lifetime) : ApproachT(age, lifetime));
		size = Clamp(baseSize, 16f, 42f, 27f) * NumberScale(age, kind) * (prismatic ? PrismaticApproachScale(num3) : ApproachScale(num3)) * resolution;
		if (prismatic)
		{
			float targetX = ApproachTargetX(width * 0.5f, side, resolution, prismatic: true);
			float targetY = ApproachTargetY(height * 0.5f, side, resolution, prismatic: true);
			PrismaticFlightPoint(seed, num3, num, num2, targetX, targetY, resolution, out x, out y, out straightX, out straightY);
			return;
		}
		FlightTarget(width * 0.5f, height * 0.5f, height, seed, out var bodyX, out var bodyY, out var exitX, out var exitY);
		float t = FlightProgress(age, lifetime);
		x = SegmentedAxis(num, bodyX, exitX, t);
		y = SegmentedAxis(num2, bodyY, exitY, t);
		straightX = bodyX;
		straightY = bodyY;
	}

	internal static float CardWidth(float iconWidth, float textWidth, float scale, float maximumWidth)
	{
		float num = Clamp(maximumWidth, 0f, 10000f, 0f);
		float num2 = Math.Max(0f, iconWidth) + 30f * Clamp(scale, 0.5f, 2f, 1f) + Math.Max(0f, Clamp(textWidth, 0f, 10000f, 0f));
		return Math.Max(0f, Math.Min(num, num2));
	}

	internal static float CenteredCardX(float screenWidth, float cardWidth, float offset, float margin = 12f)
	{
		float num = Clamp(screenWidth, 1f, 100000f, 1f);
		float num2 = Clamp(margin, 0f, num * 0.5f, 0f);
		float num3 = Clamp(cardWidth, 0f, Math.Max(0f, num - 2f * num2), Math.Max(0f, num - 2f * num2));
		float num4 = (num - num3) * 0.5f;
		return Clamp(num4 + offset, num2, Math.Max(num2, num - num3 - num2), num4);
	}

	internal static string CardDetail(KillCard card, bool distance, bool health)
	{
		string text = (distance ? (card.DistanceText + "   /   ") : string.Empty);
		if (health)
		{
			text = text + card.HealthText + "   /   ";
		}
		return text + card.EnemyName;
	}
}
