using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using HawChow.GTFOKillFeed;

// The shipped cue set, measured on the bytes the plugin will actually embed: every resource the policy
// can name must exist, decode as PCM16 mono 48 kHz, fit CueWavePlayer.MaxCueSamples and be an actually
// different waveform (two names hiding the same audio would make the flight/tier claim untrue).
internal static class HitSoundAssetTests
{
    // Mirrors CueWavePlayer.MaxCueSamples (that file needs waveOut, so it is not linked into this test
    // assembly); Test-HitSoundIl.ps1 pins the compiled constant to the same number.
    private const int MaxCueSamples = 30720;
    private const int SampleRate = 48000;
    // The cue set is derived from the single code-side count (25 cues) plus the corpse burst.
    private const int ExpectedResources = HitSoundPolicy.ShippedCueNames + 1;

    internal static void Run(Action<bool, string> check)
    {
        Assembly assembly = typeof(HitSoundAssetTests).Assembly;
        var names = new List<string>();
        foreach (string resource in assembly.GetManifestResourceNames())
            if (resource.StartsWith("KillFeed.Audio.", StringComparison.Ordinal)) names.Add(resource);
        names.Sort(StringComparer.Ordinal);

        check(names.Count == ExpectedResources,
            "hit-sound-assets: the assembly embeds exactly " + ExpectedResources + " audio resources (25 cues + corpse burst); found " + names.Count);

        var missing = new List<string>();
        var styles = new[] { HitSoundStyle.Bf1, HitSoundStyle.Bf5, HitSoundStyle.Bf6 };
        var kinds = new[] { HitCueKind.Hit, HitCueKind.WeakspotHit, HitCueKind.ArmorHit, HitCueKind.Kill };
        var tiers = new[] { KillTier.Minor, KillTier.Elite, KillTier.Boss };
        var flights = new[] { KillFlight.None, KillFlight.Flying, KillFlight.ArmoredFlying };
        var wanted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var style in styles)
            foreach (var kind in kinds)
                foreach (var tier in tiers)
                    foreach (var flight in flights)
                    {
                        string name = HitSoundPolicy.ResourceName(style, kind, tier, flight);
                        wanted.Add(name);
                        if (!names.Contains(name)) missing.Add(name);
                        foreach (string step in HitSoundPolicy.CueChain(style, kind, tier, flight))
                            if (!names.Contains(step)) missing.Add(step);
                    }
        check(missing.Count == 0 && wanted.Count == HitSoundPolicy.ShippedCueNames,
            "hit-sound-assets: all 108 routing combinations point at one of the " + HitSoundPolicy.ShippedCueNames + " embedded cue files; missing "
            + (missing.Count == 0 ? "none" : string.Join(",", missing)));

        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        bool headersValid = true, sizesValid = true, levelsValid = true;
        string firstBad = string.Empty;
        var duplicates = new List<string>();
        foreach (string name in names)
        {
            byte[] bytes = Read(assembly, name);
            if (bytes == null || bytes.Length < 44)
            {
                headersValid = false;
                if (firstBad.Length == 0) firstBad = name + " (missing/short)";
                continue;
            }
            int format = BitConverter.ToUInt16(bytes, 20);
            int channels = BitConverter.ToUInt16(bytes, 22);
            int rate = BitConverter.ToInt32(bytes, 24);
            int bits = BitConverter.ToUInt16(bytes, 34);
            int dataSize = BitConverter.ToInt32(bytes, 40);
            if (format != 1 || channels != 1 || bits != 16 || rate != SampleRate || dataSize <= 0 || 44 + dataSize > bytes.Length)
            {
                headersValid = false;
                if (firstBad.Length == 0) firstBad = name + " (not PCM16 mono 48k)";
                continue;
            }
            int samples = dataSize / 2;
            if (samples > MaxCueSamples)
            {
                sizesValid = false;
                if (firstBad.Length == 0) firstBad = name + " (" + samples + " samples)";
            }
            double milliseconds = samples * 1000.0 / SampleRate;
            if (milliseconds < 40.0 || milliseconds > 600.0) sizesValid = false;
            int peak = 0;
            for (int i = 0; i < samples; i++)
            {
                int value = Math.Abs((int)BitConverter.ToInt16(bytes, 44 + 2 * i));
                if (value > peak) peak = value;
            }
            if (peak < 16384) levelsValid = false;   // every cue must peak above -6 dBFS
            string hash;
            using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(bytes));
            if (hashes.TryGetValue(hash, out string other)) duplicates.Add(other + " == " + name);
            else hashes[hash] = name;
        }
        check(headersValid, "hit-sound-assets: every embedded cue is canonical PCM16 mono 48 kHz" + (headersValid ? string.Empty : " FIRST BAD " + firstBad));
        check(sizesValid, "hit-sound-assets: every cue fits CueWavePlayer.MaxCueSamples (" + MaxCueSamples + " = 640 ms) and lasts 40-600 ms");
        check(levelsValid, "hit-sound-assets: every cue is normalised and audible (peak above -6 dBFS)");
        check(duplicates.Count == 0,
            "hit-sound-assets: no two cue names hide the same bytes, so flight/tier/armor differences are real audio"
            + (duplicates.Count == 0 ? string.Empty : " DUPLICATES " + string.Join("; ", duplicates)));
    }

    private static byte[] Read(Assembly assembly, string resource)
    {
        try
        {
            using Stream stream = assembly.GetManifestResourceStream(resource);
            if (stream == null) return null;
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }
        catch (Exception) { return null; }
    }
}
