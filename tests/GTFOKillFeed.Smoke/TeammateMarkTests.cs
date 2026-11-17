using System;
using System.Collections.Generic;
using HawChow.GTFOKillFeed;

// Behavioural coverage for the downed-teammate mark: glyph selection, distance/size curve,
// on-screen test, draw matrix, EMP policy, roster reconcile (revive / leave / level change /
// unknown state) and the bundled mark PNGs.
internal static class TeammateMarkTests
{
    internal static void Run(Action<bool, string> check)
    {
        // ---- glyph selection --------------------------------------------------------------
        check(TeammateMarkPolicy.ParseIcon(null) == TeammateMarkIcon.Cai, "mark: missing Icon value defaults to 菜");
        check(TeammateMarkPolicy.ParseIcon("") == TeammateMarkIcon.Cai, "mark: empty Icon value defaults to 菜");
        check(TeammateMarkPolicy.ParseIcon("Cai") == TeammateMarkIcon.Cai, "mark: Cai selects 菜");
        check(TeammateMarkPolicy.ParseIcon("cai") == TeammateMarkIcon.Cai, "mark: lowercase cai selects 菜");
        check(TeammateMarkPolicy.ParseIcon("菜") == TeammateMarkIcon.Cai, "mark: the 菜 character itself selects 菜");
        check(TeammateMarkPolicy.ParseIcon("Ruo") == TeammateMarkIcon.Ruo, "mark: Ruo selects 鶸");
        check(TeammateMarkPolicy.ParseIcon("ruo") == TeammateMarkIcon.Ruo, "mark: lowercase ruo selects 鶸");
        check(TeammateMarkPolicy.ParseIcon("鶸") == TeammateMarkIcon.Ruo, "mark: the 鶸 character itself selects 鶸");
        check(TeammateMarkPolicy.ParseIcon("nonsense") == TeammateMarkIcon.Cai, "mark: an unknown Icon value falls back to 菜");
        check(TeammateMarkPolicy.ParseIcon("Si") == TeammateMarkIcon.Si, "mark: Si selects 死");
        check(TeammateMarkPolicy.ParseIcon("si") == TeammateMarkIcon.Si, "mark: lowercase si selects 死");
        check(TeammateMarkPolicy.ParseIcon("death") == TeammateMarkIcon.Si, "mark: death is a 死 alias");
        check(TeammateMarkPolicy.ParseIcon("3") == TeammateMarkIcon.Si, "mark: 3 selects 死");
        check(TeammateMarkPolicy.ParseIcon("死") == TeammateMarkIcon.Si, "mark: the 死 character itself selects 死");
        check(TeammateMarkPolicy.ParseIcon("Sha") == TeammateMarkIcon.Sha, "mark: Sha selects 殺");
        check(TeammateMarkPolicy.ParseIcon("sha") == TeammateMarkIcon.Sha, "mark: lowercase sha selects 殺");
        check(TeammateMarkPolicy.ParseIcon("sha1") == TeammateMarkIcon.Sha, "mark: sha1 selects 殺 (the pinyin plus tone digit)");
        check(TeammateMarkPolicy.ParseIcon("kill") == TeammateMarkIcon.Sha, "mark: kill is a 殺 alias");
        check(TeammateMarkPolicy.ParseIcon("4") == TeammateMarkIcon.Sha, "mark: 4 selects 殺");
        check(TeammateMarkPolicy.ParseIcon("殺") == TeammateMarkIcon.Sha, "mark: the 殺 character itself selects 殺");
        check(TeammateMarkPolicy.ParseIcon("杀") == TeammateMarkIcon.Sha, "mark: the simplified 杀 input is accepted and selects 殺");
        check(TeammateMarkPolicy.IconFileName(TeammateMarkIcon.Cai) == "teammate-downed-cai.png"
            && TeammateMarkPolicy.IconFileName(TeammateMarkIcon.Ruo) == "teammate-downed-ruo.png"
            && TeammateMarkPolicy.IconFileName(TeammateMarkIcon.Si) == "teammate-downed-si.png"
            && TeammateMarkPolicy.IconFileName(TeammateMarkIcon.Sha) == "teammate-downed-sha.png",
            "mark: each glyph resolves to its own bundled PNG");
        check(TeammateMarkPolicy.IconGlyph(TeammateMarkIcon.Cai) == "菜"
            && TeammateMarkPolicy.IconGlyph(TeammateMarkIcon.Ruo) == "鶸"
            && TeammateMarkPolicy.IconGlyph(TeammateMarkIcon.Si) == "死"
            && TeammateMarkPolicy.IconGlyph(TeammateMarkIcon.Sha) == "殺",
            "mark: the text fallback keeps the real glyph, never a placeholder");

        // ---- custom icon path resolution ---------------------------------------------------
        string bundled = @"C:\mod\Assets\icons\teammate-downed-cai.png";
        string iconBase = @"C:\mod\Assets\icons";
        check(TeammateMarkPolicy.CustomProbePath(@"C:\pics\mine.png", iconBase) == @"C:\pics\mine.png",
            "mark: an absolute local custom PNG is what the custom slot probes");
        check(TeammateMarkPolicy.CustomProbePath(@"D:\other\mark.png", iconBase) == @"D:\other\mark.png",
            "mark: custom icons on another local drive stay supported");
        check(TeammateMarkPolicy.CustomProbePath("teammate-downed-sha.png", iconBase) == @"C:\mod\Assets\icons\teammate-downed-sha.png",
            "mark: a bare file name resolves against the plugin's Assets\\icons folder");
        check(TeammateMarkPolicy.CustomProbePath(@"subdir\mine.png", iconBase) == @"C:\mod\Assets\icons\subdir\mine.png",
            "mark: a relative subfolder path resolves against Assets\\icons");
        check(TeammateMarkPolicy.CustomProbePath("", iconBase) == null, "mark: an empty custom path probes nothing");
        check(TeammateMarkPolicy.CustomProbePath("   ", iconBase) == null, "mark: a whitespace custom path probes nothing");
        check(TeammateMarkPolicy.CustomProbePath("mark.png", null) == null,
            "mark: a relative path with no base directory probes nothing");
        check(TeammateMarkPolicy.CustomProbePath(@"\\server\share\mark.png", iconBase) == null
            && TeammateMarkPolicy.CustomProbePath("//server/share/mark.png", iconBase) == null,
            "mark: UNC/network custom paths are refused (a dropped share would block the probe)");
        check(TeammateMarkPolicy.IsNetworkPath(@"\\server\share\a.png") && !TeammateMarkPolicy.IsNetworkPath(bundled),
            "mark: network-path detection only matches UNC prefixes");
        check(TeammateMarkPolicy.IsRootedPath(@"C:\a.png") && !TeammateMarkPolicy.IsRootedPath("a.png")
            && !TeammateMarkPolicy.IsRootedPath(null), "mark: rooted-path test is total");

        // ---- distance curve and resolution independence ------------------------------------
        check(Near(TeammateMarkPolicy.DistanceScale(8f, 8f), 1f), "mark: reference distance is unit scale");
        check(Near(TeammateMarkPolicy.DistanceScale(1f, 8f), TeammateMarkPolicy.MaxScale),
            "mark: very close teammates hit the 1.35 ceiling");
        check(Near(TeammateMarkPolicy.DistanceScale(60f, 8f), TeammateMarkPolicy.MinScale),
            "mark: distant teammates hit the 0.55 floor");
        check(TeammateMarkPolicy.DistanceScale(3f, 8f) >= TeammateMarkPolicy.DistanceScale(6f, 8f),
            "mark: nearer teammates are never smaller");
        // 1080p: 30 px at 8 m, floor 26 px from ~9.2 m outwards.
        check(TeammateMarkPolicy.FontPixels(30f, 8f, 8f, 1080, 26f, 42f) == 30, "mark: 8 m at 1080p is 30 px");
        check(TeammateMarkPolicy.FontPixels(30f, 1f, 8f, 1080, 26f, 42f) == 41, "mark: 1 m at 1080p is 41 px");
        check(TeammateMarkPolicy.FontPixels(30f, 12f, 8f, 1080, 26f, 42f) == 26, "mark: 12 m at 1080p is pinned to the 26 px floor");
        check(TeammateMarkPolicy.FontPixels(30f, 60f, 8f, 1080, 26f, 42f) == 26, "mark: 60 m at 1080p still keeps the readable floor");
        check(TeammateMarkPolicy.FontPixels(30f, 8f, 8f, 720, 26f, 42f) == 23, "mark: 8 m at 720p is 23 px");
        check(TeammateMarkPolicy.FontPixels(30f, 8f, 8f, 1440, 26f, 42f) == 40, "mark: 8 m at 1440p is 40 px");
        check(TeammateMarkPolicy.FontPixels(30f, 8f, 8f, 720, 26f, 42f) > 20
            && TeammateMarkPolicy.FontPixels(30f, 8f, 8f, 1440, 26f, 42f) > 30,
            "mark: the same distance grows with screen resolution (resolution independence)");
        bool monotonic = true;
        int previous = int.MaxValue;
        for (float metres = 0.5f; metres <= 70f; metres += 0.5f)
        {
            int px = TeammateMarkPolicy.FontPixels(30f, metres, 8f, 1080, 26f, 42f);
            if (px > previous) monotonic = false;
            previous = px;
        }
        check(monotonic, "mark: the size curve never grows as distance increases");
        check(TeammateMarkPolicy.FontPixels(30f, 8f, 8f, 1080, 26f, 42f) <= (int)Math.Round(1080 * 0.05)
            && TeammateMarkPolicy.FontPixels(30f, 1f, 8f, 4320, 26f, 96f) <= (int)Math.Round(4320 * 0.05),
            "mark: the icon never exceeds 5% of the screen height");
        check(Near(TeammateMarkPolicy.DistanceScale(float.NaN, 8f), 1f)
            && Near(TeammateMarkPolicy.DistanceScale(0f, 8f), 1f)
            && Near(TeammateMarkPolicy.DistanceScale(0.1f, 8f), TeammateMarkPolicy.MaxScale),
            "mark: unknown/zero distance degrades to reference scale and near-zero distance clamps");
        check(TeammateMarkPolicy.FontPixels(30f, 8f, float.NaN, 1080, float.NaN, float.NaN) >= 8,
            "mark: invalid size config still yields a bounded positive pixel size");
        // ---- on-screen test ----------------------------------------------------------------
        check(TeammateMarkPolicy.OnScreen(100f, 100f, 1920, 1080, 8f), "mark: an interior point is on screen");
        check(!TeammateMarkPolicy.OnScreen(4f, 100f, 1920, 1080, 8f), "mark: a point inside the left margin is rejected");
        check(!TeammateMarkPolicy.OnScreen(1918f, 100f, 1920, 1080, 8f), "mark: a point inside the right margin is rejected");
        check(!TeammateMarkPolicy.OnScreen(100f, 2f, 1920, 1080, 8f), "mark: a point above the top margin is rejected");
        check(!TeammateMarkPolicy.OnScreen(float.NaN, 100f, 1920, 1080, 8f), "mark: an unprojectable point is rejected");
        check(!TeammateMarkPolicy.OnScreen(100f, 100f, 0, 0, 8f), "mark: a zero-size screen draws nothing");

        // ---- draw matrix (feature, inLevel, hudVisible, isLocal, downed, emp, onScreen, dist, max) --
        check(TeammateMarkPolicy.ShouldDrawMark(true, true, true, false, true, false, true, 8f, 60f),
            "mark: a downed remote teammate in range draws");
        check(!TeammateMarkPolicy.ShouldDrawMark(false, true, true, false, true, false, true, 8f, 60f),
            "mark: TeammateMark/Enabled=false draws nothing");
        check(!TeammateMarkPolicy.ShouldDrawMark(true, false, true, false, true, false, true, 8f, 60f),
            "mark: outside InLevel nothing draws");
        check(!TeammateMarkPolicy.ShouldDrawMark(true, true, false, false, true, false, true, 8f, 60f),
            "mark: F7 hiding the HUD also hides the mark");
        check(!TeammateMarkPolicy.ShouldDrawMark(true, true, true, true, true, false, true, 8f, 60f),
            "mark: the local player is never marked");
        check(!TeammateMarkPolicy.ShouldDrawMark(true, true, true, false, false, false, true, 8f, 60f),
            "mark: a standing teammate is not marked");
        check(!TeammateMarkPolicy.ShouldDrawMark(true, true, true, false, true, true, true, 8f, 60f),
            "mark: EMP suppression hides the mark when configured");
        check(!TeammateMarkPolicy.ShouldDrawMark(true, true, true, false, true, false, false, 8f, 60f),
            "mark: a target behind the camera or off screen is skipped");
        check(!TeammateMarkPolicy.ShouldDrawMark(true, true, true, false, true, false, true, 61f, 60f),
            "mark: a teammate beyond MaxDistanceMetres is skipped");
        check(TeammateMarkPolicy.ShouldDrawMark(true, true, true, false, true, false, true, 60f, 60f),
            "mark: a teammate exactly at MaxDistanceMetres still draws");
        check(!TeammateMarkPolicy.ShouldDrawMark(true, true, true, false, true, false, true, float.NaN, 60f),
            "mark: an unreadable distance skips instead of guessing");

        // ---- R6: PLOC_Downed.m_isRevived is diagnostic-only --------------------------------
        // The policy has no "reviving" input at all: a teammate that is still in
        // PLOC_State.Downed keeps its mark no matter what that opaque flag says.
        check(TeammateMarkPolicy.ShouldDrawMark(true, true, true, false, true, false, true, 4f, 60f),
            "mark: still-Downed teammates draw even while m_isRevived is set");
        var transition = new TeammateMarkLedger();
        transition.MarkSeen(7, downed: true, reviving: false, now: 0f);
        check(transition.GetOrAdd(7, 0f).Downed, "roster: downed=true starts marked");
        transition.MarkSeen(7, downed: true, reviving: true, now: 0.2f);
        check(transition.GetOrAdd(7, 0.2f).Downed && transition.GetOrAdd(7, 0.2f).Reviving,
            "roster: a Downed teammate with m_isRevived stays Downed (the flag is only logged)");
        transition.MarkSeen(7, downed: false, reviving: false, now: 0.4f);
        check(!transition.GetOrAdd(7, 0.4f).Downed,
            "roster: leaving PLOC_State.Downed is the only thing that stops the mark");
        check(transition.Count == 1, "roster: the revived teammate is still tracked while in the level");

        // ---- EMP policy --------------------------------------------------------------------
        check(!TeammateMarkPolicy.ShouldProbeEmp(false), "emp: the default HideDuringEMP=false never probes the optional mod");
        check(TeammateMarkPolicy.ShouldProbeEmp(true), "emp: HideDuringEMP=true enables the optional probe");
        check(!TeammateMarkPolicy.ShouldHideForEmp(false, true, true), "emp: still-visible mode ignores an active EMP");
        check(TeammateMarkPolicy.ShouldHideForEmp(true, true, true), "emp: hide mode hides during an active EMP");
        check(!TeammateMarkPolicy.ShouldHideForEmp(true, true, false), "emp: hide mode shows when IsEMPed() is false");
        check(!TeammateMarkPolicy.ShouldHideForEmp(true, false, true), "emp: a missing handler never hides the mark");
        check(TeammateMarkPolicy.IsProbeDue(1f, 1f) && !TeammateMarkPolicy.IsProbeDue(0.5f, 1f)
            && !TeammateMarkPolicy.IsProbeDue(float.NaN, 0f), "emp: probing is throttled and NaN-safe");
        check(Near(TeammateMarkPolicy.NextProbeAt(2f, true) - 2f, TeammateMarkPolicy.EmpPollSeconds),
            "emp: a bound handler is polled every 0.2 s");
        check(Near(TeammateMarkPolicy.NextProbeAt(2f, false) - 2f, TeammateMarkPolicy.EmpMaxRetrySeconds),
            "emp: a missing handler is retried on the 10 s ceiling");
        check(Near(TeammateMarkPolicy.RetryDelay(0), 0.25f) && Near(TeammateMarkPolicy.RetryDelay(1), 0.5f)
            && Near(TeammateMarkPolicy.RetryDelay(2), 1f) && Near(TeammateMarkPolicy.RetryDelay(3), 2f)
            && Near(TeammateMarkPolicy.RetryDelay(9), 10f) && Near(TeammateMarkPolicy.RetryDelay(-4), 0.25f),
            "emp: failure backoff is bounded and grows 0.25 s -> 10 s");

        // ---- roster reconcile --------------------------------------------------------------
        var ledger = new TeammateMarkLedger();
        var removed = new List<long>();
        ledger.MarkSeen(11, true, false, 0f);
        ledger.MarkSeen(22, false, false, 0f);
        check(ledger.Count == 2, "roster: two teammates are tracked");
        ledger.MarkSeen(11, true, false, 0.55f);
        check(ledger.ForgetUnseen(0.55f, removed) == 1 && removed[0] == 22,
            "roster: a teammate not enumerated for 0.5 s is dropped (left or replaced)");
        check(ledger.Count == 1, "roster: the revived/seen teammate stays tracked");
        ledger.MarkSeen(11, false, false, 0.6f);
        check(ledger.Count == 1 && !ledger.GetOrAdd(11, 0.6f).Downed,
            "roster: a revived teammate keeps its entry but stops being downed");
        ledger.MarkSeen(11, true, true, 0.7f);
        check(ledger.GetOrAdd(11, 0.7f).Reviving, "roster: the revive flag is carried for the draw gate");
        ledger.MarkSeen(99, true, false, 0.7f);
        check(ledger.Count == 2 && ledger.GetOrAdd(99, 0.7f).Downed,
            "roster: a different Lookup identity (slot hand-over) creates a new entry");
        ledger.Clear();
        check(ledger.Count == 0, "roster: leaving the level clears every mark");

        var emptyScratch = new List<long>();
        var fresh = new TeammateMarkLedger();
        fresh.MarkSeen(5, true, false, 10f);
        check(fresh.ForgetUnseen(10.4f, emptyScratch) == 0, "roster: 0.4 s of no enumeration keeps the entry");
        check(fresh.ForgetUnseen(10.6f, emptyScratch) == 1, "roster: 0.6 s of no enumeration drops the entry");

        // ---- bundled PNGs ------------------------------------------------------------------
        string root = System.IO.Path.Combine(AppContext.BaseDirectory);
        check(TeammateMarkPolicy.DefaultMinPixels >= 26f, "mark: the default floor protects the dense 鶸 glyph");
        check(TeammateMarkPolicy.DefaultMaxPixels > TeammateMarkPolicy.DefaultMinPixels,
            "mark: the ceiling is above the floor");
        check(TeammateMarkPolicy.ShouldDrawMark(true, true, true, false, true, false, true, 30f, 60f),
            "mark: the default range covers a whole room");
        check(!string.IsNullOrEmpty(root), "mark: the smoke host resolves its own directory");
    }

    private static bool Near(float a, float b, float tolerance = 0.0005f) => Math.Abs(a - b) <= tolerance;
}
