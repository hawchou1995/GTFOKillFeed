using System;
using System.Collections.Generic;
using System.Globalization;

namespace HawChow.GTFOKillFeed;

internal enum KillKind
{
    Normal,
    Weakspot
}

internal enum KillTier
{
    Minor,
    Elite,
    Boss
}

// How the enemy moves - read from game data (EnemyBehaviorDataBlock.IsFlyer), never inferred from the
// limb that happened to land the killing blow. Armored flight means the enemy's own limb layout carries
// armor, which is also independent of the last hit.
internal enum KillFlight
{
    None = 0,
    Flying = 1,
    ArmoredFlying = 2
}

// The two health lines that split the kill tiers, resolved with deterministic fallbacks.
//   Boss  = the EXISTING KillIcon/HeavyHealthThreshold line (default 1000). One key, one meaning: whatever
//           the player already wrote there is the BOSS line, so the heavy kill icon and the BOSS tier can
//           never disagree and a legacy cfg keeps the line it configured.
//   Minor = KillIcon/MinorHealthThreshold (new, default 120). Everything between the two is ELITE.
// When the player's heavy line is at or below the minor line the ELITE band is simply empty - that is a
// documented, deterministic consequence of honouring the legacy value, not a silent rewrite of it.
internal readonly struct KillTierBoundaries
{
    internal readonly float Minor;   // health below this (and below Boss) is Minor
    internal readonly float Boss;    // at/above this is Boss (the heavy line)

    internal KillTierBoundaries(float minor, float boss)
    {
        Minor = minor;
        Boss = boss;
    }

    internal static KillTierBoundaries Default
        => new KillTierBoundaries(KillDefaults.MinorHealthThreshold, KillDefaults.EliteHealthThreshold);

    // True when no health value can land in the ELITE band (Boss <= Minor).
    internal bool EliteBandEmpty => Boss <= Minor;
}

internal readonly struct KillDecision
{
    internal readonly bool Credited;
    internal readonly KillKind Kind;

    internal KillDecision(bool credited, KillKind kind)
    {
        Credited = credited;
        Kind = kind;
    }
}

// The single kill record computed once per locally credited lethal hit. Tier, flight and kind travel
// together so the kill cue, the card and (later) the head HUD read the same values instead of each
// re-deriving them.
internal readonly struct KillPresentation
{
    internal readonly KillKind Kind;
    internal readonly KillTier Tier;
    internal readonly KillFlight Flight;

    internal KillPresentation(KillKind kind, KillTier tier, KillFlight flight)
    {
        Kind = kind;
        Tier = tier;
        Flight = flight;
    }
}

internal static class KillDefaults
{
    internal const bool Enabled = true;
    internal const float IconSize = 46f;
    internal const float CardLifetime = 2.6f;
    internal const float FadeSeconds = 0.45f;
    internal const float MaxDistance = 999f;
    internal const float MinorHealthThreshold = 120f;
    internal const float EliteHealthThreshold = 1000f;
    internal const int MaxCards = 3;
}

internal static class KillPresentationPolicy
{
    // Flight comes from the enemy's own behavior data; armor comes from the enemy's limb layout, so a
    // normal limb that happens to land the killing blow can never turn a plain flyer into "armored".
    internal static KillFlight ClassifyFlight(bool isFlyer, bool armored)
        => !isFlyer ? KillFlight.None : armored ? KillFlight.ArmoredFlying : KillFlight.Flying;

    internal static KillPresentation Build(KillKind kind, float healthMax, in KillTierBoundaries tiers, bool isFlyer, bool armored)
        => new KillPresentation(kind, KillPolicy.ClassifyTier(healthMax, tiers), ClassifyFlight(isFlyer, armored));

    // F9 has to keep showing the three REAL tiers (review F6): the two preview cards used to share the
    // MINOR tier, so neither the weakspot card nor any preview could show the medium/heavy families.
    // The normal sample stays MINOR, the weakspot sample is ELITE, and the cards keep reading
    // TierIcon(card.Tier) exactly like a credited kill does.
    internal static KillTier PreviewTier(bool weakspot) => weakspot ? KillTier.Elite : KillTier.Minor;
}


