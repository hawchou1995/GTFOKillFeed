using System;
using System.IO;
using UnityEngine;

namespace HawChow.GTFOKillFeed;

public sealed class KillHud : MonoBehaviour
{
    internal static readonly Color Ink = new(0.91f, 0.925f, 0.92f);
    internal static readonly Color Accent = new(0.90f, 0.67f, 0.34f);
    internal static readonly Color HeavyTone = new(0.73f, 0.16f, 0.16f);
    private Texture2D _normalIcon, _weakIcon, _heavyIcon;
    private readonly IconTextureCache _icons = new();
    private string _iconRoot, _normalPath, _weakPath, _heavyPath;
    private GUIStyle _titleStyle, _detailStyle;
    private bool _visible = true, _iconsLogged;
    private int _warnings;
    private int _layoutLogs;
    internal static KillHud Instance;
    public KillHud(IntPtr pointer) : base(pointer) { }
    private void Update()
    {
        try
        {
            float now = Time.unscaledTime;
            EnemyNames.Tick(now);
            Plugin.TickConfigHotReload(now);
            DamageNumbers.Tick(now); CorpseFeedback.Tick(now); TeammateMarks.Tick(now, _visible);
            TickCardIcons(now);
            KillIconHud.Tick(now);
            if (!Plugin.Enabled.Value) return;
            if (Plugin.ToggleKey.Value != KeyCode.None && Input.GetKeyDown(Plugin.ToggleKey.Value)) _visible = !_visible;
            if (Plugin.PreviewKey.Value != KeyCode.None && Input.GetKeyDown(Plugin.PreviewKey.Value))
            {
                KillTracker.AddPreview(false); KillTracker.AddPreview(true); DamageNumbers.Preview();
            }
            EnemyBar.Tick(now, _visible);
        }
        catch (Exception error) { Warn("update", error.Message); }
    }
    private void OnGUI()
    {
        if (!_visible || Plugin.Enabled == null || !Plugin.Enabled.Value || Event.current.type != EventType.Repaint) return;
        Color previous = GUI.color;
        try
        {
            EnsureStyles();
            VisibleIcons();
            DamageNumbers.Draw();
            EnemyBar.Draw();
            TeammateMarks.Draw();
            // Presentation switch: Head (default) draws the BFV kill icons above the dead enemies; Card keeps
            // the exact previous card layout. Both sit behind the same F7 / General/Enabled gate above.
            // The card path is ALWAYS evaluated now (review F1): in Card mode the cards are the presentation,
            // and in Head mode every card decides for itself whether its own kill got a head icon. A kill whose
            // icon could not be created (no head point) or cannot be drawn (missing tier resource) therefore
            // shows its card instead of ending up as sound only, and a kill whose icon IS on screen draws no
            // card - the two can never appear for the same kill.
            KillIconHud.Draw();
            DrawCards();
        }
        catch (Exception error) { Warn("draw", error.Message); }
        finally { GUI.color = previous; }
    }
    // Review I6: the measured title must be the title that is actually drawn (the BOSS tier renders "HEAVY
    // KILL"), otherwise a long title gets squeezed to the 8 px floor or clipped after the width was measured
    // from a shorter string. One function feeds both.
    private static string CardTitle(KillCard card)
    {
        bool weak = card.Kind == KillKind.Weakspot;
        bool boss = card.Tier == KillTier.Boss;
        return KillPolicy.IsChinese(Plugin.Language.Value)
            ? (boss ? "重装击杀" : weak ? "弱点击杀" : "击杀确认")
            : (boss ? "HEAVY KILL" : weak ? "WEAKPOINT KILL" : "KILL CONFIRMED");
    }
    private float MeasureCardWidth(VisibleKillCard visible, float iconSize, float scale, float maxWidth)
    {
        var card = visible.Card;
        string title = CardTitle(card);
        string detail = FeedbackPolicy.CardDetail(card, Plugin.ShowDistance.Value, Plugin.ShowEnemyHealth.Value);
        _titleStyle.fontSize = Mathf.RoundToInt(13f * scale);
        _detailStyle.fontSize = Mathf.RoundToInt(12f * scale);
        float textWidth = Mathf.Max(_titleStyle.CalcSize(new GUIContent(title)).x, _detailStyle.CalcSize(new GUIContent(detail)).x);
        return FeedbackPolicy.CardWidth(iconSize, textWidth, scale, maxWidth);
    }
    // Height of one card: icon + padding, plus the flight badge row when the kill is a flyer. The badge sits
    // BELOW the icon box (same geometry as the head-top path, review I5) so the row has to be reserved.
    private static float CardHeight(float iconSize, float scale)
    {
        float baseHeight = Mathf.Max(50f * scale, iconSize + 10f * scale);
        float badge = KillIconPolicy.BadgeSize(iconSize);
        return badge > 1f ? baseHeight + badge + KillIconPolicy.BadgeGap(iconSize) : baseHeight;
    }
    private void DrawCard(VisibleKillCard visible, Rect rect, float iconSize, float scale)
    {
        var card = visible.Card;
        bool weak = card.Kind == KillKind.Weakspot;
        float alpha = visible.Alpha;
        float panel = FeedbackPolicy.PanelAlpha(Plugin.BackgroundOpacity != null ? Plugin.BackgroundOpacity.Value : FeedbackPolicy.DefaultPanelOpacity);
        GUI.color = new Color(0.055f, 0.064f, 0.068f, panel * alpha);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = new Color(0.28f, 0.31f, 0.32f, FeedbackPolicy.RuleAlpha(panel) * alpha);
        GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, scale), Texture2D.whiteTexture);
        Color tone = card.Tier == KillTier.Boss ? HeavyTone : weak ? Accent : Ink;
        var iconRect = new Rect(rect.x + 8f * scale, rect.y + (rect.height - iconSize) * 0.5f, iconSize, iconSize);
        GUI.color = new Color(1, 1, 1, alpha);
        // The icon family follows the SAME tier the kill cue consumed (light/medium/heavy), through the one
        // shared mapping in KillIconPolicy - the killing limb NEVER picks the raster (1.2.15 did, so a weakspot
        // kill of a low-health enemy drew the medium raster on the card and the light one above the head).
        var texture = TierIcon(card.Tier);
        if (texture != null) GUI.DrawTexture(iconRect, texture, ScaleMode.ScaleToFit, true);
        // Flight identity stays readable in card mode too, and with the SAME geometry as the head-top path:
        // one badge row below the icon box (review I5). CardHeight reserves that row.
        float badge = KillIconPolicy.BadgeSize(iconSize);
        KillIconHud.DrawFlightMark(card.Flight, iconRect.center.x, iconRect.yMax + KillIconPolicy.BadgeGap(iconSize), badge, alpha);
        float left = iconRect.xMax + 10f * scale;
        float available = rect.xMax - left - 12f * scale;
        string title = CardTitle(card);
        _titleStyle.fontSize = Mathf.RoundToInt(13f * scale);
        var titleRect = new Rect(left, rect.center.y - 20f * scale, available, 22f * scale);
        int titleMinSize = Mathf.RoundToInt(8f * scale);
        while (_titleStyle.fontSize > titleMinSize && _titleStyle.CalcSize(new GUIContent(title)).x > available) _titleStyle.fontSize--;
        GUI.color = new Color(0f, 0f, 0f, 0.65f * alpha);
        GUI.Label(new Rect(titleRect.x + scale, titleRect.y + scale, titleRect.width, titleRect.height), title, _titleStyle);
        GUI.color = new Color(tone.r, tone.g, tone.b, alpha);
        GUI.Label(titleRect, title, _titleStyle);
        string detail = FeedbackPolicy.CardDetail(card, Plugin.ShowDistance.Value, Plugin.ShowEnemyHealth.Value);
        _detailStyle.fontSize = Mathf.RoundToInt(12f * scale);
        var content = new GUIContent(detail);
        int minSize = Mathf.RoundToInt(8f * scale);
        while (_detailStyle.fontSize > minSize && _detailStyle.CalcSize(content).x > available) _detailStyle.fontSize--;
        var detailRect = new Rect(left, rect.center.y + 1f * scale, available, 19f * scale);
        GUI.color = new Color(0f, 0f, 0f, 0.65f * alpha);
        GUI.Label(new Rect(detailRect.x + scale, detailRect.y + scale, detailRect.width, detailRect.height), detail, _detailStyle);
        GUI.color = new Color(Ink.r, Ink.g, Ink.b, 0.96f * alpha);
        GUI.Label(detailRect, detail, _detailStyle);
    }
    // Card layout, unchanged - except that each card is now asked whether its own kill is presented by a head
    // icon (review F1). Compaction happens in the snapshot array so the stack layout still counts only the
    // cards that are actually painted.
    private void DrawCards()
    {
        VisibleKillCard[] cards = KillTracker.Snapshot(Time.unscaledTime);
        float now = Time.unscaledTime;
        int painted = 0;
        for (int i = 0; i < cards.Length; i++)
            if (PaintsCard(cards[i].Card, Plugin.KillCardEnabled == null || Plugin.KillCardEnabled.Value)) cards[painted++] = cards[i];
        if (painted == 0) return;
        float resolution = Mathf.Clamp(Screen.height / 1080f, 0.75f, 1.6f);
        float icon = FeedbackPolicy.Clamp(Plugin.IconSize.Value, 26f, 76f, KillDefaults.IconSize) * resolution;
        // Review I5: measure the tallest needed card (icon + the badge row when it is a flyer) once, so a
        // badge drawn below the icon box always stays inside the panel and the stack stays aligned.
        float height = 0f;
        for (int i = 0; i < painted; i++) height = Mathf.Max(height, CardHeight(icon, resolution));
        float maxWidth = Mathf.Max(0f, Mathf.Min(Screen.width - 32f, 430f * resolution));
        float baseY = Mathf.Clamp(Screen.height * 0.78f + Plugin.VerticalOffset.Value, height * painted, Screen.height - height - 12f);
        float maxDistance = Plugin.MaxDistance?.Value ?? KillDefaults.MaxDistance;
        for (int i = 0; i < painted; i++)
        {
            // Re-resolve name mapping + distance ceiling here, so editing the cfg or the name
            // table updates cards that are already visible (history/ownership stays untouched).
            var shown = new VisibleKillCard(CardPresentation.Resolve(cards[i].Card, EnemyNames.Resolve, maxDistance), cards[i].Alpha);
            float age = Mathf.Max(0, now - shown.Card.CreatedAt);
            float enter = Mathf.Pow(1f - Mathf.Clamp01(age / 0.16f), 3f);
            float y = baseY - (painted - 1 - i) * (height + 5f * resolution) + enter * 7f * resolution;
            float width = MeasureCardWidth(shown, icon, resolution, maxWidth);
            float x = FeedbackPolicy.CenteredCardX(Screen.width, width, Plugin.HorizontalOffset.Value, 12f);
            if (_layoutLogs++ < 8) Plugin.Instance?.Log.LogInfo($"[HUD] card layout enemy={shown.Card.EnemyName};x={x:0.0};width={width:0.0};center={x + width * 0.5f:0.0};screenCenter={Screen.width * 0.5f:0.0};screen={Screen.width}x{Screen.height}");
            DrawCard(shown, new Rect(x, y, width, height), icon, resolution);
        }
    }

    // One rule, in KillIconPolicy, for "is this kill's card painted": Card mode keeps every card, Head mode
    // paints the card of a kill whose own head icon is absent or undrawable, and never both at once.
    // The card channel is its own switch now: a card is painted whenever Card/Enabled is on, and the
    // head icon (KillIcon/HeadEnabled) is drawn independently. Both can show for the same kill.
    internal static bool PaintsCard(KillCard card, bool cardEnabled)
        => card != null && KillViewPolicy.CardVisible(cardEnabled, gameVisible: true);
    // Both presentations read the same three tier slots, which are themselves named by the one tier -> BFV
    // raster mapping in KillIconPolicy (KillIconPolicy.TierIconKey / TierIconFile).
    private Texture2D TierIcon(KillTier tier)
        => tier == KillTier.Elite ? _weakIcon : tier == KillTier.Boss ? _heavyIcon : _normalIcon;
    private void EnsureStyles()
    {
        if (_titleStyle != null) return;
        _titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip, richText = false };
        _detailStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip, richText = false };
    }
    // Icon files are only *scheduled* here (Update): the probe/read happens on a worker inside
    // IconPipeline, so a slow or locked PNG can never stall a frame and OnGUI never touches the disk.
    // The file names AND the cache keys come from KillIconPolicy, i.e. the very mapping the head-top
    // icons use, so the two presentations can never drift apart.
    private void TickCardIcons(float now)
    {
        if (_iconRoot == null) _iconRoot = IconTextureCache.IconRoot();
        string normalKey = KillIconPolicy.TierIconKey(KillTier.Minor);
        string weakKey = KillIconPolicy.TierIconKey(KillTier.Elite);
        string heavyKey = KillIconPolicy.TierIconKey(KillTier.Boss);
        _normalPath = Path.Combine(_iconRoot, KillIconPolicy.TierIconFile(KillTier.Minor));
        _weakPath = Path.Combine(_iconRoot, KillIconPolicy.TierIconFile(KillTier.Elite));
        _heavyPath = Path.Combine(_iconRoot, KillIconPolicy.TierIconFile(KillTier.Boss));
        _icons.Tick(normalKey, _normalPath, now);
        _icons.Tick(weakKey, _weakPath, now);
        _icons.Tick(heavyKey, _heavyPath, now);
        if (_iconsLogged) return;
        _iconsLogged = true;
        Plugin.Instance?.Log.LogInfo($"[HUD] icon slots registered normal={HotReloadPolicy.SafeFileName(_normalPath)};weakspot={HotReloadPolicy.SafeFileName(_weakPath)};heavy={HotReloadPolicy.SafeFileName(_heavyPath)};layout=two-line;health-tiers=icon-family;hot-reload=stamp");
    }

    // OnGUI-side: pure lookup of whatever the pipeline last applied (may be null while the first
    // load is still running, which simply draws no icon for a frame or two).
    private void VisibleIcons()
    {
        _normalIcon = _icons.Texture(KillIconPolicy.TierIconKey(KillTier.Minor));
        _weakIcon = _icons.Texture(KillIconPolicy.TierIconKey(KillTier.Elite));
        _heavyIcon = _icons.Texture(KillIconPolicy.TierIconKey(KillTier.Boss));
    }

    private void Warn(string stage, string detail) { if (_warnings++ < 5) Plugin.Instance?.Log.LogWarning("[HUD] " + stage + ": " + detail); }
}
