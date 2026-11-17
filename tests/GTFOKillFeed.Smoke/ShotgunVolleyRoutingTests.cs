using System;
using System.Collections.Generic;
using HawChow.GTFOKillFeed;

// 1.2.19 fix regressions for the pellet volley (D1 / D2 / D4 / D5) - review R3: this fixture no longer copies the
// production decisions. It drives the SAME code the plugin runs:
//   * ShotgunVolleyState.BeginShot   - the whole Fire order (attributability -> foreign -> same-shot -> settle -> open),
//   * DamageNumberRouter.Decide      - the classifier -> visibility gate -> volley capture route (called by
//                                      DamageNumbers.Add in the plugin),
//   * FeedbackPolicy / ShotgunDamageNumberBatch / ShotgunCategoryRegistry / PelletCaptureReceipt.
// Only two things stay here because they need UnityEngine: the live reads (Time.frameCount / Time.unscaledTime and the
// current damage target) are passed in as arguments, and Draw(...) repeats the three-line "draw what End() handed over"
// step of ShotgunDamageNumberCapture.DrawSettled. The Unity adapter itself (Gear/Harmony/Time) can only be checked by
// the compiled-IL guard - see the report's "not covered" section.
internal static class ShotgunVolleyRoutingTests
{
    private const int Frame = 1000;

    private sealed class Pipeline
    {
        internal readonly ShotgunVolleyState State = new();
        internal readonly ShotgunDamageNumberScopeStack Stack = new();
        internal readonly List<MergedShotgunDamageNumber> Drawn = new();
        internal int Opened, EmptyDrains, Immediate, Merged, Suppressed, Dropped;
        internal string LastSettle = "none";
        internal bool Show = true;

        // ShotgunDamageNumberCapture.BeginFire: reads the live facts, asks BeginShot, draws what got settled.
        // D6: `shooterLookup` is what the plugin reads from the weapon's owner chain (0 = unreadable).
        internal ShotOpenResult Fire(float now, int frame, long owner, int expected, uint category = 6u,
            VolleyEntry entry = VolleyEntry.Shotgun, bool show = true, bool merge = true, ulong shooterLookup = 0UL)
        {
            // Production RegisterCategory in BeginFire: the weapon's own category becomes a known shotgun category, which is
            // what makes a later hit of that category a "shotgun number" even after the capture window closed.
            ShotgunCategoryRegistry.Register(category);
            // Gear.Shotgun.Fire always opens a volley; Gear.BulletWeapon.Fire only when the archetype declares > 1 pellet.
            bool opensVolley = entry == VolleyEntry.Shotgun || entry == VolleyEntry.ShotgunSynced || expected > 1;
            bool foreign = State.IsForeignShot(owner);
            ShotOpenResult result = State.BeginShot(owner, frame, now, category, expected, show, merge, entry, opensVolley,
                shooterLookup, Stack, out ShotgunDamageNumberScope opened, out ShotgunDamageNumberScope settled,
                out int settledFrames);
            Draw(settled, settledFrames, frame, foreign ? "foreign-fire" : "drained");
            if (result == ShotOpenResult.Unattributable)
            {
                LastSettle = "owner-unavailable";
                return result;
            }
            if (result != ShotOpenResult.Opened) return result;
            Opened++;
            return result;
        }

        // ShotgunDamageNumberCapture.EndFire (the Harmony finalizer): it ALWAYS pops this thread's fire scope, and it
        // forgets a volley whose switches say it can never take a pellet. Both halves are modelled, because the
        // same-shot proof reads that stack: a hit that arrives after Fire returned is NOT nested.
        internal void EndFire(bool show, bool merge)
        {
            var scope = State.Scope;
            if (scope == null) return;
            Stack.TryEnd(scope);
            if (!show || !merge)
            {
                LastSettle = show ? "forgotten:merge" : "forgotten:show";
                State.Forget(scope);
            }
        }

        // DamageNumbers.Add -> DamageNumberRouter.Decide, with the live switch the plugin would read.
        // `inFireBody` says the damage is being settled from inside the shot's own Fire call, which is what
        // ShotgunDamageNumberCapture.IsNestedPelletHit() reports in production (it asks the thread's fire scope stack).
        internal DamageNumberDecision Hit(long target, float damage, float now, int frame, uint hitCategory = 6u,
            DamageOrigin origin = DamageOrigin.Bullet, bool preview = false, bool master = true,
            bool inFireBody = false, ulong shooterLookup = 0UL)
        {
            bool nestedSameShot = inFireBody && State.IsBodyHit(Stack.Current);
            DamageNumberDecision decision = DamageNumberRouter.Decide(master, Show, preview, origin, hitCategory,
                ShotgunCategoryRegistry.IsKnown(hitCategory),
                State.IsWindowOpen(now, hitCategory, origin, shooterLookup, nestedSameShot),
                shooterLookup, nestedSameShot,
                State, target, 0f, 1f, 0f, damage, HitKind.Normal, now, frame);
            switch (decision.Route)
            {
                case DamageNumberRoute.Immediate: Immediate++; break;
                case DamageNumberRoute.Merged: Merged++; break;
                case DamageNumberRoute.Suppressed: Suppressed++; break;
                default: Dropped++; break;
            }
            return decision;
        }

