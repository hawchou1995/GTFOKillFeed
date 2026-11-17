using System;
using HawChow.GTFOKillFeed;

// 1.2.19 fix: the corpse launch HEADING and the pitch receipt.
//
// CorpseFeedback.Schedule used to flatten the death direction itself and, when the horizontal part collapsed, replace it
// with world +Z - a heading with no relation to the player's facing. These fixtures drive the real
// FeedbackPolicy.LaunchHeading / TryLaunchPitchDegrees chain for the aiming cases a player actually produces (level,
// up, down, almost straight down) plus review R2 (no camera at all must NOT claim `heading=camera-yaw`) and review R5
// (the pitch is measured on the vector the game handed us, and says `unavailable` when there is none).
internal static class CorpseHeadingTests
{
    private const float Epsilon = 0.0005f;

    internal static void Run(Action<bool, string> check)
    {
        // ---- 1) level shot: the damage direction names the heading --------------------------------
        var level = FeedbackPolicy.LaunchHeading(1f, 0f, true, 0f, -1f, out float x, out float z);
        check(level == LaunchHeadingSource.Damage && Near(x, 1f) && Near(z, 0f)
            && FeedbackPolicy.TryLaunchPitchDegrees(1f, 0f, 0f, out float levelPitch) && Near(levelPitch, 0f)
            && FeedbackPolicy.PitchText(true, levelPitch) == "0",
            "level aim: the death direction decides the heading and the receipt reports pitch 0");

        // ---- 2) looking DOWN / UP: the horizontal part still names the heading --------------------
        var down = FeedbackPolicy.LaunchHeading(0.5f, 0f, true, 0f, -1f, out x, out z);
        check(down == LaunchHeadingSource.Damage && Near(x, 1f) && Near(z, 0f)
            && FeedbackPolicy.TryLaunchPitchDegrees(0.5f, -0.8660254f, 0f, out float downPitch)
            && Near(downPitch, -60f) && FeedbackPolicy.PitchText(true, downPitch) == "-60",
            "looking down 60 deg: the heading is still the direction the shot travelled and the receipt reports -60 deg");
        var up = FeedbackPolicy.LaunchHeading(0.5f, 0f, true, 1f, 0f, out x, out z);
        check(up == LaunchHeadingSource.Damage && Near(x, 1f) && Near(z, 0f)
            && FeedbackPolicy.TryLaunchPitchDegrees(0.5f, 0.8660254f, 0f, out float upPitch) && Near(upPitch, 60f),
            "looking up 60 deg: the heading stays the horizontal part of the shot and the pitch is reported as +60 deg");

        // ---- 3) NEAR-VERTICAL: the collapse no longer jumps to world +Z ---------------------------
        var nearVertical = FeedbackPolicy.LaunchHeading(0.0958f, 0f, true, 0f, -1f, out x, out z);
        check(nearVertical == LaunchHeadingSource.CameraYaw && Near(x, 0f) && Near(z, -1f),
            "1.2.19 fix: an almost-vertical hit (84.5 deg down) uses the camera YAW heading instead of world +Z");
        var nearVerticalFacingX = FeedbackPolicy.LaunchHeading(0.0958f, 0f, true, 1f, 0f, out x, out z);
        check(nearVerticalFacingX == LaunchHeadingSource.CameraYaw && Near(x, 1f) && Near(z, 0f),
            "1.2.19 fix: the same 84.5 deg hit while facing +X flies along +X - the old code always produced world +Z");
        var vertical = FeedbackPolicy.LaunchHeading(0f, 0f, true, 0f, -1f, out x, out z);
        check(vertical == LaunchHeadingSource.CameraYaw && Near(x, 0f) && Near(z, -1f),
            "straight down (90 deg): the heading comes from the camera yaw, never from a constant");

        // ---- 4) review R2: no camera means world-forward, never a fake "camera-yaw" --------------
        // The adapter used to hand a +Z vector in as the yaw even when CameraManager returned null, so the receipt claimed
        // a camera heading that did not exist. `cameraAvailable` is that fact and it is part of the same production call.
        var noCamera = FeedbackPolicy.LaunchHeading(0f, 0f, false, 1f, 0f, out x, out z);
        check(noCamera == LaunchHeadingSource.WorldForward && Near(x, 0f) && Near(z, 1f),
            "R2: an almost-vertical hit with NO camera ends at world-forward - the camera-available flag cannot be faked by a vector");
        var noCameraButDamageHeading = FeedbackPolicy.LaunchHeading(1f, 0f, false, 0f, -1f, out x, out z);
        check(noCameraButDamageHeading == LaunchHeadingSource.Damage && Near(x, 1f) && Near(z, 0f),
            "R2: a real death direction still names the heading without a camera");
        var blind = FeedbackPolicy.LaunchHeading(0f, 0f, false, 0f, 0f, out x, out z);
        check(blind == LaunchHeadingSource.WorldForward && Near(x, 0f) && Near(z, 1f),
            "with neither a death direction nor a camera the heading falls back to the legacy world +Z (nothing regresses)");
        var nan = FeedbackPolicy.LaunchHeading(float.NaN, float.NaN, true, 1f, 0f, out x, out z);
        check(nan == LaunchHeadingSource.CameraYaw && Near(x, 1f) && Near(z, 0f),
            "a non-finite death direction is not a heading: the camera yaw is used instead of producing NaN");
        var allNaN = FeedbackPolicy.LaunchHeading(float.NaN, float.NaN, true, float.PositiveInfinity, float.NaN, out x, out z);
        check(allNaN == LaunchHeadingSource.WorldForward && Near(x, 0f) && Near(z, 1f),
            "a non-finite camera yaw also falls back to world +Z");

        // ---- 5) every case returns a unit horizontal vector ---------------------------------------
        bool unit = true;
        foreach (float dx in new[] { -3f, -0.099f, 0f, 0.099f, 0.5f, 3f })
            foreach (float dz in new[] { -3f, -0.099f, 0f, 0.099f, 0.5f, 3f })
            {
                FeedbackPolicy.LaunchHeading(dx, dz, true, 0.2f, -0.9f, out x, out z);
                unit &= Math.Abs(x * x + z * z - 1f) < 0.0001f;
            }
        check(unit, "the launch heading is always a normalised horizontal vector (36 direction pairs)");

        // ---- 6) review R5: the pitch measures the RAW vector, and says unavailable when it cannot --
        check(!FeedbackPolicy.TryLaunchPitchDegrees(0f, 0f, 0f, out float zeroPitch)
            && FeedbackPolicy.PitchText(false, zeroPitch) == "unavailable",
            "R5: a zero death direction reports pitch=unavailable instead of pretending the corpse was hit at 0 deg");
        check(!FeedbackPolicy.TryLaunchPitchDegrees(float.NaN, 1f, 0f, out _)
            && FeedbackPolicy.PitchText(false, 0f) == "unavailable",
            "R5: a non-finite death direction reports pitch=unavailable");
        check(FeedbackPolicy.TryLaunchPitchDegrees(0f, -1f, 0f, out float straightDown) && Near(straightDown, -90f)
            && FeedbackPolicy.TryLaunchPitchDegrees(0f, -0.9954f, 0.0958f, out float steep) && steep < -84f
            && FeedbackPolicy.LaunchPitchDegrees(0f, 1f, 0f) == 90f,
            "R5: the reported pitch is the angle of the raw direction (-90 straight down, steeper than -84 for a near-vertical hit)");

        // ---- 7) the receipt fields name the rung --------------------------------------------------
        check(FeedbackPolicy.HeadingText(LaunchHeadingSource.Damage) == "damage"
            && FeedbackPolicy.HeadingText(LaunchHeadingSource.CameraYaw) == "camera-yaw"
            && FeedbackPolicy.HeadingText(LaunchHeadingSource.WorldForward) == "world-forward",
            "the schedule receipt names which fallback rung produced the heading");
    }

    private static bool Near(float a, float b) => Math.Abs(a - b) <= Epsilon;
}
