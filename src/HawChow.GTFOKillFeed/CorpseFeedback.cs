using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using AK;
using CharacterDestruction;
using Enemies;
using FX_EffectSystem;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppSystem.Collections.Generic;
using UnityEngine;

namespace HawChow.GTFOKillFeed;

internal static class CorpseFeedback
{
	private sealed class Corpse
	{
		internal EnemyAgent Enemy;

		internal System.IntPtr Identity;

		internal int ModelGeneration;

		internal int RagdollGeneration;

		internal float At;

		internal float Strength;

		internal float BurstAt;

		internal float EndAt;

		internal int Sequence;

		internal uint EnemyDataId;

		internal string Source;

		internal float BodySize = 1f;

		internal Vector3 Direction;

		internal Vector3 Origin;

		internal Vector3 BurstOrigin;

		internal bool Broken;

		internal bool Exploded;

		internal bool OffSceneCleanupApplied;

		internal bool LaunchClosed;

		internal bool LaunchUnavailableLogged;

		internal int LaunchApplications;

		internal System.IntPtr LaunchLease;

		internal int LaunchLeaseGeneration;

		internal int GibDataCalls;

		internal int GibDataSetupMisses;

		internal float Travel;

		internal float Speed;

		internal string RagdollSource;

		internal string Ladder;

		internal string LadderTried;

		internal string LadderHit;

		internal int RagdollLookupAttempts;

		internal float FirstLookupAge = -1f;

		internal Transform EnemyRoot;

		internal Transform ModelRoot;

		internal CorpseVisualLease Model;

		internal CorpseVisualLease Ragdoll;

		internal readonly System.Collections.Generic.List<CD_Gib> Gibs = new System.Collections.Generic.List<CD_Gib>();
	}

	private static int _nextSequence;

	private static readonly System.Collections.Generic.List<Corpse> Active = new System.Collections.Generic.List<Corpse>();

	private static readonly System.Collections.Generic.Dictionary<System.IntPtr, Corpse> Hidden = new System.Collections.Generic.Dictionary<System.IntPtr, Corpse>();

	private static readonly System.Collections.Generic.Dictionary<System.IntPtr, Corpse> GibOwners = new System.Collections.Generic.Dictionary<System.IntPtr, Corpse>();

	private static readonly System.Collections.Generic.Dictionary<System.IntPtr, Vector3> GibOrigins = new System.Collections.Generic.Dictionary<System.IntPtr, Vector3>();

	private static readonly System.Collections.Generic.Dictionary<System.IntPtr, Vector3> GibScales = new System.Collections.Generic.Dictionary<System.IntPtr, Vector3>();

	private static readonly CorpseDeathLedger DeathLedger = new CorpseDeathLedger();

	private static readonly System.Collections.Generic.HashSet<System.IntPtr> ActiveIdentities = new System.Collections.Generic.HashSet<System.IntPtr>();

	private static readonly System.Collections.Generic.List<ValueTuple<System.IntPtr, string>> PruneKeys = new System.Collections.Generic.List<ValueTuple<System.IntPtr, string>>();

	private static FX_Pool _deathPool;

	private static Corpse _fragmentOwner;

	private static int _warnings;

	private static bool _outsideLevelSkipLogged;

	private static float _pruneAt;

	private static readonly CorpseSoundThrottle ExplosionSound = new CorpseSoundThrottle();

	private static Vector3 _pendingSoundPosition;

	private static AudioClip _customExplosionClip;

	private static bool _customExplosionLoadAttempted;

	private static readonly LogTally _duplicateSkips = new LogTally();

	private static readonly LogTally _noisyPrunes = new LogTally();

	private static bool _gibUnavailableLogged;

	private static void Log(string message)
	{
		Plugin.Instance?.Log.LogInfo("[CORPSE] " + message);
	}

	private static int NextSequence()
	{
		_nextSequence = ((_nextSequence == 2147483647) ? 1 : (_nextSequence + 1));
		return _nextSequence;
	}

	private static void CloseLaunch(Corpse item, float age)
	{
		if (FeedbackPolicy.ShouldReportUnavailableLaunch(item.LaunchApplications, item.LaunchUnavailableLogged))
		{
			LogUnavailableLaunch(item, age);
		}
	}

	private static void RemoveQueued(Corpse item, string reason, float now)
	{
		CloseLaunch(item, Mathf.Max(0f, now - item.At));
		Log($"queue=removed;scheduleId={item.Sequence};source={item.Source};identity=0x{item.Identity.ToInt64():X};reason={reason};phase={(item.Exploded ? "post-burst" : "pre-burst")};age={Mathf.Max(0f, now - item.At):0.00}s;launch={LaunchState(item)}");
		Release(item, removeHidden: true);
	}

	internal static void Schedule(EnemyAgent enemy, Vector3 direction, float strength, string source)
	{
		if (!Plugin.Enabled.Value || !Plugin.CorpseEffects.Value)
		{
			return;
		}
		if (!DeathLedger.InLevel)
		{
			if (!_outsideLevelSkipLogged)
			{
				Log("schedule=skipped;reason=outside-level");
				_outsideLevelSkipLogged = true;
			}
			return;
		}
		if (enemy == null)
		{
			Log("schedule=skipped;reason=enemy-null");
			return;
		}
		System.IntPtr pointer = enemy.Pointer;
		if (pointer == System.IntPtr.Zero)
		{
			Log($"schedule=skipped;enemyId={enemy.EnemyDataID};reason=zero-identity");
			return;
		}
		long identity = pointer.ToInt64();
		if (!DeathLedger.TrySchedule(identity))
		{
			int num = _duplicateSkips.Add("duplicate-death-callback");
			if (LogTally.ShouldLog(num, 3, 50))
			{
				Log($"schedule=skipped;enemyId={enemy.EnemyDataID};reason=duplicate-death-callback;seen={num};note=expected-dedupe");
			}
			return;
		}
		try
		{
			if (direction.sqrMagnitude < 0.01f)
			{
				Camera currentCamera = CameraManager.GetCurrentCamera();
				direction = ((currentCamera != null) ? currentCamera.transform.forward : Vector3.forward);
			}
			direction.y = 0f;
			if (direction.sqrMagnitude < 0.01f)
			{
				direction = Vector3.forward;
			}
			float num2 = FeedbackPolicy.Clamp(Plugin.CorpseExplosionDelay.Value, 0.1f, 1.5f, 0.1f);
			float volume;
			float num3 = MeasureSize(enemy, out volume);
			int num4 = NextSequence();
			float unscaledTime = Time.unscaledTime;
			source = (string.IsNullOrWhiteSpace(source) ? "unknown" : source);
			float num5 = FeedbackPolicy.Travel(FeedbackPolicy.Unit(strength), Plugin.MinLaunchDistance.Value, Plugin.MaxLaunchDistance.Value, num3) * FeedbackPolicy.Clamp(Plugin.CorpseTravelScale.Value, 0.5f, 1.5f, 1f);
			Corpse corpse = new Corpse
			{
				Sequence = num4,
				EnemyDataId = enemy.EnemyDataID,
				Source = source,
				Identity = pointer,
				Enemy = enemy,
				At = unscaledTime,
				Strength = FeedbackPolicy.Unit(strength),
				BodySize = num3,
				Direction = direction.normalized,
				Origin = enemy.transform.position,
				BurstOrigin = Chest(enemy),
				BurstAt = num2,
				EndAt = num2 + 0.75f,
				Travel = num5,
				EnemyRoot = SafeTransform(enemy)
			};
			try
			{
				GameObject mainModelGO = enemy.MainModelGO;
				if (mainModelGO != null)
				{
					corpse.ModelRoot = mainModelGO.transform;
				}
			}
			catch (System.Exception error)
			{
				Warn(error);
			}
			CaptureRagdoll(corpse, enemy);
			Active.Add(corpse);
			string text = ((corpse.Ragdoll != null) ? ("at-death;ladder=" + LadderTried(corpse) + ";ladderHit=" + LadderHit(corpse)) : ("null@death;reason=" + corpse.RagdollSource + ";ladder=" + LadderTried(corpse)));
			Log($"schedule=accepted;scheduleId={num4};source={source};identity=0x{pointer.ToInt64():X};enemyId={enemy.EnemyDataID};strength={FeedbackPolicy.Unit(strength):0.##};delay={num2:0.00}s;scheduledAt={unscaledTime:0.00}s;bodyVolume={volume:0.###}m3;sizeFactor={num3:0.##};budget={num5:0.00}m;appliedTravel={FeedbackPolicy.AppliedTravel(num5, num3):0.00}m;ragdoll={text};lookupWindow={FeedbackPolicy.RagdollLookupDeadline(num2):0.00}s;active={Active.Count}");
		}
		catch
		{
			DeathLedger.Forget(identity);
			throw;
		}
	}