        internal void Tick(float now, int frame)
        {
            if (!State.ShouldEnd(now)) return;
            Draw(State.End(frame, out int frames), frames, frame, "drained");
        }

        // ShotgunDamageNumberCapture.DrawSettled (the only draw site).
        private void Draw(ShotgunDamageNumberScope scope, int frames, int frame, string reason)
        {
            if (scope == null) return;
            LastSettle = reason;
            if (!FeedbackPolicy.ShouldDrawDrainedVolley(scope.ShowNumbers, scope.PelletHits)) { EmptyDrains++; return; }
            var merged = scope.Batch.Drain();
            if (merged.Length == 0) { EmptyDrains++; return; }
            Drawn.AddRange(merged);
        }
    }

    internal static void Run(Action<bool, string> check)
    {
        ShotgunCategoryRegistry.Reset();

        // ---- 1) one shot, several frames, one number per enemy, through the production route ---------------
        var shot = new Pipeline();
        shot.Fire(10f, Frame, 0x1000, 6, 6u);
        var h1 = shot.Hit(11, 2f, 10.01f, Frame);
        var h2 = shot.Hit(11, 3f, 10.02f, Frame + 1);
        var h3 = shot.Hit(12, 4f, 10.03f, Frame + 2);
        shot.Tick(10.04f, Frame + 2);
        shot.Tick(10.10f, Frame + 5);
        shot.Tick(10.11f, Frame + 6);
        check(h1.Route == DamageNumberRoute.Merged && h2.Route == DamageNumberRoute.Merged
            && h3.Route == DamageNumberRoute.Merged && shot.Drawn.Count == 2
            && Math.Abs(shot.Drawn[0].Damage - 5f) < 0.001f && shot.Drawn[0].PelletHits == 2
            && Math.Abs(shot.Drawn[1].Damage - 4f) < 0.001f && shot.Drawn[1].PelletHits == 1,
            "R3: through DamageNumberRouter.Decide + ShotgunVolleyState.BeginShot, pellets that settle on later frames become one summed number per enemy");

        // ---- 2) D1 on the REAL branch: live switch on, volley opened while it was off ----------------------
        // The suppression branch is only reachable when the volley's own switch is off while the live switch is on (a
        // config hot reload inside the window). Review R3: the old fixture used show:false for BOTH switches and
        // therefore tested a branch production never takes.
        var hiddenScope = new Pipeline();
        hiddenScope.Fire(20f, Frame, 0x2000, 6, 6u, show: false);
        var insideWindow = hiddenScope.Hit(21, 2f, 20.10f, Frame + 6);
        var afterWindow = hiddenScope.Hit(21, 2f, 20.35f, Frame + 21);
        var afterLifetime = hiddenScope.Hit(21, 2f, 20.45f, Frame + 27);
        check(insideWindow.Route == DamageNumberRoute.Suppressed
            && afterWindow.Route == DamageNumberRoute.Immediate && afterWindow.Capture.Refusal == PelletRefusal.Window
            && afterLifetime.Route == DamageNumberRoute.Immediate,
            "D1: with the live switch ON and the volley opened while it was OFF, only a hit inside the 0.30 s capture window is suppressed; +0.35 s / +0.45 s are refused and still draw");
        // ... and with the live switch OFF the hit is dropped by the visibility gate before the volley is asked at all.
        var hiddenLive = new Pipeline();
        hiddenLive.Fire(21f, Frame, 0x2100, 6, 6u, show: false);
        hiddenLive.Show = false;
        var dropped = hiddenLive.Hit(22, 3f, 21.05f, Frame + 3);
        var droppedLate = hiddenLive.Hit(22, 3f, 21.35f, Frame + 21);
        check(dropped.Route == DamageNumberRoute.Dropped && droppedLate.Route == DamageNumberRoute.Dropped
            && hiddenLive.Suppressed == 0 && hiddenLive.Drawn.Count == 0,
            "D1: with the shotgun numbers switched OFF the visibility gate drops the pellet (never the volley), and nothing is drawn");
        // The shotgun switch must never hide ANOTHER weapon: a non-shotgun hit stays immediate with the switch off.
        var rifleWithHiddenShotgun = new Pipeline();
        rifleWithHiddenShotgun.Show = false;
        rifleWithHiddenShotgun.Fire(22f, Frame, 0x2200, 1, 0u, VolleyEntry.BulletWeapon);
        var hiddenRifleHit = rifleWithHiddenShotgun.Hit(23, 25f, 22.01f, Frame, 0u);
        check(hiddenRifleHit.Route == DamageNumberRoute.Immediate && hiddenRifleHit.IsShotgun == false,
            "D1: with the shotgun numbers OFF another weapon's hit is still drawn immediately (the switch owns one weapon, not the pipeline)");

        // ---- 3) D2: 50 ms of silence has to hold for two end checks ---------------------------------------
        var slow = new Pipeline();
        slow.Fire(30f, Frame, 0x3000, 12, 6u);
        bool slowAll = true;
        for (int i = 0; i < 5; i++)
        {
            slowAll &= slow.Hit(31, 1f, 30f + 0.06f * i, Frame + i).Route == DamageNumberRoute.Merged;
            slow.Tick(30f + 0.06f * i + 0.001f, Frame + i);
        }
        check(slowAll && slow.Drawn.Count == 0,
            "D2: at 20 fps a pellet stream that lands one pellet per frame stays ONE volley (no early drain inside the stream)");
        slow.Tick(30.30f, Frame + 5);
        check(slow.Drawn.Count == 0, "D2: the first quiet end check after the stream still does not draw");
        slow.Tick(30.36f, Frame + 6);
        check(slow.Drawn.Count == 1 && slow.Drawn[0].PelletHits == 5 && Math.Abs(slow.Drawn[0].Damage - 5f) < 0.001f,
            "D2: the five pellets close into exactly ONE merged number once the stream really stops");
        check(FeedbackPolicy.VolleyQuietTicks == 2
            && FeedbackPolicy.SilenceElapsed(1, 10f, 10.0625f) && !FeedbackPolicy.SilenceElapsed(0, 10f, 10.5f)
            && !FeedbackPolicy.SilenceElapsed(1, 10f, 10.03125f)
            && !FeedbackPolicy.ShouldDrainVolley(1, 10f, 10f, 10.0625f, false, 1)
            && FeedbackPolicy.ShouldDrainVolley(1, 10f, 10f, 10.0625f, false, 2)
            && FeedbackPolicy.ShouldDrainVolley(1, 10f, 10f, 10.5f, false, 0)
            && !FeedbackPolicy.ShouldDrainVolley(1, 10f, 10f, 10.5f, true, 9),
            "D2: the end decision needs TWO consecutive quiet checks, still honours the 0.5 s cap, and a drained volley never ends twice");

        // ---- 4) the refusals the Survival log could not distinguish ----------------------------------------
        // The production order is: classify (origin + known category OR open window) -> visibility -> volley. A hit that is
        // NOT classified as a shotgun hit never reaches the volley at all - that is the strongest form of "another
        // weapon's hit can neither be buffered nor hidden" - and the volley's own origin/category checks stay as a second,
        // unreachable-from-here line of defence.
        var foreign = new Pipeline();
        foreign.Fire(40f, Frame, 0x4000, 6, 6u);
        var own = foreign.Hit(41, 2f, 40.01f, Frame, 0u);
        var otherGun = foreign.Hit(41, 7f, 40.02f, Frame + 1, 45u);
        var melee = foreign.Hit(41, 7f, 40.03f, Frame + 1, 0u, DamageOrigin.Melee);
        var noTarget = foreign.Hit(0, 7f, 40.04f, Frame + 1, 0u);
        var lateOwn = foreign.Hit(41, 5f, 40.35f, Frame + 21, 6u);
        check(own.Route == DamageNumberRoute.Merged
            && otherGun.Route == DamageNumberRoute.Immediate && otherGun.IsShotgun == false
            && melee.Route == DamageNumberRoute.Immediate && melee.IsShotgun == false
            && noTarget.Capture.Refusal == PelletRefusal.Target && !noTarget.Capture.TargetKnown
            && lateOwn.Capture.Refusal == PelletRefusal.Window && lateOwn.Route == DamageNumberRoute.Immediate,
            "D5: a foreign known category and a non-bullet origin are never even classified as a shotgun hit, a missing enemy identity is refused as `target`, and a registered category hit after the window is refused as `window`");
        string receipt = PelletCaptureReceipt.Explain(lateOwn.Capture, Frame + 21, foreign.LastSettle);
        check(receipt.StartsWith("[DN] pellet=drop;reason=window;", StringComparison.Ordinal)
            && receipt.Contains(";age=0.350s") && receipt.Contains(";df=21") && receipt.Contains(";volleyCat=6")
            && receipt.Contains(";hitCat=6") && receipt.Contains(";origin=Bullet") && receipt.Contains(";target=yes")
            && receipt.Contains(";last="),
            "D5: the refusal receipt carries reason, age, frame delta, volley/hit category, origin, target presence and what already happened to the volley");

        // ---- 5) no volley at all (the Survival shape) -----------------------------------------------------
        var idle = new Pipeline();
        ShotgunCategoryRegistry.Register(30u);   // the weapon fired in an earlier level: its category is known
        var stray = idle.Hit(51, 9.5f, 50f, Frame, 30u);
        check(stray.Capture.Refusal == PelletRefusal.NoVolley && stray.Route == DamageNumberRoute.Immediate
            && PelletCaptureReceipt.Explain(stray.Capture, Frame).Contains("reason=no-volley"),
            "D5: a Bullet hit of a KNOWN shotgun category with no open volley is refused as `no-volley`");

        // ---- 6) the capture window is inclusive and unchanged --------------------------------------------
        var late = new Pipeline();
        late.Fire(60f, Frame, 0x6000, 6, 6u);
        var inside = late.Hit(61, 2f, 60.29f, Frame + 17);
        var outside = late.Hit(61, 70f, 60.31f, Frame + 18);
        late.Tick(60.40f, Frame + 24);
        late.Tick(60.46f, Frame + 27);
        check(inside.Route == DamageNumberRoute.Merged && outside.Route == DamageNumberRoute.Immediate
            && outside.Capture.Refusal == PelletRefusal.Window
            && late.Drawn.Count == 1 && Math.Abs(late.Drawn[0].Damage - 2f) < 0.001f,
            "the 0.30 s capture boundary is inclusive and unchanged: +0.29 s merges, +0.31 s keeps its own number");

        // ---- 7) switches, consecutive fire, weapon swap ---------------------------------------------------
        var noMerge = new Pipeline();
        noMerge.Fire(70f, Frame, 0x7000, 8, 6u, merge: false);
        var insideFire = noMerge.Hit(71, 3f, 70.001f, Frame);
        check(insideFire.Capture.Refusal == PelletRefusal.NotMerging && insideFire.Route == DamageNumberRoute.Immediate,
            "MergeShotgunPellets=false refuses a pellet that lands INSIDE the Fire call (the scope is still open) as not-merging");
        // Review R4: once the Fire returns, EndFire forgets that volley, so a later hit is `no-volley;last=forgotten:merge`.
        noMerge.EndFire(true, false);
        var afterFire = noMerge.Hit(71, 3f, 70.05f, Frame + 3);
        check(afterFire.Capture.Refusal == PelletRefusal.NoVolley && noMerge.LastSettle == "forgotten:merge"
            && PelletCaptureReceipt.Explain(afterFire.Capture, Frame + 3, noMerge.LastSettle).Contains(";last=forgotten:merge"),
            "R4: a hit AFTER a Fire with merging off reports no-volley with last=forgotten:merge (not not-merging), and the display path is untouched");
        var refire = new Pipeline();
        refire.Fire(80f, Frame, 0x8000, 6, 6u);
        refire.Hit(81, 4f, 80.01f, Frame);
        bool second = refire.Fire(80.20f, Frame + 12, 0x8000, 6, 6u) == ShotOpenResult.Opened;
        refire.Hit(81, 6f, 80.21f, Frame + 12);
        refire.Tick(80.30f, Frame + 18);
        refire.Tick(80.36f, Frame + 21);
        check(second && refire.Drawn.Count == 2
            && Math.Abs(refire.Drawn[0].Damage - 4f) < 0.001f && Math.Abs(refire.Drawn[1].Damage - 6f) < 0.001f,
            "two consecutive shots of the same weapon stay two numbers (the new Fire flushes the old volley first)");
        // Review R1: a FAST second pull (2 frames later) must never be folded into the first shot.
        var fastFire = new Pipeline();
        fastFire.Fire(85f, Frame, 0x8500, 6, 6u);
        fastFire.Hit(86, 2f, 85.01f, Frame);
        fastFire.Hit(86, 3f, 85.02f, Frame + 1);
        bool fastSecond = fastFire.Fire(85.034f, Frame + 2, 0x8500, 6, 6u) == ShotOpenResult.Opened;
        fastFire.Hit(86, 7f, 85.04f, Frame + 2);
        fastFire.Hit(86, 9f, 85.05f, Frame + 3);
        fastFire.Tick(85.20f, Frame + 8);
        fastFire.Tick(85.26f, Frame + 9);
        check(fastSecond && fastFire.Drawn.Count == 2
            && Math.Abs(fastFire.Drawn[0].Damage - 5f) < 0.001f && fastFire.Drawn[0].PelletHits == 2
            && Math.Abs(fastFire.Drawn[1].Damage - 16f) < 0.001f && fastFire.Drawn[1].PelletHits == 2,
            "R1: two trigger pulls two frames apart stay two numbers - no merge is invented across a Fire boundary");
        var swap = new Pipeline();
        swap.Fire(90f, Frame, 0x9000, 6, 6u);
        swap.Hit(91, 4f, 90.01f, Frame);
        bool rifleFired = swap.Fire(90.03f, Frame + 2, 0x9001, 1, 0u, VolleyEntry.BulletWeapon) == ShotOpenResult.SettledOnly;
        var rifleHit = swap.Hit(91, 25f, 90.04f, Frame + 3, 0u);
        check(rifleFired && rifleHit.Route == DamageNumberRoute.Immediate && rifleHit.IsShotgun == false
            && rifleHit.Capture.Refusal == PelletRefusal.None
            && swap.Drawn.Count == 1 && Math.Abs(swap.Drawn[0].Damage - 4f) < 0.001f,
            "a rifle firing settles the open volley (its pellets are one number) and the rifle's own hit is no longer captured");

        // ---- 8) two entry points on one frame are one shot ------------------------------------------------
        var doubleEntry = new Pipeline();
        doubleEntry.Fire(100f, Frame, 0xA000, 8, 6u, VolleyEntry.Shotgun);
        bool again = doubleEntry.Fire(100f, Frame, 0xA000, 8, 6u, VolleyEntry.BulletWeapon) == ShotOpenResult.SameShot;
        check(again && doubleEntry.Opened == 1,
            "one trigger pull reaching two Fire entry points on the SAME frame stays one volley");

        // ---- 9) R6: a Fire that cannot be attributed to a weapon instance opens NOTHING -------------------
        check(ShotgunVolleyState.CanOpenVolley(0L) == false && ShotgunVolleyState.CanOpenVolley(0x1234)
            && FeedbackPolicy.IsForeignVolleyOwner(0L, 0x1234) == false,
            "R6: an unreadable weapon pointer is not a weapon identity - and an ownerless volley could never be settled as foreign");
        var unattributable = new Pipeline();
        var zero = unattributable.Fire(110f, Frame, 0L, 6, 6u);
        var zeroHit = unattributable.Hit(111, 5f, 110.01f, Frame);
        check(zero == ShotOpenResult.Unattributable && unattributable.Opened == 0 && unattributable.State.Scope == null
            && zeroHit.Capture.Refusal == PelletRefusal.NoVolley && zeroHit.Route == DamageNumberRoute.Immediate,
            "R6: an unnameable weapon opens no volley, so every hit keeps its own immediate number instead of being buffered or swallowed");
        // ... and it also settles whatever volley was open (it cannot be proven to belong to this trigger).
        var settleOnUnattributable = new Pipeline();
        settleOnUnattributable.Fire(115f, Frame, 0xB000, 6, 6u);
        settleOnUnattributable.Hit(111, 3f, 115.01f, Frame);
        var unattributable2 = settleOnUnattributable.Fire(115.05f, Frame + 3, 0L, 6, 6u);
        check(unattributable2 == ShotOpenResult.Unattributable && settleOnUnattributable.Drawn.Count == 1
            && settleOnUnattributable.LastSettle == "owner-unavailable",
            "R6: an unnameable Fire settles the open volley first (one number for the pellets it really took) and then opens nothing");

        // ---- 10) the learned category set is per level (D4) ----------------------------------------------
        ShotgunCategoryRegistry.Reset();
        var unknownVolley = new Pipeline();
        unknownVolley.Fire(120f, Frame, 0xC000, 6, 0u);
        var knownHitInUnknownVolley = unknownVolley.Hit(121, 3f, 120.01f, Frame, 272u);
        bool unknownInsideUnknownVolley = unknownVolley.Hit(121, 3f, 120.02f, Frame, 0u).Route == DamageNumberRoute.Merged;
        check(knownHitInUnknownVolley.Route == DamageNumberRoute.Immediate && knownHitInUnknownVolley.IsShotgun == false
            && unknownInsideUnknownVolley
            && !ShotgunCategoryRegistry.IsKnown(272u),
            "D4: a KNOWN hit category is refused by a volley with no readable category (no silent backfill), while unknown/unknown still merges");
        ShotgunCategoryRegistry.Register(272u);
        bool learned = ShotgunCategoryRegistry.IsKnown(272u);
        ShotgunCategoryRegistry.Reset();
        check(learned && !ShotgunCategoryRegistry.IsKnown(272u),
            "D4: the learned shotgun-category set is cleared with the level");

        // ---- 11) invariants that must not drift ----------------------------------------------------------
        // ---- 12) D6 (Survival shotgun retest): the REAL production input (fire category=6, hitCat=2) --------------
        // Evidence this fixture encodes: the Survival log printed `[DN] volley;frame=25601;..;category=6` from the Fire
        // prefix, THREE `[FEEDBACK] number=8.4999;..;hitCat=2` lines at the same frame, and then CombatPanel's
        // `Fire captured: .. hit=3` for that same frame - i.e. the game hands the rays' damage to the damage path while
        // the shot's own Fire is still on the stack. Both shotguns (category 6 and 16) reported the SAME hitCat=2.
        var realShot = new Pipeline();
        realShot.Fire(300f, Frame, 0x1A000, 8, 6u, shooterLookup: 0xAAUL);
        var real1 = realShot.Hit(201, 8.4999f, 300.00f, Frame, 2u, inFireBody: true, shooterLookup: 0xAAUL);
        var real2 = realShot.Hit(201, 8.4999f, 300.00f, Frame, 2u, inFireBody: true, shooterLookup: 0xAAUL);
        var real3 = realShot.Hit(201, 8.4999f, 300.00f, Frame, 2u, inFireBody: true, shooterLookup: 0xAAUL);
        var otherTarget = realShot.Hit(202, 8.4999f, 300.00f, Frame, 2u, inFireBody: true, shooterLookup: 0xAAUL);
        realShot.EndFire(true, true);
        realShot.Tick(300.06f, Frame + 3);
        realShot.Tick(300.12f, Frame + 7);
        check(real1.Route == DamageNumberRoute.Merged && real2.Route == DamageNumberRoute.Merged
            && real3.Route == DamageNumberRoute.Merged && otherTarget.Route == DamageNumberRoute.Merged
            && real1.Capture.NestedSameShot && real1.Capture.Refusal == PelletRefusal.None
            && realShot.Drawn.Count == 2
            && realShot.Drawn[0].EnemyIdentity == 201 && realShot.Drawn[0].PelletHits == 3
            && Math.Abs(realShot.Drawn[0].Damage - 25.4997f) < 0.01f
            && realShot.Drawn[1].EnemyIdentity == 202 && realShot.Drawn[1].PelletHits == 1,
            "D6: the real (fire category=6, hitCat=2) shot merges each target into ONE number (3 pellets summed = 25.4997 for one of them) instead of four immediate numbers");

        // The learned pellet category carries the SAME shot across the Fire boundary, and it stays inside that volley.
        var asyncTail = new Pipeline();
        asyncTail.Fire(310f, Frame, 0x1B000, 8, 6u, shooterLookup: 0xAAUL);
        var nestedFirst = asyncTail.Hit(211, 2f, 310.00f, Frame, 2u, inFireBody: true, shooterLookup: 0xAAUL);
        asyncTail.EndFire(true, true);
        var asyncSecond = asyncTail.Hit(211, 3f, 310.09f, Frame + 5, 2u, shooterLookup: 0xAAUL);
        check(nestedFirst.Route == DamageNumberRoute.Merged && nestedFirst.Capture.LearnedPelletCategory == 2u
            && asyncSecond.Route == DamageNumberRoute.Merged && !asyncSecond.Capture.NestedSameShot
            && !ShotgunCategoryRegistry.IsKnown(2u),
            "D6: one nested hit teaches THIS volley its pellet category (2), so a later asynchronous hit of the same shot still merges - and 2 is never registered as a global shotgun category");
        asyncTail.Tick(310.20f, Frame + 9);
        asyncTail.Tick(310.26f, Frame + 12);
        check(asyncTail.Drawn.Count == 1 && asyncTail.Drawn[0].PelletHits == 2
            && Math.Abs(asyncTail.Drawn[0].Damage - 5f) < 0.001f,
            "D6: the nested pellet plus its asynchronous tail close into exactly ONE number with the exact total");

        // A shot that never delivered anything inside its own Fire call proves nothing - its pellets stay independent
        // and the classifier refusal gets a bounded receipt instead of silence.
        var asyncOnly = new Pipeline();
        asyncOnly.Fire(320f, Frame, 0x1C000, 8, 6u, shooterLookup: 0xAAUL);
        asyncOnly.EndFire(true, true);
        var asyncAlone = asyncOnly.Hit(221, 8.4999f, 320.05f, Frame + 3, 2u, shooterLookup: 0xAAUL);
        string asyncReceipt = PelletCaptureReceipt.Explain(asyncAlone.Capture, Frame + 3, asyncOnly.LastSettle);
        check(asyncAlone.Route == DamageNumberRoute.Immediate && asyncAlone.IsShotgun == false
            && asyncAlone.Capture.Refusal == PelletRefusal.NotClassified && asyncAlone.NeedsReport
            && asyncReceipt.StartsWith("[DN] pellet=drop;reason=not-classified;", StringComparison.Ordinal)
            && asyncReceipt.Contains(";hitCat=2") && asyncReceipt.Contains(";learned=0")
            && asyncReceipt.Contains(";nested=no") && asyncReceipt.Contains(";volleyCat=6"),
            "D6: an asynchronous hit that cannot be tied to the shot keeps its own number AND the classifier now leaves a `reason=not-classified` receipt saying why (the line the Survival log never had)");

        // The same shot with the shotgun numbers hidden swallows the pellets (and only the pellets).
        var hiddenReal = new Pipeline();
        hiddenReal.Fire(330f, Frame, 0x1D000, 8, 6u, show: false, shooterLookup: 0xAAUL);
        var hiddenNested = hiddenReal.Hit(231, 2f, 330.00f, Frame, 2u, inFireBody: true, shooterLookup: 0xAAUL);
        var hiddenMelee = hiddenReal.Hit(231, 9f, 330.00f, Frame, 0u, DamageOrigin.Melee, inFireBody: true, shooterLookup: 0xAAUL);
        check(hiddenNested.Route == DamageNumberRoute.Suppressed && hiddenNested.Capture.Refusal == PelletRefusal.Hidden
            && hiddenMelee.Route == DamageNumberRoute.Immediate && hiddenReal.Drawn.Count == 0,
            "D6: with ShowShotgunDamageNumbers=false the real (6,2) pellet is swallowed while a melee hit of the same frame still draws");

        // Fire(16) / hit(2): the short-barrel input. NOT log-proven as a shot (its FEEDBACK budget was exhausted in the
        // Survival session - the 2 there is inferred from the shared damage entry); asserted here as the representative
        // input so a second id space cannot silently special-case one weapon.
        var shortBarrel = new Pipeline();
        shortBarrel.Fire(340f, Frame, 0x1E000, 8, 16u, shooterLookup: 0xAAUL);
        var short1 = shortBarrel.Hit(241, 4f, 340.00f, Frame, 2u, inFireBody: true, shooterLookup: 0xAAUL);
        var short2 = shortBarrel.Hit(241, 5f, 340.00f, Frame, 2u, inFireBody: true, shooterLookup: 0xAAUL);
        shortBarrel.EndFire(true, true);
        shortBarrel.Tick(340.06f, Frame + 3);
        shortBarrel.Tick(340.12f, Frame + 7);
        check(short1.Route == DamageNumberRoute.Merged && short2.Route == DamageNumberRoute.Merged
            && shortBarrel.Drawn.Count == 1 && Math.Abs(shortBarrel.Drawn[0].Damage - 9f) < 0.001f,
            "D6 (representative, not log-proven for this weapon): fire category=16 with hitCat=2 merges the same way as 6");

        // Another shooter (a second local player / a bot whose damage this machine numbers) is NOT this shot.
        var otherShooter = new Pipeline();
        otherShooter.Fire(350f, Frame, 0x1F000, 8, 6u, shooterLookup: 0xAAUL);
        var foreignShooter = otherShooter.Hit(251, 6f, 350.00f, Frame, 2u, inFireBody: true, shooterLookup: 0xBBUL);
        var ownShooter = otherShooter.Hit(251, 7f, 350.00f, Frame, 2u, inFireBody: true, shooterLookup: 0xAAUL);
        check(foreignShooter.Route == DamageNumberRoute.Immediate
            && foreignShooter.Capture.Refusal == PelletRefusal.Shooter
            && foreignShooter.Capture.Shooter == PelletShooter.Foreign && foreignShooter.NeedsReport
            && ownShooter.Route == DamageNumberRoute.Merged,
            "D6: a hit that names a DIFFERENT known shooter is refused as `shooter` (own number + receipt) while the real shooter's pellet still merges");
        var unknownShooter = new Pipeline();
        unknownShooter.Fire(355f, Frame, 0x1F800, 8, 6u);
        var unknownShooterHit = unknownShooter.Hit(252, 6f, 355.00f, Frame, 2u, inFireBody: true, shooterLookup: 0xBBUL);
        check(unknownShooterHit.Route == DamageNumberRoute.Merged && unknownShooterHit.Capture.Shooter == PelletShooter.Unknown,
            "D6: an unreadable shooter on the Fire side proves nothing - the shooter gate never refuses on an unknown identity");

        // Another weapon's KNOWN category is refused even inside the Fire once this volley has PROVEN its own pellet
        // category, and the learned value is not widened by it. The refusal now comes from the CLASSIFIER (the volley's
        // predicate is already false for a category that neither equals the volley's own nor the learned one), so the
        // honest reason is `not-classified` and the same line carries hitCat=45 / learned=2 / nested=yes.
        var foreignCategory = new Pipeline();
        foreignCategory.Fire(360f, Frame, 0x20000, 8, 6u, shooterLookup: 0xAAUL);
        var learnedFirst = foreignCategory.Hit(261, 2f, 360.00f, Frame, 2u, inFireBody: true, shooterLookup: 0xAAUL);
        var foreignKnown = foreignCategory.Hit(261, 7f, 360.00f, Frame, 45u, inFireBody: true, shooterLookup: 0xAAUL);
        string foreignKnownReceipt = PelletCaptureReceipt.Explain(foreignKnown.Capture, Frame, foreignCategory.LastSettle);
        foreignCategory.EndFire(true, true);
        foreignCategory.Tick(360.06f, Frame + 3);
        foreignCategory.Tick(360.12f, Frame + 7);
        check(learnedFirst.Route == DamageNumberRoute.Merged && foreignKnown.Route == DamageNumberRoute.Immediate
            && foreignKnown.Capture.Refusal == PelletRefusal.NotClassified && foreignKnown.NeedsReport
            && foreignKnownReceipt.Contains(";hitCat=45") && foreignKnownReceipt.Contains(";learned=2")
            && foreignKnownReceipt.Contains(";nested=yes") && foreignKnownReceipt.Contains(";volleyCat=6")
            && foreignCategory.Drawn.Count == 1 && Math.Abs(foreignCategory.Drawn[0].Damage - 2f) < 0.001f,
            "D6: learning hitCat=2 narrows nothing else - a known foreign category inside the same Fire keeps its own number and its receipt names hitCat=45/learned=2/nested=yes");

        // Two consecutive pulls of the SAME weapon: the second Fire settles the first volley, and each shot's pellets
        // are summed only in their own number.
        var fastReal = new Pipeline();
        fastReal.Fire(370f, Frame, 0x21000, 8, 6u, shooterLookup: 0xAAUL);
        fastReal.Hit(271, 2f, 370.00f, Frame, 2u, inFireBody: true, shooterLookup: 0xAAUL);
        fastReal.Hit(271, 3f, 370.00f, Frame, 2u, inFireBody: true, shooterLookup: 0xAAUL);
        fastReal.EndFire(true, true);
        bool reopenedReal = fastReal.Fire(370.08f, Frame + 2, 0x21000, 8, 6u, shooterLookup: 0xAAUL) == ShotOpenResult.Opened;
        fastReal.Hit(271, 5f, 370.08f, Frame + 2, 2u, inFireBody: true, shooterLookup: 0xAAUL);
        fastReal.EndFire(true, true);
        fastReal.Tick(370.20f, Frame + 8);
        fastReal.Tick(370.26f, Frame + 10);
        check(reopenedReal && fastReal.Drawn.Count == 2
            && Math.Abs(fastReal.Drawn[0].Damage - 5f) < 0.001f
            && Math.Abs(fastReal.Drawn[1].Damage - 5f) < 0.001f,
            "D6: two fast pulls of the real (6,2) input stay two numbers of their own totals (no cross-shot merging)");

        // The one invariant that made the Survival failure impossible to see: the classifier may never be stricter
        // than the volley. For every combination the capture would take, Decide must not answer `Immediate` with
        // IsShotgun == false.
        var matrix = new Pipeline();
        bool classifierMatchesCapture = true;
        uint[] fireCategories = { 6u, 16u };
        uint[] hitCategories = { 0u, 2u, 6u, 16u, 45u };
        foreach (uint fireCategory in fireCategories)
        {
            foreach (uint hitCategory in hitCategories)
            {
                foreach (bool nested in new[] { true, false })
                {
                    var probe = new Pipeline();
                    probe.Fire(400f, Frame, 0x22000, 8, fireCategory, shooterLookup: 0xAAUL);
                    // A nested first hit teaches the volley the pellet category under test.
                    if (!nested && hitCategory != 0u) probe.Hit(281, 1f, 400.00f, Frame, hitCategory, inFireBody: true, shooterLookup: 0xAAUL);
                    DamageNumberDecision decision = probe.Hit(282, 1f, 400.01f, Frame, hitCategory, inFireBody: nested, shooterLookup: 0xAAUL);
                    if (decision.Capture.Taken && !decision.IsShotgun) classifierMatchesCapture = false;
                }
            }
        }
        check(classifierMatchesCapture,
            "D6: over every (fire=6|16, hit=0|2|6|16|45, nested|asynchronous) combination, the classifier is never stricter than the volley - if the capture takes the hit, Decide never reports IsShotgun=false");
        check(matrix.Opened == 0, "D6: the matrix runs on its own pipelines and never leaves a volley open in the shared fixture");
        check(FeedbackPolicy.MaxPelletCaptureLag < FeedbackPolicy.MaxVolleySeconds
            && FeedbackPolicy.VolleyQuietTicks >= 2
            && damageNumberRouteIsOnlyThreeWay(),
            "the volley windows, the quiet-check requirement and the route vocabulary keep their documented relationship");
    }

    // A dropped/merged/suppressed number is never drawn by the caller; only Immediate is.
    private static bool damageNumberRouteIsOnlyThreeWay()
        => (int)DamageNumberRoute.Dropped == 0 && (int)DamageNumberRoute.Immediate == 1
            && (int)DamageNumberRoute.Merged == 2 && (int)DamageNumberRoute.Suppressed == 3;
}
