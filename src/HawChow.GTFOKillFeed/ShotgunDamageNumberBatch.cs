using System;
using System.Collections.Generic;
using System.Globalization;

namespace HawChow.GTFOKillFeed;

// Which Fire entry opened a volley. Diagnostic only: it is the field that tells a real-machine log whether ONE
// trigger pull reached two entry points (Gear.Shotgun.Fire and Gear.BulletWeapon.Fire) on two DIFFERENT frames -
// the Survival 1.2.19 log printed exactly that (`[DN] volley;frame=11820` then `;frame=11825`, 11935/11941,
// 12040/12050 for the same weapon) and the same-shot check could not see it, because it compares frames.
internal enum VolleyEntry { Shotgun = 0, ShotgunSynced = 1, BulletWeapon = 2, BulletWeaponSynced = 3 }

// What happened to one damage-number hit. `Merged` and `Suppressed` never draw their own number; `Immediate` does.
internal enum PelletDecision { Immediate = 0, Suppressed = 1, Merged = 2 }

// Why the volley refused a hit it was asked about. Every reason is a fact read from the volley, never a guess:
//   NoVolley   - no volley was open at all (the shotgun's own category was known, so the hit WAS classified as a
//                shotgun hit; that is the "merge never happened" case, not "this is another weapon");
//   Window     - a volley was open but the hit is outside MaxPelletCaptureLag (or the volley is already drained);
//   Origin     - the damage root did not come through a bullet entry (melee/fire/explosion/push/DoT/other);
//   Category   - the hit's gear category is KNOWN, differs from the volley's own and from the pellet category that
//                volley already proved, and the hit was not delivered inside the shot's own Fire call;
//   Shooter    - the volley and the hit name two DIFFERENT local players, so this damage is another shooter's;
//   Target     - the enemy identity was not available, so the pellet could not be accumulated (fail to immediate,
//                never a silent drop);
//   Value      - a non-finite position or a negative/non-finite damage;
//   NotMerging - MergeShotgunPellets is off for the volley that opened;
//   Hidden     - ShowShotgunDamageNumbers is off for the volley that opened (swallowed on purpose);
//   NotClassified - diagnostic only (never returned by Capture): the classifier refused a Bullet hit that arrived
//                while a capture window was open, i.e. exactly the refusal the Survival log could not show at all.
internal enum PelletRefusal { None = 0, NoVolley, Window, Origin, Category, Target, Value, NotMerging, Hidden, Shooter, NotClassified }

// Who the volley and the hit belong to, as far as the plugin can PROVE it. The volley records the local player who
// owns the weapon that fired (Item.Owner -> PlayerAgent.Owner -> Player.Lookup); a damage number is only ever drawn
// for the local player's own hit (KillTracker.ResolveSource), so `Same` is the normal case and `Foreign` is a hit that
// must keep its own number.
internal enum PelletShooter { Unknown = 0, Same, Foreign }

// What a Fire did to the volley (1.2.19 review R3/R6): opened a new one, was the same shot seen twice, or could not be
internal enum ShotOpenResult { Opened = 0, SameShot = 1, Unattributable = 2, SettledOnly = 3 }

internal readonly struct PelletCaptureDecision
{
    internal readonly PelletDecision Decision;
    internal readonly PelletRefusal Refusal;
    // Facts carried out of the volley so the receipt can be built without reading the state a second time.
    internal readonly uint VolleyCategory, HitCategory, LearnedPelletCategory;
    internal readonly DamageOrigin Origin;
    internal readonly float Age;
    internal readonly int FrameDelta, VolleyPellets;
    internal readonly bool HasVolley, TargetKnown;
    // Was this hit delivered INSIDE the Fire call that opened the volley (the same-shot proof), and do the volley and
    // the hit name the same shooter?
    internal readonly bool NestedSameShot;
    internal readonly PelletShooter Shooter;

    internal PelletCaptureDecision(PelletDecision decision, PelletRefusal refusal, bool hasVolley, uint volleyCategory,
        uint hitCategory, DamageOrigin origin, float age, int frameDelta, int volleyPellets, bool targetKnown,
        bool nestedSameShot = false, uint learnedPelletCategory = 0u, PelletShooter shooter = PelletShooter.Unknown)
    {
        Decision = decision;
        Refusal = refusal;
        HasVolley = hasVolley;
        VolleyCategory = volleyCategory;
        HitCategory = hitCategory;
        Origin = origin;
        Age = age;
        FrameDelta = frameDelta;
        VolleyPellets = volleyPellets;
        TargetKnown = targetKnown;
        NestedSameShot = nestedSameShot;
        LearnedPelletCategory = learnedPelletCategory;
        Shooter = shooter;
    }

    // true = the hit belongs to the volley and must not draw its own number.
    internal bool Taken => Decision != PelletDecision.Immediate;
    internal bool Suppressed => Decision == PelletDecision.Suppressed;
}