	internal static void Tick(float now)
	{
		//IL_0175: Unknown result type (might be due to invalid IL or missing references)
		//IL_002b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0030: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a2: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a7: Unknown result type (might be due to invalid IL or missing references)
		//IL_0079: Unknown result type (might be due to invalid IL or missing references)
		//IL_007e: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b0: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b5: Unknown result type (might be due to invalid IL or missing references)
		//IL_01bc: Unknown result type (might be due to invalid IL or missing references)
		//IL_013f: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d4: Unknown result type (might be due to invalid IL or missing references)
		//IL_0261: Unknown result type (might be due to invalid IL or missing references)
		CorpseFade.Sweep();
		if (now >= _pruneAt)
		{
			_pruneAt = now + 2f;
			ActiveIdentities.Clear();
			var enumerator = Active.GetEnumerator();
			try
			{
				while (enumerator.MoveNext())
				{
					Corpse current = enumerator.Current;
					ActiveIdentities.Add(current.Identity);
				}
			}
			finally
			{
				((System.IDisposable)enumerator).Dispose();
			}
			PruneKeys.Clear();
			var enumerator2 = Hidden.Values.GetEnumerator();
			try
			{
				while (enumerator2.MoveNext())
				{
					Corpse current2 = enumerator2.Current;
					if (ActiveIdentities.Contains(current2.Identity))
					{
						continue;
					}
					try
					{
						string text = null;
						if (current2.Enemy == null)
						{
							text = "enemy-null";
						}
						else if (current2.Enemy.Alive)
						{
							text = "enemy-alive";
						}
						else if (current2.Model != null && !FeedbackPolicy.IsLeaseCurrent(current2.ModelGeneration, current2.Model.Generation))
						{
							text = "model-lease-stale";
						}
						else if (current2.Ragdoll != null && !FeedbackPolicy.IsLeaseCurrent(current2.RagdollGeneration, current2.Ragdoll.Generation))
						{
							text = "ragdoll-lease-stale";
						}
						if (text != null)
						{
							PruneKeys.Add(new ValueTuple<System.IntPtr, string>(current2.Identity, text));
						}
					}
					catch (System.Exception ex)
					{
						Warn(ex);
						PruneKeys.Add(new ValueTuple<System.IntPtr, string>(current2.Identity, "prune-exception:" + ((MemberInfo)ex.GetType()).Name));
					}
				}
			}
			finally
			{
				((System.IDisposable)enumerator2).Dispose();
			}
			var enumerator3 = PruneKeys.GetEnumerator();
			try
			{
				Corpse corpse = default(Corpse);
				while (enumerator3.MoveNext())
				{
					ValueTuple<System.IntPtr, string> current3 = enumerator3.Current;
					if (Hidden.TryGetValue(current3.Item1, out corpse))
					{
						int num = _noisyPrunes.Add(current3.Item2);
						if (LogTally.ShouldLog(num, 3, 25))
						{
							Log($"hidden=pruned;scheduleId={corpse.Sequence};source={corpse.Source};identity=0x{current3.Item1.ToInt64():X};reason={current3.Item2};seen={num}");
						}
						Release(corpse, removeHidden: true);
					}
				}
			}
			finally
			{
				((System.IDisposable)enumerator3).Dispose();
			}
		}
		for (int num2 = Active.Count - 1; num2 >= 0; num2--)
		{
			Corpse item = Active[num2];
			try
			{
				if (item.Model != null && !FeedbackPolicy.IsLeaseCurrent(item.ModelGeneration, item.Model.Generation))
				{
					Log($"queue=model-lease-dropped;scheduleId={item.Sequence};source={item.Source};reason=model-lease-stale;launch={LaunchState(item)};phase={(item.Exploded ? "post-burst" : "pre-burst")};age={now - item.At:0.00}s");
					item.Model = null;
					item.ModelGeneration = 0;
				}
				if (item.Ragdoll != null && !FeedbackPolicy.IsLeaseCurrent(item.RagdollGeneration, item.Ragdoll.Generation))
				{
					item.Ragdoll = null;
					item.RagdollGeneration = 0;
					item.RagdollSource = "lease-stale";
				}
				float num3 = now - item.At;
				if (!Plugin.Enabled.Value || !Plugin.CorpseEffects.Value)
				{
					RemoveQueued(item, "effects-disabled", now);
					Active.RemoveAt(num2);
				}
				else
				{
					float num4 = ((item.Enemy != null) ? (item.Enemy.transform.position - item.Origin).sqrMagnitude : 0f);
					if (item.Enemy != null && FeedbackPolicy.IsCorpseOffSceneDistanceSquared(num4) && !item.OffSceneCleanupApplied)
					{
						int count = item.Gibs.Count;
						ReleaseGibs(item);
						item.Model?.RecyclePhysics();
						item.Ragdoll?.RecyclePhysics();
						item.OffSceneCleanupApplied = true;
						Log($"cleanup=offscene-recycled;scheduleId={item.Sequence};enemyId={item.EnemyDataId};releasedGibs={count};ragdollBodies={item.Ragdoll?.BodyCount ?? 0};age={num3:0.00}s");
					}
					if (!item.LaunchClosed && num3 >= 0.02f)
					{
						TryLaunchOrAbandon(item, num3);
					}
					if (!item.OffSceneCleanupApplied)
					{
						item.Ragdoll?.Limit(FeedbackPolicy.AppliedTravel(item.Travel, item.BodySize), FeedbackPolicy.LaunchVelocityCap(item.Speed));
						LimitGibs(item);
					}
					if (!item.Broken && num3 >= 0.12f)
					{
						item.Broken = true;
						int num5 = ((item.Enemy != null && FeedbackPolicy.ShouldBreakCorpseLimbs(item.OffSceneCleanupApplied)) ? BreakLimbs(item) : 0);
						string text2 = (item.OffSceneCleanupApplied ? "enemy-offscene-cleanup" : ((item.Enemy == null) ? "enemy-null-fallback" : "attempted"));
						if (item.GibDataCalls == 0 && item.Gibs.Count == 0)
						{
							Log($"dismemberment=complete;scheduleId={item.Sequence};enemyId={item.EnemyDataId};limbsBroken={num5};reason={text2};gibs=none;attemptBudget={FeedbackPolicy.Fragments(item.Strength)}");
						}
						else
						{
							Log($"dismemberment=complete;scheduleId={item.Sequence};enemyId={item.EnemyDataId};limbsBroken={num5};reason={text2};gibDataCalls={item.GibDataCalls};setupIdMisses={item.GibDataSetupMisses};gibsCaptured={item.Gibs.Count};attemptBudget={FeedbackPolicy.Fragments(item.Strength)}");
						}
						if (!_gibUnavailableLogged && item.GibDataCalls == 0 && item.Gibs.Count == 0 && num5 > 0)
						{
							_gibUnavailableLogged = true;
							Log("gibdata=unavailable;reason=matched-setup-has-no-gibdata;note=known-environment-no-GibData;effect=local-mesh-destruction+fx-only;budget=once-per-level");
						}
					}
					if (!item.Exploded && num3 >= item.BurstAt)
					{
						item.Exploded = true;
						Vector3 vector = ((item.Ragdoll != null) ? item.Ragdoll.Center : ((item.Enemy != null) ? Chest(item.Enemy) : item.BurstOrigin));
						float sqrMagnitude = (vector - item.Origin).sqrMagnitude;
						bool flag = item.OffSceneCleanupApplied || FeedbackPolicy.ShouldUseCapturedBurstOrigin(num4, sqrMagnitude);
						if (flag)
						{
							vector = item.BurstOrigin;
						}
						CorpseBurstPlan corpseBurstPlan = FeedbackPolicy.BurstPlan(Plugin.CorpseEffects.Value, Plugin.CorpseExplosionFx.Value, item.BodySize, item.Strength);
						int bursts = corpseBurstPlan.Bursts;
						int num6 = 0;
						float num7 = FeedbackPolicy.BurstSpread(item.Strength, item.BodySize);
						for (int i = 0; i < bursts; i++)
						{
							Vector3 position = vector;
							if (i > 0)
							{
								float f = (float)i * 2.39996f;
								position += new Vector3(Mathf.Cos(f) * num7, 0.3f + 0.25f * (float)i, Mathf.Sin(f) * num7);
							}
							if (Explode(position))
							{
								num6++;
							}
						}
						string text3 = (corpseBurstPlan.Sound ? RequestExplosionSound(vector) : "disabled");
						if (!corpseBurstPlan.Sound)
						{
							ExplosionSound.Reset();
							_pendingSoundPosition = Vector3.zero;
						}
						Log($"burst=complete;scheduleId={item.Sequence};enemyId={item.EnemyDataId};burstAge={num3:0.00}s;burstAt={item.BurstAt:0.00}s;spreadM={num7:0.00};anchor={(flag ? "captured" : "live")};fx={num6}/{bursts};sound={text3};ragdollBodies={item.Ragdoll?.BodyCount ?? 0};dismembermentStage={(item.Broken ? "attempted" : "not-run")};gibDataCalls={item.GibDataCalls};setupIdMisses={item.GibDataSetupMisses};gibsCaptured={item.Gibs.Count};sizeFactor={item.BodySize:0.##}");
						TryLeaseModel(item);
						Hidden[item.Identity] = item;
					}
					if (num3 >= item.EndAt)
					{
						ReleaseGibs(item);
						CloseLaunch(item, num3);
						CorpseFadePrep.Attempt(() => FadeRenderer(item), delegate(object fadeTarget)
						{
							CorpseFade.TryBegin(fadeTarget as Renderer, item.OffSceneCleanupApplied, DeathLedger.InLevel, item.Sequence, NativeDecay(item));
							return true;
						}, Warn);
						item.Model?.Hide();
						item.Ragdoll?.Hide();
						bool offSceneCleanupApplied = item.OffSceneCleanupApplied;
						if (!offSceneCleanupApplied)
						{
							item.Model?.RecyclePhysics();
							item.Ragdoll?.RecyclePhysics();
						}
						Log($"cleanup=physics-recycled;scheduleId={item.Sequence};enemyId={item.EnemyDataId};modelRenderers={item.Model?.RendererCount ?? 0};ragdollRenderers={item.Ragdoll?.RendererCount ?? 0};modelColliders={item.Model?.ColliderCount ?? 0};ragdollColliders={item.Ragdoll?.ColliderCount ?? 0};early={offSceneCleanupApplied}");
						Active.RemoveAt(num2);
					}
				}
			}
			catch (System.Exception ex2)
			{
				Log($"tick=failed;scheduleId={item.Sequence};error={((MemberInfo)ex2.GetType()).Name}");
				Warn(ex2);
				RemoveQueued(item, "tick-exception", now);
				Active.RemoveAt(num2);
			}
		}
		if (!Plugin.Enabled.Value || !Plugin.CorpseEffects.Value || !Plugin.CorpseExplosionFx.Value)
		{
			ExplosionSound.Reset();
			_pendingSoundPosition = Vector3.zero;
		}
		else
		{
			FlushExplosionSound(now);
		}
	}

