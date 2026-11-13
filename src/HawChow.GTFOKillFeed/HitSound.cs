using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace HawChow.GTFOKillFeed;

// The BF1/BF5/BF6 hit, weakspot-hit and kill cues, plus the opt-in silencing of the vanilla
// hit-confirmation indicator layer. Everything here is event driven: the cue is fired from the same
// damage path that already draws the number/kill card, and the mute decision is a Harmony prefix on
// CellSound.Post, so no new game event source and no per-frame work is added.
//
// Threading (review A1): the damage path is not guaranteed to be main-thread only (DamageSync applies
// damage from network callbacks) and CellSound.Post runs at the game's own audio call site, so every
// mutable field is either thread-safe by construction (ConcurrentDictionary) or guarded by the single
// gate inside HitSoundState. Nothing here holds a lock while calling back into the game.
internal static class HitSound
{
    private static readonly HitSoundState State = new();
    // Decoded cues are shared; a lost decode race only decodes the same bytes twice and is discarded.
    private static readonly ConcurrentDictionary<string, short[]> CueCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, byte> ReportedMissing = new(StringComparer.Ordinal);
    private static int _deviceFailures;

    internal static int Played => State.Played;
    internal static int MutedIdCount => State.MutedIds.Length;
    internal static int UnresolvedEventIds => State.ResolveFailures;

    // ---- mute ----------------------------------------------------------------------------------
    // Ids are read once from AK.EVENTS on the first Post call: the interop exposes the generated event
    // ids as static uint properties, so the value always matches the installed build even when a patch
    // renumbers them. A silent failure is impossible: the count is logged and a partially resolved set
    // simply mutes fewer events than requested.
    //
    // Only AK.EVENTS is parsed (verified against the Test-profile interop, see
    // reports/feature-1.2.19-review-fixes.md): BULLETHITENEMYLOCALINDICATOR and
    // BULLETHITENEMYWEAKSPOTLOCALINDICATOR live there, while BULLETHITENEMYINDICATOR exists only on
    // AK.BUSSES. BUSSES is a bus table (its siblings are HUD, ENEMIES, VOICE, WEAPONS*, ...) and
    // CellSound.Post(uint) takes an EVENT id, so a bus number is not an event id and feeding one into
    // this filter could only match by coincidence - and would then silence an unrelated event. The third
    // name therefore stays in the whitelist, is reported as unresolved, the vanilla layer keeps playing,
    // and we do NOT claim that event is muted.
    private static void ResolveMutedIds()
    {
        // Resolution runs exactly once, even if two threads reach the first Post together.
        if (!State.TryBeginResolve()) return;
        try
        {
            Type events = null;
            try { events = typeof(AK.EVENTS); }
            catch (Exception) { events = null; }
            if (events == null)
            {
                State.PublishResolved(Array.Empty<uint>(), HitSoundPolicy.MutedEventNames.Length);
                LogWarn("[HITSOUND] AK.EVENTS is not resolvable; the vanilla indicator layer stays audible");
                return;
            }
            // Every name is still probed individually against AK.EVENTS, so a build that promotes the bus name to a
            // real event joins the mute set with no code change.
            var resolution = HitSoundPolicy.ResolveIndicators(HitSoundPolicy.MutedEventNames,
                name => TryReadEventId(events, name, out uint id) ? (uint?)id : null);
            // State.ResolveFailures keeps counting every unresolved name of the original three.
            State.PublishResolved(resolution.Ids, resolution.Unresolved);
            // 1.2.18: the known bus name is EXPECTED to be absent from AK.EVENTS. Info, not warning - and no claim of
            // silence: we cannot see whether that bus carries an indicator sound of its own.
            if (resolution.OptionalBusMissing)
                LogInfo("[HITSOUND] " + HitSoundPolicy.OptionalBusIndicatorName + " is an AK.BUSSES property in this build, not an AK.EVENTS event id; it is not muted (that bus may or may not carry an indicator sound, so this does not prove the vanilla indicator is silent)");
            // Any other unresolved name is a real event-id miss and keeps the warning.
            if (resolution.HasMissingEvent)
                LogWarn("[HITSOUND] " + resolution.MissingEvents.Length + " indicator event id(s) not found on AK.EVENTS (" + string.Join(",", resolution.MissingEvents) + "); those events stay audible");
            if (resolution.ResolvedNames.Length > 0)
                LogInfo("[HITSOUND] muted vanilla indicator events: " + string.Join(",", resolution.ResolvedNames));
        }
        catch (Exception error)
        {
            // A failed resolution must leave the stock indicators audible instead of half-muted.
            State.PublishResolved(Array.Empty<uint>(), HitSoundPolicy.MutedEventNames.Length);
            LogWarn("[HITSOUND] indicator id resolution failed (" + error.GetType().Name + "); the vanilla layer stays audible");
        }
    }

