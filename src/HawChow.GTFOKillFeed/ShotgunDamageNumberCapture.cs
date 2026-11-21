using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Agents;
using Enemies;
using HarmonyLib;
using UnityEngine;

namespace HawChow.GTFOKillFeed;

// 1.2.19 pellet volley. The 1.2.17 real-machine log (KillFeed 1.2.17, `pellet merge=True`) printed
//   `Fire captured: fired=1, rays=1` and then twelve pellet numbers (2.85-3.31) on twelve consecutive
// frames. One shotgun shot therefore settles its pellet damage ACROSS frames, while the 1.2.18 scope
// was released the moment Fire returned (`fireFrame == Time.frameCount`), so every late pellet fell out
// of the scope and drew its own number. A shot is a VOLLEY now:
//   * the Fire prefix opens it (start time, expected pellet count when readable, the two switches, the
//     gear category and the SHOOTER the weapon belongs to), and it deliberately SURVIVES the Fire return;
//   * later frames keep feeding it, but only while the window is open and only for a hit that is proven to be
//     this shot's: delivered inside the shot's own Fire call (the Survival retest showed the game hands the rays'
//     damage to the damage path while `Shotgun.Fire` is still on the stack), or carrying the pellet category this
//     volley already took, or an unknown category, or the volley's own category - never a known unrelated one, and
//     never another shooter's;
//   * another weapon's Fire settles the open volley at that instant (SettleForeignVolley -> IsForeignShot), so a
//     rifle/melee hit can never be buffered or hidden (fix A);
//   * DamageNumbers.Tick drains it exactly once - on VolleySilenceSeconds of silence, on MaxVolleySeconds, or
//     when a new Fire arrives (flush the old one first). The declared ShotgunBulletCount is DIAGNOSTIC ONLY:
//     ending on it split one shot whenever a pellet touched two limbs (F3 review);
//   * overlapping shots: the second Fire settles the first volley at that instant, so a pellet landing after it is
//     counted into the new shot. Totals stay correct - the split follows the fire instants (F4 review);
// Single-pellet weapons never open a volley, so their numbers stay exactly as immediate as before.
//
// 1.2.19 fixes from the Survival session (this file is now only the Unity adapter; every DECISION lives in
// ShotgunVolleyState, so the smoke suite drives the real thing):
//   D1 - there was a SECOND window here (the raw 0.5 s volley window) in front of the 0.30 s capture window the
//        state machine used. Between 0.30 s and 0.50 s a hit the state machine does NOT consider a pellet could
//        still be swallowed by ShowShotgunDamageNumbers=false: the damage number simply vanished. There is one
//        decision now (ShotgunVolleyState.Capture) and it asks IsPelletCaptureWindow, so the suppression switch can
//        only ever swallow what the volley itself would have taken;
//   D2 - the 50 ms silence end was frame-granular (see FeedbackPolicy.VolleyQuietTicks);
//   D4 - the learned gear-category set is cleared with the level (ShotgunCategoryRegistry.Reset);
//   D5 - the three silent degradations now have bounded receipts: a refused pellet, an empty drain, and the volley
//        line names the Fire ENTRY and an owner slot.
//   D6 (Survival shotgun retest) - `[DN] volley;..;category=6|16` and `[FEEDBACK] ..;hitCat=2` are TWO id spaces: both
//        shotguns reported the same hit category, so the old equality test (and its `hitCat == 0` wildcard) never
//        matched and every pellet drew its own number. What THIS file contributes is the two facts that can be proven:
//        the shooter the firing weapon belongs to, and whether the damage is being settled from inside the shot's own
//        Fire call (the thread-static scope stack below). The classification itself stays in the state machine.
internal static class ShotgunDamageNumberCapture
{
    [ThreadStatic] private static ShotgunDamageNumberScopeStack _scopeStack;
    [ThreadStatic] private static ShotgunDamageTargetScopeStack _targetStack;
    // One open volley per session. Main thread only, exactly like the 1.2.18 scope: every read around it
    // is a UnityEngine.Time value, and pellet capture already happens on the thread that settles damage.
    private static readonly ShotgunVolleyState Volley = new();
    // The production route (classifier -> visibility gate -> volley capture) is DamageNumberRouter.Decide, and it needs
    // the live volley + the current damage target; these two accessors are the only things it takes from this class
    // (review R3: the route itself is no longer duplicated by a test fixture).
    internal static ShotgunVolleyState VolleyState => Volley;
    internal static long CurrentTarget => _targetStack != null ? _targetStack.Current : 0L;
    // Why the last volley of this level disappeared (review R4). EndFire FORGETS a volley whose switches say it can never
    // take a pellet, so a hit after that Fire is `no-volley`, not `not-merging` - the receipt has to say which.
    private static string _lastSettle = "none";
    // 1.2.19 review R1: a per-LEVEL volley serial plus the time since this owner's previous Fire. They are SUPPORTING
    // evidence only for "one trigger pull reached two entry points" (same owner, ~0.1 s apart, different frames)
    // versus "the player fired twice": `weapon.Pointer` plus a frame index cannot prove a single trigger pull, so
    // neither field decides it alone - the call needs entry/owner/seq/sincePrev/t TOGETHER plus a real-machine log
    // (this is also why R1's "merge anything within 0.3 s" was rejected). Nothing is merged or split from them; they
    // exist only so the next log is readable, not self-deciding.
    private static int _volleySerial;
    private static readonly Dictionary<long, float> LastFireAt = new();
    // Bounded diagnostics: one `[DN] volley` line and one aggregated `[DN] merged` line per volley. R6 (2nd review):
    // they use SEPARATE allowances (8 lines each per LEVEL, restored by Reset), because one shared budget was spent by
    // the volley lines alone - the 9th shot of a level could never show `[DN] merged`, the very line a real-machine
    // log needs to prove the pellet merge.
    private static readonly DiagnosticBudget VolleyLog = new();
    private static readonly DiagnosticBudget MergedLog = new();
    private const int VolleyLogLines = 8;
    private const int MergedLogLines = 8;
    // 1.2.19 D5: the two receipts the Survival log was missing. A volley that is drained without a single pellet and
    // a pellet that is refused are the ONLY two ways "merged=0" can happen, and both used to be silent. Each owns a
    // small per-level allowance and the refusal is additionally deduplicated by signature, so six distinct causes can
    // print across a whole level and a per-pellet flood is impossible.
    private static readonly DiagnosticBudget DropLog = new();
    private static readonly DiagnosticBudget DrainLog = new();
    private static readonly HashSet<string> DropSignatures = new();
    private const int DropLogLines = 6;
    private const int DrainLogLines = 4;
    // Owner handles are native pointers. A log line must not need them, so each weapon instance gets a small stable
    // ordinal for the level (`owner=#3`); the comparison itself still uses the pointer.
    private static readonly Dictionary<long, int> OwnerSlots = new();
    private const int MaxOwnerSlots = 16;
    private const int MaxTrackedOwners = 16;
    // How many per-enemy totals the aggregated merged line names before it says `+N`.
    private const int MergedDetailEnemies = 4;

