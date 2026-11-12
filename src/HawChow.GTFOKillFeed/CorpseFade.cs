using System;
using System.Reflection;
using BepInEx.Core.Logging.Interpolation;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace HawChow.GTFOKillFeed;

internal static class CorpseFade
{
	private sealed class DecayFadeTarget : ICorpseFadeTarget
	{
		public CorpseFadeBorrow TryBorrow(out object handle)
		{
			handle = null;
			ObjectPooler.Pool pool = Decay.pool;
			if (pool == null)
			{
				return CorpseFadeBorrow.PoolMissing;
			}
			if (pool.freeInstanceCount <= 0)
			{
				return CorpseFadeBorrow.NoFreeInstance;
			}
			Decay instance = Decay.GetInstance();
			if (instance == null)
			{
				return CorpseFadeBorrow.NoFreeInstance;
			}
			handle = instance;
			return CorpseFadeBorrow.Ok;
		}

		public CorpseFadeState Probe(object handle)
		{
			if (!(handle is Decay decay))
			{
				return CorpseFadeState.Gone;
			}
			if (decay == null)
			{
				return CorpseFadeState.Gone;
			}
			if (!decay.m_playing)
			{
				return CorpseFadeState.Idle;
			}
			return CorpseFadeState.Playing;
		}

		public bool Initialize(object handle, object renderer)
		{
			if (!(handle is Decay decay))
			{
				return false;
			}
			if (renderer is SkinnedMeshRenderer smr)
			{
				decay.Initialize(smr);
				return TargetsRenderer(decay);
			}
			if (renderer is MeshRenderer mr)
			{
				decay.Initialize(mr);
				return TargetsRenderer(decay);
			}
			return false;
		}

		private static bool TargetsRenderer(Decay decay)
		{
			Renderer targetRenderer = decay.m_targetRenderer;
			if (targetRenderer != null)
			{
				return targetRenderer.Pointer != System.IntPtr.Zero;
			}
			return false;
		}

		public void Play(object handle, float seconds)
		{
			if (handle is Decay decay)
			{
				decay.Play(seconds);
			}
		}

		public void Cleanup(object handle)
		{
			if (handle is Decay decay)
			{
				decay.Cleanup();
			}
		}

		public void Return(object handle)
		{
			if (handle is Decay decay)
			{
				Decay.pool?.Return(decay.gameObject);
			}
		}
	}

	private static readonly CorpseFadeCoordinator Coordinator = new CorpseFadeCoordinator();

	private static readonly DecayFadeTarget Target = new DecayFadeTarget();

	private static readonly CorpseFadeDiagnostics Diagnostics = new CorpseFadeDiagnostics();

	private static bool _patchChecked;

	private static bool _playPatched;

	internal static int Active => Coordinator.Active;

	internal static void TryBegin(Renderer renderer, bool offScene, bool inLevel, int scheduleId, Decay native = null)
	{
		try
		{
			if (!_patchChecked)
			{
				_playPatched = PlayIsPatched();
				_patchChecked = true;
				if (_playPatched && Diagnostics.AllowLine(CorpseFadePolicy.Reason(CorpseFadeSkip.PatchedByThirdParty)))
				{
					Plugin.Instance?.Log.LogInfo("[CORPSE] fade=disabled;reason=" + CorpseFadePolicy.Reason(CorpseFadeSkip.PatchedByThirdParty) + ";detail=Decay.Play has third-party patches");
				}
			}
			CorpseFadePlan plan = new CorpseFadePlan(Plugin.CorpseFade != null && Plugin.CorpseFade.Value, Plugin.CorpseEffects == null || Plugin.CorpseEffects.Value, inLevel, Paused(), _playPatched, PrefabIsSafe(), PoolState(), renderer != null && (object)renderer != null, offScene, Coordinator.Active);
			if (!TryNative(in plan, native, renderer, scheduleId))
			{
				CorpseFadeOutcome outcome;
				if (!CorpseFadePolicy.ShouldAttempt(in plan))
				{
					LogSkip(CorpseFadePolicy.SkipReason(in plan), scheduleId);
				}
				else if (!Coordinator.TryStart(Target, renderer, out outcome))
				{
					LogFailure(outcome, scheduleId);
				}
				else if (outcome == CorpseFadeOutcome.StartedProbeUnknown)
				{
					LogStartedUnknown(scheduleId, renderer);
				}
				else
				{
					LogStarted(scheduleId, renderer);
				}
			}
		}
		catch (System.Exception error)
		{
			Warn(error);
		}
	}

