using System;
using Enemies;
using UnityEngine;

namespace HawChow.GTFOKillFeed;

// The BFV kill icon above a dead enemy's head (default presentation) plus the small flight badge. Ticked and
// drawn by KillHud, so F7 and General/Enabled keep gating it exactly like the cards.
//
// World anchoring (1.2.16): the anchor is captured ONCE, at the local lethal credit, while the enemy is still
// the object that died, and it is FROZEN for the rest of the icon's life. Nothing here re-reads the dying
// model per frame any more: 1.2.15 followed the live `m_headLimb` for the first 60% of the icon's life, so a
// corpse sinking to the floor dragged its icon down with it. The BFV source mod does no world following at
// all - its hit/kill cues are screen-constant anchors that only animate alpha and shrink - so "freeze at
// death" is the faithful behaviour here. Only the projection is re-evaluated per frame, which is what makes
// the icon stay glued to the world point when the camera moves (that is reprojection, not following).
//
// The slot keeps the pointer of the enemy that died as an audit identity, but it is never read back: a pooled
// enemy that reuses the same memory therefore cannot move, inherit or steal this icon.
//
// Identity and fallback: every real kill gets a never-reused token, which the kill card carries. KillHud
// asks IsPresenting(token) before it paints a card, so a kill is presented by EXACTLY one of the two, and a
// kill whose icon cannot be created or drawn still gets its card instead of ending up as sound only.
internal static class KillIconHud
{
    private static readonly KillIconQueue Queue = new(KillIconPolicy.MaxIconsCeiling);

    private static readonly IconTextureCache Icons = new();
    private static readonly HexColorCache WingColor = new();
    private static readonly HexColorCache PlateColor = new();
    private static string _root, _lightPath, _mediumPath, _heavyPath;
    private static Texture2D _light, _medium, _heavy;
    private static bool _iconsLogged, _frozenLogged, _fallbackLogged;
    private static int _warnings;
    private static int _shown;

    internal static int ActiveCount => Queue.Count;
    internal static int Shown => _shown;
    internal static int RealCount => Queue.RealCount;
    internal static int PreviewCount => Queue.PreviewCount;

    private static readonly ScreenBox[] Occluders = new ScreenBox[KillIconOccluders.MaxCandidates];
    private static bool _occludersComplete;
    private static int _occluderCount;
    private static readonly int[] HiddenTokens = new int[32];
    private static int _hiddenCount;
    internal static void Reset(string reason)
    {
        Queue.Clear();
        Plugin.Instance?.Log.LogInfo("[KILLICON] cleared (" + reason + ")");
    }

    // Called once per credited local kill, from the same place that already built the KillPresentation, so
    // the icon consumes exactly the tier/flight the kill cue consumed; the anchor handed in here is the
    // one and only world point this icon will ever have: it is frozen at this moment (the enemy is still the
    // object that just died) and the caller records where it came from.
    // Returns the slot token (> 0) or 0 when no icon exists for this kill, which the card reads as
    // "present this kill yourself".
    internal static int Add(EnemyAgent agent, Vector3 anchor, KillPresentation presentation, float clearance,
        float now, string anchorSource)
    {
        if (!KillPolicy.IsFinite(anchor.x) || !KillPolicy.IsFinite(anchor.y) || !KillPolicy.IsFinite(anchor.z)) return 0;
        IntPtr pointer = IntPtr.Zero;
        try { if (agent != null) pointer = agent.Pointer; }
        catch (Exception) { pointer = IntPtr.Zero; }
        int token = Push(pointer, anchor, clearance, presentation, now, false);
        if (token > 0) LogFrozenAnchor(anchorSource, presentation, clearance);
        return token;
    }