// The one text of a refusal. Pure and shared, so a smoke assertion can pin the exact line the next real-machine
// log will contain instead of a copy of the formatting.
internal static class PelletCaptureReceipt
{
    internal static string Reason(PelletRefusal refusal) => refusal switch
    {
        PelletRefusal.NoVolley => "no-volley",
        PelletRefusal.Window => "window",
        PelletRefusal.Origin => "origin",
        PelletRefusal.Category => "category",
        PelletRefusal.Target => "target",
        PelletRefusal.Value => "value",
        PelletRefusal.NotMerging => "not-merging",
        PelletRefusal.Hidden => "hidden",
        PelletRefusal.Shooter => "shooter",
        PelletRefusal.NotClassified => "not-classified",
        _ => "none"
    };

    // Bounded by the caller (a signature-deduped, per-level allowance). Every field is one an operator needs to tell the
    // failures apart: no volley / outside the window / foreign category / foreign shooter / no target identity /
    // early settle / unreadable weapon / (diagnostic only) the classifier refusing a hit inside an open window.
    // `age=` is the time since the volley's Fire, `df=` its frame span, `nested=` says whether this hit was delivered
    // INSIDE the shot's own Fire call (the same-shot proof), `learned=` the pellet category that volley already took,
    // `shooter=` whether the volley and the hit name the same local player, and `last=` says what already happened to
    // that volley (review R4: a hit AFTER a Fire whose switch was off is `no-volley;last=forgotten:merge|show`, not
    // `not-merging`).
    internal static string Explain(PelletCaptureDecision decision, int frame, string lastSettle = "none")
    {
        string text = "[DN] pellet=drop;reason=" + Reason(decision.Refusal)
            + ";age=" + decision.Age.ToString("0.000", CultureInfo.InvariantCulture) + "s"
            + ";df=" + decision.FrameDelta.ToString(CultureInfo.InvariantCulture)
            + ";frame=" + frame.ToString(CultureInfo.InvariantCulture)
            + ";volleyCat=" + decision.VolleyCategory.ToString(CultureInfo.InvariantCulture)
            + ";hitCat=" + decision.HitCategory.ToString(CultureInfo.InvariantCulture)
            + ";learned=" + decision.LearnedPelletCategory.ToString(CultureInfo.InvariantCulture)
            + ";nested=" + (decision.NestedSameShot ? "yes" : "no")
            + ";shooter=" + ShooterText(decision.Shooter)
            + ";origin=" + decision.Origin
            + ";target=" + (decision.TargetKnown ? "yes" : "no")
            + ";owner=" + (decision.HasVolley ? "same-window" : "none")
            + ";last=" + (string.IsNullOrEmpty(lastSettle) ? "none" : lastSettle);
        return text;
    }

    internal static string ShooterText(PelletShooter shooter) => shooter switch
    {
        PelletShooter.Same => "same",
        PelletShooter.Foreign => "foreign",
        _ => "unknown"
    };
}

// The session's set of gear categories proven to belong to a multi-pellet weapon. It is populated by a Fire (the
// weapon's own GearCategoryData.persistentID) and by the first hit of a volley that opened without one. It is
// cleared with the level: 1.2.19 never cleared it, so a category learned from one weapon stayed "shotgun" for the
// rest of the process (and, with ShowShotgunDamageNumbers off, hid that weapon's numbers too).
internal static class ShotgunCategoryRegistry
{
    private static readonly object Gate = new();
    private static readonly HashSet<uint> Categories = new();

    internal static void Register(uint gearCategoryId)
    {
        if (gearCategoryId == 0) return;
        lock (Gate) Categories.Add(gearCategoryId);
    }

    internal static bool IsKnown(uint gearCategoryId)
    {
        if (gearCategoryId == 0) return false;
        lock (Gate) return Categories.Contains(gearCategoryId);
    }

    internal static void Reset()
    {
        lock (Gate) Categories.Clear();
    }
}

// What happened to one damage number (1.2.19 D5, moved here so the smoke suite can call the SAME decision the plugin
// calls). `Immediate` still has to be drawn by the caller; `Merged`/`Suppressed`/`Dropped` never are.
internal enum DamageNumberRoute { Dropped = 0, Immediate = 1, Merged = 2, Suppressed = 3 }