	private static bool TryNative(in CorpseFadePlan plan, Decay native, Renderer renderer, int scheduleId)
	{
		int num = -1;
		bool flag = false;
		bool playing = true;
		if (native != null)
		{
			try
			{
				num = (int)native.m_stopAction;
				flag = native.m_autoCleanup;
				playing = native.m_playing;
			}
			catch (System.Exception error)
			{
				Warn(error);
				native = null;
			}
		}
		CorpseFadeNativePath path = CorpseFadePolicy.NativePath(in plan, native != null, num, flag, playing);
		if (!CorpseFadePolicy.ShouldAttemptNative(path))
		{
			LogNativeUnavailable(path, num, flag, playing, scheduleId);
			return false;
		}
		try
		{
			if (renderer is SkinnedMeshRenderer smr)
			{
				native.Initialize(smr);
			}
			else
			{
				if (!(renderer is MeshRenderer mr))
				{
					LogFallback("native-unsupported-renderer", scheduleId);
					return false;
				}
				native.Initialize(mr);
			}
			native.Play(0.5f);
		}
		catch (System.Exception error2)
		{
			Warn(error2);
			LogFallback("native-threw", scheduleId);
			return false;
		}
		bool flag2 = false;
		try
		{
			flag2 = native.m_playing;
		}
		catch (System.Exception error3)
		{
			Warn(error3);
		}
		if (!flag2)
		{
			LogFallback("native-not-playing", scheduleId);
			return false;
		}
		if (Diagnostics.AllowLine("begun"))
		{
			Plugin instance = Plugin.Instance;
			if (instance != null)
			{
				ManualLogSource log = instance.Log;
				bool isEnabled;
				BepInExInfoLogInterpolatedStringHandler bepInExInfoLogInterpolatedStringHandler = new BepInExInfoLogInterpolatedStringHandler(129, 5, out isEnabled);
				if (isEnabled)
				{
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral("[CORPSE] fade=begun;path=native-decay;seconds=");
					((BepInExLogInterpolatedStringHandler)bepInExInfoLogInterpolatedStringHandler).AppendFormatted<float>(0.5f, "0.00");
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral(";renderer=");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted(((MemberInfo)((object)renderer).GetType()).Name);
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral(";stopAction=");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted(num);
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral(";autoCleanup=");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted(flag);
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral(";scheduleId=");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted(scheduleId);
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral(";note=corpse-own-component-no-borrow");
				}
				log.LogInfo(bepInExInfoLogInterpolatedStringHandler);
			}
		}
		return true;
	}