    // One-time receipts (bounded: two lines per session). They exist so a real-machine log can be reconciled
    // without a frame-by-frame trace: where the frozen anchor came from, and the fact that it never moves.
    private static void LogFrozenAnchor(string source, KillPresentation presentation, float clearance)
    {
        string resolved = string.IsNullOrWhiteSpace(source) ? KillIconPolicy.AnchorSourceHead : source;
        if (!_frozenLogged)
        {
            _frozenLogged = true;
            Plugin.Instance?.Log.LogInfo("[KILLICON] anchor=frozen;when=credit;source=" + resolved
                + ";tier=" + presentation.Tier + ";flight=" + presentation.Flight + ";kind=" + presentation.Kind
                + ";clearance=" + clearance.ToString("0.00") + "m;follow=none;projection=per-frame;budget=once");
            return;
        }
        if (resolved == KillIconPolicy.AnchorSourceHead || _fallbackLogged) return;
        _fallbackLogged = true;
        Plugin.Instance?.Log.LogInfo("[KILLICON] anchor=fallback;source=" + resolved
            + ";reason=head-unavailable-at-credit;note=lethal-event-point,never-screen-centre,budget=once");
    }

    // F9 preview: the nine (tier x flight) combinations in front of the camera, so the shapes can be checked
    // without a kill. The preview set REPLACES itself (fixed nine slots), so a repeated key press can never
    // stack a second copy and a preview can never evict a real kill (review F5).
    internal static void AddPreview(float now)
    {
        Camera camera = CameraManager.GetCurrentCamera();
        if (camera == null) return;
        try
        {
            Transform transform = camera.transform;
            Vector3 forward = transform.forward, right = transform.right, up = transform.up;
            Vector3 origin = transform.position + forward * 6f;
            Queue.ResetPreviews();
            for (int slot = 0; slot < KillIconPolicy.PreviewCombos; slot++)
            {
                KillIconPolicy.PreviewCombo(slot, out KillTier tier, out KillFlight flight);
                Vector3 anchor = origin
                    + right * (((int)tier - 1) * 1.15f)
                    + up * (0.35f - (int)flight * 0.35f);
                Push(IntPtr.Zero, anchor, 0f, new KillPresentation(KillKind.Normal, tier, flight), now, true);
            }
        }
        catch (Exception error) { Warn("preview", error.GetType().Name); }
    }

    private static int Push(IntPtr pointer, Vector3 anchor, float clearance, KillPresentation presentation,
        float now, bool preview)
    {
        var slot = new KillIconSlot
        {
            Pointer = pointer,
            X = anchor.x,
            Y = anchor.y,
            Z = anchor.z,
            Clearance = clearance,
            Tier = presentation.Tier,
            Flight = presentation.Flight,
            At = now,
            Preview = preview,
            Token = Queue.NextToken(),
        };
        if (preview)
        {
            // A preview never competes with a real kill for a slot; if the set is somehow full the extra
            // combination is simply not previewed.
            return Queue.PushPreview(slot) ? slot.Token : 0;
        }
        Queue.PushReal(slot, KillIconPolicy.MaxIcons(Plugin.KillIconMax != null ? Plugin.KillIconMax.Value : KillIconPolicy.DefaultMaxIcons));
        return slot.Token;
    }

    // Is this kill currently drawn by its head icon? Used by the card fallback: the answer is false when the
    // slot is gone (evicted), its tier texture is missing, or it is already past its lifetime, so the card
    // takes over for exactly those kills (review F1).
    internal static bool IsPresenting(int token)
    {
        if (IsHidden(token)) return false;
        if (!Queue.TryFind(token, out int index, out bool preview)) return false;
        KillIconSlot slot = preview ? Queue.PreviewAt(index) : Queue.RealAt(index);
        if (TextureFor(slot.Tier) == null) return false;
        float lifetime = KillIconPolicy.Lifetime(Plugin.KillIconLifetime != null ? Plugin.KillIconLifetime.Value : KillIconPolicy.DefaultLifetimeSeconds);
        return KillIconPolicy.Alpha(Time.unscaledTime - slot.At, lifetime) > 0f;
    }


    // Per-frame step: expire slots and keep the icon textures warm. It takes no enemy, no camera and no
    // position, so a tick can never move a frozen anchor (asserted by the IL gate and by the smoke tests that
    // drive KillIconQueue.TickReal directly - the same production code path).
    internal static void Tick(float now)
    {
        TickIcons(now);
        float lifetime = Plugin.KillIconLifetime != null ? Plugin.KillIconLifetime.Value : KillIconPolicy.DefaultLifetimeSeconds;
        Queue.TickReal(now, lifetime);
        Queue.TickPreview(now, lifetime);
    }

