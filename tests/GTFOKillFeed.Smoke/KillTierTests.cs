using System;
using HawChow.GTFOKillFeed;

// Kill-tier split: MINOR below KillIcon/MinorHealthThreshold, BOSS at/above the EXISTING
// KillIcon/HeavyHealthThreshold line, ELITE in between. The heavy line is the single authority for BOSS,
// so a legacy cfg keeps its own line, the heavy icon and the BOSS tier can never disagree, and a heavy
// line at or below the minor line leaves the ELITE band empty (documented, never silently "fixed").
internal static class KillTierTests
{
    internal static void Run(Action<bool, string> check)
    {
        // ---- the shipped ladder -------------------------------------------------------------
        check(KillPolicy.ClassifyTier(0f) == KillTier.Minor
            && KillPolicy.ClassifyTier(119f) == KillTier.Minor
            && KillPolicy.ClassifyTier(120f) == KillTier.Elite
            && KillPolicy.ClassifyTier(999f) == KillTier.Elite
            && KillPolicy.ClassifyTier(1000f) == KillTier.Boss,
            "kill-tier: the default ladder is <120 MINOR, 120-<1000 ELITE, >=1000 BOSS");
        check(KillPolicy.ClassifyTier(119.9999f) == KillTier.Minor
            && KillPolicy.ClassifyTier(120.0001f) == KillTier.Elite
            && KillPolicy.ClassifyTier(999.9999f) == KillTier.Elite
            && KillPolicy.ClassifyTier(1000.0001f) == KillTier.Boss,
            "kill-tier: float neighbours on both sides of 120 and 1000 stay on their side of the line");
        check(KillPolicy.ClassifyTier(1000000f) == KillTier.Boss && KillPolicy.ClassifyTier(1f) == KillTier.Minor,
            "kill-tier: very large and very small health values stay inside the ladder");
        check(KillPolicy.ClassifyTier(float.NaN) == KillTier.Minor
            && KillPolicy.ClassifyTier(float.PositiveInfinity) == KillTier.Minor
            && KillPolicy.ClassifyTier(float.NegativeInfinity) == KillTier.Minor
            && KillPolicy.ClassifyTier(-5f) == KillTier.Minor,
            "kill-tier: an unreadable or negative health pool falls back to MINOR instead of throwing");

        // ---- the BOSS line is the existing heavy key ----------------------------------------
        check(Math.Abs(KillPolicy.DefaultHeavyHealthThreshold - 1000f) < 0.0001f,
            "kill-tier: the BOSS line defaults to the existing KillIcon/HeavyHealthThreshold default (1000)");
        KillTierBoundaries defaults = KillPolicy.ResolveTierBoundaries(120f, 1000f);
        check(Math.Abs(defaults.Minor - 120f) < 0.0001f && Math.Abs(defaults.Boss - 1000f) < 0.0001f
            && !defaults.EliteBandEmpty,
            "kill-tier: resolving 120 / 1000 gives a non-empty ELITE band");

        // ---- the legacy cfg that only knows the heavy line ----------------------------------
        // HeavyHealthThreshold = 100 with no new keys: 100 HP and up is BOSS, exactly as the icon always
        // behaved. The ELITE band is empty because 120 > 100, and the heavy value is NOT moved.
        float legacyHeavy = KillPolicy.HeavyHealthThreshold(100f);
        KillTierBoundaries legacy = KillPolicy.ResolveTierBoundaries(KillDefaults.MinorHealthThreshold, legacyHeavy);
        check(Math.Abs(legacyHeavy - 100f) < 0.0001f && Math.Abs(legacy.Boss - 100f) < 0.0001f
            && Math.Abs(legacy.Minor - 120f) < 0.0001f && legacy.EliteBandEmpty,
            "kill-tier: a legacy heavy line of 100 stays 100 and becomes the BOSS line (elite band empty)");
        check(KillPolicy.ClassifyTier(99f, legacy) == KillTier.Minor
            && KillPolicy.ClassifyTier(100f, legacy) == KillTier.Boss
            && KillPolicy.ClassifyTier(110f, legacy) == KillTier.Boss
            && KillPolicy.ClassifyTier(999f, legacy) == KillTier.Boss
            && KillPolicy.ClassifyTier(1000f, legacy) == KillTier.Boss,
            "kill-tier: HP 99 is MINOR and HP 100 is BOSS under the legacy heavy line, whatever the minor line says");
        bool heavyMatchesBoss = true;
        foreach (float hp in new[] { 0f, 1f, 50f, 99f, 99.9999f, 100f, 100.0001f, 110f, 400f, 1000f, float.NaN })
            heavyMatchesBoss &= KillPolicy.IsHeavy(hp, legacyHeavy) == (KillPolicy.ClassifyTier(hp, legacy) == KillTier.Boss);
        check(heavyMatchesBoss,
            "kill-tier: the heavy icon predicate and the BOSS tier are the same predicate at every sampled health value");
        bool eliteUnreachable = true;
        for (float hp = 0f; hp <= 1200f; hp += 0.5f)
            if (KillPolicy.ClassifyTier(hp, legacy) == KillTier.Elite) eliteUnreachable = false;
        check(eliteUnreachable,
            "kill-tier: with a 100 heavy line the ELITE band really is empty - no health value reports ELITE");

        // ---- a configured heavy line at 400, then back to the default 1000 ------------------
        KillTierBoundaries heavy400 = KillPolicy.ResolveTierBoundaries(120f, 400f);
        check(KillPolicy.ClassifyTier(399f, heavy400) == KillTier.Elite
            && KillPolicy.ClassifyTier(400f, heavy400) == KillTier.Boss
            && KillPolicy.ClassifyTier(399.9999f, heavy400) == KillTier.Elite,
            "kill-tier: a heavy line of 400 moves BOSS to 400 and keeps ELITE just below it");
        KillTierBoundaries backToDefault = KillPolicy.ResolveTierBoundaries(120f, 1000f);
        check(KillPolicy.ClassifyTier(400f, backToDefault) == KillTier.Elite
            && KillPolicy.ClassifyTier(1000f, backToDefault) == KillTier.Boss,
            "kill-tier: switching the heavy line back to 1000 restores BOSS at 1000 (nothing is cached)");

        // ---- boundaries resolution ----------------------------------------------------------
        KillTierBoundaries custom = KillPolicy.ResolveTierBoundaries(80f, 200f);
        check(KillPolicy.ClassifyTier(79f, custom) == KillTier.Minor
            && KillPolicy.ClassifyTier(80f, custom) == KillTier.Elite
            && KillPolicy.ClassifyTier(199f, custom) == KillTier.Elite
            && KillPolicy.ClassifyTier(200f, custom) == KillTier.Boss,
            "kill-tier: configured boundaries 80 / 200 move every tier line");
        KillTierBoundaries badMinor = KillPolicy.ResolveTierBoundaries(float.NaN, 1000f);
        KillTierBoundaries badBoss = KillPolicy.ResolveTierBoundaries(120f, float.PositiveInfinity);
        check(Math.Abs(badMinor.Minor - 120f) < 0.0001f && Math.Abs(badMinor.Boss - 1000f) < 0.0001f
            && Math.Abs(badBoss.Boss - 1000f) < 0.0001f,
            "kill-tier: NaN/Infinity fall back per slot (minor -> 120, heavy-line -> 1000)");
        KillTierBoundaries zeroMinor = KillPolicy.ResolveTierBoundaries(0f, 1000f);
        KillTierBoundaries zeroHeavy = KillPolicy.ResolveTierBoundaries(120f, 0f);
        check(Math.Abs(zeroMinor.Minor - 120f) < 0.0001f && Math.Abs(zeroMinor.Boss - 1000f) < 0.0001f
            && Math.Abs(zeroHeavy.Boss - KillPolicy.MinHeavyHealthThreshold) < 0.0001f
            && KillPolicy.ClassifyTier(0f, zeroHeavy) == KillTier.Minor
            && KillPolicy.ClassifyTier(1f, zeroHeavy) == KillTier.Boss,
            "kill-tier: a non-positive heavy value keeps the EXISTING heavy clamping (1) instead of inventing a new line, so HP 1 is BOSS");
        KillTierBoundaries huge = KillPolicy.ResolveTierBoundaries(5e12f, 6e12f);
        check(Math.Abs(huge.Minor - KillPolicy.MaxTierHealthThreshold) < 1f
            && Math.Abs(huge.Boss - KillPolicy.MaxTierHealthThreshold) < 1f
            && huge.EliteBandEmpty
            && KillPolicy.ClassifyTier(KillPolicy.MaxTierHealthThreshold, huge) == KillTier.Boss
            && KillPolicy.ClassifyTier(999f, huge) == KillTier.Minor,
            "kill-tier: both lines at the ceiling clamp to 1e9, BOSS is still reachable and the elite band is reported empty");

        // ---- minor >= heavy: deterministic, heavy value untouched ---------------------------
        KillTierBoundaries crossed = KillPolicy.ResolveTierBoundaries(500f, 300f);
        check(Math.Abs(crossed.Boss - 300f) < 0.0001f && Math.Abs(crossed.Minor - 500f) < 0.0001f
            && crossed.EliteBandEmpty
            && KillPolicy.ClassifyTier(299f, crossed) == KillTier.Minor
            && KillPolicy.ClassifyTier(300f, crossed) == KillTier.Boss
            && KillPolicy.ClassifyTier(400f, crossed) == KillTier.Boss
            && KillPolicy.ClassifyTier(500f, crossed) == KillTier.Boss,
            "kill-tier: a minor line above the heavy line keeps the heavy value, reports an empty elite band and stays deterministic");
        KillTierBoundaries equal = KillPolicy.ResolveTierBoundaries(300f, 300f);
        check(Math.Abs(equal.Boss - 300f) < 0.0001f && equal.EliteBandEmpty
            && KillPolicy.ClassifyTier(299.9999f, equal) == KillTier.Minor
            && KillPolicy.ClassifyTier(300f, equal) == KillTier.Boss,
            "kill-tier: equal lines behave the same way (Boss wins at the line, Minor just below)");

        // ---- flight identity ---------------------------------------------------------------
        check(KillPresentationPolicy.ClassifyFlight(false, false) == KillFlight.None
            && KillPresentationPolicy.ClassifyFlight(false, true) == KillFlight.None,
            "kill-flight: a ground enemy is never reported as a flyer, even with armor limbs");
        check(KillPresentationPolicy.ClassifyFlight(true, false) == KillFlight.Flying
            && KillPresentationPolicy.ClassifyFlight(true, true) == KillFlight.ArmoredFlying,
            "kill-flight: IsFlyer decides the flight and the enemy's own armor limbs decide the armored variant");

        // ---- one presentation per kill ------------------------------------------------------
        KillTierBoundaries tierDefaults = KillPolicy.ResolveTierBoundaries(KillDefaults.MinorHealthThreshold, KillPolicy.DefaultHeavyHealthThreshold);
        KillPresentation bigEye = KillPresentationPolicy.Build(KillKind.Normal, 400f, tierDefaults, true, false);
        KillPresentation armoredBigEye = KillPresentationPolicy.Build(KillKind.Weakspot, 400f, tierDefaults, true, true);
        KillPresentation ground = KillPresentationPolicy.Build(KillKind.Normal, 400f, tierDefaults, false, true);
        check(bigEye.Tier == KillTier.Elite && bigEye.Flight == KillFlight.Flying && bigEye.Kind == KillKind.Normal
            && armoredBigEye.Tier == KillTier.Elite && armoredBigEye.Flight == KillFlight.ArmoredFlying
            && armoredBigEye.Kind == KillKind.Weakspot
            && ground.Flight == KillFlight.None,
            "kill-presentation: the same health tier carries different flight identities, and the kind rides along");
        check(KillPresentationPolicy.Build(KillKind.Normal, 119f, tierDefaults, true, false).Tier == KillTier.Minor
            && KillPresentationPolicy.Build(KillKind.Normal, 120f, tierDefaults, true, false).Tier == KillTier.Elite
            && KillPresentationPolicy.Build(KillKind.Normal, 999f, tierDefaults, true, false).Tier == KillTier.Elite
            && KillPresentationPolicy.Build(KillKind.Normal, 1000f, tierDefaults, true, false).Tier == KillTier.Boss,
            "kill-presentation: the flight variant never changes the tier the cue and the card report");
        KillPresentation legacyPresentation = KillPresentationPolicy.Build(KillKind.Normal, 100f, legacy, false, false);
        check(legacyPresentation.Tier == KillTier.Boss,
            "kill-presentation: the legacy heavy line drives the tier the cue and the card receive (HP 100 -> BOSS)");
        check(KillPresentationPolicy.Build(KillKind.Normal, float.NaN, tierDefaults, false, false).Tier == KillTier.Minor
            && KillPresentationPolicy.Build(KillKind.Normal, float.NaN, tierDefaults, true, true).Flight == KillFlight.ArmoredFlying,
            "kill-presentation: an unreadable health pool still yields a usable record");

        // ---- the card carries the same single record ---------------------------------------
        var card = new KillCard(KillKind.Weakspot, armoredBigEye.Tier, "装甲大眼", 12f, 400f, 1f, 999f, 45u, armoredBigEye.Flight);
        var repainted = card.WithPresentation("Armored Big Eye", 120f);
        check(card.Tier == KillTier.Elite && card.Flight == KillFlight.ArmoredFlying && card.EnemyDataId == 45u
            && repainted.Tier == KillTier.Elite && repainted.Flight == KillFlight.ArmoredFlying,
            "kill-presentation: the card carries the tier and flight, so a later HUD reads one record instead of re-deriving it");
        check(new KillCard(KillKind.Normal, KillTier.Minor, "x", 1f, 10f, 0f).Flight == KillFlight.None,
            "kill-presentation: a card built without flight information defaults to a ground kill");
    }
}
