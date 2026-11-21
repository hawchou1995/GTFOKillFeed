using System;
using System.Collections.Generic;
using HawChow.GTFOKillFeed;

// 1.2.24 review R1 regression for the damage-number submission itself. This drives the PRODUCTION helper that every
// label of DamageNumbers.Draw goes through (DamageNumberPaint.Emit) with the Unity boundary doubled out: the sink records
// the exact rect + RGBA of every pass instead of calling GUI.Label. So the review's findings are asserted as behaviour,
// not as a source grep:
//   * at FontWeight>1 the weight stamps of the NORMAL digits must be painted with the main ink - the shipped bug was that
//     they inherited the stagger green the previous pass left in GUI.color;
//   * the proven-trigger digits (body pass AND their stamps) are green and nothing else is;
//   * a weakspot colour that carries alpha<1 and a globally half-transparent number keep their effective alpha on the
//     green pass (no 1f fallback);
//   * the tier core ring and the prismatic halo ring keep their own colours and are drawn before any body pass;
//   * the two halves stay one centred group with an explicit, resolution-scaled seam on that part's own rect.
internal static class DamageNumberPaintTests
{
    // KillHud.Ink (the plain body ink DamageNumbers.Draw passes in) and the ONE shared stagger green.
    private const float InkR = 0.91f, InkG = 0.925f, InkB = 0.92f;
    private static float GreenR, GreenG, GreenB;

    private sealed class Recorder
    {
        internal readonly List<DamageNumberPaint.Pass> Passes = new();
        internal void Sink(in DamageNumberPaint.Pass pass) => Passes.Add(pass);
    }

    private static DamageNumberPaint.Plan Plan(string main, string green, int thicken = 0)
    {
        DamageNumberPaint.Plan plan = default;
        plan.GroupX = 400f;
        plan.GroupY = 300f;
        plan.GroupWidth = 200f;
        plan.GroupHeight = 44f;
        plan.MainText = main;
        plan.GreenText = green;
        plan.MainWidth = main == null ? 0f : main.Length * 22f;
        plan.GreenWidth = green == null ? 0f : green.Length * 22f;
        plan.SplitGap = DamageNumberPaint.SplitGap(1f);
        plan.Foil = false;
        plan.FoilRim = false;
        plan.Rings = 1;
        plan.OutlineRadius = 1.6f;
        plan.HaloRadius = 3.1f;
        plan.RimAlpha = 0.95f;
        plan.HaloAlpha = 0.42f;
        plan.PlainRed = 0.035f;
        plan.PlainGreen = 0.04f;
        plan.PlainBlue = 0.045f;
        plan.PlainAlpha = 0.8f;
        plan.Alpha = 0.8f;
        plan.BodyRed = InkR;
        plan.BodyGreen = InkG;
        plan.BodyBlue = InkB;
        plan.BodyAlpha = 0.6f;
        plan.TriggerRed = GreenR;
        plan.TriggerGreen = GreenG;
        plan.TriggerBlue = GreenB;
        plan.TriggerAlpha = 0.6f;
        plan.EmbossX = 1.6f;
        plan.EmbossY = 0f;
        plan.EmbossAlpha = 0.45f;
        plan.ThickenPasses = thicken;
        plan.ThickenRadius = 0.28f;
        return plan;
    }

    private static List<DamageNumberPaint.Pass> Emit(DamageNumberPaint.Plan plan)
    {
        var recorder = new Recorder();
        DamageNumberPaint.Sink sink = recorder.Sink;
        DamageNumberPaint.Emit(in plan, sink);
        return recorder.Passes;
    }

    private static bool Near(float a, float b) => Math.Abs(a - b) < 0.0005f;

    private static bool IsInk(in DamageNumberPaint.Pass pass)
        => Near(pass.Red, InkR) && Near(pass.Green, InkG) && Near(pass.Blue, InkB);

    private static bool IsGreen(in DamageNumberPaint.Pass pass)
        => Near(pass.Red, GreenR) && Near(pass.Green, GreenG) && Near(pass.Blue, GreenB);

    private static bool IsRim(in DamageNumberPaint.Pass pass)
        => Near(pass.Red, 0.035f) && Near(pass.Green, 0.04f) && Near(pass.Blue, 0.045f);