    private static uint ReadCategoryId(Gear.BulletWeapon weapon)
    {
        try
        {
            var category = weapon != null ? weapon.GearCategoryData : null;
            return category != null ? category.persistentID : 0u;
        }
        catch { return 0u; }
    }

    // The local player this weapon belongs to, as the damage path will report it (KillTracker.ResolveSource returns
    // `owner.Lookup` for the local player and 0 for everybody else). Interop-verified path:
    //   Gear.BulletWeapon : ItemEquippable : Item -> Item.Owner (PlayerAgent) -> PlayerAgent.Owner (Player) -> .IsLocal / .Lookup
    // 0 means "not provable" and never refuses a pellet - the shooter gate only fires when BOTH sides name a player.
    internal static ulong ReadShooterLookup(Gear.BulletWeapon weapon)
    {
        try
        {
            var agent = weapon != null ? weapon.Owner : null;
            var owner = agent != null ? agent.Owner : null;
            return owner != null && owner.IsLocal ? owner.Lookup : 0UL;
        }
        catch { return 0UL; }
    }

    // The same-shot proof, read from this thread's Fire scope stack: is the damage being settled while the Fire call
    // that opened the still-open volley is on the stack? The state machine decides (ShotgunVolleyState.IsBodyHit):
    // this accessor only names the current Fire scope of this thread. A damage event on another thread has no stack
    // here, so it is simply not nested - it falls back to the category/shooter proof instead of guessing.
    internal static bool IsNestedPelletHit()
        => Volley.IsBodyHit(_scopeStack != null ? _scopeStack.Current : null);