// The result of the production route for one hit: the classifier, the visibility gate and the volley capture, in that
// order, decided by ONE function that both DamageNumbers.Add (Unity) and the smoke suite call.
internal readonly struct DamageNumberDecision
{
    internal readonly DamageNumberRoute Route;
    internal readonly PelletCaptureDecision Capture;
    internal readonly bool IsShotgun;

    internal DamageNumberDecision(DamageNumberRoute route, PelletCaptureDecision capture, bool isShotgun)
    {
        Route = route;
        Capture = capture;
        IsShotgun = isShotgun;
    }

    // A refusal that has to produce the bounded `[DN] pellet=drop` receipt: a classified pellet the volley refused,
    // or (diagnostic only, 1.2.19 Survival retest) a Bullet hit the CLASSIFIER refused while a capture window was open.
    // The second case used to return a `default` decision, i.e. the classifier refusal was completely silent: the
    // Survival log showed twelve `route=Immediate;hitCat=2` lines, zero `[DN] pellet=drop` and zero `[DN] merged`, and
    // nothing in the plugin could say which gate had said no. `Shooter` is a classifier refusal too (the volley and the
    // hit name two different known local players), so it must be reported even though the hit is not a shotgun number.
    internal bool NeedsReport => !Capture.Taken && Capture.Refusal != PelletRefusal.None
        && (IsShotgun || Capture.Refusal == PelletRefusal.NotClassified || Capture.Refusal == PelletRefusal.Shooter);
}

internal static class DamageNumberRouter
{
    // 1.2.19 fix D1 (review R3): the production route used to live in DamageNumbers.Add, so a smoke fixture could only
    // copy it. It lives here now (Unity-free) and DamageNumbers.Add calls it with the live switches/clock - the tested
    // function IS the production function. Order, and nothing else:
    //   1) classify: only a Bullet-root hit may be a shotgun number (origin first, before the visibility switch);
    //   2) visibility gate: master switch, and for a shotgun hit the shotgun switch;
    //   3) a non-shotgun hit draws immediately (never touches the volley);
    //   4) a shotgun hit asks the volley, whose ONE decision is ShotgunVolleyState.Capture;
    //   5) an unclassified Bullet hit still asks the volley for a read-only description, so the classifier's own
    //      refusal gets a bounded receipt instead of silence (it changes no route).
    internal static DamageNumberDecision Decide(bool masterEnabled, bool showShotgunNumbers, bool preview,
        DamageOrigin origin, uint hitCategory, bool knownShotgunCategory, bool activeFireCategory,
        ulong shooterLookup, bool nestedSameShot,
        ShotgunVolleyState volley, long target, float x, float y, float z, float damage, HitKind kind,
        float now, int frame, float staggerDamage = 0f)
    {
        bool isShotgun = FeedbackPolicy.IsShotgunDamageNumber(preview, origin, knownShotgunCategory, activeFireCategory);
        if (!FeedbackPolicy.ShouldDisplayDamageNumber(masterEnabled, showShotgunNumbers, isShotgun))
            return new DamageNumberDecision(DamageNumberRoute.Dropped, default, isShotgun);
        if (!isShotgun || preview || volley == null)
        {
            // Read-only: Describe never adds a pellet, never learns a category and never ends a volley. It carries the
            // same-shot flag so the receipt's `nested=` tells the truth about the hit it refused.
            PelletCaptureDecision refusal = !isShotgun && !preview && volley != null && origin == DamageOrigin.Bullet
                ? volley.Describe(target, hitCategory, origin, shooterLookup, nestedSameShot, now, frame)
                : default;
            return new DamageNumberDecision(DamageNumberRoute.Immediate, refusal, isShotgun);
        }
        PelletCaptureDecision capture = volley.Capture(target, x, y, z, damage, kind, hitCategory, origin, shooterLookup,
            nestedSameShot, now, frame, staggerDamage);
        if (!capture.Taken) return new DamageNumberDecision(DamageNumberRoute.Immediate, capture, true);
        return new DamageNumberDecision(
            capture.Suppressed ? DamageNumberRoute.Suppressed : DamageNumberRoute.Merged, capture, true);
    }
}

internal readonly struct MergedShotgunDamageNumber
{
    internal readonly long EnemyIdentity;
    internal readonly float X, Y, Z, Damage;
    internal readonly HitKind Kind;
    internal readonly int PelletHits;
    // 1.2.24: how much of `Damage` was proven to have triggered a stagger. It is a SUBSET of Damage (never an
    // additional amount), so a merged number can show "18 + 6" without counting anything twice.
    internal readonly float TriggerDamage;

    internal MergedShotgunDamageNumber(long enemyIdentity, float x, float y, float z, float damage, HitKind kind, int pelletHits,
        float triggerDamage = 0f)
    {
        EnemyIdentity = enemyIdentity;
        X = x;
        Y = y;
        Z = z;
        Damage = damage;
        Kind = kind;
        PelletHits = pelletHits;
        TriggerDamage = triggerDamage;
    }
}

