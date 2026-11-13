using System;

namespace HawChow.GTFOKillFeed;

// One head-top kill icon slot. Deliberately Unity-free: the bookkeeping that decides "may this icon be
// drawn at all", how long it lives, whether it still owns the kill and whether a preview may evict a
// real kill is all asserted headlessly. KillIconHud only adds the projection, the texture and the draw.
//   * Pointer   - the pointer snapshot of the enemy that died, captured at the credit. Audit identity
//                 only: the anchor below is NEVER re-read from that object, so a pooled enemy that reuses
//                 the memory cannot inherit or bend this icon (1.2.16 freeze).
//   * X/Y/Z     - the frozen world anchor. Written once, by KillIconHud.Add, and never again: Tick only
//                 removes expired slots, and the draw path only projects this point.
//   * Token     - a never-reused generation number: the kill card carries it, so "this kill is
//                 currently presented by a head icon" is decided by identity, not by distance or time.
internal struct KillIconSlot
{
    internal IntPtr Pointer;
    internal float X, Y, Z;
    internal float Clearance;
    internal KillTier Tier;
    internal KillFlight Flight;
    internal float At;
    internal bool Preview;
    internal int Token;
}

// Bounded storage for the head-top icons.
//
// Two capacities, on purpose (review F5):
//   * real kills: at most the configured `KillIcon/HeadMaxIcons` (hard ceiling 12), oldest evicted first;
//   * the F9 preview: fixed at exactly the nine tier x flight combinations and replaced as a SET, so a
//     repeated key press can never stack a second copy, and a preview can never evict a real kill.
internal sealed class KillIconQueue
{
    internal const int PreviewSlots = 9;   // three tiers x three flights

    private readonly KillIconSlot[] _real;
    private readonly KillIconSlot[] _preview = new KillIconSlot[PreviewSlots];
    private int _realCount;
    private int _previewCount;
    private int _nextToken;

    internal KillIconQueue(int capacity) { _real = new KillIconSlot[Math.Max(1, capacity)]; }

    internal int Capacity => _real.Length;
    internal int RealCount => _realCount;
    internal int PreviewCount => _previewCount;
    internal int Count => _realCount + _previewCount;

    // Generation numbers are unique for the lifetime of the queue and never reused, so a card can hold
    // one as its identity without the object-pool pointer problem.
    internal int NextToken()
    {
        int token = ++_nextToken;
        if (token <= 0) token = ++_nextToken;
        return token;
    }

    internal KillIconSlot RealAt(int index) => _real[index];

    // Review I8: there is deliberately NO setter for a real slot. 1.2.15 rewrote the slot every frame
    // (that is how the icon followed the corpse), and a leftover setter is exactly the door that change came
    // through. The type-level IL guard asserts that no method of this type stores into X/Y/Z.
    internal KillIconSlot PreviewAt(int index) => _preview[index];

    // Adds one real kill. Only real slots compete with each other; the preview set is untouched.
    internal void PushReal(in KillIconSlot slot, int configuredMax)
    {
        int max = Math.Min(Math.Max(1, configuredMax), _real.Length);
        if (_realCount >= max) EvictOldestReal();
        if (_realCount >= max) return;   // defensive: the store never grows past its bound
        _real[_realCount++] = slot;
    }

    internal void RemoveRealAt(int index)
    {
        if (index < 0 || index >= _realCount) return;
        for (int i = index; i < _realCount - 1; i++) _real[i] = _real[i + 1];
        _realCount--;
    }

    // Replaces the whole preview set: the caller resets, then pushes the nine combinations.
    internal void ResetPreviews() => _previewCount = 0;

    internal bool PushPreview(in KillIconSlot slot)
    {
        if (_previewCount >= PreviewSlots) return false;   // bounded even if a caller pushes too many
        _preview[_previewCount++] = slot;
        return true;
    }

    internal void RemovePreviewAt(int index)
    {
        if (index < 0 || index >= _previewCount) return;
        for (int i = index; i < _previewCount - 1; i++) _preview[i] = _preview[i + 1];
        _previewCount--;
    }

    // ---- per-frame maintenance (Unity-free, so the freeze contract is asserted headlessly) -----------
    // The whole per-frame step for the REAL slots. It has no world-position input at all: it can remove
    // an expired slot and nothing else, so the frozen anchor of a live slot cannot be rewritten by a tick
    // (1.2.16: 1.2.15 re-read the dying enemy's head every frame through this same loop).
    // Returns how many slots were removed.
    internal int TickReal(float now, float lifetime)
    {
        if (!KillPolicy.IsFinite(now)) return 0;
        float window = KillIconPolicy.Lifetime(lifetime);
        int removed = 0;
        for (int i = _realCount - 1; i >= 0; i--)
        {
            if (now - _real[i].At < window) continue;
            RemoveRealAt(i);
            removed++;
        }
        return removed;
    }

    // Same rule for the nine F9 preview slots. Their anchors are constants in front of the camera, so
    // there is even less to do; they expire on the same clock and the same clamped lifetime.
    internal int TickPreview(float now, float lifetime)
    {
        if (!KillPolicy.IsFinite(now)) return 0;
        float window = KillIconPolicy.Lifetime(lifetime);
        int removed = 0;
        for (int i = _previewCount - 1; i >= 0; i--)
        {
            if (now - _preview[i].At < window) continue;
            RemovePreviewAt(i);
            removed++;
        }
        return removed;
    }

    // Token lookup. A token below 1 belongs to no slot ("never had an icon" or "not a kill event").
    internal bool TryFind(int token, out int index, out bool preview)
    {
        index = -1;
        preview = false;
        if (token <= 0) return false;
        for (int i = 0; i < _realCount; i++)
        {
            if (_real[i].Token != token) continue;
            index = i;
            return true;
        }
        for (int i = 0; i < _previewCount; i++)
        {
            if (_preview[i].Token != token) continue;
            index = i;
            preview = true;
            return true;
        }
        return false;
    }

    internal void Clear()
    {
        _realCount = 0;
        _previewCount = 0;
    }

    // The oldest real slot: only real slots are candidates, so a preview can never be evicted instead.
    private void EvictOldestReal()
    {
        if (_realCount == 0) return;
        int oldest = 0;
        for (int i = 1; i < _realCount; i++) if (_real[i].At < _real[oldest].At) oldest = i;
        RemoveRealAt(oldest);
    }
}
