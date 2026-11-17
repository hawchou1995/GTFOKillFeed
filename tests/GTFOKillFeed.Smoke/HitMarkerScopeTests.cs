using System;
using HawChow.GTFOKillFeed;

// The event-rule matrix for the hit/kill lines and the bounded impact-scope stack that carries the armour
// verdict and the event identity. Everything here is Unity-free: these are the exact decisions the Harmony
// prefixes make.
//
// The requirement this file pins down:
//   * a zero-damage (`noDamage`) indicator event may be replaced ONLY by a confirmed local armour impact
//     whose world position matches and whose scope is still inside its original damage call. Friendly fire,
//     an unconfirmed noDamage event, a remote hit, a lost/expired scope or an undrawable marker always keep
//     the vanilla indicator - a noDamage event is never guessed as armour;
//   * the armour verdict is answered at most once per impact, while the impact's identity stays readable for
//     the whole damage call: that identity (a never-reused generation number, not a pointer and not a
//     distance) is the ONLY thing that may merge two events into one line (review F3).
internal static class HitMarkerScopeTests
{
    internal static void Run(Action<bool, string> check)
    {
        // ---- the routing rule --------------------------------------------------------------
        HitMarkerKind kind;
        check(HitMarkerPolicy.TryDecide(true, false, true, true, HitKind.Armor, false, out kind) && kind == HitMarkerKind.Armor,
            "hitmarker-scope: noDamage + confirmed local armour impact draws the armour line and suppresses that vanilla event");
        check(!HitMarkerPolicy.TryDecide(true, false, true, false, HitKind.Normal, false, out kind),
            "hitmarker-scope: a noDamage event without an impact scope keeps the vanilla indicator");
        check(!HitMarkerPolicy.TryDecide(true, false, true, true, HitKind.Normal, false, out kind)
            && !HitMarkerPolicy.TryDecide(true, false, true, true, HitKind.Weakspot, false, out kind),
            "hitmarker-scope: a noDamage event matched to a normal/weakspot impact is never drawn as armour");
        check(!HitMarkerPolicy.TryDecide(true, true, true, true, HitKind.Armor, false, out kind)
            && !HitMarkerPolicy.TryDecide(true, true, false, true, HitKind.Armor, false, out kind),
            "hitmarker-scope: friendly-fire events keep the vanilla indicator even with a matching armour scope");
        check(!HitMarkerPolicy.TryDecide(false, false, true, true, HitKind.Armor, false, out kind),
            "hitmarker-scope: with the feature off nothing is replaced");
        check(HitMarkerPolicy.TryDecide(true, false, false, false, HitKind.Normal, false, out kind) && kind == HitMarkerKind.Normal,
            "hitmarker-scope: an ordinary hit with no scope still draws the normal line (previous behaviour)");
        check(HitMarkerPolicy.TryDecide(true, false, false, false, HitKind.Normal, true, out kind) && kind == HitMarkerKind.Weakspot,
            "hitmarker-scope: the weakspot flag still decides a normal hit's line");
        check(HitMarkerPolicy.TryDecide(true, false, false, true, HitKind.Armor, false, out kind) && kind == HitMarkerKind.Armor,
            "hitmarker-scope: a matching armour scope turns an ordinary damage hit into the armour line");
        check(HitMarkerPolicy.TryDecide(true, false, false, true, HitKind.Weakspot, false, out kind) && kind == HitMarkerKind.Normal,
            "hitmarker-scope: a weakspot scope does not override the event's own weakspot flag");

        // ---- the bounded impact-scope stack -------------------------------------------------
        var stack = new ImpactScopeStack();
        int armour = stack.Push(10f, 0f, 0f, HitKind.Armor, 100);
        check(armour > 0 && stack.Depth == 1,
            "hitmarker-scope: a local armour impact publishes a scope with a token");
        check(stack.TryClaim(100, 10f, 0f, 0f, true, out HitKind taken, out int takenId)
            && taken == HitKind.Armor && takenId == armour,
            "hitmarker-scope: the armour-only lookup finds this impact in the same frame");
        check(!stack.TryClaim(100, 10f, 0f, 0f, true, out _, out _),
            "hitmarker-scope: the armour verdict is answered once, so a second event of the same call cannot re-read it");
        check(stack.Depth == 1,
            "hitmarker-scope: claiming the verdict keeps the scope alive for the event identity (not consumed)");
        check(stack.TryIdentify(100, 10f, 0f, 0f, out int armourIdentity) && armourIdentity == armour,
            "hitmarker-scope: the claimed scope still identifies its own event (hit + death of one lethal blow)");
        stack.Reset();

        // A normal/weakspot impact never answers a noDamage (armour-only) lookup.
        stack.Push(0f, 0f, 0f, HitKind.Normal, 200);
        check(!stack.TryClaim(200, 0f, 0f, 0f, true, out _, out _)
            && stack.TryClaim(200, 0f, 0f, 0f, false, out HitKind normalKind, out _) && normalKind == HitKind.Normal,
            "hitmarker-scope: an armour-only lookup ignores a normal impact (noDamage is never guessed as armour)");

        // Position: 0.25 m matches, 0.5 m is a different impact (two enemies standing close).
        stack.Reset();
        stack.Push(0f, 0f, 0f, HitKind.Armor, 300);
        check(stack.TryIdentify(300, 0.25f, 0f, 0f, out _),
            "hitmarker-scope: an impact 0.25 m away is still the same hit (inclusive match radius)");
        check(!stack.TryIdentify(300, 0.5f, 0f, 0f, out _),
            "hitmarker-scope: an impact 0.5 m away is a different target, not this armour hit");

        // Two enemies ~0.8 m apart, both hits in one frame: each event only ever sees its own scope and id.
        stack.Reset();
        int near = stack.Push(0f, 0f, 0f, HitKind.Armor, 400);
        int far = stack.Push(0.8f, 0f, 0f, HitKind.Normal, 400);
        check(near != far && near > 0 && far > 0,
            "hitmarker-scope: two nearby enemies publish two distinct scopes");
        check(stack.TryClaim(400, 0.8f, 0f, 0f, true, out _, out _) == false,
            "hitmarker-scope: the far enemy's event cannot claim the near enemy's armour scope");
        check(stack.TryClaim(400, 0.8f, 0f, 0f, false, out HitKind farKind, out int farId) && farKind == HitKind.Normal && farId == far,
            "hitmarker-scope: the far enemy's event matches its own normal scope (innermost wins)");
        check(stack.TryIdentify(400, 0.8f, 0f, 0f, out int farIdentity) && farIdentity == far,
            "hitmarker-scope: the far enemy's identity is its own, never the neighbour's (review F3)");
        check(stack.TryClaim(400, 0f, 0f, 0f, true, out HitKind nearKind, out int nearId) && nearKind == HitKind.Armor && nearId == near,
            "hitmarker-scope: the near enemy's armour scope is still there for its own event");
        check(stack.TryIdentify(400, 0f, 0f, 0f, out int nearIdentity) && nearIdentity == near && nearIdentity != farIdentity,
            "hitmarker-scope: neighbouring impacts keep two different identities, so neither can swallow the other");

        // Expired / popped / reset scopes never match.
        stack.Reset();
        stack.Push(0f, 0f, 0f, HitKind.Armor, 500);
        check(!stack.TryClaim(500 + HitMarkerPolicy.ImpactFrameWindow + 1, 0f, 0f, 0f, true, out _, out _)
            && !stack.TryIdentify(500 + HitMarkerPolicy.ImpactFrameWindow + 1, 0f, 0f, 0f, out _),
            "hitmarker-scope: an expired scope (a finalizer that never ran) can neither answer armour nor identify an event");
        stack.Reset();
        int popped = stack.Push(0f, 0f, 0f, HitKind.Armor, 600);
        check(stack.Pop(popped) && stack.Depth == 0
            && !stack.TryClaim(600, 0f, 0f, 0f, true, out _, out _)
            && !stack.TryIdentify(600, 0f, 0f, 0f, out _),
            "hitmarker-scope: popping the scope (the original method returned or threw) removes context and identity");
        stack.Reset();
        stack.Push(0f, 0f, 0f, HitKind.Armor, 700);
        stack.Reset();
        check(stack.Depth == 0 && !stack.TryClaim(700, 0f, 0f, 0f, true, out _, out _)
            && !stack.TryIdentify(700, 0f, 0f, 0f, out _),
            "hitmarker-scope: a level change clears every scope");

        // Broken positions never publish anything.
        stack.Reset();
        check(stack.Push(float.NaN, 0f, 0f, HitKind.Armor, 800) == 0
            && stack.Push(0f, float.PositiveInfinity, 0f, HitKind.Armor, 800) == 0
            && stack.Depth == 0,
            "hitmarker-scope: an unreadable impact position never publishes a scope");

        // Depth is bounded; a nested overflow is refused (and therefore falls back to vanilla) rather than
        // silently dropping an older scope.
        stack.Reset();
        bool allPushed = true;
        for (int i = 0; i < HitMarkerPolicy.MaxImpactDepth; i++)
            allPushed &= stack.Push(i, 0f, 0f, HitKind.Armor, 900) > 0;
        check(allPushed && stack.Depth == HitMarkerPolicy.MaxImpactDepth
            && stack.Push(99f, 0f, 0f, HitKind.Armor, 900) == 0,
            "hitmarker-scope: the scope stack is bounded at " + HitMarkerPolicy.MaxImpactDepth + " and refuses overflow");

        // Innermost-first: a nested damage call's scope wins over the outer one at the same spot.
        stack.Reset();
        int outer = stack.Push(0f, 0f, 0f, HitKind.Armor, 1000);
        int inner = stack.Push(0f, 0f, 0f, HitKind.Armor, 1000);
        check(stack.TryClaim(1000, 0f, 0f, 0f, true, out _, out int firstId) && firstId == inner,
            "hitmarker-scope: the innermost matching scope wins (nested damage calls)");
        stack.Pop(inner);
        check(stack.TryClaim(1000, 0f, 0f, 0f, true, out _, out int secondId) && secondId == outer,
            "hitmarker-scope: after the nested scope is popped the outer one is available again");

        // Out-of-order pops (a finalizer for an inner scope that already left) must not remove a stranger.
        stack.Reset();
        int a = stack.Push(0f, 0f, 0f, HitKind.Armor, 1100);
        check(!stack.Pop(12345) && stack.Depth == 1 && stack.Pop(a) && stack.Depth == 0,
            "hitmarker-scope: popping an unknown token removes nothing");

        // Generation numbers are never reused, so a pooling event can never resurrect an old identity.
        stack.Reset();
        int first = stack.Push(0f, 0f, 0f, HitKind.Armor, 1200);
        stack.Pop(first);
        int second = stack.Push(0f, 0f, 0f, HitKind.Armor, 1201);
        stack.Pop(second);
        int third = stack.Push(0f, 0f, 0f, HitKind.Armor, 1202);
        check(third > second && second > first,
            "hitmarker-scope: every impact gets a fresh generation number (a pooled object can never reuse one)");
    }
}