internal sealed class ShotgunDamageNumberBatch
{
    private sealed class Accumulator
    {
        internal readonly long EnemyIdentity;
        internal double PositionX, PositionY, PositionZ, Damage;
        // 1.2.24: the subset of Damage the game proved triggered a stagger for THIS enemy. Accumulated separately so a
        // pellet that triggered and a pellet that did not can be told apart inside one merged number.
        internal double TriggerDamage;
        internal HitKind Kind;
        internal int PelletHits;
        internal bool MixedKinds;

        internal Accumulator(long enemyIdentity, float x, float y, float z, float damage, HitKind kind, float triggerDamage)
        {
            EnemyIdentity = enemyIdentity;
            PositionX = x;
            PositionY = y;
            PositionZ = z;
            Damage = damage;
            TriggerDamage = triggerDamage > 0f ? triggerDamage : 0d;
            Kind = kind;
            PelletHits = 1;
        }
    }

    private readonly List<Accumulator> _entries = new();
    private readonly Dictionary<long, Accumulator> _byEnemy = new();
    internal int TargetCount => _entries.Count;

    internal bool Add(long enemyIdentity, float x, float y, float z, float damage, HitKind kind, float triggerDamage = 0f)
    {
        if (enemyIdentity == 0 || !KillPolicy.IsFinite(x) || !KillPolicy.IsFinite(y) || !KillPolicy.IsFinite(z)
            || !KillPolicy.IsFinite(damage) || damage < 0f) return false;
        float trigger = KillPolicy.IsFinite(triggerDamage) && triggerDamage > 0f ? Math.Min(triggerDamage, damage) : 0f;

        if (!_byEnemy.TryGetValue(enemyIdentity, out var item))
        {
            item = new Accumulator(enemyIdentity, x, y, z, damage, kind, trigger);
            _byEnemy.Add(enemyIdentity, item);
            _entries.Add(item);
            return true;
        }

        item.PositionX += x;
        item.PositionY += y;
        item.PositionZ += z;
        item.Damage += damage;
        item.TriggerDamage += trigger;
        item.PelletHits++;
        if (item.Kind != kind) item.MixedKinds = true;
        if (item.MixedKinds) item.Kind = HitKind.Normal;
        return true;
    }

    internal MergedShotgunDamageNumber[] Drain()
    {
        if (_entries.Count == 0) return Array.Empty<MergedShotgunDamageNumber>();
        var result = new MergedShotgunDamageNumber[_entries.Count];
        for (int i = 0; i < _entries.Count; i++)
        {
            var item = _entries[i];
            float damage = (float)Math.Min(item.Damage, float.MaxValue);
            // The trigger subset can never exceed the total it belongs to (defensive: the two are accumulated from the
            // same hits, but a pooled/edited readout must never print "24 = 26 + 6").
            float trigger = (float)Math.Min(item.TriggerDamage, damage);
            result[i] = new MergedShotgunDamageNumber(item.EnemyIdentity,
                (float)(item.PositionX / item.PelletHits),
                (float)(item.PositionY / item.PelletHits),
                (float)(item.PositionZ / item.PelletHits),
                damage, item.Kind, item.PelletHits, trigger);
        }
        _entries.Clear();
        _byEnemy.Clear();
        return result;
    }
}

