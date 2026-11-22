using System;
using UnityEngine;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace HawChow.GTFOKillFeed;

internal static class CorpseEngineBindings
{
    // Both setters matter: even a short-lived (kinematic, ContinuousDynamic) pair causes a Unity warning.
    internal static void ResetMotion(Rigidbody body, bool kinematic, CollisionDetectionMode mode)
    {
        if (kinematic)
        {
            if (mode != CollisionDetectionMode.ContinuousSpeculative) mode = CollisionDetectionMode.Discrete;
            if (body.collisionDetectionMode != mode) body.collisionDetectionMode = mode;
            if (!body.isKinematic)
            {
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
            }
        }
        else
        {
            if (body.isKinematic) body.isKinematic = false;
            if (body.collisionDetectionMode != mode) body.collisionDetectionMode = mode;
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
    }

    // The generated two-argument instance wrapper uses IL ldlen on an Il2CppStructArray wrapper,
    // not the actual managed PCM array. Use the four-argument binding with an explicit frame count.
    internal static void FillBurst(AudioClip clip, Il2CppStructArray<float> buffer, int frames, int channels, int rate)
    {
        if (clip == null || buffer == null || frames <= 0 || channels != 1 || buffer.Length != frames ||
            clip.samples != frames || clip.channels != channels || clip.frequency != rate)
            throw new InvalidOperationException("Corpse burst PCM dimensions do not match the Unity clip.");
        if (!AudioClip.SetData(clip, buffer, frames, 0))
            throw new InvalidOperationException("Unity rejected the corpse burst PCM upload.");
    }
}
