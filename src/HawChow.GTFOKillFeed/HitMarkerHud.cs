using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace HawChow.GTFOKillFeed;

// The BFV hit lines (normal X / weakspot X / armour brackets) and the separate BFV kill line, both drawn at
// the world position the game itself reports for the event. The vanilla crosshair layer is replaced at its
// world-position entry points (CrosshairGuiLayer.ShowHitIndicator / ShowDeathIndicator) and only for the
// events we can actually draw; anything else - friendly fire, no-damage hits, a missing icon, a projection
// outside the screen, the feature switched off, a missing patch - keeps the stock indicator untouched.
public sealed class HitMarkerHud : MonoBehaviour
{
    // Il2CppInterop wraps native objects through this constructor; ClassInjector + AddComponent fail
    // without it (same shape as KillHud / CorpseFeedback).
    public HitMarkerHud(IntPtr pointer) : base(pointer) { }

    internal static HitMarkerHud Instance;

    private struct Marker
    {
        internal Vector3 World;
        internal HitMarkerKind Kind;
        internal float At;
        // The identity of the damage event this line belongs to (a never-reused local impact scope id, 0 when
        // the event could not be tied to one). Only an equal identity may merge two events (review F3).
        internal int Identity;
    }

    // Local damage scopes: published by the KillTracker prefixes (before the original damage call) and
    // consumed by the indicator event that belongs to them. This is the only armour source.
    private readonly ImpactScopeStack _impacts = new();

    private readonly IconTextureCache _icons = new();
    private readonly HexColorCache _normalColor = new();
    private readonly HexColorCache _weakColor = new();
    private readonly HexColorCache _armorColor = new();
    private readonly HexColorCache _killColor = new();
    private string _root, _normalPath, _weakPath, _armorPath, _killPath;
    private Texture2D _normal, _weak, _armor, _kill;
    private bool _iconsLogged;
    private int _warnings;

    private readonly Marker[] _markers = new Marker[HitMarkerPolicy.MaxCapacity];
    private int _count;
    private int _fired;

    internal static bool Enabled()
        => Plugin.HitMarkerEnabled == null || Plugin.HitMarkerEnabled.Value;

    internal static int Fired => Instance != null ? Instance._fired : 0;
    internal static int ActiveMarkers => Instance != null ? Instance._count : 0;

    internal static void Reset(string reason)
    {
        var hud = Instance;
        if (hud == null) return;
        hud._count = 0;
        hud._impacts.Reset();
        Plugin.Instance?.Log.LogInfo("[HITMARKER] cleared (" + reason + ")");
    }

    // ---- local impact scopes ----------------------------------------------------------------------
    // Called from the KillTracker PREFIXES (before the original damage method runs), so the context exists
    // while the game may show its hit indicator from inside that method. Only local sources qualify; a
    // remote hit never publishes a scope, so it can never be mistaken for the local player's armour hit.
    // The token is handed back to the finalizer, which pops it even when the original method throws.
    internal static int BeginImpact(Vector3 world, HitKind kind, bool local)
    {
        var hud = Instance;
        if (hud == null || !local) return 0;
        return hud._impacts.Push(world.x, world.y, world.z, kind, Time.frameCount);
    }

    internal static void EndImpact(int id)
    {
        var hud = Instance;
        if (hud != null) hud._impacts.Pop(id);
    }

    // ---- event entry points (called by the Harmony prefixes) -------------------------------------
    // Returns true only when this exact event was replaced, i.e. when the vanilla indicator may be skipped.
    internal static bool TryShow(Vector3 world, HitMarkerKind kind, int identity)
        => Instance != null && Instance.Submit(world, kind, identity);

    internal static bool TryShowKill(Vector3 world)
    {
        var hud = Instance;
        if (hud == null) return false;
        // The death indicator of a lethal local blow arrives inside the SAME original damage call as the hit
        // indicator, so it resolves the same impact scope id and is therefore provably one event with it.
        int identity = hud._impacts.TryIdentify(Time.frameCount, world.x, world.y, world.z, out int id) ? id : 0;
        return hud.Submit(world, HitMarkerKind.Kill, identity);
    }

    private bool Submit(Vector3 world, HitMarkerKind kind, int identity)
    {
        if (!HitMarkerPolicy.AcceptsSubmission(Plugin.MarkerChannel(kind), kind)) return false;
        if (!TryProject(world, out _)) return false;
        if (!IconReady(kind)) return false;
        HitMarkerAction action = Resolve(world, kind, identity, Time.unscaledTime);
        if (action == HitMarkerAction.Dropped) return false;
        _fired++;
        return true;
    }

