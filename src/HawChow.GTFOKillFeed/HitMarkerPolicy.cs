using System;

namespace HawChow.GTFOKillFeed;

// Which BFV line a marker draws. Normal / Weakspot / Armor are the three hit states; Kill is the separate
// kill line the vanilla death indicator used to draw (a different effect from the head-top kill icon).
internal enum HitMarkerKind
{
    None = 0,
    Normal = 1,
    Weakspot = 2,
    Armor = 3,
    Kill = 4,
}

// What happened when an event reached the marker queue: a new line was added, an older kill line was
// upgraded by a newer kill event, a duplicate at the same spot was merged (lifetime refreshed) or the
// event was dropped because the queue is full of older markers.
internal enum HitMarkerAction
{
    Dropped = 0,
    Added = 1,
    Upgraded = 2,
    Merged = 3,
}

// Pure policy for the world-anchored BFV hit/kill lines: kind mapping, per-kind lifetime and size,
// projection gating, the local-damage ticket window and the queue merge rules. No UnityEngine reference -
// the Smoke project compiles this file on its own, so every rule below is asserted without the game.
internal static class HitMarkerPolicy
{
    internal const bool DefaultEnabled = true;
    internal const float DefaultSize = 64f;        // px at 1080p; the BFV source raster is 64x64
    internal const float MinSize = 16f;
    internal const float MaxSize = 200f;

    // A hit line is a quick confirmation; the kill line is the "that one died" beat and stays readable a
    // little longer. Both are clamped, so a broken value can never pin a marker on screen.
    internal const float DefaultHitDurationSeconds = 0.18f;
    internal const float DefaultKillDurationSeconds = 0.34f;
    internal const float MinDurationSeconds = 0.05f;
    internal const float MaxDurationSeconds = 1.0f;

    // Queue bounds: simultaneous targets are supported, but the HUD can never accumulate markers.
    internal const int DefaultCapacity = 12;
    internal const int MaxCapacity = 24;

    // (Review F3) Two markers are only ever merged when they carry the SAME event identity: the never-reused
    // id of one local damage scope. Proximity alone is not evidence - two enemies standing half a metre
    // apart, a remote hit next to a local kill, or two hits on the same enemy are all separate events and
    // must keep their own line. The old 0.9 m merge radius is gone on purpose: unknown sources are drawn
    // separately rather than one being swallowed.

    // Association between a local damage impact (published by the KillTracker prefix, i.e. BEFORE the
    // original damage method runs) and the indicator event the game may show from inside that method.
    // The match radius is deliberately much tighter than the visual merge radius: two enemies standing next
    // to each other must never trade armour verdicts. The frame window bounds a leaked scope (a finalizer
    // that never ran); it is not a substitute for the Harmony scope itself.
    internal const float ImpactMatchDistanceMetres = 0.25f;
    internal const float ImpactMatchDistanceSquared = ImpactMatchDistanceMetres * ImpactMatchDistanceMetres;
    internal const int ImpactFrameWindow = 1;
    internal const int MaxImpactDepth = 8;

    internal const float WeakspotSizeScale = 1.18f;
    internal const float ArmorSizeScale = 0.92f;
    internal const float KillSizeScale = 1.35f;

    // Colours come from the source mod's own palettes (light white, medium orange, HD2 assist cyan); the
    // kill line uses the BFV heavy red so it never reads as another hit.
    internal const string DefaultNormalColor = "#FFFFFF";
    internal const string DefaultWeakspotColor = "#FF8C00";
    internal const string DefaultArmorColor = "#49ADC9";
    internal const string DefaultKillColor = "#B92828";

    internal static float Size(float configured)
    {
        if (float.IsNaN(configured) || float.IsInfinity(configured)) return DefaultSize;
        if (configured < MinSize) return MinSize;
        if (configured > MaxSize) return MaxSize;
        return configured;
    }

    internal static float Duration(float configured)
    {
        if (float.IsNaN(configured) || float.IsInfinity(configured)) return DefaultHitDurationSeconds;
        if (configured < MinDurationSeconds) return MinDurationSeconds;
        if (configured > MaxDurationSeconds) return MaxDurationSeconds;
        return configured;
    }

    internal static float Lifetime(HitMarkerKind kind)
        => kind == HitMarkerKind.Kill ? DefaultKillDurationSeconds : DefaultHitDurationSeconds;

    internal static int Capacity(int configured)
    {
        if (configured <= 0) return DefaultCapacity;
        return configured > MaxCapacity ? MaxCapacity : configured;
    }

    // Fades linearly from 1 to 0 over the kind's lifetime; a broken age never draws.
    internal static float Alpha(float age, float lifetime)
    {
        if (float.IsNaN(age) || float.IsInfinity(age)) return 0f;
        float window = Duration(lifetime);
        if (age < 0f) return 0f;
        if (age >= window) return 0f;
        float alpha = 1f - (age / window);
        return alpha < 0f ? 0f : alpha > 1f ? 1f : alpha;
    }

    internal static bool ShouldDraw(bool enabled, HitMarkerKind kind, float alpha)
        => enabled && kind != HitMarkerKind.None && !float.IsNaN(alpha) && alpha > 0f;

    internal static bool ChannelEnabled(bool enabled, bool channel) => enabled && channel;

    internal static bool ChannelForKind(bool hitEnabled, bool killEnabled, HitMarkerKind kind)
        => kind == HitMarkerKind.Kill ? killEnabled : hitEnabled;

