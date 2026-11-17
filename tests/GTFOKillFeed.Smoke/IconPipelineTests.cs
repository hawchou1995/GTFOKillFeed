using System;
using System.Collections.Generic;
using HawChow.GTFOKillFeed;

// Behavioural coverage for the icon slot pipeline that production actually runs (IconTextureCache
// is only a Unity adapter over it). Fake IO/sink/runner drive the same coordinator, so these tests
// exercise the real decisions: when to probe, when to read, when to keep the previous texture,
// how a failure cycle backs off and how a replaced file releases the old texture.
internal static class IconPipelineTests
{
    internal sealed class FakeIo : IIconIo
    {
        internal sealed class Entry
        {
            internal DateTime WriteUtc;
            internal byte[] Bytes;
            internal string ProbeError;
        }

        private readonly Dictionary<string, Entry> _files = new(StringComparer.Ordinal);

        internal int Probes;
        internal int Reads;
        internal string LastProbePath;

        internal void Put(string path, DateTime stamp, byte[] bytes)
            => _files[path] = new Entry { WriteUtc = stamp, Bytes = bytes };

        internal void Remove(string path) => _files.Remove(path);

        internal void FailProbe(string path, string error)
            => _files[path] = new Entry { ProbeError = error };

        public bool TryProbe(string path, out DateTime writeUtc, out long length, out string error)
        {
            Probes++;
            LastProbePath = path;
            writeUtc = DateTime.MinValue;
            length = -1;
            error = null;
            if (path == null || !_files.TryGetValue(path, out var entry))
            {
                error = "missing";
                return false;
            }
            if (entry.ProbeError != null)
            {
                error = entry.ProbeError;
                return false;
            }
            writeUtc = entry.WriteUtc;
            length = entry.Bytes == null ? 0 : entry.Bytes.Length;
            return true;
        }

        public byte[] TryRead(string path, long maximumBytes, out string error)
        {
            Reads++;
            error = null;
            if (path == null || !_files.TryGetValue(path, out var entry) || entry.Bytes == null || entry.Bytes.Length == 0)
            {
                error = "read failed";
                return null;
            }
            if (entry.Bytes.Length > maximumBytes)
            {
                error = "file too large";
                return null;
            }
            return entry.Bytes;
        }
    }

    internal sealed class FakeSink : IIconSink
    {
        internal int Created;
        internal int Released;
        internal int Live => Created - Released;
        internal bool RejectNext;

        public object Create(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0 || RejectNext) return null;
            Created++;
            return new object();
        }