    private static void RegisterCategory(uint gearCategoryId) => ShotgunCategoryRegistry.Register(gearCategoryId);

    internal static bool IsShotgunCategory(uint gearCategoryId) => ShotgunCategoryRegistry.IsKnown(gearCategoryId);

    // Pellets per shot, read from the weapon's own data:
    //   Gear.BulletWeapon.m_archeType -> Gear.BulletWeaponArchetype.m_archetypeData
    //     -> GameData.ArchetypeDataBlock.ShotgunBulletCount
    // All three members were verified read-only in the Test profile interop before they were used here.
    // 0 means unknown (null archetype, thrown accessor, non-positive value) and is not an error: the
    // silence and timeout end conditions still close an unknown volley.
    internal static int ReadShotgunBulletCount(Gear.BulletWeapon weapon)
    {
        if (weapon == null) return 0;
        try
        {
            var archetype = weapon.m_archeType;
            if (archetype == null) return 0;
            var data = archetype.m_archetypeData;
            if (data == null) return 0;
            int count = data.ShotgunBulletCount;
            return count > 0 ? count : 0;
        }
        catch { return 0; }
    }

    private static long OwnerHandle(Gear.BulletWeapon weapon)
    {
        if (weapon == null) return 0L;
        try { return weapon.Pointer.ToInt64(); }
        catch { return 0L; }
    }

    internal static bool IsActiveFireCategory(uint gearCategoryId, DamageOrigin origin, ulong shooterLookup = 0UL,
        bool nestedSameShot = false)
    {
        // F1 (1.2.19 review) + A (1.2.19 real-machine regression) + 1.2.19 Survival retest: the window has to be open
        // AND the hit must not prove it comes from another weapon / another shooter (ShotgunVolleyState.IsWindowOpen ->
        // FeedbackPolicy.IsVolleyHit, the SAME predicate the capture uses). This adapter adds exactly the two facts only
        // it can read: who fired and whether the damage is being settled inside that shot's own Fire call. Because both
        // the classifier and the capture ask that one predicate with the same arguments, the classifier can no longer
        // be stricter than the volley - the failure mode that made every pellet of the Survival session immediate.
        return Volley.IsWindowOpen(Time.unscaledTime, gearCategoryId, origin, shooterLookup, nestedSameShot);
    }

    // A (1.2.19 real-machine regression): the category wildcard that makes a real client's pellets merge again also
    // removed the only guard against a hit from another weapon. The identity gate restores it: when the open volley
    // belongs to a DIFFERENT weapon instance than the one firing now, the volley is settled immediately, so the
    // rifle/melee hits that follow can never be captured (or hidden by the shotgun visibility switch).
    // 1.2.19 review R3: that settle now happens inside ShotgunVolleyState.BeginShot (settle -> same-shot -> settle ->
    // open), so this class no longer carries a second copy of it.

    internal static ShotgunDamageNumberScope BeginFire(Gear.BulletWeapon weapon, VolleyEntry entry, bool requireMultiPellet = false)
    {
        try
        {
            uint gearCategoryId = ReadCategoryId(weapon);
            RegisterCategory(gearCategoryId);
            // D6: who fired. Read from the weapon's own owner chain, so the volley can refuse a damage event that
            // provably belongs to another player (0 = not readable, which proves nothing and refuses nothing).
            ulong shooterLookup = ReadShooterLookup(weapon);
            int frame = Time.frameCount;
            float now = Time.unscaledTime;
            long owner = OwnerHandle(weapon);
            // 1.2.19 review R3: the ORDER lives in ShotgunVolleyState.BeginShot (attributability gate -> settle a foreign
            // volley -> same-shot check -> settle the previous volley -> push the fire scope -> adopt); this adapter only
            // reads the live facts and draws whatever got settled. 1.2.19 review R6: a Fire that cannot be attributed to a
            // weapon instance opens NOTHING (an ownerless volley blinds both identity predicates) and every hit keeps its
            // own immediate number.
            bool foreign = Volley.IsForeignShot(owner);
            // Only a weapon whose own archetype data declares more than one pellet opens a volley (BeginMultiPelletFire for
            // Gear.BulletWeapon); Gear.Shotgun.Fire always opens one. The gate itself is decided in this adapter, the order
            // is not.
            bool isMultiPellet = !requireMultiPellet || ReadShotgunBulletCount(weapon) > 1;
            _scopeStack ??= new ShotgunDamageNumberScopeStack();
            ShotOpenResult result = Volley.BeginShot(owner, frame, now, gearCategoryId, ReadShotgunBulletCount(weapon),
                Plugin.ShowShotgunDamageNumbers?.Value ?? true,
                Plugin.MergeShotgunPellets?.Value ?? true,
                entry, isMultiPellet, shooterLookup, _scopeStack,
                out ShotgunDamageNumberScope opened, out ShotgunDamageNumberScope settled, out int settledFrames);
            DrawSettled(settled, settledFrames, frame, foreign ? "foreign-fire" : "drained");
            if (result == ShotOpenResult.Unattributable)
            {
                _lastSettle = "owner-unavailable";
                LogOwnerUnavailable(entry);
                return null;
            }
            // The same instance inside the same frame is ONE shot seen through two entry points
            // (Gear.Shotgun.Fire and Gear.BulletWeapon.Fire both carry a prefix). It must neither flush the volley that
            // shot just opened nor start a second one. 1.2.19 review R1: two entries on DIFFERENT frames cannot be told
            // apart from two fast trigger pulls by any game fact we can read (only weapon.Pointer + frame exist), so no
            // merge is invented across them: the second Fire settles the first volley exactly once and opens its own.
            if (result != ShotOpenResult.Opened) return null;
            LogVolley(opened, weapon, owner, now);
            return opened;
        }
        catch { return null; }
    }

