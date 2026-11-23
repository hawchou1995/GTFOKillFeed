using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using HawChow.GTFOKillFeed;

internal static class KillCounterTests
{
    internal static void Run(Action<bool, string> check)
    {
        var totals = new KillCounterTotals();
        foreach (KillTier tier in new[] { KillTier.Minor, KillTier.Elite, KillTier.Boss })
        { check(totals.Get(tier) == 0, "counter begins at zero: " + tier); totals.Add(tier); }
        check(totals.Get(KillTier.Minor) == 1 && totals.Get(KillTier.Elite) == 1 && totals.Get(KillTier.Boss) == 1,
            "counter keeps three presentation tiers independent");
        totals.Add((KillTier)(-1)); totals.Add((KillTier)3);
        check(totals.Get((KillTier)3) == 0 && totals.Get(KillTier.Boss) == 1, "counter refuses invalid tiers");
        Parallel.For(0, 10000, _ => totals.Add(KillTier.Minor));
        check(totals.Get(KillTier.Minor) == 10001, "counter does not lose concurrent local credits");
        totals.Reset();
        check(totals.Get(KillTier.Minor) + totals.Get(KillTier.Elite) + totals.Get(KillTier.Boss) == 0, "counter resets all tiers together");
        var counts = (int[])typeof(KillCounterTotals).GetField("_counts", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(totals);
        counts[2] = int.MaxValue; totals.Add(KillTier.Boss);
        check(totals.Get(KillTier.Boss) == int.MaxValue, "counter saturates instead of wrapping negative");
        totals.Reset();
        check(KillCounterTotals.Format(int.MaxValue) == "2147483647" && KillCounterTotals.Format(0) == "0",
            "counter never abbreviates a full ten-digit count or zero");
        var ledger = new KillLedger();
        for (int i = 0; i < 4; i++) if (ledger.TryCredit(42)) totals.Add(KillTier.Elite);
        check(totals.Get(KillTier.Elite) == 1, "existing kill ledger admits one death exactly once");
        foreach (float screen in new[] { 1280f, 1920f, 2560f, 3440f, 3840f })
        foreach (float width in new[] { 138f, 250f, 400f })
        {
            check(KillCounterPolicy.TryLayout(screen * 0.25f, 500f, 1f, width, screen, 1080f, out var row)
                && Math.Abs(row.X + row.Width - screen * 0.25f) < 0.01f && row.X >= 7.99f && row.Y > 500f,
                "counter row fits screen and keeps exact health-bar right edge " + screen + "/" + width);
        }
        check(!KillCounterPolicy.TryLayout(float.NaN, 500, 1, 180, 1920, 1080, out _)
            && !KillCounterPolicy.TryLayout(500, 1075, 1, 180, 1920, 1080, out _)
            && !KillCounterPolicy.TryLayout(0, 0, 1, 180, 1920, 1080, out _), "counter does not invent an anchor for invalid/offscreen geometry");
        foreach (int fps in new[] { 30, 60, 144 })
        {
            var retry = new CorpseFadeRetry();
            for (int frame = 0; frame < fps; frame++)
            {
                float age = frame / (float)fps;
                if (retry.CanPrepare(age, 0.85f)) retry.Observe(age, CorpseFadeAttempt.Retry);
            }
            check(retry.Attempts == 2 && !retry.Resolved && !retry.CanPrepare(0.9f, 0.85f), "fade reserves one final attempt and never retries after EndAt at " + fps + "fps");
            retry.Observe(0.85f, CorpseFadeAttempt.Started);
            check(retry.Resolved && retry.Attempts == CorpseFadeRetry.MaxAttempts && !retry.CanPrepare(0.84f, 0.85f), "fade never replays an already-started renderer");
        }
        check(CorpseFadeRetry.IsTransient(CorpseFadeSkip.ConcurrencyCap) && CorpseFadeRetry.IsTransient(CorpseFadeSkip.NoFreeInstance)
            && !CorpseFadeRetry.IsTransient(CorpseFadeSkip.PatchedByThirdParty) && !CorpseFadeRetry.IsTransient(CorpseFadeOutcome.PoolArmFailed),
            "fade retries only capacity/pool shortages, never native failures or third-party ownership");
        var stopped = new CorpseFadeRetry(); stopped.Observe(0.4f, CorpseFadeAttempt.Finished);
        check(!stopped.CanPrepare(0.7f, 0.85f), "permanent fade refusal terminates preparation");
        var boundary = new CorpseFadeRetry();
        check(!boundary.CanPrepare(0.49f, 1f) && boundary.CanPrepare(0.5f, 1f), "fade preparation starts exactly at the lead-window boundary");
        boundary.Observe(0.5f, CorpseFadeAttempt.Retry);
        check(!boundary.CanPrepare(0.74f, 1f) && boundary.CanPrepare(0.75f, 1f), "fade capacity retries respect the minimum interval");
        boundary.Observe(0.75f, CorpseFadeAttempt.Retry);
        check(!boundary.CanPrepare(0.99f, 1f) && boundary.Attempts == CorpseFadeRetry.MaxAttempts - 1,
            "fade preparation cannot consume the reserved EndAt attempt");
        var committed = new CorpseFadeRetry(); committed.Observe(0.5f, CorpseFadeAttempt.Started);
        check(committed.Started && committed.Resolved && (FeedbackPolicy.ShouldHideCorpse(0, true) || committed.Started),
            "a late inactive state cannot undo a successfully started dissolve");
        committed.Observe(0.75f, CorpseFadeAttempt.Retry);
        check(committed.Started && committed.Resolved, "fade commitment remains monotonic after observations");

        DirectoryInfo root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "GTFOKillFeed.csproj"))) root = root.Parent;
        if (root == null) throw new InvalidOperationException("counter source root missing");
        string tracker = File.ReadAllText(Path.Combine(root.FullName, "KillTracker.cs"));
        int build = tracker.IndexOf("KillPresentation presentation = KillPresentationPolicy.Build", StringComparison.Ordinal);
        int add = tracker.IndexOf("Counters.Add(presentation.Tier)", StringComparison.Ordinal);
        int sound = tracker.IndexOf("HitSound.OnLocalKill", StringComparison.Ordinal);
        check(build >= 0 && add > build && sound > add && tracker.Split("Counters.Add(").Length == 2,
            "counter is wired once after presentation tier and before optional effects");
        int preview = tracker.IndexOf("internal static void AddPreview", StringComparison.Ordinal);
        check(preview > add && !tracker.Substring(preview).Contains("Counters.Add("), "F9 preview cannot change real kill totals");
        check(tracker.Contains("Counters.Reset(); KillCounterHud.Reset();"), "level-reset clears counter and native anchor");
        check(tracker.Contains("internal static void Despawned(EnemyAgent agent)")
            && tracker.Contains("KillLedger.IdentityForRelease(damage, remembered)")
            && tracker.Contains("KillLedger.ShouldReleaseIdentity(identity,")
            && tracker.Contains("Ledger.Forget(identity);")
            && tracker.Contains("_identityByAgent[agent.Pointer.ToInt64()] = identity;")
            && tracker.Contains("KillTracker.Despawned(__instance);"),
            "a pooled identity is released on despawn even when Damage is already gone, so a reused enemy can be credited again (1.2.35 counter freeze)");
        string hud = File.ReadAllText(Path.Combine(root.FullName, "KillCounterHud.cs"));
        check(hud.Contains("m_healthWidth") && hud.Contains("m_healthStartPos") && hud.Contains("m_cellUICanvas")
            && hud.Contains("KillCounterAnchorPolicy.ManualRefusal(") && !hud.Contains("Camera.main") && !hud.Contains("FindObjectsOfType"),
            "counter verifies its own manual CellUI compositor without a scene scan or main-camera guess");
        check(hud.Contains("GUI.matrix = previousMatrix * Matrix4x4.TRS") && hud.Contains("GUI.matrix = previousMatrix;")
            && hud.Contains("CalcSize(Contents[i])"), "counter scales measured glyphs and icons together and restores GUI state");
        string corpse = File.ReadAllText(Path.Combine(root.FullName, "CorpseFeedback.cs"));
        check(corpse.Contains("if (!prepared) item.Fade.Observe(age, CorpseFadeAttempt.Finished);")
            && System.Text.RegularExpressions.Regex.IsMatch(corpse, @"if \(!item\.Fade\.Resolved\)\s*\{\s*if \(CorpseFade\.SkipBeforeSelection"), "completed fades never select a renderer again at cleanup; preparation exceptions terminate retries");
        check(corpse.Contains("bool releaseVisual = FeedbackPolicy.ShouldHideCorpse(item.LaunchApplications, item.RagdollInactive) || item.Fade.Started;"),
            "EndAt honors the successful-dissolve commitment after launch hot reload");
        ExportPreview();
    }
    private static void ExportPreview()
    {
        string fixture = Environment.GetEnvironmentVariable("KILLFEED_COUNTER_FIXTURE");
        if (string.IsNullOrEmpty(fixture)) return;
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(fixture));
        var rows = new List<object>();
        foreach (JsonElement item in document.RootElement.EnumerateArray())
        {
            float[] widths = item.GetProperty("textWidths").EnumerateArray().Select(v => KillCounterPolicy.GroupWidth(v.GetSingle() + 3)).ToArray();
            float right = item.GetProperty("right").GetSingle(), bottom = item.GetProperty("bottom").GetSingle();
            float w = item.GetProperty("w").GetSingle(), h = item.GetProperty("h").GetSingle();
            if (!KillCounterPolicy.TryLayout(right, bottom, h / 1080f, widths.Sum() + 2 * KillCounterPolicy.GroupGap, w, h, out var r))
                throw new InvalidOperationException("preview does not fit: " + item.GetProperty("name").GetString());
            rows.Add(new { input = item.Clone(), x = r.X, y = r.Y, width = r.Width, height = r.Height, scale = r.Scale,
                groupWidths = widths, icon = KillCounterPolicy.Icon, font = KillCounterPolicy.Font,
                iconGap = KillCounterPolicy.IconGap, groupGap = KillCounterPolicy.GroupGap, belowGap = KillCounterPolicy.BelowGap });
        }
        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("PI_SCRATCH_DIR"), "kill-counter-preview-layout.json"), JsonSerializer.Serialize(rows));
    }
}
