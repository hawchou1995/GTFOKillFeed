using System;
using HawChow.GTFOKillFeed;

// 1.2.22 regression (Survival 2026-11-20 17:45 FX.3 black scene): the pooled dissolve arms a native Decay
// by CLONING the corpse renderer, so the *input* decides whether a dangerous copy can be born at all.
// These assertions drive the real production gates (CorpseFadePolicy.Hazard/SkipReason/NativePath), not a
// source-text search: flipping any of the classifications fails here.
internal static class FadeInputGuardTests
{
    private static CorpseFadePlan Plan(bool fadeEnabled = true, bool corpseEffects = true, bool inLevel = true,
        bool paused = false, bool playPatched = false, bool prefabSafe = true,
        CorpseFadePoolState pool = CorpseFadePoolState.Ready, bool haveRenderer = true, bool offScene = false,
        int activeFades = 0, CorpseFadeHazard hazard = CorpseFadeHazard.None)
        => new CorpseFadePlan(fadeEnabled, corpseEffects, inLevel, paused, playPatched, prefabSafe, pool,
            haveRenderer, offScene, activeFades, hazard);

    internal static void Run(Action<bool, string> check)
    {
        // ---- the four dangerous inputs, each named ------------------------------------------------------
        var badCopy = new CorpseFadeInput(decayCopy: true, shadowRegistered: false, decayOwnerArmed: false, clonePresent: false, shadowRegisteredSubtree: false);
        var badClone = new CorpseFadeInput(decayCopy: false, shadowRegistered: false, decayOwnerArmed: false, clonePresent: true, shadowRegisteredSubtree: false);
        var badShadow = new CorpseFadeInput(decayCopy: false, shadowRegistered: true, decayOwnerArmed: false, clonePresent: false, shadowRegisteredSubtree: false);
        var badSubtree = new CorpseFadeInput(decayCopy: false, shadowRegistered: false, decayOwnerArmed: false, clonePresent: false, shadowRegisteredSubtree: true);
        var badOwner = new CorpseFadeInput(decayCopy: false, shadowRegistered: false, decayOwnerArmed: true, clonePresent: false, shadowRegisteredSubtree: false);
        var safe = CorpseFadeInput.Safe;

        check(CorpseFadePolicy.Hazard(badCopy) == CorpseFadeHazard.DecayCopy
            && CorpseFadePolicy.Reason(CorpseFadeHazard.DecayCopy) == "decay-copy",
            "a dissolve copy is never cloned again (hazard=decay-copy)");
        check(CorpseFadePolicy.Hazard(badClone) == CorpseFadeHazard.ClonePresent
            && CorpseFadePolicy.Reason(CorpseFadeHazard.ClonePresent) == "clone-already-present",
            "a body that already holds its own dissolve copy is refused (hazard=clone-already-present)");
        check(CorpseFadePolicy.Hazard(badShadow) == CorpseFadeHazard.ShadowRegistered
            && CorpseFadePolicy.Reason(CorpseFadeHazard.ShadowRegistered) == "shadow-registered",
            "a shadow-registered source is never cloned (hazard=shadow-registered: the duplicate registration cannot outlive the copy)");
        check(CorpseFadePolicy.Hazard(badSubtree) == CorpseFadeHazard.ShadowRegisteredSubtree
            && CorpseFadePolicy.Reason(CorpseFadeHazard.ShadowRegisteredSubtree) == "shadow-registered-subtree",
            "a registration on an (even inactive) child of the source is refused too (hazard=shadow-registered-subtree)");
        // 1.2.22 review: a dangerous first candidate must not decide the corpse. The selection chain asks this same
        // predicate for every renderer of the corpse, so a safe second renderer is still offered to the dissolve.
        check(CorpseFadePolicy.IsHazardous(badShadow) && !CorpseFadePolicy.IsHazardous(safe)
            && CorpseFadePolicy.IsHazardous(badSubtree) && !CorpseFadePolicy.IsHazardous(new CorpseFadeInput(false, false, false, false, false)),
            "one dangerous candidate and one safe candidate of the same corpse are classified independently, so the safe one can still fade");
        check(CorpseFadePolicy.Hazard(badOwner) == CorpseFadeHazard.DecayOwnerArmed
            && CorpseFadePolicy.Reason(CorpseFadeHazard.DecayOwnerArmed) == "decay-owner-armed",
            "a hierarchy whose native Decay is already armed/playing is not cloned a second time");
        check(!CorpseFadePolicy.IsHazardous(safe) && CorpseFadePolicy.Hazard(safe) == CorpseFadeHazard.None,
            "a normal corpse renderer (no copy, no shadow registration, no armed dissolve) stays cloneable");
        check(CorpseFadePolicy.IsHazardous(CorpseFadeInput.Opaque),
            "an input that cannot be classified fails closed: nothing is cloned from it");

        // ---- bad clone vs normal clone: the exact production decision -----------------------------------
        check(CorpseFadePolicy.SkipReason(Plan(hazard: CorpseFadeHazard.ShadowRegistered)) == CorpseFadeSkip.HazardousInput
            && CorpseFadePolicy.Reason(CorpseFadeSkip.HazardousInput) == "hazardous-input"
            && !CorpseFadePolicy.ShouldAttempt(Plan(hazard: CorpseFadeHazard.ShadowRegistered)),
            "the dangerous input is refused by the same policy gate the production path asks (reason=hazardous-input)");
        check(CorpseFadePolicy.ShouldAttempt(Plan()) && CorpseFadePolicy.SkipReason(Plan()) == CorpseFadeSkip.None,
            "the normal safe object still reaches the pooled dissolve: safety must not turn into 'never fade'");
        check(CorpseFadePolicy.IsReportable(CorpseFadeSkip.HazardousInput),
            "the refusal is reportable, so a host log can say why a corpse was only hidden");

        // ---- existing gates keep their meaning -----------------------------------------------------------
        check(CorpseFadePolicy.SkipReason(Plan(playPatched: true, hazard: CorpseFadeHazard.None)) == CorpseFadeSkip.PatchedByThirdParty
            && CorpseFadePolicy.SkipReason(Plan(playPatched: true, hazard: CorpseFadeHazard.ShadowRegistered)) == CorpseFadeSkip.PatchedByThirdParty,
            "a third-party Decay.Play patch (DBO) still wins: we yield before any hazard can be logged");
        check(CorpseFadePolicy.SkipReason(Plan(fadeEnabled: false, hazard: CorpseFadeHazard.ShadowRegistered)) == CorpseFadeSkip.Disabled
            && !CorpseFadePolicy.IsReportable(CorpseFadeSkip.Disabled),
            "the user's own FadeEffect=false stays silent and authoritative, even for a hazardous corpse");
        check(CorpseFadePolicy.SkipReason(Plan(pool: CorpseFadePoolState.Missing, hazard: CorpseFadeHazard.ShadowRegistered)) == CorpseFadeSkip.PoolMissing
            && CorpseFadePolicy.SkipReason(Plan(activeFades: CorpseFadePolicy.MaxConcurrentFades, hazard: CorpseFadeHazard.ShadowRegistered)) == CorpseFadeSkip.HazardousInput,
            "the pre-existing pool gates keep their order; the hazard names itself ahead of the concurrency cap");

        // ---- the corpse-own component path clones nothing, so it must stay available ---------------------
        check(CorpseFadePolicy.NativePath(Plan(hazard: CorpseFadeHazard.ShadowRegistered), true,
                CorpseFadePolicy.StopActionNone, true, false) == CorpseFadeNativePath.Started,
            "the corpse-own Decay path (Play on an already-armed component, no copy) is unaffected by the input hazard");
        check(CorpseFadePolicy.NativePath(Plan(hazard: CorpseFadeHazard.None), true,
                CorpseFadePolicy.StopActionDeactivate, true, false) == CorpseFadeNativePath.UnsafeComponent,
            "the existing stop-action refusal of a corpse-own component is unchanged");

        // ---- 1.2.22 review #4: an exhausted scan budget is "unproven", never "safe" ----------------------
        // The walker below mirrors CorpseFade.ScanForRegistrations one-to-one: same production verdict, same node
        // counter, same early exit. Only the tree is synthetic.
        var small = new Node();
        small.Children.Add(new Node());
        small.Children.Add(new Node());
        int visited = 0; bool found = false, truncated = false;
        Walk(small, 0, ref visited, ref found, ref truncated);
        check(!found && !truncated && visited == 2 && !CorpseFadePolicy.IsHazardous(Input(truncated)),
            "a small fully-scanned tree stays safe and is not refused");

        var early = new Node();
        early.Children.Add(new Node { Hazard = true });
        var deepTail = new Node();
        deepTail.Children.Add(new Node());
        early.Children.Add(deepTail);
        visited = 0; found = false; truncated = false;
        Walk(early, 0, ref visited, ref found, ref truncated);
        check(found && visited == 1 && !truncated,
            "a hazard in the first child stops the walk immediately (early exit, no needless truncation)");

        // Depth budget: a chain longer than ScanMaxDepth with the registration on the very last node.
        var chain = new Node();
        Node cursor = chain;
        for (int depth = 1; depth <= CorpseFadePolicy.ScanMaxDepth + 2; depth++)
        {
            var next = new Node { Hazard = depth == CorpseFadePolicy.ScanMaxDepth + 2 };
            cursor.Children.Add(next);
            cursor = next;
        }
        visited = 0; found = false; truncated = false;
        Walk(chain, 0, ref visited, ref found, ref truncated);
        check(!found && truncated && CorpseFadePolicy.Hazard(Input(truncated)) == CorpseFadeHazard.HierarchyUnproven
            && CorpseFadePolicy.Reason(CorpseFadeHazard.HierarchyUnproven) == "hierarchy-unproven",
            "a tree deeper than the depth budget is refused as unproven even when the hazard sits past the limit");

        // Node budget: one more sibling than ScanMaxNodes, with the registration on the last one.
        var wide = new Node();
        for (int i = 0; i <= CorpseFadePolicy.ScanMaxNodes; i++)
            wide.Children.Add(new Node { Hazard = i == CorpseFadePolicy.ScanMaxNodes });
        visited = 0; found = false; truncated = false;
        Walk(wide, 0, ref visited, ref found, ref truncated);
        check(!found && truncated && visited == CorpseFadePolicy.ScanMaxNodes,
            "spending the node budget with a sibling still unvisited is unproven, even though the hazard is the last one");

        // Exactly finished: node count == budget, every node visited, no hazard -> must NOT be refused.
        var exact = new Node();
        for (int i = 0; i < CorpseFadePolicy.ScanMaxNodes; i++) exact.Children.Add(new Node());
        visited = 0; found = false; truncated = false;
        Walk(exact, 0, ref visited, ref found, ref truncated);
        check(!found && !truncated && visited == CorpseFadePolicy.ScanMaxNodes,
            "a tree visited exactly to its last node inside the budget is complete, not truncated");

        // A leaf sitting exactly on the depth limit is finished, not truncated.
        var edge = new Node();
        cursor = edge;
        for (int depth = 1; depth <= CorpseFadePolicy.ScanMaxDepth; depth++)
        {
            var next = new Node();
            cursor.Children.Add(next);
            cursor = next;
        }
        visited = 0; found = false; truncated = false;
        Walk(edge, 0, ref visited, ref found, ref truncated);
        check(!found && !truncated && visited == CorpseFadePolicy.ScanMaxDepth,
            "a leaf at exactly the depth limit is a finished subtree and must not be refused");

        // Ancestor budget and sibling budget: only a real remainder counts.
        check(CorpseFadePolicy.AncestryTruncated(false, true) && !CorpseFadePolicy.AncestryTruncated(false, false)
            && !CorpseFadePolicy.AncestryTruncated(true, true)
            && CorpseFadePolicy.SiblingScanTruncated(CorpseFadePolicy.SiblingMaxScan + 1, CorpseFadePolicy.SiblingMaxScan)
            && !CorpseFadePolicy.SiblingScanTruncated(CorpseFadePolicy.SiblingMaxScan, CorpseFadePolicy.SiblingMaxScan),
            "hitting the ancestor budget with a parent still left (or missing siblings) is unproven; a finished walk is not");
        check(CorpseFadePolicy.IsHazardous(CorpseFadeInput.Opaque) && CorpseFadeInput.Opaque.Truncated,
            "an unreadable input is both opaque and truncated: nothing can be cloned from it");
    }

    private static CorpseFadeInput Input(bool truncated)
        => new CorpseFadeInput(false, false, false, false, false, truncated);

    private sealed class Node
    {
        internal readonly System.Collections.Generic.List<Node> Children = new();
        internal bool Hazard;
    }

    // Mirrors CorpseFade.ScanForRegistrations: same CorpseFadePolicy verdict, same counter, same early exit.
    private static void Walk(Node node, int depth, ref int visited, ref bool found, ref bool truncated)
    {
        if (node == null || found) return;
        CorpseFadePolicy.SubtreeStep step = CorpseFadePolicy.SubtreeVerdict(depth, node.Children.Count, visited);
        if (step == CorpseFadePolicy.SubtreeStep.Leaf) return;
        if (step == CorpseFadePolicy.SubtreeStep.Truncated) { truncated = true; return; }
        for (int i = 0; i < node.Children.Count; i++)
        {
            if (found) return;
            if (visited >= CorpseFadePolicy.ScanMaxNodes) { truncated = true; return; }
            visited++;
            if (node.Children[i].Hazard) found = true;
            if (!found) Walk(node.Children[i], depth + 1, ref visited, ref found, ref truncated);
        }
    }
}