    // Non-shotgun multi-pellet weapons: only a weapon whose own archetype data declares more than one
    // pellet per shot opens a volley. A rifle, a sniper or a single-shot EWC weapon never enters here,
    // so its numbers stay exactly as immediate as they are today.
    internal static ShotgunDamageNumberScope BeginMultiPelletFire(Gear.BulletWeapon weapon, VolleyEntry entry)
    {
        // A (1.2.19 real-machine regression): the identity gate runs for EVERY BulletWeapon Fire - including the
        // single-pellet rifle that never opens a volley. That Fire is exactly the "another weapon" the open shotgun
        // window must not survive, otherwise its hits (unknown category 0) would still be taken as pellets.
        // 1.2.19 review R6: an unnameable instance fails closed here too - it can never be proven to be the weapon that
        // opened the volley, so that volley is settled and nothing new is opened.
        // 1.2.19 review R3: the whole order (attributability -> foreign settle -> same-shot -> settle -> maybe open) is
        // ShotgunVolleyState.BeginShot, shared with Gear.BulletWeapon.Fire; only the >1 pellet gate is decided here.
        return BeginFire(weapon, entry, true);
    }

    internal static long EnterTarget(long target)
    {
        _targetStack ??= new ShotgunDamageTargetScopeStack();
        return _targetStack.Enter(target);
    }

    internal static Exception ExitTarget(long previous, Exception error)
    {
        _targetStack?.Exit(previous);
        return error;
    }

    // The receipt for a refused pellet. DamageNumberRouter.Decide owns the decision; this only formats and bounds it.
    internal static void ReportRefusedPellet(PelletCaptureDecision decision, int frame) => LogPelletDrop(decision, frame);


    internal static Exception EndFire(ShotgunDamageNumberScope scope, Exception error)
    {
        if (scope == null || _scopeStack == null || !_scopeStack.TryEnd(scope)) return error;
        // The volley stays open on purpose: that is the fix. The only work left here is to drop a volley
        // that provably can never take a pellet (pellets are shown individually or the weapon's numbers
        // are hidden), so no dead batch is kept for the rest of the window.
        if (!scope.ShowNumbers || !FeedbackPolicy.ShouldMergeShotgunDamageNumbers(true, scope.MergePellets))
        {
            // Review R4: remember WHY the volley is gone, so a hit that arrives after this Fire reports `no-volley;last=...`
            // instead of pretending the switches were never consulted.
            _lastSettle = scope.ShowNumbers ? "forgotten:merge" : "forgotten:show";
            Volley.Forget(scope);
        }
        return error;
    }

    // Per-frame end check, driven by the existing main-thread DamageNumbers.Tick. No coroutine and no
    // extra scan: one scope read, one policy call (the policy counts the consecutive quiet checks itself).
    internal static void Tick(float now)
    {
        if (!Volley.ShouldEnd(now)) return;
        DrainVolley(Time.frameCount);
    }

