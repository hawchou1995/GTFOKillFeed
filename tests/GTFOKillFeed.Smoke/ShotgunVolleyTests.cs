using System;
using System.Collections.Generic;
using HawChow.GTFOKillFeed;

// 1.2.19 pellet volley, asserted without Unity.
//
// The 1.2.17 real-machine log printed twelve pellet numbers (2.85-3.31) on twelve consecutive frames for
// ONE shotgun shot: the game settles pellet damage across frames, so the 1.2.18 scope (which required
// `fireFrame == Time.frameCount`) was already closed and every late pellet drew its own number.
//
// These fixtures drive the real production pieces - ShotgunVolleyState (open/capture/end sequencing),
// FeedbackPolicy.IsShotgunFireScopeCurrent / ShouldDrainVolley / ShouldDrawDrainedVolley /
// ShouldMergeShotgunDamageNumbers / ShouldSuppressShotgunDamageNumber and ShotgunDamageNumberBatch. Only
// the capture's three-line "draw what End() handed over" step is repeated here, because it needs
// DamageNumbers (UnityEngine). Nothing below re-implements a decision: every branch is a production call.
internal static class ShotgunVolleyTests
{
    private const int Frame = 100;
    private const uint Category = 44;

    // The capture's sequence, driven with a fake clock.
    private sealed class Volley
    {
        private readonly ShotgunVolleyState _state = new();
        internal readonly List<MergedShotgunDamageNumber> Drawn = new();
        internal int LastFrames;

        // The capture's BeginFire, in production order: the fire identity gate first (a DIFFERENT weapon instance
        // settles the open volley at that instant), then the same-shot check, then the new scope.
        internal bool Fire(float now, int frame, long owner, int expectedPellets, bool show = true, bool merge = true, uint category = Category)
        {
            if (_state.IsForeignShot(owner)) Draw(frame);
            if (_state.IsSameShot(owner, frame)) return false;
            Draw(frame);
            _state.Adopt(new ShotgunDamageNumberScope(null, show, merge, frame, category, now, expectedPellets, owner));
            return true;
        }

        // The capture's BeginMultiPelletFire for a weapon whose ShotgunBulletCount is 1 (a rifle): the identity gate
        // runs for that Fire too, but no volley is opened.
        internal bool NonPelletFire(int frame, long owner)
        {
            if (!_state.IsForeignShot(owner)) return false;
            Draw(frame);
            return true;
        }

        // true = the hit belongs to the volley and must not draw its own number. `category` is the HIT's gear
        // category (F1) and `origin` is the damage-root entry that produced it (1.2.19): the volley only takes hits
        // that prove they came from a bullet AND from the same weapon.
        internal bool Pellet(long enemy, float damage, float now, uint category = Category, DamageOrigin origin = DamageOrigin.Bullet)
            => _state.TryCapture(enemy, 0f, 0f, 0f, damage, HitKind.Normal, category, origin, now);

        internal bool WindowOpen(float now, uint category = Category, DamageOrigin origin = DamageOrigin.Bullet)
            => _state.IsWindowOpen(now, category, origin);

        internal void Tick(float now, int frame)
        {
            if (_state.ShouldEnd(now)) Draw(frame);
        }
        // 1.2.19 fix D2: the silence end condition has to hold for FeedbackPolicy.VolleyQuietTicks consecutive end
        // checks (production: one per frame), so a test that wants the volley CLOSED asks for that many checks.
        internal void Settle(float now, int frame, int quietChecks = FeedbackPolicy.VolleyQuietTicks)
        {
            // Same clock, same frame: only the number of end checks differs, so the reported frame span stays the one
            // the fixture set up.
            for (int i = 0; i < quietChecks; i++) Tick(now, frame);
        }

        internal void Reset() => _state.Clear();

        private void Draw(int frame)
        {
            var scope = _state.End(frame, out int frames);
            LastFrames = frames;
            if (scope == null) return;
            if (!FeedbackPolicy.ShouldDrawDrainedVolley(scope.ShowNumbers, scope.PelletHits)) return;
            Drawn.AddRange(scope.Batch.Drain());
        }
    }

