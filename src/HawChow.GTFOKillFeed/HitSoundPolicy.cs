using System;

namespace HawChow.GTFOKillFeed;

// Which bundled cue to play for an event, and - separately - which vanilla Wwise events must be
// silenced so the two feedback layers never stack. Pure data + arithmetic: no Unity type is touched,
// so GTFOKillFeed.Smoke asserts every rule directly.
internal enum HitSoundStyle
{
    Bf1 = 0,
    Bf5 = 1,
    Bf6 = 2,
}

internal enum HitCueKind
{
    Hit = 0,
    WeakspotHit = 1,
    ArmorHit = 2,
    Kill = 3,
}

internal static class HitSoundPolicy
{
    // 1.2.16: the cues ship on. BepInEx only applies a default when the key is absent, so an existing
    // .cfg that says `false` keeps playing exactly as its owner configured it.
    internal const bool DefaultEnabled = true;
    internal const string DefaultStyle = "BF5";
    internal const float DefaultVolume = 0.8f;
    // Per-channel defaults for a cfg that has never seen the new keys: both channels seeded off so a
    // brand-new install stays silent until the player asks for the sound, at half the old loudness.
    internal const bool DefaultHitEnabled = false;
    internal const bool DefaultKillEnabled = false;
    internal const float DefaultHitVolume = 0.4f;
    internal const float DefaultKillVolume = 0.4f;
    internal const bool DefaultMuteVanillaIndicator = false;  // opt-in: the stock indicator stays audible by default
    internal const double DefaultMinIntervalSeconds = 0.05;

    internal const float MinVolume = 0f;
    internal const float MaxVolume = 1f;
    internal const double MinIntervalFloor = 0.02;
    internal const double MinIntervalCeiling = 3.0;

    // Exactly the three vanilla hit-confirmation *indicator* layers. The body-impact events
    // (BULLETHITENEMYLOCAL/SYNC, BULLETHITENEMYWEAKSPOTLOCAL/SYNC) and every other Wwise event stay
    // audible: they are the shot landing in the world, not the confirmation beep this feature replaces.
    // (Review A4) Only AK.EVENTS is parsed, and only two of these three names live there: the Test-profile
    // interop exposes BULLETHITENEMYLOCALINDICATOR and BULLETHITENEMYWEAKSPOTLOCALINDICATOR on AK.EVENTS,
    // while BULLETHITENEMYINDICATOR exists only on AK.BUSSES - a bus table whose numbers are not event ids.
    // The whitelist therefore keeps all three names (it documents the intent and the third one is reported
    // as unresolved), but the third event is NOT proven muted: its vanilla layer keeps playing.
    internal static readonly string[] MutedEventNames =
    {
        "BULLETHITENEMYINDICATOR",
        "BULLETHITENEMYLOCALINDICATOR",
        "BULLETHITENEMYWEAKSPOTLOCALINDICATOR",
    };

    // 1.2.18: in the shipped build BULLETHITENEMYINDICATOR is a BUS-table property (AK.BUSSES), not an AK.EVENTS
    // event id - the Test-profile interop proves the split. Its absence from AK.EVENTS is therefore EXPECTED and must
    // never be reported as a broken event id, and a bus number must never enter the event-id filter (a bus and an
    // event id live in different namespaces; matching would be a coincidence that silences an unrelated event).
    // The name stays in the whitelist so a future build that promotes it to a real event is muted without a code
    // change; until then it is simply not muted, and the log says exactly that instead of claiming silence.
    internal const string OptionalBusIndicatorName = "BULLETHITENEMYINDICATOR";

    internal static bool IsKnownOptionalBusIndicator(string name)
        => string.Equals(name, OptionalBusIndicatorName, StringComparison.Ordinal);

