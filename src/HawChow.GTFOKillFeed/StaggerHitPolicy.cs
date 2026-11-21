using System;
using System.Globalization;

namespace HawChow.GTFOKillFeed;

// Why a hit's damage was NOT painted green (1.2.24). Every value is one fact the receipt could not prove,
// so the bounded `[DN] stagger=...` line can name the exact gate instead of "nothing happened".
internal enum StaggerHitRefusal
{
    None = 0,
    Ownership = 1,      // the damage root did not belong to the local player
    ZeroDamage = 2,     // no positive damage was dealt by this settlement
    Lethal = 3,         // the settlement killed the enemy (death feedback owns that number)
    NoReceipt = 4,      // no nested EnemyAgent.OnTakeDamage receipt for THIS enemy arrived
    DecidedType = 5,    // the decided type was not a non-death Light/Heavy stagger
    Forbidden = 6,      // ES_HitreactBase.HitreactForbidden was set, so the game refused the reaction
    Unreadable = 7,     // the accumulator (or the reaction state) could not be read
    NoThreshold = 8,    // the hitreact accumulator was not reset by this settlement (no hitreact was dispatched)
    BusyState = 9,      // the enemy was already inside another reaction, so no new stagger was entered
    Value = 10,         // unreadable/invalid damage or accumulator values
}

// The facts one settled damage call leaves behind, as they are read from the game (1.2.24). Everything here is
// either a value the game itself wrote or the receipt EnemyAgent.OnTakeDamage delivered, never a guess.
internal readonly struct StaggerHitFacts
{
    internal readonly bool LocalSource, AliveAfter, ReceiptSeen, Forbidden;
    internal readonly float Damage;
    internal readonly int DecidedType;
    internal readonly bool AccumulatorKnown;
    internal readonly float AccumulatorBefore, AccumulatorAfter;
    internal readonly bool ReactionKnown;
    internal readonly int ReactionBefore, ReactionAfter;

    internal StaggerHitFacts(bool localSource, bool aliveAfter, float damage, bool receiptSeen, int decidedType,
        bool forbidden, bool accumulatorKnown, float accumulatorBefore, float accumulatorAfter,
        bool reactionKnown, int reactionBefore, int reactionAfter)
    {
        LocalSource = localSource;
        AliveAfter = aliveAfter;
        Damage = damage;
        ReceiptSeen = receiptSeen;
        DecidedType = decidedType;
        Forbidden = forbidden;
        AccumulatorKnown = accumulatorKnown;
        AccumulatorBefore = accumulatorBefore;
        AccumulatorAfter = accumulatorAfter;
        ReactionKnown = reactionKnown;
        ReactionBefore = reactionBefore;
        ReactionAfter = reactionAfter;
    }
}

// The verdict for one settled hit. `Green` says the damage that this settlement dealt is the damage that really
// triggered the stagger; `Refusal` names the first gate that said no; the carried facts are exactly what the
// bounded diagnostic prints, so a real-machine log can be reconciled with the row without a second read.
internal readonly struct StaggerHitVerdict
{
    internal readonly bool Green;
    internal readonly StaggerHitRefusal Refusal;
    internal readonly int DecidedType;
    internal readonly bool AccumulatorKnown;
    internal readonly float AccumulatorBefore, AccumulatorAfter;
    internal readonly bool ReactionKnown;
    internal readonly int ReactionBefore, ReactionAfter;
    internal readonly bool EnteredByTransition;

    internal StaggerHitVerdict(bool green, StaggerHitRefusal refusal, int decidedType, bool accumulatorKnown,
        float accumulatorBefore, float accumulatorAfter, bool reactionKnown, int reactionBefore, int reactionAfter,
        bool enteredByTransition)
    {
        Green = green;
        Refusal = refusal;
        DecidedType = decidedType;
        AccumulatorKnown = accumulatorKnown;
        AccumulatorBefore = accumulatorBefore;
        AccumulatorAfter = accumulatorAfter;
        ReactionKnown = reactionKnown;
        ReactionBefore = reactionBefore;
        ReactionAfter = reactionAfter;
        EnteredByTransition = enteredByTransition;
    }
}

