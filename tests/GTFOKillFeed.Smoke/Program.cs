using System;
using HawChow.GTFOKillFeed;

internal static class Program
{
    private static int _assertions;

    private static void Main()
    {
        var weakLethal = KillPolicy.ClassifyLethalHit(true, 10f, 0f, true, true);
        Check(weakLethal.Credited && weakLethal.Kind == KillKind.Weakspot, "lethal observed weakspot hit selects weak icon");

        var bodyLethalAfterWeakEarlier = KillPolicy.ClassifyLethalHit(true, 10f, 0f, true, false);
        Check(bodyLethalAfterWeakEarlier.Credited && bodyLethalAfterWeakEarlier.Kind == KillKind.Normal, "lethal non-weakspot hit stays normal even after earlier weak hits");

        var unobservedLimb = KillPolicy.ClassifyLethalHit(true, 10f, 0f, false, true);
        Check(unobservedLimb.Credited && unobservedLimb.Kind == KillKind.Normal, "missing limb evidence never guesses weakspot");
        Check(FeedbackPolicy.ShouldKeepShotgunDamageNumbers(true, true, false)
            && !FeedbackPolicy.ShouldKeepShotgunDamageNumbers(false, true, false)
            && FeedbackPolicy.ShouldKeepShotgunDamageNumbers(true, false, false), "shotgun damage-number visibility is independently switchable");
        Check(FeedbackPolicy.ShouldMergeShotgunDamageNumbers(true, true)
            && !FeedbackPolicy.ShouldMergeShotgunDamageNumbers(true, false)
            && !FeedbackPolicy.ShouldMergeShotgunDamageNumbers(false, true), "pellet merging is opt-in only for shotgun shots and defaults on");
        Check(FeedbackPolicy.ShouldDisplayDamageNumber(true, true, false)
            && FeedbackPolicy.ShouldDisplayDamageNumber(true, true, true)
            && !FeedbackPolicy.ShouldDisplayDamageNumber(true, false, true)
            && !FeedbackPolicy.ShouldDisplayDamageNumber(false, true, true), "shotgun visibility is separate from other weapons but remains under the global damage-number switch");
        Check(FeedbackPolicy.IsShotgunFireScopeCurrent(12f, 12f, false)
            && FeedbackPolicy.IsShotgunFireScopeCurrent(12f, 12.25f, false)
            && !FeedbackPolicy.IsShotgunFireScopeCurrent(12f, 12f, true)
            && !FeedbackPolicy.IsShotgunFireScopeCurrent(12f, 12.6f, false)
            && !FeedbackPolicy.IsShotgunFireScopeCurrent(12f, 11.9f, false),
            "shotgun pellets belong to a time-boxed volley: a later frame is still the same shot, a drained volley takes nothing");
        Check(FeedbackPolicy.ShouldSuppressShotgunDamageNumber(true, false, false)
            && !FeedbackPolicy.ShouldSuppressShotgunDamageNumber(false, false, false)
            && !FeedbackPolicy.ShouldSuppressShotgunDamageNumber(true, true, false)
            && !FeedbackPolicy.ShouldSuppressShotgunDamageNumber(true, false, true), "shotgun suppression hides only captured non-preview hits when the option is off");
        var fireScopeStack = new ShotgunDamageNumberScopeStack();
        var outerFireScope = fireScopeStack.Begin(true, true, 12, 44);
        var innerFireScope = fireScopeStack.Begin(true, true, 12, 44);
        Check(ReferenceEquals(fireScopeStack.Current, innerFireScope)
            && !fireScopeStack.TryEnd(outerFireScope) && ReferenceEquals(fireScopeStack.Current, innerFireScope)
            && fireScopeStack.TryEnd(innerFireScope) && ReferenceEquals(fireScopeStack.Current, outerFireScope)
            && fireScopeStack.TryEnd(outerFireScope) && fireScopeStack.Current == null, "nested fire scopes restore in LIFO order and ignore out-of-order finalizers");
        var targetScopeStack = new ShotgunDamageTargetScopeStack();
        long outerTarget = targetScopeStack.Enter(11);
        long innerTarget = targetScopeStack.Enter(22);
        targetScopeStack.Exit(innerTarget);
        Check(outerTarget == 0 && innerTarget == 11 && targetScopeStack.Current == 11,
            "nested damage target scopes restore the enclosing enemy identity");
        targetScopeStack.Exit(outerTarget);
        Check(targetScopeStack.Current == 0, "damage target scope restores the empty target after completion");
        var shotgunBatch = new ShotgunDamageNumberBatch();
        Check(shotgunBatch.Add(11, 10f, 20f, 30f, 4f, HitKind.Normal)
            && shotgunBatch.Add(11, 14f, 24f, 34f, 6f, HitKind.Normal)
            && shotgunBatch.Add(11, 12f, 22f, 32f, 1f, HitKind.Normal)
            && shotgunBatch.Add(22, -2f, 3f, 8f, 9f, HitKind.Weakspot), "shotgun pellet samples enter per-enemy groups");
        var mergedShotgun = shotgunBatch.Drain();
        Check(mergedShotgun.Length == 2 && mergedShotgun[0].EnemyIdentity == 11 && mergedShotgun[0].PelletHits == 3
            && Math.Abs(mergedShotgun[0].Damage - 11f) < 0.0001f
            && Math.Abs(mergedShotgun[0].X - 12f) < 0.0001f && Math.Abs(mergedShotgun[0].Y - 22f) < 0.0001f
            && Math.Abs(mergedShotgun[0].Z - 32f) < 0.0001f && mergedShotgun[0].Kind == HitKind.Normal
            && mergedShotgun[1].EnemyIdentity == 22 && mergedShotgun[1].Damage == 9f && mergedShotgun[1].Kind == HitKind.Weakspot,
            "one shotgun shot produces one summed damage number per enemy at the mean impact point");
        Check(shotgunBatch.Drain().Length == 0
            && shotgunBatch.Add(11, 1f, 2f, 3f, 5f, HitKind.Weakspot)
            && shotgunBatch.Drain().Length == 1, "drained pellet aggregation cannot leak across consecutive shotgun shots");
        Check(!shotgunBatch.Add(0, 0f, 0f, 0f, 1f, HitKind.Normal)
            && !shotgunBatch.Add(11, float.NaN, 0f, 0f, 1f, HitKind.Normal)
            && !shotgunBatch.Add(11, 0f, 0f, 0f, -1f, HitKind.Normal)
            && shotgunBatch.TargetCount == 0, "invalid shotgun aggregation samples are rejected without creating entries");
        var mixedShotgunKinds = new ShotgunDamageNumberBatch();
        mixedShotgunKinds.Add(33, 0f, 0f, 0f, 2f, HitKind.Weakspot);
        mixedShotgunKinds.Add(33, 0f, 0f, 0f, 3f, HitKind.Armor);
        Check(mixedShotgunKinds.Drain()[0].Kind == HitKind.Normal, "mixed pellet types do not label their entire summed damage as weakspot or armor");
        Check(!KillPolicy.ClassifyLethalHit(false, 10f, 0f, true, true).Credited, "non-local damage is not shown");
        Check(!KillPolicy.ClassifyLethalHit(true, 10f, 0.1f, true, true).Credited, "non-lethal weakspot hit is not a kill");
        Check(!KillPolicy.ClassifyLethalHit(true, 0f, 0f, true, true).Credited, "duplicate zero-health observation is not a kill");
        Check(KillPolicy.CrossedLethalHealth(10f, 0f), "positive health crossing zero triggers immediate corpse scheduling");
        Check(KillPolicy.CrossedLethalHealth(10f, -1f), "overkill still crosses the lethal boundary");
        Check(!KillPolicy.CrossedLethalHealth(0f, 0f), "duplicate zero-health observation does not reschedule");
        Check(!KillPolicy.CrossedLethalHealth(10f, 0.01f), "nonlethal damage does not schedule corpse feedback");
        Check(!KillPolicy.CrossedLethalHealth(float.NaN, 0f) && !KillPolicy.CrossedLethalHealth(10f, float.NaN), "invalid health snapshots never trigger corpse scheduling");

        Check(KillPolicy.IconKey(KillKind.Normal) == "kill-normal.png", "normal kill selects normal resource key");
        Check(KillPolicy.IconKey(KillKind.Weakspot) == "kill-weakspot.png", "weakspot kill selects weakpoint resource key");
        Check(KillPolicy.ClassifyTier(119f) == KillTier.Minor, "119 max health is minor");
        Check(KillPolicy.ClassifyTier(120f) == KillTier.Elite, "120 max health is elite");
        Check(KillPolicy.ClassifyTier(999.9f) == KillTier.Elite, "999.9 max health is elite");
        Check(KillPolicy.ClassifyTier(1000f) == KillTier.Boss, "1000 max health is boss");
        Check(KillPolicy.ClassifyTier(float.NaN) == KillTier.Minor, "unreadable health falls back to minor");
        Check(KillPolicy.ClassifyTier(80f, 75f, 500f) == KillTier.Elite, "configured thresholds change health tiers");
        Check(KillPolicy.ClassifyTier(120f, float.NaN, 500f) == KillTier.Elite, "invalid threshold input falls back safely");
        Check(KillDefaults.IconSize >= 32f && KillDefaults.CardLifetime > 0f && KillDefaults.FadeSeconds >= 0f
            && KillDefaults.MaxCards >= 1 && KillDefaults.MaxDistance >= 1f, "published config defaults are in range");
        Check(KillPolicy.FormatDistance(12.49f) == "12m", "distance rounds down below half meter");
        Check(KillPolicy.FormatDistance(12.5f) == "13m", "distance rounds halves away from zero");
        Check(KillPolicy.FormatDistance(1100f) == "999m+", "distance is clamped at default ceiling");
        Check(KillPolicy.FormatDistance(100f, 25f) == "25m+", "configured distance ceiling is applied");
        Check(KillPolicy.FormatDistance(float.NaN) == "—", "unreadable distance has explicit placeholder");
        Check(KillPolicy.FormatHealth(120f) == "120 HP", "enemy maximum health formatting is stable");
        Check(KillPolicy.TierLabel(KillTier.Elite, "zh") == "精英" && KillPolicy.TierLabel(KillTier.Boss, "en") == "BOSS", "tier labels follow selected language");
        var actualMonsterName = new KillCard(KillKind.Normal, KillTier.Minor, "Enemy_Big_Shooter", 10f, 80f, 0f);
        Check(actualMonsterName.EnemyName == "Enemy_Big_Shooter", "card preserves the game's actual monster name verbatim");

        var ledger = new KillLedger();
        Check(ledger.TryCredit(0x1234), "first death callback credits");
        Check(!ledger.TryCredit(0x1234), "InstantDead and OnDead dedupe to one feed entry");
        Check(ledger.Count == 1 && !ledger.TryCredit(0), "zero pointers never enter the dedupe ledger");
        ledger.Reset();
        Check(ledger.TryCredit(0x1234), "ledger resets between expeditions");
        var corpseLedger = new CorpseDeathLedger();
        Check(!corpseLedger.TrySchedule(0x2345), "lobby teardown cannot enqueue corpse feedback");
        corpseLedger.EnterLevel();
        Check(corpseLedger.TrySchedule(0x2345), "first in-level death callback schedules corpse feedback");
        Check(!corpseLedger.TrySchedule(0x2345) && corpseLedger.Count == 1, "OnDead and InstantDead callbacks dedupe to one corpse effect");
        Check(!corpseLedger.TrySchedule(0), "zero identity cannot schedule corpse feedback");
        corpseLedger.Close(0x2345);
        Check(!corpseLedger.TrySchedule(0x2345) && corpseLedger.HasCompleted(0x2345),
            "R2: a death already handled to the end is not scheduled again by the pooled wrapper's late OnDead (the real sid1 @377.12 s -> sid3 @385.48 s double schedule)");
        corpseLedger.Unlock(0x2345);
        Check(corpseLedger.TrySchedule(0x2345) && !corpseLedger.HasCompleted(0x2345),
            "R2: only a real EnemyAgent.Setup reuse (or a schedule that failed before becoming a corpse) unlocks the identity for a new death");
        corpseLedger.Close(0x2345);
        for (int i = 0; i < CorpseDeathLedger.MaxCompleted + 8; i++) corpseLedger.Close(0x5000 + i);
        Check(corpseLedger.CompletedCount <= CorpseDeathLedger.MaxCompleted,
            "R2: the finished-death memory is bounded (oldest identities are dropped first), so a long level cannot grow it without limit");
        corpseLedger.Reset();
        Check(corpseLedger.Count == 0 && corpseLedger.CompletedCount == 0, "corpse death ledger resets between expeditions");
        Check(!corpseLedger.TrySchedule(0x2345), "leaving the level closes the corpse scheduling gate");
        corpseLedger.EnterLevel();
        Check(corpseLedger.TrySchedule(0x2345), "next level reopens the corpse scheduling gate");
        corpseLedger.Reset();

        var queue = new KillCardQueue();
        var configuredDistanceCard = new KillCard(KillKind.Normal, KillTier.Minor, "Striker", 100f, 80f, 0f, 25f);
        Check(configuredDistanceCard.DistanceText == "25m+", "kill-card model honors configured distance ceiling");
        var first = new KillCard(KillKind.Normal, KillTier.Minor, "Striker", 7f, 100f, 0f);
        var second = new KillCard(KillKind.Weakspot, KillTier.Elite, "Shooter", 12f, 120f, 0.2f);
        var third = new KillCard(KillKind.Normal, KillTier.Boss, "Tank", 20f, 1000f, 0.4f);
        Check(queue.Enqueue(first, 0f, 2f, 0.5f, 2), "first card enters queue");
        Check(queue.Enqueue(second, 0.2f, 2f, 0.5f, 2), "second card stacks independently");
        Check(queue.Enqueue(third, 0.4f, 2f, 0.5f, 2) && queue.Count == 2, "capacity evicts oldest card FIFO");
        var snapshot = queue.Snapshot(1.9f, 2f, 0.5f, 2);
        Check(snapshot.Length == 2 && snapshot[0].Card.EnemyName == "Shooter" && snapshot[1].Card.EnemyName == "Tank", "newest surviving entries keep deterministic order");
        Check(snapshot[0].Alpha < 1f && snapshot[0].Alpha > 0f, "each card fades on its own timer");
        Check(queue.Snapshot(2.5f, 2f, 0.5f, 2).Length == 0, "expired cards leave the queue");
        Check(!queue.Enqueue(first, 3f, 2f, 0.5f, 0), "zero card capacity disables rendering");

        Check(FeedbackPolicy.CardDetail(actualMonsterName, true, true) == "10m   /   80 HP   /   Enemy_Big_Shooter", "name replaces tier on existing detail line");
        Check(!FeedbackPolicy.CardDetail(actualMonsterName, true, true).Contains('\n'), "card name does not create a third line");
        Check(FeedbackPolicy.CardDetail(actualMonsterName, false, false) == "Enemy_Big_Shooter", "name remains when optional metrics hidden");
        Check(FeedbackPolicy.DamageText(24.35f, HitKind.Normal) == "24.35", "damage decimals are not rounded to integer");
        Check(FeedbackPolicy.DamageText(6.75f, HitKind.Armor) == "[6.75]", "armor has redundant bracket styling");
        Check(FeedbackPolicy.DamageText(81.2f, HitKind.Weakspot) == "81.2!", "weakspot has redundant emphasis mark");
        Check(FeedbackPolicy.DamageText(float.NaN, HitKind.Normal) == "", "non-finite damage never enters UI");
        Check(FeedbackPolicy.DamageText(-1f, HitKind.Normal) == "", "negative damage is not a hit");
        Check(FeedbackPolicy.DamageText(0f, HitKind.Armor) == "[0]", "zero armor hit is explicit");
        Check(FeedbackPolicy.Strength(200f) > FeedbackPolicy.Strength(20f), "concentrated damage increases strength");
        Check(FeedbackPolicy.Strength(float.NaN) == 0f, "invalid burst input is neutral");
        Check(FeedbackPolicy.Strength(float.MaxValue) <= 1f, "extreme damage cannot exceed feedback ceiling");
        Check(Math.Abs(FeedbackPolicy.Travel(-100) - 12f) < 0.0001f && Math.Abs(FeedbackPolicy.Travel(100) - 18f) < 0.0001f, "default corpse launch budget spans 12-18 m");
        Check(FeedbackPolicy.Fragments(-1) == 2 && FeedbackPolicy.Fragments(100) == 6, "every supported corpse gets at least two fragment attempts, bounded at six");
        Check(FeedbackPolicy.FragmentRadius(-1) == 0.65f && FeedbackPolicy.FragmentRadius(100) == 2f, "fragment radius is bounded");
        Check(FeedbackPolicy.AppliedTravel(0.5f) >= 0.5f && FeedbackPolicy.AppliedTravel(0.5f) < 12f, "a small configured budget is no longer lifted to the old 12 m floor by the position clamp");
        Check(FeedbackPolicy.Travel(100, 2.4f) > FeedbackPolicy.Travel(100, 1f), "bigger body is launched further");
        Check(Math.Abs(FeedbackPolicy.LaunchBudget(0f, 2f, 20f) - 2f) < 0.0001f && Math.Abs(FeedbackPolicy.LaunchBudget(1f, 2f, 20f) - 20f) < 0.0001f, "launch budget keeps a small configured minimum (2 m stays 2 m) and still reaches the maximum at full burst");
        Check(FeedbackPolicy.LaunchBudget(0f, 500f, -5f) <= 30f && FeedbackPolicy.LaunchBudget(1f, 9f, 2f) >= 9f, "configured distances are clamped to 0.5-30 m and the maximum never drops below the minimum");
        Check(FeedbackPolicy.Travel(1f, 12f, 18f, 2.4f) >= 12f && FeedbackPolicy.AppliedTravel(1000f) <= 40f && FeedbackPolicy.LaunchSpeedFor(3.8f, 0.5f) > 0f, "large-body launch keeps the configured budget and stays under the absolute 40 m ceiling");
        Check(Math.Abs(FeedbackPolicy.SizeFactor(FeedbackPolicy.SizeBaselineVolume) - 1f) < 0.0001f, "baseline body volume maps to a neutral size factor");
        Check(Math.Abs(FeedbackPolicy.SizeFactor(11.2f) - 2f) < 0.0001f, "eightfold volume maps to double linear size");
        Check(FeedbackPolicy.SizeFactor(0f) == 1f && FeedbackPolicy.SizeFactor(float.NaN) == 1f && FeedbackPolicy.SizeFactor(100000f) <= 2.4f, "unmeasurable body volume falls back to neutral and stays bounded");
        Check(Math.Abs(FeedbackPolicy.GibScale(1f) - 1f) < 0.0001f && FeedbackPolicy.GibScale(2.4f) == 2.4f, "giblet scale is proportional to body size");
        Check(FeedbackPolicy.GibScale(0.1f) >= 0.7f && FeedbackPolicy.GibScale(100f) <= 2.4f, "giblet scale stays bounded");
        Check(FeedbackPolicy.BurstCount(0.7f, 0f) == 3 && FeedbackPolicy.BurstCount(2.4f, 1f) == 7, "every body gets a visible multi-point burst, scaled by body size and strength");
        Check(FeedbackPolicy.BurstCount(0f, -1f) >= 3 && FeedbackPolicy.BurstCount(100f, 100f) <= 7, "burst count remains bounded from three to seven");
        Check(FeedbackPolicy.BurstSpread(0f, 2.4f) > FeedbackPolicy.BurstSpread(0f, 1f) && FeedbackPolicy.BurstSpread(1f, 1f) > FeedbackPolicy.BurstSpread(0f, 1f), "burst points spread farther with size and kill strength");
        Check(Math.Abs(FeedbackPolicy.BurstSpread(0.2f, 1.2f) - 1.52064f) < 0.0001f, "corpse burst points use the expanded visual spread radius");
        Check(FeedbackPolicy.GibLaunchSpeed(0f) == 5.5f && FeedbackPolicy.GibLaunchSpeed(1f) == 12f, "captured fragments receive a strong bounded outward launch");
        Check(FeedbackPolicy.ShouldWaitForRagdoll(0.04f, 0.1f) && !FeedbackPolicy.ShouldWaitForRagdoll(0.1f, 0.1f) && !FeedbackPolicy.ShouldWaitForRagdoll(float.NaN, 0.1f), "the pre-burst ragdoll wait still answers the burst question");
        Check(FeedbackPolicy.RagdollLookupGrace == 0.3f && FeedbackPolicy.ShouldRetryRagdollLookup(false, 0, 0.1f, 0.1f)
            && Math.Abs(FeedbackPolicy.RagdollLookupDeadline(0.1f) - 0.4f) < 0.0001f,
            "ragdoll lookups are retried past the burst inside a 0.30 s grace window (1.2.16 real-machine fix)");
        Check(FeedbackPolicy.CorpseBurstDelay == 0.1f
            && FeedbackPolicy.Clamp(0f, FeedbackPolicy.MinCorpseBurstDelay, FeedbackPolicy.MaxCorpseBurstDelay, FeedbackPolicy.CorpseBurstDelay) == 0.1f
            && FeedbackPolicy.Clamp(2f, FeedbackPolicy.MinCorpseBurstDelay, FeedbackPolicy.MaxCorpseBurstDelay, FeedbackPolicy.CorpseBurstDelay) == 1.5f,
            "corpse burst defaults to 0.1 s and clamps to the DBO-aligned 0.1-1.5 s range");
        Check(!FeedbackPolicy.ShouldUseCapturedBurstOrigin(8f * 8f, 9f * 9f)
            && FeedbackPolicy.ShouldUseCapturedBurstOrigin(10000f * 10000f, 8f * 8f)
            && FeedbackPolicy.ShouldUseCapturedBurstOrigin(0f, 10000f * 10000f)
            && FeedbackPolicy.ShouldUseCapturedBurstOrigin(float.NaN, 0f), "off-scene DBO root displacement keeps the delayed burst at its captured origin even when the ragdoll was launch-clamped");
        Check(!FeedbackPolicy.ShouldBreakCorpseLimbs(true, true) && FeedbackPolicy.ShouldBreakCorpseLimbs(false, true), "off-scene corpse cleanup prevents later limb breaks from spawning new fragments");
        float shortCardWidth = FeedbackPolicy.CardWidth(46f, 60f, 1f, 430f);
        float longCardWidth = FeedbackPolicy.CardWidth(46f, 130f, 1f, 430f);
        Check(shortCardWidth == 136f && longCardWidth == 206f, "transparent card width follows its measured visible content, not an invisible fixed panel");
        Check(Math.Abs(FeedbackPolicy.CenteredCardX(1920f, shortCardWidth, 0f) + shortCardWidth * 0.5f - 960f) < 0.001f && Math.Abs(FeedbackPolicy.CenteredCardX(1920f, longCardWidth, 0f) + longCardWidth * 0.5f - 960f) < 0.001f, "short and long cards both center their actual content on screen");
        Check(Math.Abs(FeedbackPolicy.CenteredCardX(1920f, shortCardWidth, 24f) - FeedbackPolicy.CenteredCardX(1920f, shortCardWidth, 0f) - 24f) < 0.001f, "configured horizontal offset remains relative to the screen center");
        Check(Math.Abs(FeedbackPolicy.PanelAlpha(0.42f) - 0.42f) < 0.0001f && FeedbackPolicy.PanelAlpha(5f) <= 0.9f && FeedbackPolicy.PanelAlpha(-3f) >= 0f, "card panel opacity is clamped and defaults transparent enough to see through");
        Check(FeedbackPolicy.PanelAlpha(float.NaN) == FeedbackPolicy.DefaultPanelOpacity, "invalid panel opacity falls back to the see-through default");
        Check(FeedbackPolicy.RuleAlpha(0.42f) < FeedbackPolicy.RuleAlpha(0.9f) && FeedbackPolicy.RuleAlpha(0f) >= 0.05f, "accent rule stays subtle and tracks the panel");
        Check(FeedbackPolicy.DefaultPanelOpacity == 0f && FeedbackPolicy.PanelAlpha(FeedbackPolicy.DefaultPanelOpacity) == 0f, "kill cards default to a fully see-through panel");
        Check(FeedbackPolicy.NumberScale(0.07f, HitKind.Weakspot) > FeedbackPolicy.NumberScale(0.07f, HitKind.Normal), "weakspot pop is stronger");
        Check(FeedbackPolicy.NumberScale(0.07f, HitKind.Armor) < FeedbackPolicy.NumberScale(0.07f, HitKind.Normal), "armor has reduced scale");
        Check(FeedbackPolicy.Alpha(1f, 0.72f) == 0, "expired numbers fade to zero");
        Check(FeedbackPolicy.CorpseBurstDelay < FeedbackPolicy.CorpseCleanupDelay && FeedbackPolicy.CorpseCleanupDelay <= 2.4f, "corpse stages finish promptly");
        var soundThrottle = new CorpseSoundThrottle();
        Check(soundThrottle.Request(0f), "first corpse sound posts immediately");
        bool burstCoalesced = true;
        for (int i = 0; i < 100; i++) burstCoalesced &= !soundThrottle.Request(0f);
        Check(burstCoalesced && soundThrottle.HasPending, "same-frame sound burst collapses to one bounded pending request");
        Check(!soundThrottle.TryFlush(0.119f) && soundThrottle.HasPending, "coalesced sound waits until the 120 ms spacing boundary");
        Check(soundThrottle.TryFlush(0.12f) && !soundThrottle.HasPending, "one pending sound flushes at exactly 120 ms");
        Check(!soundThrottle.TryFlush(0.12f), "a same-frame burst cannot flush twice");
        Check(!soundThrottle.Request(0.239f) && soundThrottle.HasPending, "a new request inside cooldown coalesces");
        Check(!soundThrottle.Request(0.24f) && soundThrottle.HasPending, "cooldown-boundary arrival preserves the queued sound");
        Check(soundThrottle.TryFlush(0.24f) && !soundThrottle.HasPending, "queued sound flushes once at the cooldown boundary");
        Check(!soundThrottle.Request(0.24f) && soundThrottle.HasPending, "new same-frame sound queues behind the flushed sound");
        Check(!soundThrottle.TryFlush(0.359f) && soundThrottle.HasPending, "queued follow-up remains blocked before its own interval");
        Check(soundThrottle.TryFlush(0.36f) && !soundThrottle.HasPending, "queued follow-up flushes after a full second interval");

        var burst = new BurstHistory();
        burst.Add(1, 0f, 20); burst.Add(1, 0.2f, 30); burst.Add(2, 0.2f, 999);
        Check(burst.Sum(1, 0.3f) == 50, "burst sums one enemy only");
        Check(burst.Sum(1, 0.7f) == 30, "burst window expires older damage");
        Check(burst.Sum(1, 1f) == 0, "burst fully expires");
        burst.Add(1, 2f, float.NaN); Check(burst.Sum(1, 2f) == 0, "non-finite burst sample ignored");
        var rules = new[] {
            new EnemyNameRule { RawName = "MOD_Shooter", DisplayName = "自定义射手" },
            new EnemyNameRule { DataBlockId = 13, RawName = "MOD_Shooter", DisplayName = "特定图射手" }
        };
        Check(EnemyNamePolicy.Resolve(13, "MOD_Shooter", rules) == "特定图射手", "ID plus raw name beats general raw override");
        Check(EnemyNamePolicy.Resolve(99, "MOD_Shooter", rules) == "自定义射手", "raw-name override works with modded IDs");
        Check(EnemyNamePolicy.Resolve(13, "NEW_MOD_ENEMY", rules) == "NEW_MOD_ENEMY", "ID reuse never aliases a different raw name");
        Check(EnemyNamePolicy.Resolve(8000, "CustomBoss", rules) == "CustomBoss", "unmapped mod enemies keep actual names");
        Check(!EnemyNamePolicy.Valid(new EnemyNameRule { RawName="x", DisplayName="a\nb" }), "multiline names rejected");
        ledger.Forget(0x1234); Check(ledger.TryCredit(0x1234), "pooled enemy pointers can be credited again after spawn");

        var defaults = new System.Collections.Generic.Dictionary<uint, string> { [11]="菜花", [13]="小白", [16]="大壮" };
        Check(EnemyNamePolicy.Resolve(13, "Shooter", null, defaults) == "小白", "Hikaria default fills known enemy ID");
        Check(EnemyNamePolicy.Resolve(11, "Striker", null, defaults) == "菜花", "Hikaria terminology retained verbatim");
        Check(EnemyNamePolicy.Resolve(13, "MOD_Shooter", rules, defaults) == "特定图射手", "MOD custom mapping overrides Hikaria default ID");
        Check(EnemyNamePolicy.Resolve(13, "MOD_Shooter", rules, null) == "特定图射手", "custom mappings still work with defaults disabled");
        Check(EnemyNamePolicy.Resolve(9000, "MOD_Boss", null, defaults) == "MOD_Boss", "unknown IDs keep MOD actual name with defaults enabled");
        Check(EnemyNamePolicy.Resolve(13, "Shooter", null, null) == "Shooter", "disabled default map restores raw name");

        Check(default(HitKind) == HitKind.Normal, "missing limb evidence defaults to normal, not armor");
        Check(FeedbackPolicy.IsLeaseCurrent(3, 3), "current visual lease is actionable");
        Check(!FeedbackPolicy.IsLeaseCurrent(3, 4), "disabled or reused visual lease rejects old timer");
        Check(!FeedbackPolicy.IsLeaseCurrent(0, 0), "uncaptured lease is never actionable");
        var keepRaw = new[] { new EnemyNameRule { DataBlockId=13, RawName="Shooter", DisplayName="Shooter" } };
        Check(EnemyNamePolicy.Resolve(13, "Shooter", keepRaw, defaults) == "Shooter", "explicit raw-name override suppresses default nickname");

        // Focused-enemy health bar.
        Check(Near(BarPolicy.FillFraction(400f, 400f), 1f), "health bar fills completely at full health");
        Check(Near(BarPolicy.FillFraction(100f, 400f), 0.25f), "health bar fills a quarter at quarter health");
        Check(Near(BarPolicy.FillFraction(0f, 400f), 0f), "health bar fills nothing at zero health");
        Check(Near(BarPolicy.FillFraction(500f, 400f), 1f), "overheal clamps to a full bar");
        Check(Near(BarPolicy.FillFraction(100f, 0f), 0f), "zero maximum never renders a misleading bar");
        Check(Near(BarPolicy.FillFraction(float.NaN, 400f), 0f), "unreadable health fills nothing");
        Check(BarPolicy.DisplayValue(1234.6f) == 1235, "bar readout rounds up past the midpoint");
        Check(BarPolicy.DisplayValue(1.5f) == 2, "bar readout rounds halves away from zero");
        Check(BarPolicy.DisplayValue(-3f) == 0, "bar readout never shows negative health");
        Check(BarPolicy.DisplayValue(1e9f) == 9999999, "bar readout caps absurd values");
        Check(BarPolicy.Readout(1234f, 4000f) == "1234 / 4000", "bar readout is remaining over maximum");
        Check(BarPolicy.Label("小白", 100f, 400f, true, true) == "小白   100 / 400", "bar label combines name and health");
        Check(BarPolicy.Label("小白", 100f, 400f, false, true) == "100 / 400", "bar label can hide the name");
        Check(BarPolicy.Label("小白", 100f, 400f, true, false) == "小白", "bar label can hide the numbers");
        Check(BarPolicy.Label("小白", 100f, 400f, false, false) == string.Empty, "bar label is empty when both parts are hidden");
        Check(BarPolicy.Label(null, 0f, 0f, true, true) == "Unknown   0 / 0", "a missing name still prints a valid bar label");
        Check(BarPolicy.TrimName(new string('甲', 40)).Length == BarPolicy.MaxLabelNameChars, "an overlong bar name is trimmed to the configured character cap with an ellipsis");
        Check(Near(BarPolicy.ResolutionScale(1080), 1f), "bar scale is unit at 1080p");
        Check(Near(BarPolicy.ResolutionScale(2160), 1.6f), "bar scale clamps at 4K");
        Check(Near(BarPolicy.Width(10f, 1080, 1920), BarPolicy.MinWidth), "bar width below the floor is raised");
        Check(Near(BarPolicy.Width(400f, 1080, 300), 270f), "bar width is limited by a narrow screen");
        Check(Near(BarPolicy.Height(99f, 1080), BarPolicy.MaxHeight), "bar thickness is clamped down");
        Check(Near(BarPolicy.Opacity(0f), BarPolicy.MinOpacity), "bar opacity below the floor stays visible");
        Check(Near(BarPolicy.MaxDistance(1f), BarPolicy.MinRange), "bar range below the floor is raised");
        Check(Near(BarPolicy.HeadOffset(99f), BarPolicy.MaxHeadOffsetMetres), "bar head offset is clamped");
        Check(Near(BarPolicy.AnchorY(1000f, 1080, 0.10f, 0.46f), 496.8f), "bar stays inside the upper band");
        Check(Near(BarPolicy.AnchorX(5f, 200f, 1920, 1080), 112f), "bar is kept on screen horizontally");
        Check(Near(BarPolicy.GhostDecay(1f, 0.4f, 0.1f), 0.74f), "bar ghost decays toward the live fraction");
        Check(Near(BarPolicy.GhostDecay(0.42f, 0.4f, 1f), 0.4f), "bar ghost never falls below the live fraction");
        Check(Near(BarPolicy.AlphaFor(true, 0.05f, 0f), 0.5f), "bar fades in linearly");
        Check(Near(BarPolicy.AlphaFor(false, 0f, BarPolicy.HoldSeconds), 1f), "a just-lost target is held at full opacity");
        Check(Near(BarPolicy.AlphaFor(false, 0f, BarPolicy.DroppedLostSeconds), 0f), "a dropped target is fully faded");
        Check(BarPolicy.ShouldDraw(false, false, 1f) == false, "no target and no anchor draws nothing");
        Check(BarPolicy.ShouldDraw(true, false, 1f), "a live target draws before its first projection");
        Check(BarPolicy.ShouldDraw(false, true, 0.5f), "a lost target keeps fading from its cached anchor");
        Check(BarPolicy.ShouldDraw(false, true, 0f) == false, "a finished fade draws nothing");
        Check(Near(BarPolicy.TickFraction(2, 4), 0.5f), "bar tick sits at the midpoint");
        Check(BarPolicy.TickFraction(0, 4) < 0f, "no bar tick is drawn on the left edge");
        Check(!BarPolicy.ShouldShow(true, 61f, 60f), "a target beyond the bar range shows nothing");
        Check(BarPolicy.ShouldShow(true, 60f, 60f), "a target at the bar range limit still shows");
        Check(BarPolicy.IsHeadMode(null) && !BarPolicy.IsHeadMode("screen"), "bar anchoring modes resolve");

        Check(BarPolicy.WeakspotSuffix("头", true) == "[头]", "weakspot part formats after the enemy name");
        Check(BarPolicy.WeakspotSuffix("头", false) == string.Empty, "weakspot part visibility can be disabled");
        Check(BarPolicy.WeakspotSuffix(string.Empty, true) == string.Empty, "missing weakspot evidence hides the suffix");
        Check(BarPolicy.PartLabel("Enemy_Tentacle_Head", "zh") == "头", "head weakspot gets the anatomical head label");
        Check(BarPolicy.PartLabel("Enemy_Weakspot_Back", "zh") == "背部", "named back weakspot maps to back");
        Check(BarPolicy.PartLabel("Enemy_Spore_Sac", "zh") == "孢子", "spore weakspot has a concise localized label");
        Check(BarPolicy.PartLabel("unknown limb", "zh") == "弱点" && BarPolicy.PartLabel("unknown limb", "en") == "Weakspot", "unrecognized weakspot name uses an honest localized fallback");
        Check(BarPolicy.WeakspotMultiplierTag("孢子", 4f, "zh") == "[孢子4×]"
            && BarPolicy.WeakspotMultiplierTag("头", 2f, "zh") == "[头2×]"
            && BarPolicy.WeakspotMultiplierTag("Head", 2.5f, "en") == "[Head 2.5×]", "weakspot tags show localized parts and compact multipliers");
        Check(BarPolicy.WeakspotMultiplierTag("头", float.NaN, "zh") == "[头?×]"
            && BarPolicy.WeakspotMultiplierTag(string.Empty, 2f, "zh") == string.Empty, "unknown multipliers stay explicit and missing part names are omitted");
        var weakspotTags = BarPolicy.WeakspotTags(new[] {
            new WeakspotPartMultiplier("孢子", 4f), new WeakspotPartMultiplier("头", 2f),
            new WeakspotPartMultiplier("头", 2f)
        }, "zh");
        Check(weakspotTags == "[孢子4×][头2×]", "multiple weakspot limbs render compact tags and exact duplicates collapse");
        var threeWeakspotTags = BarPolicy.WeakspotTags(new[] {
            new WeakspotPartMultiplier("A", 1f), new WeakspotPartMultiplier("B", 2f),
            new WeakspotPartMultiplier("C", 3f)
        }, "en");
        Check(threeWeakspotTags == "[A 1×][B 2×][C 3×]", "three weakspot parts show without an overflow marker");
        var fourWeakspotTags = BarPolicy.WeakspotTags(new[] {
            new WeakspotPartMultiplier("A", 1f), new WeakspotPartMultiplier("B", 2f),
            new WeakspotPartMultiplier("C", 3f), new WeakspotPartMultiplier("D", 4f)
        }, "en");
        Check(fourWeakspotTags == "[A 1×][B 2×][C 3×][+1]", "four weakspot parts show three labels and count the fourth");
        var manyWeakspotTags = BarPolicy.WeakspotTags(new[] {
            new WeakspotPartMultiplier("A", 1f), new WeakspotPartMultiplier("B", 2f),
            new WeakspotPartMultiplier("C", 3f), new WeakspotPartMultiplier("D", 4f),
            new WeakspotPartMultiplier("E", 5f)
        }, "en");
        Check(manyWeakspotTags == "[A 1×][B 2×][C 3×][+2]", "weakspot tag cap keeps the header bounded and counts omitted parts");
        Check(BarPolicy.CfoamTag(4f) == "[结沫*2]" && BarPolicy.CfoamTag(3f) == "[结沫*1]"
            && BarPolicy.CfoamTag(2f) == "[结沫*1]", "C-Foam tag rounds glue tolerance up by three units per blob");
        Check(BarPolicy.CfoamTag(0f) == "[结沫*0]" && BarPolicy.CfoamTag(1000000f) == "[结沫*免疫]",
            "zero tolerance and the immune sentinel have explicit C-Foam labels");
        Check(BarPolicy.CfoamTag(float.NaN) == string.Empty && BarPolicy.CfoamTag(-1f) == string.Empty,
            "invalid glue tolerance never fabricates a blob count");
        Check(BarPolicy.SelectEnemyTag(true, false, "[头2×]", "[结沫*2]") == "[结沫*2]"
            && BarPolicy.SelectEnemyTag(false, true, "[头2×]", "[结沫*2]") == "[头2×]"
            && BarPolicy.SelectEnemyTag(false, false, "[头2×]", "[结沫*2]") == string.Empty,
            "held GlueGun replaces weakspot tags while normal weapon behavior obeys ShowWeakspotPart");
        Check(Near(BarPolicy.CfoamTagRed, 242f / 255f) && Near(BarPolicy.CfoamTagGreen, 166f / 255f)
            && Near(BarPolicy.CfoamTagBlue, 90f / 255f), "the complete C-Foam tag uses the restrained warm amber #F2A65A");
        Check(BarPolicy.TryParseHexColor("#F2A65A", out float foamRed, out float foamGreen, out float foamBlue, out float foamAlpha)
            && Near(foamRed, BarPolicy.CfoamTagRed) && Near(foamGreen, BarPolicy.CfoamTagGreen)
            && Near(foamBlue, BarPolicy.CfoamTagBlue) && Near(foamAlpha, 1f),
            "the C-Foam amber equals its published hex and stays fully opaque, so only the tag hue changes");
        Check(BarPolicy.TryParseHexColor("#E84B4B", out float red, out float green, out float blue, out float alpha)
            && Near(red, 232f / 255f) && Near(green, 75f / 255f) && Near(blue, 75f / 255f) && Near(alpha, 1f), "six-digit config colors parse as opaque RGB");
        Check(BarPolicy.TryParseHexColor("#10203080", out _, out _, out _, out float alpha8) && Near(alpha8, 128f / 255f), "eight-digit config colors parse alpha");
        Check(!BarPolicy.TryParseHexColor("not-a-color", out _, out _, out _, out _), "invalid config colors are rejected for fallback");
        Check(Near(BarPolicy.HeaderWidth(100f, 70f, 30f, 60f, 4f, 6f, 300f), 180f), "compact title row reserves measured text widths and spacing");
        Check(Near(BarPolicy.HeaderWidth(100f, 170f, 30f, 60f, 4f, 6f, 220f), 220f), "title row width is bounded on narrow screens");
        Check(BarPolicy.HealthColorBand(0.6601f, 66f, 33f) == BarPolicy.HealthBand.Base, "health above 66 percent keeps the configured base fill color");
        Check(BarPolicy.HealthColorBand(0.66f, 66f, 33f) == BarPolicy.HealthBand.Yellow, "exactly 66 percent switches to yellow");
        Check(BarPolicy.HealthColorBand(0.3301f, 66f, 33f) == BarPolicy.HealthBand.Yellow, "health between 33 and 66 percent stays yellow");
        Check(BarPolicy.HealthColorBand(0.33f, 66f, 33f) == BarPolicy.HealthBand.Red, "exactly 33 percent switches to red");
        Check(BarPolicy.HealthColorBand(0.1f, 66f, 33f) == BarPolicy.HealthBand.Red, "health below 33 percent stays red");
        Check(BarPolicy.HealthColorBand(float.NaN, 66f, 33f) == BarPolicy.HealthBand.Base, "unknown health keeps the base fill color");
        Check(BarPolicy.HealthColorBand(0.55f, 60f, 25f) == BarPolicy.HealthBand.Yellow, "custom health thresholds apply immediately");
        Check(BarPolicy.HealthColorBand(0.5f, 30f, 80f) == BarPolicy.HealthBand.Base, "red threshold is bounded by yellow when config values are reversed");
        Check(BarPolicy.HealthColorBand(0.25f, 20f, float.NaN) == BarPolicy.HealthBand.Base, "invalid red threshold fallback remains capped by a custom yellow threshold");

        var reloadGate = new ConfigHotReloadGate();
        var stamp0 = new DateTime(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);
        var stamp1 = stamp0.AddSeconds(1);
        reloadGate.Seed(stamp0, 100);
        Check(!reloadGate.ShouldReload(stamp0, 100, 0f), "unchanged config file does not reload");
        Check(!reloadGate.ShouldReload(stamp1, 150, 1f), "first config write observation starts debounce");
        Check(!reloadGate.ShouldReload(stamp1, 150, 1.39f), "config is not reloaded before write stability window");
        Check(reloadGate.ShouldReload(stamp1, 150, 1.41f), "stable config write becomes reloadable");
        reloadGate.MarkApplied(stamp1, 150);
        Check(!reloadGate.ShouldReload(stamp1, 150, 1.5f), "successful config reload is not repeated");
        Check(!reloadGate.ShouldReload(stamp1, 160, 2f), "a subsequent config edit starts a fresh debounce");
        Check(reloadGate.ShouldReload(stamp1, 160, 2.41f), "a subsequent stable config edit reloads");
        reloadGate.MarkFailed(2.41f);
        Check(!reloadGate.ShouldReload(stamp1, 160, 2.9f), "failed config reload uses bounded retry delay");
        Check(reloadGate.ShouldReload(stamp1, 160, 3.42f), "failed config reload can retry after backoff");

        GlueGunWieldTests.Run(Check);
        DiagnosticBudgetTests.Run(Check);
        NameFileTests.Run(Check);
        TeammateMarkTests.Run(Check);
        EmpBridgeTests.Run(Check);
        IconPipelineTests.Run(Check);
        HotReloadTests.Run(Check);
        CardPresentationTests.Run(Check);
        CorpseFadeTests.Run(Check);
        KillCounterTests.Run(Check);
        DamageNumberOpacityTests.Run(Check);
        HitSoundPolicyTests.Run(Check);
        HitSoundAssetTests.Run(Check);
        HitMarkerAssetTests.Run(Check);
        HitMarkerScopeTests.Run(Check);
        KillIconTests.Run(Check);
        KillIconFreezeTests.Run(Check);
        KillCardExpiryTests.Run(Check);
        CorpseLaunchTests.Run(Check);
        KillTierTests.Run(Check);
        DamageFxTests.Run(Check);
        // Prismatic incoming numbers (1.2.16): measured against the frozen 1.2.15 numbers.
        PrismaticIncomingTests.Run(Check);
        // The three damage-number switches (ApproachEffect / PrismaticEnabled / TiersEnabled), 1.2.16.
        DamageNumberApproachTests.Run(Check);
        // Pellet volley (1.2.19): one shotgun shot is one number even when its pellets settle across frames.
        ShotgunVolleyTests.Run(Check);
        // Pellet routing (1.2.19 fix D1/D2/D4/D5): the real ShotgunVolleyState.Capture decision - which window, which
        // refusal, which bounded receipt - plus the two-frame silence guard.
        ShotgunVolleyRoutingTests.Run(Check);
        // Corpse launch heading (1.2.19 fix): the near-vertical fallback is the camera yaw, not world +Z.
        CorpseHeadingTests.Run(Check);
        FocusOutlineTests.Run(Check);
        HitMarkerTests.Run(Check);
        ReviewFixTests.Run(Check);
        ChannelSwitchTests.Run(Check);
        // Pellet path proof (1.2.19): only a damage-root Bullet may ever join the volley; melee/explosion/DoT/push
        // keep their own immediate number.
        DamageOriginTests.Run(Check);
        Console.WriteLine("PASS: " + _assertions + " kill-feed policy assertions");
    }

    private static void Check(bool condition, string name)
    {
        _assertions++;
        if (!condition) throw new InvalidOperationException("FAIL: " + name);
        Console.WriteLine("PASS " + name);
    }

    private static bool Near(float a, float b, float tolerance = 0.0005f) => Math.Abs(a - b) <= tolerance;
}