    private static void TickIcons(float now)
    {
        try
        {
            if (_root == null)
            {
                _root = IconTextureCache.IconRoot();
                _lightPath = System.IO.Path.Combine(_root, KillIconPolicy.TierIconFile(KillTier.Minor));
                _mediumPath = System.IO.Path.Combine(_root, KillIconPolicy.TierIconFile(KillTier.Elite));
                _heavyPath = System.IO.Path.Combine(_root, KillIconPolicy.TierIconFile(KillTier.Boss));
            }
            Icons.Tick(KillIconPolicy.TierIconKey(KillTier.Minor), _lightPath, now);
            Icons.Tick(KillIconPolicy.TierIconKey(KillTier.Elite), _mediumPath, now);
            Icons.Tick(KillIconPolicy.TierIconKey(KillTier.Boss), _heavyPath, now);
            _light = Icons.Texture(KillIconPolicy.TierIconKey(KillTier.Minor));
            _medium = Icons.Texture(KillIconPolicy.TierIconKey(KillTier.Elite));
            _heavy = Icons.Texture(KillIconPolicy.TierIconKey(KillTier.Boss));
            if (_iconsLogged) return;
            _iconsLogged = true;
            Plugin.Instance?.Log.LogInfo("[KILLICON] icon slots registered light=" + HotReloadPolicy.SafeFileName(_lightPath)
                + ";medium=" + HotReloadPolicy.SafeFileName(_mediumPath)
                + ";heavy=" + HotReloadPolicy.SafeFileName(_heavyPath) + ";anchor=frozen-at-credit");
        }
        catch (Exception error) { Warn("icon tick", error.GetType().Name); }
    }

    private static Texture2D TextureFor(KillTier tier)
        => tier == KillTier.Elite ? _medium : tier == KillTier.Boss ? _heavy : _light;

    internal static void Draw()
    {
        ResetHidden();
        if (Queue.Count == 0) return;
        bool headEnabled = Plugin.KillIconHeadEnabled != null && Plugin.KillIconHeadEnabled.Value;
        Camera camera = CameraManager.GetCurrentCamera();
        if (!KillViewPolicy.HeadVisible(headEnabled, gameVisible: true, cameraReady: camera != null)) return;
        // One candidate collection per frame, shared by every slot.
        _occluderCount = KillIconOccluders.Collect(camera, Occluders, out _occludersComplete);
        float now = Time.unscaledTime;
        float lifetime = KillIconPolicy.Lifetime(Plugin.KillIconLifetime != null ? Plugin.KillIconLifetime.Value : KillIconPolicy.DefaultLifetimeSeconds);
        float resolution = Mathf.Clamp(Screen.height / 1080f, 0.75f, 1.6f);
        float size = KillIconPolicy.Size(Plugin.IconSize != null ? Plugin.IconSize.Value : 46f) * resolution;
        float badge = KillIconPolicy.BadgeSize(size);
        float badgeGap = KillIconPolicy.BadgeGap(size);
        Color previous = GUI.color;
        try
        {
            for (int i = 0; i < Queue.RealCount; i++) DrawSlot(Queue.RealAt(i), camera, now, lifetime, size, badge, badgeGap);
            for (int i = 0; i < Queue.PreviewCount; i++) DrawSlot(Queue.PreviewAt(i), camera, now, lifetime, size, badge, badgeGap);
        }
        finally { GUI.color = previous; }
    }

