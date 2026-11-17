using System;
using HawChow.GTFOKillFeed;

// Damage-number opacity. These assertions call the exact production policy methods the renderer
// uses (FeedbackPolicy.NumberOpacity / NumberAlpha / NumberTextAlpha), because the smoke project
// links the real FeedbackPolicy.cs source - so the shipped maths is what gets measured, not a copy.
internal static class DamageNumberOpacityTests
{
    internal static void Run(Action<bool, string> check)
    {
        const float lifetime = FeedbackPolicy.NumberLifetime;
        // Alpha(age, 0.72) is clamped to 1 until ~0.5 s, so the interesting fade values live here.
        float[] ages = { 0f, 0.2f, 0.6f, 0.65f, 0.7f, 0.715f };
        float[] colourAlphas = { 1f, 128f / 255f, 0.35f };

        // ---- 1.0 is not "close to" the 1.2.10 algorithm: it is bit-identical -------------------
        bool defaultIsOne = FeedbackPolicy.DefaultNumberOpacity == 1f && FeedbackPolicy.NumberOpacity(1f) == 1f;
        bool outlineIdentical = true, textIdentical = true;
        foreach (float age in ages)
        {
            float fade = FeedbackPolicy.Alpha(age, lifetime);
            outlineIdentical &= FeedbackPolicy.NumberAlpha(age, lifetime, 1f) == fade;
            foreach (float colourAlpha in colourAlphas)
            {
                textIdentical &= FeedbackPolicy.NumberTextAlpha(colourAlpha, FeedbackPolicy.NumberAlpha(age, lifetime, 1f))
                    == colourAlpha * fade;
            }
        }
        check(defaultIsOne && outlineIdentical,
            "opacity: the default 1.0 reproduces the previous fade for every outline alpha bit for bit");
        check(textIdentical,
            "opacity: at 1.0 the text alpha still equals colour alpha times the old fade exactly (RGBA colours behave as in the previous release)");

        // ---- 0.5 halves the existing fade, exactly once ---------------------------------------
        float midFade = FeedbackPolicy.Alpha(0.65f, lifetime);
        check(midFade > 0.2f && midFade < 0.5f, "opacity: the mid-life fade used by the half-opacity case is in range (test precondition)");
        check(FeedbackPolicy.NumberOpacity(0.5f) == 0.5f,
            "opacity: 0.5 survives the policy unmodified");
        float halfAlpha = FeedbackPolicy.NumberAlpha(0.65f, lifetime, 0.5f);
        check(halfAlpha == midFade * 0.5f,
            "opacity: 0.5 multiplies the existing fade-out exactly once, so the original fade is preserved");
        float halfText = FeedbackPolicy.NumberTextAlpha(128f / 255f, halfAlpha);
        check(halfText == (128f / 255f) * (midFade * 0.5f)
            && Math.Abs(halfText - (128f / 255f) * midFade * 0.25f) > 0.001f,
            "opacity: the weakspot colour alpha and the total opacity are each applied once, never twice");
        check(halfAlpha < midFade && halfAlpha > FeedbackPolicy.NumberAlpha(0.7f, lifetime, 0.5f),
            "opacity: a half-transparent number still fades out along its own lifetime curve");

        // ---- 0 draws absolutely nothing --------------------------------------------------------
        check(!FeedbackPolicy.ShowsNumber(0f)
            && FeedbackPolicy.NumberAlpha(0.65f, lifetime, 0f) == 0f
            && FeedbackPolicy.NumberTextAlpha(1f, FeedbackPolicy.NumberAlpha(0.65f, lifetime, 0f)) == 0f
            && FeedbackPolicy.NumberTextAlpha(128f / 255f, FeedbackPolicy.NumberAlpha(0.65f, lifetime, 0f)) == 0f,
            "opacity: 0 yields a zero outline alpha and a zero text alpha, so no text and no outline survives");
        check(FeedbackPolicy.ShowsNumber(0.01f) && FeedbackPolicy.ShowsNumber(1f) && FeedbackPolicy.ShowsNumber(128f / 255f),
            "opacity: any non-zero value keeps drawing");

        // ---- out-of-range values are clamped --------------------------------------------------
        check(FeedbackPolicy.NumberOpacity(2f) == 1f && FeedbackPolicy.NumberOpacity(1.0001f) == 1f
            && FeedbackPolicy.NumberOpacity(float.MaxValue) == 1f,
            "opacity: values above 1 are clamped to fully opaque instead of brightening or hiding the number");
        check(FeedbackPolicy.NumberOpacity(-0.01f) == 0f && FeedbackPolicy.NumberOpacity(-5f) == 0f
            && FeedbackPolicy.NumberOpacity(float.MinValue) == 0f,
            "opacity: negative values are clamped to 0 instead of raising the alpha");

        // ---- non-finite values fall back to the safe default ----------------------------------
        float nanAlpha = FeedbackPolicy.NumberAlpha(0.65f, lifetime, float.NaN);
        check(FeedbackPolicy.NumberOpacity(float.NaN) == FeedbackPolicy.DefaultNumberOpacity
            && FeedbackPolicy.NumberOpacity(float.PositiveInfinity) == FeedbackPolicy.DefaultNumberOpacity
            && FeedbackPolicy.NumberOpacity(float.NegativeInfinity) == FeedbackPolicy.DefaultNumberOpacity,
            "opacity: NaN and +/-Infinity fall back to the safe default 1.0 instead of throwing or blanking the HUD");
        check(nanAlpha == midFade && FeedbackPolicy.NumberAlpha(0.65f, lifetime, float.PositiveInfinity) == midFade,
            "opacity: the non-finite fallback restores the untouched previous fade");

        // ---- one multiplier for the text and all eight outline passes -------------------------
        float sharedOutline = FeedbackPolicy.NumberAlpha(0.65f, lifetime, 0.5f);
        float opaqueText = FeedbackPolicy.NumberTextAlpha(1f, sharedOutline);
        check(opaqueText == sharedOutline,
            "opacity: an opaque colour makes the text alpha equal the shared outline alpha, so text and all 8 outline labels fade together");
        check(FeedbackPolicy.NumberTextAlpha(0f, sharedOutline) == 0f,
            "opacity: a fully transparent RGBA colour stays transparent regardless of the opacity multiplier");

        // ---- the opacity never leaks into the number's own content or motion ------------------
        check(FeedbackPolicy.DamageText(81.2f, HitKind.Weakspot) == "81.2!"
            && FeedbackPolicy.DamageText(6.75f, HitKind.Armor) == "[6.75]"
            && FeedbackPolicy.NumberScale(0.07f, HitKind.Weakspot) > FeedbackPolicy.NumberScale(0.07f, HitKind.Normal)
            && FeedbackPolicy.NumberLifetime == 0.72f,
            "opacity: weakspot/armor text, the pop scale and the 0.72 s lifetime are untouched by the opacity");
    }
}