	private static string RequestExplosionSound(Vector3 position)
	{
		if (!ExplosionSound.Request(Time.unscaledTime))
		{
			_pendingSoundPosition = position;
			return "coalesced";
		}
		return PostExplosionSound(position);
	}

	private static void FlushExplosionSound(float now)
	{
		if (ExplosionSound.TryFlush(now))
		{
			Log("sound=" + PostExplosionSound(_pendingSoundPosition) + ";coalesced=true");
		}
	}

	private static string PostExplosionSound(Vector3 position)
	{
		AudioClip audioClip = LoadExplosionSound();
		if (audioClip != null)
		{
			try
			{
				AudioSource.PlayClipAtPoint(audioClip, position, 0.82f);
				return "custom";
			}
			catch (System.Exception error)
			{
				Warn(error);
			}
		}
		try
		{
			CellSound.Post(EVENTS.ENEMYEXPLODE, position);
			return "game-fallback";
		}
		catch (System.Exception error2)
		{
			Warn(error2);
			return "failed";
		}
	}

	private static AudioClip LoadExplosionSound()
	{
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0041: Expected O, but got Unknown
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_008d: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e3: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b8: Unknown result type (might be due to invalid IL or missing references)
		//IL_0102: Unknown result type (might be due to invalid IL or missing references)
		//IL_0167: Unknown result type (might be due to invalid IL or missing references)
		//IL_0238: Unknown result type (might be due to invalid IL or missing references)
		if (_customExplosionLoadAttempted)
		{
			return _customExplosionClip;
		}
		_customExplosionLoadAttempted = true;
		try
		{
			Stream manifestResourceStream = typeof(Plugin).Assembly.GetManifestResourceStream("KillFeed.Audio.corpse-burst.wav");
			try
			{
				if (manifestResourceStream == null)
				{
					throw new InvalidOperationException("Embedded corpse burst WAV is missing.");
				}
				MemoryStream val = new MemoryStream();
				try
				{
					manifestResourceStream.CopyTo((Stream)(object)val);
					byte[] array = val.ToArray();
					if (array.Length < 12 || Encoding.ASCII.GetString(array, 0, 4) != "RIFF" || Encoding.ASCII.GetString(array, 8, 4) != "WAVE")
					{
						throw new InvalidOperationException("Embedded corpse burst WAV has an invalid RIFF header.");
					}
					int num = 0;
					int num2 = 0;
					int num3 = 0;
					int num4 = 0;
					int num5 = -1;
					int num6 = 0;
					int num7 = 12;
					while (num7 <= array.Length - 8)
					{
						string @string = Encoding.ASCII.GetString(array, num7, 4);
						int num8 = BitConverter.ToInt32(array, num7 + 4);
						int num9 = num7 + 8;
						if (num8 < 0 || num9 > array.Length - num8)
						{
							throw new InvalidOperationException("Embedded corpse burst WAV has a malformed chunk.");
						}
						if (@string == "fmt ")
						{
							if (num8 < 16)
							{
								throw new InvalidOperationException("Embedded corpse burst WAV format chunk is incomplete.");
							}
							num = BitConverter.ToUInt16(array, num9);
							num2 = BitConverter.ToUInt16(array, num9 + 2);
							num3 = BitConverter.ToInt32(array, num9 + 4);
							num4 = BitConverter.ToUInt16(array, num9 + 14);
						}
						else if (@string == "data")
						{
							num5 = num9;
							num6 = num8;
						}
						long num10 = (long)num9 + (long)num8 + (num8 & 1);
						if (num10 > array.Length)
						{
							throw new InvalidOperationException("Embedded corpse burst WAV chunk exceeds the file length.");
						}
						num7 = (int)num10;
					}
					if (num != 1 || num2 != 1 || num3 < 8000 || num3 > 192000 || num4 != 16 || num5 < 0 || num6 <= 0 || num6 % (num2 * (num4 / 8)) != 0)
					{
						throw new InvalidOperationException("Embedded corpse burst WAV must be mono PCM16 with a valid sample rate.");
					}
					int num11 = num6 / 2;
					float[] array2 = new float[num11];
					for (int i = 0; i < num11; i++)
					{
						int num12 = num5 + i * 2;
						short num13 = (short)(array[num12] | (array[num12 + 1] << 8));
						array2[i] = (float)num13 / 32768f;
					}
					AudioClip audioClip = AudioClip.Create("KillFeed_CorpseBurst", num11, num2, num3, stream: false);
					if (audioClip == null || !audioClip.SetData(new Il2CppStructArray<float>(array2), 0))
					{
						throw new InvalidOperationException("Unity could not load the embedded corpse burst samples.");
					}
					_customExplosionClip = audioClip;
				}
				finally
				{
					((System.IDisposable)val)?.Dispose();
				}
			}
			finally
			{
				((System.IDisposable)manifestResourceStream)?.Dispose();
			}
		}
		catch (System.Exception error)
		{
			Warn(error);
		}
		return _customExplosionClip;
	}