internal sealed class ShotgunDamageNumberScope
{
    internal readonly ShotgunDamageNumberScope Previous;
    internal readonly bool ShowNumbers, MergePellets;
    internal readonly ShotgunDamageNumberBatch Batch = new();
    // FireFrame is only an identity tag (the "one shot through two entry points" check) and the source
    // of the diagnostic frame span. It is NEVER the capture criterion: requiring the same frame is
    // exactly what let 1.2.18 print one number per pellet.
    internal readonly int FireFrame;
    internal readonly float StartTime;
    internal readonly int ExpectedPellets;
    internal readonly long OwnerHandle;
    // Diagnostic: which Fire entry opened this volley (a single trigger pull can reach two of them).
    internal readonly VolleyEntry Entry;
    internal uint GearCategoryId;
    // The hit-side gear category this shot PROVED belongs to its own pellets (learned from a hit delivered inside this
    // shot's own Fire call, or from a hit that already equals GearCategoryId). 0 = nothing learned. It lives and dies
    // with this one volley: it is never written into ShotgunCategoryRegistry, so a hit category can never become a
    // process-wide "shotgun" category. The two ids are NOT one id space - the Survival retest printed the weapon's own
    // persistentID (6 / 16) on the volley line and 2 on every `[FEEDBACK] hitCat=` of the same shots.
    internal uint PelletCategoryId;
    // The local player who owns the weapon that fired (Item.Owner -> PlayerAgent.Owner -> Player.Lookup), 0 = not
    // readable. A damage number is only ever drawn for the local player's own hit, so a KNOWN and different shooter
    // proves the hit belongs to somebody else's shot.
    internal readonly ulong ShooterLookup;
    // Diagnostic: how many captured pellets were delivered INSIDE this Fire call (the same-shot proof).
    internal int NestedHits;
    internal float LastPelletAt;
    internal int PelletHits;
    // How many END CHECKS in a row have seen the silence window satisfied. Frame-granular without a frame number:
    // Tick runs exactly once per frame, so a pellet stream that lands one pellet per frame can never be split by a
    // long frame (the Survival profile runs far below 20 fps; 50 ms of silence was reached by a single frame gap).
    internal int QuietTicks;
    internal bool Drained;

    internal ShotgunDamageNumberScope(ShotgunDamageNumberScope previous, bool showNumbers, bool mergePellets, int fireFrame,
        uint gearCategoryId, float startTime = 0f, int expectedPellets = 0, long ownerHandle = 0L,
        VolleyEntry entry = VolleyEntry.Shotgun, ulong shooterLookup = 0UL)
    {
        Previous = previous;
        ShowNumbers = showNumbers;
        MergePellets = mergePellets;
        FireFrame = fireFrame;
        GearCategoryId = gearCategoryId;
        StartTime = startTime;
        LastPelletAt = startTime;
        ExpectedPellets = expectedPellets;
        OwnerHandle = ownerHandle;
        Entry = entry;
        ShooterLookup = shooterLookup;
    }

    // One captured pellet moves the silence deadline and grows the frame span the diagnostic reports.
    internal void NotePellet(float now)
    {
        PelletHits++;
        LastPelletAt = now;
        QuietTicks = 0;
    }

    // How many frames this volley really spanned - the number the next real-machine log proves.
    internal int FrameSpan(int currentFrame) => Math.Max(1, currentFrame - FireFrame + 1);
}

internal sealed class ShotgunDamageNumberScopeStack
{
    private ShotgunDamageNumberScope _current;
    internal ShotgunDamageNumberScope Current => _current;

    internal ShotgunDamageNumberScope Begin(bool showNumbers, bool mergePellets, int fireFrame, uint gearCategoryId,
        float startTime = 0f, int expectedPellets = 0, long ownerHandle = 0L, VolleyEntry entry = VolleyEntry.Shotgun,
        ulong shooterLookup = 0UL)
    {
        var scope = new ShotgunDamageNumberScope(_current, showNumbers, mergePellets, fireFrame, gearCategoryId,
            startTime, expectedPellets, ownerHandle, entry, shooterLookup);
        _current = scope;
        return scope;
    }

    internal bool TryEnd(ShotgunDamageNumberScope scope)
    {
        if (scope == null || !ReferenceEquals(_current, scope)) return false;
        _current = scope.Previous;
        return true;
    }
}

internal sealed class ShotgunDamageTargetScopeStack
{
    private long _current;
    internal long Current => _current;

    internal long Enter(long target)
    {
        long previous = _current;
        _current = target;
        return previous;
    }

    internal void Exit(long previous) => _current = previous;
}

// 1.2.19 pellet volley: the Unity-free state machine the capture drives with live Time values, so the
// smoke suite exercises the REAL open -> capture -> end sequencing instead of a copy of it (only the
// drawing stays in the capture). Nothing here treats a frame as an identity - a frame is a span for the
// diagnostic and nothing else.
internal sealed class ShotgunVolleyState
{
    private ShotgunDamageNumberScope _scope;
    internal ShotgunDamageNumberScope Scope => _scope;

    // The same instance inside the same frame is ONE shot seen through two entry points (Gear.Shotgun
    // and Gear.BulletWeapon both carry a Fire prefix): it must neither flush nor re-open the volley.
    internal bool IsSameShot(long ownerHandle, int frame)
        => _scope != null && ownerHandle != 0L && _scope.OwnerHandle == ownerHandle && _scope.FireFrame == frame;

