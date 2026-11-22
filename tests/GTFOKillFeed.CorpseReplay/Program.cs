using System;
using UnityEngine;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using HawChow.GTFOKillFeed;

int cases = 0;
foreach (var before in Enum.GetValues<CollisionDetectionMode>())
foreach (bool start in new[] { false, true })
foreach (var desired in Enum.GetValues<CollisionDetectionMode>())
foreach (bool end in new[] { false, true })
{
    if (start && !Rigidbody.Supported(before)) continue;
    var rb = new Rigidbody(start, before);
    CorpseEngineBindings.ResetMotion(rb, end, desired);
    var expected = end && !Rigidbody.Supported(desired) ? CollisionDetectionMode.Discrete : desired;
    if (rb.isKinematic != end || rb.collisionDetectionMode != expected || rb.VelocityWrites != (start && end ? 0 : 2))
        throw new Exception($"bad transition {start}/{before} -> {end}/{desired}");
    cases++;
}
const int n = 20160;
var audio = new AudioClip { samples = n, channels = 1, frequency = 48000 };
var pcm = new Il2CppStructArray<float>(n);
CorpseEngineBindings.FillBurst(audio, pcm, n, 1, 48000);
if (audio.Submissions != 1 || audio.N != n || audio.Offset != 0 || audio.Data != pcm) throw new Exception("bad PCM submission");
void Reject(Action run, int submissions)
{
    try { run(); } catch (InvalidOperationException) { if (audio.Submissions == submissions) return; }
    throw new Exception("invalid PCM submission was not rejected at the right boundary");
}
Reject(() => CorpseEngineBindings.FillBurst(audio, pcm, n+1, 1, 48000), 1);
Reject(() => CorpseEngineBindings.FillBurst(audio, pcm, n, 1, 44100), 1);
Reject(() => CorpseEngineBindings.FillBurst(audio, pcm, n, 2, 48000), 1);
Reject(() => CorpseEngineBindings.FillBurst(audio, new Il2CppStructArray<float>(n-1), n, 1, 48000), 1);
audio.samples--;
Reject(() => CorpseEngineBindings.FillBurst(audio, pcm, n, 1, 48000), 1);
audio.samples++; audio.Accept = false;
Reject(() => CorpseEngineBindings.FillBurst(audio, pcm, n, 1, 48000), 2);
Console.WriteLine($"CORPSE_BINDINGS_REPLAY=PASS;transitions={cases};pcmFrames={n};audioCases=7;engine=boundary-double");

namespace Il2CppInterop.Runtime.InteropTypes.Arrays
{
    internal sealed class Il2CppStructArray<T> { public int Length { get; } public Il2CppStructArray(int n) => Length=n; }
}
namespace UnityEngine
{
    internal struct Vector3 { public static Vector3 zero => default; }
    internal enum CollisionDetectionMode { Discrete, Continuous, ContinuousDynamic, ContinuousSpeculative }
    internal sealed class Rigidbody
    {
        public static bool Supported(CollisionDetectionMode mode) => mode == CollisionDetectionMode.Discrete || mode == CollisionDetectionMode.ContinuousSpeculative;
        private bool kin; private CollisionDetectionMode mode;
        public int VelocityWrites;
        public Rigidbody(bool k, CollisionDetectionMode m) { kin=k; mode=m; }
        public bool isKinematic { get => kin; set { if(value && !Supported(mode)) throw new Exception("kinematic before safe CCD"); kin=value; } }
        public CollisionDetectionMode collisionDetectionMode { get => mode; set { if(kin && !Supported(value)) throw new Exception("swept CCD on kinematic"); mode=value; } }
        public Vector3 velocity { set { if(kin) throw new Exception("kinematic velocity"); VelocityWrites++; } }
        public Vector3 angularVelocity { set { if(kin) throw new Exception("kinematic angular velocity"); VelocityWrites++; } }
    }
    internal sealed class AudioClip
    {
        public int samples, channels, frequency, N, Offset, Submissions; public bool Accept=true; public Il2CppStructArray<float> Data;
        public static bool SetData(AudioClip clip, Il2CppStructArray<float> data, int frames, int offset)
        { clip.N=frames; clip.Data=data; clip.Offset=offset; clip.Submissions++; return clip.Accept; }
    }
}
