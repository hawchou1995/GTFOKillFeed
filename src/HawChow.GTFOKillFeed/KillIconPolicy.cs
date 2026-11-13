using System;

namespace HawChow.GTFOKillFeed;

// Where a credited local kill is shown: the BFV icon above the dead enemy's head (default) or the existing
// kill cards. The switch is read at draw time, so a cfg edit applies to the next frame without a restart.
internal enum KillIconDisplay
{
    Head = 0,
    Card = 1,
}

// The small badge that tells flight identity apart at the SAME tier. Shape first, colour second: a plain
// kill has no badge, a flying kill gets a wing chevron, an armoured flyer a hollow armour plate.
internal enum FlightMark
{
    None = 0,
    Wings = 1,
    ArmorPlate = 2,
}

// Pure rules for the head-top kill icons: display switch, tier -> BFV icon family, flight badge, anchor
// clearance, size/lifetime/count clamps, fade curve and projection. No UnityEngine reference, so the whole
// contract is asserted headlessly.
internal static class KillIconPolicy
{
    internal const string DisplayHead = "Head";
    internal const string DisplayCard = "Card";
    internal const KillIconDisplay DefaultDisplay = KillIconDisplay.Head;

    internal const float DefaultLifetimeSeconds = 2.2f;
    internal const float MinLifetimeSeconds = 0.4f;
    internal const float MaxLifetimeSeconds = 6f;

    internal const int DefaultMaxIcons = 6;
    internal const int MaxIconsCeiling = 12;

    // Extra world clearance above the enemy's own head point. With the bar in Head mode the icon must stay
    // clearly above the bar, so the bar's own head offset is added on top of this gap.
    internal const float DefaultGapMetres = 0.30f;
    internal const float MinGapMetres = 0f;
    internal const float MaxGapMetres = 2f;

    // Same clamp Plugin.IconSize uses (Layout/IconSize), so the head icon obeys the existing size setting.
    internal const float MinIconSize = 26f;
    internal const float MaxIconSize = 76f;

    // 1.2.16: there is NO follow window any more. The world anchor is frozen at the local lethal credit
    // (the moment the enemy is still the object that died) and is only ever reprojected; the corpse's
    // head moving down therefore cannot drag the icon with it. 1.2.15 followed the live head for the
    // first 60% of the icon's life (1.32 s at the default 2.2 s lifetime), which is exactly the slide
    // the real-machine report saw.
    internal const string AnchorSourceHead = "head-world";
    internal const string AnchorSourceLethal = "lethal-position";

    // Weakspot is never a second icon family and, since the 1.2.16 review, not a head-icon marker either: the
    // icon family comes only from the health tier (MINOR/ELITE/BOSS -> light/medium/heavy), and the killing
    // limb is expressed by the CARD TITLE only. 1.2.15 picked the card raster from the classification while the
    // head icon picked it from the tier, so one kill could show two different icons; a short-lived amber pip
    // above the icon was removed again because it duplicated the card title without adding information.

    internal const float FadeInSeconds = 0.10f;
    internal const float FadeOutSeconds = 0.35f;

    internal const string DefaultWingsColor = "#8FD3E8";       // cool cyan: movement
    internal const string DefaultArmorPlateColor = "#E6AB57";  // amber plate: armour

    internal static KillIconDisplay ParseDisplay(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return DefaultDisplay;
        string trimmed = value.Trim();
        if (string.Equals(trimmed, DisplayCard, StringComparison.OrdinalIgnoreCase)) return KillIconDisplay.Card;
        if (string.Equals(trimmed, "Cards", StringComparison.OrdinalIgnoreCase)) return KillIconDisplay.Card;
        // Head is the documented default and the fallback for anything unrecognised.
        return KillIconDisplay.Head;
    }

    internal static string DisplayToken(KillIconDisplay display)
        => display == KillIconDisplay.Card ? DisplayCard : DisplayHead;

    internal static bool UsesHeadIcons(KillIconDisplay display) => display == KillIconDisplay.Head;

    internal static float Lifetime(float configured)
        => FeedbackPolicy.Clamp(configured, MinLifetimeSeconds, MaxLifetimeSeconds, DefaultLifetimeSeconds);

    internal static int MaxIcons(int configured)
    {
        if (configured <= 0) return DefaultMaxIcons;
        return configured > MaxIconsCeiling ? MaxIconsCeiling : configured;
    }

    internal static float Gap(float configured)
        => FeedbackPolicy.Clamp(configured, MinGapMetres, MaxGapMetres, DefaultGapMetres);

    internal static float Size(float configured)
        => FeedbackPolicy.Clamp(configured, MinIconSize, MaxIconSize, 46f);

    // The offset handed to EnemyFocus.TryGetHeadWorld (which already adds its own 0.12 m head padding).
    // Head-mode bar: keep the bar's own offset AND this gap, so the icon clears the bar itself.
    internal static float HeadAnchorOffset(bool barEnabled, bool barHeadMode, float barHeadOffsetMetres, float gapMetres)
    {
        float gap = Gap(gapMetres);
        if (!barEnabled || !barHeadMode) return gap;
        float barOffset = FeedbackPolicy.Clamp(barHeadOffsetMetres, BarPolicy.MinHeadOffsetMetres, BarPolicy.MaxHeadOffsetMetres, BarPolicy.DefaultHeadOffsetMetres);
        return barOffset + gap;
    }

