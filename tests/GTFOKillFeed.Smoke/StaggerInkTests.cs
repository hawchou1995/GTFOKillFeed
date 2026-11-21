using System;
using HawChow.GTFOKillFeed;

// 1.2.24 regression: the green "this hit really staggered" damage digits. Everything asserted here is a production
// function the shipped read/paint path calls (StaggerHitPolicy.Decide / Parts / TriggerInk, DamageNumberRouter.Decide,
// ShotgunVolleyState.Capture / Batch.Add / Drain, DamageFxPolicy.Paint), so a wrong receipt, a guessed type, a
// double-counted volley, a lost cap or a green that leaks into the tier/foil colours fails here instead of only
// looking right in a source grep. The compiled drawing wiring (which pass takes the green, which keeps the rim) is
// pinned by scripts/Test-StaggerInkIl.ps1 on the real assembly.
internal static class StaggerInkTests
{
    private const int None = StaggerHitPolicy.HitreactNone;
    private const int Micro = StaggerHitPolicy.HitreactMicro;
    private const int Light = StaggerHitPolicy.HitreactLight;
    private const int Heavy = StaggerHitPolicy.HitreactHeavy;
    private const int ToDeath = StaggerHitPolicy.HitreactToDeath;

    // A settlement the game proved: local, positive, alive, receipt Light, accumulator was 20 and is back at 0,
    // the reaction state went idle -> Light. Everything else in this file is a one-field change from here.
    private static StaggerHitFacts Facts(bool local = true, bool alive = true, float damage = 12f, bool receipt = true,
        int type = Light, bool forbidden = false, bool accumulatorKnown = true, float before = 20f, float after = 0f,
        bool reactionKnown = true, int reactionBefore = None, int reactionAfter = Light)
        => new StaggerHitFacts(local, alive, damage, receipt, type, forbidden, accumulatorKnown, before, after,
            reactionKnown, reactionBefore, reactionAfter);

