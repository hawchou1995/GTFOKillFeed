using System;

namespace HawChow.GTFOKillFeed;

// Bounded per-target cache for "the highest enabled Renderer of this enemy", the measurement
// EnemyFocus.TryGetHeadWorld adds to the head limb position.
//
// Why it exists (review F4): a single-slot cache is enough for one focused enemy, but several head
// consumers run every frame - the focused-enemy bar and the downed teammate marks (the head-top kill icons
// stopped reading the live head in 1.2.16: they freeze their anchor at the lethal credit). With one slot,
// two alternating targets invalidated each other on every call, so GetComponentsInChildren<Renderer> ran
// several times per frame per head. This cache keeps one sample per target for the refresh window, is
// hard-bounded, and caches misses too (an enemy with no enabled renderer used to re-enumerate every frame).
//
// A slot is only ever a *measurement*: the consumer still decides whether the sample is usable (the
// caller keeps its own "inside this window around the anchor" sanity check), so a stale or pooled
// sample can never move a head to a guessed position.
internal sealed class HeadBoundsCache
{
    // 12 head icons (KillIconPolicy.MaxIconsCeiling) + the focus bar + the teammate marks + slack.
    internal const int MaxSlots = 16;
    internal const float RefreshSeconds = 0.35f;

    private struct Slot
    {
        internal int Id;
        internal float At;
        internal float TopY;
        internal bool Filled;
    }

    private readonly Slot[] _slots = new Slot[MaxSlots];
    private int _slotCursor;
    private int _samples;
    private int _hits;

    // Measurable bounds for the tests: how many times the renderer enumeration actually ran.
    internal int Samples => _samples;
    internal int Hits => _hits;
    internal int Filled
    {
        get
        {
            int filled = 0;
            for (int i = 0; i < _slots.Length; i++) if (_slots[i].Filled) filled++;
            return filled;
        }
    }

    // Returns the cached top for this target, or measures once and caches it. An unusable instance id
    // (int.MinValue/0, i.e. no live object) is measured but never cached: it cannot identify a target.
    internal float Top(int id, float now, Func<float> measure)
    {
        if (measure == null) return 0f;
        // An unusable id or clock cannot identify a target, so it is measured but never cached.
        if (id == 0 || id == int.MinValue || !KillPolicy.IsFinite(now)) return measure();
        int slot = Find(id);
        if (slot >= 0 && now >= _slots[slot].At && now - _slots[slot].At < RefreshSeconds)
        {
            _hits++;
            return _slots[slot].TopY;
        }
        float measured = measure();
        if (!KillPolicy.IsFinite(measured)) measured = 0f;
        _samples++;
        Store(id, now, measured);
        return measured;
    }

    // True when a fresh sample exists for this target; used by the tests and by nothing else.
    internal bool HasFresh(int id, float now)
    {
        int slot = Find(id);
        return slot >= 0 && KillPolicy.IsFinite(now) && now >= _slots[slot].At
            && now - _slots[slot].At < RefreshSeconds;
    }
    internal void Clear()
    {
        for (int i = 0; i < _slots.Length; i++) _slots[i] = default;
        _slotCursor = 0;
        _samples = 0;
        _hits = 0;
    }

    private int Find(int id)
    {
        for (int i = 0; i < _slots.Length; i++) if (_slots[i].Filled && _slots[i].Id == id) return i;
        return -1;
    }

    // Never grows: a same-id slot is refreshed in place, anything else takes the round-robin slot.
    private void Store(int id, float now, float value)
    {
        int slot = Find(id);
        if (slot < 0)
        {
            slot = _slotCursor;
            _slotCursor = (_slotCursor + 1) % _slots.Length;
        }
        _slots[slot] = new Slot { Id = id, At = now, TopY = value, Filled = true };
    }
}
