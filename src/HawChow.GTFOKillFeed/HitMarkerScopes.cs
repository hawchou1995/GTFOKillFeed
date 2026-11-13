using System;

namespace HawChow.GTFOKillFeed;

// One local damage impact, published by the KillTracker prefixes (which run BEFORE the original damage
// method, and therefore before the game may show its hit indicator from inside that method).
//
// This is the only source of the armour verdict: the game's own hit-indicator event carries the world
// position and the weakspot flag but no armour flag, so "this event is an armour hit" can only come from
// the limb the local damage landed on. Everything here is plain floats/ints, so the whole matching rule is
// asserted headlessly (the game's native call order is NOT proven by the interop assemblies - see
// reports/feature-1.2.17-hitkill-lines.md).
//
// The scope id doubles as the *event identity* (review F3): it is a never-reused generation number of one
// damage call, so the hit indicator and the death indicator that the same lethal blow produces can be
// recognised as ONE event, while two different damage calls - even on two enemies standing next to each
// other - can never be confused by their world positions.
internal struct ImpactScope
{
    internal int Id;
    internal float X, Y, Z;
    internal HitKind Kind;
    internal int Frame;
    internal bool Claimed;   // the armour verdict has already been read by one indicator event
}

// Bounded, LIFO stack of the local impacts currently inside their original damage call. Matching rules:
//   * only an ACTIVE scope can match (Popped = the original call returned, the context is gone);
//   * the scope must be from this frame or the previous one (a leaked scope can never match later);
//   * the impact position must be within HitMarkerPolicy.ImpactMatchDistanceMetres - deliberately much
//     tighter than a visual merge radius, so two enemies standing next to each other cannot trade scopes;
//   * the innermost matching scope wins (nested damage calls);
//   * the armour verdict (TryClaim) is answered at most once per scope, while the identity (TryIdentify)
//     stays readable for the whole damage call so a paired death event can be tied to the same event.
internal sealed class ImpactScopeStack
{
    private readonly ImpactScope[] _scopes = new ImpactScope[HitMarkerPolicy.MaxImpactDepth];
    private int _count;
    private int _nextId;

    internal int Depth => _count;

    internal int Push(float x, float y, float z, HitKind kind, int frame)
    {
        if (!HitMarkerPolicy.IsFinite(x) || !HitMarkerPolicy.IsFinite(y) || !HitMarkerPolicy.IsFinite(z)) return 0;
        PruneExpired(frame);
        if (_count >= _scopes.Length) return 0;   // bounded: refuse the nested scope instead of dropping evidence
        int id = ++_nextId;
        if (id == 0) id = ++_nextId;
        _scopes[_count++] = new ImpactScope { Id = id, X = x, Y = y, Z = z, Kind = kind, Frame = frame };
        return id;
    }

    // Removes the scope by id (innermost first). Called from the Harmony finalizer, so a throwing original
    // method still leaves no stale context behind.
    internal bool Pop(int id)
    {
        if (id == 0) return false;
        for (int i = _count - 1; i >= 0; i--)
        {
            if (_scopes[i].Id != id) continue;
            RemoveAt(i);
            return true;
        }
        return false;
    }

    // Armour verdict for one indicator event. armourOnly restricts the search to confirmed armour impacts
    // (used for noDamage events, which must never be guessed as armour). The matched scope is marked as
    // claimed instead of being removed, so a second event of the same call cannot read the verdict again
    // while the identity below still works for the paired death event.
    internal bool TryClaim(int frame, float x, float y, float z, bool armourOnly, out HitKind kind, out int id)
    {
        kind = HitKind.Normal;
        id = 0;
        int index = Find(frame, x, y, z, armourOnly, true);
        if (index < 0) return false;
        kind = _scopes[index].Kind;
        id = _scopes[index].Id;
        _scopes[index].Claimed = true;
        return true;
    }

    // The event identity: the id of the innermost ACTIVE scope that describes this impact, whether or not
    // its armour verdict was already read. Two events that resolve to the same id come from the same damage
    // call and may be merged into one line; anything else must stay its own line (review F3).
    internal bool TryIdentify(int frame, float x, float y, float z, out int id)
    {
        id = 0;
        int index = Find(frame, x, y, z, false, false);
        if (index < 0) return false;
        id = _scopes[index].Id;
        return true;
    }

    internal void Reset()
    {
        _count = 0;
    }

    private int Find(int frame, float x, float y, float z, bool armourOnly, bool unclaimedOnly)
    {
        for (int i = _count - 1; i >= 0; i--)
        {
            ImpactScope scope = _scopes[i];
            if (unclaimedOnly && scope.Claimed) continue;
            if (armourOnly && scope.Kind != HitKind.Armor) continue;
            float distance = HitMarkerPolicy.DistanceSquared(scope.X, scope.Y, scope.Z, x, y, z);
            if (!HitMarkerPolicy.ImpactMatches(frame, scope.Frame, distance)) continue;
            return i;
        }
        return -1;
    }

    private void PruneExpired(int frame)
    {
        for (int i = _count - 1; i >= 0; i--)
            if (!HitMarkerPolicy.ImpactFrameCurrent(frame, _scopes[i].Frame)) RemoveAt(i);
    }

    private void RemoveAt(int index)
    {
        for (int i = index; i < _count - 1; i++) _scopes[i] = _scopes[i + 1];
        _count--;
    }
}
