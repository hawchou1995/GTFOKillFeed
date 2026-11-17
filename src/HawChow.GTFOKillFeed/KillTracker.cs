using System;
using System.Reflection;
using Agents;
using BepInEx.Logging;
using Enemies;
using GameData;
using HarmonyLib;
using Player;
using UnityEngine;

namespace HawChow.GTFOKillFeed;

internal sealed class DamageRoot
{
    internal IntPtr EnemyPointer;
    internal float HealthBefore;
    internal bool LocalSource, LimbObserved, Weakspot, Credited, HitRecorded, HasValue, Processed;
    internal ulong PlayerLookup;
    internal HitKind Kind;
    // 1.2.19: the entry this root came from. Written once when the root is created and only ever PROMOTED
    // afterwards (FeedbackPolicy.ResolveOrigin), so the inner ProcessReceivedDamage of a bullet can never turn it
    // back into Other - that would un-merge every pellet again. Defaults to Other (0).
    internal DamageOrigin Origin;
    internal float Damage;
    internal Vector3 Position, Direction;
    internal uint GearCategoryId;
}
internal sealed class DamageScope
{
    internal DamageScope Previous;
    internal DamageRoot Root;
    // Token of the local impact scope published for this damage call (0 = none). Popped by the finalizer,
    // so the hit-indicator event the game may show inside the original method can be tied to this impact.
    internal int ImpactId;
}

// The origin label an entry that is NOT one of the four damage roots carries (ReceiveFireDamage / ReceivePushDamage).
// The game calls ProcessReceivedDamage from inside those methods, so the root created there must inherit their kind -
// and a fire tick must never be able to look like a pellet. The hint is a real CALL SCOPE (the prefix pushes it, the
// finalizer pops it), never a frame/time window: it can only ever label the call that is still on the stack, so it
// cannot leak into a later, unrelated hit on the same enemy.
internal sealed class DamageOriginScope
{
    internal DamageOriginScope Previous;
    internal IntPtr EnemyPointer;
    internal DamageOrigin Origin;
}

internal static class KillTracker
{
    private static readonly KillLedger Ledger = new();
    private static readonly KillCardQueue Cards = new();
    private static readonly BurstHistory Bursts = new();
    private static readonly RepeatedLogBudget KillLogBudget = new();
    internal static readonly KillCounterTotals Counters = new();
    [ThreadStatic] private static DamageScope _active;
    [ThreadStatic] private static DamageOriginScope _originScope;
    // 1.2.19 fix: 12 `[FEEDBACK]` lines per LEVEL (not per process). The Survival log spent the whole process budget
    // inside the first shotgun volley (twelve pellets), so the rest of the level - including every later shot - had no
    // feedback at all and "no record" became indistinguishable from "nothing happened".
    private const int FeedbackLines = 12;
    private static int _diagnosticBudget = FeedbackLines;
    private static bool _diagnosticBudgetNoted;