    internal void Adopt(ShotgunDamageNumberScope scope) => _scope = scope;

    // A (1.2.19 real-machine regression): the same instance inside the same frame is ONE shot (IsSameShot); a
    // DIFFERENT weapon instance firing is the proof that the open volley has to be settled right now. Without it a
    // rifle/melee hit inside the open shotgun window - whose gear category is unknown (0) and therefore accepted by
    // FeedbackPolicy.IsVolleyHit - would be buffered as a pellet (or swallowed while the shotgun numbers are off).
    internal bool IsForeignShot(long fireOwnerHandle)
        => _scope != null && FeedbackPolicy.IsForeignVolleyOwner(_scope.OwnerHandle, fireOwnerHandle);

    // Used only when a volley provably can never receive a pellet (pellet merging switched off): the
    // pellet path returns false immediately, so keeping the volley would just hold a dead batch.
    internal void Forget(ShotgunDamageNumberScope scope)
    {
        if (ReferenceEquals(_scope, scope)) _scope = null;
    }
    // F1 (1.2.19 review) + R1 (2nd review) + 1.2.19 path proof + the Survival shotgun retest: the open window alone is
    // not enough. `gearCategoryId` is the hit's own category, `origin` the entry the damage root came through,
    // `nestedSameShot` whether this hit is being delivered inside the Fire call that opened THIS volley, and the two
    // lookups are the shooter identities. FeedbackPolicy.IsVolleyHit owns the whole membership decision, and both the
    // classifier (IsWindowOpen) and the capture (Capture) call it with the SAME facts - so the classifier can never be
    // stricter than the volley, which is exactly what made the Survival session silent.
    internal bool IsWindowOpen(float now, uint gearCategoryId, DamageOrigin origin, ulong shooterLookup = 0UL,
        bool nestedSameShot = false)
        => _scope != null && FeedbackPolicy.IsVolleyHit(
            FeedbackPolicy.IsPelletCaptureWindow(_scope.StartTime, now, _scope.Drained),
            _scope.GearCategoryId, _scope.PelletCategoryId, gearCategoryId, origin, nestedSameShot,
            _scope.ShooterLookup, shooterLookup);

    // The same-shot proof, for the Unity adapter: `fireScope` is the Fire scope this thread is INSIDE right now
    // (ShotgunDamageNumberCapture's thread-static fire stack). A hit is nested when that Fire scope is the scope of the
    // volley that is still open - i.e. the damage is being settled by the shot's own Fire call, on that thread.
    internal bool IsBodyHit(ShotgunDamageNumberScope fireScope)
        => fireScope != null && !fireScope.Drained && ReferenceEquals(_scope, fireScope);

    // true = the hit belongs to the volley and must not draw its own number (buffered, or deliberately
    // suppressed because the shotgun numbers are hidden). A non-Bullet hit returns false here, i.e. it draws
    // its own number immediately and is never suppressed by the shotgun visibility switch.
    internal bool TryCapture(long enemyIdentity, float x, float y, float z, float damage, HitKind kind, uint gearCategoryId,
        DamageOrigin origin, float now, ulong shooterLookup = 0UL, bool nestedSameShot = false, float staggerDamage = 0f)
        => Capture(enemyIdentity, x, y, z, damage, kind, gearCategoryId, origin, shooterLookup, nestedSameShot, now, 0, staggerDamage).Taken;

