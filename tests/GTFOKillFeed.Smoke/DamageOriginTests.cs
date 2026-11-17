using System;
using HawChow.GTFOKillFeed;

// 1.2.19 pellet path proof, asserted without Unity.
//
// The shotgun merge used to be decided by "gear category + 0.30 s window", so a melee / explosion / DoT / push hit
// landing right after a blast could still be summed into the shot - and vanished entirely when
// DamageNumbers/ShowShotgunDamageNumbers was off. The damage root now records WHICH entry produced the hit
// (DamageOrigin) and every decision asks that fact instead of guessing from a category or a clock.
//
// These fixtures drive the production predicates only: FeedbackPolicy.ResolveOrigin (the shared-root rule),
// FeedbackPolicy.IsVolleyHit (the volley membership), FeedbackPolicy.IsShotgunDamageNumber (the classifier) and
// FeedbackPolicy.ShouldDisplayDamageNumber / IsPelletCaptureWindow / IsForeignVolleyOwner (the gates that must keep
// working). Nothing here re-implements a branch.
internal static class DamageOriginTests
{
    internal static void Run(Action<bool, string> check)
    {
        // ---- 1) the vocabulary and its default ------------------------------------------------
        // Other must be 0: it is the default of DamageRoot.Origin, so a root nobody labelled can never be read as a
        // bullet. (Bullet=1, Melee=2, Fire=3, Explosion=4, Push=5 are pinned by the IL gate too.)
        check(default(DamageOrigin) == DamageOrigin.Other
            && DamageOrigin.Bullet != DamageOrigin.Other
            && DamageOrigin.Melee != DamageOrigin.Bullet
            && DamageOrigin.Fire != DamageOrigin.Melee
            && DamageOrigin.Explosion != DamageOrigin.Fire
            && DamageOrigin.Push != DamageOrigin.Explosion,
            "1.2.19: an unlabelled damage root defaults to DamageOrigin.Other (never Bullet), and the five real entries are distinct");

        // ---- 2) the shared-root rule -----------------------------------------------------------
        // ReceiveBulletDamage -> ProcessReceivedDamage is a NESTED call that reuses the same DamageRoot. The inner
        // entry is anonymous (Other) by construction, so reuse must keep the Bullet the outer entry proved - the
        // downgrade below is exactly the bug that would un-merge every pellet.
        check(FeedbackPolicy.ResolveOrigin(true, DamageOrigin.Bullet, DamageOrigin.Other, DamageOrigin.Other) == DamageOrigin.Bullet,
            "1.2.19: a reused root keeps Bullet when the inner ProcessReceivedDamage entry passes Other");
        check(FeedbackPolicy.ResolveOrigin(true, DamageOrigin.Melee, DamageOrigin.Other, DamageOrigin.Other) == DamageOrigin.Melee
            && FeedbackPolicy.ResolveOrigin(true, DamageOrigin.Fire, DamageOrigin.Other, DamageOrigin.Other) == DamageOrigin.Fire
            && FeedbackPolicy.ResolveOrigin(true, DamageOrigin.Push, DamageOrigin.Other, DamageOrigin.Other) == DamageOrigin.Push,
            "1.2.19: reuse never downgrades any known origin to Other");
        // A fresh root created by ProcessReceivedDamage with nothing enclosing it is Other - never Bullet.
        check(FeedbackPolicy.ResolveOrigin(false, DamageOrigin.Other, DamageOrigin.Other, DamageOrigin.Other) == DamageOrigin.Other,
            "1.2.19: BeginProcessedDamage creates a fresh root as Other and never guesses Bullet");
        // A fresh root adopts its own entry's origin, whatever it is.
        check(FeedbackPolicy.ResolveOrigin(false, DamageOrigin.Other, DamageOrigin.Bullet, DamageOrigin.Other) == DamageOrigin.Bullet
            && FeedbackPolicy.ResolveOrigin(false, DamageOrigin.Other, DamageOrigin.Melee, DamageOrigin.Other) == DamageOrigin.Melee
            && FeedbackPolicy.ResolveOrigin(false, DamageOrigin.Other, DamageOrigin.Fire, DamageOrigin.Other) == DamageOrigin.Fire
            && FeedbackPolicy.ResolveOrigin(false, DamageOrigin.Other, DamageOrigin.Push, DamageOrigin.Other) == DamageOrigin.Push,
            "1.2.19: a fresh root takes the origin its own entry proved (Bullet/Melee/Fire/Push)");
        // A nested anonymous entry inside a proven call inherits that proof (the bullet ancestry / fire / push hint).
        check(FeedbackPolicy.ResolveOrigin(false, DamageOrigin.Other, DamageOrigin.Other, DamageOrigin.Bullet) == DamageOrigin.Bullet
            && FeedbackPolicy.ResolveOrigin(false, DamageOrigin.Other, DamageOrigin.Other, DamageOrigin.Fire) == DamageOrigin.Fire
            && FeedbackPolicy.ResolveOrigin(false, DamageOrigin.Other, DamageOrigin.Other, DamageOrigin.Push) == DamageOrigin.Push,
            "1.2.19: an anonymous root inherits the enclosing call's proof (bullet ancestry, fire, push) instead of guessing");
        // Other may be promoted by a real entry, and a promoted root still never loses a known origin.
        check(FeedbackPolicy.ResolveOrigin(true, DamageOrigin.Other, DamageOrigin.Bullet, DamageOrigin.Other) == DamageOrigin.Bullet
            && FeedbackPolicy.ResolveOrigin(true, DamageOrigin.Other, DamageOrigin.Fire, DamageOrigin.Other) == DamageOrigin.Fire
            && FeedbackPolicy.ResolveOrigin(true, DamageOrigin.Bullet, DamageOrigin.Fire, DamageOrigin.Other) == DamageOrigin.Bullet,
            "1.2.19: reuse may promote Other, and a real entry can never overwrite another known origin");

        // ---- 3) volley membership requires a bullet --------------------------------------------
        check(FeedbackPolicy.IsVolleyHit(true, 6u, 0u, 0u, DamageOrigin.Bullet, false, 0UL, 0UL)
            && FeedbackPolicy.IsVolleyHit(true, 6u, 0u, 6u, DamageOrigin.Bullet, false, 0UL, 0UL)
            && !FeedbackPolicy.IsVolleyHit(true, 6u, 0u, 0u, DamageOrigin.Melee, false, 0UL, 0UL)
            && !FeedbackPolicy.IsVolleyHit(true, 6u, 0u, 0u, DamageOrigin.Fire, false, 0UL, 0UL)
            && !FeedbackPolicy.IsVolleyHit(true, 6u, 0u, 0u, DamageOrigin.Explosion, false, 0UL, 0UL)
            && !FeedbackPolicy.IsVolleyHit(true, 6u, 0u, 0u, DamageOrigin.Push, false, 0UL, 0UL)
            && !FeedbackPolicy.IsVolleyHit(true, 6u, 0u, 0u, DamageOrigin.Other, false, 0UL, 0UL),
            "1.2.19: inside the open window only a Bullet hit is a pellet - melee/fire/explosion/push/unknown are never merged");
        check(!FeedbackPolicy.IsVolleyHit(false, 6u, 0u, 0u, DamageOrigin.Bullet, false, 0UL, 0UL)
            && !FeedbackPolicy.IsVolleyHit(true, 6u, 0u, 45u, DamageOrigin.Bullet, false, 0UL, 0UL),
            "1.2.19: a Bullet origin still needs the open window AND a category that does not prove another weapon");

        // ---- 3b) D6 (Survival shotgun retest): the real (fire=6, hitCat=2) input, and what may NOT open the gate -----
        // The two numbers are different id spaces: the weapon's own persistentID is on the volley line (6 / 16) and the
        // damage entry reported 2 for BOTH shotguns. The old predicate compared them for equality (plus a `hit == 0`
        // wildcard that never applied), so every pellet drew its own number.
        check(!FeedbackPolicy.IsVolleyHit(true, 6u, 0u, 2u, DamageOrigin.Bullet, false, 0UL, 0UL),
            "D6: a non-nested hit whose category matches neither the volley's nor a learned pellet category is NOT a pellet (that alone would be a timing guess)");
        check(FeedbackPolicy.IsVolleyHit(true, 6u, 0u, 2u, DamageOrigin.Bullet, true, 0UL, 0UL),
            "D6: the SAME real hit (fire category=6, hitCat=2) IS a pellet when the damage is delivered inside the shot's own Fire call - the same-shot proof needs no category");
        check(FeedbackPolicy.IsVolleyHit(true, 6u, 2u, 2u, DamageOrigin.Bullet, false, 0UL, 0UL),
            "D6: after one nested hit taught this volley its pellet category (2), a later asynchronous hit of the same shot is still recognised - per volley, never globally");
        check(!FeedbackPolicy.IsVolleyHit(true, 6u, 2u, 45u, DamageOrigin.Bullet, false, 0UL, 0UL)
            && !FeedbackPolicy.IsVolleyHit(true, 6u, 2u, 45u, DamageOrigin.Bullet, true, 0UL, 0UL),
            "D6: a hit inside another weapon's KNOWN category is refused even when it is nested - a learned pellet category narrows nothing else");
        check(!FeedbackPolicy.IsVolleyHit(true, 6u, 2u, 2u, DamageOrigin.Bullet, true, 0x1111UL, 0x2222UL)
            && FeedbackPolicy.IsVolleyHit(true, 6u, 2u, 2u, DamageOrigin.Bullet, true, 0x1111UL, 0x1111UL)
            && FeedbackPolicy.IsVolleyHit(true, 6u, 2u, 2u, DamageOrigin.Bullet, true, 0UL, 0x2222UL)
            && FeedbackPolicy.IsVolleyHit(true, 6u, 2u, 2u, DamageOrigin.Bullet, true, 0x1111UL, 0UL),
            "D6: two KNOWN and different shooters are two different shots; an unreadable identity on either side proves nothing and never refuses a pellet");

        // ---- 4) the classifier: only a bullet may be hidden as a shotgun number -----------------
        check(FeedbackPolicy.IsShotgunDamageNumber(false, DamageOrigin.Bullet, true, false)
            && FeedbackPolicy.IsShotgunDamageNumber(false, DamageOrigin.Bullet, false, true)
            && !FeedbackPolicy.IsShotgunDamageNumber(false, DamageOrigin.Bullet, false, false)
            && !FeedbackPolicy.IsShotgunDamageNumber(true, DamageOrigin.Bullet, true, true),
            "1.2.19: a Bullet hit in a shotgun category or inside the open volley is a shotgun number, and the F9 preview is never one");
        check(!FeedbackPolicy.IsShotgunDamageNumber(false, DamageOrigin.Melee, true, true)
            && !FeedbackPolicy.IsShotgunDamageNumber(false, DamageOrigin.Fire, true, true)
            && !FeedbackPolicy.IsShotgunDamageNumber(false, DamageOrigin.Push, true, true)
            && !FeedbackPolicy.IsShotgunDamageNumber(false, DamageOrigin.Other, true, true),
            "1.2.19: even inside a shotgun category and an open volley, a non-bullet hit is NOT classified as a shotgun number");

        // ---- 5) ShowShotgunDamageNumbers cannot swallow a non-bullet hit ------------------------
        // The classifier runs before the visibility switch, so with the shotgun numbers hidden only the real pellets
        // disappear while a melee / explosion / DoT hit keeps its own number.
        bool showOffBullet = FeedbackPolicy.IsShotgunDamageNumber(false, DamageOrigin.Bullet, true, false);
        bool showOffMelee = FeedbackPolicy.IsShotgunDamageNumber(false, DamageOrigin.Melee, true, true);
        bool showOffDot = FeedbackPolicy.IsShotgunDamageNumber(false, DamageOrigin.Other, true, true);
        check(FeedbackPolicy.ShouldDisplayDamageNumber(true, false, showOffBullet) == false
            && FeedbackPolicy.ShouldDisplayDamageNumber(true, false, showOffMelee)
            && FeedbackPolicy.ShouldDisplayDamageNumber(true, false, showOffDot),
            "1.2.19: with ShowShotgunDamageNumbers=false the pellets are hidden while melee/DoT hits still draw their own number");

        // ---- 6) the origin gate keeps the other two gates intact -------------------------------
        // 0.30 s capture window and the fire identity are unchanged by this release: they still bound a bullet hit.
        check(FeedbackPolicy.IsPelletCaptureWindow(300f, 300.25f, false)
            && !FeedbackPolicy.IsPelletCaptureWindow(300f, 300.25f, true)
            && FeedbackPolicy.IsPelletCaptureWindow(300f, 300.25f, false) != false
            && Math.Abs(FeedbackPolicy.MaxPelletCaptureLag - 0.30f) < 0.0001f,
            "1.2.19: the 0.30 s capture window still bounds a Bullet hit (the origin gate is additional, not a relaxation)");
        check(FeedbackPolicy.IsForeignVolleyOwner(0xAA, 0xBB)
            && !FeedbackPolicy.IsForeignVolleyOwner(0xAA, 0xAA)
            && !FeedbackPolicy.IsForeignVolleyOwner(0L, 0xBB)
            && FeedbackPolicy.IsVolleyHit(
                FeedbackPolicy.IsPelletCaptureWindow(300f, 300.05f, false), 6u, 0u, 0u, DamageOrigin.Bullet, false, 0UL, 0UL),
            "1.2.19: the fire identity gate and the capture window still compose with the origin gate for a real bullet");
    }
}
