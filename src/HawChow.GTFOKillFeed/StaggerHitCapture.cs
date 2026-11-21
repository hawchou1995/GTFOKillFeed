using System;
using System.Collections.Generic;
using System.Globalization;
using Enemies;
using HarmonyLib;

namespace HawChow.GTFOKillFeed;

// 1.2.24 Unity adapter: the two live reads the stagger verdict needs, the nested receipt the game hands out, and the
// bounded diagnostic. Every DECISION lives in StaggerHitPolicy (Unity-free, asserted by the smoke suite); this class
// only reads game state and writes one receipt line.
//
// Why EnemyAgent.OnTakeDamage is the receipt: the shipped binary calls it from
// Dam_EnemyDamageBase::ProcessReceivedDamage (RVA 0x137E70C..0x137E724) with the type the settlement DECIDED
// (`mov byte ptr [rsp+0x28], dil` at 0x137E6EE), after the accumulator/threshold branch. It is a plain end-of-
// settlement method - no ES_* state machine is detoured, nothing is written back, and the patch only reads the
// argument and the enemy identity.
internal static class StaggerHitCapture
{
    // A per-level allowance: the receipt is a reconciliation aid, never a stream.
    private const int ReceiptLines = 6;
    private static readonly DiagnosticBudget Receipt = new();
    private static readonly HashSet<string> ReceiptSignatures = new();

    // Reads the enemy's own hitreact accumulator (Dam_EnemyDamageBase.m_damBuildToHitreact). Unreadable/null means the
    // caller must fail closed - never 0 by default, because 0 is exactly the value that proves a trigger.
    internal static bool TryReadAccumulator(Dam_EnemyDamageBase damage, out float value)
    {
        value = 0f;
        if (damage == null) return false;
        try
        {
            float read = damage.m_damBuildToHitreact;
            if (float.IsNaN(read) || float.IsInfinity(read) || read < 0f) return false;
            value = read;
            return true;
        }
        catch { return false; }
    }

    // Reads the game's own refusal flag and the current reaction type from the enemy's locomotion hitreact state.
    // Both are plain property reads of fields the game maintains (byte at this+0x49 / this+0x48); no state machine
    // method is replaced or called.
    internal static bool TryReadReaction(EnemyAgent agent, out bool forbidden, out int reactionType)
    {
        forbidden = false;
        reactionType = StaggerHitPolicy.HitreactUnspecified;
        if (agent == null) return false;
        try
        {
            var locomotion = agent.Locomotion;
            var hitreact = locomotion != null ? locomotion.Hitreact : null;
            if (hitreact == null) return false;
            forbidden = hitreact.HitreactForbidden;
            reactionType = (int)hitreact.CurrentReactionType;
            return true;
        }
        catch { return false; }
    }

    // The nested receipt: OnTakeDamage was called while this thread is inside the damage settlement of THIS enemy.
    // A receipt for another enemy, from another thread (the scope stack is thread-static) or outside a settlement is
    // discarded - it can never paint somebody else's number.
    internal static void NoteDecided(EnemyAgent agent, ES_HitreactType type)
    {
        DamageRoot root = KillTracker.ActiveRoot;
        if (root == null || agent == null) return;
        IntPtr pointer;
        try
        {
            var damage = agent.Damage;
            pointer = damage != null ? damage.Pointer : IntPtr.Zero;
        }
        catch { return; }
        if (pointer == IntPtr.Zero || pointer != root.EnemyPointer) return;
        root.StaggerSeen = true;
        root.StaggerDecidedType = (int)type;
    }

    // One bounded line per distinct outcome (reason + decided type + whether the accumulator was reset), so a
    // real-machine log shows WHY a number stayed normal instead of leaving "no green" indistinguishable from
    // "the receipt never ran".
    internal static void Report(in StaggerHitVerdict verdict, float damage, int frame)
    {
        string signature = (int)verdict.Refusal + ":" + verdict.DecidedType + ":"
            + (verdict.AccumulatorAfter > 0f ? "open" : "reset");
        if (ReceiptSignatures.Count >= ReceiptLines && !ReceiptSignatures.Contains(signature)) return;
        if (!ReceiptSignatures.Add(signature)) return;
        if (!Receipt.TryUse(ReceiptLines)) return;
        Plugin.Instance?.Log.LogInfo(
            "[DN] stagger=" + (verdict.Green ? "green" : "none")
            + ";reason=" + StaggerHitPolicy.ReasonText(verdict.Refusal)
            + ";type=" + verdict.DecidedType.ToString(CultureInfo.InvariantCulture)
            + ";damage=" + damage.ToString("0.##", CultureInfo.InvariantCulture)
            + ";build=" + (verdict.AccumulatorKnown ? verdict.AccumulatorBefore.ToString("0.##", CultureInfo.InvariantCulture) : "?")
            + "->" + (verdict.AccumulatorKnown ? verdict.AccumulatorAfter.ToString("0.##", CultureInfo.InvariantCulture) : "?")
            + ";reaction=" + (verdict.ReactionKnown
                ? verdict.ReactionBefore.ToString(CultureInfo.InvariantCulture) + "->" + verdict.ReactionAfter.ToString(CultureInfo.InvariantCulture)
                : "?")
            + ";entered=" + (verdict.EnteredByTransition ? "yes" : "no")
            + ";frame=" + frame.ToString(CultureInfo.InvariantCulture)
            + ".");
    }

    internal static void Reset()
    {
        Receipt.Reset();
        ReceiptSignatures.Clear();
    }
}

// The receipt hook. Harmony injects only the two parameters this prefix declares (the enemy and the decided type);
// the rest of the signature is untouched, so the game's call is unchanged.
[HarmonyPatch(typeof(EnemyAgent), "OnTakeDamage")]
internal static class EnemyAgentTakeDamagePatch
{
    [HarmonyPrefix]
    private static void Prefix(EnemyAgent __instance, ES_HitreactType hitreact)
        => KillTracker.GuardAction(() => StaggerHitCapture.NoteDecided(__instance, hitreact));
}