    // The ONE pellet decision (1.2.19 fix D1). Every branch the capture used to evaluate twice - the raw 0.5 s volley
    // window in the capture and the 0.30 s capture window in the state - is decided here exactly once, so the suppression
    // switch can only ever swallow a hit the volley itself would have accepted as a pellet. `frame` is diagnostic.
    // 1.2.24: `staggerDamage` is the part of THIS pellet's damage the game proved it staggered with; it travels with the
    // pellet so a merged number can show the triggering subset instead of guessing from the merged total.
    internal PelletCaptureDecision Capture(long enemyIdentity, float x, float y, float z, float damage, HitKind kind,
        uint gearCategoryId, DamageOrigin origin, ulong shooterLookup, bool nestedSameShot, float now, int frame,
        float staggerDamage = 0f)
    {
        var scope = _scope;
        if (scope == null)
            return new PelletCaptureDecision(PelletDecision.Immediate, PelletRefusal.NoVolley, false, 0u, gearCategoryId,
                origin, 0f, 0, 0, enemyIdentity != 0L, nestedSameShot, 0u, ShooterState(null, shooterLookup));

        float age = now - scope.StartTime;
        int frameDelta = frame > 0 ? frame - scope.FireFrame : 0;
        bool targetKnown = enemyIdentity != 0L;
        PelletShooter shooter = ShooterState(scope, shooterLookup);
        // One predicate for "may this hit be a pellet of THIS volley": the capture window (never the 0.5 s volley
        // lifetime), the path proof (Bullet), the same-shot proof (delivered inside this shot's Fire), the shooter, and
        // the category (unknown, the volley's own, or the pellet category this volley already took are accepted; a KNOWN
        // unrelated one is not).
        if (!FeedbackPolicy.IsPelletCaptureWindow(scope.StartTime, now, scope.Drained))
            return Refuse(PelletRefusal.Window, scope, gearCategoryId, origin, age, frameDelta, targetKnown, nestedSameShot, shooter);
        if (!FeedbackPolicy.IsVolleyHit(true, scope.GearCategoryId, scope.PelletCategoryId, gearCategoryId, origin,
                nestedSameShot, scope.ShooterLookup, shooterLookup))
            return Refuse(origin != DamageOrigin.Bullet ? PelletRefusal.Origin
                    : shooter == PelletShooter.Foreign ? PelletRefusal.Shooter
                    : PelletRefusal.Category,
                scope, gearCategoryId, origin, age, frameDelta, targetKnown, nestedSameShot, shooter);
        if (FeedbackPolicy.ShouldSuppressShotgunDamageNumber(true, false, scope.ShowNumbers))
            return new PelletCaptureDecision(PelletDecision.Suppressed, PelletRefusal.Hidden, true, scope.GearCategoryId,
                gearCategoryId, origin, age, frameDelta, scope.PelletHits, targetKnown, nestedSameShot,
                scope.PelletCategoryId, shooter);
        if (!FeedbackPolicy.ShouldMergeShotgunDamageNumbers(true, scope.MergePellets))
            return Refuse(PelletRefusal.NotMerging, scope, gearCategoryId, origin, age, frameDelta, targetKnown, nestedSameShot, shooter);
        if (!scope.Batch.Add(enemyIdentity, x, y, z, damage, kind, staggerDamage))
            return Refuse(!targetKnown ? PelletRefusal.Target : PelletRefusal.Value,
                scope, gearCategoryId, origin, age, frameDelta, targetKnown, nestedSameShot, shooter);
        // 1.2.19 Survival retest: the volley remembers the hit-side category its OWN shot delivered. It can only be
        // learned from a hit that was either delivered inside this shot's Fire call (the same-shot proof) or that already
        // equals the weapon's own category, so a foreign category can never be learned here. The value is never written
        // into ShotgunCategoryRegistry: it dies with this volley.
        if (scope.PelletCategoryId == 0u && gearCategoryId != 0u
            && (nestedSameShot || gearCategoryId == scope.GearCategoryId))
            scope.PelletCategoryId = gearCategoryId;
        if (nestedSameShot) scope.NestedHits++;
        scope.NotePellet(now);
        return new PelletCaptureDecision(PelletDecision.Merged, PelletRefusal.None, true, scope.GearCategoryId,
            gearCategoryId, origin, age, frameDelta, scope.PelletHits, targetKnown, nestedSameShot,
            scope.PelletCategoryId, shooter);
    }

    // Read-only: what this volley WOULD say about the hit, without adding a pellet, learning a category or ending
    // anything. Used only for the classifier receipt, the refusal the Survival log could not show at all. It reports
    // nothing when no capture window is open, so an ordinary weapon's hit is never logged. The refusal keeps the most
    // specific fact the volley can prove: a foreign SHOOTER is named as such (the classifier refused it for that
    // reason), everything else stays `not-classified` with `hitCat=`/`learned=`/`nested=` on the same line.
    internal PelletCaptureDecision Describe(long enemyIdentity, uint hitCategoryId, DamageOrigin origin, ulong shooterLookup,
        bool nestedSameShot, float now, int frame)
    {
        var scope = _scope;
        if (scope == null || !FeedbackPolicy.IsPelletCaptureWindow(scope.StartTime, now, scope.Drained))
            return new PelletCaptureDecision(PelletDecision.Immediate, PelletRefusal.None, false, 0u, hitCategoryId,
                origin, 0f, 0, 0, enemyIdentity != 0L);
        PelletShooter shooter = ShooterState(scope, shooterLookup);
        PelletRefusal refusal = shooter == PelletShooter.Foreign ? PelletRefusal.Shooter : PelletRefusal.NotClassified;
        return new PelletCaptureDecision(PelletDecision.Immediate, refusal, true, scope.GearCategoryId,
            hitCategoryId, origin, now - scope.StartTime, frame > 0 ? frame - scope.FireFrame : 0, scope.PelletHits,
            enemyIdentity != 0L, nestedSameShot, scope.PelletCategoryId, shooter);
    }

