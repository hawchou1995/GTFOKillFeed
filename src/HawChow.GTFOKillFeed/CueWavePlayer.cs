using System;
using System.Runtime.InteropServices;

namespace HawChow.GTFOKillFeed;

// Plays the shipped PCM cues through Win32 waveOut. GTFO never touches UnityEngine.AudioSource /
// AudioClip / AudioListener (verified against the interop assemblies), so a Unity AudioSource would
// be silent in this game; waveOut is the path that does not depend on either Wwise or Unity audio.
//
// Allocation discipline: the device handles, the unmanaged mixing buffer and the WAVEHDR are created once
// per open (Plugin.Load warms them up when HitSound/Enabled is true at load). Playing a cue copies the
// samples into that fixed buffer and re-arms the header, so the per-event cost is one bounded copy - no new
// objects, no scene searches, no per-frame work.
// Honest exception: with the cues switched OFF at load nothing is opened, so the FIRST cue after a hot
// enable does pay for six waveOutOpen calls and their allocations, inside that damage callback. Only the
// steady state is allocation-free.
internal static class CueWavePlayer
{
    internal const int VoiceCount = 6;
    internal const int MaxCueSamples = 30720; // 640 ms at 48 kHz, above the longest shipped cue
    internal const int SampleRate = 48000;
    internal const int Channels = 1;
    internal const int BitsPerSample = 16;