    internal static void Reset(string reason = "unspecified")
    {
        if (KillLogBudget.Suppressed > 0)
            Plugin.Instance?.Log.LogInfo($"[KILLFEED] diagnostics=summary;seen={KillLogBudget.Seen};printed={KillLogBudget.Printed};suppressed={KillLogBudget.Suppressed};policy=first-3-and-every-100-per-kind");
        KillLogBudget.Reset();
        Counters.Reset(); KillCounterHud.Reset();
        Ledger.Reset(); Cards.Clear(); Bursts.Clear(); _active = null; _originScope = null;
        // 1.2.19: the feedback allowance is per LEVEL too (12 lines, then one exhaustion receipt), not per process.
        _diagnosticBudget = FeedbackLines;
        _diagnosticBudgetNoted = false;
        DamageNumbers.Clear(); ShotgunDamageNumberCapture.Reset(); CorpseFeedback.Reset(reason); TeammateMarks.Reset(reason);
        DamageFx.Reset(reason); HitSound.Reset(reason); HitMarkerHud.Reset(reason); KillIconHud.Reset(reason); KillIconOccluders.Reset();
    }
    internal static void Spawned(Dam_EnemyDamageBase enemy)
    {
        if (enemy == null) return;
        Ledger.Forget(enemy.Pointer.ToInt64());
        Bursts.Remove(enemy.Pointer.ToInt64());
    }
    // ---- 1.2.19 damage origin: the enclosing-call proof and its scope ------------------------------------------
    // Fire and push are not damage roots of their own here: the game calls ProcessReceivedDamage from inside them, and
    // that call is where the root is created. So the entry publishes a real CALL SCOPE instead of a root. The prefix
    // pushes it and the finalizer pops it, which means it can only ever label a call that is still on the stack - it
    // is not a frame/time window and it cannot leak into a later, unrelated hit on the same enemy. Nothing about the
    // game is changed: the payload is never read for this and nothing is written back.
    internal static DamageOriginScope BeginOriginScope(Dam_EnemyDamageBase enemy, DamageOrigin origin)
    {
        try
        {
            var scope = new DamageOriginScope
            {
                Previous = _originScope,
                EnemyPointer = enemy != null ? enemy.Pointer : IntPtr.Zero,
                Origin = origin,
            };
            _originScope = scope;
            return scope;
        }
        catch (Exception error) { Note("origin scope unavailable: " + error.GetType().Name); return null; }
    }
    internal static Exception EndOriginScope(DamageOriginScope scope, Exception error)
    {
        // Popped even when the original threw, so a failed fire/push call cannot relabel the next hit.
        try { if (scope != null && ReferenceEquals(_originScope, scope)) _originScope = scope.Previous; }
        catch (Exception popError) { Note("origin scope pop skipped: " + popError.GetType().Name); }
        return error;
    }
    // What an ANONYMOUS entry (ProcessReceivedDamage) may inherit: the still-open fire/push call for this enemy, or a
    // damage root of this enemy that an enclosing entry already created (the bullet that is nesting right now).
    // Nothing else - in particular never a guess from damage values, categories or timing.
    private static DamageOrigin EnclosingOrigin(IntPtr pointer)
    {
        DamageOriginScope hint = _originScope;
        if (hint != null && hint.EnemyPointer == pointer && hint.Origin != DamageOrigin.Other) return hint.Origin;
        DamageRoot enclosing = _active?.Root;
        if (enclosing != null && enclosing.EnemyPointer == pointer) return enclosing.Origin;
        return DamageOrigin.Other;
    }
    // One root per damage event. `entry` is the origin the CALLING ENTRY proved (Bullet from ReceiveBulletDamage /
    // Dam_EnemyDamageLimb.BulletDamage, Melee from MeleeDamage, Other from the anonymous ProcessReceivedDamage).
    // The root is shared by everything that entry calls, so its origin may only be promoted there - never replaced -
    // and a fresh root created by the anonymous entry inherits the enclosing call's proof (bullet ancestry, fire,
    // push) instead of guessing.
    private static DamageScope Begin(Dam_EnemyDamageBase enemy, Agent sourceAgent, int limbId, Vector3 position, Vector3 direction, DamageOrigin entry)
    {
        var source = ResolveSource(sourceAgent);
        IntPtr pointer = enemy != null ? enemy.Pointer : IntPtr.Zero;
        DamageRoot root = _active?.Root;
        bool reused = root != null && !root.HitRecorded && root.EnemyPointer == pointer
            && root.PlayerLookup == source.Lookup && root.LocalSource == source.Local;
        if (reused)
        {
            // Reuse NEVER writes Other over Bullet: that is exactly the downgrade that would un-merge every pellet,
            // because the inner ProcessReceivedDamage entry is anonymous by construction.
            root.Origin = FeedbackPolicy.ResolveOrigin(true, root.Origin, entry, DamageOrigin.Other);
        }
        else
        {
            root = new DamageRoot
            {
                Kind = HitKind.Normal,
                Origin = FeedbackPolicy.ResolveOrigin(false, DamageOrigin.Other, entry, EnclosingOrigin(pointer)),
                EnemyPointer = pointer,
                HealthBefore = enemy != null ? enemy.Health : 0f,
                LocalSource = source.Local,
                PlayerLookup = source.Lookup,
                Position = position,
                Direction = direction,
            };
        }
        if (enemy != null && limbId >= 0 && enemy.DamageLimbs != null && limbId < enemy.DamageLimbs.Length)
        {
            var limb = enemy.DamageLimbs[limbId];
            if (limb != null)
            {
                root.LimbObserved = true;
                root.Weakspot = limb.m_type == eLimbDamageType.Weakspot;
                root.Kind = root.Weakspot ? HitKind.Weakspot : limb.m_type == eLimbDamageType.Armor ? HitKind.Armor : HitKind.Normal;
            }
        }
        var scope = new DamageScope { Previous = _active, Root = root };
        _active = scope;
        return scope;
    }
    internal static DamageScope BeginLimb(Dam_EnemyDamageLimb limb, float damage, Agent source, Vector3 position, Vector3 direction, float precision, bool behind, bool melee, float backstabber = 1f, float sleeper = 1f, uint gearCategoryId = 0)
    {
        var enemy = limb != null ? limb.m_base : null;
        // 1.2.19: the limb path names its own origin before anything is read. MeleeDamage is a melee blow, BulletDamage
        // is a projectile - this is the entry proof the pellet merge asks for, not a category or a timing guess.
        DamageOrigin entry = melee ? DamageOrigin.Melee : DamageOrigin.Bullet;
        var scope = Begin(enemy, source, limb != null ? limb.m_limbID : -1, position, direction, entry);
        var root = scope.Root;
        if (gearCategoryId != 0) root.GearCategoryId = gearCategoryId;
        // Publish the impact BEFORE the original damage method runs. The game may show its hit indicator from
        // inside that method, so a context created later (in the finalizer) would arrive too late - and the
        // position here is the game's own impact point, which is what the indicator event will carry.
        if (root.LocalSource && root.LimbObserved) scope.ImpactId = HitMarkerHud.BeginImpact(position, root.Kind, true);
        if (!root.LocalSource || root.HealthBefore <= 0 || enemy == null || limb == null) return scope;
        try
        {
            float value = AgentModifierManager.ApplyModifier(enemy.Owner, melee ? AgentModifier.MeleeResistance : AgentModifier.ProjectileResistance, damage);
            if (melee && enemy.Owner.Locomotion.CurrentStateEnum == ES_StateEnum.Hibernate) value *= sleeper;
            value = limb.ApplyWeakspotAndArmorModifiers(value, precision);
            if (behind) value = limb.ApplyDamageFromBehindBonus(value, position, direction, backstabber);
            root.Damage = Dam_EnemyDamageBase.RoundDamage(value);
            root.HasValue = KillPolicy.IsFinite(root.Damage) && root.Damage >= 0;
        }
        catch (Exception error) { Note("modifier capture unavailable: " + error.GetType().Name); }
        return scope;
    }
    internal static DamageScope BeginBullet(Dam_EnemyDamageBase enemy, pBulletDamageData data)
    {
        Agent source = null;
        try { data.source.TryGet(out source); } catch { }
        var scope = Begin(enemy, source, data.limbID, enemy != null ? enemy.transform.position + Vector3.up : Vector3.zero, Vector3.zero, DamageOrigin.Bullet);
        // Estimated pivot, not an impact point: this path deliberately publishes NO impact scope, so an
        // armour verdict can never be taken from a guessed position.
        if (data.gearCategoryId != 0) scope.Root.GearCategoryId = data.gearCategoryId;
        return scope;
    }
    internal static DamageScope BeginProcessedDamage(Dam_EnemyDamageBase enemy, Agent source, float damage, Vector3 position, Vector3 direction, int limbId, uint gearCategoryId = 0)
    {
        // 1.2.19: this entry is ANONYMOUS - it is the settlement step of whatever called it (a bullet, a melee blow, a
        // fire tick, a push, an explosion...). It therefore passes Other and lets the shared root / the enclosing call
        // decide, so it can never invent a Bullet and never overwrite one that the real entry already proved.
        var scope = Begin(enemy, source, limbId, position, direction, DamageOrigin.Other);
        if (gearCategoryId != 0) scope.Root.GearCategoryId = gearCategoryId;
        scope.Root.Processed = true;
        scope.Root.Damage = damage;
        scope.Root.HasValue = KillPolicy.IsFinite(damage) && damage >= 0;
        scope.Root.Position = position; scope.Root.Direction = direction;
        // Same rule as the limb path: ProcessReceivedDamage carries the game's real impact point, and this
        // prefix runs before the original method (which may show the indicator itself).
        if (scope.Root.LocalSource && scope.Root.LimbObserved) scope.ImpactId = HitMarkerHud.BeginImpact(position, scope.Root.Kind, true);
        return scope;
    }
    private static void RecordHit(Dam_EnemyDamageBase enemy, DamageRoot root)
    {
        if (root.HitRecorded || !root.LocalSource || root.HealthBefore <= 0) return;
        root.HitRecorded = true;
        float value = root.HasValue ? root.Damage : Math.Max(0f, root.HealthBefore - enemy.Health);
        if (!KillPolicy.IsFinite(value) || value < 0) return;
        if (root.Processed && enemy.Health >= root.HealthBefore) value = 0;
        if (value > 0) Bursts.Add(root.EnemyPointer.ToInt64(), Time.unscaledTime, value);
        // The crosshair marker is a hit confirmation, not a damage readout: it fires even when the blow dealt
        // no readable damage (immunity / already-dead settlement), where the vanilla indicator would also show.
        // The hit/kill lines come from the game's own world-position indicator events; the armour verdict is
        // carried by the impact scope published in the prefix (see BeginLimb/BeginProcessedDamage), never by
        // this late point in the pipeline.
        if (value > 0 || root.Kind == HitKind.Armor)
        {
            // 1.2.19: the root's origin travels with the number, and the classifier reports which route it took. The
            // route is the fact the Survival log could not show: twelve per-pellet `[FEEDBACK]` lines appear both when
            // a shot merged and when every pellet drew its own number.
            // D6: `root.PlayerLookup` is the local player this damage came from (ResolveSource: 0 for anybody else),
            // so the volley can refuse a hit that provably belongs to another shooter instead of summing it in.
            DamageNumberRoute route = DamageNumbers.Add(root.Position, value, root.Kind, false, root.GearCategoryId, root.Origin, root.PlayerLookup);
            if (value > 0f) DamageFx.NoteDamage(value);
            HitSound.OnLocalHit(root.Kind);
            Note($"number={value:0.####};kind={root.Kind};origin={root.Origin};route={route};hitCat={root.GearCategoryId};frame={Time.frameCount};source={(root.Processed ? "final-settlement" : root.HasValue ? "native-modifiers" : "health-delta")}");
        }
    }
    internal static void Complete(Dam_EnemyDamageBase enemy, DamageScope scope)
    {
        if (enemy == null || scope?.Root == null) return;
        DamageRoot root = scope.Root;
        if (root.EnemyPointer != enemy.Pointer || !root.LocalSource) return;
        RecordHit(enemy, root);
        KillDecision decision = KillPolicy.ClassifyLethalHit(root.LocalSource, root.HealthBefore, enemy.Health, root.LimbObserved, root.Weakspot);
        if (!decision.Credited || root.Credited || !Ledger.TryCredit(root.EnemyPointer.ToInt64())) return;
        root.Credited = true;
        EnemyAgent agent = enemy.Owner;
        // ONE presentation per credited local kill. The tier (live health lines), the flight identity (the
        // enemy's own behavior data) and the kind (the killing limb) are resolved here and then reused by
        // the kill cue, the card and, next phase, the head HUD - nothing re-derives them per frame.
        // The BOSS line is the player's existing KillIcon/HeavyHealthThreshold value, read live: the heavy
        // kill icon and the BOSS tier therefore share one authority, and a legacy cfg keeps its own line.
        KillTierBoundaries tiers = KillPolicy.ResolveTierBoundaries(
            Plugin.KillMinorHealthThreshold != null ? Plugin.KillMinorHealthThreshold.Value : KillDefaults.MinorHealthThreshold,
            Plugin.KillHeavyHealthThreshold != null ? Plugin.KillHeavyHealthThreshold.Value : KillPolicy.DefaultHeavyHealthThreshold);
        KillPresentation presentation = KillPresentationPolicy.Build(decision.Kind, enemy.HealthMax, tiers,
            IsFlyer(agent), HasArmorLimb(enemy));
        Counters.Add(presentation.Tier);
        HitSound.OnLocalKill(presentation.Tier, presentation.Flight);
        // Head-top kill icon: the world anchor is snapshotted ONCE, right here, while this enemy is still the
        // object that just died, and the icon freezes on it (1.2.16). The corpse may sink, be hidden or be
        // pooled into a new enemy later; the icon only ever reprojects the point it captured at the credit.
        // The returned token ties THIS kill to THAT slot. It is 0 when no anchor could be captured at all
        // (no finite head point AND no finite lethal position), which is exactly the case the card has to
        // cover as the visible fallback instead of the kill ending up as sound only (review F1).
        // 1.2.18 (finding 3, FIXED): a kill made while KillIcon/HeadEnabled is OFF must NOT mint an icon token.
        // A token extended this card's lifetime to the (invisible) icon's expiry and left a queued icon that the
        // player's next hot-enable replayed. The two channels are independent: a disabled head channel leaves no
        // trace on the card and cannot replay kills from the time it was off.
        float now = Time.unscaledTime;
        bool headChannel = Plugin.KillIconHeadEnabled != null && Plugin.KillIconHeadEnabled.Value;
        int iconToken = headChannel ? SnapshotKillIcon(agent, presentation, now, root.Position) : 0;
        float iconExpiresAt = iconToken > 0
            ? now + KillIconPolicy.Lifetime(Plugin.KillIconLifetime != null ? Plugin.KillIconLifetime.Value : KillIconPolicy.DefaultLifetimeSeconds)
            : 0f;
        if (agent == null) return;
        // The card is the fallback for a kill that never got a head icon; it needs no name lookup in that case.
        if (agent == null) return;
        float distance = float.NaN;
        try { var local = PlayerManager.GetLocalPlayerAgent(); if (local != null) distance = Vector3.Distance(local.transform.position, agent.transform.position); } catch { }
        string rawName = ResolveEnemyName(agent);
        string displayName = EnemyNames.Resolve(agent.EnemyDataID, rawName);
        var card = new KillCard(decision.Kind, presentation.Tier, rawName, distance, enemy.HealthMax, now,
            Plugin.MaxDistance?.Value ?? KillDefaults.MaxDistance, agent.EnemyDataID, presentation.Flight,
            iconToken, iconExpiresAt);
        Cards.Enqueue(card, now, Plugin.CardLifetime.Value, Plugin.FadeSeconds.Value, ClampCards(Plugin.MaxCards.Value));
        if (KillLogBudget.TryLog(decision.Kind == KillKind.Weakspot ? "classification=WEAKSPOT" : "classification=NORMAL"))
            Plugin.Instance.Log.LogInfo($"[KILLFEED] classification={(decision.Kind == KillKind.Weakspot ? "WEAKSPOT" : "NORMAL")};tier={presentation.Tier};flight={presentation.Flight};healthMax={enemy.HealthMax:0.##};enemyId={agent.EnemyDataID};rawName={rawName};displayName={displayName};distance={card.DistanceText};limbObserved={root.LimbObserved}.");
    }
    internal static void CompleteFromAgent(EnemyAgent agent)
    {
        if (agent?.Damage != null && _active?.Root?.EnemyPointer == agent.Damage.Pointer) Complete(agent.Damage, _active);
    }
    internal static void EnemyDied(EnemyAgent agent)
    {
        if (agent == null) return;
        try { CompleteFromAgent(agent); }
        finally { ScheduleCorpseForDeath(agent, "EnemyAgent.OnDead"); }
    }
    internal static void ScheduleCorpseForDeath(EnemyAgent agent, string source)
    {
        if (agent == null) return;
        IntPtr damagePointer = IntPtr.Zero;
        try { if (agent.Damage != null) damagePointer = agent.Damage.Pointer; } catch { }
        DamageRoot root = _active?.Root;
        Vector3 direction = damagePointer != IntPtr.Zero && root != null && root.EnemyPointer == damagePointer ? root.Direction : Vector3.zero;
        float burstDamage = damagePointer != IntPtr.Zero ? Bursts.Sum(damagePointer.ToInt64(), Time.unscaledTime) : 0f;
        float strength = burstDamage > 0f ? FeedbackPolicy.Strength(burstDamage) : FeedbackPolicy.DefaultDeathStrength;
        CorpseFeedback.Schedule(agent, direction, strength, source);
    }
    internal static void CompleteFromActive(Dam_EnemyDamageBase enemy)
    {
        if (enemy == null) return;
        EnemyAgent agent = null;
        try
        {
            agent = enemy.Owner;
            if (_active?.Root?.EnemyPointer == enemy.Pointer) Complete(enemy, _active);
        }
        finally { ScheduleCorpseForDeath(agent, "Dam_EnemyDamageBase.InstantDead"); }
    }
    internal static Exception End(Dam_EnemyDamageBase enemy, DamageScope scope, Exception error)
    {
        try
        {
            if (error == null)
            {
                try { Complete(enemy, scope); }
                catch (Exception captureError) { Note("capture skipped: " + captureError.Message); }
            }

            try
            {
                DamageRoot root = scope?.Root;
                if (enemy != null && root != null && root.EnemyPointer == enemy.Pointer
                    && KillPolicy.CrossedLethalHealth(root.HealthBefore, enemy.Health))
                {
                    ScheduleCorpseForDeath(enemy.Owner, "lethal-damage");
                }
            }
            catch (Exception scheduleError) { Note("lethal corpse scheduling skipped: " + scheduleError.GetType().Name); }
        }
        finally
        {
            // Pop the impact scope even when the original method threw, so a failed call cannot leave a
            // stale armour verdict behind for a later, unrelated indicator event.
            HitMarkerHud.EndImpact(scope != null ? scope.ImpactId : 0);
            if (ReferenceEquals(_active, scope)) _active = scope?.Previous;
        }
        return error;
    }
    internal static VisibleKillCard[] Snapshot(float now) => Cards.Snapshot(now, Plugin.CardLifetime.Value, Plugin.FadeSeconds.Value, ClampCards(Plugin.MaxCards.Value));
    internal static void AddPreview(bool weakspot)
    {
        float now = Time.unscaledTime;
        // The card channel and the head-icon channel are independent now: F9 queues a preview card only
        // when Card/Enabled is on, and a preview icon only when KillIcon/HeadEnabled is on.
        if (Plugin.KillCardEnabled == null || Plugin.KillCardEnabled.Value)
        {
            // F6: the weakspot sample is the ELITE tier, so its card icon is the medium/weakspot raster again
            // F6: the weakspot sample is the ELITE tier, so its card icon is the medium/weakspot raster again
            // instead of the same light one the normal sample uses. Token -1 marks "preview, not a kill": the
            // card follows the Display switch only and never claims a head icon.
            Cards.Enqueue(new KillCard(weakspot ? KillKind.Weakspot : KillKind.Normal, KillPresentationPolicy.PreviewTier(weakspot),
                weakspot ? "大壮" : "小白", weakspot ? 13f : 27f, weakspot ? 120f : 80f, now,
                KillDefaults.MaxDistance, 0u, KillFlight.None, -1), now, Plugin.CardLifetime.Value, Plugin.FadeSeconds.Value, ClampCards(Plugin.MaxCards.Value));
        }
        // F9 always previews the nine head-icon combinations (tier x flight) when the head channel is on; the
        // preview set replaces itself so a double key press cannot stack 18 preview icons.
        if (Plugin.KillIconHeadEnabled != null && Plugin.KillIconHeadEnabled.Value)
        {
            KillIconHud.AddPreview(now);
        }
    }

