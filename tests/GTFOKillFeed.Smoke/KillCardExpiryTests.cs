using System;
using HawChow.GTFOKillFeed;

// Review I1 + I7: a kill is shown by its head icon OR by its card, never by neither. The card is suppressed
// while the icon presents it, so when the icon is evicted early (max icons) or cannot draw (missing raster)
// the card's life has to cover that window. These assertions drive the production helpers
// (KillCardPolicy.ExpiresAt/Alpha and KillIconPolicy.ShouldDrawCard) and cover the full decision table the
// review called out: display x token(<0|0|>0) x now(before|after the icon expiry) x icon present/absent.
internal static class KillCardExpiryTests
{
    internal static void Run(Action<bool, string> check)
    {
        // ---- the card's own life, extended by the icon's expiry + fade -------------------------------
        check(Math.Abs(KillCardPolicy.ExpiresAt(10f, 0, 0f, 2.6f, 0.45f) - 12.6f) < 0.0001f,
            "card life: a card without a head icon expires on its own lifetime");
        check(Math.Abs(KillCardPolicy.ExpiresAt(10f, 7, 30f, 2.6f, 0.45f) - 30.45f) < 0.0001f,
            "card life: a card that owns a head icon lives until that icon's expiry plus its fade (an early eviction can never leave the kill invisible)");
        check(Math.Abs(KillCardPolicy.ExpiresAt(10f, 7, 11f, 2.6f, 0.45f) - 12.6f) < 0.0001f,
            "card life: a short icon lifetime never SHORTENS the card (the later of the two wins)");
        check(Math.Abs(KillCardPolicy.ExpiresAt(10f, -1, 99f, 2.6f, 0.45f) - 12.6f) < 0.0001f,
            "card life: an F9 preview card (token < 0) keeps the plain card life, it never claims an icon window");
        check(Math.Abs(KillCardPolicy.ExpiresAt(10f, 7, float.NaN, 2.6f, 0.45f) - 12.6f) < 0.0001f
            && Math.Abs(KillCardPolicy.ExpiresAt(10f, 0, 0f, float.NaN, 0.45f) - 12.6f) < 0.0001f,
            "card life: a broken icon expiry or lifetime falls back to the shipped 2.6 s card life");

        // ---- alpha follows the same (possibly extended) expiry --------------------------------------
        check(Math.Abs(KillCardPolicy.Alpha(5f, 10f, 0.5f, 2.6f) - 1f) < 0.0001f,
            "card alpha: opaque until the last fade seconds");
        check(Math.Abs(KillCardPolicy.Alpha(9.75f, 10f, 0.5f, 2.6f) - 0.5f) < 0.0001f,
            "card alpha: linear fade over the configured seconds");
        check(KillCardPolicy.Alpha(10f, 10f, 0.5f, 2.6f) == 0f && KillCardPolicy.Alpha(11f, 10f, 0.5f, 2.6f) == 0f,
            "card alpha: gone at and after the expiry");
        check(KillCardPolicy.Alpha(float.NaN, 10f, 0.5f, 2.6f) == 0f && KillCardPolicy.Alpha(5f, float.NaN, 0.5f, 2.6f) == 0f,
            "card alpha: a broken clock or expiry never draws");

        // ---- the queue itself keeps an icon-owning card alive past its own lifetime ------------------
        var queue = new KillCardQueue();
        var owned = new KillCard(KillKind.Normal, KillTier.Minor, "x", 1f, 10f, 0f, 999f, 0u, KillFlight.None, 7, 5f);
        queue.Enqueue(owned, 0f, 1f, 0.5f, 3);
        check(queue.Snapshot(0.5f, 1f, 0.5f, 3).Length == 1 && queue.Count == 1,
            "card queue: the card is still queued while its icon window is open");
        check(queue.Snapshot(2f, 1f, 0.5f, 3).Length == 1 && queue.Count == 1,
            "card queue: past its own lifetime the card survives because the head icon still owns the kill (review I1)");
        check(queue.Snapshot(5.5f, 1f, 0.5f, 3).Length == 0 && queue.Count == 0,
            "card queue: it is dropped once the icon's expiry plus fade has passed");
        var plain = new KillCard(KillKind.Normal, KillTier.Minor, "x", 1f, 10f, 0f);
        queue.Enqueue(plain, 0f, 1f, 0.5f, 3);
        check(queue.Snapshot(1.01f, 1f, 0.5f, 3).Length == 0,
            "card queue: a card without an icon keeps the plain lifetime (no accidental extension)");

        // ---- I7: the complete "icon or card" table --------------------------------------------------
        // Expected values are the documented rule: preview tokens follow the Display switch, Card mode always
        // paints, a kill with no icon slot always paints, and in Head mode the card is suppressed only while
        // the icon is both inside its window AND actually present.
        int covered = 0;
        bool tableHolds = true;
        foreach (KillIconDisplay display in new[] { KillIconDisplay.Head, KillIconDisplay.Card })
            foreach (int token in new[] { -1, 0, 7 })
                foreach (bool after in new[] { false, true })
                    foreach (bool present in new[] { false, true })
                    {
                        const float expiresAt = 12f;
                        float now = after ? 12.01f : 11f;
                        bool expected;
                        if (token < 0) expected = display == KillIconDisplay.Card;
                        else if (display == KillIconDisplay.Card) expected = true;
                        else if (token == 0) expected = true;
                        else expected = after ? false : !present;
                        bool actual = KillIconPolicy.ShouldDrawCard(display, token, expiresAt, now, present);
                        tableHolds &= actual == expected;
                        covered++;
                    }
        check(tableHolds && covered == 24,
            "card/icon table (I7): 24/24 combinations of display x token x before/after x present follow the documented rule");

        // Mutual exclusion: the same kill never paints both. Inside the icon's window at least one of the two is
        // visible; once that window has passed the kill's presentation is over (the icon showed it first).
        bool mutuallyExclusive = true, visibleInsideWindow = true;
        foreach (int token in new[] { 0, 7 })
            foreach (bool present in new[] { false, true })
                foreach (bool after in new[] { false, true })
                {
                    float now = after ? 12.01f : 11f;
                    bool card = KillIconPolicy.ShouldDrawCard(KillIconDisplay.Head, token, 12f, now, present);
                    bool icon = token > 0 && !after && present;
                    mutuallyExclusive &= !(card && icon);
                    if (!after) visibleInsideWindow &= card || icon;
                }
        check(mutuallyExclusive, "card/icon exclusivity: a kill never paints both effects at once");
        check(visibleInsideWindow, "card/icon coverage: while the icon window is open the kill always has at least one presentation");
    }
}
