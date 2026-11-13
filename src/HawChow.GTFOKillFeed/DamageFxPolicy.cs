using System;

namespace HawChow.GTFOKillFeed;

// Per-number damage effect rules: the prismatic single-hit flare and the per-level cumulative-damage
// tier ladder. Pure arithmetic on floats/longs - no Unity type - so GTFOKillFeed.Smoke asserts every
// boundary directly. Colours are returned as plain triples for the same reason.
internal enum DamageTier
{
    None = 0,
    White = 1,
    Silver = 2,
    Gold = 3,
    Diamond = 4,
}

internal static class DamageFxPolicy
{
    internal const bool DefaultTiersEnabled = true;
    // DamageFx/PrismaticEnabled (1.2.16): the foil LOOK and the prismatic flight are switched independently
    // of the threshold. true keeps 1.2.16 behaviour; false renders the same number as a plain one (still
    // coloured by the tier ladder and still flown/drifted by DamageNumbers/ApproachEffect). A threshold of
    // 0 already means "never"; either switch off is enough.
    internal const bool DefaultPrismaticEnabled = true;
    internal const float DefaultPrismaticThreshold = 60f;
    internal const float MinPrismaticThreshold = 0f;
    internal const float MaxPrismaticThreshold = 1000000f;

    // Cumulative damage needed for each tier, ascending. The highest satisfied tier wins. These are the
    // DEFAULTS: DamageFx/WhiteThreshold .. DamageFx/DiamondThreshold override them at runtime.
    internal const long WhiteThreshold = 3000;
    internal const long SilverThreshold = 5000;
    internal const long GoldThreshold = 10000;
    internal const long DiamondThreshold = 20000;
    internal const long MinTierThreshold = 1;
    internal const long MaxTierThreshold = 1000000000;

    internal readonly struct TierThresholds
    {
        internal readonly long White, Silver, Gold, Diamond;
        internal TierThresholds(long white, long silver, long gold, long diamond)
        {
            White = white;
            Silver = silver;
            Gold = gold;
            Diamond = diamond;
        }
    }

    internal static readonly TierThresholds DefaultTiers = new TierThresholds(WhiteThreshold, SilverThreshold, GoldThreshold, DiamondThreshold);

    // Values of zero or below fall back to the shipped default for that slot; anything else is clamped
    // into range and then pushed up to the tier below it, so the ladder can never invert. An inverted
    // ladder would make a tier unreachable, which reads as "the mod stopped colouring my numbers".
    internal static long ClampTierThreshold(long configured, long fallback)
    {
        if (configured <= 0) return fallback < MinTierThreshold ? MinTierThreshold : fallback;
        if (configured < MinTierThreshold) return MinTierThreshold;
        if (configured > MaxTierThreshold) return MaxTierThreshold;
        return configured;
    }

    internal static TierThresholds ResolveTiers(long white, long silver, long gold, long diamond)
    {
        long w = ClampTierThreshold(white, WhiteThreshold);
        long s = Math.Max(ClampTierThreshold(silver, SilverThreshold), w);
        long g = Math.Max(ClampTierThreshold(gold, GoldThreshold), s);
        long d = Math.Max(ClampTierThreshold(diamond, DiamondThreshold), g);
        return new TierThresholds(w, s, g, d);
    }

    // Palette. Gold is the existing card accent (#E6AB57) pushed brighter so it separates from the
    // weakspot red and from the amber kill-card title; silver and diamond are new but stay desaturated
    // enough to read against the dark outline the numbers already draw.
    internal const float WhiteR = 1.000f, WhiteG = 1.000f, WhiteB = 1.000f;
    internal const float SilverR = 0.784f, SilverG = 0.816f, SilverB = 0.847f;
    internal const float GoldR = 0.941f, GoldG = 0.761f, GoldB = 0.290f;
    internal const float DiamondR = 0.498f, DiamondG = 0.914f, DiamondB = 1.000f;

    // Prismatic sweep: one full hue rotation per 1.8 s. Slow enough to read, fast enough that two
    // consecutive hits on screen differ.
    internal const float PrismaticCycleSeconds = 1.8f;
    internal const float PrismaticSaturation = 0.85f;
    internal const float PrismaticValue = 1.0f;
    internal const float TierOutlineAlpha = 0.55f;

    internal static float PrismaticThreshold(float configured)
    {
        if (float.IsNaN(configured) || float.IsInfinity(configured)) return DefaultPrismaticThreshold;
        if (configured < MinPrismaticThreshold) return MinPrismaticThreshold;
        if (configured > MaxPrismaticThreshold) return MaxPrismaticThreshold;
        return configured;
    }