internal static class KillPolicy
{
    internal static KillDecision ClassifyLethalHit(
        bool localPlayerSource,
        float healthBefore,
        float healthAfter,
        bool limbObserved,
        bool limbIsWeakspot)
    {
        bool lethal = CrossedLethalHealth(healthBefore, healthAfter);
        if (!localPlayerSource || !lethal) return new KillDecision(false, KillKind.Normal);
        return new KillDecision(true, limbObserved && limbIsWeakspot ? KillKind.Weakspot : KillKind.Normal);
    }

    internal static bool CrossedLethalHealth(float healthBefore, float healthAfter)
        => IsFinite(healthBefore) && IsFinite(healthAfter) && healthBefore > 0f && healthAfter <= 0f;

    internal const float MinTierHealthThreshold = 1f;
    internal const float MaxTierHealthThreshold = 1000000000f;

    // Deterministic resolution of the two tier lines. NaN/Infinity/non-positive fall back to the shipped
    // default for that slot (120 / 1000); an inverted pair pushes the ELITE line up to the previous
    // rule's floor (max(default elite, minor + 1)) so the minor line the player configured is kept and
    // the ladder can never invert. Nothing here reads or rewrites KillIcon/HeavyHealthThreshold.
    internal static float TierHealthThreshold(float configured, float fallback)
    {
        if (!IsFinite(configured) || configured <= 0f) return fallback;
        if (configured < MinTierHealthThreshold) return MinTierHealthThreshold;
        if (configured > MaxTierHealthThreshold) return MaxTierHealthThreshold;
        return configured;
    }

    // bossThreshold is the player's KillIcon/HeavyHealthThreshold (the existing key). It is clamped with
    // the exact same rules the heavy icon already used, and it is NEVER moved to make room for the minor
    // line: if the two cross, the ELITE band is empty and that is reported by EliteBandEmpty.
    internal static KillTierBoundaries ResolveTierBoundaries(float minorThreshold, float bossThreshold)
    {
        float minor = TierHealthThreshold(minorThreshold, KillDefaults.MinorHealthThreshold);
        float boss = HeavyHealthThreshold(bossThreshold);
        return new KillTierBoundaries(minor, boss);
    }

    // BOSS is tested first, so the heavy line wins even when it sits below the minor line (a legacy
    // cfg with HeavyHealthThreshold=100 means "100 HP and up is BOSS", exactly as before).
    internal static KillTier ClassifyTier(float healthMax, in KillTierBoundaries boundaries)
    {
        if (!IsFinite(healthMax) || healthMax < 0f) return KillTier.Minor;
        if (healthMax >= boundaries.Boss) return KillTier.Boss;
        if (healthMax < boundaries.Minor) return KillTier.Minor;
        return KillTier.Elite;
    }

    internal static KillTier ClassifyTier(float healthMax, float minorThreshold = KillDefaults.MinorHealthThreshold, float eliteThreshold = KillDefaults.EliteHealthThreshold)
        => ClassifyTier(healthMax, ResolveTierBoundaries(minorThreshold, eliteThreshold));


    internal static string FormatDistance(float meters, float maximum = KillDefaults.MaxDistance)
    {
        if (!IsFinite(meters) || meters < 0f) return "—";
        if (!IsFinite(maximum) || maximum < 1f) maximum = KillDefaults.MaxDistance;
        if (meters > maximum) return ((int)Math.Floor(maximum + 0.5f)).ToString(CultureInfo.InvariantCulture) + "m+";
        int rounded = (int)Math.Floor(meters + 0.5f);
        return rounded.ToString(CultureInfo.InvariantCulture) + "m";
    }


    internal static string FormatHealth(float healthMax)
    {
        if (!IsFinite(healthMax) || healthMax < 0f) return "— HP";
        return healthMax.ToString("0.#", CultureInfo.InvariantCulture) + " HP";
    }

    internal const float DefaultHeavyHealthThreshold = KillDefaults.EliteHealthThreshold; // 1000, the same elite line CombatPanel uses
    internal const float MinHeavyHealthThreshold = 1f;
    internal const float MaxHeavyHealthThreshold = 1000000000f;

    // A kill is "heavy" when the enemy's own health pool crosses the threshold - that is how bosses and
    // the armoured flyers separate from the rank and file without hard-coding any enemy id.
    internal static float HeavyHealthThreshold(float configured)
    {
        if (float.IsNaN(configured) || float.IsInfinity(configured)) return DefaultHeavyHealthThreshold;
        if (configured < MinHeavyHealthThreshold) return MinHeavyHealthThreshold;
        if (configured > MaxHeavyHealthThreshold) return MaxHeavyHealthThreshold;
        return configured;
    }