    // Queue step: only a marker with the SAME event identity is a candidate for an upgrade/merge. Without an
    // identity (or with a different one) the event becomes its own line, whatever the distance (review F3).
    private HitMarkerAction Resolve(Vector3 world, HitMarkerKind kind, int identity, float now)
    {
        int match = -1;
        if (identity > 0)
        {
            for (int i = 0; i < _count; i++)
            {
                if (_markers[i].Identity != identity) continue;
                match = i;
                break;
            }
        }
        HitMarkerKind existing = match >= 0 ? _markers[match].Kind : HitMarkerKind.None;
        int existingIdentity = match >= 0 ? _markers[match].Identity : 0;
        HitMarkerAction action = HitMarkerPolicy.Resolve(existing, existingIdentity, kind, identity);
        switch (action)
        {
            case HitMarkerAction.Added:
                if (_count >= _markers.Length) EvictOldest();
                _markers[_count++] = new Marker { World = world, Kind = kind, At = now, Identity = identity };
                return action;
            case HitMarkerAction.Upgraded:
            case HitMarkerAction.Merged:
                if (match < 0) return HitMarkerAction.Dropped;
                _markers[match] = new Marker { World = world, Kind = kind, At = now, Identity = identity };
                return action;
            default:
                return HitMarkerAction.Dropped;
        }
    }

    private void EvictOldest()
    {
        int oldest = 0;
        for (int i = 1; i < _count; i++) if (_markers[i].At < _markers[oldest].At) oldest = i;
        for (int i = oldest; i < _count - 1; i++) _markers[i] = _markers[i + 1];
        _count--;
    }

    // The same projection the draw uses, applied at event time: a marker we could not have drawn must not
    // swallow the vanilla indicator. Everything, including the camera lookup, sits inside the guard, because
    // an interop exception here would otherwise escape into the game's own indicator method (review F2).
    private static bool TryProject(Vector3 world, out Vector3 screen)
    {
        screen = default;
        if (!HitMarkerPolicy.IsFinite(world.x) || !HitMarkerPolicy.IsFinite(world.y) || !HitMarkerPolicy.IsFinite(world.z)) return false;
        try
        {
            Camera camera = CameraManager.GetCurrentCamera();
            if (camera == null) return false;
            screen = camera.WorldToScreenPoint(world);
        }
        catch (Exception) { return false; }
        float y = Screen.height - screen.y;
        return HitMarkerPolicy.IsProjectable(screen.z, screen.x, y, Screen.width, Screen.height, 0f);
    }

    // ---- icons -----------------------------------------------------------------------------------
    private bool IconReady(HitMarkerKind kind)
        => kind == HitMarkerKind.Weakspot ? _weak != null
            : kind == HitMarkerKind.Armor ? _armor != null
            : kind == HitMarkerKind.Kill ? _kill != null
            : _normal != null;

    private Texture2D IconFor(HitMarkerKind kind)
        => kind == HitMarkerKind.Weakspot ? _weak
            : kind == HitMarkerKind.Armor ? _armor
            : kind == HitMarkerKind.Kill ? _kill
            : _normal;

    private void Update() => TickIcons(Time.unscaledTime);

    private void TickIcons(float now)
    {
        try
        {
            if (_root == null)
            {
                _root = IconTextureCache.IconRoot();
                _normalPath = System.IO.Path.Combine(_root, HitMarkerPolicy.IconFile(HitMarkerKind.Normal));
                _weakPath = System.IO.Path.Combine(_root, HitMarkerPolicy.IconFile(HitMarkerKind.Weakspot));
                _armorPath = System.IO.Path.Combine(_root, HitMarkerPolicy.IconFile(HitMarkerKind.Armor));
                _killPath = System.IO.Path.Combine(_root, HitMarkerPolicy.IconFile(HitMarkerKind.Kill));
            }
            _icons.Tick(HitMarkerPolicy.IconKey(HitMarkerKind.Normal), _normalPath, now);
            _icons.Tick(HitMarkerPolicy.IconKey(HitMarkerKind.Weakspot), _weakPath, now);
            _icons.Tick(HitMarkerPolicy.IconKey(HitMarkerKind.Armor), _armorPath, now);
            _icons.Tick(HitMarkerPolicy.IconKey(HitMarkerKind.Kill), _killPath, now);
            _normal = _icons.Texture(HitMarkerPolicy.IconKey(HitMarkerKind.Normal));
            _weak = _icons.Texture(HitMarkerPolicy.IconKey(HitMarkerKind.Weakspot));
            _armor = _icons.Texture(HitMarkerPolicy.IconKey(HitMarkerKind.Armor));
            _kill = _icons.Texture(HitMarkerPolicy.IconKey(HitMarkerKind.Kill));
            for (int i = _count - 1; i >= 0; i--)
                if (now - _markers[i].At >= HitMarkerPolicy.Lifetime(_markers[i].Kind)) RemoveAt(i);
            if (_iconsLogged) return;
            _iconsLogged = true;
            Plugin.Instance?.Log.LogInfo("[HITMARKER] icon slots registered normal=" + HotReloadPolicy.SafeFileName(_normalPath)
                + ";weakspot=" + HotReloadPolicy.SafeFileName(_weakPath)
                + ";armor=" + HotReloadPolicy.SafeFileName(_armorPath)
                + ";kill=" + HotReloadPolicy.SafeFileName(_killPath) + ";anchor=world-impact");
        }
        catch (Exception error)
        {
            if (_warnings++ < 5) Plugin.Instance?.Log.LogWarning("[HITMARKER] icon tick failed: " + error.GetType().Name);
        }
    }