    private static void DrawSlot(in KillIconSlot slot, Camera camera, float now, float lifetime, float size, float badge, float badgeGap)
    {
        // The configured opacity is multiplied into the lifecycle fade exactly once.
        float alpha = KillViewPolicy.CompositeAlpha(
            KillIconPolicy.Alpha(now - slot.At, lifetime),
            Plugin.KillIconHeadOpacity != null ? Plugin.KillIconHeadOpacity.Value : KillViewPolicy.DefaultOpacity);
        if (alpha <= 0f) return;
        Texture2D texture = TextureFor(slot.Tier);
        if (texture == null) return;
        var anchor = new Vector3(slot.X, slot.Y, slot.Z);
        Vector3 screen;
        try { screen = camera.WorldToScreenPoint(anchor); }
        catch (Exception) { return; }
        float top = Screen.height - screen.y;
        if (!HitMarkerPolicy.IsProjectable(screen.z, screen.x, top, Screen.width, Screen.height, 0f)) return;
        // An incomplete candidate set means the avoidance pass could not see every visible monster
        // this frame; hiding the icon is the safe answer (a card is NOT forced open in its place).
        if (!_occludersComplete) { Hide(slot.Token); return; }
        float badgeExtent = KillIconPolicy.MarkFor(slot.Flight) != FlightMark.None && badge > 1f ? badgeGap + badge : 0f;
        var box = new ScreenBox(screen.x - size * 0.5f, top - size * 0.5f, screen.x + size * 0.5f, top + size * 0.5f + badgeExtent);
        IconPlacement placement = KillViewPolicy.ResolvePlacement(box, Occluders, _occluderCount, 0f, Screen.height);
        if (placement.Hidden) { Hide(slot.Token); return; }
        var rect = new Rect(box.Left, placement.Top, size, size);
        GUI.color = new Color(1f, 1f, 1f, alpha);
        GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit, true);
        DrawFlightMark(slot.Flight, box.CentreX, rect.yMax + badgeGap, badge, alpha);
        _shown++;
    }

    // Shape first: a wing chevron for a plain flyer, a hollow armour plate for an armoured flyer, nothing for
    // a ground kill. The colour repeats the meaning, it never carries it alone.
    internal static void DrawFlightMark(KillFlight flight, float centreX, float top, float badge, float alpha)
    {
        FlightMark mark = KillIconPolicy.MarkFor(flight);
        if (mark == FlightMark.None || badge <= 1f) return;
        Color color = ColorFor(flight);
        GUI.color = new Color(color.r, color.g, color.b, alpha);
        float thickness = Mathf.Max(1f, badge * 0.16f);
        if (mark == FlightMark.ArmorPlate)
        {
            float w = badge, h = badge * 0.72f;
            GUI.DrawTexture(new Rect(centreX - w * 0.5f, top, w, thickness), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(centreX - w * 0.5f, top + h - thickness, w, thickness), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(centreX - w * 0.5f, top, thickness, h), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(centreX + w * 0.5f - thickness, top, thickness, h), Texture2D.whiteTexture);
            return;
        }
        // Wings: a stepped chevron, three bars per side descending towards the centre.
        float stepW = badge * 0.20f;
        float stepH = badge * 0.16f;
        for (int i = 0; i < 3; i++)
        {
            float offsetX = badge * 0.5f - stepW * (i + 1);
            float offsetY = stepH * i;
            GUI.DrawTexture(new Rect(centreX - offsetX - stepW, top + offsetY, stepW, stepH), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(centreX + offsetX, top + offsetY, stepW, stepH), Texture2D.whiteTexture);
        }
    }

    private static Color ColorFor(KillFlight flight)
    {
        if (flight == KillFlight.ArmoredFlying)
        {
            PlateColor.Resolve(Plugin.KillIconArmorFlyerColor != null ? Plugin.KillIconArmorFlyerColor.Value : null, 0.902f, 0.671f, 0.341f, 1f);
            return new Color(PlateColor.Red, PlateColor.Green, PlateColor.Blue, 1f);
        }
        WingColor.Resolve(Plugin.KillIconFlyerColor != null ? Plugin.KillIconFlyerColor.Value : null, 0.561f, 0.827f, 0.910f, 1f);
        return new Color(WingColor.Red, WingColor.Green, WingColor.Blue, 1f);
    }

    private static void ResetHidden() { _hiddenCount = 0; }

    // A head icon that this frame could not place (occluder candidate set incomplete, or no room
    // above the pushed-up position) is hidden for this frame only - the slot itself is untouched.
    private static void Hide(int token)
    {
        if (_hiddenCount < HiddenTokens.Length) HiddenTokens[_hiddenCount++] = token;
    }

    private static bool IsHidden(int token)
    {
        for (int i = 0; i < _hiddenCount; i++) if (HiddenTokens[i] == token) return true;
        return false;
    }

    private static void Warn(string stage, string detail)
    {
        if (_warnings++ < 5) Plugin.Instance?.Log.LogWarning("[KILLICON] " + stage + ": " + detail);
    }
}