    internal static void Run(Action<bool, string> check)
    {
        // ---- only a non-death Light/Heavy decision may paint: the input enum is never the evidence ----------------
        check(StaggerHitPolicy.IsStaggerType(Light) && StaggerHitPolicy.IsStaggerType(Heavy),
            "only the decided Light(3)/Heavy(4) hitreact paints the digits");
        check(!StaggerHitPolicy.IsStaggerType(StaggerHitPolicy.HitreactUnspecified) && !StaggerHitPolicy.IsStaggerType(None)
            && !StaggerHitPolicy.IsStaggerType(Micro) && !StaggerHitPolicy.IsStaggerType(ToDeath)
            && !StaggerHitPolicy.IsStaggerType(StaggerHitPolicy.HitreactInstantRagdollDeath) && !StaggerHitPolicy.IsStaggerType(-1),
            "Unspecified/None/Micro/ToDeath/InstantRagdollDeath and mod values never paint (death and flinch keep their own look)");

        // ---- the proven case ------------------------------------------------------------------------------------
        StaggerHitVerdict good = StaggerHitPolicy.Decide(Facts());
        check(good.Green && good.Refusal == StaggerHitRefusal.None && good.EnteredByTransition
            && Math.Abs(good.AccumulatorBefore - 20f) < 0.001f && Math.Abs(good.AccumulatorAfter) < 0.001f
            && good.ReactionBefore == None && good.ReactionAfter == Light,
            "a nested Light receipt + accumulator reset + idle->Light transition is the one green case");
        check(StaggerHitPolicy.Decide(Facts(type: Heavy, reactionAfter: Heavy)).Green,
            "a decided Heavy also paints (heavy stagger is the same proof)");

        // ---- every gate that must refuse, one field at a time ---------------------------------------------------
        check(!StaggerHitPolicy.Decide(Facts(receipt: false)).Green
            && StaggerHitPolicy.Decide(Facts(receipt: false)).Refusal == StaggerHitRefusal.NoReceipt,
            "no nested OnTakeDamage receipt for this enemy => normal ink (never inferred from damage)");
        check(StaggerHitPolicy.Decide(Facts(local: false)).Refusal == StaggerHitRefusal.Ownership,
            "damage that is not the local player's keeps its normal ink");
        check(StaggerHitPolicy.Decide(Facts(damage: 0f)).Refusal == StaggerHitRefusal.ZeroDamage
            && StaggerHitPolicy.Decide(Facts(damage: float.NaN)).Refusal == StaggerHitRefusal.ZeroDamage,
            "a zero / NaN settlement never paints (immunity or a broken readout is not a stagger)");
        check(StaggerHitPolicy.Decide(Facts(alive: false)).Refusal == StaggerHitRefusal.Lethal,
            "the killing blow keeps the kill feedback colours, not the stagger green");
        check(StaggerHitPolicy.Decide(Facts(type: None)).Refusal == StaggerHitRefusal.DecidedType
            && StaggerHitPolicy.Decide(Facts(type: Micro, reactionAfter: Micro)).Refusal == StaggerHitRefusal.DecidedType
            && StaggerHitPolicy.Decide(Facts(type: ToDeath, reactionAfter: ToDeath)).Refusal == StaggerHitRefusal.DecidedType,
            "a decided None/Micro/ToDeath is not a stagger: the caller's requested enum can never paint on its own");
        check(StaggerHitPolicy.Decide(Facts(forbidden: true)).Refusal == StaggerHitRefusal.Forbidden,
            "ES_HitreactBase.HitreactForbidden set => the game refused the reaction, so no green");
        check(StaggerHitPolicy.Decide(Facts(after: 4f)).Refusal == StaggerHitRefusal.NoThreshold,
            "the accumulator was not reset by this settlement => the threshold branch did not run => no green");
        check(StaggerHitPolicy.Decide(Facts(accumulatorKnown: false)).Refusal == StaggerHitRefusal.Value
            && StaggerHitPolicy.Decide(Facts(before: float.NaN)).Refusal == StaggerHitRefusal.Value,
            "an unreadable accumulator fails closed instead of assuming a trigger");
        check(StaggerHitPolicy.Decide(Facts(reactionKnown: false)).Refusal == StaggerHitRefusal.Unreadable
            && StaggerHitPolicy.Decide(Facts(reactionAfter: Heavy)).Refusal == StaggerHitRefusal.Unreadable,
            "unprovable reaction state (unreadable, or the type did not change) is not painted - no false report");
        check(StaggerHitPolicy.Decide(Facts(reactionBefore: Light, reactionAfter: Light)).Refusal == StaggerHitRefusal.BusyState,
            "an enemy already inside a reaction does not re-enter one, so a threshold hit mid-reaction stays normal");
        check(StaggerHitPolicy.Decide(Facts(reactionBefore: StaggerHitPolicy.HitreactUnspecified)).Green,
            "Unspecified counts as idle, so a fresh enemy is not refused by the transition rule");

        // ---- the texts: one green number, or a split that never double counts -----------------------------------
        StaggerHitPolicy.DamageNumberParts plain = StaggerHitPolicy.Parts(24.35f, 0f, HitKind.Normal);
        check(!plain.Split && plain.Main == "24.35" && plain.Green.Length == 0,
            "a hit that did not trigger keeps exactly the old text and ink");
        StaggerHitPolicy.DamageNumberParts full = StaggerHitPolicy.Parts(24.35f, 24.35f, HitKind.Normal);
        check(full.Split && full.Main.Length == 0 && full.Green == "24.35",
            "a single hit that triggered is ONE green number - no arithmetic is added to a single hit");
        StaggerHitPolicy.DamageNumberParts mixed = StaggerHitPolicy.Parts(24f, 6f, HitKind.Normal);
        check(mixed.Split && mixed.Main == "18" && mixed.Green == "+6",
            "a merged volley that mixes pellets reads 18 + 6: total readable, triggering part green");
        check(StaggerHitPolicy.Parts(24f, 6f, HitKind.Weakspot).Green == "+6!"
            && StaggerHitPolicy.Parts(24f, 6f, HitKind.Armor).Main == "[18"
            && StaggerHitPolicy.Parts(24f, 6f, HitKind.Armor).Green == "+6]",
            "the weakspot ! and armour [ ] decorations survive the split instead of being dropped");
        StaggerHitPolicy.DamageNumberParts rounded = StaggerHitPolicy.Parts(24.35f, 6.75f, HitKind.Normal);
        check(rounded.Main == "17.6" && rounded.Green == "+6.75"
            && Math.Abs(float.Parse(rounded.Main, System.Globalization.CultureInfo.InvariantCulture)
                + float.Parse("6.75", System.Globalization.CultureInfo.InvariantCulture) - 24.35f) < 0.01f,
            "the two printed halves add up to the printed total (rounded before subtracting, so nothing is double counted)");
        check(StaggerHitPolicy.Parts(6f, 9f, HitKind.Normal).Main.Length == 0
            && StaggerHitPolicy.Parts(6f, 9f, HitKind.Normal).Green == "6",
            "a trigger amount larger than the number can never print a negative remainder");
        check(StaggerHitPolicy.Parts(0f, 0f, HitKind.Normal).Main.Length == 0
            && StaggerHitPolicy.Parts(float.NaN, 1f, HitKind.Normal).Main.Length == 0
            && StaggerHitPolicy.Parts(12f, float.NaN, HitKind.Normal).Main == "12",
            "zero/NaN totals produce no text and a NaN trigger leaves the number untouched");

        // ---- the production router + volley: per-enemy totals and the trigger subset ----------------------------
        ShotgunCategoryRegistry.Reset();
        ShotgunCategoryRegistry.Register(6u);
        var state = new ShotgunVolleyState();
        var stack = new ShotgunDamageNumberScopeStack();
        state.BeginShot(0x1000L, 100, 10f, 6u, 6, true, true, VolleyEntry.Shotgun, true, 0UL, stack,
            out _, out _, out _);
        check(Route(state, 11L, 12f, 0f, 10.01f, 101) == DamageNumberRoute.Merged,
            "pellet 1 (plain) of a shotgun volley is merged, not painted on its own");
        check(Route(state, 11L, 6f, 6f, 10.02f, 102) == DamageNumberRoute.Merged,
            "pellet 2 (the one that triggered the stagger) is merged into the same container");
        check(Route(state, 12L, 8f, 0f, 10.03f, 103) == DamageNumberRoute.Merged,
            "a pellet on a second enemy opens its own container (two targets, two values)");
        ShotgunDamageNumberScope settled = state.End(104, out _);
        MergedShotgunDamageNumber[] merged = settled.Batch.Drain();
        check(merged.Length == 2
            && Math.Abs(merged[0].Damage - 18f) < 0.001f && Math.Abs(merged[0].TriggerDamage - 6f) < 0.001f
            && merged[0].PelletHits == 2
            && Math.Abs(merged[1].Damage - 8f) < 0.001f && Math.Abs(merged[1].TriggerDamage) < 0.001f,
            "merged per-enemy totals keep the trigger subset apart: 12+6 with 6 proven, 8 with nothing proven");
        StaggerHitPolicy.DamageNumberParts volleyParts = StaggerHitPolicy.Parts(merged[0].Damage, merged[0].TriggerDamage, merged[0].Kind);
        check(volleyParts.Main == "12" && volleyParts.Green == "+6",
            "the merged container renders 12 + 6, not 18 + 6 or 24");
        StaggerHitPolicy.DamageNumberParts cleanParts = StaggerHitPolicy.Parts(merged[1].Damage, merged[1].TriggerDamage, merged[1].Kind);
        check(!cleanParts.Split && cleanParts.Main == "8",
            "an enemy whose pellets never triggered keeps one plain number");

        // ---- the next shot starts from zero: no trigger leaks across volleys ------------------------------------
        state.BeginShot(0x1000L, 200, 20f, 6u, 6, true, true, VolleyEntry.Shotgun, true, 0UL, stack,
            out _, out _, out _);
        Route(state, 11L, 5f, 0f, 20.01f, 201);
        MergedShotgunDamageNumber[] second = state.End(202, out _).Batch.Drain();
        check(second.Length == 1 && Math.Abs(second[0].Damage - 5f) < 0.001f && Math.Abs(second[0].TriggerDamage) < 0.001f,
            "a new trigger pull starts with a clean trigger total (no carry-over, no pooled value)");

        // ---- the switches: off means no number at all / no merge, never a green one ------------------------------
        var hidden = new ShotgunVolleyState();
        hidden.BeginShot(0x2000L, 300, 30f, 6u, 6, false, true, VolleyEntry.Shotgun, true, 0UL, stack,
            out _, out _, out _);
        check(Route(hidden, 21L, 4f, 4f, 30.01f, 301) == DamageNumberRoute.Suppressed,
            "ShowShotgunDamageNumbers=false swallows the pellet (the trigger can never resurrect it)");
        var off = new ShotgunVolleyState();
        off.BeginShot(0x3000L, 400, 40f, 6u, 6, true, true, VolleyEntry.Shotgun, true, 0UL, stack,
            out _, out _, out _);
        check(DamageNumberRouter.Decide(false, true, false, DamageOrigin.Bullet, 2u, true, true, 0UL, true, off, 31L,
            0f, 1f, 0f, 9f, HitKind.Normal, 40.01f, 401, 9f).Route == DamageNumberRoute.Dropped,
            "the master damage-number switch off drops the hit even when it did trigger a stagger");

        // ---- the green is the row's green and nobody else's -----------------------------------------------------
        StaggerHitPolicy.TriggerInk(out float inkRed, out float inkGreen, out float inkBlue);
        check(Math.Abs(inkRed - 120f / 255f) < 0.0001f && Math.Abs(inkGreen - 220f / 255f) < 0.0001f
            && Math.Abs(inkBlue - 160f / 255f) < 0.0001f,
            "the damage-digit green IS the shared #78DCA0 the focused-enemy stagger row uses (one green, not two)");
        DamageFxPolicy.TierColor(DamageTier.White, out float wR, out float wG, out float wB);
        DamageFxPolicy.TierColor(DamageTier.Silver, out float sR, out float sG, out float sB);
        DamageFxPolicy.TierColor(DamageTier.Gold, out float gR, out float gG, out float gB);
        DamageFxPolicy.TierColor(DamageTier.Diamond, out float dR, out float dG, out float dB);
        check(Different(inkRed, inkGreen, inkBlue, wR, wG, wB) && Different(inkRed, inkGreen, inkBlue, sR, sG, sB)
            && Different(inkRed, inkGreen, inkBlue, gR, gG, gB) && Different(inkRed, inkGreen, inkBlue, dR, dG, dB),
            "the green can never be confused with the white/silver/gold/diamond tier inks");
        check(Different(inkRed, inkGreen, inkBlue, 0.937f, 0.298f, 0.298f)
            && Different(inkRed, inkGreen, inkBlue, BarPolicy.CfoamTagRed, BarPolicy.CfoamTagGreen, BarPolicy.CfoamTagBlue),
            "the green stays distinct from the weakspot red and the C-Foam amber it must never recolour");
        DamageFxPolicy.NumberPaintPlan foilTier = DamageFxPolicy.Paint(true, true, DamageTier.Gold);
        DamageFxPolicy.NumberPaintPlan plainTier = DamageFxPolicy.Paint(false, true, DamageTier.Gold);
        check(foilTier.Foil && foilTier.TierInk && foilTier.Rings == 2 && !foilTier.FoilRim,
            "a prismatic number at Gold keeps its foil halo, emboss, rim passes and tier rim - the green only replaces the body");
        check(plainTier.TierInk && !plainTier.Foil && plainTier.Rings == 1,
            "a non-prismatic tiered number keeps its tier body and its single rim");
    }

    private static bool Different(float r1, float g1, float b1, float r2, float g2, float b2)
        => Math.Abs(r1 - r2) > 0.02f || Math.Abs(g1 - g2) > 0.02f || Math.Abs(b1 - b2) > 0.02f;

    private static DamageNumberRoute Route(ShotgunVolleyState state, long target, float damage, float stagger,
        float now, int frame)
        => DamageNumberRouter.Decide(true, true, false, DamageOrigin.Bullet, 2u, true,
            state.IsWindowOpen(now, 2u, DamageOrigin.Bullet, 0UL, true), 0UL, true, state, target,
            0f, 1f, 0f, damage, HitKind.Normal, now, frame, stagger).Route;
}