    // Tier -> BFV kill family. These are the three shipped rasters built from BFV kill.light/medium/heavy
    // (the file names are historic: weakspot == medium, heavy == heavy).
    internal static string TierIconKey(KillTier tier)
        => tier == KillTier.Elite ? "kill-weakspot" : tier == KillTier.Boss ? "kill-heavy" : "kill-normal";

    internal static string TierIconFile(KillTier tier) => TierIconKey(tier) + ".png";

    internal static FlightMark MarkFor(KillFlight flight)
        => flight == KillFlight.ArmoredFlying ? FlightMark.ArmorPlate
            : flight == KillFlight.Flying ? FlightMark.Wings
            : FlightMark.None;

    // Review I2: the badge/mark sizes are derived from the FINAL icon pixels, so they must not run through the
    // 26-76 px cfg clamp again - at 4K (icon = 76 x 1.6 = 121.6 px) that clamp shrank the badge by ~37% and the
    // proportions no longer matched the card. Positive-guard only, with a generous absurd-value ceiling.
    internal static float PixelSize(float iconSize)
        => KillPolicy.IsFinite(iconSize) && iconSize > 0f ? Math.Min(iconSize, MaxIconSize * 2f) : 46f;
    internal static float BadgeSize(float iconSize) => PixelSize(iconSize) * 0.30f;
    internal static float BadgeGap(float iconSize) => PixelSize(iconSize) * 0.10f;

    // Quick pop-in, hold, then fade out. A broken age never draws.
    internal static float Alpha(float age, float lifetime)
    {
        if (float.IsNaN(age) || float.IsInfinity(age)) return 0f;
        float window = Lifetime(lifetime);
        if (age < 0f || age >= window) return 0f;
        float fadeIn = Math.Min(FadeInSeconds, window * 0.25f);
        float fadeOut = Math.Min(FadeOutSeconds, window * 0.5f);
        float alpha = 1f;
        if (fadeIn > 0f && age < fadeIn) alpha = age / fadeIn;
        float remaining = window - age;
        if (fadeOut > 0f && remaining < fadeOut) alpha = Math.Min(alpha, remaining / fadeOut);
        return alpha < 0f ? 0f : alpha > 1f ? 1f : alpha;
    }

    // One mapping for BOTH presentations (head icon and card): KillIconPolicy.TierIconKey/TierIconFile take
    // the tier ONLY, so no call site can bring the classification back into the icon choice.

    // Whether a head icon may be drawn at all: the mode must be Head and the HUD must be visible.
    internal static bool ShouldDrawIcons(bool visible, KillIconDisplay display)
        => visible && UsesHeadIcons(display);
    // Review I4: in Head mode the F9 preview CARDS can never be painted (ShouldDrawCard returns false for a
    // preview token), so queueing them would only occupy a slot and could evict a real fallback card when
    // MaxCards is 1. F9 still previews the nine icon combinations, which is what Head mode shows.
    internal static bool ShouldQueueCardPreviews(KillIconDisplay display) => !UsesHeadIcons(display);

    // F9 previews the nine (tier x flight) combinations and the preview set REPLACES itself, so a second
    // key press can never stack a second copy and a preview can never evict a real kill (review F5).
    internal const int PreviewCombos = 9;

    internal static void PreviewCombo(int index, out KillTier tier, out KillFlight flight)
    {
        int slot = index <= 0 ? 0 : index % PreviewCombos;
        tier = slot < 3 ? KillTier.Minor : slot < 6 ? KillTier.Elite : KillTier.Boss;
        flight = (KillFlight)(slot % 3);
    }

    // Whether the card queue may paint one card while the head icons own the kills (review F1).
    // The kill that the head icon cannot present must never lose its visible feedback, and the same kill
    // must never show both effects:
    //   * a kill that never received an icon slot (anchored capture failed) falls back immediately;
    //   * a kill whose slot is gone (evicted) or cannot draw right now (tier texture missing) falls back
    //     while the icon's own window is still open - mutually exclusive with the icon by construction;
    //   * once that window has passed, the icon owned the kill, so no late card appears either;
    //   * a preview card (token < 0) is not a kill event and keeps the old rule: Card mode only.
    internal static bool ShouldDrawCard(KillIconDisplay display, int iconToken, float iconExpiresAt, float now, bool iconPresent)
    {
        if (iconToken < 0) return !UsesHeadIcons(display);
        if (!UsesHeadIcons(display)) return true;
        if (iconToken == 0) return true;
        if (!KillPolicy.IsFinite(now) || !KillPolicy.IsFinite(iconExpiresAt)) return true;
        if (now >= iconExpiresAt) return false;
        return !iconPresent;
    }
}