    private void RemoveAt(int index)
    {
        for (int i = index; i < _count - 1; i++) _markers[i] = _markers[i + 1];
        _count--;
    }

    private void OnGUI()
    {
        if (Event.current == null || Event.current.type != EventType.Repaint) return;
        if (!Enabled() || _count == 0) return;
        float now = Time.unscaledTime;
        float resolution = Mathf.Clamp(Screen.height / 1080f, 0.75f, 1.6f);
        float baseSize = HitMarkerPolicy.Size(Plugin.HitMarkerSize != null ? Plugin.HitMarkerSize.Value : HitMarkerPolicy.DefaultSize);
        Color previous = GUI.color;
        try
        {
            // Our own HUD, so a camera/interop failure only skips this frame instead of spamming the log.
            Camera camera = CameraManager.GetCurrentCamera();
            if (camera == null) return;
            for (int i = 0; i < _count; i++)
            {
                Marker marker = _markers[i];
                float alpha = HitMarkerPolicy.Alpha(now - marker.At, HitMarkerPolicy.Lifetime(marker.Kind));
                if (!HitMarkerPolicy.ShouldDraw(true, marker.Kind, alpha)) continue;
                Vector3 screen;
                try { screen = camera.WorldToScreenPoint(marker.World); }
                catch (Exception) { continue; }
                float y = Screen.height - screen.y;
                if (!HitMarkerPolicy.IsProjectable(screen.z, screen.x, y, Screen.width, Screen.height, 0f)) continue;
                Texture2D texture = IconFor(marker.Kind);
                if (texture == null) continue;
                float size = baseSize * HitMarkerPolicy.SizeScale(marker.Kind) * resolution;
                var rect = new Rect(screen.x - size * 0.5f, y - size * 0.5f, size, size);
                Color tint = TintFor(marker.Kind);
                GUI.color = new Color(tint.r, tint.g, tint.b, alpha);
                GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit, true);
            }
        }
        finally { GUI.color = previous; }
    }

    // Colour per state: white normal, BFV orange weakspot, cyan brackets for armour, BFV heavy red for the
    // kill line. Read through the same hex cache the damage numbers use, so an invalid value falls back.
    private Color TintFor(HitMarkerKind kind)
    {
        if (kind == HitMarkerKind.Weakspot)
        {
            _weakColor.Resolve(Plugin.HitMarkerWeakspotColor != null ? Plugin.HitMarkerWeakspotColor.Value : null, 1f, 0.549f, 0f, 1f);
            return new Color(_weakColor.Red, _weakColor.Green, _weakColor.Blue, 1f);
        }
        if (kind == HitMarkerKind.Armor)
        {
            _armorColor.Resolve(Plugin.HitMarkerArmorColor != null ? Plugin.HitMarkerArmorColor.Value : null, 0.286f, 0.678f, 0.788f, 1f);
            return new Color(_armorColor.Red, _armorColor.Green, _armorColor.Blue, 1f);
        }
        if (kind == HitMarkerKind.Kill)
        {
            _killColor.Resolve(Plugin.HitMarkerKillColor != null ? Plugin.HitMarkerKillColor.Value : null, 0.725f, 0.157f, 0.157f, 1f);
            return new Color(_killColor.Red, _killColor.Green, _killColor.Blue, 1f);
        }
        _normalColor.Resolve(Plugin.HitMarkerNormalColor != null ? Plugin.HitMarkerNormalColor.Value : null, 1f, 1f, 1f, 1f);
        return new Color(_normalColor.Red, _normalColor.Green, _normalColor.Blue, 1f);
    }

    // ---- patches ---------------------------------------------------------------------------------
    // Patched manually (not through an attribute) so a game build without CrosshairGuiLayer degrades to
    // "vanilla indicator stays visible" instead of taking the whole plugin down with a HarmonyException.
    // The world-position methods are the game's own hit/kill indicator entry points; the old unconditional
    // CrosshairHitIndicator.PlayHit suppression is gone, so an event we cannot replace still reaches it.
    internal static void ApplyPatches(Harmony harmony, ManualLogSource log)
    {
        try
        {
            Type layer = AccessTools.TypeByName("CrosshairGuiLayer");
            if (layer == null)
            {
                log?.LogWarning("[HITMARKER] CrosshairGuiLayer not found; the vanilla indicators stay visible");
                return;
            }
            int patched = 0;
            MethodInfo hit = AccessTools.Method(layer, "ShowHitIndicator", new[] { typeof(Vector3), typeof(bool), typeof(bool), typeof(bool) });
            MethodInfo death = AccessTools.Method(layer, "ShowDeathIndicator", new[] { typeof(Vector3) });
            MethodInfo hitPrefix = typeof(HitMarkerHud).GetMethod(nameof(ShowHitIndicatorPrefix), BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo deathPrefix = typeof(HitMarkerHud).GetMethod(nameof(ShowDeathIndicatorPrefix), BindingFlags.Static | BindingFlags.NonPublic);
            if (hit != null && hitPrefix != null)
            {
                harmony.Patch(hit, prefix: new HarmonyMethod(hitPrefix));
                patched++;
            }
            else log?.LogWarning("[HITMARKER] CrosshairGuiLayer.ShowHitIndicator missing; hit lines keep the vanilla indicator");
            if (death != null && deathPrefix != null)
            {
                harmony.Patch(death, prefix: new HarmonyMethod(deathPrefix));
                patched++;
            }
            else log?.LogWarning("[HITMARKER] CrosshairGuiLayer.ShowDeathIndicator missing; the vanilla kill line stays");
            log?.LogInfo(patched == 2
                ? "[HITMARKER] vanilla hit/kill indicators replaced on 2 world-position entry points (anchor=impact)"
                : "[HITMARKER] only " + patched + " of 2 world-position entry points could be replaced; the rest stays vanilla");
        }
        catch (Exception error)
        {
            log?.LogWarning("[HITMARKER] vanilla indicator replacement unavailable (" + error.GetType().Name + "); the stock indicators stay visible");
        }
    }

    // Harmony binds prefix parameters by NAME: these must stay identical to the interop parameter names.
    // The impact scope published by the KillTracker prefix (frame + position + limb kind) is what tells an
    // armour hit apart; noDamage events are replaced ONLY by such a confirmed local armour impact, while
    // friendly fire, an unconfirmed noDamage event, a lost/expired scope or an undrawable marker keep the
    // vanilla indicator.
    //
    // Both prefixes are total (review F2): ANY failure inside our replacement - an interop exception, a
    // broken scope stack, a camera error - returns true, which is Harmony's "run the original method". The
    // event is therefore never swallowed by us; the stock indicator simply stays for that one event.
    private static bool ShowHitIndicatorPrefix(Vector3 worldPos, bool isWeakspot, bool isFriendly, bool noDamage)
    {
        try
        {
            var hud = Instance;
            if (hud == null) return true;
            // The armour verdict is answered once per scope; the same scope still identifies the event, so
            // the paired death indicator of this very damage call resolves the same id (review F3).
            bool matched = hud._impacts.TryClaim(Time.frameCount, worldPos.x, worldPos.y, worldPos.z, noDamage,
                out HitKind scopeKind, out _);
            int identity = hud._impacts.TryIdentify(Time.frameCount, worldPos.x, worldPos.y, worldPos.z, out int impactId)
                ? impactId : 0;
            if (!HitMarkerPolicy.TryDecide(Enabled(), isFriendly, noDamage, matched, scopeKind, isWeakspot, out HitMarkerKind kind)) return true;
            return !TryShow(worldPos, kind, identity);
        }
        catch (Exception error)
        {
            Report("hit-indicator", error.GetType().Name);
            return true;
        }
    }

    private static bool ShowDeathIndicatorPrefix(Vector3 worldPos)
    {
        try { return !TryShowKill(worldPos); }
        catch (Exception error)
        {
            Report("kill-indicator", error.GetType().Name);
            return true;
        }
    }

    // Bounded diagnostics: a repeated failure must not turn into a log flood either.
    private static void Report(string stage, string detail)
    {
        var hud = Instance;
        if (hud == null || hud._warnings++ >= 5) return;
        Plugin.Instance?.Log.LogWarning("[HITMARKER] " + stage + " failed (" + detail + "); that event keeps the vanilla indicator");
    }
}
