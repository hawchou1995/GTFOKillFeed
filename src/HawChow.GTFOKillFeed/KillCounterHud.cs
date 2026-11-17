using System;
using System.Collections.Generic;
using UnityEngine;

namespace HawChow.GTFOKillFeed;

internal static class KillCounterHud
{
    private static readonly KillTier[] Tiers = { KillTier.Minor, KillTier.Elite, KillTier.Boss };
    private static readonly string[] Fallback = { "I", "II", "III" };
    private static readonly Color[] Tones = { KillHud.Ink, KillHud.Accent, new(0.83f, 0.42f, 0.36f) };
    private static readonly HashSet<string> Notes = new();
    private static readonly int[] LastCounts = { -1, -1, -1 };
    private static readonly string[] Texts = { "0", "0", "0" };
    private static readonly float[] Widths = new float[3];
    private static readonly GUIContent[] Contents = { new("0"), new("0"), new("0") };
    private static GUIStyle _style, _fallbackStyle;
    private static PUI_LocalPlayerStatus _status;
    private static float _nextProbe, _right, _bottom, _scale;
    private static bool _measured, _placedLogged;
    private static int _screenWidth, _screenHeight;
    private static string _cameraSource;

    internal static void Reset()
    {
        _status = null; _measured = false; _nextProbe = 0f; _placedLogged = false;
        Notes.Clear();
    }
    internal static void Tick(float now)
    {
        if (GameStateManager.CurrentStateName != eGameStateName.InLevel) { _measured = false; return; }
        if (now < _nextProbe && _screenWidth == Screen.width && _screenHeight == Screen.height) return;
        _nextProbe = now + 0.25f;
        _screenWidth = Screen.width; _screenHeight = Screen.height; _measured = false;
        try
        {
            var layer = GuiManager.PlayerLayer;
            if (layer == null || !layer.IsVisible()) { Note("native-hud-hidden"); return; }
            _status = layer.m_playerStatus;
            if (_status == null || !_status.gameObject.activeInHierarchy) { Note("status-unavailable"); return; }
            var health = _status.m_health1;
            if (health == null) { Note("health-renderer-unavailable"); return; }
            if (health.sprite == null) { Note("health-sprite-unavailable"); return; }
            if (health.transform.parent == null) { Note("health-parent-unavailable"); return; }
            if (!TryHudCamera(layer, health, out Camera camera, out _cameraSource)) return;
            float fullWidth = _status.m_healthWidth;
            if (!KillPolicy.IsFinite(fullWidth) || fullWidth <= 0f) { Note("health-width-unavailable"); return; }
            // Use the captured full-length origin/width, never the health-dependent world bounds.
            Vector2 start = _status.m_healthStartPos;
            Bounds sprite = health.sprite.bounds;
            if (sprite.size.x <= 0f || sprite.size.y <= 0f) { Note("sprite-size-unavailable"); return; }
            float pivot = sprite.max.x / sprite.size.x;
            var parent = health.transform.parent;
            float z = health.transform.localPosition.z;
            float localHeight = health.drawMode == SpriteDrawMode.Simple ? sprite.size.y : health.size.y;
            if (!KillPolicy.IsFinite(localHeight) || localHeight <= 0f) { Note("health-height-unavailable"); return; }
            float lower = (sprite.min.y / sprite.size.y) * localHeight * health.transform.localScale.y;
            Vector3 left = camera.WorldToScreenPoint(parent.TransformPoint(new Vector3(start.x - fullWidth * (1f - pivot), start.y + lower, z)));
            Vector3 right = camera.WorldToScreenPoint(parent.TransformPoint(new Vector3(start.x + fullWidth * pivot, start.y + lower, z)));
            if (left.z <= 0f || right.z <= 0f || !KillPolicy.IsFinite(left.x) || !KillPolicy.IsFinite(right.x)
                || !KillPolicy.IsFinite(left.y) || !KillPolicy.IsFinite(right.y)) { Note("health-projection-invalid"); return; }
            _right = Math.Max(left.x, right.x);
            _bottom = Screen.height - Math.Min(left.y, right.y);
            _scale = Mathf.Clamp(Screen.height / 1080f, 0.7f, 2f);
            _measured = _right > 0f && _bottom > 0f && _right <= Screen.width && _bottom < Screen.height;
            if (!_measured) Note("health-projection-offscreen");
        }
        catch (Exception error) { Note("anchor-read-error:" + error.GetType().Name); }
    }
    internal static void Draw(Texture2D minor, Texture2D elite, Texture2D boss)
    {
        if (Plugin.KillCounterEnabled == null || !Plugin.KillCounterEnabled.Value || !_measured
            || GameStateManager.CurrentStateName != eGameStateName.InLevel) return;
        Color previousColor = GUI.color;
        Matrix4x4 previousMatrix = GUI.matrix;
        try
        {
            var layer = GuiManager.PlayerLayer;
            if (layer == null || !layer.IsVisible() || _status == null || ! _status.gameObject.activeInHierarchy) return;
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleRight, fontStyle = FontStyle.Bold,
                    clipping = TextClipping.Clip, richText = false, padding = new RectOffset(0, 0, 0, 0) };
                _fallbackStyle = new GUIStyle(_style) { alignment = TextAnchor.MiddleCenter };
            }
            _style.fontSize = (int)KillCounterPolicy.Font;
            float width = KillCounterPolicy.GroupGap * 2f;
            for (int i = 0; i < 3; i++)
            {
                int count = KillTracker.Counters.Get(Tiers[i]);
                if (LastCounts[i] != count) { LastCounts[i] = count; Texts[i] = KillCounterTotals.Format(count); Contents[i].text = Texts[i]; }
                Widths[i] = KillCounterPolicy.GroupWidth(_style.CalcSize(Contents[i]).x + 3f);
                width += Widths[i];
            }
            if (!KillCounterPolicy.TryLayout(_right, _bottom, _scale, width, Screen.width, Screen.height, out var row))
            { Note("row-does-not-fit"); return; }
            _fallbackStyle.fontSize = _style.fontSize;
            GUI.matrix = previousMatrix * Matrix4x4.TRS(new Vector3(row.X, row.Y, 0f), Quaternion.identity, new Vector3(row.Scale, row.Scale, 1f));
            float x = 0f;
            for (int i = 0; i < 3; i++)
            {
                var icon = i == 0 ? minor : i == 1 ? elite : boss;
                var iconRect = new Rect(x, 0f, KillCounterPolicy.Icon, KillCounterPolicy.Icon);
                GUI.color = Tones[i];
                if (icon != null) GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, true);
                else { GUI.Label(iconRect, Fallback[i], _fallbackStyle); Note("tier-icon-missing"); }
                float textX = x + KillCounterPolicy.Icon + KillCounterPolicy.IconGap;
                var textRect = new Rect(textX, 0f, Widths[i] - (textX - x), KillCounterPolicy.Icon);
                GUI.color = new Color(0f, 0f, 0f, 0.8f);
                GUI.Label(new Rect(textRect.x + 1f, textRect.y + 1f, textRect.width, textRect.height), Contents[i], _style);
                GUI.color = KillHud.Ink;
                GUI.Label(textRect, Contents[i], _style);
                x += Widths[i] + KillCounterPolicy.GroupGap;
            }
            if (!_placedLogged)
            {
                _placedLogged = true;
                Plugin.Instance?.Log.LogInfo($"[KILLCOUNTER] placed;x={row.X:0.0};y={row.Y:0.0};width={row.Width:0.0};height={row.Height:0.0};healthRight={_right:0.0};healthBottom={_bottom:0.0};screen={Screen.width}x{Screen.height};anchor=native-full-health-bar;camera={_cameraSource};counts={Texts[0]}/{Texts[1]}/{Texts[2]};icons={(minor != null ? 1 : 0)}/{(elite != null ? 1 : 0)}/{(boss != null ? 1 : 0)}");
            }
        }
        catch (Exception error) { Note("draw-error:" + error.GetType().Name); }
        finally { GUI.color = previousColor; GUI.matrix = previousMatrix; }
    }
    private static bool TryHudCamera(PlayerGuiLayer layer, SpriteRenderer health, out Camera camera, out string source)
    {
        camera = null; source = null;
        var gui = layer.GuiLayerBase;
        if (gui == null) { Note("hud-layer-base-unavailable"); return false; }
        var canvas = gui.m_cellUICanvas;
        if (canvas != null)
        {
            var manual = canvas.m_UICamera;
            var root = canvas.m_rectTransform;
            if (manual != null && root != null)
            {
                Rect viewport = manual.rect;
                var texture = manual.targetTexture;
                string refusal = KillCounterAnchorPolicy.ManualRefusal(health.transform.IsChildOf(root), manual.cullingMask,
                    health.gameObject.layer, texture != null ? texture.width : manual.pixelWidth,
                    texture != null ? texture.height : manual.pixelHeight, canvas.m_pixelWidth, canvas.m_pixelHeight,
                    viewport.x, viewport.y, viewport.width, viewport.height, Screen.width, Screen.height);
                if (refusal == null) { camera = manual; source = "cellui-manual-verified"; return true; }
                Note(refusal, $"target={(texture != null ? texture.width : manual.pixelWidth)}x{(texture != null ? texture.height : manual.pixelHeight)};canvas={canvas.m_pixelWidth:0.##}x{canvas.m_pixelHeight:0.##};screen={Screen.width}x{Screen.height};mask=0x{manual.cullingMask:X8};layer={health.gameObject.layer};viewport={viewport.x:0.####},{viewport.y:0.####},{viewport.width:0.####},{viewport.height:0.####}");
            }
            else Note(manual == null ? "manual-canvas-camera-unavailable" : "manual-canvas-root-unavailable");
        }
        var world = gui.GetWorldCamera();
        if (world != null && world.enabled && world.gameObject.activeInHierarchy && world.targetTexture == null
            && KillCounterAnchorPolicy.ManualRefusal(true, world.cullingMask, health.gameObject.layer,
                world.pixelWidth, world.pixelHeight, world.pixelWidth, world.pixelHeight,
                world.rect.x, world.rect.y, world.rect.width, world.rect.height, Screen.width, Screen.height) == null)
        { camera = world; source = "hud-world-verified"; return true; }
        Note("hud-camera-unavailable");
        return false;
    }
    private static void Note(string reason, string detail = null)
    {
        if (Notes.Count < 12 && Notes.Add(reason)) Plugin.Instance?.Log.LogInfo("[KILLCOUNTER] held;reason=" + reason + (detail == null ? "" : ";" + detail));
    }
}
