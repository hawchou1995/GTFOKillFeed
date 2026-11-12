using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Agents;
using BepInEx.Core.Logging.Interpolation;
using BepInEx.Logging;
using Enemies;
using GameData;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Player;
using SNetwork;
using UnityEngine;

namespace HawChow.GTFOKillFeed;

internal static class KillTracker
{
	private static readonly KillLedger Ledger = new KillLedger();

	private static readonly KillCardQueue Cards = new KillCardQueue();

	private static readonly BurstHistory Bursts = new BurstHistory();

	[ThreadStatic]
	private static DamageScope _active;

	private static int _diagnosticBudget = 12;

	internal static void Reset(string reason = "unspecified")
	{
		Ledger.Reset();
		Cards.Clear();
		Bursts.Clear();
		_active = null;
		DamageNumbers.Clear();
		CorpseFeedback.Reset(reason);
		TeammateMarks.Reset(reason);
		DamageFx.Reset(reason);
		HitSound.Reset(reason);
		HitMarkerHud.Reset(reason);
		KillIconHud.Reset(reason);
	}

	internal static void Spawned(Dam_EnemyDamageBase enemy)
	{
		if (!(enemy == null))
		{
			Ledger.Forget(enemy.Pointer.ToInt64());
			Bursts.Remove(enemy.Pointer.ToInt64());
		}
	}

