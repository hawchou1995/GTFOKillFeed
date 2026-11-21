using System;

namespace HawChow.GTFOKillFeed;

// 1.2.24 review R1: the LAST step of DamageNumbers.Draw - every label the damage number submits, in production order -
// extracted into one Unity-free helper. Why:
//   * the drawing used to submit each label by mutating GUI.color and then calling GUI.Label, so the weight>1 "thicken"
//     stamps inherited whatever colour the previous pass left behind (the stagger green leaked onto the normal digits of
//     a split number). Here every pass carries its own RGBA, so no pass can ever inherit another pass's ink.
//   * the layout, the colours and the pass order can now be driven by a test through the production helper (the smoke
//     suite links this file and records the submitted passes), instead of asserting a copy of the maths or a source grep.
// Nothing in this file touches UnityEngine: Draw measures the two texts (only it owns the GUIStyle) and fills a Plan;
// Emit lays the parts out, walks the same pass order the shipped look had, and hands every pass to the sink.
internal static class DamageNumberPaint
{
    // One submitted label. RGBA is explicit for every pass - that is the whole point of the extraction.
    internal readonly struct Pass
    {
        internal readonly float X, Y, Width, Height;
        internal readonly float Red, Green, Blue, Alpha;
        internal readonly string Text;

        internal Pass(float x, float y, float width, float height, string text,
            float red, float green, float blue, float alpha)
        {
            X = x; Y = y; Width = width; Height = height;
            Text = text;
            Red = red; Green = green; Blue = blue; Alpha = alpha;
        }
    }

    internal delegate void Sink(in Pass pass);

    // Everything one damage number needs to be painted. Filled field by field every frame by DamageNumbers.Draw (no
    // allocation, no per-frame object): the rect comes from the shared placement, the widths from Measure, the inks from
    // the tier/foil/weakspot/armour decision plus the ONE stagger green.
    internal struct Plan
    {
        // The number's anchor rect (already offset by the flight/drift pose), plus the measured glyph widths of the two
        // parts and the seam between them.
        internal float GroupX, GroupY, GroupWidth, GroupHeight;
        internal string MainText, GreenText;
        internal float MainWidth, GreenWidth, SplitGap;

        // Rim plan: 1 plain ring, or 2 (core + halo) for prismatic foil.
        internal bool Foil, FoilRim;
        internal int Rings;
        internal float OutlineRadius, HaloRadius, RimAlpha, HaloAlpha, FoilPhase;

        // The core/halo ring ink when the ring is NOT foil: the dark shadow, or the tier tint at its own strength.
        internal float PlainRed, PlainGreen, PlainBlue, PlainAlpha;

        // The number's own fade/opacity (rim, halo and emboss are scaled with it).
        internal float Alpha;

        // The body ink the number would have had without the stagger proof (tier / foil / weakspot / armour / plain),
        // with the alpha it is really drawn at, and the ONE shared stagger green. TriggerAlpha is filled from the SAME
        // expression as BodyAlpha (the number's own colour alpha x fade x opacity), so a weakspot colour that carries
        // alpha<1 and a globally half-transparent number keep their effective alpha instead of silently becoming opaque.
        internal float BodyRed, BodyGreen, BodyBlue, BodyAlpha;
        internal float TriggerRed, TriggerGreen, TriggerBlue, TriggerAlpha;

        // Foil emboss highlight (already multiplied by the HUD resolution) and the weight>1 thicken stamps.
        internal float EmbossX, EmbossY, EmbossAlpha;
        internal int ThickenPasses;
        internal float ThickenRadius;
    }

    // The seam between the two halves of a split number: the widths come from the font, so without it the rim of one
    // half sits on the other half's glyphs. Scaled with the HUD resolution and rounded to whole pixels.
    internal static float SplitGap(float resolution) => Math.Max(2f, (float)Math.Round(5f * resolution));

