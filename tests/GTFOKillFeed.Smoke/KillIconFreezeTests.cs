using System;
using System.Reflection;
using HawChow.GTFOKillFeed;

// The head-top kill icon anchor is FROZEN at the lethal credit (1.2.16). These assertions drive the very
// store the plugin uses (KillIconQueue, Unity-free) through the very per-frame step the plugin calls
// (KillIconHud.Tick -> KillIconQueue.TickReal/TickPreview), so the contract is proven on production code
// instead of on a test-side re-implementation:
//   * a tick can only REMOVE expired slots - it has no world-position input at all, so an anchor written at
//     the credit cannot move with the corpse;
//   * a pooled enemy that reuses the same memory (same stored pointer) cannot pull an older icon onto itself;
//   * Reset clears everything, and the kill token keeps pointing at the same frozen point.
internal static class KillIconFreezeTests
{
    private static KillIconSlot Slot(IntPtr pointer, float x, float y, float z, KillPresentation presentation, float at, int token)
        => new KillIconSlot
        {
            Pointer = pointer,
            X = x,
            Y = y,
            Z = z,
            Clearance = 0.3f,
            Tier = presentation.Tier,
            Flight = presentation.Flight,
            At = at,
            Token = token,
        };

    internal static void Run(Action<bool, string> check)
    {
        // ---- the anchor a kill captured is never rewritten by ticking -------------------------------
        var queue = new KillIconQueue(KillIconPolicy.MaxIconsCeiling);
        var presentation = new KillPresentation(KillKind.Weakspot, KillTier.Elite, KillFlight.Flying);
        queue.PushReal(Slot(new IntPtr(0x1234), 12.5f, 3.25f, -8.75f, presentation, 0f, 7), KillIconPolicy.MaxIcons(6));
        // The icon lives 2.2 s; tick across its whole life in 1/60 s steps, i.e. exactly what Update does.
        bool unchanged = true;
        for (int frame = 1; frame <= 120; frame++)
        {
            queue.TickReal(frame / 60f, KillIconPolicy.DefaultLifetimeSeconds);
            if (queue.RealCount != 1) { unchanged = false; break; }
            KillIconSlot slot = queue.RealAt(0);
            if (slot.X != 12.5f || slot.Y != 3.25f || slot.Z != -8.75f) { unchanged = false; break; }
        }
        check(unchanged, "kill-icon freeze: 120 Update ticks (2 s, inside the 2.2 s lifetime) never move the credited anchor");

        KillIconSlot afterTicks = queue.RealAt(0);
        check(afterTicks.Tier == KillTier.Elite && afterTicks.Flight == KillFlight.Flying
            && afterTicks.Pointer == new IntPtr(0x1234),
            "kill-icon freeze: ticking keeps the tier/flight and the audit pointer exactly as credited");

        // Expiry is the ONLY thing a tick can do.
        check(queue.TickReal(2.19f, KillIconPolicy.DefaultLifetimeSeconds) == 0 && queue.RealCount == 1,
            "kill-icon freeze: the slot survives until its lifetime is over");
        check(queue.TickReal(2.2f, KillIconPolicy.DefaultLifetimeSeconds) == 1 && queue.RealCount == 0,
            "kill-icon freeze: the slot is removed exactly at the end of its lifetime");

        // ---- a pooled enemy that reuses the same memory cannot steal the older icon ------------------
        var pool = new KillIconQueue(KillIconPolicy.MaxIconsCeiling);
        var pooledPointer = new IntPtr(0x5000);
        pool.PushReal(Slot(pooledPointer, 100f, 5f, 100f, new KillPresentation(KillKind.Normal, KillTier.Minor, KillFlight.None), 0f, 11), 6);
        // Same pointer, different death, different world point: the object was pooled and reused.
        pool.PushReal(Slot(pooledPointer, -40f, 1f, 62f, new KillPresentation(KillKind.Weakspot, KillTier.Boss, KillFlight.ArmoredFlying), 1f, 12), 6);
        bool crossed = false;
        for (int frame = 0; frame <= 60; frame++)
        {
            pool.TickReal(1f + frame / 60f, KillIconPolicy.DefaultLifetimeSeconds);
            if (pool.RealCount != 2) { crossed = true; break; }
            if (pool.RealAt(0).X != 100f || pool.RealAt(0).Y != 5f || pool.RealAt(0).Z != 100f) { crossed = true; break; }
            if (pool.RealAt(1).X != -40f || pool.RealAt(1).Y != 1f || pool.RealAt(1).Z != 62f) { crossed = true; break; }
        }
        check(!crossed, "kill-icon freeze: two icons sharing one pooled pointer keep their own frozen anchors (no cross-talk)");
        check(pool.TryFind(11, out int oldIndex, out _) && pool.RealAt(oldIndex).X == 100f
            && pool.TryFind(12, out int newIndex, out _) && pool.RealAt(newIndex).X == -40f,
            "kill-icon freeze: the kill token still resolves to the anchor that kill captured");

        // ---- Reset clears the store and invalidates every token -------------------------------------
        pool.Clear();
        check(pool.Count == 0 && !pool.TryFind(11, out _, out _) && !pool.TryFind(12, out _, out _),
            "kill-icon freeze: Reset clears the store and every kill token with it");

        // ---- previews expire on the same clock and never touch the real store ------------------------
        var previewQueue = new KillIconQueue(KillIconPolicy.MaxIconsCeiling);
        previewQueue.PushReal(Slot(IntPtr.Zero, 1f, 2f, 3f, new KillPresentation(KillKind.Normal, KillTier.Minor, KillFlight.None), 0f, 21), 6);
        previewQueue.PushPreview(Slot(IntPtr.Zero, 9f, 9f, 9f, new KillPresentation(KillKind.Normal, KillTier.Boss, KillFlight.None), 0f, 22));
        previewQueue.TickPreview(0.5f, KillIconPolicy.DefaultLifetimeSeconds);
        check(previewQueue.RealCount == 1 && previewQueue.PreviewCount == 1,
            "kill-icon freeze: a preview tick never touches the real kill store");
        previewQueue.TickPreview(2.2f, KillIconPolicy.DefaultLifetimeSeconds);
        check(previewQueue.PreviewCount == 0 && previewQueue.RealCount == 1,
            "kill-icon freeze: the preview set expires on the same clamped lifetime");

        // A broken clock removes nothing (never a phantom expiry).
        check(previewQueue.TickReal(float.NaN, KillIconPolicy.DefaultLifetimeSeconds) == 0 && previewQueue.RealCount == 1
            && previewQueue.TickPreview(float.NaN, KillIconPolicy.DefaultLifetimeSeconds) == 0,
            "kill-icon freeze: a broken clock never expires an icon");

        // The lifetime handed to the tick is clamped exactly like the draw path clamps it.
        var clampQueue = new KillIconQueue(4);
        clampQueue.PushReal(Slot(IntPtr.Zero, 0f, 0f, 0f, new KillPresentation(KillKind.Normal, KillTier.Minor, KillFlight.None), 0f, 31), 4);
        clampQueue.TickReal(KillIconPolicy.MaxLifetimeSeconds - 0.01f, 999f);
        bool clampedLifetime = clampQueue.RealCount == 1;
        clampQueue.TickReal(KillIconPolicy.MaxLifetimeSeconds, 999f);
        check(clampedLifetime && clampQueue.RealCount == 0,
            "kill-icon freeze: the tick uses the clamped lifetime, so a cfg value of 999 s still ends at 6 s");

        // ---- the anchor SOURCES the fix allows, and the pip that is gone for good -------------------
        check(KillIconPolicy.AnchorSourceHead == "head-world" && KillIconPolicy.AnchorSourceLethal == "lethal-position"
            && KillIconPolicy.AnchorSourceHead != KillIconPolicy.AnchorSourceLethal,
            "kill-icon freeze: a frozen anchor is always attributed to the head point or to the lethal event point");
        // The amber weakspot pip above the icon was removed in 1.2.16 (the card title already says
        // WEAKPOINT KILL). A reflection guard here, plus the type-level IL gate, keeps it from coming back.
        check(typeof(KillIconPolicy).GetMember("HasWeakspotMark", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Length == 0
            && typeof(KillIconPolicy).GetMember("DefaultWeakspotMarkColor", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Length == 0,
            "kill-icon secondary mark: the weakspot pip is gone from the head icon (weakspot lives in the card title only)");
        // The icon FAMILY never depends on the classification: every tier maps to exactly one raster no
        // matter which kind was recorded (this is the card/head-icon disagreement of 1.2.15).
        bool familyStable = true;
        foreach (KillTier tier in new[] { KillTier.Minor, KillTier.Elite, KillTier.Boss })
        {
            string expected = tier == KillTier.Minor ? "kill-normal" : tier == KillTier.Elite ? "kill-weakspot" : "kill-heavy";
            foreach (KillKind kind in new[] { KillKind.Normal, KillKind.Weakspot })
            {
                var combo = new KillPresentation(kind, tier, KillFlight.None);
                familyStable &= KillIconPolicy.TierIconKey(combo.Tier) == expected;
                familyStable &= KillIconPolicy.TierIconFile(combo.Tier) == expected + ".png";
            }
        }
        check(familyStable, "kill-icon + card: one tier -> raster mapping (light/medium/heavy) regardless of the classification");
    }
}