    // Strictly greater than the threshold. 60.0 with the default threshold stays a plain number;
    // 60.01 flares. NaN, negative and infinite damage never flare - a broken readout must not look
    // like a big hit.
    internal static bool IsPrismatic(float damage, float threshold)
    {
        if (float.IsNaN(damage) || float.IsInfinity(damage) || damage < 0f) return false;
        float limit = PrismaticThreshold(threshold);
        if (limit <= MinPrismaticThreshold) return false;
        return damage > limit;
    }
    // DamageFx/PrismaticEnabled is an independent switch: the threshold still decides WHEN a hit is big
    // enough, the switch decides whether the foil look exists at all. Either one off means no foil.
    internal static bool IsPrismatic(float damage, float threshold, bool prismaticEnabled)
        => prismaticEnabled && IsPrismatic(damage, threshold);

    // What one damage number is painted with (1.2.16). The three switches are independent:
    //   prismatic  - DamageFx/PrismaticEnabled AND the per-hit threshold (already resolved by the caller);
    //   tiersEnabled - DamageFx/TiersEnabled AND a reached tier;
    //   the caller's HitKind only picks the plain body colour when no tier is active.
    // Foil: eight hue rims + emboss + pastel body. TierInk: body uses the tier colour. FoilRim: the CORE
    // rim (ring 0) is foil; with a tier active the core rim keeps the tier colour and only the halo, the
    // emboss and the body are foil. Rings: 1 plain rim, or 2 (core + halo) for foil.
    internal readonly struct NumberPaintPlan
    {
        internal readonly bool Foil, TierInk, FoilRim;
        internal readonly int Rings;
        internal NumberPaintPlan(bool foil, bool tierInk, bool foilRim, int rings)
        {
            Foil = foil;
            TierInk = tierInk;
            FoilRim = foilRim;
            Rings = rings;
        }
    }

    internal static NumberPaintPlan Paint(bool prismatic, bool tiersEnabled, DamageTier tier)
    {
        bool tierInk = tiersEnabled && tier != DamageTier.None;
        // The caller has already gated prismatic on PrismaticEnabled and the threshold; here only the tier
        // interaction is left. A foil number without a tier gets the foil core rim, with a tier it keeps the
        // tier colour on the core rim and only foils the halo/emboss/body.
        return new NumberPaintPlan(prismatic, tierInk, prismatic && !tierInk, prismatic ? 2 : 1);
    }

    internal static DamageTier TierFor(long cumulative, bool tiersEnabled) => TierFor(cumulative, tiersEnabled, DefaultTiers);

    internal static DamageTier TierFor(long cumulative, bool tiersEnabled, in TierThresholds tiers)
    {
        if (!tiersEnabled || cumulative <= 0) return DamageTier.None;
        if (cumulative >= tiers.Diamond) return DamageTier.Diamond;
        if (cumulative >= tiers.Gold) return DamageTier.Gold;
        if (cumulative >= tiers.Silver) return DamageTier.Silver;
        if (cumulative >= tiers.White) return DamageTier.White;
        return DamageTier.None;
    }

    // Saturating accumulate: NaN/Infinity/non-positive damage leaves the running total alone, and the
    // sum can never wrap. Fractional damage truncates toward zero, matching the integer readout.
    internal static long Accumulate(long cumulative, float damage)
    {
        if (cumulative < 0) cumulative = 0;
        if (float.IsNaN(damage) || float.IsInfinity(damage) || damage <= 0f) return cumulative;
        long add = (long)damage;
        if (add <= 0) return cumulative;
        if (cumulative > long.MaxValue - add) return long.MaxValue;
        return cumulative + add;
    }

    internal static void TierColor(DamageTier tier, out float red, out float green, out float blue)
    {
        switch (tier)
        {
            case DamageTier.Diamond: red = DiamondR; green = DiamondG; blue = DiamondB; return;
            case DamageTier.Gold: red = GoldR; green = GoldG; blue = GoldB; return;
            case DamageTier.Silver: red = SilverR; green = SilverG; blue = SilverB; return;
            case DamageTier.White: red = WhiteR; green = WhiteG; blue = WhiteB; return;
            default: red = 0f; green = 0f; blue = 0f; return;
        }
    }

    // Monotonic hue sweep. phase is wrapped into [0,1) so a long session cannot lose precision.
    internal static float WrapPhase(float phase)
    {
        if (float.IsNaN(phase) || float.IsInfinity(phase)) return 0f;
        float wrapped = phase - (float)Math.Floor(phase);
        if (wrapped < 0f) wrapped += 1f;
        return wrapped >= 1f ? 0f : wrapped;
    }

    internal static float PhaseFor(float unscaledSeconds)
    {
        if (float.IsNaN(unscaledSeconds) || float.IsInfinity(unscaledSeconds)) return 0f;
        return WrapPhase(unscaledSeconds / PrismaticCycleSeconds);
    }