	private static CorpseVisualLease Lease(GameObject obj)
	{
		CorpseVisualLease corpseVisualLease = obj.GetComponent<CorpseVisualLease>();
		if (corpseVisualLease == null)
		{
			corpseVisualLease = obj.AddComponent<CorpseVisualLease>();
		}
		corpseVisualLease.Capture();
		return corpseVisualLease;
	}

	private static void CaptureRagdoll(Corpse item, EnemyAgent enemy)
	{
		ResolveRagdoll(item, enemy, atDeath: true);
	}

	private static void TryLateRagdoll(Corpse item)
	{
		ResolveRagdoll(item, item.Enemy, atDeath: false);
	}

	private static void ResolveRagdoll(Corpse item, EnemyAgent enemy, bool atDeath)
	{
		if (item.Ragdoll != null && item.Ragdoll.BodyCount > 0)
		{
			return;
		}
		string text = "no-ragdoll-instance";
		bool flag = false;
		try
		{
			if (enemy != null)
			{
				NoteRung(item, "ragdoll-instance");
				GameObject host = null;
				try
				{
					host = enemy.RagdollInstance;
				}
				catch (System.Exception error)
				{
					Warn(error);
					text = "capture-failed";
				}
				if (AcceptRagdoll(item, host, atDeath))
				{
					return;
				}
			}
			for (int i = 0; i < 2; i++)
			{
				Transform transform = ((i == 0) ? item.EnemyRoot : item.ModelRoot);
				if (!(transform == null) && (i != 1 || !(item.ModelRoot == item.EnemyRoot)))
				{
					flag = true;
					SearchRagdollRoot(item, transform, atDeath);
					if (item.Ragdoll != null)
					{
						return;
					}
				}
			}
			if (!flag)
			{
				NoteRung(item, (enemy == null) ? "enemy-wrapper-and-hierarchy-gone" : "no-hierarchy");
			}
		}
		catch (System.Exception error2)
		{
			Warn(error2);
			text = "capture-failed";
		}
		if (item.Ragdoll == null)
		{
			if (item.RagdollSource == "capture-failed")
			{
				text = "capture-failed";
			}
			else if (item.RagdollSource == "no-rigidbody" && text != "capture-failed")
			{
				text = "no-rigidbody";
			}
			item.RagdollSource = ((flag || enemy != null) ? text : "enemy-destroyed");
		}
	}

	private static void SearchRagdollRoot(Corpse item, Transform root, bool atDeath)
	{
		RagdollScript[] array = Components<RagdollScript>(root);
		for (int i = 0; i < 2; i++)
		{
			foreach (RagdollScript ragdollScript in array)
			{
				if (ragdollScript == null)
				{
					continue;
				}
				bool flag;
				try
				{
					flag = ragdollScript.ragdolled;
				}
				catch (System.Exception error)
				{
					Warn(error);
					flag = false;
				}
				if (i == 0 == flag)
				{
					NoteRung(item, "ragdoll-script");
					if (AcceptRagdoll(item, SafeGameObject(ragdollScript), atDeath))
					{
						return;
					}
				}
			}
		}
		EnemyRagdollRef[] array2 = Components<EnemyRagdollRef>(root);
		foreach (EnemyRagdollRef enemyRagdollRef in array2)
		{
			if (enemyRagdollRef == null)
			{
				continue;
			}
			try
			{
				if (enemyRagdollRef.m_rigidBodyLookup == null)
				{
					continue;
				}
			}
			catch (System.Exception error2)
			{
				Warn(error2);
				continue;
			}
			NoteRung(item, "enemy-ragdoll-ref");
			if (AcceptRagdoll(item, SafeGameObject(enemyRagdollRef), atDeath))
			{
				return;
			}
		}
		Rigidbody[] array3 = Components<Rigidbody>(root);
		for (int l = 0; l < array3.Length; l++)
		{
			GameObject gameObject = SafeHost(array3[l]);
			if (!(gameObject == null) && !IsLivingModelHost(gameObject, item))
			{
				NoteRung(item, "raw-rigidbodies");
				if (AcceptRagdoll(item, gameObject, atDeath))
				{
					break;
				}
			}
		}
	}