    // Production pass order, unchanged from the shipped look:
    //   rim ring(s) x 8 directions -> foil emboss -> body (main ink) -> body (stagger green) -> thicken stamps.
    // The green is only ever a BODY pass that runs after the rim/halo and the emboss, and each thicken stamp sets its
    // own pass ink again, so the green can never replace a rim, a tier outline, the emboss or the normal digits.
    internal static void Emit(in Plan plan, Sink sink)
    {
        if (sink == null) return;
        bool hasMain = !string.IsNullOrEmpty(plan.MainText);
        bool hasGreen = !string.IsNullOrEmpty(plan.GreenText);
        if (!hasMain && !hasGreen) return;
        bool split = hasMain && hasGreen;

        // ONE centred group: the pair keeps exactly the anchor an unsplit number has (a single part measures to the old
        // centre too), and each part keeps its own tight rect for every pass - the rim is never stamped from the
        // concatenated text, so the two halves can never drift apart or overlap.
        float gap = split ? plan.SplitGap : 0f;
        float width = plan.MainWidth + gap + plan.GreenWidth;
        float left = plan.GroupX + (plan.GroupWidth - width) * 0.5f;
        float mainX = left;
        float greenX = split ? left + plan.MainWidth + gap : left;

        for (int ring = 0; ring < plan.Rings; ring++)
        {
            float radius = ring == 0 ? plan.OutlineRadius : plan.HaloRadius;
            float ringAlpha = ring == 0 ? plan.RimAlpha : plan.HaloAlpha;
            for (int direction = 0; direction < 8; direction++)
            {
                DamageFxPolicy.FoilDirection(direction, out int dx, out int dy);
                float red, green, blue, alpha;
                if (plan.Foil && (ring != 0 || plan.FoilRim))
                {
                    DamageFxPolicy.FoilRimColor(direction, plan.FoilPhase, out red, out green, out blue);
                    alpha = plan.Alpha * ringAlpha;
                }
                else
                {
                    red = plan.PlainRed; green = plan.PlainGreen; blue = plan.PlainBlue;
                    alpha = plan.PlainAlpha;
                }
                if (hasMain)
                    sink(new Pass(mainX + dx * radius, plan.GroupY + dy * radius, plan.MainWidth, plan.GroupHeight,
                        plan.MainText, red, green, blue, alpha));
                if (hasGreen)
                    sink(new Pass(greenX + dx * radius, plan.GroupY + dy * radius, plan.GreenWidth, plan.GroupHeight,
                        plan.GreenText, red, green, blue, alpha));
            }
        }

        if (plan.Foil)
        {
            float embossAlpha = plan.Alpha * plan.EmbossAlpha;
            if (hasMain)
                sink(new Pass(mainX + plan.EmbossX, plan.GroupY + plan.EmbossY, plan.MainWidth, plan.GroupHeight,
                    plan.MainText, 1f, 1f, 1f, embossAlpha));
            if (hasGreen)
                sink(new Pass(greenX + plan.EmbossX, plan.GroupY + plan.EmbossY, plan.GreenWidth, plan.GroupHeight,
                    plan.GreenText, 1f, 1f, 1f, embossAlpha));
        }

        if (hasMain)
            sink(new Pass(mainX, plan.GroupY, plan.MainWidth, plan.GroupHeight, plan.MainText,
                plan.BodyRed, plan.BodyGreen, plan.BodyBlue, plan.BodyAlpha));
        if (hasGreen)
            sink(new Pass(greenX, plan.GroupY, plan.GreenWidth, plan.GroupHeight, plan.GreenText,
                plan.TriggerRed, plan.TriggerGreen, plan.TriggerBlue, plan.TriggerAlpha));

        // Weight above 1 stamps the body again on a small circle: the stroke itself gets thicker instead of only its
        // shadow, and weight 1 adds nothing. Every stamp names its own ink - the main digits keep the body ink, the
        // proven trigger keeps the green - so a stamp can never pick up the other part's colour.
        for (int stamp = 0; stamp < plan.ThickenPasses; stamp++)
        {
            float angle = (stamp / (float)plan.ThickenPasses) * (float)Math.PI * 2f;
            float dx = (float)Math.Cos(angle) * plan.ThickenRadius;
            float dy = (float)Math.Sin(angle) * plan.ThickenRadius;
            if (hasMain)
                sink(new Pass(mainX + dx, plan.GroupY + dy, plan.MainWidth, plan.GroupHeight, plan.MainText,
                    plan.BodyRed, plan.BodyGreen, plan.BodyBlue, plan.BodyAlpha));
            if (hasGreen)
                sink(new Pass(greenX + dx, plan.GroupY + dy, plan.GreenWidth, plan.GroupHeight, plan.GreenText,
                    plan.TriggerRed, plan.TriggerGreen, plan.TriggerBlue, plan.TriggerAlpha));
        }
    }
}