    // The single gate HitMarkerHud.Submit uses before any projection or queue work: a disabled
    // channel and the unresolved kind (None) are both rejected here.
    internal static bool AcceptsSubmission(bool channelEnabled, HitMarkerKind kind)
        => channelEnabled && kind != HitMarkerKind.None;

    // The three hit states come straight from the game's own hit-indicator event (isWeakspot) plus the
    // local-damage armour ticket; the kill flag comes from the death-indicator event.
    internal static HitMarkerKind KindFor(bool weakspot, bool armor, bool kill)
        => kill ? HitMarkerKind.Kill
            : armor ? HitMarkerKind.Armor
            : weakspot ? HitMarkerKind.Weakspot
            : HitMarkerKind.Normal;

    internal static string IconKey(HitMarkerKind kind)
        => kind == HitMarkerKind.Weakspot ? "hitmark-weakspot"
            : kind == HitMarkerKind.Armor ? "hitmark-armor"
            : kind == HitMarkerKind.Kill ? "hitmark-kill"
            : kind == HitMarkerKind.Normal ? "hitmark-normal"
            : string.Empty;

    internal static string IconFile(HitMarkerKind kind)
        => kind == HitMarkerKind.None ? string.Empty : IconKey(kind) + ".png";

    internal static float SizeScale(HitMarkerKind kind)
        => kind == HitMarkerKind.Weakspot ? WeakspotSizeScale
            : kind == HitMarkerKind.Armor ? ArmorSizeScale
            : kind == HitMarkerKind.Kill ? KillSizeScale
            : 1f;

    // Projection gate shared by the "can we replace this event" decision and the per-frame draw: the point
    // must be in front of the camera (z past the near plane) and inside the screen with a small margin.
    internal static bool IsProjectable(float z, float x, float y, float screenWidth, float screenHeight, float margin)
    {
        if (!IsFinite(z) || !IsFinite(x) || !IsFinite(y)) return false;
        if (!IsFinite(screenWidth) || !IsFinite(screenHeight) || screenWidth <= 0f || screenHeight <= 0f) return false;
        if (margin < 0f || float.IsNaN(margin)) margin = 0f;
        if (z <= 0.05f) return false;
        return x >= -margin && x <= screenWidth + margin && y >= -margin && y <= screenHeight + margin;
    }

    internal static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    internal static float DistanceSquared(float ax, float ay, float az, float bx, float by, float bz)
    {
        if (!IsFinite(ax) || !IsFinite(ay) || !IsFinite(az) || !IsFinite(bx) || !IsFinite(by) || !IsFinite(bz))
            return float.PositiveInfinity;
        float dx = ax - bx, dy = ay - by, dz = az - bz;
        return dx * dx + dy * dy + dz * dz;
    }

    // The same impact, inside the frame window. Out-of-order or stale scopes never match.
    internal static bool ImpactMatches(int currentFrame, int scopeFrame, float distanceSquared)
        => ImpactFrameCurrent(currentFrame, scopeFrame) && distanceSquared <= ImpactMatchDistanceSquared;

    internal static bool ImpactFrameCurrent(int currentFrame, int scopeFrame)
    {
        if (currentFrame < scopeFrame) return false;
        return currentFrame - scopeFrame <= ImpactFrameWindow;
    }

    // The single routing rule for an indicator event. `scopeMatched`/`scopeKind` come from the active local
    // damage scope (ImpactScopeStack); `weakspot` is the game's own flag for this event.
    //   * friendly fire is never ours;
    //   * a noDamage event is replaced ONLY by a confirmed local ARMOUR impact - an unconfirmed noDamage
    //     event, a normal/weakspot impact or a lost context always keeps the vanilla indicator;
    //   * every other event keeps the previous behaviour: weakspot flag + armour verdict from the scope.
    internal static bool TryDecide(bool enabled, bool friendly, bool noDamage, bool scopeMatched,
        HitKind scopeKind, bool weakspot, out HitMarkerKind kind)
    {
        kind = HitMarkerKind.None;
        if (!enabled || friendly) return false;
        bool armour = scopeMatched && scopeKind == HitKind.Armor;
        if (noDamage)
        {
            if (!armour) return false;
            kind = HitMarkerKind.Armor;
            return true;
        }
        kind = KindFor(weakspot, armour, false);
        return true;
    }

    // Queue decision for one event against the marker that carries the same event identity (review F3):
    //   * no identity, or a different identity -> Added. Two events are independent, whatever their
    //     distance: a nearby kill can never swallow another enemy's hit line (and the reverse);
    //   * the same identity, kill after hit      -> Upgraded (one lethal blow stays one line);
    //   * the same identity, hit after kill      -> Merged (the kill line already reports that event);
    //   * the same identity, two hits            -> Merged (lifetime refresh of the very same event).
    internal static HitMarkerAction Resolve(HitMarkerKind existing, int existingIdentity, HitMarkerKind incoming, int incomingIdentity)
    {
        if (incoming == HitMarkerKind.None) return HitMarkerAction.Dropped;
        if (existing == HitMarkerKind.None) return HitMarkerAction.Added;
        if (incomingIdentity <= 0 || incomingIdentity != existingIdentity) return HitMarkerAction.Added;
        if (incoming == HitMarkerKind.Kill && existing != HitMarkerKind.Kill) return HitMarkerAction.Upgraded;
        return HitMarkerAction.Merged;
    }
}