	private static bool AcceptRagdoll(Corpse item, GameObject host, bool atDeath)
	{
		if (host == null)
		{
			return false;
		}
		try
		{
			CorpseVisualLease corpseVisualLease = Lease(host);
			if (corpseVisualLease == null || corpseVisualLease.BodyCount <= 0)
			{
				corpseVisualLease?.RestoreState();
				if (item.RagdollSource != "capture-failed")
				{
					item.RagdollSource = "no-rigidbody";
				}
				return false;
			}
			item.Ragdoll = corpseVisualLease;
			item.RagdollGeneration = corpseVisualLease.Generation;
			item.LadderHit = item.Ladder;
			item.RagdollSource = (atDeath ? "ragdoll-at-death:" : "late-lookup:") + item.Ladder;
			return true;
		}
		catch (System.Exception error)
		{
			Warn(error);
			item.RagdollSource = "capture-failed";
			return false;
		}
	}

	private static T[] Components<T>(Transform root) where T : Component
	{
		try
		{
			return (root != null) ? ((T[])root.GetComponentsInChildren<T>(includeInactive: true)) : System.Array.Empty<T>();
		}
		catch (System.Exception error)
		{
			Warn(error);
			return System.Array.Empty<T>();
		}
	}

	private static GameObject SafeGameObject(Component component)
	{
		try
		{
			return (component != null) ? component.gameObject : null;
		}
		catch (System.Exception error)
		{
			Warn(error);
			return null;
		}
	}

	private static GameObject SafeHost(Rigidbody body)
	{
		try
		{
			Transform transform = ((body != null) ? body.transform : null);
			if (transform == null)
			{
				return null;
			}
			return (transform.parent != null) ? transform.parent.gameObject : transform.gameObject;
		}
		catch (System.Exception error)
		{
			Warn(error);
			return null;
		}
	}

	private static bool IsLivingModelHost(GameObject host, Corpse item)
	{
		try
		{
			if (item.ModelRoot != null && host == item.ModelRoot.gameObject)
			{
				return true;
			}
			if (item.EnemyRoot != null && host == item.EnemyRoot.gameObject)
			{
				return true;
			}
		}
		catch (System.Exception error)
		{
			Warn(error);
		}
		return false;
	}

	private static Transform SafeTransform(EnemyAgent enemy)
	{
		try
		{
			return (enemy != null) ? enemy.transform : null;
		}
		catch (System.Exception error)
		{
			Warn(error);
			return null;
		}
	}

	private static string FirstLookupAge(Corpse item)
	{
		if (!(item.FirstLookupAge >= 0f))
		{
			return "never";
		}
		return $"{item.FirstLookupAge:0.00}s";
	}

	private static string UnavailableReason(Corpse item)
	{
		if (!string.IsNullOrEmpty(item.RagdollSource))
		{
			return item.RagdollSource;
		}
		return "no-ragdoll-instance";
	}

	private static string LaunchState(Corpse item)
	{
		return FeedbackPolicy.LaunchState(item.LaunchApplications, item.LaunchClosed);
	}

	private static FeedbackPolicy.RagdollLease LeaseState(Corpse item)
	{
		bool flag = item.Ragdoll != null && FeedbackPolicy.IsLeaseCurrent(item.RagdollGeneration, item.Ragdoll.Generation);
		int bodyCount = (flag ? item.Ragdoll.BodyCount : 0);
		return FeedbackPolicy.ClassifyRagdoll(item.Ragdoll != null, bodyCount, flag);
	}

	private static void TryLaunchOrAbandon(Corpse item, float age)
	{
		FeedbackPolicy.RagdollLease lease = LeaseState(item);
		if (FeedbackPolicy.ShouldReplaceRagdollLease(lease))
		{
			Log($"ragdoll=discarded;scheduleId={item.Sequence};enemyId={item.EnemyDataId};reason=lease-empty;attempts={item.RagdollLookupAttempts};ladder={LadderTried(item)};age={age:0.00}s");
			item.Ragdoll = null;
			item.RagdollGeneration = 0;
			item.RagdollSource = "no-bodies";
			lease = FeedbackPolicy.RagdollLease.Missing;
		}
		if (!FeedbackPolicy.ShouldApplyLaunch(lease) && FeedbackPolicy.ShouldRetryRagdollLookup(haveRagdoll: false, item.RagdollLookupAttempts, age, item.BurstAt))
		{
			if (item.FirstLookupAge < 0f)
			{
				item.FirstLookupAge = age;
			}
			item.RagdollLookupAttempts++;
			TryLateRagdoll(item);
			if (item.Ragdoll != null)
			{
				Log($"ragdoll=late@attempts={item.RagdollLookupAttempts};scheduleId={item.Sequence};enemyId={item.EnemyDataId};firstLookupAge={FirstLookupAge(item)};ladder={LadderTried(item)};ladderHit={LadderHit(item)};age={age:0.00}s;note=coroutine-spawned-ragdoll");
			}
			lease = LeaseState(item);
		}
		if (FeedbackPolicy.CanApplyLaunch(lease, item.LaunchApplications, SameLease(item), age, item.BurstAt))
		{
			ApplyLaunch(item, age);
		}
		else if (FeedbackPolicy.ShouldApplyLaunch(lease))
		{
			if (FeedbackPolicy.ShouldCloseAppliedLaunch(item.LaunchApplications, age, item.BurstAt))
			{
				item.LaunchClosed = true;
			}
		}
		else if (FeedbackPolicy.ShouldAbandonRagdollLookup(haveRagdoll: false, age, item.BurstAt))
		{
			item.LaunchClosed = true;
			CloseLaunch(item, age);
		}
	}

	private static bool SameLease(Corpse item)
	{
		if (item.Ragdoll != null)
		{
			return FeedbackPolicy.IsSameLaunchLease(item.LaunchLease, item.LaunchLeaseGeneration, item.Ragdoll.Pointer, item.Ragdoll.Generation);
		}
		return false;
	}

	private static void LogUnavailableLaunch(Corpse item, float age)
	{
		item.LaunchClosed = true;
		item.LaunchUnavailableLogged = true;
		Log($"launch=unavailable;scheduleId={item.Sequence};enemyId={item.EnemyDataId};reason={UnavailableReason(item)};attempts={item.RagdollLookupAttempts};firstLookupAge={FirstLookupAge(item)};haveRagdoll={((item.Ragdoll != null) ? "yes" : "no")};bodies={((item.Ragdoll != null) ? item.Ragdoll.BodyCount : 0)};ladder={LadderTried(item)};ladderHit={LadderHit(item)};budget={item.Travel:0.00}m;appliedTravel={FeedbackPolicy.AppliedTravel(item.Travel, item.BodySize):0.00}m;speed=0.00m/s;distance=0.00m;age={age:0.00}s;window={FeedbackPolicy.RagdollLookupDeadline(item.BurstAt):0.00}s;note=no-usable-ragdoll;burst-and-dismemberment-unaffected");
	}

