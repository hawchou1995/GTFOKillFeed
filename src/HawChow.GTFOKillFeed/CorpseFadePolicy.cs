namespace HawChow.GTFOKillFeed;

internal static class CorpseFadePolicy
{
	internal const float FadeSeconds = 0.5f;

	internal const int MaxConcurrentFades = 2;

	internal const int StopActionDeactivate = 1;

	internal const int MaxBorrowPolls = 3;

	internal const int StopActionNone = 0;

	internal const int StopActionDestroy = 2;

	internal static CorpseFadeSkip SkipReason(in CorpseFadePlan plan)
	{
		if (!plan.FadeEnabled)
		{
			return CorpseFadeSkip.Disabled;
		}
		if (!plan.CorpseEffects)
		{
			return CorpseFadeSkip.CorpseEffectsOff;
		}
		if (!plan.InLevel)
		{
			return CorpseFadeSkip.OffLevel;
		}
		if (plan.Paused)
		{
			return CorpseFadeSkip.Paused;
		}
		if (plan.PlayPatched)
		{
			return CorpseFadeSkip.PatchedByThirdParty;
		}
		if (!plan.PrefabSafe)
		{
			return CorpseFadeSkip.PrefabUnsafe;
		}
		if (plan.Pool == CorpseFadePoolState.Missing)
		{
			return CorpseFadeSkip.PoolMissing;
		}
		if (plan.Pool != CorpseFadePoolState.Ready)
		{
			return CorpseFadeSkip.NoFreeInstance;
		}
		if (!plan.HaveRenderer)
		{
			return CorpseFadeSkip.NoRenderer;
		}
		if (plan.OffScene)
		{
			return CorpseFadeSkip.OffScene;
		}
		if (plan.ActiveFades >= 2)
		{
			return CorpseFadeSkip.ConcurrencyCap;
		}
		return CorpseFadeSkip.None;
	}

	internal static bool ShouldAttempt(in CorpseFadePlan plan)
	{
		return SkipReason(in plan) == CorpseFadeSkip.None;
	}

	internal static bool IsReportable(CorpseFadeSkip skip)
	{
		return skip switch
		{
			CorpseFadeSkip.Paused => true, 
			CorpseFadeSkip.PatchedByThirdParty => true, 
			CorpseFadeSkip.PrefabUnsafe => true, 
			CorpseFadeSkip.PoolMissing => true, 
			CorpseFadeSkip.NoFreeInstance => true, 
			CorpseFadeSkip.ConcurrencyCap => true, 
			_ => false, 
		};
	}

	internal static string Reason(CorpseFadeSkip skip)
	{
		return skip switch
		{
			CorpseFadeSkip.Disabled => "disabled", 
			CorpseFadeSkip.CorpseEffectsOff => "corpse-effects-off", 
			CorpseFadeSkip.OffLevel => "off-level", 
			CorpseFadeSkip.Paused => "paused", 
			CorpseFadeSkip.PatchedByThirdParty => "patched-by-third-party", 
			CorpseFadeSkip.PrefabUnsafe => "prefab-unsafe", 
			CorpseFadeSkip.PoolMissing => "pool-missing", 
			CorpseFadeSkip.NoFreeInstance => "no-free-instance", 
			CorpseFadeSkip.NoRenderer => "no-renderer", 
			CorpseFadeSkip.OffScene => "off-scene", 
			CorpseFadeSkip.ConcurrencyCap => "concurrency-cap", 
			_ => "none", 
		};
	}