	private static DamageScope Begin(Dam_EnemyDamageBase enemy, Agent sourceAgent, int limbId, Vector3 position, Vector3 direction)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_0095: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a1: Unknown result type (might be due to invalid IL or missing references)
		//IL_004f: Unknown result type (might be due to invalid IL or missing references)
		//IL_005d: Unknown result type (might be due to invalid IL or missing references)
		ValueTuple<bool, ulong> val = ResolveSource(sourceAgent);
		System.IntPtr intPtr = ((enemy != null) ? enemy.Pointer : System.IntPtr.Zero);
		DamageRoot damageRoot = _active?.Root;
		if (damageRoot == null || damageRoot.HitRecorded || damageRoot.EnemyPointer != intPtr || damageRoot.PlayerLookup != val.Item2 || damageRoot.LocalSource != val.Item1)
		{
			damageRoot = new DamageRoot
			{
				Kind = HitKind.Normal,
				EnemyPointer = intPtr,
				HealthBefore = ((enemy != null) ? enemy.Health : 0f),
				LocalSource = val.Item1,
				PlayerLookup = val.Item2,
				Position = position,
				Direction = direction
			};
		}
		if (enemy != null && limbId >= 0 && enemy.DamageLimbs != null && limbId < enemy.DamageLimbs.Length)
		{
			Dam_EnemyDamageLimb dam_EnemyDamageLimb = enemy.DamageLimbs[limbId];
			if (dam_EnemyDamageLimb != null)
			{
				damageRoot.LimbObserved = true;
				damageRoot.Weakspot = dam_EnemyDamageLimb.m_type == eLimbDamageType.Weakspot;
				damageRoot.Kind = (damageRoot.Weakspot ? HitKind.Weakspot : ((dam_EnemyDamageLimb.m_type == eLimbDamageType.Armor) ? HitKind.Armor : HitKind.Normal));
			}
		}
		return _active = new DamageScope
		{
			Previous = _active,
			Root = damageRoot
		};
	}

	internal static DamageScope BeginLimb(Dam_EnemyDamageLimb limb, float damage, Agent source, Vector3 position, Vector3 direction, float precision, bool behind, bool melee, float backstabber = 1f, float sleeper = 1f, uint gearCategoryId = 0u)
	{
		Dam_EnemyDamageBase dam_EnemyDamageBase = ((limb != null) ? limb.m_base : null);
		DamageScope damageScope = Begin(dam_EnemyDamageBase, source, (limb != null) ? limb.m_limbID : (-1), position, direction);
		DamageRoot root = damageScope.Root;
		if (gearCategoryId != 0)
		{
			root.GearCategoryId = gearCategoryId;
		}
		if (root.LocalSource && root.LimbObserved)
		{
			damageScope.ImpactId = HitMarkerHud.BeginImpact(position, root.Kind, local: true);
		}
		if (!root.LocalSource || root.HealthBefore <= 0f || dam_EnemyDamageBase == null || limb == null)
		{
			return damageScope;
		}
		try
		{
			float num = AgentModifierManager.ApplyModifier(dam_EnemyDamageBase.Owner, melee ? AgentModifier.MeleeResistance : AgentModifier.ProjectileResistance, damage);
			if (melee && dam_EnemyDamageBase.Owner.Locomotion.CurrentStateEnum == ES_StateEnum.Hibernate)
			{
				num *= sleeper;
			}
			num = limb.ApplyWeakspotAndArmorModifiers(num, precision);
			if (behind)
			{
				num = limb.ApplyDamageFromBehindBonus(num, position, direction, backstabber);
			}
			root.Damage = Dam_EnemyDamageBase.RoundDamage(num);
			root.HasValue = KillPolicy.IsFinite(root.Damage) && root.Damage >= 0f;
		}
		catch (System.Exception ex)
		{
			Note("modifier capture unavailable: " + ((MemberInfo)ex.GetType()).Name);
		}
		return damageScope;
	}

	internal static DamageScope BeginBullet(Dam_EnemyDamageBase enemy, pBulletDamageData data)
	{
		Agent comp = null;
		try
		{
			data.source.TryGet(out comp);
		}
		catch
		{
		}
		DamageScope damageScope = Begin(enemy, comp, data.limbID, (enemy != null) ? (enemy.transform.position + Vector3.up) : Vector3.zero, Vector3.zero);
		if (data.gearCategoryId != 0)
		{
			damageScope.Root.GearCategoryId = data.gearCategoryId;
		}
		return damageScope;
	}

	internal static DamageScope BeginProcessedDamage(Dam_EnemyDamageBase enemy, Agent source, float damage, Vector3 position, Vector3 direction, int limbId, uint gearCategoryId = 0u)
	{
		DamageScope damageScope = Begin(enemy, source, limbId, position, direction);
		if (gearCategoryId != 0)
		{
			damageScope.Root.GearCategoryId = gearCategoryId;
		}
		damageScope.Root.Processed = true;
		damageScope.Root.Damage = damage;
		damageScope.Root.HasValue = KillPolicy.IsFinite(damage) && damage >= 0f;
		damageScope.Root.Position = position;
		damageScope.Root.Direction = direction;
		if (damageScope.Root.LocalSource && damageScope.Root.LimbObserved)
		{
			damageScope.ImpactId = HitMarkerHud.BeginImpact(position, damageScope.Root.Kind, local: true);
		}
		return damageScope;
	}

	private static void RecordHit(Dam_EnemyDamageBase enemy, DamageRoot root)
	{
		if (root.HitRecorded || !root.LocalSource || root.HealthBefore <= 0f)
		{
			return;
		}
		root.HitRecorded = true;
		float num = (root.HasValue ? root.Damage : Math.Max(0f, root.HealthBefore - enemy.Health));
		if (!KillPolicy.IsFinite(num) || num < 0f)
		{
			return;
		}
		if (root.Processed && enemy.Health >= root.HealthBefore)
		{
			num = 0f;
		}
		if (num > 0f)
		{
			Bursts.Add(root.EnemyPointer.ToInt64(), Time.unscaledTime, num);
		}
		if (num > 0f || root.Kind == HitKind.Armor)
		{
			DamageNumbers.Add(root.Position, num, root.Kind, preview: false, root.GearCategoryId);
			if (num > 0f)
			{
				DamageFx.NoteDamage(num);
			}
			HitSound.OnLocalHit(root.Kind);
			Note($"number={num:0.####};kind={root.Kind};source={(root.Processed ? "final-settlement" : (root.HasValue ? "native-modifiers" : "health-delta"))}");
		}
	}

	internal static void Complete(Dam_EnemyDamageBase enemy, DamageScope scope)
	{
		if (enemy == null || scope?.Root == null)
		{
			return;
		}
		DamageRoot root = scope.Root;
		if (root.EnemyPointer != enemy.Pointer || !root.LocalSource)
		{
			return;
		}
		RecordHit(enemy, root);
		KillDecision killDecision = KillPolicy.ClassifyLethalHit(root.LocalSource, root.HealthBefore, enemy.Health, root.LimbObserved, root.Weakspot);
		if (!killDecision.Credited || root.Credited || !Ledger.TryCredit(root.EnemyPointer.ToInt64()))
		{
			return;
		}
		root.Credited = true;
		EnemyAgent owner = enemy.Owner;
		KillTierBoundaries tiers = KillPolicy.ResolveTierBoundaries((Plugin.KillMinorHealthThreshold != null) ? Plugin.KillMinorHealthThreshold.Value : 120f, (Plugin.KillHeavyHealthThreshold != null) ? Plugin.KillHeavyHealthThreshold.Value : 1000f);
		KillPresentation presentation = KillPresentationPolicy.Build(killDecision.Kind, enemy.HealthMax, in tiers, IsFlyer(owner), HasArmorLimb(enemy));
		HitSound.OnLocalKill(presentation.Tier, presentation.Flight);
		float unscaledTime = Time.unscaledTime;
		int num = SnapshotKillIcon(owner, presentation, unscaledTime, root.Position);
		float iconExpiresAt = ((num > 0) ? (unscaledTime + KillIconPolicy.Lifetime((Plugin.KillIconLifetime != null) ? Plugin.KillIconLifetime.Value : 2.2f)) : 0f);
		if (owner == null || owner == null)
		{
			return;
		}
		float distanceMeters = 0f / 0f;
		try
		{
			PlayerAgent localPlayerAgent = PlayerManager.GetLocalPlayerAgent();
			if (localPlayerAgent != null)
			{
				distanceMeters = Vector3.Distance(localPlayerAgent.transform.position, owner.transform.position);
			}
		}
		catch
		{
		}
		string text = ResolveEnemyName(owner);
		string t = EnemyNames.Resolve(owner.EnemyDataID, text);
		KillCard killCard = new KillCard(killDecision.Kind, presentation.Tier, text, distanceMeters, enemy.HealthMax, unscaledTime, Plugin.MaxDistance?.Value ?? 999f, owner.EnemyDataID, presentation.Flight, num, iconExpiresAt);
		Cards.Enqueue(killCard, unscaledTime, Plugin.CardLifetime.Value, Plugin.FadeSeconds.Value, ClampCards(Plugin.MaxCards.Value));
		ManualLogSource log = Plugin.Instance.Log;
		bool isEnabled;
		BepInExInfoLogInterpolatedStringHandler bepInExInfoLogInterpolatedStringHandler = new BepInExInfoLogInterpolatedStringHandler(107, 9, out isEnabled);
		if (isEnabled)
		{
			bepInExInfoLogInterpolatedStringHandler.AppendLiteral("[KILLFEED] classification=");
			bepInExInfoLogInterpolatedStringHandler.AppendFormatted((killDecision.Kind == KillKind.Weakspot) ? "WEAKSPOT" : "NORMAL");
			bepInExInfoLogInterpolatedStringHandler.AppendLiteral(";tier=");
			bepInExInfoLogInterpolatedStringHandler.AppendFormatted(presentation.Tier);
			bepInExInfoLogInterpolatedStringHandler.AppendLiteral(";flight=");
			bepInExInfoLogInterpolatedStringHandler.AppendFormatted(presentation.Flight);
			bepInExInfoLogInterpolatedStringHandler.AppendLiteral(";healthMax=");
			((BepInExLogInterpolatedStringHandler)bepInExInfoLogInterpolatedStringHandler).AppendFormatted<float>(enemy.HealthMax, "0.##");
			bepInExInfoLogInterpolatedStringHandler.AppendLiteral(";enemyId=");
			bepInExInfoLogInterpolatedStringHandler.AppendFormatted(owner.EnemyDataID);
			bepInExInfoLogInterpolatedStringHandler.AppendLiteral(";rawName=");
			bepInExInfoLogInterpolatedStringHandler.AppendFormatted(text);
			bepInExInfoLogInterpolatedStringHandler.AppendLiteral(";displayName=");
			bepInExInfoLogInterpolatedStringHandler.AppendFormatted(t);
			bepInExInfoLogInterpolatedStringHandler.AppendLiteral(";distance=");
			bepInExInfoLogInterpolatedStringHandler.AppendFormatted(killCard.DistanceText);
			bepInExInfoLogInterpolatedStringHandler.AppendLiteral(";limbObserved=");
			bepInExInfoLogInterpolatedStringHandler.AppendFormatted(root.LimbObserved);
			bepInExInfoLogInterpolatedStringHandler.AppendLiteral(".");
		}
		log.LogInfo(bepInExInfoLogInterpolatedStringHandler);
	}

	internal static void CompleteFromAgent(EnemyAgent agent)
	{
		if (agent?.Damage != null)
		{
			System.IntPtr? intPtr = _active?.Root?.EnemyPointer;
			System.IntPtr pointer = agent.Damage.Pointer;
			if (intPtr.HasValue && intPtr.GetValueOrDefault() == pointer)
			{
				Complete(agent.Damage, _active);
			}
		}
	}

	internal static void EnemyDied(EnemyAgent agent)
	{
		if (agent == null)
		{
			return;
		}
		try
		{
			CompleteFromAgent(agent);
		}
		finally
		{
			ScheduleCorpseForDeath(agent, "EnemyAgent.OnDead");
		}
	}

	internal static void ScheduleCorpseForDeath(EnemyAgent agent, string source)
	{
		if (agent == null)
		{
			return;
		}
		System.IntPtr intPtr = System.IntPtr.Zero;
		try
		{
			if (agent.Damage != null)
			{
				intPtr = agent.Damage.Pointer;
			}
		}
		catch
		{
		}
		DamageRoot damageRoot = _active?.Root;
		Vector3 direction = ((intPtr != System.IntPtr.Zero && damageRoot != null && damageRoot.EnemyPointer == intPtr) ? damageRoot.Direction : Vector3.zero);
		float num = ((intPtr != System.IntPtr.Zero) ? Bursts.Sum(intPtr.ToInt64(), Time.unscaledTime) : 0f);
		float strength = ((num > 0f) ? FeedbackPolicy.Strength(num) : 0.5f);
		CorpseFeedback.Schedule(agent, direction, strength, source);
	}

	internal static void CompleteFromActive(Dam_EnemyDamageBase enemy)
	{
		if (enemy == null)
		{
			return;
		}
		EnemyAgent agent = null;
		try
		{
			agent = enemy.Owner;
			System.IntPtr? intPtr = _active?.Root?.EnemyPointer;
			System.IntPtr pointer = enemy.Pointer;
			if (intPtr.HasValue && intPtr.GetValueOrDefault() == pointer)
			{
				Complete(enemy, _active);
			}
		}
		finally
		{
			ScheduleCorpseForDeath(agent, "Dam_EnemyDamageBase.InstantDead");
		}
	}

	internal static System.Exception End(Dam_EnemyDamageBase enemy, DamageScope scope, System.Exception error)
	{
		try
		{
			if (error == null)
			{
				try
				{
					Complete(enemy, scope);
				}
				catch (System.Exception ex)
				{
					Note("capture skipped: " + ex.Message);
				}
			}
			try
			{
				DamageRoot damageRoot = scope?.Root;
				if (enemy != null && damageRoot != null && damageRoot.EnemyPointer == enemy.Pointer && KillPolicy.CrossedLethalHealth(damageRoot.HealthBefore, enemy.Health))
				{
					ScheduleCorpseForDeath(enemy.Owner, "lethal-damage");
				}
			}
			catch (System.Exception ex2)
			{
				Note("lethal corpse scheduling skipped: " + ((MemberInfo)ex2.GetType()).Name);
			}
		}
		finally
		{
			HitMarkerHud.EndImpact(scope?.ImpactId ?? 0);
			if (_active == scope)
			{
				_active = scope?.Previous;
			}
		}
		return error;
	}

	internal static VisibleKillCard[] Snapshot(float now)
	{
		return Cards.Snapshot(now, Plugin.CardLifetime.Value, Plugin.FadeSeconds.Value, ClampCards(Plugin.MaxCards.Value));
	}

	internal static void AddPreview(bool weakspot)
	{
		float unscaledTime = Time.unscaledTime;
		if (KillIconPolicy.ShouldQueueCardPreviews(KillIconPolicy.ParseDisplay((Plugin.KillIconDisplay != null) ? Plugin.KillIconDisplay.Value : null)))
		{
			Cards.Enqueue(new KillCard(weakspot ? KillKind.Weakspot : KillKind.Normal, KillPresentationPolicy.PreviewTier(weakspot), weakspot ? "大壮" : "小白", weakspot ? 13f : 27f, weakspot ? 120f : 80f, unscaledTime, 999f, 0u, KillFlight.None, -1), unscaledTime, Plugin.CardLifetime.Value, Plugin.FadeSeconds.Value, ClampCards(Plugin.MaxCards.Value));
		}
		KillIconHud.AddPreview(unscaledTime);
	}

	private static int SnapshotKillIcon(EnemyAgent agent, KillPresentation presentation, float now, Vector3 lethalPosition)
	{
		if (agent == null)
		{
			return 0;
		}
		try
		{
			float gapMetres = KillIconPolicy.Gap((Plugin.KillIconGap != null) ? Plugin.KillIconGap.Value : 0.3f);
			bool barEnabled = Plugin.EnemyBarEnabled == null || Plugin.EnemyBarEnabled.Value;
			bool barHeadMode = BarPolicy.IsHeadMode((Plugin.EnemyBarAnchor != null) ? Plugin.EnemyBarAnchor.Value : null);
			float barHeadOffsetMetres = BarPolicy.HeadOffset((Plugin.EnemyBarHeadOffset != null) ? Plugin.EnemyBarHeadOffset.Value : 0.12f);
			float num = KillIconPolicy.HeadAnchorOffset(barEnabled, barHeadMode, barHeadOffsetMetres, gapMetres);
			if (EnemyFocus.TryGetHeadWorld(agent, num, out var world))
			{
				return KillIconHud.Add(agent, world, presentation, num, now, "head-world");
			}
			Vector3 anchor = lethalPosition + Vector3.up * num;
			if (!KillPolicy.IsFinite(anchor.x) || !KillPolicy.IsFinite(anchor.y) || !KillPolicy.IsFinite(anchor.z))
			{
				return 0;
			}
			return KillIconHud.Add(agent, anchor, presentation, num, now, "lethal-position");
		}
		catch (System.Exception ex)
		{
			Note("kill-icon snapshot skipped: " + ((MemberInfo)ex.GetType()).Name);
			return 0;
		}
	}

	internal static void ObserveState(eGameStateName state)
	{
		if (state == eGameStateName.InLevel)
		{
			CorpseFeedback.EnterLevel();
			TeammateMarks.EnterLevel();
		}
		else
		{
			Reset("state:" + ((object)state).ToString());
		}
	}

	internal static void LogPatchReceipts(Harmony harmony, ManualLogSource log)
	{
		System.Collections.Generic.IEnumerator<MethodBase> enumerator = harmony.GetPatchedMethods().GetEnumerator();
		try
		{
			while (((System.Collections.IEnumerator)enumerator).MoveNext())
			{
				MethodBase current = enumerator.Current;
				Patches patchInfo = Harmony.GetPatchInfo(current);
				bool isEnabled;
				BepInExInfoLogInterpolatedStringHandler bepInExInfoLogInterpolatedStringHandler = new BepInExInfoLogInterpolatedStringHandler(33, 5, out isEnabled);
				if (isEnabled)
				{
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral("[DIAG] patch ");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted(((MemberInfo)((MemberInfo)current).DeclaringType).Name);
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral(".");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted(((MemberInfo)current).Name);
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral(": pre=");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted(patchInfo.Prefixes.Count);
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral(",post=");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted(patchInfo.Postfixes.Count);
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral(",final=");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted(patchInfo.Finalizers.Count);
				}
				log.LogInfo(bepInExInfoLogInterpolatedStringHandler);
			}
		}
		finally
		{
			((System.IDisposable)enumerator)?.Dispose();
		}
	}

	private static int ClampCards(int value)
	{
		return Math.Max(1, Math.Min(6, value));
	}

	private static ValueTuple<bool, ulong> ResolveSource(Agent source)
	{
		//IL_0047: Unknown result type (might be due to invalid IL or missing references)
		//IL_004c: Unknown result type (might be due to invalid IL or missing references)
		//IL_004f: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0040: Unknown result type (might be due to invalid IL or missing references)
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			SNet_Player sNet_Player = source?.TryCast<PlayerAgent>()?.Owner;
			return (sNet_Player != null && sNet_Player.IsLocal) ? new ValueTuple<bool, ulong>(true, sNet_Player.Lookup) : new ValueTuple<bool, ulong>(false, 0uL);
		}
		catch
		{
			return new ValueTuple<bool, ulong>(false, 0uL);
		}
	}

	private static bool IsFlyer(EnemyAgent agent)
	{
		if (agent == null)
		{
			return false;
		}
		try
		{
			EnemyBehaviorDataBlock enemyBehaviorDataBlock = agent.EnemyBehaviorData;
			if (enemyBehaviorDataBlock == null)
			{
				uint num = agent.EnemyData?.BehaviorDataId ?? 0;
				if (num != 0)
				{
					enemyBehaviorDataBlock = GameDataBlockBase<EnemyBehaviorDataBlock>.GetBlock(num);
				}
			}
			return enemyBehaviorDataBlock?.IsFlyer ?? false;
		}
		catch (System.Exception ex)
		{
			Note("flyer probe skipped: " + ((MemberInfo)ex.GetType()).Name);
			return false;
		}
	}

	private static bool HasArmorLimb(Dam_EnemyDamageBase enemy)
	{
		if (enemy == null)
		{
			return false;
		}
		try
		{
			Il2CppReferenceArray<Dam_EnemyDamageLimb> damageLimbs = enemy.DamageLimbs;
			if (damageLimbs == null)
			{
				return false;
			}
			for (int i = 0; i < damageLimbs.Length; i++)
			{
				Dam_EnemyDamageLimb dam_EnemyDamageLimb = damageLimbs[i];
				if (dam_EnemyDamageLimb != null && dam_EnemyDamageLimb.m_type == eLimbDamageType.Armor)
				{
					return true;
				}
			}
		}
		catch (System.Exception ex)
		{
			Note("armor probe skipped: " + ((MemberInfo)ex.GetType()).Name);
		}
		return false;
	}

	private static string ResolveEnemyName(EnemyAgent agent)
	{
		try
		{
			EnemyDataBlock block = GameDataBlockBase<EnemyDataBlock>.GetBlock(agent.EnemyDataID);
			if (block != null && !string.IsNullOrWhiteSpace(block.name))
			{
				return block.name;
			}
		}
		catch
		{
		}
		try
		{
			return agent.name;
		}
		catch
		{
			return "Unknown";
		}
	}

	internal static DamageScope Guard(Func<DamageScope> capture)
	{
		DamageScope active = _active;
		try
		{
			return capture.Invoke();
		}
		catch (System.Exception ex)
		{
			_active = active;
			Note("capture unavailable: " + ex.Message);
			return null;
		}
	}

	internal static void GuardAction(Action action)
	{
		try
		{
			action.Invoke();
		}
		catch (System.Exception ex)
		{
			Note("feedback skipped: " + ex.Message);
		}
	}

	internal static System.Exception EndLimb(Dam_EnemyDamageLimb limb, DamageScope scope, System.Exception error)
	{
		Dam_EnemyDamageBase enemy = null;
		try
		{
			enemy = limb?.m_base;
		}
		catch
		{
		}
		return End(enemy, scope, error);
	}

	private static void Note(string message)
	{
		if (_diagnosticBudget-- > 0)
		{
			Plugin.Instance?.Log.LogInfo("[FEEDBACK] " + message);
		}
	}
}