	private static string LadderTried(Corpse item)
	{
		return FeedbackPolicy.LadderText(item.LadderTried, item.Ladder);
	}

	private static string LadderHit(Corpse item)
	{
		if (!string.IsNullOrEmpty(item.LadderHit))
		{
			return item.LadderHit;
		}
		return "none";
	}

	private static void NoteRung(Corpse item, string rung)
	{
		item.Ladder = rung;
		if (string.IsNullOrEmpty(item.LadderTried))
		{
			item.LadderTried = rung;
		}
		else if (item.LadderTried.IndexOf(rung, (StringComparison)4) < 0)
		{
			item.LadderTried = item.LadderTried + "|" + rung;
		}
	}

	private static object FadeRenderer(Corpse item)
	{
		Renderer renderer = item.Ragdoll?.PrimaryRenderer() ?? item.Model?.PrimaryRenderer();
		if (renderer != null)
		{
			return renderer;
		}
		return HierarchyRenderer(item.ModelRoot) ?? HierarchyRenderer(item.EnemyRoot);
	}

	private static Renderer HierarchyRenderer(Transform root)
	{
		if (root == null)
		{
			return null;
		}
		try
		{
			Renderer[] array = root.GetComponentsInChildren<Renderer>(includeInactive: true);
			Renderer renderer = null;
			foreach (Renderer renderer2 in array)
			{
				if (!(renderer2 == null) && renderer2.enabled)
				{
					if (renderer2 is SkinnedMeshRenderer)
					{
						return renderer2;
					}
					if (renderer == null)
					{
						renderer = renderer2;
					}
				}
			}
			return renderer;
		}
		catch (System.Exception error)
		{
			Warn(error);
			return null;
		}
	}

	private static Decay NativeDecay(Corpse item)
	{
		Transform[] array = new Transform[2] { item.ModelRoot, item.EnemyRoot };
		for (int i = 0; i < array.Length; i++)
		{
			if (array[i] == null)
			{
				continue;
			}
			try
			{
				Decay componentInChildren = array[i].GetComponentInChildren<Decay>(includeInactive: true);
				if (componentInChildren != null)
				{
					return componentInChildren;
				}
			}
			catch (System.Exception error)
			{
				Warn(error);
			}
		}
		return null;
	}

	private static void TryLeaseModel(Corpse item)
	{
		try
		{
			GameObject gameObject = ((item.Enemy != null) ? item.Enemy.MainModelGO : null);
			if (!(gameObject == null))
			{
				CorpseVisualLease component = gameObject.GetComponent<CorpseVisualLease>();
				item.Model = ((component != null && item.Ragdoll != null && component.Pointer == item.Ragdoll.Pointer) ? item.Ragdoll : Lease(gameObject));
				item.ModelGeneration = item.Model.Generation;
				if (item.OffSceneCleanupApplied)
				{
					item.Model.RecyclePhysics();
				}
			}
		}
		catch (System.Exception error)
		{
			Warn(error);
		}
	}

	private static void ApplyLaunch(Corpse item, float age)
	{
		int bodyCount = item.Ragdoll.BodyCount;
		item.Speed = FeedbackPolicy.LaunchSpeedFor(item.Travel, item.Strength);
		item.Ragdoll.Launch(item.Direction, item.Strength, item.Speed);
		item.RagdollGeneration = item.Ragdoll.Generation;
		item.LaunchApplications++;
		item.LaunchLease = item.Ragdoll.Pointer;
		item.LaunchLeaseGeneration = item.RagdollGeneration;
		string text = (string.IsNullOrEmpty(item.RagdollSource) ? "late-lookup" : item.RagdollSource);
		Log($"launch=applied;scheduleId={item.Sequence};enemyId={item.EnemyDataId};path={text};application=#{item.LaunchApplications};ragdollBodies={bodyCount};attempts={item.RagdollLookupAttempts};firstLookupAge={FirstLookupAge(item)};ladder={LadderTried(item)};ladderHit={LadderHit(item)};budget={item.Travel:0.00}m;appliedTravel={FeedbackPolicy.AppliedTravel(item.Travel, item.BodySize):0.00}m;speed={item.Speed:0.00}m/s;distance={FeedbackPolicy.LaunchDistance(item.Speed, item.Strength):0.00}m;capped={(FeedbackPolicy.LaunchSpeedCapped(item.Travel, item.Strength) ? "speed" : "no")};collision={(FeedbackPolicy.NeedsContinuousCollision(item.Speed) ? "continuous" : "discrete")};mode=radial;age={age:0.00}s");
	}

	private static Vector3 Chest(EnemyAgent enemy)
	{
		Transform transform = enemy.ModelRef?.m_chestBone;
		if (!(transform != null))
		{
			return enemy.transform.position + Vector3.up;
		}
		return transform.position;
	}

	private static float MeasureSize(EnemyAgent enemy, out float volume)
	{
		volume = 0f;
		try
		{
			BurnableSkinnedMesh componentInChildren = enemy.GetComponentInChildren<BurnableSkinnedMesh>(includeInactive: true);
			SkinnedMeshRenderer skinnedMeshRenderer = ((componentInChildren != null) ? componentInChildren.m_skinnedMesh : null);
			if (skinnedMeshRenderer != null)
			{
				Vector3 size = skinnedMeshRenderer.bounds.size;
				volume = Math.Abs(size.x * size.y * size.z);
			}
		}
		catch (System.Exception error)
		{
			Warn(error);
		}
		return FeedbackPolicy.SizeFactor(volume);
	}

	private static int ActivateGibData(CD_CharacterDestructionCollider destruction, byte destructionId, Vector3 position, Vector3 force, int maxCalls, out bool setupMatched, out int setupMatches, out int dataEntries)
	{
		setupMatched = false;
		setupMatches = 0;
		dataEntries = 0;
		if (destruction == null || maxCalls <= 0)
		{
			return 0;
		}
		Il2CppSystem.Collections.Generic.List<CD_DestructionSetup> allowedDestructions = destruction.m_allowedDestructions;
		if (allowedDestructions == null)
		{
			return 0;
		}
		int num = 0;
		for (int i = 0; i < allowedDestructions.Count; i++)
		{
			if (num >= maxCalls)
			{
				break;
			}
			CD_DestructionSetup cD_DestructionSetup = allowedDestructions[i];
			if (cD_DestructionSetup == null || cD_DestructionSetup.m_id != destructionId)
			{
				continue;
			}
			setupMatched = true;
			setupMatches++;
			Il2CppReferenceArray<CD_GibData> gibDatas = cD_DestructionSetup.m_gibDatas;
			if (gibDatas == null || gibDatas.Length == 0)
			{
				continue;
			}
			dataEntries += gibDatas.Length;
			for (int j = 0; j < gibDatas.Length; j++)
			{
				if (num >= maxCalls)
				{
					break;
				}
				CD_GibData cD_GibData = gibDatas[j];
				if (!(cD_GibData == null) && !(cD_GibData.m_gibPrefab == null))
				{
					try
					{
						cD_GibData.Activate(position, force);
						num++;
					}
					catch (System.Exception error)
					{
						Warn(error);
					}
				}
			}
		}
		return num;
	}