// 1.2.24: which damage digits are painted with the shared stagger green (#78DCA0).
//
// The evidence chain, and why each link exists:
//   * EnemyAgent.OnTakeDamage is the game's own end-of-settlement method. The shipped binary calls it from
//     Dam_EnemyDamageBase::ProcessReceivedDamage (RVA 0x137E724) with the DECIDED hitreact type in the argument
//     slot filled at 0x137E6EE (`mov byte ptr [rsp+0x28], dil`) - not the caller's REQUEST. `dil` is the decided
//     value: the requested type when the accumulator did not cross, Light (3) forced when it did and the request
//     was Unspecified (0x137E6AB `mov dil, 3`), ToDeath (5) when the settlement was lethal (0x137E6B0..0x137E6C3).
//     So a Light/Heavy value here is the game's decision, never the input enum.
//   * The same code accumulates m_damBuildToHitreact (this+0x150), compares it with HealthData.DamageUntilHitreact
//     and RESETS it to 0 in the trigger branch (0x137E693). A settlement that leaves the accumulator at 0 while it
//     dealt positive damage therefore proves the trigger branch really ran - a state transition of the game's own
//     counter, not "the damage happened to equal the threshold".
//   * ES_HitreactBase.HitreactForbidden is the game's own refusal flag; a settlement that carries a decided stagger
//     while that flag is set must not be painted.
//   * ES_HitreactBase.CurrentReactionType (byte at this+0x48, written by ES_Hitreact.CommonEnter) is the state
//     transition evidence: idle before + the decided type after means the reaction was really entered. When the
//     reaction state is readable this is a hard requirement; when it is not readable the hit fails closed (the
//     number keeps its normal ink) instead of claiming a stagger the client could not prove.
internal static class StaggerHitPolicy
{
    // ES_HitreactType values, pinned against the installed interop
    // (BepInEx/interop/Modules-ASM.dll) by tools/Test-StaggerInkIl.ps1.
    internal const int HitreactUnspecified = 0;
    internal const int HitreactNone = 1;
    internal const int HitreactMicro = 2;
    internal const int HitreactLight = 3;
    internal const int HitreactHeavy = 4;
    internal const int HitreactToDeath = 5;
    internal const int HitreactInstantRagdollDeath = 6;

    // Only a non-death light/heavy stagger paints digits green. Micro is a flinch the row does not count, and the
    // death types belong to the kill feedback that already has its own colours.
    internal static bool IsStaggerType(int decidedType)
        => decidedType == HitreactLight || decidedType == HitreactHeavy;

    private static bool IsIdle(int reactionType)
        => reactionType == HitreactNone || reactionType == HitreactUnspecified;

    internal static StaggerHitVerdict Decide(StaggerHitFacts facts)
    {
        StaggerHitVerdict Refusal(StaggerHitRefusal refusal)
            => new StaggerHitVerdict(false, refusal, facts.DecidedType, facts.AccumulatorKnown,
                facts.AccumulatorBefore, facts.AccumulatorAfter, facts.ReactionKnown,
                facts.ReactionBefore, facts.ReactionAfter, false);

        if (!facts.LocalSource) return Refusal(StaggerHitRefusal.Ownership);
        if (!(facts.Damage > 0f) || float.IsNaN(facts.Damage) || float.IsInfinity(facts.Damage)) return Refusal(StaggerHitRefusal.ZeroDamage);
        if (!facts.AliveAfter) return Refusal(StaggerHitRefusal.Lethal);
        if (!facts.ReceiptSeen) return Refusal(StaggerHitRefusal.NoReceipt);
        if (!IsStaggerType(facts.DecidedType)) return Refusal(StaggerHitRefusal.DecidedType);
        if (facts.Forbidden) return Refusal(StaggerHitRefusal.Forbidden);
        if (!facts.AccumulatorKnown || float.IsNaN(facts.AccumulatorBefore) || float.IsInfinity(facts.AccumulatorBefore)
            || float.IsNaN(facts.AccumulatorAfter) || float.IsInfinity(facts.AccumulatorAfter)
            || facts.AccumulatorBefore < 0f || facts.AccumulatorAfter < 0f) return Refusal(StaggerHitRefusal.Value);
        // The accumulator must be back at zero: that is the game's own reset in the trigger branch. A settlement
        // that leaves it non-zero did not dispatch a hitreact, whatever type the caller asked for.
        if (facts.AccumulatorAfter > 0f) return Refusal(StaggerHitRefusal.NoThreshold);
        // State transition: the reaction state has to be readable, idle before and the decided type afterwards.
        // Unreadable state fails closed - a client that cannot prove the entry must not paint the digits.
        if (!facts.ReactionKnown) return Refusal(StaggerHitRefusal.Unreadable);
        if (!IsIdle(facts.ReactionBefore)) return Refusal(StaggerHitRefusal.BusyState);
        if (facts.ReactionAfter != facts.DecidedType) return Refusal(StaggerHitRefusal.Unreadable);
        return new StaggerHitVerdict(true, StaggerHitRefusal.None, facts.DecidedType, true,
            facts.AccumulatorBefore, facts.AccumulatorAfter, true, facts.ReactionBefore, facts.ReactionAfter, true);
    }