    // The pure half of the resolution, so the three cases below are testable without AK.EVENTS:
    //   * a resolved name contributes its event id (deduplicated, insertion order kept);
    //   * the known optional bus name missing from AK.EVENTS is expected (info only, still counted unresolved);
    //   * any other missing name is a real event-id miss (warning), and its name is reported.
    internal readonly struct IndicatorResolution
    {
        internal readonly uint[] Ids;
        internal readonly string[] ResolvedNames;
        internal readonly string[] MissingEvents;
        internal readonly int Unresolved;
        internal readonly bool OptionalBusMissing;

        internal IndicatorResolution(uint[] ids, string[] resolvedNames, string[] missingEvents, int unresolved, bool optionalBusMissing)
        {
            Ids = ids;
            ResolvedNames = resolvedNames;
            MissingEvents = missingEvents;
            Unresolved = unresolved;
            OptionalBusMissing = optionalBusMissing;
        }

        internal bool HasMissingEvent => MissingEvents.Length > 0;
    }

    internal static IndicatorResolution ResolveIndicators(System.Collections.Generic.IList<string> names, Func<string, uint?> lookup)
    {
        var ids = new System.Collections.Generic.List<uint>(names?.Count ?? 0);
        var resolved = new System.Collections.Generic.List<string>(names?.Count ?? 0);
        var missing = new System.Collections.Generic.List<string>(2);
        int unresolved = 0;
        bool optionalBusMissing = false;
        if (names != null)
        {
            for (int i = 0; i < names.Count; i++)
            {
                string name = names[i];
                if (string.IsNullOrEmpty(name)) continue;
                uint? id = lookup != null ? lookup(name) : null;
                if (id.HasValue)
                {
                    resolved.Add(name);
                    if (!ids.Contains(id.Value)) ids.Add(id.Value);
                    continue;
                }
                unresolved++;
                if (IsKnownOptionalBusIndicator(name)) optionalBusMissing = true;
                else missing.Add(name);
            }
        }
        return new IndicatorResolution(ids.ToArray(), resolved.ToArray(), missing.ToArray(), unresolved, optionalBusMissing);
    }

    // The shipped cue set (Assets/audio, minus the corpse burst). Keep this in step with
    // scripts/Build-HitKillAudio.py RECIPES and with the embedded-resource test; the matrix collapses onto
    // exactly these names because BF6 has no hit take and the heavy take has no per-style variant.
    internal const int ShippedCueNames = 25;

    // Reported once per missing cue so a pack/packaging mistake can never turn into silence that
    // nobody notices (the caller logs it; the policy states the fallback order).
    internal const string MissingCueReason = "cue-missing";

    internal const string ResourcePrefix = "KillFeed.Audio.";
    private const string FallbackBodyCue = "hit-bf5-hit.wav";

    internal static HitSoundStyle ParseStyle(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return HitSoundStyle.Bf5;
        string trimmed = value.Trim();
        if (string.Equals(trimmed, "BF1", StringComparison.OrdinalIgnoreCase)) return HitSoundStyle.Bf1;
        if (string.Equals(trimmed, "BF6", StringComparison.OrdinalIgnoreCase)) return HitSoundStyle.Bf6;
        // BF5 is both the documented default and the fallback for anything unrecognised.
        return HitSoundStyle.Bf5;
    }

    internal static string StyleToken(HitSoundStyle style)
        => style == HitSoundStyle.Bf1 ? "bf1" : style == HitSoundStyle.Bf6 ? "bf6" : "bf5";

    internal static string TierToken(KillTier tier)
        => tier == KillTier.Minor ? "minor" : tier == KillTier.Elite ? "elite" : "boss";

    internal static string FlightToken(KillFlight flight)
        => flight == KillFlight.ArmoredFlying ? "armor-flyer" : flight == KillFlight.Flying ? "flyer" : string.Empty;

    // The BFV pack only records per-style takes for hit_normal (BF1), light (BF1/BF6) and medium
    // (BF1/BF6). Every other category has exactly one take, so those cues collapse onto the BF5 file
    // NAME instead of shipping byte-identical duplicates under a second name.
    internal static bool HasStyleVariant(HitSoundStyle style, HitCueKind kind, KillTier tier, KillFlight flight)
    {
        if (style == HitSoundStyle.Bf5) return true;
        switch (kind)
        {
            case HitCueKind.Hit:
                return style == HitSoundStyle.Bf1;          // hit_normal_bf1 exists, hit_normal_bf6 does not
            case HitCueKind.Kill:
                if (tier == KillTier.Boss) return false;    // heavy has no per-style take
                return true;                                // light_bf1/bf6 and medium_bf1/bf6 exist
            default:
                return false;                               // hit_heavy has no weakspot/armor per-style take
        }
    }

