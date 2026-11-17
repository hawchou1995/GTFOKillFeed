using System;
using HawChow.GTFOKillFeed;

// 1.2.18: the four independent channel splits that landed with the company 1.2.17 build, asserted on the
// production types (SoundSettings / SoundConfigPlan / KillViewPolicy / CorpseSwitches / HitMarkerPolicy).
internal static class ChannelSwitchTests
{
    internal static void Run(Action<bool, string> check)
    {
        // ---- audio: master gain, per-channel switch and per-channel volume ----
        SoundSettings unity = SoundSettings.Resolve(true, true, true, 0.4f, 0.4f, 0.8f, 0.8f);
        check(Math.Abs(unity.HitVolume - 0.4f) < 0.0001f && Math.Abs(unity.MasterGain - 1f) < 0.0001f
            && unity.HitPlays && unity.KillPlays && unity.AnyPlays,
            "audio: at the 1.2.16 master volume of 0.8 a channel volume of 0.4 is exactly 0.4");

        SoundSettings migrated = SoundSettings.Resolve(true, true, true, 0.8f, 0.8f, 0.3f, 0.8f);
        check(Math.Abs(migrated.HitVolume - 0.3f) < 0.0001f && Math.Abs(migrated.KillVolume - 0.3f) < 0.0001f,
            "audio: a legacy cfg with Volume=0.3 keeps the pre-split output of 0.3 on both channels");

        SoundSettings masterOff = SoundSettings.Resolve(false, true, true, 0.5f, 0.5f, 0.8f, 0.8f);
        check(!masterOff.HitPlays && !masterOff.KillPlays && !masterOff.AnyPlays,
            "audio: the legacy HitSound/Enabled=false still silences both split channels");

        SoundSettings oneChannel = SoundSettings.Resolve(true, true, true, 0f, 1f, 0.8f, 0.8f);
        check(!oneChannel.HitPlays && oneChannel.KillPlays,
            "audio: a zero channel volume stops only that channel, not the switch state");

        SoundSettings degraded = SoundSettings.Resolve(true, true, true, float.NaN, float.NaN, float.NaN, 0.4f);
        check(Math.Abs(degraded.MasterGain - 1f) < 0.0001f && Math.Abs(degraded.HitVolume - 0.4f) < 0.0001f,
            "audio: NaN master and NaN channel volumes fall back to the reference gain and the channel default");

        // ---- audio migration: the scan runs before the first Bind, so the plan must decide the seeds ----
        SoundConfigPlan fresh = SoundConfigPlan.FromText(null, false);
        check(fresh.Mode == SoundConfigMode.NewInstall && !fresh.BindHitEnabled && !fresh.BindKillEnabled
            && Math.Abs(fresh.BindHitVolume - 0.4f) < 0.0001f,
            "audio migration: a brand-new cfg seeds both channels off at 0.4 so a fresh install stays silent");

        SoundConfigPlan legacy = SoundConfigPlan.FromText("[HitSound]\nEnabled = true\nVolume = 0.8\nStyle = BF5\n", true);
        check(legacy.Mode == SoundConfigMode.Legacy && legacy.LegacyPresent && !legacy.ChannelKeysPresent
            && legacy.BindHitEnabled && legacy.BindKillEnabled
            && Math.Abs(legacy.BindHitVolume - 0.8f) < 0.0001f && Math.Abs(legacy.Defaults().HitVolume - 0.8f) < 0.0001f,
            "audio migration: a 1.2.16 cfg seeds both channels on at the legacy volume, so the split is inaudible");

        SoundConfigPlan legacyQuiet = SoundConfigPlan.FromText("[HitSound]\nEnabled = false\nVolume = 0.3\n", true);
        check(legacyQuiet.Mode == SoundConfigMode.Legacy && !legacyQuiet.Defaults().Master
            && Math.Abs(legacyQuiet.Defaults().HitVolume - 0.3f) < 0.0001f,
            "audio migration: a legacy cfg with Enabled=false stays silent and keeps its 0.3 master gain");

        SoundConfigPlan modern = SoundConfigPlan.FromText(
            "[HitSound]\nEnabled = true\nHitEnabled = false\nKillEnabled = true\nHitVolume = 0.2\nKillVolume = 1\n", true);
        check(modern.Mode == SoundConfigMode.Modern && modern.ChannelKeysPresent
            && !modern.BindHitEnabled && modern.BindKillEnabled
            && Math.Abs(modern.BindHitVolume - 0.2f) < 0.0001f && Math.Abs(modern.BindKillVolume - 1f) < 0.0001f,
            "audio migration: explicit channel keys always win over the migration seeding");

        SoundConfigPlan unreadable = SoundConfigPlan.FromUnreadableFile();
        check(unreadable.Mode == SoundConfigMode.Unreadable && unreadable.BindHitEnabled && unreadable.BindKillEnabled
            && unreadable.Defaults().HitPlays && unreadable.Defaults().KillPlays,
            "audio migration: an unreadable cfg keeps the pre-split behaviour instead of going silent");

        // ---- kill view: legacy Display migration and the two independent channels ----
        KillViewConfigFile legacyHead = KillViewConfigFile.Parse("[KillIcon]\nDisplay = Head\n");
        KillViewSwitches headSwitch = KillViewPolicy.ResolveSwitches(legacyHead);
        check(headSwitch.Mode == HawChow.GTFOKillFeed.ViewMode.Legacy && headSwitch.LegacyKeyPresent
            && headSwitch.HeadEnabled && !headSwitch.CardEnabled,
            "kill view: a 1.2.16 cfg with Display=Head keeps only the head icon channel on");

        KillViewConfigFile legacyCard = KillViewConfigFile.Parse("[KillIcon]\nDisplay = Card\n");
        KillViewSwitches cardSwitch = KillViewPolicy.ResolveSwitches(legacyCard);
        check(cardSwitch.Mode == HawChow.GTFOKillFeed.ViewMode.Legacy && !cardSwitch.HeadEnabled && cardSwitch.CardEnabled,
            "kill view: Display=Card keeps only the card channel on");

        KillViewConfigFile explicitBoth = KillViewConfigFile.Parse(
            "[KillIcon]\nDisplay = Head\nHeadEnabled = true\n[Card]\nEnabled = true\n");
        KillViewSwitches bothSwitch = KillViewPolicy.ResolveSwitches(explicitBoth);
        check(bothSwitch.Mode == HawChow.GTFOKillFeed.ViewMode.Explicit && bothSwitch.HeadEnabled && bothSwitch.CardEnabled,
            "kill view: setting both switches explicitly leaves the legacy Display key inert and keeps both channels on");

        bool wroteHead = false;
        bool wroteCard = true;
        string wroteMode = null;
        bool wrote = KillViewConfigApply.Apply(cardSwitch,
            value => wroteHead = value, value => wroteCard = value, value => wroteMode = value);
        check(wrote && !wroteHead && wroteCard && wroteMode == cardSwitch.ModeToken,
            "kill view: the migration writes head/card/mode through one pass of the apply callbacks");

        KillViewSwitches noKeys = KillViewPolicy.ResolveSwitches(null);
        check(!noKeys.LegacyKeyPresent && noKeys.CardEnabled && !noKeys.HeadEnabled,
            "kill view: with no cfg at all the card channel is the default and the head channel is off");

        check(Math.Abs(KillViewPolicy.CompositeAlpha(1f, 0.5f) - 0.5f) < 0.0001f
            && Math.Abs(KillViewPolicy.CompositeAlpha(0.4f, 0.5f) - 0.2f) < 0.0001f,
            "kill view: the configured opacity multiplies the lifecycle fade exactly once");
        check(KillViewPolicy.Opacity(float.NaN) == KillViewPolicy.DefaultOpacity
            && KillViewPolicy.Opacity(-1f) == 0f && KillViewPolicy.Opacity(5f) == 1f,
            "kill view: opacity clamps to 0-1 and falls back to 0.5 for NaN/Infinity");
        check(!KillViewPolicy.HeadVisible(false, true, true)
            && !KillViewPolicy.HeadVisible(true, true, false)
            && !KillViewPolicy.HeadVisible(true, false, true)
            && KillViewPolicy.HeadVisible(true, true, true),
            "kill view: the head channel needs its switch, the game HUD and a camera");
        check(KillViewPolicy.CardVisible(true, true) && !KillViewPolicy.CardVisible(false, true)
            && !KillViewPolicy.CardVisible(true, false),
            "kill view: the card channel answers only to its own switch and the game HUD");

        // ---- kill view: visible-hitbox avoidance ----
        var occluders = new[] { new ScreenBox(0f, 100f, 200f, 200f) };
        var icon = new ScreenBox(50f, 150f, 120f, 190f);
        IconPlacement lifted = KillViewPolicy.ResolvePlacement(icon, occluders, 1, 0f, 1080f);
        check(!lifted.Hidden && lifted.Top <= 100f - icon.Height,
            "kill view: an icon whose box overlaps a visible hitbox is pushed above it");
        var lowCeiling = new[] { new ScreenBox(0f, 0f, 200f, 400f) };
        IconPlacement noRoom = KillViewPolicy.ResolvePlacement(new ScreenBox(50f, 20f, 120f, 60f), lowCeiling, 1, 0f, 1080f);
        check(noRoom.Hidden, "kill view: an icon with no room left above the top edge hides instead of covering the body");
        IconPlacement untouched = KillViewPolicy.ResolvePlacement(icon, new ScreenBox[0], 0, 0f, 1080f);
        check(!untouched.Hidden && Math.Abs(untouched.Top - icon.Top) < 0.0001f,
            "kill view: with no occluders the icon keeps its projected position");

        // ---- hit marker: hit and kill are separate channels with one submit gate ----
        check(HitMarkerPolicy.ChannelForKind(true, false, HitMarkerKind.Normal)
            && !HitMarkerPolicy.ChannelForKind(true, false, HitMarkerKind.Kill)
            && HitMarkerPolicy.ChannelForKind(false, true, HitMarkerKind.Kill),
            "hit marker: the kill line has its own switch, independent of the hit markers");
        check(HitMarkerPolicy.AcceptsSubmission(HitMarkerPolicy.ChannelEnabled(true, true), HitMarkerKind.Normal)
            && !HitMarkerPolicy.AcceptsSubmission(HitMarkerPolicy.ChannelEnabled(false, true), HitMarkerKind.Normal)
            && !HitMarkerPolicy.AcceptsSubmission(HitMarkerPolicy.ChannelEnabled(true, false), HitMarkerKind.Normal)
            && !HitMarkerPolicy.AcceptsSubmission(true, HitMarkerKind.None),
            "hit marker: submission requires the master switch, the channel switch and a resolved kind");

        // ---- corpse: five independent sub-switches under the master switch ----
        var allOn = new CorpseSwitches(true, true, true, true, true, true, true);
        check(allOn.LaunchActive && allOn.DismemberActive && allOn.ChunksActive
            && allOn.BurstVisualActive && allOn.BurstSoundActive && allOn.AnyBurstActive,
            "corpse switches: with everything on the split reproduces the 1.2.16 behaviour exactly");
        var launchOff = new CorpseSwitches(true, false, true, true, true, true, true);
        check(!launchOff.LaunchActive && launchOff.DismemberActive && launchOff.ChunksActive && launchOff.BurstVisualActive,
            "corpse switches: Launch=true -> false stops the launch only");
        var dismemberOff = new CorpseSwitches(true, true, false, true, true, true, true);
        check(!dismemberOff.DismemberActive && dismemberOff.ChunksActive && dismemberOff.LaunchActive,
            "corpse switches: Dismember=false stops the limb break only");
        var fxOff = new CorpseSwitches(true, true, true, true, false, true, true);
        check(!fxOff.BurstVisualActive && !fxOff.BurstSoundActive && !fxOff.AnyBurstActive && fxOff.LaunchActive,
            "corpse switches: Corpse/ExplosionFx still masters both burst halves");
        var soundOff = new CorpseSwitches(true, true, true, true, true, true, false);
        check(soundOff.BurstVisualActive && !soundOff.BurstSoundActive && soundOff.AnyBurstActive,
            "corpse switches: BurstSound=false leaves the burst visual running");
        var effectsOff = new CorpseSwitches(false, true, true, true, true, true, true);
        check(!effectsOff.LaunchActive && !effectsOff.DismemberActive && !effectsOff.ChunksActive
            && !effectsOff.AnyBurstActive,
            "corpse switches: the Corpse/Enabled master switch still gates every sub-switch");

        check(!FeedbackPolicy.ShouldApplyLaunch(false, false, 1f)
            && !FeedbackPolicy.ShouldApplyLaunch(true, true, 1f)
            && !FeedbackPolicy.ShouldApplyLaunch(true, false, 0.01f)
            && FeedbackPolicy.ShouldApplyLaunch(true, false, 0.02f),
            "corpse launch: the switch, the closed state and the 0.02 s first attempt all gate the impulse");
        check(!FeedbackPolicy.ShouldLimitRagdoll(false, false)
            && !FeedbackPolicy.ShouldLimitRagdoll(true, true)
            && FeedbackPolicy.ShouldLimitRagdoll(true, false),
            "corpse launch: disabling the launch removes the per-frame ragdoll limiting but not the cleanup");
        check(!FeedbackPolicy.ShouldTrackChunks(false) && FeedbackPolicy.ShouldTrackChunks(true),
            "corpse chunks: no gib is registered when Chunks is off");
        check(!FeedbackPolicy.ShouldFlushCorpseSound(false) && FeedbackPolicy.ShouldFlushCorpseSound(true),
            "corpse burst: a disabled burst sound flushes the pending coalesced cue instead of flushing when ExplosionFx is on");
        check(!FeedbackPolicy.ShouldReportUnavailableLaunch(0, false, false)
            && FeedbackPolicy.ShouldReportUnavailableLaunch(0, false, true),
            "corpse launch: a disabled launch channel prints no unavailable receipt at all");

        // ---- R3: which search results may become a lease (fail closed) ----
        var liveHost = new FeedbackPolicy.RagdollCandidate(livingHost: true, scriptKnown: true, ragdolled: true, nonKinematicBody: true);
        var notRagdolledScript = new FeedbackPolicy.RagdollCandidate(false, true, false, true);
        var unreadableFlag = new FeedbackPolicy.RagdollCandidate(false, false, false, true);
        var refHostOnly = new FeedbackPolicy.RagdollCandidate(false, false, false, false);
        var kinematicOnlyBody = new FeedbackPolicy.RagdollCandidate(false, false, false, false);
        var rawBodyOk = new FeedbackPolicy.RagdollCandidate(false, false, false, true);
        check(!FeedbackPolicy.AcceptRagdollCandidate(FeedbackPolicy.RagdollRung.RagdollScript, liveHost)
            && !FeedbackPolicy.AcceptRagdollCandidate(FeedbackPolicy.RagdollRung.EnemyRagdollRef, liveHost)
            && !FeedbackPolicy.AcceptRagdollCandidate(FeedbackPolicy.RagdollRung.RawBodies, liveHost),
            "candidate (R3): rungs 2-4 may not lease a host that is (or contains) the living enemy or its model - fail closed");
        // 1.2.19 (real-machine fix): rung 1 is the game's OWN reference and no longer consults the living-hierarchy
        // heuristic at all - after a death the game parents its ragdoll under the model, and that veto refused 3 of
        // 9 corpses in the real log (`rung=ragdoll-instance;reason=instance-under-living-model`). Only provable
        // nonsense is refused, each with its own reason.
        check(FeedbackPolicy.AcceptRagdollInstance(FeedbackPolicy.ClassifyRagdollInstance(present: true, isLivingModelRoot: false, unreadable: false))
            && FeedbackPolicy.AcceptRagdollInstance(FeedbackPolicy.ClassifyRagdollBodies(FeedbackPolicy.RagdollInstanceVerdict.Accept, 3)),
            "candidate (1.2.19): a RagdollInstance parented under the living model (the real-machine case) is accepted, and a lease with bodies really reaches the applied launch");
        check(!FeedbackPolicy.AcceptRagdollInstance(FeedbackPolicy.ClassifyRagdollInstance(true, true, false))
            && FeedbackPolicy.Reason(FeedbackPolicy.ClassifyRagdollInstance(true, true, false)) == "instance-is-model-root"
            && !FeedbackPolicy.AcceptRagdollInstance(FeedbackPolicy.ClassifyRagdollBodies(FeedbackPolicy.RagdollInstanceVerdict.Accept, 0))
            && FeedbackPolicy.Reason(FeedbackPolicy.ClassifyRagdollBodies(FeedbackPolicy.RagdollInstanceVerdict.Accept, 0)) == "instance-no-bodies",
            "candidate (1.2.19): the living model root ITSELF and an instance whose lease carries no rigidbody are both refused, each with its own reason");
        check(!FeedbackPolicy.AcceptRagdollInstance(FeedbackPolicy.ClassifyRagdollInstance(false, false, false))
            && FeedbackPolicy.Reason(FeedbackPolicy.ClassifyRagdollInstance(false, false, false)) == "instance-missing"
            && FeedbackPolicy.Reason(FeedbackPolicy.ClassifyRagdollInstance(true, false, true)) == "instance-unreadable"
            && FeedbackPolicy.Reason(FeedbackPolicy.ClassifyRagdollInstance(true, false, false)) == "instance-accepted"
            && FeedbackPolicy.ClassifyRagdollBodies(FeedbackPolicy.RagdollInstanceVerdict.Accept, -1) == FeedbackPolicy.RagdollInstanceVerdict.Accept,
            "candidate (1.2.19): a null reference, an unreadable one and an unmeasured lease are classified by name, and an unmeasured lease never counts as empty");
        check(!FeedbackPolicy.AcceptRagdollCandidate(FeedbackPolicy.RagdollRung.RagdollScript, notRagdolledScript),
            "candidate (R3): a RagdollScript whose ragdolled flag is FALSE is a living/not-yet-spawned model and is refused (1.2.16 accepted exactly these)");
        check(!FeedbackPolicy.AcceptRagdollCandidate(FeedbackPolicy.RagdollRung.RagdollScript, unreadableFlag),
            "candidate (R3): an unreadable ragdolled flag is refused instead of defaulting to 'usable'");
        check(FeedbackPolicy.AcceptRagdollCandidate(FeedbackPolicy.RagdollRung.RagdollScript, new FeedbackPolicy.RagdollCandidate(false, true, true, false))
            && FeedbackPolicy.AcceptRagdollCandidate(FeedbackPolicy.RagdollRung.EnemyRagdollRef, refHostOnly)
            && FeedbackPolicy.AcceptRagdollCandidate(FeedbackPolicy.RagdollRung.RagdollInstance, refHostOnly),
            "candidate (R3): the game's own ragdoll instance, a RAGDOLLED script and a non-living ref host are still accepted");
        check(!FeedbackPolicy.AcceptRagdollCandidate(FeedbackPolicy.RagdollRung.RawBodies, kinematicOnlyBody)
            && FeedbackPolicy.AcceptRagdollCandidate(FeedbackPolicy.RagdollRung.RawBodies, rawBodyOk),
            "candidate (R3): the raw-rigidbody rung only accepts a non-kinematic body, so a living/prefab hierarchy can never be pushed");
        check(!FeedbackPolicy.AcceptRagdollCandidate((FeedbackPolicy.RagdollRung)99, rawBodyOk),
            "candidate (R3): an unknown rung is refused (no silent default)");

        // ---- F1: cleanup-then-unlock vs unlock-then-cleanup (the order Restore must use) ----
        var ledgerOk = new CorpseDeathLedger();
        ledgerOk.EnterLevel();
        check(ledgerOk.TrySchedule(7), "restore order (F1): the death schedules its corpse");
        ledgerOk.Close(7);
        check(!ledgerOk.TrySchedule(7),
            "restore order (F1): a despawn (no unlock) must not let the same identity schedule a second full corpse");
        ledgerOk.Close(7);            // what Release(hidden,true) / RemoveQueued do during the setup cleanup
        ledgerOk.Unlock(7);          // Restore's finally, AFTER the cleanup
        check(ledgerOk.TrySchedule(7),
            "restore order (F1): a real EnemyAgent.Setup unlocks the reused wrapper only after the cleanup, so its next death IS scheduled");
        var ledgerBad = new CorpseDeathLedger();
        ledgerBad.EnterLevel();
        ledgerBad.TrySchedule(7);
        ledgerBad.Unlock(7);         // the 1.2.18 order: unlock first ...
        ledgerBad.Close(7);          // ... then the cleanup re-closes the same identity
        check(!ledgerBad.TrySchedule(7),
            "restore order (F1, negative control): unlocking before the cleanup re-closes the identity, so the next real death of the reused wrapper is refused - the exact defect this fix removes");

        // ---- F4: the completed-order queue stays bounded and never evicts the newest stamp ----
        var ledgerChurn = new CorpseDeathLedger();
        ledgerChurn.EnterLevel();
        for (int i = 0; i < CorpseDeathLedger.MaxCompleted * 4 + 200; i++) { ledgerChurn.Close(0x9001); ledgerChurn.Unlock(0x9001); }
        check(ledgerChurn.CompletedCount == 0 && ledgerChurn.OrderCount <= CorpseDeathLedger.MaxCompleted * 2 + 1,
            "ledger queue (F4): " + (CorpseDeathLedger.MaxCompleted * 4 + 200) + " close/unlock cycles of ONE identity leave no live entry and a bounded queue (order=" + ledgerChurn.OrderCount + ")");
        var ledgerEvict = new CorpseDeathLedger();
        ledgerEvict.EnterLevel();
        for (int i = 0; i < CorpseDeathLedger.MaxCompleted; i++) ledgerEvict.Close(0x1000 + i);
        ledgerEvict.Close(0x1000);    // re-close: this identity now carries the NEWEST stamp
        for (int i = 0; i < 60; i++) ledgerEvict.Close(0x7000 + i);
        check(ledgerEvict.HasCompleted(0x1000) && ledgerEvict.CompletedCount <= CorpseDeathLedger.MaxCompleted,
            "ledger queue (F4): eviction drops the oldest stamps, never the newest entry of an identity that was closed again (completed=" + ledgerEvict.CompletedCount + ")");
        check(ledgerEvict.HasCompleted(0x7000) && !ledgerEvict.HasCompleted(0x1001),
            "ledger queue (F4): mixed identities - the freshest close survives, the oldest slot is the one that is evicted");

        // ---- R1b: one observation-only probe between the deadline and EndAt, production predicate order ----
        // Production order, one frame per entry: retry (counts an attempt) -> the single impulse gate -> the give-up
        // decision -> terminal observation (which re-enters the SAME gate) -> close. The age sequence models a 60 fps
        // corpse plus one frame gap from jumpFrom to jumpTo; the game hands the body over at `bodyAt`.
        void RunTerminal(float jumpFrom, float jumpTo, bool bodyAvailable, float bodyAt, int preApplied, bool channelOn,
            out bool probed, out int applied, out int receipts, out bool closed)
        {
            int attempts = 0;
            bool sameObject = true;   // one corpse, one physical ragdoll: any push targets the same pointer
            probed = false;
            applied = preApplied;
            receipts = 0;
            closed = false;
            var ages = new System.Collections.Generic.List<float>();
            for (float a = 0f; a < jumpFrom; a += 1f / 60f) ages.Add(a);
            for (float a = jumpTo; a <= 1.2f; a += 1f / 60f) ages.Add(a);
            foreach (float age in ages)
            {
                if (closed) break;
                // The Tick-level gate: with the launch channel off, TryLaunchOrAbandon is never entered at all.
                if (!FeedbackPolicy.ShouldApplyLaunch(channelOn, false, age)) continue;
                // The body existing is not the same as holding a lease: only a lookup (scheduled retry or the terminal
                // probe) can create one, exactly as TryLateRagdoll -> AcceptRagdoll does in production.
                bool hasLease = preApplied > 0;
                if (FeedbackPolicy.ShouldRetryRagdollLookup(false, attempts, age, 0.1f))
                {
                    attempts++;
                    if (bodyAvailable && age >= bodyAt) hasLease = true;
                    continue;
                }
                if (FeedbackPolicy.CanApplyLaunch(hasLease ? FeedbackPolicy.RagdollLease.Usable : FeedbackPolicy.RagdollLease.Missing,
                        true, applied, sameObject, age, 0.1f))
                {
                    applied++;
                    continue;
                }
                if (FeedbackPolicy.ShouldApplyLaunch(hasLease ? FeedbackPolicy.RagdollLease.Usable : FeedbackPolicy.RagdollLease.Missing))
                {
                    if (FeedbackPolicy.ShouldCloseAppliedLaunch(applied, age, 0.1f)) closed = true;
                    continue;
                }
                if (!FeedbackPolicy.ShouldAbandonRagdollLookup(false, attempts, age, 0.1f)) continue;
                if (FeedbackPolicy.ShouldRunFinalRagdollProbe(false, attempts, probed, age, 0.1f))
                {
                    probed = true;
                    if (bodyAvailable && age >= bodyAt) hasLease = true;
                    if (FeedbackPolicy.CanApplyLaunch(hasLease ? FeedbackPolicy.RagdollLease.Usable : FeedbackPolicy.RagdollLease.Missing,
                            true, applied, sameObject, age, 0.1f))
                    {
                        applied++;
                        continue;
                    }
                }
                closed = true;
                if (FeedbackPolicy.ShouldReportUnavailableLaunch(applied, false, channelOn)) receipts = 1;
            }
            // A launched corpse is closed out on the following tick by ShouldCloseAppliedLaunch (already modelled),
            // so an unclosed run here would be a real leak.
            if (applied > 0 && !closed) closed = FeedbackPolicy.ShouldCloseAppliedLaunch(applied, 1.2f, 0.1f);
        }
        RunTerminal(0.39f, 0.60f, true, 0.60f, 0, true, out bool probedBody, out int appliedBody, out int receiptsBody, out bool closedBody);
        check(probedBody && appliedBody == 1 && receiptsBody == 0 && closedBody,
            "terminal probe (B): a trusted body handed over after a 0.39 s -> 0.60 s jump is really launched once through the single gate (applied=" + appliedBody + ", receipts=" + receiptsBody + ")");
        RunTerminal(0.39f, 0.60f, false, 0f, 0, true, out bool probedNone, out int appliedNone, out int receiptsNone, out bool closedNone);
        check(probedNone && appliedNone == 0 && receiptsNone == 1 && closedNone,
            "terminal probe (B): the same jump without a body still closes with exactly one unavailable receipt (applied=" + appliedNone + ", receipts=" + receiptsNone + ")");
        RunTerminal(0.39f, 0.60f, true, 0.60f, 1, true, out bool probedApplied, out int appliedTwice, out int receiptsApplied, out bool closedApplied);
        check(appliedTwice == 1 && receiptsApplied == 0 && closedApplied,
            "terminal probe (B): a corpse that already received its impulse is never pushed a second time on the same object (applied=" + appliedTwice + ", probed=" + probedApplied + "; the applied state closes the launch before any probe)");
        RunTerminal(0.39f, 0.95f, true, 0.95f, 0, true, out bool probedLate, out int appliedLate, out int receiptsLate, out bool closedLate);
        check(!probedLate && appliedLate == 0 && receiptsLate == 1 && closedLate,
            "terminal probe (B): a jump that lands past EndAt neither captures nor launches, and still closes with a receipt (applied=" + appliedLate + ")");
        RunTerminal(0.39f, 0.60f, true, 0.60f, 0, false, out bool probedOff, out int appliedOff, out int receiptsOff, out bool closedOff);
        check(!probedOff && appliedOff == 0 && receiptsOff == 0 && !closedOff,
            "terminal probe (B): with Corpse/Launch off the decision path is never entered, so nothing is probed, launched or reported as unavailable (applied=" + appliedOff + ")");
        check(!FeedbackPolicy.ShouldRunFinalRagdollProbe(false, FeedbackPolicy.MaxRagdollLookupAttempts + 1, false, 0.6f, 0.1f)
            && !FeedbackPolicy.ShouldRunFinalRagdollProbe(false, 0, true, 0.6f, 0.1f)
            && !FeedbackPolicy.ShouldRunFinalRagdollProbe(true, 0, false, 0.6f, 0.1f)
            && !FeedbackPolicy.ShouldRunFinalRagdollProbe(false, 0, false, FeedbackPolicy.CorpseEndAt(0.1f), 0.1f),
            "terminal probe (B): bounded to once per corpse, to a spent-but-not-exhausted schedule, to a corpse without a ragdoll and to strictly before EndAt");
        check(FeedbackPolicy.CanApplyLaunch(FeedbackPolicy.RagdollLease.Usable, true, 0, false, FeedbackPolicy.CorpseEndAt(0.1f) - 0.01f, 0.1f)
            && !FeedbackPolicy.CanApplyLaunch(FeedbackPolicy.RagdollLease.Usable, true, 0, false, FeedbackPolicy.CorpseEndAt(0.1f), 0.1f)
            && !FeedbackPolicy.CanApplyLaunch(FeedbackPolicy.RagdollLease.Missing, true, 0, false, 0.6f, 0.1f),
            "terminal probe (B): the single impulse gate stays open until EndAt (exclusive) and never for an unusable lease - the corpse's own EndAt is unchanged");
        // ---- Finding 3 (FIXED): the head channel must not leak into the card channel ----
        // Production now mints a token only when KillIcon/HeadEnabled is on at the moment of the kill, so the two
        // rows below are the whole cross-switch contract: Head OFF -> no token -> CardLifetime alone (1.2 s with the
        // stated config), and nothing was queued for a later hot-enable to replay; Head ON -> the documented
        // KillCardPolicy rule keeps the card alive until the icon it owns has faded.
        float cardHeadOff = KillCardPolicy.ExpiresAt(0f, 0, 0f, 1.2f, 0.35f);
        float cardHeadOn = KillCardPolicy.ExpiresAt(0f, 1, 2.2f, 1.2f, 0.35f);
        check(Math.Abs(cardHeadOff - 1.2f) < 0.0001f,
            "card lifetime (finding 3): HeadEnabled=false at the kill mints no token, so CardLifetime=1.2 is the whole lifetime (1.2 s, not 2.55 s)");
        check(Math.Abs(cardHeadOn - (2.2f + 0.35f)) < 0.0001f,
            "card lifetime (finding 3): with HeadEnabled=true the card still outlives the icon it owns (2.2 + 0.35) - the company rule is preserved when the channel is on");
        check(cardHeadOff < cardHeadOn,
            "card lifetime (finding 3): the head switch really changes the card lifetime - a disabled channel cannot extend it");
    }
}