    internal static bool IsHeavy(float healthMax, float threshold)
        => IsFinite(healthMax) && healthMax > 0f && healthMax >= HeavyHealthThreshold(threshold);

    internal static string IconKey(KillKind kind, bool heavy)
        => heavy ? "kill-heavy.png" : kind == KillKind.Weakspot ? "kill-weakspot.png" : "kill-normal.png";

    internal static string IconKey(KillKind kind) => IconKey(kind, false);

    internal static bool IsChinese(string language)
        => string.Equals(language, "zh", StringComparison.OrdinalIgnoreCase)
            || string.Equals(language, "zh-CN", StringComparison.OrdinalIgnoreCase)
            || string.Equals(language, "cn", StringComparison.OrdinalIgnoreCase);

    internal static string TierLabel(KillTier tier, string language)
    {
        if (!IsChinese(language)) return tier == KillTier.Minor ? "MINOR" : tier == KillTier.Elite ? "ELITE" : "BOSS";
        return tier == KillTier.Minor ? "普通" : tier == KillTier.Elite ? "精英" : "BOSS";
    }

    internal static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}

internal sealed class KillLedger
{
    private readonly HashSet<long> _credited = new HashSet<long>();

    internal bool TryCredit(long enemyIdentity)
        => enemyIdentity != 0 && _credited.Add(enemyIdentity);

    internal void Reset() => _credited.Clear();
    internal void Forget(long enemyIdentity) => _credited.Remove(enemyIdentity);
    internal int Count => _credited.Count;

    // 1.2.35: a despawn can only release the ledger entry it can still name - the live damage pointer, or the one
    // recorded when this life was credited. 0 means nothing to release.
    internal static long IdentityForRelease(long damagePointer, long rememberedPointer)
        => damagePointer != 0 ? damagePointer : rememberedPointer;
    // Never release an identity whose own settlement is still on the stack: its finalizer would credit it again.
    internal static bool ShouldReleaseIdentity(long identity, long activeRootPointer)
        => identity != 0 && identity != activeRootPointer;
}

internal sealed class KillCard
{
    internal readonly KillKind Kind;
    internal readonly KillTier Tier;
    internal readonly KillFlight Flight;
    internal readonly string EnemyName;
    internal readonly string DistanceText;
    internal readonly string HealthText;
    internal readonly float CreatedAt;

    // Stable event-time inputs, kept so the queue can re-derive the *presentation* later without
    // touching who got the kill, the tier or the timestamps.
    internal readonly uint EnemyDataId;
    internal readonly string RawName;
    internal readonly float DistanceMeters;
    internal readonly float HealthMax;

    // Who presents this kill (review F1). IconToken > 0 is the never-reused generation number of the
    // head-icon slot that was created for THIS kill; 0 means the kill never got a slot (so the card is
    // its only visible feedback) and a negative token marks a preview, which is not a kill at all.
    // IconExpiresAt is the moment that slot's own lifetime ends, so the card can never appear long after
    // the icon already showed the kill.
    internal readonly int IconToken;
    internal readonly float IconExpiresAt;

    internal KillCard(KillKind kind, KillTier tier, string enemyName, float distanceMeters, float healthMax, float createdAt,
        float maxDistance = KillDefaults.MaxDistance, uint enemyDataId = 0, KillFlight flight = KillFlight.None,
        int iconToken = 0, float iconExpiresAt = 0f)
    {
        Kind = kind;
        Tier = tier;
        Flight = flight;
        EnemyName = string.IsNullOrWhiteSpace(enemyName) ? "Unknown" : enemyName.Trim();
        DistanceText = KillPolicy.FormatDistance(distanceMeters, maxDistance);
        HealthText = KillPolicy.FormatHealth(healthMax);
        CreatedAt = createdAt;
        EnemyDataId = enemyDataId;
        RawName = string.IsNullOrWhiteSpace(enemyName) ? null : enemyName.Trim();
        DistanceMeters = distanceMeters;
        HealthMax = healthMax;
        IconToken = iconToken;
        IconExpiresAt = iconExpiresAt;
    }