    private const uint WaveMapper = 0xFFFFFFFFu;
    private const uint CallbackNull = 0x00000000u;
    private const int RetryDelayMs = 5000;

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveFormatEx
    {
        internal ushort FormatTag;
        internal ushort ChannelCount;
        internal uint SamplesPerSecond;
        internal uint AverageBytesPerSecond;
        internal ushort BlockAlign;
        internal ushort BitsPerSample;
        internal ushort ExtraSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHeader
    {
        internal IntPtr Data;
        internal uint BufferLength;
        internal uint BytesRecorded;
        internal IntPtr User;
        internal uint Flags;
        internal uint Loops;
        internal IntPtr Next;
        internal IntPtr Reserved;
    }

    [DllImport("winmm.dll", SetLastError = true)]
    private static extern int waveOutOpen(out IntPtr handle, uint deviceId, ref WaveFormatEx format, IntPtr callback, IntPtr instance, uint flags);

    [DllImport("winmm.dll", SetLastError = true)]
    private static extern int waveOutPrepareHeader(IntPtr handle, IntPtr header, int headerSize);

    [DllImport("winmm.dll", SetLastError = true)]
    private static extern int waveOutWrite(IntPtr handle, IntPtr header, int headerSize);

    [DllImport("winmm.dll", SetLastError = true)]
    private static extern int waveOutReset(IntPtr handle);

    [DllImport("winmm.dll", SetLastError = true)]
    private static extern int waveOutUnprepareHeader(IntPtr handle, IntPtr header, int headerSize);

    [DllImport("winmm.dll", SetLastError = true)]
    private static extern int waveOutClose(IntPtr handle);

    private sealed class Voice
    {
        internal IntPtr Handle;
        internal IntPtr Header;
        internal IntPtr Data;
        internal bool Ready;
        internal long EndsAt; // Environment.TickCount64 deadline of the cue currently in flight
    }

    private static readonly Voice[] Voices = new Voice[VoiceCount];
    private static readonly short[] Mix = new short[MaxCueSamples];
    // The damage path is not guaranteed to be single-threaded (KillTracker keeps its re-entrancy scope
    // thread-static for exactly that reason), and Mix / the voice table / the device handles are shared
    // static state, so every device-touching call goes through one gate. Cost is one uncontended lock
    // per hit event, never per frame.
    private static readonly object Gate = new object();
    private static int _nextVoice;
    private static int _headerSize;
    private static bool _unavailable;
    private static long _retryAfter;

    internal static bool Available => !_unavailable;
    internal static int Failures { get; private set; }

    // Called from Plugin.Load so the six device opens never land inside a damage callback - but only when the
    // cues are enabled at load. With them off this is a no-op, and the first cue after a hot enable pays the
    // opens inside that damage callback (see the class comment: the steady state, not the first, is free).
    internal static void Warmup()
    {
        lock (Gate) EnsureOpen();
    }

    private static void EnsureOpen()
    {
        if (Voices[0] != null && Voices[0].Ready) return;
        long now = Environment.TickCount64;
        if (_unavailable && now < _retryAfter) return;
        _unavailable = false;
        ReleaseVoices();
        try
        {
            _headerSize = Marshal.SizeOf<WaveHeader>();
            var format = new WaveFormatEx
            {
                FormatTag = 1,
                ChannelCount = Channels,
                SamplesPerSecond = SampleRate,
                AverageBytesPerSecond = (uint)(SampleRate * Channels * BitsPerSample / 8),
                BlockAlign = (ushort)(Channels * BitsPerSample / 8),
                BitsPerSample = BitsPerSample,
                ExtraSize = 0,
            };
            for (int i = 0; i < VoiceCount; i++)
            {
                var voice = new Voice();
                Voices[i] = voice;
                if (waveOutOpen(out voice.Handle, WaveMapper, ref format, IntPtr.Zero, IntPtr.Zero, CallbackNull) != 0)
                {
                    Fail();
                    return;
                }
                voice.Data = Marshal.AllocHGlobal(MaxCueSamples * sizeof(short));
                voice.Header = Marshal.AllocHGlobal(_headerSize);
                var header = new WaveHeader { Data = voice.Data, BufferLength = (uint)(MaxCueSamples * sizeof(short)) };
                Marshal.StructureToPtr(header, voice.Header, false);
                if (waveOutPrepareHeader(voice.Handle, voice.Header, _headerSize) != 0)
                {
                    Fail();
                    return;
                }
                voice.Ready = true;
            }
        }
        catch (Exception)
        {
            Fail();
        }
    }

    // A failed open never becomes permanent: the half-opened voices are rolled back and the next call
    // after a short backoff tries again (a Bluetooth switch or an exclusive-mode grab is transient).
    private static void Fail()
    {
        Failures++;
        _unavailable = true;
        _retryAfter = Environment.TickCount64 + RetryDelayMs;
        ReleaseVoices();
    }

    private static void ReleaseVoices()
    {
        for (int i = 0; i < VoiceCount; i++)
        {
            var voice = Voices[i];
            Voices[i] = null;
            if (voice == null) continue;
            try
            {
                if (voice.Handle != IntPtr.Zero)
                {
                    waveOutReset(voice.Handle);
                    if (voice.Header != IntPtr.Zero) waveOutUnprepareHeader(voice.Handle, voice.Header, _headerSize);
                    waveOutClose(voice.Handle);
                    voice.Handle = IntPtr.Zero;
                }
            }
            catch (Exception) { }
            if (voice.Header != IntPtr.Zero) { try { Marshal.FreeHGlobal(voice.Header); } catch (Exception) { } voice.Header = IntPtr.Zero; }
            if (voice.Data != IntPtr.Zero) { try { Marshal.FreeHGlobal(voice.Data); } catch (Exception) { } voice.Data = IntPtr.Zero; }
            voice.Ready = false;
            voice.EndsAt = 0;
        }
        _nextVoice = 0;
    }

    // samples is the decoded cue; count its length. volume is already clamped by the policy.
    internal static bool Play(short[] samples, int count, float volume)
    {
        if (samples == null || count <= 0) return false;
        lock (Gate)
        {
            EnsureOpen();
            if (_unavailable) return false;
            if (count > MaxCueSamples) count = MaxCueSamples;
            if (count > samples.Length) count = samples.Length;
            var voice = PickVoice(Environment.TickCount64);
            if (voice == null) return false;
            try
            {
                // Stop before touching the buffer: waveOutReset returns with the header done, so the
                // device can no longer be reading the samples this voice is about to overwrite.
                waveOutReset(voice.Handle);
                for (int i = 0; i < count; i++)
                {
                    int value = (int)(samples[i] * volume);
                    if (value > short.MaxValue) value = short.MaxValue;
                    else if (value < short.MinValue) value = short.MinValue;
                    Mix[i] = (short)value;
                }
                Marshal.Copy(Mix, 0, voice.Data, count);
                var header = Marshal.PtrToStructure<WaveHeader>(voice.Header);
                header.BufferLength = (uint)(count * sizeof(short));
                header.BytesRecorded = 0;
                Marshal.StructureToPtr(header, voice.Header, false);
                if (waveOutWrite(voice.Handle, voice.Header, _headerSize) != 0)
                {
                    Failures++;
                    return false;
                }
                voice.EndsAt = Environment.TickCount64 + (count * 1000L / SampleRate) + 40;
                return true;
            }
            catch (Exception)
            {
                Failures++;
                return false;
            }
        }
    }

    // Round-robin over the voices that have finished; when every voice is still playing, the one with
    // the shortest remaining cue is reused (the game cuts the least audio that way) instead of always
    // cutting the oldest slot.
    private static Voice PickVoice(long now)
    {
        for (int i = 0; i < VoiceCount; i++)
        {
            int index = (_nextVoice + i) % VoiceCount;
            var voice = Voices[index];
            if (voice != null && voice.Ready && voice.EndsAt <= now)
            {
                _nextVoice = (index + 1) % VoiceCount;
                return voice;
            }
        }
        Voice soonest = null;
        int soonestIndex = -1;
        for (int i = 0; i < VoiceCount; i++)
        {
            var voice = Voices[i];
            if (voice == null || !voice.Ready) continue;
            if (soonest == null || voice.EndsAt < soonest.EndsAt)
            {
                soonest = voice;
                soonestIndex = i;
            }
        }
        if (soonestIndex >= 0) _nextVoice = (soonestIndex + 1) % VoiceCount;
        return soonest;
    }

    internal static void Shutdown()
    {
        lock (Gate)
        {
            ReleaseVoices();
            _unavailable = false;
            _retryAfter = 0;
        }
    }
}