        public void Release(object texture)
        {
            if (texture != null) Released++;
        }
    }

    internal sealed class InlineRunner : IIconJobRunner
    {
        internal int Jobs;
        public void Run(Action job)
        {
            Jobs++;
            job();
        }
    }

    private static readonly DateTime Base = new(2026, 2, 3, 4, 5, 6, DateTimeKind.Utc);

    private static IconPipeline Build(FakeIo io, FakeSink sink, InlineRunner runner, List<string> warnings)
        => new(io, sink, runner, message => warnings.Add(message));

    // A Tick only schedules; the inline runner completes it, so one more Tick applies the result.
    private static void Pump(IconPipeline pipeline, string key, string path, float now)
    {
        pipeline.Tick(key, path, now);
        pipeline.Tick(key, path, now);
    }

    internal static void Run(Action<bool, string> check)
    {
        string pathA = @"C:\mod\Assets\icons\mark-a.png";
        string pathB = @"D:\pics\mine.png";
        byte[] bytesA = { 1, 2, 3, 4 };
        byte[] bytesB = { 9, 8, 7, 6, 5 };

        // ---- first load, then an unchanged file -------------------------------------------
        var io = new FakeIo();
        var sink = new FakeSink();
        var runner = new InlineRunner();
        var warnings = new List<string>();
        var pipeline = Build(io, sink, runner, warnings);
        io.Put(pathA, Base, bytesA);
        Pump(pipeline, "mark", pathA, 0f);
        check(pipeline.Texture("mark") != null && sink.Created == 1 && sink.Released == 0,
            "pipeline: the first probe loads the icon and keeps it");
        check(io.Reads == 1, "pipeline: exactly one read per successful load");
        int probesAfterLoad = io.Probes;
        pipeline.Tick("mark", pathA, 0.2f);
        pipeline.Tick("mark", pathA, 0.49f);
        check(io.Probes == probesAfterLoad,
            "pipeline: an unchanged icon is not probed again inside the 0.5 s window");
        Pump(pipeline, "mark", pathA, 0.55f);
        check(io.Probes == probesAfterLoad + 1 && io.Reads == 1,
            "pipeline: after the window it re-probes, sees the same stamp and does not re-read");

        // ---- same path, replaced content ----------------------------------------------------
        object firstTexture = pipeline.Texture("mark");
        io.Put(pathA, Base.AddSeconds(1), bytesB);
        Pump(pipeline, "mark", pathA, 1.10f);
        check(!ReferenceEquals(pipeline.Texture("mark"), firstTexture) && sink.Created == 2,
            "pipeline: replacing the file content behind the same path swaps the texture");
        check(sink.Released == 1 && sink.Live == 1,
            "pipeline: a successful swap releases exactly the previous texture");

        // ---- key points at a different file (A -> B) ---------------------------------------
        io.Put(pathB, Base.AddSeconds(2), bytesB);
        Pump(pipeline, "mark", pathB, 1.70f);
        check(pipeline.LoadedPath("mark") == pathB && sink.Created == 3,
            "pipeline: the same key pointing at a new path reloads from that path");
        check(sink.Live == 1, "pipeline: swapping paths never leaks a second live texture");

        // ---- corrupt / half-written replacement -------------------------------------------
        object good = pipeline.Texture("mark");
        sink.RejectNext = true;
        io.Put(pathB, Base.AddSeconds(3), new byte[] { 0 });
        Pump(pipeline, "mark", pathB, 2.30f);
        check(ReferenceEquals(pipeline.Texture("mark"), good),
            "pipeline: a corrupt replacement keeps the last good texture on screen");
        check(pipeline.FailureCount("mark") > 0, "pipeline: a corrupt replacement counts as a failure");
        sink.RejectNext = false;
        int probesBeforeCorruptRetry = io.Probes;
        pipeline.Tick("mark", pathB, 2.40f);
        check(io.Probes == probesBeforeCorruptRetry,
            "pipeline: a failure is not retried before its backoff");
        Pump(pipeline, "mark", pathB, 3.0f);
        check(!ReferenceEquals(pipeline.Texture("mark"), good) && sink.Created == 4,
            "pipeline: the retry after the backoff applies the fixed file");
        check(pipeline.FailureCount("mark") == 0, "pipeline: a successful load resets the failure cycle");

        // ---- missing file: bounded probes and bounded warnings ------------------------------
        var missingIo = new FakeIo();
        var missingSink = new FakeSink();
        var missingWarnings = new List<string>();
        var missing = Build(missingIo, missingSink, new InlineRunner(), missingWarnings);
        float clock = 0f;
        for (int i = 0; i < 25; i++)
        {
            missing.Tick("mark", @"C:\mod\gone.png", clock);
            clock += 0.5f;
        }
        check(missingIo.Probes <= 8,
            "pipeline: 25 frames of a missing icon stay under the backoff instead of probing every 0.5 s");
        check(missingWarnings.Count == 1,
            "pipeline: a missing icon warns once per failure cycle, never once per frame");
        check(missing.Texture("mark") == null, "pipeline: nothing is ever applied for a missing icon");

        // ---- recovery resets the cycle -----------------------------------------------------
        var recoverIo = new FakeIo();
        var recoverSink = new FakeSink();
        var recoverWarnings = new List<string>();
        var recover = Build(recoverIo, recoverSink, new InlineRunner(), recoverWarnings);
        float t = 0f;
        for (int i = 0; i < 12; i++)
        {
            recover.Tick("mark", pathA, t);
            t += 0.5f;
        }
        check(recoverWarnings.Count == 1, "pipeline: repeated failures keep a single warning");
        recoverIo.Put(pathA, Base, bytesA);
        for (int i = 0; i < 40 && recover.Texture("mark") == null; i++)
        {
            Pump(recover, "mark", pathA, t);
            t += 0.5f;
        }
        check(recover.Texture("mark") != null && recover.FailureCount("mark") == 0,
            "pipeline: once the file appears the icon loads and the failure cycle resets");
        check(recoverWarnings.Count == 1, "pipeline: recovery itself does not add warnings");

        // ---- network paths are refused without touching IO ---------------------------------
        var uncIo = new FakeIo();
        var uncWarnings = new List<string>();
        var unc = Build(uncIo, new FakeSink(), new InlineRunner(), uncWarnings);
        Pump(unc, "mark", @"\\server\share\mark.png", 0f);
        check(uncIo.Probes == 0 && uncIo.Reads == 0,
            "pipeline: a UNC/network icon path is refused before any IO (it can block the worker)");
        check(uncWarnings.Count == 1 && unc.Texture("mark") == null,
            "pipeline: the refused network path warns once and keeps the built-in art");

        // ---- a result for the old path is dropped after a switch ---------------------------
        var staleIo = new FakeIo();
        var staleSink = new FakeSink();
        var deferred = new DeferredRunner();
        var stale = new IconPipeline(staleIo, staleSink, deferred, _ => { });
        staleIo.Put(pathA, Base, bytesA);
        staleIo.Put(pathB, Base, bytesB);
        stale.Tick("mark", pathA, 0f);      // job queued, not run yet
        stale.Tick("mark", pathB, 0.01f);   // the path switches while the A job is still in flight
        deferred.Flush();                   // deliver the A result anyway
        Pump(stale, "mark", pathB, 0.02f);
        check(stale.LoadedPath("mark") != pathA,
            "pipeline: a result for a path that is no longer configured is discarded");
    }

    // Runner that holds jobs until the test releases them, proving stale results are dropped.
    internal sealed class DeferredRunner : IIconJobRunner
    {
        private readonly List<Action> _pending = new();

        public void Run(Action job) { _pending.Add(job); }

        internal void Flush()
        {
            var copy = _pending.ToArray();
            _pending.Clear();
            foreach (var job in copy) job();
        }
    }
}
