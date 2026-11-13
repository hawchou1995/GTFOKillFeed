using System;

namespace HawChow.GTFOKillFeed;

// Presentation-only re-resolution for cards that are already in the queue. The killed enemy, the
// kill kind/tier, the timestamp and the captured distance/health stay exactly as recorded; only the
// display name mapping and the distance ceiling are recomputed, so a config (or name-table) change
// shows up on cards that are already on screen instead of waiting for the next kill.
internal static class CardPresentation
{
    internal static KillCard Resolve(KillCard card, Func<uint, string, string> nameResolver, float maxDistance)
    {
        if (card == null) return null;
        string display = card.EnemyName;
        if (nameResolver != null && !string.IsNullOrEmpty(card.RawName))
        {
            try
            {
                string resolved = nameResolver(card.EnemyDataId, card.RawName);
                if (!string.IsNullOrWhiteSpace(resolved)) display = resolved;
            }
            catch { }
        }
        return card.WithPresentation(display, maxDistance);
    }
}
