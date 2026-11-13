using System.Collections.Generic;
using UnityEngine;

namespace HawChow.GTFOKillFeed;

// `DamageNumberRoute` moved to ShotgunDamageNumberBatch.cs (1.2.19 review R3) so the smoke suite compiles the same type
// and calls the same production route function (DamageNumberRouter.Decide).

internal static class DamageNumbers
{
    private sealed class Number
    {
        internal Vector3 World;
        internal float At, Side;
        internal HitKind Kind;
        internal float Damage;
        internal string Text;
        internal bool Preview;
        internal int FlySeed;
    }
    private static readonly List<Number> Active = new(FeedbackPolicy.MaxNumbers);
    private static int _sequence;
    private static GUIStyle _style;
    private static readonly HexColorCache WeakspotColor = new();
    private static Color ResolveWeakspotColor()
    {
        string configured = Plugin.DamageNumberWeakspotColor != null ? Plugin.DamageNumberWeakspotColor.Value : null;
        WeakspotColor.Resolve(configured, KillHud.Accent.r, KillHud.Accent.g, KillHud.Accent.b, KillHud.Accent.a);
        return new Color(WeakspotColor.Red, WeakspotColor.Green, WeakspotColor.Blue, WeakspotColor.Alpha);
    }
    // `origin` is the entry that produced this hit (1.2.19). Only DamageOrigin.Bullet may be merged as a shotgun
    // pellet: a melee / explosion / DoT / push / third-party custom hit always takes the immediate path, so it can
    // never be delayed into a volley, summed with pellets, or swallowed by DamageNumbers/ShowShotgunDamageNumbers.
    // The default is Other, i.e. "nobody proved this came from a bullet" - the F9 preview path is unaffected because
    // a preview is never classified as shotgun, exactly as before.
    // 1.2.19 review R3: this is now a thin Unity adapter - the route itself (classifier -> visibility gate -> volley
    // capture) is DamageNumberRouter.Decide, which is Unity-free and therefore asserted directly by the smoke suite.
    // Nothing here re-decides: it reads the live switches and clock, calls the shared decision and draws what it says.
    internal static DamageNumberRoute Add(Vector3 world, float damage, HitKind kind, bool preview = false, uint gearCategoryId = 0, DamageOrigin origin = DamageOrigin.Other, ulong shooterLookup = 0UL)
    {
        bool masterEnabled = Plugin.ShowDamageNumbers != null && Plugin.ShowDamageNumbers.Value
            && Plugin.Enabled != null && Plugin.Enabled.Value;
        bool showShotgunNumbers = Plugin.ShowShotgunDamageNumbers?.Value ?? true;
        int frame = Time.frameCount;
        // D6: the two facts only this adapter can read. `nested` is true when this damage is being settled from inside
        // the Fire call that opened the still-open volley (the same-thread scope stack) - the same-shot proof. It is
        // false on another thread or after the Fire returned, and then the category/shooter proof has to carry the hit;
        // an unprovable hit keeps its own number.
        bool nestedSameShot = ShotgunDamageNumberCapture.IsNestedPelletHit();
        DamageNumberDecision decision = DamageNumberRouter.Decide(masterEnabled, showShotgunNumbers, preview, origin,
            gearCategoryId,
            ShotgunDamageNumberCapture.IsShotgunCategory(gearCategoryId),
            ShotgunDamageNumberCapture.IsActiveFireCategory(gearCategoryId, origin, shooterLookup, nestedSameShot),
            shooterLookup, nestedSameShot,
            ShotgunDamageNumberCapture.VolleyState,
            ShotgunDamageNumberCapture.CurrentTarget,
            world.x, world.y, world.z, damage, kind,
            Time.unscaledTime, frame);
        if (decision.NeedsReport) ShotgunDamageNumberCapture.ReportRefusedPellet(decision.Capture, frame);
        if (decision.Route != DamageNumberRoute.Immediate) return decision.Route;
        return AddImmediate(world, damage, kind, preview) ? DamageNumberRoute.Immediate : DamageNumberRoute.Dropped;
    }
    internal static void AddMergedShotgun(Vector3 world, float damage, HitKind kind)
        => AddImmediate(world, damage, kind, false);
    private static bool AddImmediate(Vector3 world, float damage, HitKind kind, bool preview)
    {
        if (Plugin.ShowDamageNumbers == null || !Plugin.ShowDamageNumbers.Value || !Plugin.Enabled.Value) return false;
        string text = FeedbackPolicy.DamageText(damage, kind);
        if (text.Length == 0 || !KillPolicy.IsFinite(world.x) || !KillPolicy.IsFinite(world.y) || !KillPolicy.IsFinite(world.z)) return false;
        while (Active.Count >= FeedbackPolicy.MaxNumbers) Active.RemoveAt(0);
        int sequence = _sequence++;
        Active.Add(new Number { World = world, At = Time.unscaledTime, Side = FeedbackPolicy.NumberSide(sequence), Kind = kind, Text = text, Preview = preview, Damage = damage, FlySeed = sequence });
        return true;
    }
    internal static void Tick(float now)
    {
        // 1.2.19: the pellet volley ends on the existing main-thread tick (silence, known pellet count or
        // the hard time cap), never on a coroutine and never on a per-pellet scan.
        ShotgunDamageNumberCapture.Tick(now);
        float lifetime = Plugin.DamageNumberLifetime != null ? FeedbackPolicy.Clamp(Plugin.DamageNumberLifetime.Value, 0.35f, 1.2f, FeedbackPolicy.NumberLifetime) : FeedbackPolicy.NumberLifetime;
        for (int i = Active.Count - 1; i >= 0; i--) if (now - Active[i].At >= lifetime) Active.RemoveAt(i);
    }
    internal static void Draw()
    {
        if (!Plugin.ShowDamageNumbers.Value || Active.Count == 0) return;
        float opacity = FeedbackPolicy.NumberOpacity(Plugin.DamageNumberOpacity != null ? Plugin.DamageNumberOpacity.Value : FeedbackPolicy.DefaultNumberOpacity);
        // Opacity 0 must submit nothing at all: no outline pass, no text pass.
        if (opacity <= 0f) return;
        var camera = CameraManager.GetCurrentCamera();
        if (_style == null) _style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, clipping = TextClipping.Overflow, richText = false };
        float weight = FeedbackPolicy.NumberWeight(Plugin.DamageNumberFontWeight != null ? Plugin.DamageNumberFontWeight.Value : FeedbackPolicy.DefaultNumberWeight);
        _style.fontStyle = FeedbackPolicy.NumberUsesBold(weight) ? FontStyle.Bold : FontStyle.Normal;
        float resolution = Mathf.Clamp(Screen.height / 1080f, 0.75f, 1.6f);
        float lifetime = FeedbackPolicy.Clamp(Plugin.DamageNumberLifetime.Value, 0.35f, 1.2f, FeedbackPolicy.NumberLifetime);
        Color previous = GUI.color;
        try
        {
            foreach (var number in Active)
            {
                float age = Time.unscaledTime - number.At;
                if (age < 0 || age >= lifetime) continue;
                Vector3 screen;
                if (number.Preview) screen = new Vector3(Screen.width * 0.5f + number.World.x * resolution, Screen.height * 0.5f, 1f);
                else
                {
                    if (camera == null) continue;
                    screen = camera.WorldToScreenPoint(number.World);
                    if (screen.z <= 0.05f) continue;
                    screen.y = Screen.height - screen.y;
                }
                if (screen.x < 0 || screen.x > Screen.width || screen.y < 0 || screen.y > Screen.height) continue;
                // The single production prismatic decision: the per-hit threshold AND DamageFx/PrismaticEnabled.
                // Either switch off means the number is drawn exactly like a plain one (still tier-tinted).
                bool prismatic = DamageFx.IsPrismatic(number.Damage);
                // DamageNumbers/ApproachEffect, read live so a saved cfg repaints numbers already on screen:
                // false (the default) keeps the first shipped drift-only trajectory, true flies at the viewer.
                bool approachEffect = Plugin.DamageNumberApproachEffect != null && Plugin.DamageNumberApproachEffect.Value;
                // The whole placement (glyph size, drift, flight, alpha) is computed by ONE pure policy call
                // that the locked prismatic tests drive with the same inputs (review A5). Nothing about the
                // flight is re-derived here, so a test can no longer pass against its own copy of the maths.
                // Two flight shapes, both aimed at the viewer (only when ApproachEffect is on):
                //  - prismatic: one shared point above the reticle, dived in from a random bearing;
                //  - every other number: a random line that crosses the body (slightly below the eyes)
                //    and keeps going past it, so it reads as passing through the player.
                FeedbackPolicy.NumberPose(prismatic, approachEffect, number.FlySeed, number.Side, age, lifetime, opacity,
                    screen.x, screen.y, Screen.width, Screen.height, resolution, number.Kind,
                    Plugin.DamageNumberSize.Value, out float x, out float y, out float size, out float alpha,
                    out _, out _);
                _style.fontSize = Mathf.RoundToInt(size);
                var rect = new Rect(x - 100f * resolution, y - size, 200f * resolution, size * 2f);
                DamageTier tier = DamageFx.Tier();
                // DamageFx/PrismaticEnabled, DamageFx/TiersEnabled and DamageNumbers/ApproachEffect are three
                // independent switches; where they meet is asserted as one pure policy call.
                DamageFxPolicy.NumberPaintPlan paint = DamageFxPolicy.Paint(prismatic, DamageFx.TiersEnabled(), tier);
                Color plainOutline = new Color(0.035f, 0.04f, 0.045f, alpha);
                if (paint.TierInk)
                {
                    Color tierTint = DamageFx.TierColor(tier);
                    plainOutline = new Color(tierTint.r, tierTint.g, tierTint.b, alpha * DamageFxPolicy.OutlineStrength(tier));
                }
                float outline = FeedbackPolicy.NumberOutlineRadius(weight) * resolution;
                // Prismatic numbers are drawn as holographic foil, not as a recoloured glyph: the eight
                // rim passes each take a different pastel hue (the foil edge), one extra pass is a
                // rotating white-hot emboss highlight, and the body is a near-white pastel core whose
                // brightness follows the shimmer. Cost: the same eight rim labels plus one emboss.
                float foilPhase = paint.Foil ? DamageFxPolicy.WrapPhase(DamageFx.Phase() + number.Side * 0.01f) : 0f;
                // Foil numbers get two rim rings - the 1.6 px core rim plus a wider, dimmer halo - so the
                // eight pastel hues actually read as a foil edge at HUD sizes instead of a one-pixel fringe.
                // Plain numbers (foil switched off, or below the threshold) keep the single dark/tier ring.
                int rings = paint.Rings;
                for (int ring = 0; ring < rings; ring++)
                {
                    float radius = ring == 0
                        ? outline
                        : DamageFxPolicy.FoilRimHaloRadius * resolution;
                    float ringAlpha = ring == 0 ? DamageFxPolicy.FoilRimAlpha : DamageFxPolicy.FoilRimHaloAlpha;
                    for (int direction = 0; direction < 8; direction++)
                    {
                        DamageFxPolicy.FoilDirection(direction, out int dx, out int dy);
                        if (paint.Foil && (ring != 0 || paint.FoilRim))
                        {
                            DamageFxPolicy.FoilRimColor(direction, foilPhase, out float rimRed, out float rimGreen, out float rimBlue);
                            GUI.color = new Color(rimRed, rimGreen, rimBlue, alpha * ringAlpha);
                        }
                        // A prismatic number still carries its tier: the inner ring keeps the tier colour
                        // and only the halo, the emboss and the body are foil.
                        else GUI.color = plainOutline;
                        GUI.Label(new Rect(rect.x + dx * radius, rect.y + dy * radius, rect.width, rect.height), number.Text, _style);
                    }
                }
                if (paint.Foil)
                {
                    DamageFxPolicy.FoilEmbossOffset(foilPhase, out float embossX, out float embossY);
                    GUI.color = new Color(1f, 1f, 1f, alpha * DamageFxPolicy.FoilEmbossAlpha);
                    GUI.Label(new Rect(rect.x + embossX * resolution, rect.y + embossY * resolution, rect.width, rect.height), number.Text, _style);
                }
                Color color;
                if (paint.Foil)
                {
                    DamageFxPolicy.FoilBodyColor(foilPhase, out float foilRed, out float foilGreen, out float foilBlue);
                    color = new Color(foilRed, foilGreen, foilBlue);
                }
                // A tier replaces the body colour of EVERY number - normal, weakspot and armour alike -
                // so the whole damage readout switches to one tier palette. Weakspot and armour are
                // still told apart by their "!" / "[ ]" decoration and their different font scale.
                else if (paint.TierInk) color = DamageFx.TierColor(tier);
                else if (number.Kind == HitKind.Weakspot) color = ResolveWeakspotColor();
                else if (number.Kind == HitKind.Armor) color = new Color(0.65f, 0.72f, 0.77f);
                else color = KillHud.Ink;
                GUI.color = new Color(color.r, color.g, color.b, FeedbackPolicy.NumberTextAlpha(color.a, alpha));
                GUI.Label(rect, number.Text, _style);
                // Weight above 1 stamps the body again on a small circle: the stroke itself gets thicker
                // instead of only its shadow, and weight 1 adds nothing (the shipped look is unchanged).
                int thicken = FeedbackPolicy.NumberThickenPasses(weight);
                if (thicken > 0)
                {
                    float thickenRadius = FeedbackPolicy.NumberThickenRadius(weight) * resolution;
                    for (int stamp = 0; stamp < thicken; stamp++)
                    {
                        float angle = (stamp / (float)thicken) * Mathf.PI * 2f;
                        GUI.Label(new Rect(rect.x + Mathf.Cos(angle) * thickenRadius, rect.y + Mathf.Sin(angle) * thickenRadius, rect.width, rect.height), number.Text, _style);
                    }
                }
            }
        }
        finally { GUI.color = previous; }
    }
    internal static void Preview()
    {
        Add(new Vector3(-90, 0, 0), 6.75f, HitKind.Armor, true);
        Add(Vector3.zero, 24.35f, HitKind.Normal, true);
        Add(new Vector3(90, 0, 0), 81.2f, HitKind.Weakspot, true);
    }
    internal static void Clear() => Active.Clear();
}