    private static bool TryReadEventId(Type events, string name, out uint id)
    {
        id = 0;
        try
        {
            PropertyInfo property = events.GetProperty(name, BindingFlags.Public | BindingFlags.Static);
            if (property != null && property.PropertyType == typeof(uint))
            {
                id = (uint)property.GetValue(null);
                return true;
            }
            FieldInfo field = events.GetField(name, BindingFlags.Public | BindingFlags.Static);
            if (field != null && field.FieldType == typeof(uint))
            {
                id = (uint)field.GetValue(null);
                return true;
            }
        }
        catch (Exception) { }
        return false;
    }

    // Called from the Harmony prefixes only. Reads the mute switch and nothing else, so turning the
    // cue player off can never un-mute the vanilla layer and vice versa.
    internal static bool ShouldRunPost(uint eventId)
    {
        ResolveMutedIds();
        uint[] muted = State.MutedIds;
        if (muted.Length == 0) return true;
        // Deliberately independent of HitSound/Enabled, Volume and Style (AC10): the shipped default is
        // MuteVanillaIndicator=false, so a player who never enables the cues still hears the stock
        // indicator. A missing config entry is treated as "off": the vanilla layer stays audible.
        bool mute = HitSoundPolicy.MuteRequested(Plugin.HitSoundMuteVanillaIndicator != null
            ? Plugin.HitSoundMuteVanillaIndicator.Value
            : (bool?)null);
        return !HitSoundPolicy.ShouldMute(mute, muted, eventId);
    }

    // ---- cues ----------------------------------------------------------------------------------
    internal static void OnLocalHit(HitKind kind)
    {
        SoundSettings sound = Current();
        if (!sound.HitPlays) return;
        PlayThrottled(false, HitSoundPolicy.KindOf(kind, false), KillTier.Minor, KillFlight.None, sound.HitVolume);
    }

    // tier and flight come from the one KillPresentation the tracker built at the credited lethal hit,
    // so the kill cue can never disagree with the tier the card/next-phase head HUD reads.
    internal static void OnLocalKill(KillTier tier, KillFlight flight)
    {
        SoundSettings sound = Current();
        if (!sound.KillPlays) return;
        PlayThrottled(true, HitCueKind.Kill, tier, flight, sound.KillVolume);
    }

    // One throttle gate per cue family: check the gap, play, remember the time - atomically, so two
    // threads can never both pass the gap check for the same window.
    private static void PlayThrottled(bool kill, HitCueKind kind, KillTier tier, KillFlight flight, float volume)
    {
        double now = Now();
        double interval = HitSoundPolicy.MinInterval(Plugin.HitSoundMinIntervalSeconds != null
            ? Plugin.HitSoundMinIntervalSeconds.Value : HitSoundPolicy.DefaultMinIntervalSeconds);
        State.TryPlay(kill, now, interval, () => PlayCue(kind, tier, flight, volume));
    }

    // Resolved once per event: the master switch and the per-channel switch/volume come from the same
    // snapshot, so a hot reload cannot make a hit audible that the same tick already treated as off.
    internal static SoundSettings Current() => Plugin.SoundNow();

    private static HitSoundStyle Style()
        => HitSoundPolicy.ParseStyle(Plugin.HitSoundStyle != null ? Plugin.HitSoundStyle.Value : HitSoundPolicy.DefaultStyle);

    private static double Now()
    {
        try { return Time.unscaledTime; }
        catch (Exception) { return 0.0; }
    }

    private static bool PlayCue(HitCueKind kind, KillTier tier, KillFlight flight, float volume)
    {
        string[] chain = HitSoundPolicy.CueChain(Style(), kind, tier, flight);
        for (int i = 0; i < chain.Length; i++)
        {
            short[] samples = LoadCue(chain[i]);
            if (samples == null || samples.Length == 0) continue;
            if (CueWavePlayer.Play(samples, samples.Length, volume))
            {
                return true;
            }
            if (System.Threading.Interlocked.Increment(ref _deviceFailures) == 1)
                LogWarn("[HITSOUND] no audio output could be opened; cues stay silent (game audio is unaffected)");
            return false;
        }
        return false;
    }

