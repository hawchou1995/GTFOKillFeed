using System;
using System.Collections.Generic;
using System.Threading;

namespace HawChow.GTFOKillFeed;

// Injectable file access. Both members run on the worker, never on the Unity main thread, because a
// dropped UNC share or a slow disk can block for seconds.
internal interface IIconIo
{
    bool TryProbe(string path, out DateTime writeUtc, out long length, out string error);
    byte[] TryRead(string path, long maximumBytes, out string error);
}

// Injectable texture owner. Called on the main thread only: Unity textures cannot be created or
// destroyed anywhere else.
internal interface IIconSink
{
    object Create(byte[] bytes);
    void Release(object texture);
}

internal interface IIconJobRunner
{
    void Run(Action job);
}

internal sealed class ThreadPoolIconJobRunner : IIconJobRunner
{
    internal static readonly ThreadPoolIconJobRunner Instance = new();

    public void Run(Action job)
    {
        if (job == null) return;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try { job(); }
            catch { }
        });
    }
}

// End-to-end icon slot coordinator: "should I look at the file, should I read it, is the result
// still current, what happens to the previous texture". It owns no engine types, so tests drive
// exactly this production path through fake IO/sink/runner objects.
internal sealed class IconPipeline
{
    internal const long MaxIconBytes = 8L * 1024 * 1024;
    private const int MaxResultsPerTick = 4;

    private sealed class Slot
    {
        internal readonly AssetReloadState State = new();
        internal object Texture;
        internal string LoadedPath;
        internal float NextProbeAt;
        internal bool Warned;
        internal DateTime PendingWriteUtc;
        internal long PendingLength = -1;
    }

    private sealed class Job
    {
        internal string Key;
        internal string Path;
        internal bool ReadPhase;
        internal DateTime WriteUtc;
        internal long Length;
        internal bool Ok;
        internal byte[] Bytes;
        internal string Error;
        internal int Generation;
    }

    private readonly IIconIo _io;
    private readonly IIconSink _sink;
    private readonly IIconJobRunner _runner;
    private readonly Action<string> _warn;
    private readonly Dictionary<string, Slot> _slots = new();
    private readonly Queue<Job> _done = new();
    private readonly object _gate = new();
    private bool _inFlight;
    private int _generation;

    internal IconPipeline(IIconIo io, IIconSink sink, IIconJobRunner runner, Action<string> warn)
    {
        _io = io;
        _sink = sink;
        _runner = runner ?? ThreadPoolIconJobRunner.Instance;
        _warn = warn;
    }

    internal bool InFlight
    {
        get { lock (_gate) return _inFlight; }
    }

    internal object Texture(string key)
        => key != null && _slots.TryGetValue(key, out var slot) ? slot.Texture : null;

    internal string LoadedPath(string key)
        => key != null && _slots.TryGetValue(key, out var slot) ? slot.LoadedPath : null;

    internal int FailureCount(string key)
        => key != null && _slots.TryGetValue(key, out var slot) ? slot.State.Failures : 0;

    internal int SlotCount => _slots.Count;