    internal static string CueToken(HitSoundStyle style, HitCueKind kind, KillTier tier, KillFlight flight)
        => HasStyleVariant(style, kind, tier, flight) ? StyleToken(style) : "bf5";

    // The resource that carries (style, kind, tier, flight). Hits ignore tier/flight; kills encode both,
    // so a flying kill keeps its tier and an armored flyer is a different file from a plain flyer.
    internal static string CueFile(HitSoundStyle style, HitCueKind kind, KillTier tier, KillFlight flight)
    {
        string token = CueToken(style, kind, tier, flight);
        switch (kind)
        {
            case HitCueKind.WeakspotHit:
                return "hit-bf5-weakspot.wav";
            case HitCueKind.ArmorHit:
                return "hit-bf5-armor.wav";
            case HitCueKind.Kill:
                string flightToken = FlightToken(flight);
                string stem = flightToken.Length == 0 ? "kill" : "kill-" + flightToken;
                return "hit-" + token + "-" + stem + "-" + TierToken(tier) + ".wav";
            default:
                return "hit-" + token + "-hit.wav";
        }
    }

    internal static string ResourceName(HitSoundStyle style, HitCueKind kind, KillTier tier, KillFlight flight)
        => ResourcePrefix + CueFile(style, kind, tier, flight);

    internal static string ResourceName(HitSoundStyle style, HitCueKind kind)
        => ResourceName(style, kind, KillTier.Minor, KillFlight.None);

    // Ordered list of resources to try. The last entry is always the BF5 hit cue, so a missing file
    // degrades to *some* audible feedback instead of swallowing the event.
    internal static string[] CueChain(HitSoundStyle style, HitCueKind kind, KillTier tier, KillFlight flight)
    {
        var chain = new System.Collections.Generic.List<string>(4);
        AddUnique(chain, ResourceName(style, kind, tier, flight));
        AddUnique(chain, ResourceName(HitSoundStyle.Bf5, kind, tier, flight));
        if (kind == HitCueKind.Kill)
        {
            // A flight variant falls back to the SAME tier without the flight layer, never to another
            // tier line: the tier is part of what the cue reports.
            AddUnique(chain, ResourceName(HitSoundStyle.Bf5, kind, tier, KillFlight.None));
        }
        AddUnique(chain, ResourcePrefix + FallbackBodyCue);
        return chain.ToArray();
    }

    internal static string[] CueChain(HitSoundStyle style, HitCueKind kind)
        => CueChain(style, kind, KillTier.Minor, KillFlight.None);

    private static void AddUnique(System.Collections.Generic.List<string> chain, string value)
    {
        if (!chain.Contains(value)) chain.Add(value);
    }

    internal static float Volume(float configured)
    {
        if (float.IsNaN(configured) || float.IsInfinity(configured)) return DefaultVolume;
        if (configured < MinVolume) return MinVolume;
        if (configured > MaxVolume) return MaxVolume;
        return configured;
    }

    internal static double MinInterval(double configured)
    {
        if (double.IsNaN(configured) || double.IsInfinity(configured)) return DefaultMinIntervalSeconds;
        if (configured < MinIntervalFloor) return MinIntervalFloor;
        if (configured > MinIntervalCeiling) return MinIntervalCeiling;
        return configured;
    }

    internal static HitCueKind KindOf(HitKind kind, bool kill)
        => kill ? HitCueKind.Kill
            : kind == HitKind.Weakspot ? HitCueKind.WeakspotHit
            : kind == HitKind.Armor ? HitCueKind.ArmorHit
            : HitCueKind.Hit;

    internal static bool ShouldPlay(bool enabled, float volume) => enabled && volume > MinVolume;

    // A negative or non-finite "last played" means nothing has played yet, so the first cue is
    // always allowed. Elapsed time is compared against the clamped interval only.
    internal static bool IntervalAllows(double now, double lastPlayed, double interval)
    {
        if (double.IsNaN(now) || double.IsInfinity(now)) return false;
        if (double.IsNaN(lastPlayed) || double.IsInfinity(lastPlayed) || lastPlayed < 0.0) return true;
        double gap = MinInterval(interval);
        return now - lastPlayed >= gap;
    }