	private static void LogFallback(string reason, int scheduleId)
	{
		if (!Diagnostics.AllowLine(reason))
		{
			return;
		}
		Plugin instance = Plugin.Instance;
		if (instance != null)
		{
			ManualLogSource log = instance.Log;
			bool isEnabled;
			BepInExInfoLogInterpolatedStringHandler bepInExInfoLogInterpolatedStringHandler = new BepInExInfoLogInterpolatedStringHandler(67, 2, out isEnabled);
			if (isEnabled)
			{
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral("[CORPSE] fade=fallback;reason=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(reason);
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral(";scheduleId=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(scheduleId);
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral(";note=pooled-path-or-hide");
			}
			log.LogInfo(bepInExInfoLogInterpolatedStringHandler);
		}
	}

	private static void LogNativeUnavailable(CorpseFadeNativePath path, int stopAction, bool autoCleanup, bool playing, int scheduleId)
	{
		if (!CorpseFadePolicy.ShouldLogNativeRefusal(path) || !Diagnostics.AllowLine(CorpseFadePolicy.Reason(path)))
		{
			return;
		}
		Plugin instance = Plugin.Instance;
		if (instance != null)
		{
			ManualLogSource log = instance.Log;
			bool isEnabled;
			BepInExInfoLogInterpolatedStringHandler bepInExInfoLogInterpolatedStringHandler = new BepInExInfoLogInterpolatedStringHandler(86, 5, out isEnabled);
			if (isEnabled)
			{
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral("[CORPSE] fade=native-unavailable;reason=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(CorpseFadePolicy.Reason(path));
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral(";stopAction=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted((stopAction < 0) ? "unreadable" : stopAction.ToString());
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral(";autoCleanup=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(autoCleanup);
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral(";playing=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(playing);
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral(";scheduleId=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(scheduleId);
			}
			log.LogInfo(bepInExInfoLogInterpolatedStringHandler);
		}
	}

	internal static void Sweep()
	{
		try
		{
			if (Coordinator.Sweep(Target) <= 0 || !Diagnostics.AllowWarning())
			{
				return;
			}
			Plugin instance = Plugin.Instance;
			if (instance != null)
			{
				ManualLogSource log = instance.Log;
				bool isEnabled;
				BepInExWarningLogInterpolatedStringHandler bepInExWarningLogInterpolatedStringHandler = new BepInExWarningLogInterpolatedStringHandler(47, 1, out isEnabled);
				if (isEnabled)
				{
					bepInExWarningLogInterpolatedStringHandler.AppendLiteral("[CORPSE] fade=held;reason=probe-unknown;active=");
					bepInExWarningLogInterpolatedStringHandler.AppendFormatted(Coordinator.Active);
				}
				log.LogWarning(bepInExWarningLogInterpolatedStringHandler);
			}
		}
		catch (System.Exception error)
		{
			Warn(error);
		}
	}

	internal static void Reset()
	{
		try
		{
			if (Coordinator.Reset(Target) > 0 && Diagnostics.AllowWarning())
			{
				Plugin instance = Plugin.Instance;
				if (instance != null)
				{
					ManualLogSource log = instance.Log;
					bool isEnabled;
					BepInExWarningLogInterpolatedStringHandler bepInExWarningLogInterpolatedStringHandler = new BepInExWarningLogInterpolatedStringHandler(53, 1, out isEnabled);
					if (isEnabled)
					{
						bepInExWarningLogInterpolatedStringHandler.AppendLiteral("[CORPSE] fade=reset-held;reason=probe-unknown;active=");
						bepInExWarningLogInterpolatedStringHandler.AppendFormatted(Coordinator.Active);
					}
					log.LogWarning(bepInExWarningLogInterpolatedStringHandler);
				}
			}
		}
		catch (System.Exception error)
		{
			Warn(error);
		}
		_patchChecked = false;
		_playPatched = false;
		Diagnostics.Reset();
		Coordinator.ResetCounters();
	}

	private static bool Paused()
	{
		try
		{
			return Time.timeScale <= 0.0001f || Time.deltaTime <= 0f;
		}
		catch
		{
			return true;
		}
	}

	private static bool PlayIsPatched()
	{
		try
		{
			MethodInfo method = typeof(Decay).GetMethod("Play", new System.Type[1] { typeof(float) });
			MethodInfo method2 = typeof(Decay).GetMethod("Play", System.Type.EmptyTypes);
			return HasPatches((MethodBase)(object)method) || HasPatches((MethodBase)(object)method2);
		}
		catch
		{
			return true;
		}
	}

	private static bool HasPatches(MethodBase method)
	{
		if (method == (MethodBase)null)
		{
			return true;
		}
		Patches patchInfo;
		try
		{
			patchInfo = Harmony.GetPatchInfo(method);
		}
		catch
		{
			return true;
		}
		if (patchInfo == null)
		{
			return false;
		}
		try
		{
			return patchInfo.Prefixes.Count > 0 || patchInfo.Postfixes.Count > 0 || patchInfo.Transpilers.Count > 0 || patchInfo.Finalizers.Count > 0;
		}
		catch
		{
			return true;
		}
	}

	private static bool PrefabIsSafe()
	{
		try
		{
			GameObject prefab = Decay.prefab;
			if (prefab == null)
			{
				return false;
			}
			Decay component = prefab.GetComponent<Decay>();
			if (component == null)
			{
				return false;
			}
			return CorpseFadePolicy.PrefabIsSafe((int)component.m_stopAction, component.m_autoCleanup);
		}
		catch
		{
			return false;
		}
	}

	private static CorpseFadePoolState PoolState()
	{
		try
		{
			ObjectPooler.Pool pool = Decay.pool;
			if (pool == null)
			{
				return CorpseFadePoolState.Missing;
			}
			return (pool.freeInstanceCount <= 0) ? CorpseFadePoolState.Exhausted : CorpseFadePoolState.Ready;
		}
		catch
		{
			return CorpseFadePoolState.Missing;
		}
	}

	private static void LogSkip(CorpseFadeSkip skip, int scheduleId)
	{
		if (!CorpseFadePolicy.IsReportable(skip))
		{
			return;
		}
		string text = CorpseFadePolicy.Reason(skip);
		if (!Diagnostics.AllowLine(text))
		{
			return;
		}
		Plugin instance = Plugin.Instance;
		if (instance != null)
		{
			ManualLogSource log = instance.Log;
			bool isEnabled;
			BepInExInfoLogInterpolatedStringHandler bepInExInfoLogInterpolatedStringHandler = new BepInExInfoLogInterpolatedStringHandler(49, 3, out isEnabled);
			if (isEnabled)
			{
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral("[CORPSE] fade=skipped;reason=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(text);
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral(";scheduleId=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(scheduleId);
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral(";active=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(Coordinator.Active);
			}
			log.LogInfo(bepInExInfoLogInterpolatedStringHandler);
		}
	}

	private static void LogFailure(CorpseFadeOutcome outcome, int scheduleId)
	{
		string text = CorpseFadePolicy.Reason(outcome);
		bool isEnabled;
		Plugin instance;
		if (Diagnostics.AllowLine(text))
		{
			instance = Plugin.Instance;
			if (instance != null)
			{
				ManualLogSource log = instance.Log;
				BepInExInfoLogInterpolatedStringHandler bepInExInfoLogInterpolatedStringHandler = new BepInExInfoLogInterpolatedStringHandler(81, 6, out isEnabled);
				if (isEnabled)
				{
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral("[CORPSE] fade=failed;reason=");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted(text);
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral(";scheduleId=");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted(scheduleId);
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral(";polls=");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted(Coordinator.LastPolls);
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral(";failures=");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted(Coordinator.Failures);
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral(";manualReleases=");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted(Coordinator.ManualReleases);
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral(";active=");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted(Coordinator.Active);
				}
				log.LogInfo(bepInExInfoLogInterpolatedStringHandler);
			}
		}
		System.Exception lastError = Coordinator.LastError;
		if (lastError == null || !Diagnostics.AllowWarning())
		{
			return;
		}
		instance = Plugin.Instance;
		if (instance != null)
		{
			ManualLogSource log2 = instance.Log;
			BepInExWarningLogInterpolatedStringHandler bepInExWarningLogInterpolatedStringHandler = new BepInExWarningLogInterpolatedStringHandler(42, 3, out isEnabled);
			if (isEnabled)
			{
				bepInExWarningLogInterpolatedStringHandler.AppendLiteral("[CORPSE] fade=error;reason=");
				bepInExWarningLogInterpolatedStringHandler.AppendFormatted(text);
				bepInExWarningLogInterpolatedStringHandler.AppendLiteral(";type=");
				bepInExWarningLogInterpolatedStringHandler.AppendFormatted(((MemberInfo)lastError.GetType()).Name);
				bepInExWarningLogInterpolatedStringHandler.AppendLiteral(";message=");
				bepInExWarningLogInterpolatedStringHandler.AppendFormatted(Short(lastError.Message));
			}
			log2.LogWarning(bepInExWarningLogInterpolatedStringHandler);
		}
	}

	private static void LogStarted(int scheduleId, Renderer renderer)
	{
		if (!Diagnostics.AllowLine("begun"))
		{
			return;
		}
		Plugin instance = Plugin.Instance;
		if (instance != null)
		{
			ManualLogSource log = instance.Log;
			bool isEnabled;
			BepInExInfoLogInterpolatedStringHandler bepInExInfoLogInterpolatedStringHandler = new BepInExInfoLogInterpolatedStringHandler(62, 4, out isEnabled);
			if (isEnabled)
			{
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral("[CORPSE] fade=begun;seconds=");
				((BepInExLogInterpolatedStringHandler)bepInExInfoLogInterpolatedStringHandler).AppendFormatted<float>(0.5f, "0.00");
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral(";renderer=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(((MemberInfo)((object)renderer).GetType()).Name);
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral(";concurrent=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(Coordinator.Active);
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral(";scheduleId=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(scheduleId);
			}
			log.LogInfo(bepInExInfoLogInterpolatedStringHandler);
		}
	}

	private static void LogStartedUnknown(int scheduleId, Renderer renderer)
	{
		string text = CorpseFadePolicy.Reason(CorpseFadeOutcome.StartedProbeUnknown);
		if (!Diagnostics.AllowLine(text))
		{
			return;
		}
		Plugin instance = Plugin.Instance;
		if (instance != null)
		{
			ManualLogSource log = instance.Log;
			bool isEnabled;
			BepInExInfoLogInterpolatedStringHandler bepInExInfoLogInterpolatedStringHandler = new BepInExInfoLogInterpolatedStringHandler(70, 5, out isEnabled);
			if (isEnabled)
			{
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral("[CORPSE] fade=begun;reason=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(text);
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral(";seconds=");
				((BepInExLogInterpolatedStringHandler)bepInExInfoLogInterpolatedStringHandler).AppendFormatted<float>(0.5f, "0.00");
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral(";renderer=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(((MemberInfo)((object)renderer).GetType()).Name);
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral(";concurrent=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(Coordinator.Active);
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral(";scheduleId=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(scheduleId);
			}
			log.LogInfo(bepInExInfoLogInterpolatedStringHandler);
		}
	}

	private static string Short(string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return "-";
		}
		string text2 = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
		if (text2.Length > 140)
		{
			return text2.Substring(0, 140);
		}
		return text2;
	}

	private static void Warn(System.Exception error)
	{
		if (!Diagnostics.AllowWarning())
		{
			return;
		}
		Plugin instance = Plugin.Instance;
		if (instance != null)
		{
			ManualLogSource log = instance.Log;
			bool isEnabled;
			BepInExWarningLogInterpolatedStringHandler bepInExWarningLogInterpolatedStringHandler = new BepInExWarningLogInterpolatedStringHandler(52, 2, out isEnabled);
			if (isEnabled)
			{
				bepInExWarningLogInterpolatedStringHandler.AppendLiteral("[CORPSE] fade=error;reason=unexpected;type=");
				bepInExWarningLogInterpolatedStringHandler.AppendFormatted(((MemberInfo)error.GetType()).Name);
				bepInExWarningLogInterpolatedStringHandler.AppendLiteral(";message=");
				bepInExWarningLogInterpolatedStringHandler.AppendFormatted(Short(error.Message));
			}
			log.LogWarning(bepInExWarningLogInterpolatedStringHandler);
		}
	}
}