    // Only a hit that NAMES a different local player than the volley is foreign; an unreadable identity on either side
    // proves nothing and must never refuse a pellet.
    private static PelletShooter ShooterState(ShotgunDamageNumberScope scope, ulong hitShooterLookup)
    {
        if (scope == null || scope.ShooterLookup == 0UL || hitShooterLookup == 0UL) return PelletShooter.Unknown;
        return scope.ShooterLookup == hitShooterLookup ? PelletShooter.Same : PelletShooter.Foreign;
    }

    private static PelletCaptureDecision Refuse(PelletRefusal refusal, ShotgunDamageNumberScope scope, uint hitCategory,
        DamageOrigin origin, float age, int frameDelta, bool targetKnown, bool nestedSameShot, PelletShooter shooter)
        => new(PelletDecision.Immediate, refusal, true, scope.GearCategoryId, hitCategory, origin, age, frameDelta,
            scope.PelletHits, targetKnown, nestedSameShot, scope.PelletCategoryId, shooter);

    // F3 (1.2.19 review): the declared pellet count is a diagnostic field, never an end condition - see
    // FeedbackPolicy.ShouldDrainVolley. Only silence and the hard cap end a volley. The silence has to hold for
    // VolleyQuietTicks consecutive end checks (one per frame) so a single long frame cannot split one shot.
    internal bool ShouldEnd(float now)
    {
        var scope = _scope;
        if (scope == null) return false;
        if (FeedbackPolicy.SilenceElapsed(scope.PelletHits, scope.LastPelletAt, now)) scope.QuietTicks++;
        else scope.QuietTicks = 0;
        return FeedbackPolicy.ShouldDrainVolley(
            scope.PelletHits, scope.StartTime, scope.LastPelletAt, now, scope.Drained, scope.QuietTicks);
    }

    // 1.2.19 review R3: the WHOLE Fire ordering (attributability gate -> settle a foreign volley -> same-shot check ->
    // settle the previous volley -> push the fire scope -> adopt) is production code here, not a fixture copy. The Unity
    // adapter only reads the live facts (weapon handle, frame, clock, switches, expected pellets) and draws whatever this
    // settled. 1.2.19 review R6: `Unattributable` means the Fire cannot be tied to a weapon instance, so nothing is opened
    // (an ownerless volley blinds both identity predicates) and every hit keeps its own immediate number.
    internal ShotOpenResult BeginShot(long owner, int frame, float now, uint category, int expectedPellets, bool show,
        bool merge, VolleyEntry entry, bool opensVolley, ulong shooterLookup, ShotgunDamageNumberScopeStack stack,
        out ShotgunDamageNumberScope opened, out ShotgunDamageNumberScope settled, out int settledFrames)
    {
        opened = null;
        settled = null;
        settledFrames = 0;
        if (!CanOpenVolley(owner)) { settled = End(frame, out settledFrames); return ShotOpenResult.Unattributable; }
        if (!IsForeignShot(owner) && IsSameShot(owner, frame)) return ShotOpenResult.SameShot;
        settled = End(frame, out settledFrames);
        // A single-pellet weapon never opens a volley (BeginMultiPelletFire); it still settles whatever was open, which is
        // what keeps a rifle's Fire from leaving the shotgun window alive.
        if (!opensVolley) return ShotOpenResult.SettledOnly;
        opened = stack.Begin(show, merge, frame, category, now, expectedPellets, owner, entry, shooterLookup);
        Adopt(opened);
        return ShotOpenResult.Opened;
    }

    // Hands the finished volley over (or null) and takes ownership away first, so a volley can never be ended twice and
    // no late pellet can land in it afterwards. The caller owns the drawing.
    internal ShotgunDamageNumberScope End(int currentFrame, out int frames)
    {
        var scope = _scope;
        frames = 0;
        if (scope == null) return null;
        _scope = null;
        scope.Drained = true;
        frames = scope.FrameSpan(currentFrame);
        return scope;
    }
    // 1.2.19 review R6: can a Fire be attributed to a weapon instance at all? A zero handle blinds BOTH identity predicates
    // (IsSameShot never matches, IsForeignVolleyOwner never fires for an ownerless volley), so the capture must never open
    // one. This is the predicate the Unity adapter asks before it opens anything, and the smoke suite proves it.
    internal static bool CanOpenVolley(long ownerHandle) => ownerHandle != 0L;

    internal void Clear() => _scope = null;
}