    private static short[] LoadCue(string resourceName)
    {
        if (CueCache.TryGetValue(resourceName, out short[] cached)) return cached;
        short[] decoded = ReadEmbeddedCue(resourceName);
        // First writer wins; the loser decoded the same bytes and simply drops its copy.
        short[] stored = CueCache.GetOrAdd(resourceName, decoded);
        if (stored == null && ReportedMissing.TryAdd(resourceName, 0))
            LogWarn("[HITSOUND] " + HitSoundPolicy.MissingCueReason + ": " + resourceName);
        return stored;
    }

    private static short[] ReadEmbeddedCue(string resourceName)
    {
        try
        {
            using Stream stream = typeof(HitSound).Assembly.GetManifestResourceStream(resourceName);
            if (stream == null) return null;
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            byte[] bytes = buffer.ToArray();
            return ParsePcm16Mono(bytes);
        }
        catch (Exception error)
        {
            LogWarn("[HITSOUND] cue read failed (" + resourceName + "): " + error.GetType().Name);
            return null;
        }
    }

    // Minimal RIFF reader: only the canonical PCM16 layout the build script writes is accepted, and
    // anything else is reported as a missing cue instead of being played as noise.
    internal static short[] ParsePcm16Mono(byte[] bytes)
    {
        if (bytes == null || bytes.Length < 44) return null;
        if (bytes[0] != (byte)'R' || bytes[1] != (byte)'I' || bytes[2] != (byte)'F' || bytes[3] != (byte)'F') return null;
        if (bytes[8] != (byte)'W' || bytes[9] != (byte)'A' || bytes[10] != (byte)'V' || bytes[11] != (byte)'E') return null;
        int offset = 12;
        int dataOffset = -1, dataLength = 0;
        int channels = 0, rate = 0, bits = 0, format = 0;
        while (offset + 8 <= bytes.Length)
        {
            int size = BitConverter.ToInt32(bytes, offset + 4);
            int body = offset + 8;
            if (size < 0 || body + size > bytes.Length) break;
            if (bytes[offset] == (byte)'f' && bytes[offset + 1] == (byte)'m' && bytes[offset + 2] == (byte)'t' && bytes[offset + 3] == (byte)' ')
            {
                format = BitConverter.ToUInt16(bytes, body);
                channels = BitConverter.ToUInt16(bytes, body + 2);
                rate = BitConverter.ToInt32(bytes, body + 4);
                bits = BitConverter.ToUInt16(bytes, body + 14);
            }
            else if (bytes[offset] == (byte)'d' && bytes[offset + 1] == (byte)'a' && bytes[offset + 2] == (byte)'t' && bytes[offset + 3] == (byte)'a')
            {
                dataOffset = body;
                dataLength = size;
            }
            offset = body + size + (size & 1);
        }
        if (dataOffset < 0 || dataLength < 2) return null;
        if (format != 1 || channels != 1 || bits != 16 || rate != CueWavePlayer.SampleRate) return null;
        int count = dataLength / 2;
        var samples = new short[count];
        Buffer.BlockCopy(bytes, dataOffset, samples, 0, count * 2);
        return samples;
    }

    internal static void Reset(string reason) => State.Reset();

    // Called from Plugin.Unload (BepInEx BasePlugin.Unload). Idempotent: the device handles, the HDRs and
    // the sample buffers are freed and the cache is dropped, so a plugin reload never leaks waveOut
    // handles. PlayCue re-opens the devices lazily if a cue somehow arrives afterwards.
    internal static void Shutdown()
    {
        try { CueWavePlayer.Shutdown(); }
        catch (Exception) { }
        CueCache.Clear();
        ReportedMissing.Clear();
        State.Reset();
    }

    private static void LogInfo(string message) => Plugin.Instance?.Log.LogInfo(message);

    private static void LogWarn(string message) => Plugin.Instance?.Log.LogWarning(message);
}

[HarmonyPatch(typeof(CellSound), "Post", new Type[] { typeof(uint) })]
internal static class CellSoundPostMutePatch
{
    [HarmonyPrefix]
    private static bool Prefix(uint eventID) => HitSound.ShouldRunPost(eventID);
}

[HarmonyPatch(typeof(CellSound), "Post", new Type[] { typeof(uint), typeof(Vector3) })]
internal static class CellSoundPostMutePositionPatch
{
    [HarmonyPrefix]
    // Harmony binds prefix parameters by NAME: the game's second parameter is called `pos`, and a
    // mismatched name makes the whole PatchAll fail (which is exactly what killed the plugin once).
    private static bool Prefix(uint eventID, Vector3 pos) => HitSound.ShouldRunPost(eventID);
}