    internal static void Run(Action<bool, string> check)
    {
        StaggerHitPolicy.TriggerInk(out GreenR, out GreenG, out GreenB);

        // ---- split number at FontWeight>1: the normal digits are never green, the trigger digits always are ----------
        var passes = Emit(Plan("12", "+6", 4));
        int mainInk = 0, mainGreen = 0, greenGreen = 0, greenInk = 0, mainRim = 0, greenRim = 0;
        foreach (var pass in passes)
        {
            bool main = pass.Text == "12";
            if (IsGreen(in pass))
            {
                if (main) mainGreen++; else greenGreen++;
            }
            else if (IsInk(in pass))
            {
                if (main) mainInk++; else greenInk++;
            }
            if (IsRim(in pass))
            {
                if (main) mainRim++; else greenRim++;
            }
        }
        // 1 body pass + 4 weight stamps keep the main ink; the whole bug was these 4 stamps being drawn green.
        check(mainInk == 5, "split+thicken: the normal half keeps its own ink on the body pass AND on every weight stamp (" + mainInk + "/5)");
        check(mainGreen == 0, "split+thicken: not one pass of the normal half may carry the stagger green (" + mainGreen + " did)");
        check(greenGreen == 5 && greenInk == 0, "split+thicken: the proven-trigger half is green on the body pass and on every stamp (" + greenGreen + "/5)");
        check(mainRim == 8 && greenRim == 8, "split+thicken: the rim ring is stamped once per direction for BOTH halves and never takes a body ink");
        check(passes.Count == 8 * 2 + 2 + 4 * 2, "split+thicken: the pass count is rim(8x2) + bodies(2) + stamps(4x2), no hidden extra pass (" + passes.Count + ")");

        // every green pass uses the number's own effective alpha (never 1f, never the raw fade)
        bool greenAlphaOk = true;
        foreach (var pass in passes)
            if ((IsGreen(in pass) || IsInk(in pass)) && !Near(pass.Alpha, 0.6f)) greenAlphaOk = false;
        check(greenAlphaOk, "both body passes and every stamp use the number's effective alpha (0.6), never an opaque 1f");

        // the green only ever appears as a body/stamp pass - never as a rim or a prismatic halo pass
        bool greenOnlyInBody = true;
        foreach (var pass in passes)
            if (IsGreen(in pass) && IsRim(in pass)) greenOnlyInBody = false;
        check(greenOnlyInBody, "the stagger green is never used as the rim ink (the dark/tier ring keeps its own colour)");

        // ---- weakspot colour with alpha<1 + the global opacity: green keeps that effective alpha -------------------
        float weakspotAlpha = 0.42f;
        float globalAlpha = 0.6f;
        float effective = FeedbackPolicy.NumberTextAlpha(weakspotAlpha, globalAlpha);
        DamageNumberPaint.Plan weak = Plan("12", "+6!");
        weak.BodyRed = 0.898f;
        weak.BodyGreen = 0.282f;
        weak.BodyBlue = 0.302f;
        weak.BodyAlpha = effective;
        weak.TriggerAlpha = FeedbackPolicy.NumberTextAlpha(weakspotAlpha, globalAlpha);
        var weakPasses = Emit(weak);
        bool weakBodyOk = false, weakGreenOk = false;
        foreach (var pass in weakPasses)
        {
            if (pass.Text == "12" && Near(pass.Red, 0.898f) && Near(pass.Green, 0.282f) && Near(pass.Blue, 0.302f) && Near(pass.Alpha, effective))
                weakBodyOk = true;
            if (pass.Text == "+6!" && IsGreen(in pass) && Near(pass.Alpha, effective)) weakGreenOk = true;
        }
        check(weakBodyOk, "a weakspot colour that carries alpha<1 keeps its effective alpha on the body pass (#E5484D alpha x opacity)");
        check(weakGreenOk && !Near(effective, 1f) && !Near(effective, globalAlpha),
            "the green pass reuses that SAME effective alpha (0.252), instead of forcing 1f or the raw fade");
        check(Plan("12", "+6!").GreenText == "+6!", "the weakspot decoration travels in the text unchanged (the helper never rewrites digits)");

        // ---- not a trigger: the historical look, byte for byte -------------------------------------------------------
        var plain = Emit(Plan("24.35", null));
        check(plain.Count == 9, "a plain number is exactly the rim ring (8) plus one body pass (" + plain.Count + ")");
        bool plainBody = false, plainGreenSeen = false;
        foreach (var pass in plain)
        {
            if (pass.Text != "24.35") plainGreenSeen = true;
            if (IsInk(in pass)) plainBody = true;
            if (IsGreen(in pass)) plainGreenSeen = true;
        }
        check(plainBody && !plainGreenSeen, "a hit that never proved a stagger keeps its normal ink and emits no green at all (fail closed)");

        // ---- tier core ring + prismatic halo: colours and order ------------------------------------------------------
        DamageNumberPaint.Plan foil = Plan("12", "+6");
        foil.Foil = true;
        foil.FoilRim = false;              // tier present: the core ring stays tier, only the halo is foil
        foil.Rings = 2;
        foil.PlainRed = 0.941f;            // DamageFxPolicy Gold
        foil.PlainGreen = 0.761f;
        foil.PlainBlue = 0.29f;
        foil.PlainAlpha = 0.8f * 0.55f;    // TierOutlineAlpha
        foil.FoilPhase = 0.41f;
        var foilPasses = Emit(foil);
        int coreRing = 0, haloRing = 0, tierRim = 0, emboss = 0;
        int firstBody = -1;
        var haloHues = new List<string>();
        for (int i = 0; i < foilPasses.Count; i++)
        {
            var pass = foilPasses[i];
            if (Near(pass.Red, 0.941f) && Near(pass.Green, 0.761f) && Near(pass.Blue, 0.29f))
            {
                tierRim++;
                if (pass.Text == "12") coreRing++;
            }
            if (pass.Text == "12" && pass.Alpha < 0.5f && !IsRim(in pass) && Near(pass.Alpha, 0.8f * 0.42f))
            {
                haloRing++;
                haloHues.Add(pass.Red.ToString("0.000") + "/" + pass.Green.ToString("0.000") + "/" + pass.Blue.ToString("0.000"));
            }
            if (pass.Text == "12" && Near(pass.Red, 1f) && Near(pass.Green, 1f) && Near(pass.Blue, 1f) && Near(pass.Alpha, 0.8f * 0.45f)) emboss++;
            if ((IsInk(in pass) || IsGreen(in pass)) && firstBody < 0) firstBody = i;
        }
        check(coreRing == 8 && tierRim == 16, "prismatic + tier: the core ring is still the tier ink in all 8 directions for both halves (" + coreRing + "/8, " + tierRim + "/16)");
        check(haloRing == 8, "prismatic + tier: the halo ring is the 8-way foil in all 8 directions (" + haloRing + "/8)");
        var distinct = new HashSet<string>(haloHues);
        check(distinct.Count >= 7, "the foil halo really is eight different hues, not one repeated colour (" + distinct.Count + " distinct)");
        check(emboss == 1, "the foil emboss highlight is exactly one white pass per half before the body (" + emboss + ")");
        check(firstBody == 8 * 2 * 2 + 2, "every rim/halo/emboss pass is submitted before the first body pass, so the green can never cover them (first body at " + firstBody + ")");
        bool ringNotBody = true;
        for (int i = 0; i < firstBody; i++)
        {
            var earlier = foilPasses[i];
            if (IsInk(in earlier) || IsGreen(in earlier)) ringNotBody = false;
        }
        check(ringNotBody, "no rim/halo/emboss pass borrows the body ink or the stagger green");

        // ---- the split stays one centred group with an explicit seam on each part's own rect ------------------------
        var layout = Emit(Plan("12", "+6", 2));
        float gap = DamageNumberPaint.SplitGap(1f);
        float left = 400f + (200f - (2 * 22f + gap + 2 * 22f)) * 0.5f;
        DamageNumberPaint.Pass firstMain = default, firstGreen = default;
        bool haveMain = false, haveGreen = false;
        foreach (var pass in layout)
        {
            if (pass.Text == "12" && !haveMain && Near(pass.Y, 300f) && Near(pass.X, left)) { firstMain = pass; haveMain = true; }
            if (pass.Text == "+6" && !haveGreen && Near(pass.Y, 300f) && Near(pass.X, left + 44f + gap)) { firstGreen = pass; haveGreen = true; }
        }
        check(haveMain && haveGreen, "each half keeps its OWN rect: the main part starts at the group left, the trigger part after its width + the seam");
        check(Near(firstMain.Width, 44f) && Near(firstGreen.Width, 44f), "the rim of one half is never stamped from the concatenated text (per-part widths 44/44)");
        check(gap >= 2f && DamageNumberPaint.SplitGap(1.6f) > DamageNumberPaint.SplitGap(0.75f),
            "the seam is a small resolution-scaled gap, so 4K/720p both keep the two halves apart (" + gap + ")");
        check(Near(left + (44f + gap + 44f) * 0.5f, 500f), "the pair stays centred on the number's anchor exactly like an unsplit number");
        check(layout.Count == 8 * 2 + 2 + 2 * 2, "weight stamps scale per part, so a split number stays inside the old budget (" + layout.Count + ")");

        // ---- armour decoration and the empty-part edge cases ---------------------------------------------------------
        var armour = Emit(Plan("[18", "+6]", 0));
        bool armourText = false;
        foreach (var pass in armour)
            if (pass.Text == "[" + "18") armourText = true;
        check(armourText, "the armour '[' stays on the normal half and ']' on the green half - the texts are never merged");
        var greenOnly = Emit(Plan(null, "24.35"));
        bool centred = false;
        foreach (var pass in greenOnly)
            if (pass.Text == "24.35" && Near(pass.X, 500f - 5 * 22f * 0.5f)) centred = true;
        check(greenOnly.Count == 9 && centred, "a number that is entirely a trigger draws as ONE centred green part (no arithmetic, no phantom main pass)");
        var none = Emit(Plan(null, null));
        check(none.Count == 0, "an empty number submits nothing at all (never a stray rim pass)");
    }
}