    // The single mute decision. It reads the mute switch and the id set and nothing else: the cue
    // player's own Enabled/Volume must never un-mute the vanilla layer (they are separate features).
    internal static bool ShouldMute(bool muteVanillaIndicator, System.Collections.Generic.ICollection<uint> mutedIds, uint eventId)
    {
        if (!muteVanillaIndicator || mutedIds == null || mutedIds.Count == 0) return false;
        return mutedIds.Contains(eventId);
    }

    // A missing switch value must NOT mute (review A3): the shipped default is "vanilla stays audible", so
    // an unbound/absent config entry has to mean false instead of silently silencing the stock indicator.
    internal static bool MuteRequested(bool? configured) => configured.HasValue && configured.Value;
}

// Thread-safe state for the hit/kill cues (review A1). The damage path is not guaranteed to be main-thread
// only (DamageSync applies damage from network callbacks) and CellSound.Post reads the mute decision from the
// game's own audio call site, so every mutable field is either immutable after publication or guarded by one
// private gate:
//   * the resolved mute ids are published as one frozen uint[] and read lock-free afterwards;
//   * the resolution itself runs exactly once (TryBeginResolve / PublishResolved);
//   * the throttle timestamps are read-modify-write, so "check the gap, play, remember the time" runs under
//     one gate. The play callback runs inside that gate, and CueWavePlayer never calls back into this type,
//     so the only lock order is Throttle -> WavePlayer and it cannot deadlock.
// Live only in the plugin; the pure tests hammer it from several threads.
internal sealed class HitSoundState
{
    private static readonly uint[] NoIds = Array.Empty<uint>();
    private readonly object _resolveGate = new object();
    private readonly object _throttleGate = new object();
    private uint[] _mutedIds = NoIds;
    private bool _resolveStarted;
    private bool _resolveFinished;
    private int _resolveFailures = -1;
    private double _lastHit = -1.0;
    private double _lastKill = -1.0;
    private int _played;

    internal uint[] MutedIds => _mutedIds;
    internal int MutedIdCount => _mutedIds.Length;
    // -1 until a resolution completed; after that the number of whitelisted names that were not found.
    internal int ResolveFailures => _resolveFailures;
    internal int Played => _played;

    internal bool ResolveStarted
    {
        get { lock (_resolveGate) return _resolveStarted; }
    }

    internal bool ResolveFinished
    {
        get { lock (_resolveGate) return _resolveFinished; }
    }

    // True for exactly one caller, ever: the interop type lookup must not run twice.
    internal bool TryBeginResolve()
    {
        lock (_resolveGate)
        {
            if (_resolveStarted) return false;
            _resolveStarted = true;
            return true;
        }
    }

    // Publishes the frozen result. A null array means "nothing resolved", i.e. nothing is muted.
    internal void PublishResolved(uint[] ids, int failures)
    {
        lock (_resolveGate)
        {
            _mutedIds = ids ?? NoIds;
            _resolveFailures = failures < 0 ? 0 : failures;
            _resolveFinished = true;
        }
    }

    // The throttle. Returns true only when the callback actually played, and remembers that moment.
    internal bool TryPlay(bool kill, double now, double interval, Func<bool> play)
    {
        if (play == null) return false;
        lock (_throttleGate)
        {
            double last = kill ? _lastKill : _lastHit;
            if (!IntervalAllows(now, last, interval)) return false;
            if (!play()) return false;
            if (kill) _lastKill = now; else _lastHit = now;
            _played++;
            return true;
        }
    }

    internal void Reset()
    {
        lock (_throttleGate)
        {
            _lastHit = -1.0;
            _lastKill = -1.0;
        }
    }

    // Mirrors HitSoundPolicy.IntervalAllows without making the state object depend on the static policy
    // instance (keeps the test able to drive the gate directly).
    private static bool IntervalAllows(double now, double lastPlayed, double interval)
    {
        if (double.IsNaN(now) || double.IsInfinity(now)) return false;
        if (double.IsNaN(lastPlayed) || double.IsInfinity(lastPlayed) || lastPlayed < 0.0) return true;
        return now - lastPlayed >= (interval < 0.0 || double.IsNaN(interval) ? 0.0 : interval);
    }
}
