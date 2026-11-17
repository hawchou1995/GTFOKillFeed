using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using HawChow.GTFOKillFeed;

// Regression coverage for the independent review round (F1, F4, F5, F6, A1, A3, A7) plus the explicitly
// requested 42/43/45 flyer fixture.
//
// Everything here is offline: the flyer fixture is driven by the SHIPPED name table only (which enemy is
// which), and it never claims that the real game values (HealthMax, IsFlyer, armour limbs) of those enemies
// have been verified. It proves what the plugin does for a given set of inputs, nothing more.
internal static class ReviewFixTests
{
    internal static void Run(Action<bool, string> check)
    {
        // ---- F1: which kills the card has to cover ------------------------------------------
        check(KillIconPolicy.ShouldDrawCard(KillIconDisplay.Card, 0, 0f, 10f, false)
            && KillIconPolicy.ShouldDrawCard(KillIconDisplay.Card, 7, 12f, 10f, true)
            && KillIconPolicy.ShouldDrawCard(KillIconDisplay.Card, -1, 0f, 10f, false),
            "review-F1: in Card mode every card is painted, whatever its icon token says");
        check(KillIconPolicy.ShouldDrawCard(KillIconDisplay.Head, 0, 0f, 10f, false),
            "review-F1: a kill that never received a head icon falls back to its card (no sound-only kill)");
        check(!KillIconPolicy.ShouldDrawCard(KillIconDisplay.Head, 7, 12f, 10f, true),
            "review-F1: while its own head icon is on screen the kill draws NO card (never both at once)");
        check(KillIconPolicy.ShouldDrawCard(KillIconDisplay.Head, 7, 12f, 10f, false),
            "review-F1: a kill whose icon slot is gone or whose tier texture is missing falls back to the card");
        check(!KillIconPolicy.ShouldDrawCard(KillIconDisplay.Head, 7, 9f, 10f, true)
            && !KillIconPolicy.ShouldDrawCard(KillIconDisplay.Head, 7, 9f, 10f, false),
            "review-F1: once the icon's own window has passed no late card appears either");
        check(!KillIconPolicy.ShouldDrawCard(KillIconDisplay.Head, -1, 0f, 10f, false),
            "review-F1: an F9 preview card is not a kill and stays hidden in Head mode (the preview icons show it)");
        check(KillIconPolicy.ShouldDrawCard(KillIconDisplay.Head, 7, float.NaN, 10f, false)
            && KillIconPolicy.ShouldDrawCard(KillIconDisplay.Head, 7, 12f, float.NaN, false),
            "review-F1: a broken timestamp keeps the fallback visible instead of hiding the kill");

        // ---- F5: the preview set is separate, fixed and never evicts a real kill -------------
        var queue = new KillIconQueue(KillIconPolicy.MaxIconsCeiling);
        check(KillIconQueue.PreviewSlots == KillIconPolicy.PreviewCombos && KillIconPolicy.PreviewCombos == 9,
            "review-F5: the preview set is exactly the nine tier x flight combinations");
        for (int i = 0; i < KillIconPolicy.MaxIconsCeiling; i++)
            queue.PushReal(Slot(queue.NextToken(), i), KillIconPolicy.MaxIconsCeiling);
        check(queue.RealCount == KillIconPolicy.MaxIconsCeiling,
            "review-F5: real kills fill up to the hard ceiling");
        for (int i = 0; i < KillIconPolicy.PreviewCombos; i++)
            queue.PushPreview(Slot(queue.NextToken(), 100 + i, true));
        check(queue.RealCount == KillIconPolicy.MaxIconsCeiling && queue.PreviewCount == KillIconPolicy.PreviewCombos,
            "review-F5: nine previews never evict a real kill (separate capacity)");
        check(!queue.PushPreview(Slot(queue.NextToken(), 0f, true)),
            "review-F5: the preview store is bounded - extra combinations are refused, not appended");
        queue.ResetPreviews();
        for (int i = 0; i < KillIconPolicy.PreviewCombos; i++)
            queue.PushPreview(Slot(queue.NextToken(), 200 + i, true));
        check(queue.PreviewCount == KillIconPolicy.PreviewCombos && queue.RealCount == KillIconPolicy.MaxIconsCeiling,
            "review-F5: a repeated F9 press replaces the preview set instead of stacking (and keeps every real kill)");
        int oldestRealToken = queue.RealAt(0).Token;              // pushed with At = 0
        int newestRealToken = queue.RealAt(queue.RealCount - 1).Token;
        check(queue.TryFind(newestRealToken, out int foundIndex, out bool foundPreview)
            && !foundPreview && foundIndex == queue.RealCount - 1,
            "review-F5: a real kill is still addressable by its token after previews were replaced");
        queue.PushReal(Slot(queue.NextToken(), 999f), KillIconPolicy.MaxIconsCeiling);
        check(queue.RealCount == KillIconPolicy.MaxIconsCeiling && queue.PreviewCount == KillIconPolicy.PreviewCombos
            && !queue.TryFind(oldestRealToken, out _, out _) && queue.TryFind(newestRealToken, out _, out _),
            "review-F5: a full real queue evicts only the OLDEST REAL kill; the preview set is untouched");
        var tokenQueue = new KillIconQueue(4);
        int firstToken = tokenQueue.NextToken();
        KillIconSlot firstSlot = Slot(firstToken, 1f);
        tokenQueue.PushReal(firstSlot, 4);
        tokenQueue.RemoveRealAt(0);
        int secondToken = tokenQueue.NextToken();
        check(secondToken > firstToken && !tokenQueue.TryFind(firstToken, out _, out _),
            "review-F5: icon tokens are generation numbers (never reused, so a removed kill cannot be re-identified)");
        tokenQueue.Clear();
        check(tokenQueue.Count == 0 && !tokenQueue.TryFind(secondToken, out _, out _),
            "review-F5: a level reset clears real and preview slots");

        // ---- F4: bounded per-target head-bounds cache ---------------------------------------
        var bounds = new HeadBoundsCache();
        int measured = 0;
        for (int frame = 0; frame < 100; frame++)
        {
            float now = frame * 0.001f;   // 100 calls inside ONE 0.35 s window
            bounds.Top(11, now, () => { measured++; return 2.0f; });
            bounds.Top(22, now, () => { measured++; return 3.0f; });
        }
        check(bounds.Samples == 2 && measured == 2,
            "review-F4: two alternating head targets are measured once each per window, not once per call (measured=" + measured + ")");
        bounds.Top(11, 0.30f, () => { measured++; return 2.0f; });
        check(bounds.Samples == 2, "review-F4: an in-window hit is served from the cache");
        bounds.Top(11, 0.36f, () => { measured++; return 2.5f; });
        check(bounds.Samples == 3 && measured == 3, "review-F4: an expired sample is re-measured exactly once");
        // The retired single slot cached the no-renderer case as 0; the bounded cache must keep that, so an
        // enemy without an enabled Renderer neither re-enumerates every frame nor invents a height.
        var noRenderer = new HeadBoundsCache();
        int noRendererMeasurements = 0;
        float firstMiss = noRenderer.Top(33, 1.00f, () => { noRendererMeasurements++; return 0f; });
        float secondMiss = noRenderer.Top(33, 1.01f, () => { noRendererMeasurements++; return 0f; });
        check(firstMiss == 0f && secondMiss == 0f && noRendererMeasurements == 1 && noRenderer.Samples == 1,
            "review-F4: a no-renderer measurement (0) is cached like any other, so the miss is not re-enumerated per frame");
        var crowd = new HeadBoundsCache();
        for (int id = 1; id <= 40; id++) crowd.Top(id, 1f, () => 1f);
        check(crowd.Filled <= HeadBoundsCache.MaxSlots && crowd.Filled == HeadBoundsCache.MaxSlots,
            "review-F4: the cache is hard-bounded at " + HeadBoundsCache.MaxSlots + " targets (a crowd cannot grow it)");
        var uncacheable = new HeadBoundsCache();
        int uncacheableMeasurements = 0;
        for (int i = 0; i < 5; i++) uncacheable.Top(int.MinValue, 1f, () => { uncacheableMeasurements++; return 0f; });
        check(uncacheableMeasurements == 5 && uncacheable.Filled == 0,
            "review-F4: an unreadable instance id is measured but never cached (no guessing which target it was)");
        check(bounds.HasFresh(11, 0.40f) && !bounds.HasFresh(11, 0.80f),
            "review-F4: freshness follows the same window the measurement uses");
        bounds.Clear();
        check(bounds.Filled == 0 && bounds.Samples == 0,
            "review-F4: a level reset drops every measurement");

        // ---- F6: the F9 preview cards keep the real three tiers ------------------------------
        check(KillPresentationPolicy.PreviewTier(false) == KillTier.Minor
            && KillPresentationPolicy.PreviewTier(true) == KillTier.Elite,
            "review-F6: the normal preview sample is MINOR and the weakspot sample is ELITE");
        check(KillIconPolicy.TierIconKey(KillPresentationPolicy.PreviewTier(true)) == "kill-weakspot"
            && KillIconPolicy.TierIconKey(KillPresentationPolicy.PreviewTier(false)) == "kill-normal"
            && KillIconPolicy.TierIconKey(KillTier.Boss) == "kill-heavy",
            "review-F6: the weakspot preview therefore draws the medium/weakspot raster again, not the normal one");
        check(HitSoundPolicy.ResourceName(HitSoundStyle.Bf5, HitCueKind.Kill, KillPresentationPolicy.PreviewTier(true), KillFlight.None)
            != HitSoundPolicy.ResourceName(HitSoundStyle.Bf5, HitCueKind.Kill, KillPresentationPolicy.PreviewTier(false), KillFlight.None),
            "review-F6: the two preview samples name two different kill cues, matching the real tier rule");
        var combos = new HashSet<string>();
        for (int i = 0; i < KillIconPolicy.PreviewCombos; i++)
        {
            KillIconPolicy.PreviewCombo(i, out KillTier tier, out KillFlight flight);
            combos.Add(tier + "|" + flight);
        }
        check(combos.Count == KillIconPolicy.PreviewCombos,
            "review-F6: the nine preview combinations are nine distinct tier x flight pairs (no duplicate preview icon)");

        // ---- A1: thread-safe, resolve-once, atomic throttle ---------------------------------
        var state = new HitSoundState();
        int winners = 0;
        var threads = new Thread[8];
        var start = new ManualResetEventSlim(false);
        for (int i = 0; i < threads.Length; i++)
        {
            threads[i] = new Thread(() =>
            {
                start.Wait();
                if (state.TryBeginResolve()) Interlocked.Increment(ref winners);
            });
            threads[i].Start();
        }
        start.Set();
        for (int i = 0; i < threads.Length; i++) threads[i].Join();
        check(winners == 1 && state.ResolveStarted,
            "review-A1: eight concurrent first Post calls resolve the indicator ids exactly once");
        state.PublishResolved(new[] { 11u, 22u }, 1);
        check(state.MutedIds.Length == 2 && state.ResolveFailures == 1 && state.ResolveFinished,
            "review-A1: the resolved id set is published as one frozen snapshot with its failure count");
        check(!state.TryBeginResolve(),
            "review-A1: a second resolution attempt is refused even after the first one finished");

        var throttled = new HitSoundState();
        int plays = 0;
        var hits = new Thread[8];
        start.Reset();
        for (int i = 0; i < hits.Length; i++)
        {
            hits[i] = new Thread(() =>
            {
                start.Wait();
                for (int attempt = 0; attempt < 200; attempt++) throttled.TryPlay(false, 1.0, 0.05, () => { Interlocked.Increment(ref plays); return true; });
            });
            hits[i].Start();
        }
        start.Set();
        for (int i = 0; i < hits.Length; i++) hits[i].Join();
        check(plays == 1 && throttled.Played == 1,
            "review-A1: eight threads racing the same instant play exactly once (the gap check and the timestamp are one operation)");
        check(!throttled.TryPlay(false, 1.02, 0.05, () => { Interlocked.Increment(ref plays); return true; }) && plays == 1,
            "review-A1: a hit inside the throttle window is dropped");
        check(throttled.TryPlay(false, 1.05, 0.05, () => { Interlocked.Increment(ref plays); return true; }) && plays == 2,
            "review-A1: the next window plays exactly once more");
        check(throttled.TryPlay(true, 1.05, 0.05, () => { Interlocked.Increment(ref plays); return true; }),
            "review-A1: the kill cue keeps its own independent throttle");
        check(!throttled.TryPlay(false, 1.10, 0.05, () => false) && plays == 3,
            "review-A1: a cue that failed to play never advances the throttle");
        throttled.Reset();
        check(throttled.TryPlay(false, 1.10, 0.05, () => { Interlocked.Increment(ref plays); return true; }),
            "review-A1: Reset re-arms the first cue (level change)");

        // ---- A3: a missing mute switch never mutes ------------------------------------------
        check(!HitSoundPolicy.MuteRequested(null),
            "review-A3: an unbound/absent MuteVanillaIndicator means 'do not mute' (vanilla stays audible)");
        check(HitSoundPolicy.MuteRequested(true) && !HitSoundPolicy.MuteRequested(false),
            "review-A3: the switch keeps its two explicit values");
        check(HitSoundPolicy.DefaultMuteVanillaIndicator == false && HitSoundPolicy.DefaultEnabled,
            "review-A3: the shipped defaults stay independent (cues on, vanilla indicator audible)");

        // ---- A7: one true cue count ---------------------------------------------------------
        int shipped = CountShippedCueNames();
        check(HitSoundPolicy.ShippedCueNames == 25 && shipped == HitSoundPolicy.ShippedCueNames,
            "review-A7: the code, the log line and the cue matrix agree on 25 shipped cue files (2 hit + 1 weakspot + 1 armor + 21 kill), found " + shipped);

        // ---- 42/43/45 offline flyer fixture (name table only) -------------------------------
        Dictionary<uint, string> names = LoadShippedNames(check);
        if (names != null)
        {
            check(names.TryGetValue(42u, out string name42) && name42 == "大眼"
                && names.TryGetValue(43u, out string name43) && name43 == "大眼"
                && names.TryGetValue(45u, out string name45) && name45 == "装甲大眼",
                "fixture-42/43/45: the SHIPPED name table maps 42 and 43 to 大眼 and 45 to 装甲大眼 (offline name data only)");
            KillTierBoundaries tiers = KillPolicy.ResolveTierBoundaries(KillDefaults.MinorHealthThreshold, KillPolicy.DefaultHeavyHealthThreshold);
            // Same health (400 HP) for all three, so only the flight/armour identity can differ. The armour
            // flag is the FIXTURE's assumption for 45 (its name says 'armoured'); the plugin itself reads
            // that from the enemy's own limb layout, which is NOT verified here.
            KillPresentation plain42 = KillPresentationPolicy.Build(KillKind.Normal, 400f, tiers, true, false);
            KillPresentation plain43 = KillPresentationPolicy.Build(KillKind.Normal, 400f, tiers, true, false);
            KillPresentation armoured45 = KillPresentationPolicy.Build(KillKind.Normal, 400f, tiers, true, true);
            check(plain42.Tier == KillTier.Elite && plain42.Flight == KillFlight.Flying
                && plain43.Tier == KillTier.Elite && plain43.Flight == KillFlight.Flying
                && armoured45.Tier == KillTier.Elite && armoured45.Flight == KillFlight.ArmoredFlying,
                "fixture-42/43/45: at the same 400 HP, 42/43 stay plain flyers while 45 becomes an armoured flyer (fixture inputs)");
            check(KillIconPolicy.TierIconKey(plain42.Tier) == KillIconPolicy.TierIconKey(armoured45.Tier)
                && KillIconPolicy.MarkFor(plain42.Flight) == FlightMark.Wings
                && KillIconPolicy.MarkFor(armoured45.Flight) == FlightMark.ArmorPlate,
                "fixture-42/43: 42/43 and 45 share the ELITE/medium raster but differ in the badge SHAPE (wings vs armour plate)");
            string cue42 = HitSoundPolicy.ResourceName(HitSoundStyle.Bf5, HitCueKind.Kill, plain42.Tier, plain42.Flight);
            string cue43 = HitSoundPolicy.ResourceName(HitSoundStyle.Bf5, HitCueKind.Kill, plain43.Tier, plain43.Flight);
            string cue45 = HitSoundPolicy.ResourceName(HitSoundStyle.Bf5, HitCueKind.Kill, armoured45.Tier, armoured45.Flight);
            check(cue42 == "KillFeed.Audio.hit-bf5-kill-flyer-elite.wav"
                && cue43 == cue42
                && cue45 == "KillFeed.Audio.hit-bf5-kill-armor-flyer-elite.wav",
                "fixture-42/43/45: 42 and 43 name the same flying-elite cue, 45 names the armoured-flying-elite cue (offline routing only)");
            check(cue42 != cue45
                && KillIconPolicy.MarkFor(plain43.Flight) != KillIconPolicy.MarkFor(armoured45.Flight),
                "fixture-42/43/45: the graphic AND the cue differ between a plain and an armoured big-eye at the same health");
        }
    }

