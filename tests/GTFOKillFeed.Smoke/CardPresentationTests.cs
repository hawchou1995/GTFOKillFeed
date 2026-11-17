using System;
using HawChow.GTFOKillFeed;

// R5 coverage: a card that is already on screen must follow the *current* name mapping and distance
// ceiling, while the recorded kill (kind, tier, timestamp, captured distance/health) never changes.
internal static class CardPresentationTests
{
    internal static void Run(Action<bool, string> check)
    {
        var card = new KillCard(KillKind.Weakspot, KillTier.Elite, "MOD_Shooter", 100f, 120f, 12.5f, 999f, 13);
        check(card.EnemyName == "MOD_Shooter" && card.EnemyDataId == 13 && card.RawName == "MOD_Shooter",
            "card: the queue keeps the raw name and enemy id as stable inputs");
        check(card.DistanceText == "100m", "card: the captured distance stays readable before any refresh");

        string current = "小白";
        Func<uint, string, string> resolver = (id, raw) => id == 13 ? current : raw;

        KillCard shown = CardPresentation.Resolve(card, resolver, 999f);
        check(shown.EnemyName == "小白" && shown.DistanceText == "100m",
            "card: drawing resolves the display name from the current mapping");

        // The user edits the mapping (or the name JSON reloads) while the card is visible.
        current = "实验体";
        KillCard renamed = CardPresentation.Resolve(card, resolver, 999f);
        check(renamed.EnemyName == "实验体" && card.EnemyName == "MOD_Shooter",
            "card: a mapping change applies to the already-visible card, the queued card is untouched");

        // The user lowers Layout/MaxDistance while the card is visible.
        KillCard clamped = CardPresentation.Resolve(card, resolver, 25f);
        check(clamped.DistanceText == "25m+",
            "card: a hot-reloaded distance ceiling re-derives the already-visible distance text");
        check(clamped.Kind == KillKind.Weakspot && clamped.Tier == KillTier.Elite && clamped.CreatedAt == 12.5f
            && clamped.EnemyDataId == 13 && clamped.HealthText == "120 HP",
            "card: refreshing presentation never changes the kill kind, tier, timestamp, id or health");

        KillCard noResolver = CardPresentation.Resolve(card, null, 999f);
        check(noResolver.EnemyName == "MOD_Shooter", "card: a missing resolver keeps the recorded name");

        Func<uint, string, string> broken = (id, raw) => throw new InvalidOperationException("name table broken");
        KillCard survived = CardPresentation.Resolve(card, broken, 999f);
        check(survived.EnemyName == "MOD_Shooter",
            "card: a throwing resolver keeps the recorded name instead of losing the card");

        Func<uint, string, string> empty = (id, raw) => "   ";
        check(CardPresentation.Resolve(card, empty, 999f).EnemyName == "MOD_Shooter",
            "card: a blank mapping result never replaces a real name");

        // Preview cards (F9) go through the same resolver, so they show what the current table says
        // for their raw name; an unknown raw name is returned unchanged by the real table.
        var preview = new KillCard(KillKind.Normal, KillTier.Minor, "大壮", 27f, 80f, 0f);
        Func<uint, string, string> passThrough = (id, raw) => raw;
        check(CardPresentation.Resolve(preview, passThrough, 999f).EnemyName == "大壮",
            "card: the F9 preview card keeps its raw name through the production resolver shape");
        check(preview.EnemyName == "大壮" && preview.EnemyDataId == 0,
            "card: resolving a preview card never mutates it (no enemy id is invented)");

        // The queue itself must still hand back the recorded card (history/ownership unchanged).
        var queue = new KillCardQueue();
        queue.Enqueue(card, 0f, 2f, 0.5f, 3);
        var snapshot = queue.Snapshot(0.1f, 2f, 0.5f, 3);
        check(snapshot.Length == 1 && snapshot[0].Card.EnemyName == "MOD_Shooter"
            && snapshot[0].Card.DistanceText == "100m",
            "card: the queue snapshot still exposes the recorded values, not the refreshed presentation");
    }
}