    // The ONE drain in the whole plugin: the volley is handed over once, one merged number is drawn per enemy at
    // the mean impact point, and the batch can never leak into the next shot. `reason` is the diagnostic fact a later
    // `no-volley` receipt repeats (review R4).
    private static void DrainVolley(int frame, string reason = "drained")
        => DrawSettled(Volley.End(frame, out int frames), frames, frame, reason);

    private static void DrawSettled(ShotgunDamageNumberScope scope, int frames, int frame, string reason)
    {
        if (scope == null) return;
        // Review R4: a settled volley is gone. Remembering WHY is what lets a later `no-volley` receipt say whether the
        // volley had already been drained, settled by another weapon, or forgotten by a switch, instead of reading as
        // "this weapon never had a volley".
        _lastSettle = reason;
        if (!FeedbackPolicy.ShouldDrawDrainedVolley(scope.ShowNumbers, scope.PelletHits))
        {
            // 1.2.19 D5: an empty drain used to be completely silent, which is exactly the shape the Survival log
            // had (`[DN] volley` lines, zero `[DN] merged`) - "the volley opened and took nothing" could not be told
            // apart from "the log was quiet". Bounded per level.
            LogEmptyDrain(scope, frames);
            return;
        }
        try
        {
            MergedShotgunDamageNumber[] merged = scope.Batch.Drain();
            for (int i = 0; i < merged.Length; i++)
                DamageNumbers.AddMergedShotgun(new Vector3(merged[i].X, merged[i].Y, merged[i].Z), merged[i].Damage, merged[i].Kind, merged[i].TriggerDamage);
            LogMerged(scope, merged, frames);
        }
        catch { }
    }

    // Level change, pause exit and plugin unload: a volley must never survive a level boundary, nothing is drawn
    // for a level that is already gone, and the diagnostic allowance starts over (F6: a per-session budget was
    // spent by the first few volleys of the first level and never came back).
    internal static void Reset()
    {
        Volley.Clear();
        _scopeStack = null;
        _targetStack = null;
        VolleyLog.Reset();
        MergedLog.Reset();
        DropLog.Reset();
        DrainLog.Reset();
        DropSignatures.Clear();
        OwnerSlots.Clear();
        LastFireAt.Clear();
        _lastSettle = "none";
        _volleySerial = 0;
        // D4: a gear category learned from one weapon must not stay "shotgun" for the rest of the process.
        ShotgunCategoryRegistry.Reset();
    }

    // Review R6: one bounded line when a Fire cannot be attributed to a weapon instance. Nothing is merged for that
    // trigger (every pellet keeps its own number), so this is a diagnostic, never a silent behaviour change.
    private static void LogOwnerUnavailable(VolleyEntry entry)
    {
        if (!DropLog.TryUse(DropLogLines)) return;
        string reason = "owner-unavailable";
        // The same signature de-dupe as every other receipt line: one line per level is enough to see it happen.
        if (!DropSignatures.Add(reason + ":" + entry)) return;
        Plugin.Instance?.Log.LogInfo($"[DN] volley=skip;reason={reason};entry={entry};note=weapon-pointer-unreadable-immediate-numbers-only");
    }

    private static int OwnerSlot(long handle)
    {
        if (handle == 0L) return 0;
        if (OwnerSlots.TryGetValue(handle, out int slot)) return slot;
        if (OwnerSlots.Count >= MaxOwnerSlots) return 0;
        slot = OwnerSlots.Count + 1;
        OwnerSlots.Add(handle, slot);
        return slot;
    }

    private static string OwnerText(long handle)
    {
        int slot = OwnerSlot(handle);
        return slot > 0 ? "#" + slot.ToString(CultureInfo.InvariantCulture) : "unknown";
    }