    internal static void PrismaticColor(float phase, out float red, out float green, out float blue)
        => HueColor(WrapPhase(phase), PrismaticSaturation, PrismaticValue, out red, out green, out blue);

    // Shared HSV-sector conversion (hue only, fixed saturation/value passed in).
    internal static void HueColor(float hue, float saturation, float value, out float red, out float green, out float blue)
    {
        float sector = WrapPhase(hue) * 6f;
        int index = (int)sector;
        if (index > 5) index = 5;
        float fraction = sector - index;
        float low = value * (1f - saturation);
        float falling = value * (1f - saturation * fraction);
        float rising = value * (1f - saturation * (1f - fraction));
        switch (index)
        {
            case 0: red = value; green = rising; blue = low; return;
            case 1: red = falling; green = value; blue = low; return;
            case 2: red = low; green = value; blue = rising; return;
            case 3: red = low; green = falling; blue = value; return;
            case 4: red = rising; green = low; blue = value; return;
            default: red = value; green = low; blue = falling; return;
        }
    }

    // ---- holographic foil -------------------------------------------------------------------
    // The prismatic look is a *material*, not a hue swap: a pastel-rainbow rim that rotates around
    // the glyph, a white-hot core with a travelling emboss highlight, and a slow shimmer. IMGUI has
    // no shaders or gradients here, so it is built from a fixed number of extra passes:
    //   1) eight rim passes, each of the eight directions a different pastel hue (the foil edge),
    //   2) one emboss pass offset along a rotating direction (the moving hot highlight),
    //   3) the body pass, near-white with a pastel tint and a pulsing shimmer.
    internal const float FoilSaturation = 0.55f;      // pastel, not neon
    internal const float FoilSheenCycles = 2.0f;      // shimmer pulses per hue rotation
    internal const float FoilRimAlpha = 0.95f;
    internal const float FoilRimHaloAlpha = 0.42f;   // second, wider rim ring: the foil glow
    internal const float FoilRimHaloRadius = 3.1f;   // px at 1080p, on top of the 1.6 px core rim
    internal const float FoilEmbossAlpha = 0.45f;
    internal const float FoilEmbossRadius = 1.6f;    // px at 1080p
    internal const float FoilCoreBlend = 0.62f;       // how far the body is pulled toward white

    // direction 0..7 maps to the eight outline offsets (-1..1, centre excluded).
    internal static void FoilDirection(int direction, out int dx, out int dy)
    {
        switch (direction & 7)
        {
            case 0: dx = -1; dy = -1; return;
            case 1: dx = 0; dy = -1; return;
            case 2: dx = 1; dy = -1; return;
            case 3: dx = 1; dy = 0; return;
            case 4: dx = 1; dy = 1; return;
            case 5: dx = 0; dy = 1; return;
            case 6: dx = -1; dy = 1; return;
            default: dx = -1; dy = 0; return;
        }
    }

    internal static void FoilRimColor(int direction, float phase, out float red, out float green, out float blue)
        => HueColor(WrapPhase(phase + (direction & 7) / 8f), FoilSaturation, 1f, out red, out green, out blue);

    internal static float FoilSheen(float phase)
        => 0.5f + 0.5f * (float)Math.Sin(2.0 * Math.PI * FoilSheenCycles * WrapPhase(phase));

    // Body colour: pastel tint pulled toward white, brightness tracking the shimmer.
    internal static void FoilBodyColor(float phase, out float red, out float green, out float blue)
    {
        HueColor(WrapPhase(phase), FoilSaturation * 0.6f, 1f, out float pRed, out float pGreen, out float pBlue);
        float sheen = FoilSheen(phase);
        float tint = FoilCoreBlend + 0.30f * sheen;
        red = pRed + (1f - pRed) * tint;
        green = pGreen + (1f - pGreen) * tint;
        blue = pBlue + (1f - pBlue) * tint;
    }

    // Travelling emboss highlight: the offset rotates, so the hot edge sweeps around the glyph.
    internal static void FoilEmbossOffset(float phase, out float dx, out float dy)
    {
        double angle = 2.0 * Math.PI * WrapPhase(phase);
        dx = (float)Math.Sin(angle) * FoilEmbossRadius;
        dy = (float)-Math.Cos(angle) * FoilEmbossRadius;
    }

    // A tier recolours the body of EVERY number - normal, weakspot and armour alike - and is also
    // carried by the outline, so a prismatic number (foil body) still shows its tier through the rim.
    internal static float OutlineStrength(DamageTier tier) => tier == DamageTier.None ? 0f : TierOutlineAlpha;
}
