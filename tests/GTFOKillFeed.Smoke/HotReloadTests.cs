using System;
using System.IO;
using HawChow.GTFOKillFeed;

// Hot-reload coverage: the existing stable-window gate against real files (in-place edit, atomic
// replace, same-length rewrite, truncated write, restart), the asset stamp/backoff state used by
// the replaceable PNGs, the weakspot-colour cache and the new corpse burst switch policy.
internal static class HotReloadTests
{
    internal static void Run(Action<bool, string> check)
    {
        // Clean machines have no PI_SCRATCH_DIR: fall back to the OS temp directory. The test never
        // touches the real BepInEx config folder and never writes inside the project tree.
        string scratch = Environment.GetEnvironmentVariable("PI_SCRATCH_DIR");
        bool fromScratch = !string.IsNullOrWhiteSpace(scratch);
        if (!fromScratch) scratch = Path.GetTempPath();
        string dir = Path.Combine(scratch, "killfeed-hotreload-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            check(Directory.Exists(dir), "hotreload harness: the temporary directory exists (" + (fromScratch ? "PI_SCRATCH_DIR" : "system temp") + ")");
            check(dir.IndexOf("BepInEx", StringComparison.OrdinalIgnoreCase) < 0
                && dir.IndexOf("profiles", StringComparison.OrdinalIgnoreCase) < 0,
                "hotreload harness: the temporary directory is outside any BepInEx/profile config path");
            ConfigFileTests(check, dir);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
        AssetStateTests(check);
        ColorCacheTests(check);
        CorpseBurstSwitchTests(check);
    }

    private static void ConfigFileTests(Action<bool, string> check, string dir)
    {
        string cfg = Path.Combine(dir, "HawChow.GTFOKillFeed.cfg");
        File.WriteAllText(cfg, "[Corpse]\r\nExplosionFx = false\r\n");
        StampOf(cfg, out DateTime stamp0, out long length0);

        var gate = new ConfigHotReloadGate();
        gate.Seed(stamp0, length0);
        check(!gate.ShouldReload(stamp0, length0, 0f), "cfg: seeding the current file never triggers a reload");

        // In-place edit, then the configured 0.40 s stability window.
        File.WriteAllText(cfg, "[Corpse]\r\nExplosionFx = true\r\n[TeammateMark]\r\nIcon = Ruo\r\n");
        StampOf(cfg, out DateTime stamp1, out long length1);
        check(length1 != length0, "cfg: the in-place edit changed the file length (test precondition)");
        check(!gate.ShouldReload(stamp1, length1, 100f), "cfg: a fresh write only starts the debounce");
        check(!gate.ShouldReload(stamp1, length1, 100.39f), "cfg: nothing is applied before the 0.40 s window");
        check(gate.ShouldReload(stamp1, length1, 100.41f), "cfg: a stable write becomes reloadable");
        // A write that keeps changing inside the window resets the candidate.
        check(!gate.ShouldReload(stamp1, length1 + 5, 100.42f), "cfg: a further write inside the window restarts the debounce");
        check(!gate.ShouldReload(stamp1, length1 + 5, 100.81f), "cfg: the restarted window is honoured");
        check(gate.ShouldReload(stamp1, length1 + 5, 100.83f), "cfg: the restarted window completes");
        gate.MarkApplied(stamp1, length1 + 5);
        check(!gate.ShouldReload(stamp1, length1 + 5, 200f), "cfg: an applied write is not re-applied");

        // Atomic replacement: write beside the target, then move over it.
        string replacement = Path.Combine(dir, "cfg.partial");
        File.WriteAllText(replacement, "[Corpse]\r\nExplosionFx = true\r\n[TeammateMark]\r\nIcon = Cai\r\n");
        File.Move(replacement, cfg, true);
        File.SetLastWriteTimeUtc(cfg, DateTime.UtcNow.AddSeconds(5));
        StampOf(cfg, out DateTime stamp2, out long length2);
        check(stamp2 != stamp1, "cfg: the atomic replacement produced a new timestamp (test precondition)");
        check(!gate.ShouldReload(stamp2, length2, 300f), "cfg: an atomic replace starts a fresh debounce");
        check(gate.ShouldReload(stamp2, length2, 300.41f), "cfg: an atomic replace is applied after the window");
        gate.MarkApplied(stamp2, length2);

        // Same length, different content: the timestamp alone must be enough to detect it.
        File.WriteAllText(cfg, "[Corpse]\r\nExplosionFx = true\r\n[TeammateMark]\r\nIcon = Ruo\r\n");
        File.SetLastWriteTimeUtc(cfg, DateTime.UtcNow.AddSeconds(10));
        StampOf(cfg, out DateTime stamp3, out long length3);
        check(length3 == length2, "cfg: the same-length rewrite kept the byte count (test precondition)");
        check(stamp3 != stamp2, "cfg: the same-length rewrite still moved the timestamp");
        check(!gate.ShouldReload(stamp3, length3, 400f), "cfg: a same-length change starts a fresh debounce");
        check(gate.ShouldReload(stamp3, length3, 400.41f), "cfg: a same-length change is not missed");
        gate.MarkApplied(stamp3, length3);

        // Truncated / half-written file: detected, then the reader reports failure and backs off.
        File.WriteAllText(cfg, string.Empty);
        File.SetLastWriteTimeUtc(cfg, DateTime.UtcNow.AddSeconds(15));
        StampOf(cfg, out DateTime stamp4, out long length4);
        check(length4 == 0, "cfg: the truncation is visible as a zero-length file (test precondition)");
        check(!gate.ShouldReload(stamp4, length4, 500f), "cfg: a truncated file starts a fresh debounce");
        check(gate.ShouldReload(stamp4, length4, 500.41f), "cfg: a truncated file is detected");
        gate.MarkFailed(500.41f);
        check(!gate.ShouldReload(stamp4, length4, 500.9f), "cfg: after a failed reload the gate backs off for 1 s");
        check(gate.ShouldReload(stamp4, length4, 501.42f), "cfg: the failed reload is retried after the backoff");
        // Restoring real content recovers.
        File.WriteAllText(cfg, "[Corpse]\r\nExplosionFx = true\r\n");
        File.SetLastWriteTimeUtc(cfg, DateTime.UtcNow.AddSeconds(20));
        StampOf(cfg, out DateTime stamp5, out long length5);
        gate.MarkApplied(stamp5, length5);
        check(!gate.ShouldReload(stamp5, length5, 600f), "cfg: recovered content is stable again");

        // Restart: a new gate seeded from the live file must not reload on the first tick.
        var restarted = new ConfigHotReloadGate();
        restarted.Seed(stamp5, length5);
        check(!restarted.ShouldReload(stamp5, length5, 700f), "cfg: a plugin restart does not reload unchanged config");
        File.WriteAllText(cfg, "[Corpse]\r\nExplosionFx = false\r\n");
        File.SetLastWriteTimeUtc(cfg, DateTime.UtcNow.AddSeconds(25));
        StampOf(cfg, out DateTime stamp6, out long length6);
        check(!restarted.ShouldReload(stamp6, length6, 800f), "cfg: after a restart an edit still needs the window");
        check(restarted.ShouldReload(stamp6, length6, 800.41f), "cfg: after a restart an edit applies once stable");
    }

    private static void AssetStateTests(Action<bool, string> check)
    {
        string path = @"C:\mod\Assets\icons\teammate-downed-cai.png";
        var stamp = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var state = new AssetReloadState();

        check(state.Evaluate(path, stamp, 100, 10f) == AssetReloadAction.Reload,
            "asset: the first sighting of an icon loads it");
        state.MarkAttempt(10f);
        state.MarkLoaded(path, stamp, 100, 10f);
        check(state.Evaluate(path, stamp, 100, 10.4f) == AssetReloadAction.Wait,
            "asset: an unchanged icon is not re-read on every frame");
        check(state.Evaluate(path, stamp.AddSeconds(1), 100, 11f) == AssetReloadAction.Reload,
            "asset: a same-length content change at a new timestamp reloads");
        state.MarkLoaded(path, stamp.AddSeconds(1), 100, 11f);

        // A corrupt or locked replacement: the caller keeps its previous texture because the applied
        // stamp is untouched, and the retry is throttled.
        state.MarkFailed(20f);
        check(state.HasApplied, "asset: a failed reload keeps the last good texture as the applied one");
        check(state.Evaluate(path, stamp.AddSeconds(2), 120, 20.1f) == AssetReloadAction.Wait,
            "asset: a failed reload is not retried before its backoff");
        check(state.Evaluate(path, stamp.AddSeconds(2), 120, 20.26f) == AssetReloadAction.Wait,
            "asset: the first retry waits the full backoff step");
        check(state.Evaluate(path, stamp.AddSeconds(2), 120, 20.51f) == AssetReloadAction.Reload,
            "asset: a failed reload retries after the backoff");
        state.MarkLoaded(path, stamp.AddSeconds(2), 120, 20.51f);

        check(state.Evaluate(@"C:\icons\other.png", stamp, 50, 30f) == AssetReloadAction.Reload
            && state.Failures == 0, "asset: pointing the config at a different file reloads immediately");
        check(state.Evaluate(string.Empty, stamp, 50, 30f) == AssetReloadAction.Wait,
            "asset: an empty icon path never loads anything");
        check(AssetReloadState.RetryDelay(0) > 0f && AssetReloadState.RetryDelay(40) == AssetReloadState.FailureRetryCeiling,
            "asset: the reload backoff is bounded by a 5 s ceiling");
        var exhausted = new AssetReloadState();
        for (int i = 0; i < AssetReloadState.MaxFailedAttempts; i++) exhausted.MarkFailed(i);
        check(exhausted.IsRetryExhausted, "asset: repeated failures are counted and capped");

        var restarted = new AssetReloadState();
        check(restarted.Evaluate(path, stamp, 100, 0f) == AssetReloadAction.Reload,
            "asset: a plugin restart reloads the bundled icon from scratch");
        check(!restarted.HasApplied, "asset: no texture is considered applied before the first successful read");
    }

    private static void ColorCacheTests(Action<bool, string> check)
    {
        var cache = new HexColorCache();
        const float fallbackR = 0.90f, fallbackG = 0.67f, fallbackB = 0.34f, fallbackA = 1f;
        cache.Resolve("#E5484D", fallbackR, fallbackG, fallbackB, fallbackA);
        check(Near(cache.Red, 229f / 255f) && Near(cache.Green, 72f / 255f) && Near(cache.Blue, 77f / 255f),
            "colour: the new weakspot default #E5484D parses to 229/72/77");
        check(Near(cache.Alpha, 1f) && !cache.IsFallback,
            "colour: a six-digit colour stays fully opaque so the draw alpha is unchanged");
        check(cache.Source == "#E5484D", "colour: the cache records the configured source string");

        cache.Resolve("#E5484D", fallbackR, fallbackG, fallbackB, fallbackA);
        check(Near(cache.Red, 229f / 255f) && !cache.IsFallback,
            "colour: the same string is served from the cache");

        cache.Resolve("#F0483C", fallbackR, fallbackG, fallbackB, fallbackA);
        check(Near(cache.Red, 240f / 255f) && Near(cache.Green, 72f / 255f) && Near(cache.Blue, 60f / 255f),
            "colour: a same-length edit is picked up on the next frame");
        check(!Near(cache.Red, fallbackR), "colour: the configured value wins over the fallback");

        cache.Resolve("#E5484D80", fallbackR, fallbackG, fallbackB, fallbackA);
        check(Near(cache.Alpha, 128f / 255f), "colour: an eight-digit colour keeps its own alpha");

        cache.Resolve("#GGGGGG", fallbackR, fallbackG, fallbackB, fallbackA);
        check(cache.IsFallback && Near(cache.Red, fallbackR) && Near(cache.Green, fallbackG)
            && Near(cache.Blue, fallbackB) && Near(cache.Alpha, fallbackA),
            "colour: an invalid value falls back to the amber accent instead of black or transparent");

        cache.Resolve("", fallbackR, fallbackG, fallbackB, fallbackA);
        check(cache.IsFallback && Near(cache.Red, fallbackR), "colour: an empty value falls back too");

        cache.Resolve(null, 0.1f, 0.2f, 0.3f, 0.4f);
        check(cache.IsFallback && Near(cache.Red, 0.1f) && Near(cache.Green, 0.2f)
            && Near(cache.Blue, 0.3f) && Near(cache.Alpha, 0.4f),
            "colour: a missing config entry falls back without throwing");

        cache.Resolve("#E5484D", fallbackR, fallbackG, fallbackB, fallbackA);
        check(!cache.IsFallback, "colour: recovering from an invalid value restores the configured colour");
        cache.Reset();
        check(cache.Source == null && cache.IsFallback, "colour: reset clears the cached style");
    }

    private static void CorpseBurstSwitchTests(Action<bool, string> check)
    {
        CorpseBurstPlan off = FeedbackPolicy.BurstPlan(new CorpseSwitches(true, true, true, true, false, true, true), 1f, 1f);
        check(off.Bursts == 0 && !off.Sound,
            "corpse: the default ExplosionFx=false plays no burst FX and no burst sound");
        CorpseBurstPlan on = FeedbackPolicy.BurstPlan(new CorpseSwitches(true, true, true, true, true, true, true), 1f, 1f);
        check(on.Bursts == FeedbackPolicy.BurstCount(1f, 1f) && on.Sound && on.Bursts >= 3,
            "corpse: enabling ExplosionFx restores the full 3-7 point burst and its sound");
        CorpseBurstPlan corpseDisabled = FeedbackPolicy.BurstPlan(new CorpseSwitches(false, true, true, true, true, true, true), 1f, 1f);
        check(corpseDisabled.Bursts == 0 && !corpseDisabled.Sound,
            "corpse: Corpse/Enabled=false still suppresses the burst even when ExplosionFx=true");
        check(!FeedbackPolicy.ShouldPlayCorpseBurstFx(new CorpseSwitches(true, true, true, true, false, true, true))
            && !FeedbackPolicy.ShouldPlayCorpseBurstFx(new CorpseSwitches(false, true, true, true, true, true, true))
            && FeedbackPolicy.ShouldPlayCorpseBurstFx(new CorpseSwitches(true, true, true, true, true, true, true)),
            "corpse: the burst switch requires both the corpse master switch and the new key");

        // The switch must not touch launch / dismemberment / timing: those paths take no such flag.
        check(FeedbackPolicy.ShouldBreakCorpseLimbs(false, true) && Math.Abs(FeedbackPolicy.Travel(0f) - 12f) < 0.001f
            && Math.Abs(FeedbackPolicy.Travel(1f) - 18f) < 0.001f && FeedbackPolicy.Fragments(0f) == 2,
            "corpse: launch, distance and dismemberment budgets are independent of the burst switch");
        check(FeedbackPolicy.ShouldWaitForRagdoll(0.04f, 0.1f) && FeedbackPolicy.CorpsePostBurstHold == 0.75f
            && FeedbackPolicy.CorpseCleanupDelay > FeedbackPolicy.CorpseBurstDelay,
            "corpse: the pre-burst ragdoll wait and the 0.75 s post-burst window are unchanged");
    }

    private static void StampOf(string path, out DateTime stamp, out long length)
    {
        var info = new FileInfo(path);
        stamp = info.LastWriteTimeUtc;
        length = info.Length;
    }

    private static bool Near(float a, float b, float tolerance = 0.0005f) => Math.Abs(a - b) <= tolerance;
}