    // The head point uses exactly the helper the enemy bar uses, plus a clearance that keeps the icon above
    // the bar when the bar itself is in Head mode. The anchor is frozen HERE (1.2.16): it is captured once,
    // while the enemy is still the object that died, and the icon never re-reads the corpse afterwards.
    // Order of sources, so nothing is ever faked:
    //   1. the enemy's own head point (KillIconPolicy.AnchorSourceHead) - the normal case;
    //   2. the world position the lethal damage call already carried (AnchorSourceLethal), lifted by the same
    //      clearance, because a head lookup can fail on a corpse whose head limb was just destroyed;
    //   3. nothing: the kill returns 0, which is the signal the card uses to show itself as the visible
    //      fallback (review F1). A screen-centre icon is never invented.
    private static int SnapshotKillIcon(EnemyAgent agent, KillPresentation presentation, float now, Vector3 lethalPosition)
    {
        if (agent == null) return 0;
        try
        {
            float gap = KillIconPolicy.Gap(Plugin.KillIconGap != null ? Plugin.KillIconGap.Value : KillIconPolicy.DefaultGapMetres);
            bool barEnabled = Plugin.EnemyBarEnabled == null || Plugin.EnemyBarEnabled.Value;
            bool barHead = BarPolicy.IsHeadMode(Plugin.EnemyBarAnchor != null ? Plugin.EnemyBarAnchor.Value : null);
            float barOffset = BarPolicy.HeadOffset(Plugin.EnemyBarHeadOffset != null ? Plugin.EnemyBarHeadOffset.Value : BarPolicy.DefaultHeadOffsetMetres);
            float clearance = KillIconPolicy.HeadAnchorOffset(barEnabled, barHead, barOffset, gap);
            if (EnemyFocus.TryGetHeadWorld(agent, clearance, out Vector3 head))
                return KillIconHud.Add(agent, head, presentation, clearance, now, KillIconPolicy.AnchorSourceHead);
            Vector3 fallback = lethalPosition + Vector3.up * clearance;
            if (!KillPolicy.IsFinite(fallback.x) || !KillPolicy.IsFinite(fallback.y) || !KillPolicy.IsFinite(fallback.z)) return 0;
            return KillIconHud.Add(agent, fallback, presentation, clearance, now, KillIconPolicy.AnchorSourceLethal);
        }
        catch (Exception error) { Note("kill-icon snapshot skipped: " + error.GetType().Name); return 0; }
    }
    internal static void ObserveState(eGameStateName state)
    {
        if (state == eGameStateName.InLevel) { CorpseFeedback.EnterLevel(); TeammateMarks.EnterLevel(); KillIconOccluders.EnterLevel(); return; }
        Reset("state:" + state);
    }
    internal static void LogPatchReceipts(Harmony harmony, ManualLogSource log)
    {
        foreach (MethodBase method in harmony.GetPatchedMethods())
        {
            var info = Harmony.GetPatchInfo(method);
            log.LogInfo($"[DIAG] patch {method.DeclaringType.Name}.{method.Name}: pre={info.Prefixes.Count},post={info.Postfixes.Count},final={info.Finalizers.Count}");
        }
    }
    private static int ClampCards(int value) => Math.Max(1, Math.Min(6, value));
    private static (bool Local, ulong Lookup) ResolveSource(Agent source)
    {
        try { var owner = source?.TryCast<PlayerAgent>()?.Owner; return owner != null && owner.IsLocal ? (true, owner.Lookup) : (false, 0); }
        catch { return (false, 0); }
    }
    // Flight identity: the game's own behavior block, never a guess from the enemy name or id. The agent
    // exposes it directly; the data-block lookup is the fallback for an agent whose reference is not
    // populated yet. Unreadable data means "not a flyer" and the log line only fires once per reason.
    private static bool IsFlyer(EnemyAgent agent)
    {
        if (agent == null) return false;
        try
        {
            EnemyBehaviorDataBlock behavior = agent.EnemyBehaviorData;
            if (behavior == null)
            {
                EnemyDataBlock data = agent.EnemyData;
                uint behaviorId = data != null ? data.BehaviorDataId : 0;
                if (behaviorId != 0) behavior = GameDataBlockBase<EnemyBehaviorDataBlock>.GetBlock(behaviorId);
            }
            return behavior != null && behavior.IsFlyer;
        }
        catch (Exception error)
        {
            Note("flyer probe skipped: " + error.GetType().Name);
            return false;
        }
    }