	private static int BreakLimbs(Corpse item)
	{
		Il2CppReferenceArray<Dam_EnemyDamageLimb> il2CppReferenceArray = item.Enemy.Damage?.DamageLimbs;
		if (il2CppReferenceArray == null)
		{
			return 0;
		}
		int num = FeedbackPolicy.Fragments(item.Strength);
		int num2 = 0;
		_fragmentOwner = item;
		try
		{
			int num3 = il2CppReferenceArray.Length - 1;
			while (num3 >= 0 && num2 < num)
			{
				Dam_EnemyDamageLimb dam_EnemyDamageLimb = il2CppReferenceArray[num3];
				CD_CharacterDestructionCollider cD_CharacterDestructionCollider = dam_EnemyDamageLimb?.m_destruction;
				if (!(dam_EnemyDamageLimb == null) && !dam_EnemyDamageLimb.IsDestroyed && !(cD_CharacterDestructionCollider == null) && cD_CharacterDestructionCollider.HasDestruction())
				{
					float f = (float)(num2 + 1) * 2.39996f;
					Vector3 vector = new Vector3(Mathf.Cos(f), 0.65f, Mathf.Sin(f));
					Vector3 force = vector.normalized * (1.8f + 3f * item.Strength);
					if (cD_CharacterDestructionCollider.TryDoDestruction(ImpactDirection.Front, item.Direction, Vector3.zero, force, (item.Strength > 0.45f) ? CD_DestructionSeverity.Death : CD_DestructionSeverity.Severe, severeArmsDestuctionNotAllowed: false, out var destructionEventData))
					{
						Vector3 position = dam_EnemyDamageLimb.transform.position;
						int num4 = 0;
						try
						{
							num4 = cD_CharacterDestructionCollider.m_allowedDestructions?.Count ?? 0;
						}
						catch (System.Exception error)
						{
							Warn(error);
						}
						CD_CharacterDestruction characterDestruction = cD_CharacterDestructionCollider.m_characterDestruction;
						bool flag = false;
						if (characterDestruction != null)
						{
							try
							{
								characterDestruction.ApplyDestruction(item.Enemy, destructionEventData);
								flag = true;
							}
							catch (System.Exception error2)
							{
								Warn(error2);
							}
						}
						if (item.GibDataCalls < 24)
						{
							bool setupMatched;
							int setupMatches;
							int dataEntries;
							int num5 = ActivateGibData(cD_CharacterDestructionCollider, destructionEventData.m_destructionID, position, vector.normalized * FeedbackPolicy.GibLaunchSpeed(item.Strength), 24 - item.GibDataCalls, out setupMatched, out setupMatches, out dataEntries);
							item.GibDataCalls += num5;
							if (!setupMatched)
							{
								item.GibDataSetupMisses++;
							}
							Log($"gibdata=resolved;enemyId={item.Enemy.EnemyDataID};destructionId={destructionEventData.m_destructionID};setupMatched={setupMatched};setupMatches={setupMatches};entries={dataEntries};activateCalls={num5}");
						}
						dam_EnemyDamageLimb.DestroyLimb(destructionEventData);
						FX_Manager.PlayEffect(playLocalVersion: true, FX_GroupName.Impact_EnemyBody, position, Quaternion.identity, isDecalsAllowed: false);
						Log($"destruction=local;enemyId={item.Enemy.EnemyDataID};limb={num3};destructionId={destructionEventData.m_destructionID};meshDestroyed={flag};setups={num4};gibs={item.Gibs.Count}");
						num2++;
					}
				}
				num3--;
			}
		}
		finally
		{
			_fragmentOwner = null;
		}
		return num2;
	}

	internal static void GibReused(CD_Gib gib)
	{
		if (gib == null)
		{
			return;
		}
		Corpse corpse = default(Corpse);
		if (GibOwners.TryGetValue(gib.Pointer, out corpse))
		{
			for (int num = corpse.Gibs.Count - 1; num >= 0; num--)
			{
				if (corpse.Gibs[num].Pointer == gib.Pointer)
				{
					corpse.Gibs.RemoveAt(num);
				}
			}
		}
		GibOwners.Remove(gib.Pointer);
		GibOrigins.Remove(gib.Pointer);
		Vector3 localScale = default(Vector3);
		if (GibScales.TryGetValue(gib.Pointer, out localScale))
		{
			Rigidbody rigidbody = gib.m_rigidbody;
			if (rigidbody != null && rigidbody.transform != null)
			{
				rigidbody.transform.localScale = localScale;
			}
			GibScales.Remove(gib.Pointer);
		}
	}

	internal static void CaptureGib(CD_Gib gib)
	{
		//IL_002e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		if (_fragmentOwner == null || gib == null || _fragmentOwner.Gibs.Count >= 24)
		{
			return;
		}
		var enumerator = _fragmentOwner.Gibs.GetEnumerator();
		try
		{
			while (enumerator.MoveNext())
			{
				if (enumerator.Current.Pointer == gib.Pointer)
				{
					return;
				}
			}
		}
		finally
		{
			((System.IDisposable)enumerator).Dispose();
		}
		_fragmentOwner.Gibs.Add(gib);
		GibOwners[gib.Pointer] = _fragmentOwner;
		Rigidbody rigidbody = gib.m_rigidbody;
		if (!(rigidbody == null))
		{
			GibOrigins[gib.Pointer] = rigidbody.position;
			Transform transform = rigidbody.transform;
			if (transform != null && !GibScales.ContainsKey(gib.Pointer))
			{
				GibScales[gib.Pointer] = transform.localScale;
				transform.localScale *= FeedbackPolicy.GibScale(_fragmentOwner.BodySize);
			}
			float f = (float)_fragmentOwner.Gibs.Count * 2.39996f;
			Vector3 normalized = new Vector3(Mathf.Cos(f), 0.7f, Mathf.Sin(f)).normalized;
			Vector3 vector = rigidbody.position - _fragmentOwner.Origin;
			if (vector.sqrMagnitude > 0.01f)
			{
				vector.y = Mathf.Max(vector.y, 0.25f);
				normalized = (vector.normalized * 0.7f + normalized * 0.3f).normalized;
			}
			rigidbody.velocity = normalized * FeedbackPolicy.GibLaunchSpeed(_fragmentOwner.Strength);
		}
	}

	private static void LimitGibs(Corpse owner)
	{
		//IL_0018: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		float num = FeedbackPolicy.FragmentRadius(owner.Strength, owner.BodySize);
		var enumerator = owner.Gibs.GetEnumerator();
		try
		{
			Corpse corpse = default(Corpse);
			Vector3 vector = default(Vector3);
			while (enumerator.MoveNext())
			{
				CD_Gib current = enumerator.Current;
				if (current == null || !GibOwners.TryGetValue(current.Pointer, out corpse) || corpse != owner)
				{
					continue;
				}
				Rigidbody rigidbody = current.m_rigidbody;
				if (!(rigidbody == null) && GibOrigins.TryGetValue(current.Pointer, out vector))
				{
					Vector3 vector2 = rigidbody.position - vector;
					if (vector2.sqrMagnitude > num * num)
					{
						rigidbody.position = vector + vector2.normalized * num;
						rigidbody.velocity = Vector3.zero;
					}
				}
			}
		}
		finally
		{
			((System.IDisposable)enumerator).Dispose();
		}
	}