    // The shipped cue matrix, counted the same way HitSoundPolicyTests/HitSoundAssetTests count it.
    private static int CountShippedCueNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var style in new[] { HitSoundStyle.Bf1, HitSoundStyle.Bf5, HitSoundStyle.Bf6 })
            foreach (var kind in new[] { HitCueKind.Hit, HitCueKind.WeakspotHit, HitCueKind.ArmorHit, HitCueKind.Kill })
                foreach (var tier in new[] { KillTier.Minor, KillTier.Elite, KillTier.Boss })
                    foreach (var flight in new[] { KillFlight.None, KillFlight.Flying, KillFlight.ArmoredFlying })
                        names.Add(HitSoundPolicy.ResourceName(style, kind, tier, flight));
        return names.Count;
    }

    // Fully assigned slot: the plugin fills these fields, so the test states them all explicitly.
    private static KillIconSlot Slot(int token, float at, bool preview = false)
        => new KillIconSlot
        {
            Pointer = IntPtr.Zero,
            X = 0f,
            Y = 0f,
            Z = 0f,
            Clearance = 0f,
            Tier = KillTier.Minor,
            Flight = KillFlight.None,
            At = at,
            Preview = preview,
            Token = token,
        };

    // Minimal reader for the embedded name table: {"IDs":[42,43],"Name":"大眼"} per line.
    private static Dictionary<uint, string> LoadShippedNames(Action<bool, string> check)
    {
        try
        {
            using Stream stream = typeof(ReviewFixTests).Assembly.GetManifestResourceStream("KillFeed.EnemyNames.defaults.json");
            if (stream == null)
            {
                check(false, "fixture-42/43/45: the shipped name table is embedded in the test assembly");
                return null;
            }
            using var reader = new StreamReader(stream);
            string json = reader.ReadToEnd();
            var names = new Dictionary<uint, string>();
            int cursor = 0;
            while (true)
            {
                int idsAt = json.IndexOf("\"IDs\":[", cursor, StringComparison.Ordinal);
                if (idsAt < 0) break;
                int idsStart = idsAt + "\"IDs\":[".Length;
                int idsEnd = json.IndexOf(']', idsStart);
                int nameAt = json.IndexOf("\"Name\":\"", idsEnd, StringComparison.Ordinal);
                if (idsEnd < 0 || nameAt < 0) break;
                int nameStart = nameAt + "\"Name\":\"".Length;
                int nameEnd = json.IndexOf('"', nameStart);
                if (nameEnd < 0) break;
                string name = json.Substring(nameStart, nameEnd - nameStart);
                foreach (string part in json.Substring(idsStart, idsEnd - idsStart).Split(','))
                {
                    if (uint.TryParse(part.Trim(), out uint id)) names[id] = name;
                }
                cursor = nameEnd;
            }
            check(names.Count >= 36, "fixture-42/43/45: the shipped name table parsed into " + names.Count + " ids");
            return names;
        }
        catch (Exception error)
        {
            check(false, "fixture-42/43/45: reading the shipped name table failed (" + error.GetType().Name + ")");
            return null;
        }
    }
}