    // Copy with a re-resolved name/ceiling. Used while drawing so a language/name-table/distance
    // change applies to cards that are already on screen. The kill identity (including which head icon
    // presents it) is preserved, so a re-drawn card can never lose its fallback link.
    internal KillCard WithPresentation(string displayName, float maxDistance)
        => new KillCard(Kind, Tier, displayName, DistanceMeters, HealthMax, CreatedAt, maxDistance, EnemyDataId, Flight,
            IconToken, IconExpiresAt);
}

internal readonly struct VisibleKillCard
{
    internal readonly KillCard Card;
    internal readonly float Alpha;

    internal VisibleKillCard(KillCard card, float alpha)
    {
        Card = card;
        Alpha = alpha;
    }
}

// Review I1: a credited kill is presented by the head icon OR by its card, and the card is suppressed while
// the icon shows it. If the icon is evicted early (max icons) or cannot draw (missing raster) the card has to
// be there - so whenever a card carries an icon token its life is extended to at least the icon's expiry plus
// the fade. A kill can therefore never end up with neither presentation, whatever the icon settings are.
internal static class KillCardPolicy
{
    internal const float FallbackLifetime = 2.6f;
    internal static float Lifetime(float configured) => KillPolicy.IsFinite(configured) ? Math.Max(0.1f, configured) : FallbackLifetime;
    internal static float Fade(float configured, float lifetime)
        => KillPolicy.IsFinite(configured) ? Math.Max(0f, Math.Min(configured, Lifetime(lifetime))) : 0.45f;
    // The moment this card may be dropped: its own life, extended to the icon's expiry + fade when it holds
    // an icon token. Non-kill previews (token < 0) keep the plain card life.
    internal static float ExpiresAt(float createdAt, int iconToken, float iconExpiresAt, float lifetime, float fadeSeconds)
    {
        float own = createdAt + Lifetime(lifetime);
        if (iconToken <= 0 || !KillPolicy.IsFinite(iconExpiresAt)) return own;
        float icon = iconExpiresAt + Math.Max(0f, Fade(fadeSeconds, lifetime));
        return icon > own ? icon : own;
    }
    // Alpha for the same rule: fully opaque until the last `fade` seconds of the (possibly extended) life.
    internal static float Alpha(float now, float expiresAt, float fadeSeconds, float lifetime)
    {
        if (!KillPolicy.IsFinite(now) || !KillPolicy.IsFinite(expiresAt)) return 0f;
        float fade = Fade(fadeSeconds, lifetime);
        float remaining = expiresAt - now;
        if (remaining <= 0f) return 0f;
        if (fade <= 0f || remaining >= fade) return 1f;
        return Math.Max(0f, Math.Min(1f, remaining / fade));
    }
}

internal sealed class KillCardQueue
{
    private readonly List<KillCard> _cards = new List<KillCard>();

    internal bool Enqueue(KillCard card, float now, float lifetime, float fadeSeconds, int capacity)
    {
        if (card == null || capacity <= 0 || !KillPolicy.IsFinite(now)) return false;
        Trim(now, lifetime, fadeSeconds);
        while (_cards.Count >= capacity) _cards.RemoveAt(0);
        _cards.Add(card);
        return true;
    }

    internal VisibleKillCard[] Snapshot(float now, float lifetime, float fadeSeconds, int capacity)
    {
        if (!KillPolicy.IsFinite(now)) return Array.Empty<VisibleKillCard>();
        Trim(now, lifetime, fadeSeconds);
        int start = Math.Max(0, _cards.Count - Math.Max(0, capacity));
        var result = new VisibleKillCard[_cards.Count - start];
        for (int i = start; i < _cards.Count; i++)
        {
            KillCard card = _cards[i];
            // Review I1: the life is the card's own, extended to the head icon's expiry when it carries one.
            float expires = KillCardPolicy.ExpiresAt(card.CreatedAt, card.IconToken, card.IconExpiresAt, lifetime, fadeSeconds);
            result[i - start] = new VisibleKillCard(card, KillCardPolicy.Alpha(now, expires, fadeSeconds, lifetime));
        }
        return result;
    }

    internal void Clear() => _cards.Clear();
    internal int Count => _cards.Count;

    private void Trim(float now, float lifetime, float fadeSeconds)
    {
        for (int i = _cards.Count - 1; i >= 0; i--)
        {
            KillCard card = _cards[i];
            float expires = KillCardPolicy.ExpiresAt(card.CreatedAt, card.IconToken, card.IconExpiresAt, lifetime, fadeSeconds);
            if (now >= expires) _cards.RemoveAt(i);
        }
    }
}