    // The one place a green digit takes its colour from: the SAME #78DCA0 the focused-enemy stagger row uses, so
    // the HUD can never grow a second, slightly different green.
    internal static void TriggerInk(out float red, out float green, out float blue)
        => BarPolicy.StaggerInk(out red, out green, out blue);

    // The two texts one damage number is drawn from. `Main` keeps the number's historical ink (dark outline,
    // tier ink, prismatic foil body, weakspot red, armour grey); `Green` is the part this settlement proved it
    // triggered the stagger. A number that is entirely a trigger is one green text (no arithmetic added to a
    // single hit); a merged volley that mixes trigger and non-trigger pellets shows `18 + 6`, so the total stays
    // readable and nothing is counted twice.
    internal readonly struct DamageNumberParts
    {
        internal readonly string Main, Green;
        internal readonly bool Split;
        internal DamageNumberParts(string main, string green, bool split)
        {
            Main = main;
            Green = green;
            Split = split;
        }
    }

    private static string Number(float value)
        => value > 0f && value < 0.01f
            ? value.ToString("0.####", CultureInfo.InvariantCulture)
            : value.ToString("0.##", CultureInfo.InvariantCulture);

    internal static DamageNumberParts Parts(float total, float trigger, HitKind kind)
    {
        if (!KillPolicy.IsFinite(total) || total <= 0f) return new DamageNumberParts(string.Empty, string.Empty, false);
        string whole = FeedbackPolicy.DamageText(total, kind);
        if (!KillPolicy.IsFinite(trigger) || trigger <= 0f) return new DamageNumberParts(whole, string.Empty, false);
        // Both sides are rounded to the two decimals the readout prints before they are subtracted, so the two
        // printed halves always add up to the printed total (no rounding drift, no double counting).
        double roundedTotal = Math.Round(total, 2, MidpointRounding.AwayFromZero);
        double roundedTrigger = Math.Round(trigger, 2, MidpointRounding.AwayFromZero);
        if (roundedTrigger >= roundedTotal)
            return new DamageNumberParts(string.Empty, whole, true);
        double remainder = roundedTotal - roundedTrigger;
        string main = Number((float)remainder);
        if (kind == HitKind.Armor) main = "[" + main;
        string green = "+" + Number((float)roundedTrigger)
            + (kind == HitKind.Weakspot ? "!" : kind == HitKind.Armor ? "]" : string.Empty);
        return new DamageNumberParts(main, green, true);
    }

    internal static string ReasonText(StaggerHitRefusal refusal) => refusal switch
    {
        StaggerHitRefusal.Ownership => "foreign",
        StaggerHitRefusal.ZeroDamage => "no-damage",
        StaggerHitRefusal.Lethal => "lethal",
        StaggerHitRefusal.NoReceipt => "no-receipt",
        StaggerHitRefusal.DecidedType => "type",
        StaggerHitRefusal.Forbidden => "forbidden",
        StaggerHitRefusal.Unreadable => "unreadable",
        StaggerHitRefusal.NoThreshold => "no-threshold",
        StaggerHitRefusal.BusyState => "busy",
        StaggerHitRefusal.Value => "value",
        _ => "none"
    };
}