	internal static string Reason(CorpseFadeOutcome outcome)
	{
		return outcome switch
		{
			CorpseFadeOutcome.Started => "started", 
			CorpseFadeOutcome.StartedProbeUnknown => "probe-unknown", 
			CorpseFadeOutcome.TargetMissing => "target-missing", 
			CorpseFadeOutcome.ConcurrencyCap => "concurrency-cap", 
			CorpseFadeOutcome.InstanceBusy => "instance-busy", 
			CorpseFadeOutcome.InstanceTracked => "instance-tracked", 
			CorpseFadeOutcome.InstanceGone => "instance-gone", 
			CorpseFadeOutcome.PoolMissing => "pool-missing", 
			CorpseFadeOutcome.NoFreeInstance => "no-free-instance", 
			CorpseFadeOutcome.BorrowError => "borrow-error", 
			CorpseFadeOutcome.ProbeUnknown => "probe-unknown", 
			CorpseFadeOutcome.InitializeFailed => "initialize-failed", 
			CorpseFadeOutcome.InitializeThrew => "initialize-threw", 
			CorpseFadeOutcome.PlayThrew => "play-threw", 
			CorpseFadeOutcome.PlayNotObserved => "play-not-observed", 
			_ => "unexpected", 
		};
	}

	internal static bool IsStarted(CorpseFadeOutcome outcome)
	{
		if (outcome != 0)
		{
			return outcome == CorpseFadeOutcome.StartedProbeUnknown;
		}
		return true;
	}

	internal static bool NativeStopActionSafe(int stopAction)
	{
		return stopAction == 0;
	}

	internal static CorpseFadeNativePath NativePath(in CorpseFadePlan plan, bool haveComponent, int stopAction, bool autoCleanup, bool playing)
	{
		if (!plan.FadeEnabled)
		{
			return CorpseFadeNativePath.Disabled;
		}
		if (!plan.CorpseEffects)
		{
			return CorpseFadeNativePath.CorpseEffectsOff;
		}
		if (!plan.InLevel)
		{
			return CorpseFadeNativePath.OffLevel;
		}
		if (plan.Paused)
		{
			return CorpseFadeNativePath.Paused;
		}
		if (!plan.HaveRenderer)
		{
			return CorpseFadeNativePath.NoRenderer;
		}
		if (plan.OffScene)
		{
			return CorpseFadeNativePath.OffScene;
		}
		if (plan.PlayPatched)
		{
			return CorpseFadeNativePath.PatchedByThirdParty;
		}
		if (!haveComponent)
		{
			return CorpseFadeNativePath.NoComponent;
		}
		if (!NativeStopActionSafe(stopAction) || !autoCleanup)
		{
			return CorpseFadeNativePath.UnsafeComponent;
		}
		if (playing)
		{
			return CorpseFadeNativePath.AlreadyPlaying;
		}
		return CorpseFadeNativePath.Started;
	}

	internal static bool ShouldAttemptNative(CorpseFadeNativePath path)
	{
		return path == CorpseFadeNativePath.Started;
	}

	internal static bool IsReportableNative(CorpseFadeNativePath path)
	{
		return path switch
		{
			CorpseFadeNativePath.NoComponent => true, 
			CorpseFadeNativePath.UnsafeComponent => true, 
			CorpseFadeNativePath.AlreadyPlaying => true, 
			_ => false, 
		};
	}

	internal static bool ShouldLogNativeRefusal(CorpseFadeNativePath path)
	{
		return IsReportableNative(path);
	}

	internal static string Reason(CorpseFadeNativePath path)
	{
		return path switch
		{
			CorpseFadeNativePath.Started => "started", 
			CorpseFadeNativePath.Disabled => "disabled", 
			CorpseFadeNativePath.CorpseEffectsOff => "corpse-effects-off", 
			CorpseFadeNativePath.OffLevel => "off-level", 
			CorpseFadeNativePath.Paused => "paused", 
			CorpseFadeNativePath.OffScene => "off-scene", 
			CorpseFadeNativePath.NoRenderer => "no-renderer", 
			CorpseFadeNativePath.NoComponent => "no-decay-component", 
			CorpseFadeNativePath.UnsafeComponent => "unsafe-decay-component", 
			CorpseFadeNativePath.AlreadyPlaying => "decay-already-playing", 
			CorpseFadeNativePath.PatchedByThirdParty => "patched-by-third-party", 
			_ => "unexpected", 
		};
	}

	internal static bool PrefabIsSafe(int stopAction, bool autoCleanup)
	{
		return stopAction == 1 && autoCleanup;
	}
}