    // Armor identity: the enemy's own limb layout (does any limb carry armor), NOT the limb that landed
    // the killing blow - a head shot on an armored flyer still reports armored flight.
    private static bool HasArmorLimb(Dam_EnemyDamageBase enemy)
    {
        if (enemy == null) return false;
        try
        {
            var limbs = enemy.DamageLimbs;
            if (limbs == null) return false;
            for (int i = 0; i < limbs.Length; i++)
            {
                var limb = limbs[i];
                if (limb != null && limb.m_type == eLimbDamageType.Armor) return true;
            }
        }
        catch (Exception error)
        {
            Note("armor probe skipped: " + error.GetType().Name);
        }
        return false;
    }

    private static string ResolveEnemyName(EnemyAgent agent)
    {
        try { var block = GameDataBlockBase<EnemyDataBlock>.GetBlock(agent.EnemyDataID); if (block != null && !string.IsNullOrWhiteSpace(block.name)) return block.name; } catch { }
        try { return agent.name; } catch { return "Unknown"; }
    }
    internal static DamageScope Guard(Func<DamageScope> capture)
    {
        var previous = _active;
        try { return capture(); }
        catch (Exception error) { _active = previous; Note("capture unavailable: " + error.Message); return null; }
    }
    internal static void GuardAction(Action action)
    {
        try { action(); } catch (Exception error) { Note("feedback skipped: " + error.Message); }
    }
    internal static Exception EndLimb(Dam_EnemyDamageLimb limb, DamageScope scope, Exception error)
    {
        Dam_EnemyDamageBase enemy = null;
        try { enemy = limb?.m_base; } catch { }
        return End(enemy, scope, error);
    }
    private static void Note(string message)
    {
        if (_diagnosticBudget-- > 0) { Plugin.Instance?.Log.LogInfo("[FEEDBACK] " + message); return; }
        // One receipt the first time the allowance runs out, so a quiet log can never be mistaken for a quiet fight.
        if (_diagnosticBudgetNoted) return;
        _diagnosticBudgetNoted = true;
        Plugin.Instance?.Log.LogInfo($"[FEEDBACK] suppressed;reason=budget;limit={FeedbackLines};note=one-line-per-level-then-silent");
    }
}