    private static void LogVolley(ShotgunDamageNumberScope scope, Gear.BulletWeapon weapon, long owner, float now)
    {
        if (!VolleyLog.TryUse(VolleyLogLines)) return;
        string weaponName;
        try { weaponName = weapon != null ? weapon.GetType().Name : "null"; }
        catch { weaponName = "unknown"; }
        string bullets = scope.ExpectedPellets > 0
            ? scope.ExpectedPellets.ToString(CultureInfo.InvariantCulture)
            : "unknown";
        // `entry=`, `owner=#n`, `seq=` and `sincePrev=` are SUPPORTING evidence for ONE trigger pull reaching two entry
        // points (the Survival log printed 11820/11825, 11935/11941, 12040/12050 for the same weapon) versus two
        // trigger pulls - none of them is sufficient alone: a frame index and a weapon pointer cannot prove a single
        // trigger pull, so the verdict needs the whole line plus the real log. Nothing is merged from them.
        LastFireAt.TryGetValue(owner, out float previous);
        string sincePrev = previous > 0f && now >= previous
            ? (now - previous).ToString("0.000", CultureInfo.InvariantCulture)
            : "-";
        if (LastFireAt.Count < MaxTrackedOwners || LastFireAt.ContainsKey(owner)) LastFireAt[owner] = now;
        Plugin.Instance?.Log.LogInfo($"[DN] volley;frame={scope.FireFrame};weapon={weaponName};bullets={bullets};category={scope.GearCategoryId};entry={scope.Entry};owner={OwnerText(scope.OwnerHandle)};seq={++_volleySerial};sincePrev={sincePrev};t={scope.StartTime.ToString("0.000", CultureInfo.InvariantCulture)}");
    }

    // F6 (1.2.19 review): ONE aggregated line per volley, so the next real-machine log always shows both
    // `[DN] volley;` and `[DN] merged;...;frames=` for the same shot instead of the budget dying inside the first
    // multi-enemy volley. F3: `expected=` is printed here because it is now diagnostic only.
    private static void LogMerged(ShotgunDamageNumberScope scope, MergedShotgunDamageNumber[] merged, int frames)
    {
        if (merged.Length == 0) return;
        if (!MergedLog.TryUse(MergedLogLines)) return;
        double total = 0;
        var each = new StringBuilder();
        for (int i = 0; i < merged.Length; i++)
        {
            total += merged[i].Damage;
            if (i >= MergedDetailEnemies) continue;
            if (i > 0) each.Append(',');
            each.Append(merged[i].EnemyIdentity.ToString(CultureInfo.InvariantCulture))
                .Append(':').Append(merged[i].Damage.ToString("0.##", CultureInfo.InvariantCulture));
        }
        if (merged.Length > MergedDetailEnemies)
            each.Append(",+").Append((merged.Length - MergedDetailEnemies).ToString(CultureInfo.InvariantCulture));
        string expected = scope.ExpectedPellets > 0
            ? scope.ExpectedPellets.ToString(CultureInfo.InvariantCulture)
            : "unknown";
        // D6: `learned=` is the pellet category this volley proved for itself (0 = none), `nested=` how many of the
        // pellets were delivered inside the shot's own Fire call - the two facts that say WHY the merge happened.
        Plugin.Instance?.Log.LogInfo($"[DN] merged;enemies={merged.Length};pellets={scope.PelletHits};nested={scope.NestedHits};learned={scope.PelletCategoryId};expected={expected};frames={frames};total={total:0.##};each={each};entry={scope.Entry};owner={OwnerText(scope.OwnerHandle)}");
    }

    // One line per drained volley that took nothing, and per distinct reason a real pellet was refused. Both are
    // allowances, not streams: `[DN] pellet=drop` prints the FIRST occurrence of each distinct signature
    // (reason + volley category + hit category + origin + target presence), at most DropLogLines per level.
    private static void LogEmptyDrain(ShotgunDamageNumberScope scope, int frames)
    {
        if (!DrainLog.TryUse(DrainLogLines)) return;
        string reason = scope.ShowNumbers ? "no-pellets" : "hidden";
        Plugin.Instance?.Log.LogInfo($"[DN] drain;taken=0;reason={reason};frames={frames};fireFrame={scope.FireFrame};pellets={scope.PelletHits};nested={scope.NestedHits};learned={scope.PelletCategoryId};category={scope.GearCategoryId};entry={scope.Entry};owner={OwnerText(scope.OwnerHandle)}");
    }

