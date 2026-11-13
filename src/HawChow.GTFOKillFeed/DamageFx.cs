using UnityEngine;

namespace HawChow.GTFOKillFeed;

// Runtime envelope around DamageFxPolicy: the per-level running total plus the two config reads the
// draw path needs. The total lives here (not in the config) and is zeroed from KillTracker.Reset, so a
// level change, a death restart or a manual reset all clear it while a pause, an EMP or a config
// reload cannot.
internal static class DamageFx
{
    private static long _cumulative;

    internal static long Cumulative => _cumulative;

    internal static void NoteDamage(float damage) => _cumulative = DamageFxPolicy.Accumulate(_cumulative, damage);

    internal static void Reset(string reason) => _cumulative = 0;

    internal static bool TiersEnabled()
        => Plugin.DamageFxTiersEnabled == null || Plugin.DamageFxTiersEnabled.Value;

    // DamageFx/PrismaticEnabled (1.2.16): read per number so a reload repaints numbers already on screen.
    // A missing entry (Smoke, or an older cfg bound before the key existed) means "on", the shipped default.
    internal static bool PrismaticEnabled()
        => Plugin.DamageFxPrismaticEnabled == null || Plugin.DamageFxPrismaticEnabled.Value;

    internal static float PrismaticThreshold()
        => DamageFxPolicy.PrismaticThreshold(Plugin.DamageFxPrismaticThreshold != null
            ? Plugin.DamageFxPrismaticThreshold.Value : DamageFxPolicy.DefaultPrismaticThreshold);

    // The single production prismatic decision: the per-hit threshold AND the user's switch. Turning either
    // off removes the foil, the prismatic approach scale and the seeded swoop in one place.
    internal static bool IsPrismatic(float damage)
        => DamageFxPolicy.IsPrismatic(damage, PrismaticThreshold(), PrismaticEnabled());

    internal static DamageFxPolicy.TierThresholds Thresholds() => DamageFxPolicy.ResolveTiers(
        Plugin.DamageFxWhiteThreshold != null ? Plugin.DamageFxWhiteThreshold.Value : DamageFxPolicy.WhiteThreshold,
        Plugin.DamageFxSilverThreshold != null ? Plugin.DamageFxSilverThreshold.Value : DamageFxPolicy.SilverThreshold,
        Plugin.DamageFxGoldThreshold != null ? Plugin.DamageFxGoldThreshold.Value : DamageFxPolicy.GoldThreshold,
        Plugin.DamageFxDiamondThreshold != null ? Plugin.DamageFxDiamondThreshold.Value : DamageFxPolicy.DiamondThreshold);

    // Read per draw so an edited .cfg moves the tier boundaries immediately; it is a struct, no allocation.
    internal static DamageTier Tier() => DamageFxPolicy.TierFor(_cumulative, TiersEnabled(), Thresholds());

    internal static float Phase()
    {
        float now;
        try { now = Time.unscaledTime; }
        catch (System.Exception) { return 0f; }
        return DamageFxPolicy.PhaseFor(now);
    }

    internal static Color PrismaticColor(float phase)
    {
        DamageFxPolicy.PrismaticColor(phase, out float red, out float green, out float blue);
        return new Color(red, green, blue);
    }

    internal static Color TierColor(DamageTier tier)
    {
        DamageFxPolicy.TierColor(tier, out float red, out float green, out float blue);
        return new Color(red, green, blue);
    }
}