[HarmonyPatch(typeof(Dam_EnemyDamageLimb), "BulletDamage")]
internal static class LocalBulletPatch
{
    [HarmonyPrefix] private static void Prefix(Dam_EnemyDamageLimb __instance, float dam, Agent sourceAgent, Vector3 position, Vector3 direction, float precisionMulti, bool allowDirectionalBonus, uint gearCategoryId, out DamageScope __state)
        => __state = KillTracker.Guard(() => KillTracker.BeginLimb(__instance, dam, sourceAgent, position, direction, precisionMulti, allowDirectionalBonus, false, 1f, 1f, gearCategoryId));
    [HarmonyFinalizer] private static Exception Finalizer(Dam_EnemyDamageLimb __instance, DamageScope __state, Exception __exception) => KillTracker.EndLimb(__instance, __state, __exception);
}
[HarmonyPatch(typeof(Dam_EnemyDamageLimb), "MeleeDamage")]
internal static class LocalMeleePatch
{
    [HarmonyPrefix] private static void Prefix(Dam_EnemyDamageLimb __instance, float dam, Agent sourceAgent, Vector3 position, Vector3 direction, float precisionMulti, float backstabberMulti, float sleeperMulti, uint gearCategoryId, out DamageScope __state)
        => __state = KillTracker.Guard(() => KillTracker.BeginLimb(__instance, dam, sourceAgent, position, direction, precisionMulti, true, true, backstabberMulti, sleeperMulti, gearCategoryId));
    [HarmonyFinalizer] private static Exception Finalizer(Dam_EnemyDamageLimb __instance, DamageScope __state, Exception __exception) => KillTracker.EndLimb(__instance, __state, __exception);
}
[HarmonyPatch(typeof(Dam_EnemyDamageBase), "ReceiveBulletDamage")]
internal static class EnemyBulletDamagePatch
{
    [HarmonyPrefix] private static void Prefix(Dam_EnemyDamageBase __instance, pBulletDamageData data, out DamageScope __state) => __state = KillTracker.Guard(() => KillTracker.BeginBullet(__instance, data));
    [HarmonyFinalizer] private static Exception Finalizer(Dam_EnemyDamageBase __instance, DamageScope __state, Exception __exception) => KillTracker.End(__instance, __state, __exception);
}
[HarmonyPatch(typeof(Dam_EnemyDamageBase), "ProcessReceivedDamage")]
internal static class EnemyProcessedDamagePatch
{
    [HarmonyPrefix] private static void Prefix(Dam_EnemyDamageBase __instance, Agent damageSource, float damage, Vector3 position, Vector3 direction, int limbID, uint gearCategoryId, out DamageScope __state)
        => __state = KillTracker.Guard(() => KillTracker.BeginProcessedDamage(__instance, damageSource, damage, position, direction, limbID, gearCategoryId));
    [HarmonyFinalizer] private static Exception Finalizer(Dam_EnemyDamageBase __instance, DamageScope __state, Exception __exception) => KillTracker.End(__instance, __state, __exception);
}
// 1.2.19 damage origin, fire and push: read-only labels, no root of their own. Both entries end up in
// ProcessReceivedDamage (that is where the damage root is created), so the only thing these prefixes do is publish
// the call scope that tells that root "this came from fire / a push". Nothing is written back, no payload field is
// read, and no damage number or game state changes because of it - a fire DoT and a push keep drawing exactly what
// they drew before, they are simply never mistaken for a shotgun pellet any more.
[HarmonyPatch(typeof(Dam_EnemyDamageBase), "ReceiveFireDamage")]
internal static class EnemyFireDamagePatch
{
    [HarmonyPrefix] private static void Prefix(Dam_EnemyDamageBase __instance, out DamageOriginScope __state)
        => __state = KillTracker.BeginOriginScope(__instance, DamageOrigin.Fire);
    [HarmonyFinalizer] private static Exception Finalizer(DamageOriginScope __state, Exception __exception) => KillTracker.EndOriginScope(__state, __exception);
}
[HarmonyPatch(typeof(Dam_EnemyDamageBase), "ReceivePushDamage")]
internal static class EnemyPushDamagePatch
{
    [HarmonyPrefix] private static void Prefix(Dam_EnemyDamageBase __instance, out DamageOriginScope __state)
        => __state = KillTracker.BeginOriginScope(__instance, DamageOrigin.Push);
    [HarmonyFinalizer] private static Exception Finalizer(DamageOriginScope __state, Exception __exception) => KillTracker.EndOriginScope(__state, __exception);
}
[HarmonyPatch(typeof(Dam_EnemyDamageBase), "InstantDead")]
internal static class EnemyDamageDeathPatch
{
    [HarmonyPostfix] private static void Postfix(Dam_EnemyDamageBase __instance) => KillTracker.GuardAction(() => KillTracker.CompleteFromActive(__instance));
}
[HarmonyPatch(typeof(EnemyAgent), "OnDead")]
internal static class EnemyAgentDeathPatch
{
    [HarmonyPostfix] private static void Postfix(EnemyAgent __instance) => KillTracker.GuardAction(() => KillTracker.EnemyDied(__instance));
}
[HarmonyPatch(typeof(EnemyAgent), "Setup")]
internal static class EnemySpawnPatch
{
    [HarmonyPrefix] private static void Prefix(EnemyAgent __instance) => KillTracker.GuardAction(() => { CorpseFeedback.Restore(__instance, "enemy-setup"); KillIconOccluders.Register(__instance); KillTracker.Spawned(__instance.Damage); });
}
[HarmonyPatch(typeof(GameStateManager), "DoChangeState")]
internal static class KillFeedStatePatch
{
    [HarmonyPrefix] private static void Prefix(eGameStateName nextState) => KillTracker.GuardAction(() => KillTracker.ObserveState(nextState));
}

[HarmonyPatch(typeof(EnemyAgent), "OnDeSpawn")]
internal static class EnemyDespawnPatch
{
    [HarmonyPrefix] private static void Prefix(EnemyAgent __instance) => KillTracker.GuardAction(() => { KillIconOccluders.Unregister(__instance); CorpseFeedback.Restore(__instance, "enemy-despawn"); });
}