    // Main thread. Never blocks: work is handed to the injected runner and applied on later ticks.
    internal void Tick(string key, string path, float now)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        var slot = SlotFor(key);
        DrainResults(now);
        if (string.IsNullOrWhiteSpace(path)) return;
        // A configured path change invalidates any in-flight work for the previous path, even while a
        // job is still running, so a late result can never be applied to the new slot.
        if (!string.Equals(slot.State.Path, path, StringComparison.Ordinal)) GenerationFor(key, path);
        if (_inFlight) return;
        if (!HotReloadPolicy.ShouldProbe(now, slot.NextProbeAt)) return;
        if (TeammateMarkPolicy.IsNetworkPath(path))
        {
            // A UNC/network icon would block the probe itself; refuse it explicitly and keep the
            // bundled/built-in art instead of hanging the HUD.
            WarnOnce(slot, "network icon path refused: " + HotReloadPolicy.SafeFileName(path));
            slot.NextProbeAt = now + AssetReloadState.FailureRetryCeiling;
            return;
        }
        slot.NextProbeAt = now + ProbeInterval(slot.State);
        StartProbe(key, path);
    }

    private static float ProbeInterval(AssetReloadState state)
    {
        float backoff = state.Failures <= 0 ? 0f : AssetReloadState.RetryDelay(state.Failures);
        return backoff > HotReloadPolicy.FileProbeSeconds ? backoff : HotReloadPolicy.FileProbeSeconds;
    }

    private Slot SlotFor(string key)
    {
        if (_slots.TryGetValue(key, out var slot)) return slot;
        slot = new Slot();
        _slots[key] = slot;
        return slot;
    }

    private void StartProbe(string key, string path)
    {
        int generation = GenerationFor(key, path);
        lock (_gate) _inFlight = true;
        _runner.Run(() =>
        {
            var job = new Job { Key = key, Path = path, Generation = generation };
            try
            {
                job.Ok = _io.TryProbe(path, out DateTime stamp, out long length, out string error);
                job.WriteUtc = stamp;
                job.Length = length;
                job.Error = error;
            }
            catch (Exception error) { job.Ok = false; job.Error = error.GetType().Name; }
            Complete(job);
        });
    }

    private void StartRead(string key, string path, DateTime writeUtc, long length)
    {
        int generation = GenerationFor(key, path);
        var slot = SlotFor(key);
        slot.PendingWriteUtc = writeUtc;
        slot.PendingLength = length;
        lock (_gate) _inFlight = true;
        _runner.Run(() =>
        {
            var job = new Job { Key = key, Path = path, ReadPhase = true, Generation = generation };
            try
            {
                job.Bytes = _io.TryRead(path, MaxIconBytes, out string error);
                job.Error = error;
                job.Ok = job.Bytes != null && job.Bytes.Length > 0;
            }
            catch (Exception error) { job.Ok = false; job.Error = error.GetType().Name; }
            Complete(job);
        });
    }

    private void Complete(Job job)
    {
        lock (_gate)
        {
            _inFlight = false;
            if (_done.Count < 32) _done.Enqueue(job);
        }
    }

    private int GenerationFor(string key, string path)
    {
        var slot = SlotFor(key);
        if (!string.Equals(slot.State.Path, path, StringComparison.Ordinal))
        {
            // A new path invalidates anything already running for the old one.
            _generation++;
            slot.State.Reset();
            slot.State.Path = path;
        }
        return _generation;
    }

    private void DrainResults(float now)
    {
        for (int i = 0; i < MaxResultsPerTick; i++)
        {
            Job job;
            lock (_gate)
            {
                if (_done.Count == 0) return;
                job = _done.Dequeue();
            }
            if (job == null || job.Key == null) continue;
            var slot = SlotFor(job.Key);
            if (job.Generation != _generation) continue;
            if (!string.Equals(slot.State.Path, job.Path, StringComparison.Ordinal)) continue;
            if (job.ReadPhase) ApplyRead(slot, job, now);
            else ApplyProbe(job, now);
        }
    }

    private void ApplyProbe(Job job, float now)
    {
        var slot = SlotFor(job.Key);
        if (!job.Ok)
        {
            MarkFailure(slot, now, string.IsNullOrEmpty(job.Error) ? "probe failed" : job.Error);
            return;
        }
        if (slot.State.Evaluate(job.Path, job.WriteUtc, job.Length, now) != AssetReloadAction.Reload) return;
        slot.State.MarkAttempt(now);
        StartRead(job.Key, job.Path, job.WriteUtc, job.Length);
    }

    private void ApplyRead(Slot slot, Job job, float now)
    {
        if (!job.Ok)
        {
            MarkFailure(slot, now, string.IsNullOrEmpty(job.Error) ? "read failed" : job.Error);
            return;
        }
        object texture = null;
        try { texture = _sink.Create(job.Bytes); }
        catch (Exception error) { MarkFailure(slot, now, "decode threw " + error.GetType().Name); return; }
        if (texture == null)
        {
            MarkFailure(slot, now, "not a decodable PNG");
            return;
        }
        object previous = slot.Texture;
        slot.Texture = texture;
        slot.LoadedPath = job.Path;
        slot.State.MarkLoaded(job.Path, slot.PendingWriteUtc, slot.PendingLength, now);
        // Exactly one texture is released per successful swap.
        if (previous != null)
        {
            try { _sink.Release(previous); } catch { }
        }
        slot.Warned = false;
    }

    private void MarkFailure(Slot slot, float now, string reason)
    {
        slot.State.MarkFailed(now);
        slot.NextProbeAt = now + ProbeInterval(slot.State);
        WarnOnce(slot, "icon reload failed: " + reason);
    }

    private void WarnOnce(Slot slot, string message)
    {
        if (slot.Warned) return;
        slot.Warned = true;
        try { _warn?.Invoke(message); } catch { }
    }
}