	private static bool Explode(Vector3 position)
	{
		//IL_004f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0059: Expected O, but got Unknown
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		//IL_0030: Expected O, but got Unknown
		//IL_0074: Unknown result type (might be due to invalid IL or missing references)
		//IL_007e: Expected O, but got Unknown
		if (_deathPool == null)
		{
			GameObject gameObject = Resources.Load<GameObject>("FXC_FlyerDeath");
			if (gameObject == null)
			{
				Warn((System.Exception)new InvalidOperationException("FXC_FlyerDeath resource unavailable"));
				return false;
			}
			_deathPool = FX_Manager.CreatePool(gameObject);
		}
		if (_deathPool == null)
		{
			Warn((System.Exception)new InvalidOperationException("FXC_FlyerDeath pool unavailable"));
			return false;
		}
		FX_EffectBase_Poolable fX_EffectBase_Poolable = _deathPool.AquireEffect();
		if (fX_EffectBase_Poolable == null)
		{
			Warn((System.Exception)new InvalidOperationException("FXC_FlyerDeath pool returned no effect"));
			return false;
		}
		fX_EffectBase_Poolable.Play(null, position, Quaternion.identity);
		return true;
	}

	private static void ReleaseGibs(Corpse item)
	{
		Corpse corpse = default(Corpse);
		while (item.Gibs.Count > 0)
		{
			int count = item.Gibs.Count;
			CD_Gib cD_Gib = item.Gibs[count - 1];
			if (cD_Gib == null)
			{
				item.Gibs.RemoveAt(count - 1);
				continue;
			}
			try
			{
				int num;
				if (GibOwners.TryGetValue(cD_Gib.Pointer, out corpse))
				{
					num = ((corpse == item) ? 1 : 0);
					if (num != 0)
					{
						GibReused(cD_Gib);
						goto IL_006e;
					}
				}
				else
				{
					num = 0;
				}
				item.Gibs.RemoveAt(count - 1);
				goto IL_006e;
				IL_006e:
				if (num != 0)
				{
					cD_Gib.Deactivate();
				}
			}
			catch (System.Exception error)
			{
				Warn(error);
				if (item.Gibs.Count == count)
				{
					item.Gibs.RemoveAt(count - 1);
				}
			}
		}
	}

	private static void Release(Corpse item, bool removeHidden)
	{
		try
		{
			ReleaseGibs(item);
		}
		catch (System.Exception error)
		{
			Warn(error);
		}
		try
		{
			if (item.Model != null && FeedbackPolicy.IsLeaseCurrent(item.ModelGeneration, item.Model.Generation))
			{
				item.Model.RestoreState();
			}
		}
		catch (System.Exception error2)
		{
			Warn(error2);
		}
		try
		{
			if (item.Ragdoll != null && FeedbackPolicy.IsLeaseCurrent(item.RagdollGeneration, item.Ragdoll.Generation))
			{
				item.Ragdoll.RestoreState();
			}
		}
		catch (System.Exception error3)
		{
			Warn(error3);
		}
		if (removeHidden)
		{
			Hidden.Remove(item.Identity);
			DeathLedger.Forget(item.Identity.ToInt64());
		}
	}

	internal static void Restore(EnemyAgent enemy, string reason)
	{
		if (enemy == null)
		{
			return;
		}
		System.IntPtr pointer = enemy.Pointer;
		DeathLedger.Forget(pointer.ToInt64());
		Corpse corpse = default(Corpse);
		Corpse corpse2 = (Hidden.TryGetValue(pointer, out corpse) ? corpse : null);
		bool flag = false;
		for (int num = Active.Count - 1; num >= 0; num--)
		{
			if (Active[num].Identity == pointer && Active[num] == corpse2)
			{
				flag = true;
			}
		}
		if (corpse2 != null && !flag)
		{
			Log($"hidden=restored;scheduleId={corpse2.Sequence};source={corpse2.Source};identity=0x{pointer.ToInt64():X};reason={reason}");
			Release(corpse2, removeHidden: true);
		}
		for (int num2 = Active.Count - 1; num2 >= 0; num2--)
		{
			Corpse corpse3 = Active[num2];
			if (!(corpse3.Identity != pointer))
			{
				if (FeedbackPolicy.ShouldKeepCorpseForPendingLaunch(LeaseState(corpse3), corpse3.LaunchApplications, reason))
				{
					Log($"queue=kept;scheduleId={corpse3.Sequence};source={corpse3.Source};identity=0x{pointer.ToInt64():X};reason={reason};launch={LaunchState(corpse3)};bodies={corpse3.Ragdoll.BodyCount};note=pending-launch-survives-despawn;handles=dropped");
					corpse3.Enemy = null;
					corpse3.EnemyRoot = null;
					corpse3.ModelRoot = null;
					corpse3.Model = null;
					corpse3.ModelGeneration = 0;
				}
				else
				{
					RemoveQueued(corpse3, reason, Time.unscaledTime);
					Active.RemoveAt(num2);
				}
			}
		}
	}

	internal static void EnterLevel()
	{
		DeathLedger.EnterLevel();
		_outsideLevelSkipLogged = false;
	}

	internal static void Reset(string reason)
	{
		//IL_00a9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ae: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e7: Unknown result type (might be due to invalid IL or missing references)
		float unscaledTime = Time.unscaledTime;
		Log($"queue=reset;reason={reason};active={Active.Count};hidden={Hidden.Count};dedupe={DeathLedger.Count};inLevel={DeathLedger.InLevel}");
		var enumerator = Active.GetEnumerator();
		try
		{
			while (enumerator.MoveNext())
			{
				RemoveQueued(enumerator.Current, reason, unscaledTime);
			}
		}
		finally
		{
			((System.IDisposable)enumerator).Dispose();
		}
		var enumerator2 = Hidden.Values.GetEnumerator();
		try
		{
			while (enumerator2.MoveNext())
			{
				Corpse current = enumerator2.Current;
				Log($"hidden=restored;scheduleId={current.Sequence};source={current.Source};identity=0x{current.Identity.ToInt64():X};reason={reason}");
				Release(current, removeHidden: false);
			}
		}
		finally
		{
			((System.IDisposable)enumerator2).Dispose();
		}
		Active.Clear();
		Hidden.Clear();
		ActiveIdentities.Clear();
		GibOwners.Clear();
		GibOrigins.Clear();
		GibScales.Clear();
		DeathLedger.Reset();
		PruneKeys.Clear();
		_deathPool = null;
		_fragmentOwner = null;
		_pruneAt = 0f;
		_outsideLevelSkipLogged = false;
		ExplosionSound.Reset();
		_pendingSoundPosition = Vector3.zero;
		_duplicateSkips.Reset();
		_noisyPrunes.Reset();
		_gibUnavailableLogged = false;
		CorpseFade.Reset();
	}

	private static void Warn(System.Exception error)
	{
		if (_warnings++ < 6)
		{
			Plugin.Instance?.Log.LogWarning("[CORPSE] " + error.Message);
		}
	}
}
