using System;
using HawChow.GTFOKillFeed;

// EnemyBar used to spend one shared `_warnings` counter on both the LogFocus debug lines (budget 16) and real
// [BAR] tick/draw exception warnings (budget 5), so five focus transitions permanently muted every exception.
// These assertions pin the separated budgets the production bar now owns.
internal static class DiagnosticBudgetTests
{
    internal static void Run(Action<bool, string> check)
    {
        // 1. The legacy shared-counter shape loses the exception budget to focus logging.
        int sharedWarnings = 0;
        int warningsLogged = 0;
        for (int focusLines = 0; focusLines < 16; focusLines++)
            if (sharedWarnings++ < 16) { }
        for (int exceptions = 0; exceptions < 5; exceptions++)
            if (sharedWarnings++ < 5) warningsLogged++;
        check(warningsLogged == 0,
            "legacy shared counter: sixteen focus lines consume the five-warning exception budget");

        // 2. The shipped budgets are independent: focus logging cannot silence warnings.
        var warningBudget = new DiagnosticBudget();
        var focusChangeBudget = new DiagnosticBudget();
        var focusAcquiredBudget = new DiagnosticBudget();
        int warningsAfterFocus = 0;
        for (int focusLines = 0; focusLines < 16; focusLines++) focusChangeBudget.TryUse(16);
        for (int focusLines = 0; focusLines < 12; focusLines++) focusAcquiredBudget.TryUse(12);
        for (int exceptions = 0; exceptions < 5; exceptions++)
            if (warningBudget.TryUse(5)) warningsAfterFocus++;
        check(warningsAfterFocus == 5,
            "focus logging no longer consumes the exception-warning budget");

        // 3. Warning exhaustion cannot silence the focus diagnostics either.
        check(!warningBudget.TryUse(5), "the sixth warning is suppressed");
        check(focusChangeBudget.TryUse(16) == false && focusChangeBudget.Used == 17,
            "the focus-change budget is exhausted by its own lines only");
        check(focusAcquiredBudget.Used == 12 && !focusAcquiredBudget.TryUse(12) && focusAcquiredBudget.Used == 13,
            "focus=acquired keeps its own twelve-line budget");

        // 4. Fresh budgets accept exactly `limit` lines and count denied attempts like the old post-increment.
        var fresh = new DiagnosticBudget();
        int accepted = 0;
        for (int i = 0; i < 8; i++) if (fresh.TryUse(5)) accepted++;
        check(accepted == 5 && fresh.Used == 8, "a budget accepts exactly its limit and counts denied attempts");
        check(!fresh.TryUse(0) && !fresh.TryUse(-3) && fresh.Used == 8, "non-positive limits never log and never consume the budget");

        var repeated = new RepeatedLogBudget();
        int gibLines = 0, destructionLines = 0;
        for (int i = 1; i <= 4520; i++)
        {
            if (repeated.TryLog($"gibdata=resolved;enemyId={i};entries=0")) gibLines++;
            if (repeated.TryLog($"destruction=local;limb={i};gibs=0")) destructionLines++;
        }
        check(gibLines == 48 && destructionLines == 48 && repeated.Seen == 9040
            && repeated.Printed == 96 && repeated.Suppressed == 8944,
            "corpse log budget: two 4520-event streams retain their first three and every hundredth independently");

        var reasons = new RepeatedLogBudget();
        for (int i = 0; i < 3; i++) reasons.TryLog("launch=unavailable;reason=no-ragdoll-instance;scheduleId=1");
        check(!reasons.TryLog("launch=unavailable;reason=no-ragdoll-instance;scheduleId=2")
            && reasons.TryLog("launch=unavailable;reason=ragdoll-inactive;scheduleId=3"),
            "corpse log budget: a frequent failure cannot suppress the first receipt for a different reason");
        var errors = new RepeatedLogBudget();
        for (int i = 0; i < 3; i++) errors.TryLog("tick=failed;scheduleId=1;error=NullReferenceException");
        check(!errors.TryLog("tick=failed;scheduleId=2;error=NullReferenceException")
            && errors.TryLog("tick=failed;scheduleId=3;error=MissingReferenceException"),
            "corpse log budget: a new exception type retains its own first receipt after another type is suppressed");
        var ragdolls = new RepeatedLogBudget();
        for (int i = 1; i <= 3; i++) ragdolls.TryLog($"ragdoll=late@attempts={i};scheduleId={i}");
        check(!ragdolls.TryLog("ragdoll=late@attempts=17;scheduleId=4"),
            "corpse log budget: varying ragdoll attempt numbers share one event allowance");
        ragdolls.Reset();
        check(ragdolls.Seen == 0 && ragdolls.Printed == 0 && ragdolls.Suppressed == 0
            && ragdolls.TryLog("ragdoll=late@attempts=17;scheduleId=5"),
            "corpse log budget: a level reset restores samples and clears summary counters");

        var concurrent = new RepeatedLogBudget();
        System.Threading.Tasks.Parallel.For(0, 1000, i => concurrent.TryLog("classification=NORMAL"));
        check(concurrent.Seen == 1000 && concurrent.Printed == 13 && concurrent.Suppressed == 987,
            "combat log budget: concurrent callbacks retain exact counters and a bounded sample");

        check(CorpseAudioCachePolicy.ShouldAttempt(false, false)
            && !CorpseAudioCachePolicy.ShouldAttempt(true, false)
            && CorpseAudioCachePolicy.ShouldAttempt(true, true),
            "corpse audio cache: initial load and destroyed-clip recovery retry; a failed load stays bounded");
    }
}