    internal static void Run(Action<bool, string> check)
    {
        // ---- 1) several pellets in ONE frame: one number, and it is the sum --------------------------
        var sameFrame = new Volley();
        sameFrame.Fire(10f, Frame, 0x1000, 8);
        bool sameFrameCaptured = sameFrame.Pellet(11, 2.85f, 10.01f)
            && sameFrame.Pellet(11, 3.10f, 10.02f)
            && sameFrame.Pellet(11, 3.31f, 10.03f);
        sameFrame.Settle(10.10f, Frame);
        check(sameFrameCaptured && sameFrame.Drawn.Count == 1
            && Math.Abs(sameFrame.Drawn[0].Damage - 9.26f) < 0.001f && sameFrame.Drawn[0].PelletHits == 3
            && sameFrame.LastFrames == 1,
            "one shotgun shot in one frame draws one number per enemy and it is the pellet sum");

        // ---- 2) the same shot settling over later frames: still one number --------------------------
        var acrossFrames = new Volley();
        acrossFrames.Fire(20f, Frame, 0x2000, 0);
        bool late1 = acrossFrames.Pellet(21, 2f, 20.02f);
        bool late2 = acrossFrames.Pellet(21, 3f, 20.07f);   // frame +3 is not frame +0
        check(late1 && late2 && acrossFrames.Drawn.Count == 0,
            "a pellet on a later frame is still part of the same shot (no frame equality)");
        acrossFrames.Settle(20.20f, Frame + 4);
        check(acrossFrames.Drawn.Count == 1 && Math.Abs(acrossFrames.Drawn[0].Damage - 5f) < 0.001f
            && acrossFrames.LastFrames == 5,
            "a volley settled over several frames draws one number and reports its real frame span");

        // ---- 3) two shots 0.6 s apart: two numbers, never merged ------------------------------------
        var twoShots = new Volley();
        twoShots.Fire(30f, Frame, 0x3000, 0);
        twoShots.Pellet(31, 4f, 30.02f);
        twoShots.Tick(30.60f, Frame + 36);
        twoShots.Fire(30.60f, Frame + 36, 0x3000, 0);
        twoShots.Pellet(31, 6f, 30.62f);
        twoShots.Settle(30.70f, Frame + 40);
        check(twoShots.Drawn.Count == 2
            && Math.Abs(twoShots.Drawn[0].Damage - 4f) < 0.001f
            && Math.Abs(twoShots.Drawn[1].Damage - 6f) < 0.001f,
            "two shots 0.6 s apart draw two numbers and never merge across shots");

        // ---- 4) silence flush at 0.05 s (TWO end checks), with an UNKNOWN expected pellet count -----
        // 1.2.19 fix D2: one quiet end check is not enough. At any frame rate below 20 fps a single frame gap is
        // already longer than the 50 ms silence window, so a one-check end split one shot across its own pellet
        // stream. The volley now ends on the second consecutive quiet check (or the 0.5 s cap).
        var silence = new Volley();
        silence.Fire(40f, Frame, 0x4000, 0);
        silence.Pellet(41, 2f, 40.00f);
        silence.Pellet(41, 3f, 40.00f);
        silence.Tick(40.04f, Frame);
        check(silence.Drawn.Count == 0, "a volley is not flushed before the 0.05 s silence window");
        silence.Tick(40.06f, Frame + 3);
        check(silence.Drawn.Count == 0, "D2: ONE quiet end check is not enough - a single long frame must not end the shot");
        silence.Tick(40.06f, Frame + 4);
        check(silence.Drawn.Count == 1 && Math.Abs(silence.Drawn[0].Damage - 5f) < 0.001f,
            "an unknown pellet count still merges, and 0.05 s of silence flushes the volley after the second quiet check");

        // ---- 5) the hard cap flushes even while pellets keep landing --------------------------------
        // R1 (2nd review): the pellets stay inside MaxPelletCaptureLag (0.30 s); a hit that lands later is not part of
        // the shot any more (see 9e) - the volley's own 0.5 s cap only decides when it CLOSES.
        var capped = new Volley();
        capped.Fire(50f, Frame, 0x5000, 0);
        capped.Pellet(51, 2f, 50.00f);
        capped.Pellet(51, 3f, 50.28f);
        capped.Tick(50.50f, Frame + 30);
        check(capped.Drawn.Count == 1 && Math.Abs(capped.Drawn[0].Damage - 5f) < 0.001f,
            "the 0.5 s hard cap flushes a volley whose pellets never went quiet");

        // ---- 6) F3: the declared ShotgunBulletCount is diagnostic, NEVER an end condition ------------
        // A real shot can settle more pellet hits than the archetype declares (one pellet can touch two limbs), so
        // ending on `pellets >= expected` split one shot into a merged number plus a tail of single numbers - the
        // very symptom 1.2.19 fixes. captured == / < / > expected must each end in exactly ONE merged number.
        var counted = new Volley();
        counted.Fire(55f, Frame, 0x5500, 4);
        counted.Pellet(56, 1f, 55.00f);
        counted.Pellet(56, 1f, 55.01f);
        counted.Pellet(56, 1f, 55.02f);
        counted.Pellet(56, 1f, 55.03f);
        check(counted.Drawn.Count == 0 && counted.WindowOpen(55.04f),
            "F3: reaching the declared pellet count must not end the volley by itself");
        counted.Pellet(56, 1f, 55.06f);          // a 5th hit of the SAME shot
        counted.Settle(55.12f, Frame + 2);
        check(counted.Drawn.Count == 1 && counted.Drawn[0].PelletHits == 5
            && Math.Abs(counted.Drawn[0].Damage - 5f) < 0.001f,
            "F3: more pellet hits than ShotgunBulletCount still end in exactly one merged number");
        var belowExpected = new Volley();
        belowExpected.Fire(56f, Frame, 0x5600, 4);
        belowExpected.Pellet(57, 2f, 56.00f);
        belowExpected.Pellet(57, 3f, 56.01f);
        belowExpected.Settle(56.10f, Frame + 4);
        check(belowExpected.Drawn.Count == 1 && belowExpected.Drawn[0].PelletHits == 2
            && Math.Abs(belowExpected.Drawn[0].Damage - 5f) < 0.001f,
            "F3: fewer pellet hits than declared also end in exactly one merged number (silence, not the count)");

        // ---- 7) MergeShotgunPellets = false: every pellet stays immediate ---------------------------
        var noMerge = new Volley();
        noMerge.Fire(60f, Frame, 0x6000, 8, show: true, merge: false);
        bool merged1 = noMerge.Pellet(61, 2f, 60.01f);
        bool merged2 = noMerge.Pellet(61, 3f, 60.02f);
        noMerge.Tick(60.10f, Frame);
        check(!merged1 && !merged2 && noMerge.Drawn.Count == 0,
            "MergeShotgunPellets=false keeps every pellet an immediate number and merges nothing");

        // ---- 8) ShowShotgunDamageNumbers = false: consumed and never drawn --------------------------
        var hidden = new Volley();
        hidden.Fire(70f, Frame, 0x7000, 8, show: false, merge: true);
        bool swallowed = hidden.Pellet(71, 2f, 70.01f);
        hidden.Tick(70.10f, Frame);
        check(swallowed && hidden.Drawn.Count == 0 && !FeedbackPolicy.ShouldDrawDrainedVolley(false, 3),
            "ShowShotgunDamageNumbers=false swallows the pellets and draws nothing at all");

        // ---- 9) a single-pellet weapon never enters a volley ---------------------------------------
        var single = new Volley();
        check(!single.Pellet(81, 24.35f, 80.00f) && single.Drawn.Count == 0,
            "with no volley open (every single-pellet weapon) the capture declines the hit, so the number draws immediately");

        // ---- 9b) A (1.2.19 real-machine): the pellet's own gear category is UNKNOWN on a real client ------------
        // The 1.2.19 review decided an unknown hit category (0) is "never a pellet". The Survival log proved the
        // opposite: `[DN] volley;weapon=Shotgun;bullets=6;category=6` (and 16 / 272) with ZERO `[DN] merged`, i.e.
        // the WEAPON category is known while the pellet's own category is 0. Rejecting it made every shotgun print
        // one number per pellet again, so the wildcard is back - and the weapon identity (9d) owns the separation.
        var unknownHitCategory = new Volley();
        unknownHitCategory.Fire(130f, Frame, 0xD000, 6, category: 6u);
        bool unknownPellet1 = unknownHitCategory.Pellet(131, 2f, 130.01f, 0u);
        bool unknownPellet2 = unknownHitCategory.Pellet(131, 3f, 130.02f, 0u);
        unknownHitCategory.Settle(130.10f, Frame);
        check(unknownPellet1 && unknownPellet2 && unknownHitCategory.Drawn.Count == 1
            && unknownHitCategory.Drawn[0].PelletHits == 2
            && Math.Abs(unknownHitCategory.Drawn[0].Damage - 5f) < 0.001f,
            "A: a pellet whose own gear category is 0 is merged into the open volley (weapon category 6) and the whole shot draws exactly ONE number (2 pellets summed)");
        // A KNOWN and different category is still refused, so a rifle that really carries a category is never a pellet.
        var categoryGate = new Volley();
        categoryGate.Fire(131f, Frame, 0xD100, 8, category: 44u);
        bool ownHit = categoryGate.Pellet(131, 2f, 131.01f, 44u);
        bool rifleHit = categoryGate.Pellet(131, 7f, 131.02f, 45u);   // another weapon's KNOWN category
        check(ownHit && !rifleHit && categoryGate.WindowOpen(131.03f, 44u) && !categoryGate.WindowOpen(131.03f, 45u),
            "A: a hit with another weapon's KNOWN category is still declined while the same category stays a pellet");
        categoryGate.Settle(131.10f, Frame);
        check(categoryGate.Drawn.Count == 1 && Math.Abs(categoryGate.Drawn[0].Damage - 2f) < 0.001f,
            "A: the foreign-category hit is never added to the batch, so the merged number carries only the real pellet");
        // ... and it must not be swallowed by the shotgun visibility switch either (the classification runs before
        // the suppression): with ShowShotgunDamageNumbers=false only real pellets are hidden.
        var hiddenWithForeign = new Volley();
        hiddenWithForeign.Fire(132f, Frame, 0xD200, 8, show: false, merge: true, category: 44u);
        bool hiddenOwn = hiddenWithForeign.Pellet(133, 3f, 132.01f, 44u);
        bool hiddenForeign = hiddenWithForeign.Pellet(133, 8f, 132.02f, 45u);
        check(hiddenOwn && !hiddenForeign,
            "A: with ShowShotgunDamageNumbers=false a real pellet is swallowed but a foreign-category hit is declined, so it still draws");
        check(FeedbackPolicy.IsVolleyHit(true, 44u, 0u, 44u, DamageOrigin.Bullet, false, 0UL, 0UL)
            && !FeedbackPolicy.IsVolleyHit(true, 44u, 0u, 45u, DamageOrigin.Bullet, false, 0UL, 0UL)
            && FeedbackPolicy.IsVolleyHit(true, 6u, 0u, 0u, DamageOrigin.Bullet, false, 0UL, 0UL)
            && !FeedbackPolicy.IsVolleyHit(true, 0u, 0u, 6u, DamageOrigin.Bullet, false, 0UL, 0UL)
            && FeedbackPolicy.IsVolleyHit(true, 0u, 0u, 0u, DamageOrigin.Bullet, false, 0UL, 0UL)
            && !FeedbackPolicy.IsVolleyHit(false, 44u, 0u, 44u, DamageOrigin.Bullet, false, 0UL, 0UL)
            && !FeedbackPolicy.IsVolleyHit(false, 6u, 0u, 0u, DamageOrigin.Bullet, false, 0UL, 0UL),
            "A/R1: IsVolleyHit accepts an unknown HIT category, no longer wildcards an unknown VOLLEY category, still rejects two KNOWN and different categories, and never accepts without the open window (the same-shot / learned-category / shooter arms are asserted in DamageOriginTests 3b)");

        // ---- 9d) A: another weapon firing settles the open volley at that instant -------------------------------
        // The restored wildcard means a rifle/melee hit's unknown category can no longer separate the weapons, so the
        // weapon IDENTITY does: the moment a different instance fires, the open volley is drained. Its later hits find
        // no window and keep their own immediate number, and the pellets the shotgun really took are still one number.
        var foreignFire = new Volley();
        foreignFire.Fire(150f, Frame, 0xF000, 6, category: 6u);
        bool pelletBeforeForeignFire = foreignFire.Pellet(151, 4f, 150.01f, 0u);
        bool rifleFired = foreignFire.NonPelletFire(Frame + 3, 0xF001);
        bool rifleHitAfterItsOwnFire = foreignFire.Pellet(151, 9f, 150.06f, 0u);
        check(pelletBeforeForeignFire && rifleFired && !rifleHitAfterItsOwnFire
            && foreignFire.Drawn.Count == 1 && Math.Abs(foreignFire.Drawn[0].Damage - 4f) < 0.001f,
            "A: another weapon instance firing settles the open volley immediately (one merged number for the pellets it already took) and that weapon's later hits are no longer captured");
        // The same instance firing again is NOT a foreign fire: the normal same-shot / new-shot path decides.
        check(FeedbackPolicy.IsForeignVolleyOwner(0xF000, 0xF001)
            && !FeedbackPolicy.IsForeignVolleyOwner(0xF000, 0xF000)
            && !FeedbackPolicy.IsForeignVolleyOwner(0L, 0xF001) && !FeedbackPolicy.IsForeignVolleyOwner(0L, 0L),
            "A: the fire identity gate is one pointer comparison - the same instance is never foreign and an unknown volley owner never settles anything");

        // ---- 9c) R1: an unknown VOLLEY category only takes an unknown hit category ------------------
        // A multi-pellet weapon without a GearCategoryData declares nothing - and a melee/explosion/DoT/EWC hit, which
        // never passes a Fire prefix so the identity gate is never asked, declares nothing either. The old
        // `volleyCategoryId == 0` wildcard accepted BOTH; now an unknown volley category only takes the unknown hit
        // category, i.e. exactly the same-weapon case the Fire itself proved.
        var unknownVolley = new Volley();
        unknownVolley.Fire(140f, Frame, 0xE000, 0, category: 0u);
        bool unknownScopeSame = unknownVolley.Pellet(141, 3f, 140.01f, 0u);
        bool unknownScopeForeign = unknownVolley.Pellet(141, 9f, 140.02f, 77u);
        unknownVolley.Settle(140.10f, Frame);
        check(unknownScopeSame && !unknownScopeForeign && unknownVolley.Drawn.Count == 1
            && unknownVolley.Drawn[0].PelletHits == 1 && Math.Abs(unknownVolley.Drawn[0].Damage - 3f) < 0.001f,
            "R1: with an unknown volley category only the unknown (same-weapon) hit category merges, and a KNOWN foreign category is declined even there");

        // ---- 10) several enemies in one shot: one number each --------------------------------------
        var multi = new Volley();
        multi.Fire(90f, Frame, 0x9000, 0);
        multi.Pellet(91, 2f, 90.01f);
        multi.Pellet(92, 7f, 90.02f);
        multi.Pellet(91, 3f, 90.03f);
        multi.Settle(90.10f, Frame);
        check(multi.Drawn.Count == 2
            && multi.Drawn[0].EnemyIdentity == 91 && Math.Abs(multi.Drawn[0].Damage - 5f) < 0.001f
            && multi.Drawn[1].EnemyIdentity == 92 && Math.Abs(multi.Drawn[1].Damage - 7f) < 0.001f,
            "one shot gives every hit enemy its own merged number");

        // ---- 11) a new Fire flushes the previous volley before it opens ----------------------------
        var refire = new Volley();
        refire.Fire(100f, Frame, 0xA000, 0);
        refire.Pellet(101, 4f, 100.01f);
        bool reopened = refire.Fire(100.2f, Frame + 12, 0xA000, 0);
        check(reopened && refire.Drawn.Count == 1 && Math.Abs(refire.Drawn[0].Damage - 4f) < 0.001f,
            "firing again flushes the previous volley into its own number before the new one opens");
        bool nextShotPellet = refire.Pellet(101, 6f, 100.21f);
        refire.Settle(100.30f, Frame + 18);
        check(nextShotPellet && refire.Drawn.Count == 2 && Math.Abs(refire.Drawn[1].Damage - 6f) < 0.001f,
            "the new shot keeps its own number: no cross-shot merging");

        // ---- 12) the same shot seen through two entry points is still one volley -------------------
        var doubleEntry = new Volley();
        doubleEntry.Fire(110f, Frame, 0xB000, 8);
        bool secondEntry = doubleEntry.Fire(110f, Frame, 0xB000, 8);
        doubleEntry.Pellet(111, 2f, 110.01f);
        doubleEntry.Pellet(111, 3f, 110.02f);
        doubleEntry.Settle(110.10f, Frame);
        check(!secondEntry && doubleEntry.Drawn.Count == 1 && Math.Abs(doubleEntry.Drawn[0].Damage - 5f) < 0.001f,
            "Gear.Shotgun.Fire and Gear.BulletWeapon.Fire on the same instance and frame are one shot, not two flushes");

        // ---- 13) a level change clears the volley: nothing survives into the next level ------------
        var reset = new Volley();
        reset.Fire(120f, Frame, 0xC000, 0);
        reset.Pellet(121, 5f, 120.01f);
        reset.Reset();
        reset.Tick(120.90f, Frame + 60);
        check(reset.Drawn.Count == 0 && !reset.Pellet(121, 5f, 121.00f),
            "a level change clears the open volley, so no number and no pellet survives into the next level");

        // ---- 9e) R1: MaxPelletCaptureLag bounds the capture window ---------------------------------
        // Real pellets settle 0.02-0.21 s after the Fire. A 0-category hit later than MaxPelletCaptureLag (0.30 s) is
        // no longer part of that shot - it keeps its own immediate number even though the volley is still open, which
        // is what keeps a late melee/explosion/DoT/EWC hit out of the batch without a new patch.
        var lateZeroCategory = new Volley();
        lateZeroCategory.Fire(160f, Frame, 0x1600, 6, category: 6u);
        bool insideLag = lateZeroCategory.Pellet(161, 2f, 160.25f, 0u);
        bool afterLag = lateZeroCategory.Pellet(161, 70f, 160.35f, 0u);
        lateZeroCategory.Settle(160.40f, Frame + 24);
        check(insideLag && !afterLag && lateZeroCategory.Drawn.Count == 1
            && Math.Abs(lateZeroCategory.Drawn[0].Damage - 2f) < 0.001f,
            "R1: a 0-category hit at +0.25 s is merged, and a 0-category hit at +0.35 s is NOT merged (it keeps its own immediate number)");
        check(Math.Abs(FeedbackPolicy.MaxPelletCaptureLag - 0.30f) < 0.0001f
            && FeedbackPolicy.MaxPelletCaptureLag < FeedbackPolicy.MaxVolleySeconds
            && FeedbackPolicy.IsPelletCaptureWindow(160f, 160.25f, false)
            && !FeedbackPolicy.IsPelletCaptureWindow(160f, 160.35f, false)
            && !FeedbackPolicy.IsPelletCaptureWindow(160f, 160.25f, true)
            && FeedbackPolicy.IsShotgunFireScopeCurrent(160f, 160.35f, false),
            "R1: the capture window is the volley window narrowed to a pinned 0.30 s - a drained volley takes nothing, and the volley itself still lives to 0.5 s");

        // ---- 14) R6: the volley line and the merged line own separate allowances --------------------
        // One shared 8-line budget was spent by `[DN] volley` alone, so from the 9th shot of a level on the only line
        // that proves the merge could not print. Two budgets, 8 lines each, restored by Reset together.
        var volleyLines = new DiagnosticBudget();
        var mergedLines = new DiagnosticBudget();
        bool volleySpent = true, mergedOpen = true;
        for (int i = 0; i < 8; i++) volleySpent &= volleyLines.TryUse(8);
        for (int i = 0; i < 8; i++) mergedOpen &= mergedLines.TryUse(8);
        check(volleySpent && mergedOpen && !volleyLines.TryUse(8) && !mergedLines.TryUse(8)
            && !ReferenceEquals(volleyLines, mergedLines),
            "R6: `[DN] volley` and `[DN] merged` each own an 8-line allowance, so eight volley lines can no longer suppress the merged receipt");

        // ---- 15) 1.2.19 path proof: only a BULLET may join the volley -------------------------------
        // The 0.30 s window and the category wildcard used to be the whole decision, so a melee / explosion / DoT /
        // push hit landing right after a shotgun blast inside the window was buffered as a pellet (delayed, mean
        // position, summed) - and vanished when ShowShotgunDamageNumbers was off. The damage-root origin now decides,
        // and every non-bullet hit keeps its own immediate number.
        var mixedOrigins = new Volley();
        mixedOrigins.Fire(200f, Frame, 0x20000, 6, category: 6u);
        bool mixedPellet = mixedOrigins.Pellet(201, 3f, 200.01f, 0u, DamageOrigin.Bullet);
        bool mixedMelee = mixedOrigins.Pellet(201, 11f, 200.02f, 0u, DamageOrigin.Melee);
        bool mixedExplosion = mixedOrigins.Pellet(202, 13f, 200.03f, 0u, DamageOrigin.Explosion);
        bool mixedDot = mixedOrigins.Pellet(203, 17f, 200.04f, 0u, DamageOrigin.Other);
        bool mixedPush = mixedOrigins.Pellet(201, 19f, 200.05f, 0u, DamageOrigin.Push);
        bool mixedFire = mixedOrigins.Pellet(201, 23f, 200.06f, 0u, DamageOrigin.Fire);
        mixedOrigins.Settle(200.20f, Frame + 4);
        check(mixedPellet && !mixedMelee && !mixedExplosion && !mixedDot && !mixedPush && !mixedFire
            && mixedOrigins.Drawn.Count == 1
            && mixedOrigins.Drawn[0].EnemyIdentity == 201 && mixedOrigins.Drawn[0].PelletHits == 1
            && Math.Abs(mixedOrigins.Drawn[0].Damage - 3f) < 0.001f,
            "1.2.19: inside one frame and one open volley only the Bullet hit is merged - melee, explosion, DoT, push and fire each keep their own immediate number");

        // ... and the same scene with the shotgun numbers HIDDEN: the pellet is swallowed, the others still draw.
        var hiddenMixed = new Volley();
        hiddenMixed.Fire(210f, Frame, 0x21000, 6, show: false, merge: true, category: 6u);
        bool hiddenBullet = hiddenMixed.Pellet(211, 4f, 210.01f, 0u, DamageOrigin.Bullet);
        bool shownMelee = hiddenMixed.Pellet(211, 9f, 210.02f, 0u, DamageOrigin.Melee);
        bool shownExplosion = hiddenMixed.Pellet(212, 12f, 210.03f, 0u, DamageOrigin.Explosion);
        hiddenMixed.Tick(210.10f, Frame);
        check(hiddenBullet && !shownMelee && !shownExplosion && hiddenMixed.Drawn.Count == 0,
            "1.2.19: ShowShotgunDamageNumbers=false swallows only the real pellet - a melee/explosion hit inside the same window still draws its own number");

        // ---- 16) the origin gate never replaces the window or the fire identity ---------------------
        // A Bullet hit late in the volley, or against another weapon's KNOWN category, is still refused: the path proof
        // is an ADDITIONAL condition, never a relaxation of the two existing ones.
        var originStillBounded = new Volley();
        originStillBounded.Fire(220f, Frame, 0x22000, 6, category: 6u);
        bool originInsideLag = originStillBounded.Pellet(221, 2f, 220.25f, 0u, DamageOrigin.Bullet);
        bool originAfterLag = originStillBounded.Pellet(221, 5f, 220.35f, 0u, DamageOrigin.Bullet);
        bool originForeignCategory = originStillBounded.Pellet(221, 7f, 220.26f, 45u, DamageOrigin.Bullet);
        bool originForeignWeaponFire = originStillBounded.NonPelletFire(Frame + 20, 0x22001);
        bool originAfterForeignFire = originStillBounded.Pellet(221, 8f, 220.28f, 0u, DamageOrigin.Bullet);
        originStillBounded.Tick(220.60f, Frame + 36);
        check(originInsideLag && !originAfterLag && !originForeignCategory && originForeignWeaponFire && !originAfterForeignFire
            && originStillBounded.Drawn.Count == 1
            && Math.Abs(originStillBounded.Drawn[0].Damage - 2f) < 0.001f,
            "1.2.19: a Bullet origin does not soften the 0.30 s capture window, the foreign category gate or the fire identity gate");

        // ---- 17) a single bullet still never enters a volley ---------------------------------------
        var singleBullet = new Volley();
        check(!singleBullet.Pellet(231, 24.35f, 230.00f, 0u, DamageOrigin.Bullet) && singleBullet.Drawn.Count == 0,
            "1.2.19: with no volley open a Bullet hit is declined by the capture, so even a projectile keeps its immediate number");
    }
}