    // One line per distinct refusal signature. `last=` names what happened to the volley this trigger opened (review
    // R4): `forgotten:merge`/`forgotten:show` is what a hit AFTER a Fire with the switch off really sees, so the line can
    // no longer read as if the switch had never been consulted.
    private static void LogPelletDrop(PelletCaptureDecision decision, int frame)
    {
        string signature = ((int)decision.Refusal).ToString(CultureInfo.InvariantCulture)
            + ":" + decision.VolleyCategory.ToString(CultureInfo.InvariantCulture)
            + ":" + decision.HitCategory.ToString(CultureInfo.InvariantCulture)
            + ":" + decision.LearnedPelletCategory.ToString(CultureInfo.InvariantCulture)
            + ":" + (decision.NestedSameShot ? "nested" : "async")
            + ":" + (int)decision.Origin
            + ":" + (decision.TargetKnown ? "target" : "no-target")
            + ":" + _lastSettle;
        if (DropSignatures.Count >= DropLogLines && !DropSignatures.Contains(signature)) return;
        if (!DropSignatures.Add(signature)) return;
        if (!DropLog.TryUse(DropLogLines)) return;
        Plugin.Instance?.Log.LogInfo(PelletCaptureReceipt.Explain(decision, frame, _lastSettle));
    }
}

[HarmonyPatch(typeof(Gear.Shotgun), "Fire", new Type[] { typeof(bool) })]
internal static class ShotgunFireDamageNumberPatch
{
    [HarmonyPrefix]
    private static void Prefix(Gear.Shotgun __instance, out ShotgunDamageNumberScope __state)
        => __state = ShotgunDamageNumberCapture.BeginFire(__instance, VolleyEntry.Shotgun);

    [HarmonyFinalizer]
    private static Exception Finalizer(ShotgunDamageNumberScope __state, Exception __exception)
        => ShotgunDamageNumberCapture.EndFire(__state, __exception);
}

[HarmonyPatch(typeof(Gear.ShotgunSynced), "Fire", new Type[] { typeof(bool) })]
internal static class ShotgunSyncedFireDamageNumberPatch
{
    [HarmonyPrefix]
    private static void Prefix(Gear.ShotgunSynced __instance, out ShotgunDamageNumberScope __state)
        => __state = ShotgunDamageNumberCapture.BeginFire(__instance, VolleyEntry.ShotgunSynced);

    [HarmonyFinalizer]
    private static Exception Finalizer(ShotgunDamageNumberScope __state, Exception __exception)
        => ShotgunDamageNumberCapture.EndFire(__state, __exception);
}

// 1.2.19: multi-pellet weapons that are NOT Gear.Shotgun (a custom/modded weapon whose archetype data
// declares more than one pellet per shot). ReadShotgunBulletCount() > 1 is the only entry condition, so
// every single-pellet weapon returns null here and is untouched.
[HarmonyPatch(typeof(Gear.BulletWeapon), "Fire", new Type[] { typeof(bool) })]
internal static class BulletWeaponFireDamageNumberPatch
{
    [HarmonyPrefix]
    private static void Prefix(Gear.BulletWeapon __instance, out ShotgunDamageNumberScope __state)
        => __state = ShotgunDamageNumberCapture.BeginMultiPelletFire(__instance, VolleyEntry.BulletWeapon);

    [HarmonyFinalizer]
    private static Exception Finalizer(ShotgunDamageNumberScope __state, Exception __exception)
        => ShotgunDamageNumberCapture.EndFire(__state, __exception);
}

[HarmonyPatch(typeof(Gear.BulletWeaponSynced), "Fire", new Type[] { typeof(bool) })]
internal static class BulletWeaponSyncedFireDamageNumberPatch
{
    [HarmonyPrefix]
    private static void Prefix(Gear.BulletWeaponSynced __instance, out ShotgunDamageNumberScope __state)
        => __state = ShotgunDamageNumberCapture.BeginMultiPelletFire(__instance, VolleyEntry.BulletWeaponSynced);

    [HarmonyFinalizer]
    private static Exception Finalizer(ShotgunDamageNumberScope __state, Exception __exception)
        => ShotgunDamageNumberCapture.EndFire(__state, __exception);
}

[HarmonyPatch(typeof(KillTracker), "Complete", new Type[] { typeof(Dam_EnemyDamageBase), typeof(DamageScope) })]
internal static class DamageNumberTargetScopePatch
{
    [HarmonyPrefix]
    private static void Prefix(Dam_EnemyDamageBase __0, out long __state)
        => __state = ShotgunDamageNumberCapture.EnterTarget(__0 != null ? __0.Pointer.ToInt64() : 0L);

    [HarmonyFinalizer]
    private static Exception Finalizer(long __state, Exception __exception)
        => ShotgunDamageNumberCapture.ExitTarget(__state, __exception);
}
