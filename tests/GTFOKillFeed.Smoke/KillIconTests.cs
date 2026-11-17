using System;
using System.Collections.Generic;
using HawChow.GTFOKillFeed;

// Head-top kill icons: the display switch (Head default / Card), the tier -> BFV icon family, the flight
// badge, the anchor clearance above the enemy bar, and the size/lifetime/count clamps. The tier used here is
// the very same KillPresentation.Tier the kill cue consumes, so icon and sound can never disagree.
internal static class KillIconTests
{
    internal static void Run(Action<bool, string> check)
    {
        // ---- display switch ---------------------------------------------------------------
        check(KillIconPolicy.ParseDisplay(null) == KillIconDisplay.Head
            && KillIconPolicy.ParseDisplay("") == KillIconDisplay.Head
            && KillIconPolicy.ParseDisplay("   ") == KillIconDisplay.Head
            && KillIconPolicy.DefaultDisplay == KillIconDisplay.Head,
            "kill-icon: the head-top icons are the default, and a missing value falls back to them");
        check(KillIconPolicy.ParseDisplay("Head") == KillIconDisplay.Head
            && KillIconPolicy.ParseDisplay("head") == KillIconDisplay.Head
            && KillIconPolicy.ParseDisplay(" Head ") == KillIconDisplay.Head,
            "kill-icon: the Head token is case/whitespace tolerant");
        check(KillIconPolicy.ParseDisplay("Card") == KillIconDisplay.Card
            && KillIconPolicy.ParseDisplay("card") == KillIconDisplay.Card
            && KillIconPolicy.ParseDisplay("Cards") == KillIconDisplay.Card,
            "kill-icon: Card (and the plural alias) selects the previous kill cards");
        check(KillIconPolicy.ParseDisplay("bogus") == KillIconDisplay.Head,
            "kill-icon: an unknown display mode falls back to Head instead of drawing nothing");
        check(KillIconPolicy.UsesHeadIcons(KillIconDisplay.Head) && !KillIconPolicy.UsesHeadIcons(KillIconDisplay.Card),
            "kill-icon: exactly one presentation is active at a time");
        check(KillIconPolicy.ShouldDrawIcons(true, KillIconDisplay.Head)
            && !KillIconPolicy.ShouldDrawIcons(false, KillIconDisplay.Head)
            && !KillIconPolicy.ShouldDrawIcons(true, KillIconDisplay.Card),
            "kill-icon: F7 visibility and the Head mode both gate the head icons");

        // ---- tier -> BFV icon family (119/120/999/1000 and the hot-reloaded lines) ----------
        KillTierBoundaries defaults = KillPolicy.ResolveTierBoundaries(KillDefaults.MinorHealthThreshold, KillPolicy.DefaultHeavyHealthThreshold);
        check(KillIconPolicy.TierIconKey(KillPolicy.ClassifyTier(119f, defaults)) == "kill-normal"
            && KillIconPolicy.TierIconKey(KillPolicy.ClassifyTier(120f, defaults)) == "kill-weakspot"
            && KillIconPolicy.TierIconKey(KillPolicy.ClassifyTier(999f, defaults)) == "kill-weakspot"
            && KillIconPolicy.TierIconKey(KillPolicy.ClassifyTier(1000f, defaults)) == "kill-heavy",
            "kill-icon: 119 -> light, 120/999 -> medium, 1000 -> heavy (BFV kill.light/medium/heavy)");
        check(KillIconPolicy.TierIconFile(KillTier.Minor) == "kill-normal.png"
            && KillIconPolicy.TierIconFile(KillTier.Elite) == "kill-weakspot.png"
            && KillIconPolicy.TierIconFile(KillTier.Boss) == "kill-heavy.png",
            "kill-icon: the three tiers map onto the three shipped BFV rasters");
        KillTierBoundaries moved = KillPolicy.ResolveTierBoundaries(300f, 400f);
        check(KillIconPolicy.TierIconKey(KillPolicy.ClassifyTier(299f, moved)) == "kill-normal"
            && KillIconPolicy.TierIconKey(KillPolicy.ClassifyTier(300f, moved)) == "kill-weakspot"
            && KillIconPolicy.TierIconKey(KillPolicy.ClassifyTier(400f, moved)) == "kill-heavy",
            "kill-icon: an edited minor/BOSS line moves the icon breakpoints with the tier lines");
        check(KillIconPolicy.TierIconKey(KillPolicy.ClassifyTier(float.NaN, defaults)) == "kill-normal"
            && KillIconPolicy.TierIconKey(KillPolicy.ClassifyTier(-5f, defaults)) == "kill-normal",
            "kill-icon: an unreadable or negative health pool still gets the light icon, never a broken lookup");

        // ---- flight badge: shape first, colour second --------------------------------------
        check(KillIconPolicy.MarkFor(KillFlight.None) == FlightMark.None
            && KillIconPolicy.MarkFor(KillFlight.Flying) == FlightMark.Wings
            && KillIconPolicy.MarkFor(KillFlight.ArmoredFlying) == FlightMark.ArmorPlate,
            "kill-icon: ground kills carry no badge, flyers a wing chevron, armoured flyers an armour plate");
        var shapes = new HashSet<FlightMark>
        {
            KillIconPolicy.MarkFor(KillFlight.None),
            KillIconPolicy.MarkFor(KillFlight.Flying),
            KillIconPolicy.MarkFor(KillFlight.ArmoredFlying),
        };
        check(shapes.Count == 3,
            "kill-icon: the three flight states differ in SHAPE, not only in colour");
        check(KillIconPolicy.DefaultWingsColor != KillIconPolicy.DefaultArmorPlateColor,
            "kill-icon: the two badges also differ in colour (redundant encoding)");
        // The same tier must stay distinguishable across all three flight states.
        bool sameTierDistinct = true;
        foreach (KillTier tier in new[] { KillTier.Minor, KillTier.Elite, KillTier.Boss })
        {
            var combos = new HashSet<string>();
            foreach (KillFlight flight in new[] { KillFlight.None, KillFlight.Flying, KillFlight.ArmoredFlying })
                combos.Add(KillIconPolicy.TierIconKey(tier) + "|" + KillIconPolicy.MarkFor(flight));
            sameTierDistinct &= combos.Count == 3;
        }
        check(sameTierDistinct,
            "kill-icon: at every tier the ground/flying/armoured-flying variants are three distinct visuals (icon + badge)");

        // ---- icon and cue consume the same tier/flight -------------------------------------
        bool consistent = true;
        foreach (KillTier tier in new[] { KillTier.Minor, KillTier.Elite, KillTier.Boss })
        {
            string expected = tier == KillTier.Minor ? "minor" : tier == KillTier.Elite ? "elite" : "boss";
            string icon = KillIconPolicy.TierIconKey(tier);
            foreach (KillFlight flight in new[] { KillFlight.None, KillFlight.Flying, KillFlight.ArmoredFlying })
            {
                string cue = HitSoundPolicy.ResourceName(HitSoundStyle.Bf5, HitCueKind.Kill, tier, flight);
                consistent &= cue.Contains("-" + expected) && icon == (tier == KillTier.Minor ? "kill-normal" : tier == KillTier.Elite ? "kill-weakspot" : "kill-heavy");
                string flightToken = HitSoundPolicy.FlightToken(flight);
                if (flightToken.Length > 0) consistent &= cue.Contains(flightToken);
            }
        }
        check(consistent,
            "kill-icon: for all nine tier/flight combinations the icon family and the kill cue name the same tier and flight");

        // ---- clamps ------------------------------------------------------------------------
        check(Math.Abs(KillIconPolicy.Lifetime(float.NaN) - KillIconPolicy.DefaultLifetimeSeconds) < 0.0001f
            && Math.Abs(KillIconPolicy.Lifetime(0.01f) - KillIconPolicy.MinLifetimeSeconds) < 0.0001f
            && Math.Abs(KillIconPolicy.Lifetime(99f) - KillIconPolicy.MaxLifetimeSeconds) < 0.0001f,
            "kill-icon: the icon lifetime clamps to 0.4-6 s and NaN falls back to 2.2");
        check(KillIconPolicy.MaxIcons(0) == KillIconPolicy.DefaultMaxIcons
            && KillIconPolicy.MaxIcons(-4) == KillIconPolicy.DefaultMaxIcons
            && KillIconPolicy.MaxIcons(99) == KillIconPolicy.MaxIconsCeiling
            && KillIconPolicy.MaxIcons(3) == 3,
            "kill-icon: the simultaneous-icon cap clamps to 1-12 and never grows without bound");
        check(Math.Abs(KillIconPolicy.Gap(float.NaN) - KillIconPolicy.DefaultGapMetres) < 0.0001f
            && Math.Abs(KillIconPolicy.Gap(-1f) - KillIconPolicy.MinGapMetres) < 0.0001f
            && Math.Abs(KillIconPolicy.Gap(9f) - KillIconPolicy.MaxGapMetres) < 0.0001f,
            "kill-icon: the head clearance clamps to 0-2 m and NaN falls back to 0.3");
        check(Math.Abs(KillIconPolicy.Size(1f) - KillIconPolicy.MinIconSize) < 0.0001f
            && Math.Abs(KillIconPolicy.Size(999f) - KillIconPolicy.MaxIconSize) < 0.0001f
            && Math.Abs(KillIconPolicy.Size(46f) - 46f) < 0.0001f,
            "kill-icon: the icon size reuses the existing IconSize clamp (26-76 px)");

        // ---- anchor clearance above the enemy bar -----------------------------------------
        float gap = KillIconPolicy.Gap(0.30f);
        check(Math.Abs(KillIconPolicy.HeadAnchorOffset(true, true, 0.12f, gap) - (0.12f + gap)) < 0.0001f,
            "kill-icon: with the bar in Head mode the icon clears the bar's own head offset plus the gap");
        check(Math.Abs(KillIconPolicy.HeadAnchorOffset(true, false, 0.12f, gap) - gap) < 0.0001f,
            "kill-icon: a Screen-mode bar does not push the icon anywhere - it stays above the head");
        check(Math.Abs(KillIconPolicy.HeadAnchorOffset(false, true, 0.5f, gap) - gap) < 0.0001f,
            "kill-icon: a disabled bar contributes no offset; the icon is still anchored on the head");
        check(Math.Abs(KillIconPolicy.HeadAnchorOffset(true, true, float.NaN, 0.5f) - (BarPolicy.DefaultHeadOffsetMetres + 0.5f)) < 0.0001f,
            "kill-icon: a broken bar offset falls back to the bar's own default instead of NaN");

        // ---- fade / follow ----------------------------------------------------------------
        check(KillIconPolicy.Alpha(0f, 2.2f) == 0f
            && KillIconPolicy.Alpha(KillIconPolicy.FadeInSeconds * 0.5f, 2.2f) > 0.3f
            && Math.Abs(KillIconPolicy.Alpha(1f, 2.2f) - 1f) < 0.0001f
            && KillIconPolicy.Alpha(2.2f, 2.2f) == 0f,
            "kill-icon: the icon fades in quickly, holds, and is gone exactly at the end of its lifetime");
        check(KillIconPolicy.Alpha(float.NaN, 2.2f) == 0f && KillIconPolicy.Alpha(-0.5f, 2.2f) == 0f,
            "kill-icon: a broken or negative age never draws");
        // 1.2.16: there is no follow window any more. The anchor is frozen at the credit and only the fade
        // curve is time driven, so the icon's alpha is the single thing that changes with age.
        check(KillIconPolicy.Alpha(1.0f, 2.2f) > 0.99f && KillIconPolicy.Alpha(2.19f, 2.2f) < 0.05f,
            "kill-icon: age only fades the icon (no follow window, no per-frame anchor re-read)");
        check(KillIconPolicy.BadgeSize(46f) > 0f && KillIconPolicy.BadgeSize(46f) < 46f
            && KillIconPolicy.BadgeGap(46f) > 0f,
            "kill-icon: the flight badge is a small annotation inside the icon block, not a second icon");
        // Review I2: the badge size is proportional to the FINAL icon pixels. It used to be re-clamped
        // to 26-76 px, which shrank a 4K icon's badge by ~37% and broke the card/head proportion.
        check(Math.Abs(KillIconPolicy.BadgeSize(121.6f) - 121.6f * 0.30f) < 0.001f
            && Math.Abs(KillIconPolicy.BadgeGap(121.6f) - 121.6f * 0.10f) < 0.001f,
            "kill-icon: at 4K (121.6 px icon) the flight badge keeps its proportion instead of being re-clamped to 76 px");
        check(Math.Abs(KillIconPolicy.BadgeSize(float.NaN) - 46f * 0.30f) < 0.001f
            && KillIconPolicy.BadgeSize(0f) > 0f,
            "kill-icon: a broken or zero icon size falls back to the 46 px reference for the flight badge");

        // Review I4: in Head mode an F9 preview CARD can never be painted, so it must not be queued at all.
        check(!KillIconPolicy.ShouldQueueCardPreviews(KillIconDisplay.Head)
            && KillIconPolicy.ShouldQueueCardPreviews(KillIconDisplay.Card),
            "kill-icon: F9 only queues preview cards in Card mode (Head mode would waste a slot and could evict a real fallback card)");

        // ---- the card consumes the same record ---------------------------------------------
        var card = new KillCard(KillKind.Weakspot, KillTier.Boss, "装甲大眼", 12f, 1400f, 1f, 999f, 45u, KillFlight.ArmoredFlying);
        check(KillIconPolicy.TierIconKey(card.Tier) == "kill-heavy"
            && KillIconPolicy.MarkFor(card.Flight) == FlightMark.ArmorPlate,
            "kill-icon: card mode reads the same tier/flight record, so switching modes never changes the icon family");
    }
}
