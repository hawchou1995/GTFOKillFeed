using System;
using BepInEx.Configuration;
using Enemies;
using UnityEngine;

namespace HawChow.GTFOKillFeed;

// Focused-enemy health bar: state machine plus IMGUI drawing. Driven by KillHud so it shares the
// existing Repaint pass, F7 visibility toggle and palette. Read-only, no gameplay calls.
internal static class EnemyBar
{
    private static readonly Color DefaultBorder = new(0.043f, 0.055f, 0.062f, 1f);
    private static readonly Color DefaultTrack = new(0.086f, 0.100f, 0.110f, 1f);
    private static readonly Color DefaultGhost = new(0.660f, 0.690f, 0.700f, 1f);
    private static readonly Color DefaultText = new(0.91f, 0.925f, 0.92f, 1f);
    private static readonly Color DefaultWeakspot = new(0.937f, 0.298f, 0.298f, 1f);
    // Complete C-Foam tag in GlueGun mode: warm restrained amber #F2A65A at full alpha, so the tag reads
    // as its own state instead of sharing the neutral monster-name grey. Opacity still fades it.
    private static readonly Color DefaultCfoamTag = new(BarPolicy.CfoamTagRed, BarPolicy.CfoamTagGreen, BarPolicy.CfoamTagBlue, 1f);
    private static readonly Color DefaultFillYellow = new(0.902f, 0.722f, 0.298f, 1f);
    private const float WeakspotRefreshSeconds = 0.35f;
    private static readonly Color DefaultFillRed = new(0.937f, 0.298f, 0.298f, 1f);

    private static Color _nameColor = DefaultText;
    private static Color _weakspotColor = DefaultWeakspot;
    private static Color _healthTextColor = DefaultText;
    private static Color _fillColor = DefaultText;
    private static Color _fillYellowColor = DefaultFillYellow;
    private static Color _fillRedColor = DefaultFillRed;
    private static Color _ghostColor = DefaultGhost;
    private static Color _trackColor = DefaultTrack;
    private static Color _borderColor = DefaultBorder;
    private static string _nameColorSource, _weakspotColorSource, _healthTextColorSource, _fillColorSource;
    private static string _fillYellowColorSource, _fillRedColorSource, _ghostColorSource, _trackColorSource, _borderColorSource;

    private static GUIStyle _nameStyle, _weakspotStyle, _numbersStyle;
    private static readonly GUIContent NameContent = new();
    private static readonly GUIContent PartContent = new();
    private static readonly GUIContent NumbersContent = new();
    // 1.2.22: its own style and content so the stagger row can never borrow (or disturb) the name, weakspot
    // or C-Foam labels; the colour is the fixed light green from BarPolicy, not a configurable bar colour.
    private static readonly GUIContent StaggerContent = new();
    private static GUIStyle _staggerStyle;
    private static BarPolicy.StaggerValues _stagger = BarPolicy.StaggerValues.None;
    private static string _labelStagger = string.Empty;
    // 1.2.23: the name label's real first-glyph x (rect + alignment + padding/content offset), shared by the green
    // row so it starts exactly under the monster name; invalid when the name is hidden, which makes the row use the
    // health bar's own left edge instead.
    private static float _nameOriginX;
    private static bool _nameOriginValid;
    private static EnemyAgent _target;
    private static string _name = string.Empty;
    private static string _weakspotPart = string.Empty, _partLanguage;
    private static string _foamTag = string.Empty;
    private static bool _glueMode;
    private static int _nameRevision = -1;
    private static float _weakspotRefreshAt = float.NegativeInfinity;
    private static int _targetId = int.MinValue;
    private static float _health, _maxHealth, _ghost;
    private static float _shownFor, _lostFor;
    private static bool _haveTarget, _wasFocused;
    private static float _lastNow;
    // Independent diagnostic budgets: the LogFocus debug switch must never be able to silence real bar
    // exceptions, and exception warnings must never eat the focus-log budget.
    private static readonly DiagnosticBudget WarningBudget = new();
    // 1.2.22: bounded evidence line for a normal-hitreact value whose unit could not be proven for this enemy.
    // 1.2.23: the anatomical-part refusal gets its OWN allowance, so an unknown/mod part can never spend the
    // normal-hitreact evidence line (or silence it).
    private static readonly DiagnosticBudget LogStaggerBudget = new();
    private static readonly DiagnosticBudget LogStaggerPartBudget = new();
    private static readonly DiagnosticBudget FocusChangeBudget = new();
    private static readonly DiagnosticBudget FocusAcquiredBudget = new();

    // Last drawn screen anchor, so a fade-out never needs the enemy object again.
    private static bool _hasAnchor;
    private static float _anchorX, _anchorY;

    // Label content is rebuilt only when a displayed value changes.
    private static string _labelName = string.Empty, _labelPart = string.Empty, _labelNumbers = string.Empty;
    private static string _labelSourceName, _labelSourcePart;
    private static int _labelHealth = int.MinValue, _labelMaxHealth = int.MinValue;
    private static bool _labelNamed, _labelPartVisible, _labelNumbered, _labelFoamMode;

    internal static void Reset()
    {
        _target = null;
        _name = string.Empty;
        _weakspotPart = string.Empty;
        _foamTag = string.Empty;
        _glueMode = false;
        _partLanguage = null;
        _weakspotRefreshAt = float.NegativeInfinity;
        _nameRevision = -1;
        _targetId = int.MinValue;
        _health = 0f;
        _maxHealth = 0f;
        _ghost = 0f;
        _shownFor = 0f;
        _lostFor = 0f;
        _haveTarget = false;
        _hasAnchor = false;
        _labelName = string.Empty;
        _labelPart = string.Empty;
        _labelNumbers = string.Empty;
        _labelSourceName = null;
        _labelSourcePart = null;
        _labelHealth = int.MinValue;
        _labelMaxHealth = int.MinValue;
        _labelNamed = false;
        _labelPartVisible = false;
        _labelNumbered = false;
        _labelFoamMode = false;
        _stagger = BarPolicy.StaggerValues.None;
        _labelStagger = string.Empty;
        _nameOriginValid = false;
        _nameOriginX = 0f;
        EnemyFocus.Reset();
    }

    internal static void Tick(float now, bool hudVisible)
    {
        try
        {
            float delta = _lastNow <= 0f ? 0f : Mathf.Clamp(now - _lastNow, 0f, 0.25f);
            _lastNow = now;

            if (Plugin.Enabled == null || !Plugin.Enabled.Value ||
                Plugin.EnemyBarEnabled == null || !Plugin.EnemyBarEnabled.Value || !hudVisible)
            {
                Drop();
                return;
            }

            EnemyFocus.Update(Plugin.EnemyBarMaxDistance.Value);
            EnemyAgent found = EnemyFocus.Target;
            bool focused = found != null && BarPolicy.ShouldShow(true, EnemyFocus.Distance, Plugin.EnemyBarMaxDistance.Value);
            LogFocus(focused);

            if (!focused)
            {
                // Drop the wrapper immediately: a dead or despawned enemy must never be dereferenced
                // again, and the fade-out replays from the cached screen anchor instead.
                _target = null;
                if (_haveTarget)
                {
                    _haveTarget = false;
                    _lostFor = 0f;
                }
                else if (_lostFor < BarPolicy.HoldSeconds + BarPolicy.FadeOutSeconds)
                {
                    _lostFor += delta;
                }
                return;
            }

            int id = SafeId(found);
            bool switched = !_haveTarget || (id != int.MinValue && _targetId != int.MinValue && id != _targetId);
            string language = Plugin.Language != null ? Plugin.Language.Value : "zh";
            if (switched)
            {
                _shownFor = 0f;
                _hasAnchor = false;
                ReadHealth(found, out _health, out _maxHealth);
                _ghost = BarPolicy.FillFraction(_health, _maxHealth);
            }

            if (switched || _nameRevision != EnemyNames.Revision)
            {
                _name = EnemyNames.Resolve(SafeDataId(found), EnemyFocus.RawNameOf(found));
                _nameRevision = EnemyNames.Revision;
            }
            bool languageChanged = !string.Equals(_partLanguage, language, StringComparison.OrdinalIgnoreCase);
            bool glueMode = EnemyFocus.GlueGunWielded;
            bool glueModeChanged = glueMode != _glueMode;
            if (switched || languageChanged || glueModeChanged || now - _weakspotRefreshAt >= WeakspotRefreshSeconds)
            {
                _glueMode = glueMode;
                if (_glueMode)
                {
                    _weakspotPart = string.Empty;
                    _foamTag = BarPolicy.CfoamTag(EnemyFocus.GlueToleranceOf(found));
                }
                else
                {
                    string tags = EnemyFocus.ResolveWeakspotParts(found, language);
                    if (!string.Equals(_weakspotPart, tags, StringComparison.Ordinal)) _weakspotPart = tags;
                    _foamTag = string.Empty;
                }
                _partLanguage = language;
                // 1.2.22: the stagger row is refreshed on the same 0.35 s cadence as the weakspot tags, from
                // THIS enemy only (no scene scan, no extra network read), and formatted once here so drawing
                // never allocates or recomputes.
                _stagger = ReadStagger(found);
                _labelStagger = BarPolicy.StaggerLine(_stagger, language);
                _weakspotRefreshAt = now;
            }
            if (switched && FocusAcquiredBudget.TryUse(12))
                Plugin.Instance?.Log.LogInfo($"[BAR] focus=acquired;enemyId={SafeDataId(found)};name={_name};weakspotTags={_weakspotPart};distance={EnemyFocus.Distance:0.0}m;health={BarPolicy.DisplayValue(_health)};maxHealth={BarPolicy.DisplayValue(_maxHealth)}");

            _targetId = id;
            _target = found;
            _haveTarget = true;
            _lostFor = 0f;
            _shownFor += delta;

            ReadHealth(found, out _health, out _maxHealth);
            float fraction = BarPolicy.FillFraction(_health, _maxHealth);
            _ghost = BarPolicy.GhostDecay(Mathf.Max(_ghost, fraction), fraction, delta);
        }
        catch (Exception error)
        {
            Drop();
            Warn("tick", error.Message);
        }
    }

    internal static void Draw()
    {
        float alpha = BarPolicy.AlphaFor(_haveTarget, _shownFor, _lostFor);
        if (!BarPolicy.ShouldDraw(_haveTarget, _hasAnchor, alpha)) return;

        int width = Screen.width, height = Screen.height;
        if (width <= 0 || height <= 0) return;

        Color previous = GUI.color;
        try
        {
            EnsureStyles();
            Color border = ConfigColor(Plugin.EnemyBarBorderColor, ref _borderColorSource, ref _borderColor, DefaultBorder);
            Color track = ConfigColor(Plugin.EnemyBarTrackColor, ref _trackColorSource, ref _trackColor, DefaultTrack);
            Color ghostColor = ConfigColor(Plugin.EnemyBarGhostColor, ref _ghostColorSource, ref _ghostColor, DefaultGhost);
            Color baseFillColor = ConfigColor(Plugin.EnemyBarFillColor, ref _fillColorSource, ref _fillColor, DefaultText);
            Color yellowFillColor = ConfigColor(Plugin.EnemyBarFillYellowColor, ref _fillYellowColorSource, ref _fillYellowColor, DefaultFillYellow);
            Color redFillColor = ConfigColor(Plugin.EnemyBarFillRedColor, ref _fillRedColorSource, ref _fillRedColor, DefaultFillRed);

            float barWidth = BarPolicy.Width(Plugin.EnemyBarWidth.Value, height, width);
            float barHeight = BarPolicy.Height(Plugin.EnemyBarHeight.Value, height);
            float opacity = BarPolicy.Opacity(Plugin.EnemyBarOpacity.Value) * alpha;
            float offset = BarPolicy.VerticalOffset(Plugin.EnemyBarVerticalOffset.Value);

            if (!TryResolveAnchor(width, height, offset, out float centreX, out float topY)) return;
            topY = BarPolicy.AnchorY(topY, height, Plugin.EnemyBarMinScreenFraction.Value, Plugin.EnemyBarMaxScreenFraction.Value);
            centreX = BarPolicy.AnchorX(centreX, barWidth, width, height);
            if (_haveTarget)
            {
                _anchorX = centreX;
                _anchorY = topY;
                _hasAnchor = true;
            }

            var rect = new Rect(centreX - barWidth * 0.5f, topY, barWidth, barHeight);
            float fraction = BarPolicy.FillFraction(_health, _maxHealth);
            float healthRatio = KillPolicy.IsFinite(_health) && KillPolicy.IsFinite(_maxHealth) && _maxHealth > 0f
                ? Mathf.Clamp01(_health / _maxHealth) : float.NaN;
            float yellowThreshold = Plugin.EnemyBarYellowThresholdPercent != null
                ? Plugin.EnemyBarYellowThresholdPercent.Value : BarPolicy.DefaultYellowThresholdPercent;
            float redThreshold = Plugin.EnemyBarRedThresholdPercent != null
                ? Plugin.EnemyBarRedThresholdPercent.Value : BarPolicy.DefaultRedThresholdPercent;
            BarPolicy.HealthBand band = BarPolicy.HealthColorBand(healthRatio, yellowThreshold, redThreshold);
            Color fillColor = band == BarPolicy.HealthBand.Red ? redFillColor
                : band == BarPolicy.HealthBand.Yellow ? yellowFillColor : baseFillColor;
            float ghost = Mathf.Clamp(_ghost, fraction, 1f);
            float edge = Mathf.Max(1f, Mathf.Round(barHeight * 0.36f));

            Fill(new Rect(rect.x - edge, rect.y - edge, rect.width + edge * 2f, rect.height + edge * 2f),
                WithAlpha(border, BarPolicy.BorderAlpha * opacity));
            Fill(rect, WithAlpha(track, BarPolicy.TrackAlpha * opacity));
            if (ghost > fraction)
                Fill(new Rect(rect.x, rect.y, rect.width * ghost, rect.height),
                    WithAlpha(ghostColor, BarPolicy.GhostAlpha * opacity));
            if (fraction > 0f)
                Fill(new Rect(rect.x, rect.y, rect.width * fraction, rect.height),
                    WithAlpha(fillColor, BarPolicy.FillAlpha * opacity));

            for (int i = 1; i < BarPolicy.Segments; i++)
            {
                float tick = BarPolicy.TickFraction(i, BarPolicy.Segments);
                if (tick < 0f) continue;
                Fill(new Rect(rect.x + rect.width * tick, rect.y, edge, rect.height),
                    WithAlpha(border, BarPolicy.TickAlpha * opacity));
            }
            DrawFocusOutline(rect, edge, barHeight, opacity);

            DrawLabel(rect, width, height, opacity, border);
            // 1.2.22: one extra row UNDER the bar. It is drawn after every existing element, with its own
            // style/content/colour, and the method restores nothing globally: the caller's GUI.color is put
            // back by the finally block, exactly as before.
            DrawStaggerRow(rect, width, height, opacity, border);
        }
        catch (Exception error) { Warn("draw", error.Message); }
        finally { GUI.color = previous; }
    }

    // Live projection while a target is held; the cached anchor afterwards, never the enemy object.
    // Focus outline: orange on the enemy's back, red on a weakspot, prismatic when the crosshair is
    // both behind the enemy and on a weakspot. Four fills, no allocation, and nothing is drawn when the
    // option is off or the crosshair is on neither.
    private static void DrawFocusOutline(Rect rect, float edge, float barHeight, float opacity)
    {
        bool enabled = Plugin.EnemyBarFocusOutlineEnabled == null || Plugin.EnemyBarFocusOutlineEnabled.Value;
        FocusOutlineKind kind = FocusOutlinePolicy.Classify(
            FocusOutlinePolicy.IsBehind(EnemyFocus.BackDot), EnemyFocus.HitIsWeakspot);
        if (!FocusOutlinePolicy.ShouldDraw(enabled, kind)) return;
        float thickness = kind == FocusOutlineKind.Prismatic
            ? FocusOutlinePolicy.FoilThickness(barHeight)
            : FocusOutlinePolicy.Thickness(barHeight);
        float phase = DamageFxPolicy.PhaseFor(Time.unscaledTime);
        float left = rect.x - edge - thickness;
        float top = rect.y - edge - thickness;
        float width = rect.width + (edge + thickness) * 2f;
        float height = rect.height + (edge + thickness) * 2f;
        if (kind != FocusOutlineKind.Prismatic)
        {
            FocusOutlinePolicy.OutlineColor(kind, phase, out float red, out float green, out float blue);
            Color shaded = WithAlpha(new Color(red, green, blue), opacity);
            Fill(new Rect(left, top, width, thickness), shaded);
            Fill(new Rect(left, top + height - thickness, width, thickness), shaded);
            Fill(new Rect(left, top, thickness, height), shaded);
            Fill(new Rect(left + width - thickness, top, thickness, height), shaded);
            return;
        }
        // Prismatic: hue advances around the whole perimeter (foil rim). 4 edges x 5 segments = 20
        // fills while the crosshair is on the enemy's back AND on a weakspot; still constant cost and
        // nothing is drawn when the option is off.
        int segments = FocusOutlinePolicy.FoilFrameSegments;
        int total = segments * 4;
        float spanWidth = width / segments;
        float spanHeight = height / segments;
        for (int i = 0; i < segments; i++)
        {
            FocusOutlinePolicy.FoilFrameColor(i, total, phase, out float r, out float g, out float b);
            Fill(new Rect(left + i * spanWidth, top, spanWidth + 0.5f, thickness), WithAlpha(new Color(r, g, b), opacity));
            FocusOutlinePolicy.FoilFrameColor(segments + i, total, phase, out r, out g, out b);
            Fill(new Rect(left + width - (i + 1) * spanWidth, top + height - thickness, spanWidth + 0.5f, thickness),
                WithAlpha(new Color(r, g, b), opacity));
            FocusOutlinePolicy.FoilFrameColor(2 * segments + i, total, phase, out r, out g, out b);
            Fill(new Rect(left + width - thickness, top + i * spanHeight, thickness, spanHeight + 0.5f),
                WithAlpha(new Color(r, g, b), opacity));
            FocusOutlinePolicy.FoilFrameColor(3 * segments + i, total, phase, out r, out g, out b);
            Fill(new Rect(left, top + height - (i + 1) * spanHeight, thickness, spanHeight + 0.5f),
                WithAlpha(new Color(r, g, b), opacity));
        }
    }

    private static bool TryResolveAnchor(int width, int height, float offset, out float centreX, out float topY)
    {
        centreX = 0f;
        topY = 0f;
        if (_haveTarget && _target != null)
        {
            if (!BarPolicy.IsHeadMode(Plugin.EnemyBarAnchor.Value))
            {
                centreX = width * 0.5f;
                topY = BarPolicy.ScreenAnchorY(Plugin.EnemyBarScreenAnchor.Value, height) + offset;
                return true;
            }
            try
            {
                Camera camera = CameraManager.GetCurrentCamera();
                if (camera == null) return false;
                if (!EnemyFocus.TryGetHeadWorld(_target, BarPolicy.HeadOffset(Plugin.EnemyBarHeadOffset.Value), out Vector3 world)) return false;
                Vector3 screen = camera.WorldToScreenPoint(world);
                if (screen.z <= 0.1f) return false;
                centreX = screen.x;
                topY = height - screen.y + offset;
                return true;
            }
            catch { return false; }
        }
        if (!_hasAnchor) return false;
        centreX = _anchorX;
        topY = _anchorY;
        return true;
    }

    private static void DrawLabel(Rect bar, int screenWidth, int screenHeight, float opacity, Color shadowColor)
    {
        _nameOriginValid = false;
        _nameOriginX = 0f;
        bool showName = Plugin.EnemyBarShowName == null || Plugin.EnemyBarShowName.Value;
        bool showWeakspotPart = Plugin.EnemyBarShowWeakspotPart == null || Plugin.EnemyBarShowWeakspotPart.Value;
        bool showNumbers = Plugin.EnemyBarShowNumbers == null || Plugin.EnemyBarShowNumbers.Value;
        string targetTag = BarPolicy.SelectEnemyTag(_glueMode, showWeakspotPart, _weakspotPart, _foamTag);
        bool showPart = showName && targetTag.Length > 0;
        EnsureLabel(showName, showPart, showNumbers, targetTag);
        if (_labelName.Length == 0 && _labelPart.Length == 0 && _labelNumbers.Length == 0) return;

        float scale = BarPolicy.ResolutionScale(screenHeight);
        int font = Mathf.RoundToInt(BarPolicy.NumberSize(Plugin.EnemyBarNumberSize.Value, screenHeight));
        int minFont = Mathf.Max(7, Mathf.RoundToInt(8f * scale));
        SetFontSize(font);
        NameContent.text = _labelName;
        PartContent.text = _labelPart;
        NumbersContent.text = _labelNumbers;

        float padding = 7f * scale;
        float spacing = 6f * scale;
        float margin = 6f * scale;
        float maxWidth = Mathf.Max(1f, screenWidth - margin * 2f);
        float nameWidth = Measure(NameContent, _nameStyle);
        float partWidth = Measure(PartContent, _weakspotStyle);
        float numberWidth = Measure(NumbersContent, _numbersStyle);
        float required = BarPolicy.HeaderWidth(0f, nameWidth, partWidth, numberWidth, padding, spacing, float.PositiveInfinity);

        while (required > maxWidth && _nameStyle.fontSize > minFont)
        {
            SetFontSize(_nameStyle.fontSize - 1);
            nameWidth = Measure(NameContent, _nameStyle);
            partWidth = Measure(PartContent, _weakspotStyle);
            numberWidth = Measure(NumbersContent, _numbersStyle);
            required = BarPolicy.HeaderWidth(0f, nameWidth, partWidth, numberWidth, padding, spacing, float.PositiveInfinity);
        }

        if (nameWidth > 0f && required > maxWidth)
        {
            float fixedWidth = partWidth + numberWidth + padding * 2f;
            int fixedSegments = (partWidth > 0f ? 1 : 0) + (numberWidth > 0f ? 1 : 0);
            float availableName = Mathf.Max(0f, maxWidth - fixedWidth - Mathf.Max(0, fixedSegments) * spacing);
            int maxChars = _labelName.Length;
            while (maxChars > 1 && nameWidth > availableName)
            {
                maxChars--;
                NameContent.text = BarPolicy.TrimName(_name, maxChars);
                nameWidth = Measure(NameContent, _nameStyle);
            }
        }

        Color nameColor = ConfigColor(Plugin.EnemyBarNameColor, ref _nameColorSource, ref _nameColor, DefaultText);
        Color partColor = _glueMode ? DefaultCfoamTag
            : ConfigColor(Plugin.EnemyBarWeakspotColor, ref _weakspotColorSource, ref _weakspotColor, DefaultWeakspot);
        Color numbersColor = ConfigColor(Plugin.EnemyBarHealthTextColor, ref _healthTextColorSource, ref _healthTextColor, DefaultText);
        numberWidth = Measure(NumbersContent, _numbersStyle);
        float rowWidth = BarPolicy.HeaderWidth(bar.width, nameWidth, partWidth, numberWidth, padding, spacing, maxWidth);
        var row = new Rect(bar.center.x - rowWidth * 0.5f, bar.y - (_nameStyle.fontSize + 8f * scale) - 2f * scale, rowWidth, _nameStyle.fontSize + 8f * scale);
        row.x = Mathf.Clamp(row.x, margin, Mathf.Max(margin, screenWidth - margin - rowWidth));
        row.y = Mathf.Max(row.y, 4f * scale);

        float left = row.x + padding;
        if (NameContent.text.Length > 0)
        {
            var nameRect = new Rect(left, row.y, nameWidth + scale, row.height);
            _nameOriginX = TextOriginX(nameRect, _nameStyle, nameWidth);
            _nameOriginValid = true;
            DrawLabelText(NameContent, nameRect, _nameStyle, nameColor, shadowColor, opacity, scale);
            left += nameWidth + (PartContent.text.Length > 0 ? spacing : 0f);
        }
        if (PartContent.text.Length > 0)
            DrawLabelText(PartContent, new Rect(left, row.y, partWidth + scale, row.height), _weakspotStyle, partColor, shadowColor, opacity, scale);
        if (NumbersContent.text.Length > 0)
        {
            float numberX = row.xMax - padding - numberWidth;
            DrawLabelText(NumbersContent, new Rect(numberX, row.y, numberWidth + scale, row.height), _numbersStyle, numbersColor, shadowColor, opacity, scale);
        }
    }

    // 1.2.23: one text-origin calculation shared by the name and the green row. GUIStyle.CalcSize only says how wide
    // the glyphs are; the real first glyph also includes the rect origin, alignment and the style's padding/offset.
    // A style with a centred rect but left-aligned text would NOT start at the rect centre, so both facts are used.
    private static float TextOriginX(Rect rect, GUIStyle style, float contentWidth)
        => BarPolicy.TextOriginX(rect.x, rect.width, contentWidth, style.padding.left, style.padding.right,
            style.contentOffset.x, AlignmentCode(style.alignment));

    private static int AlignmentCode(TextAnchor anchor)
    {
        switch (anchor)
        {
            case TextAnchor.UpperCenter:
            case TextAnchor.MiddleCenter:
            case TextAnchor.LowerCenter:
                return BarPolicy.TextAlignCenter;
            case TextAnchor.UpperRight:
            case TextAnchor.MiddleRight:
            case TextAnchor.LowerRight:
                return BarPolicy.TextAlignRight;
            default:
                return BarPolicy.TextAlignLeft;
        }
    }

    private static void EnsureLabel(bool showName, bool showPart, bool showNumbers, string targetTag)
    {
        int health = BarPolicy.DisplayValue(_health);
        int maxHealth = BarPolicy.DisplayValue(_maxHealth);
        if (health == _labelHealth && maxHealth == _labelMaxHealth &&
            showName == _labelNamed && showPart == _labelPartVisible && showNumbers == _labelNumbered &&
            _glueMode == _labelFoamMode && string.Equals(_labelSourceName, _name, StringComparison.Ordinal) &&
            string.Equals(_labelSourcePart, targetTag, StringComparison.Ordinal)) return;
        _labelHealth = health;
        _labelMaxHealth = maxHealth;
        _labelNamed = showName;
        _labelPartVisible = showPart;
        _labelNumbered = showNumbers;
        _labelFoamMode = _glueMode;
        _labelSourceName = _name;
        _labelSourcePart = targetTag;
        _labelName = showName ? BarPolicy.TrimName(_name) : string.Empty;
        _labelPart = showPart ? targetTag : string.Empty;
        _labelNumbers = showNumbers ? BarPolicy.Readout(_health, _maxHealth) : string.Empty;
    }

    private static void EnsureStyles()
    {
        if (_nameStyle != null) return;
        _nameStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleLeft,
            fontStyle = FontStyle.Bold,
            clipping = TextClipping.Clip,
            richText = false
        };
        _weakspotStyle = new GUIStyle(_nameStyle);
        _numbersStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleRight,
            fontStyle = FontStyle.Normal,
            clipping = TextClipping.Clip,
            richText = false
        };
        // 1.2.23: the row starts at the monster name's real first glyph, so it is left-aligned and inherits the name
        // style's padding/content offset. Its rect is placed from that origin - never a centred rect.
        _staggerStyle = new GUIStyle(_nameStyle)
        {
            alignment = TextAnchor.MiddleLeft,
            fontStyle = FontStyle.Normal,
            wordWrap = false
        };
    }

    private static void SetFontSize(int size)
    {
        _nameStyle.fontSize = size;
        _weakspotStyle.fontSize = size;
        _numbersStyle.fontSize = size;
        if (_staggerStyle != null) _staggerStyle.fontSize = size;
    }

    private static float Measure(GUIContent content, GUIStyle style)
        => content == null || string.IsNullOrEmpty(content.text) ? 0f : style.CalcSize(content).x;

    private static void DrawLabelText(GUIContent content, Rect rect, GUIStyle style, Color color, Color shadow,
        float opacity, float scale)
    {
        if (content == null || string.IsNullOrEmpty(content.text) || rect.width <= 0f) return;
        GUI.color = WithAlpha(shadow, 0.76f * opacity);
        GUI.Label(new Rect(rect.x + scale, rect.y + scale, rect.width, rect.height), content, style);
        GUI.color = WithAlpha(color, 0.96f * opacity);
        GUI.Label(rect, content, style);
    }

    private static Color ConfigColor(ConfigEntry<string> entry, ref string source, ref Color cached, Color fallback)
    {
        string value = entry != null ? entry.Value : null;
        if (string.Equals(value, source, StringComparison.Ordinal)) return cached;
        source = value;
        if (!BarPolicy.TryParseHexColor(value, out float red, out float green, out float blue, out float alpha))
        {
            cached = fallback;
            return cached;
        }
        cached = new Color(red, green, blue, alpha);
        return cached;
    }

    private static Color WithAlpha(Color color, float alpha)
        => new(color.r, color.g, color.b, color.a * alpha);

    private static void Fill(Rect rect, Color color)
    {
        if (rect.width <= 0f || rect.height <= 0f) return;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
    }
    // 1.2.23: one row directly under the bar, LEFT-ANCHORED to the monster name's real first glyph (or to the
    // health bar's left edge when ShowName is off). It reuses the existing shadow and fade alpha through
    // DrawLabelText, owns its font size, scales down before wrapping, wraps at group boundaries and clamps a
    // still-too-wide token with a visible ellipsis. It is never dropped merely because the screen is narrow.
    private static void DrawStaggerRow(Rect bar, int screenWidth, int screenHeight, float opacity, Color shadowColor)
    {
        if (Plugin.EnemyBarShowStaggerLine != null && !Plugin.EnemyBarShowStaggerLine.Value) return;
        if (string.IsNullOrEmpty(_labelStagger) || _staggerStyle == null) return;
        if (screenWidth <= 0 || screenHeight <= 0) return;
        float scale = BarPolicy.ResolutionScale(screenHeight);
        int baseFont = BarPolicy.StaggerFontPixels(
            Plugin.EnemyBarNumberSize != null ? Plugin.EnemyBarNumberSize.Value : BarPolicy.DefaultNumberSize, screenHeight);
        int minFont = BarPolicy.StaggerMinFontPixels(screenHeight);
        bool showName = Plugin.EnemyBarShowName == null || Plugin.EnemyBarShowName.Value;
        float targetLeft = showName && _nameOriginValid ? _nameOriginX : bar.x;
        float available = BarPolicy.StaggerAvailableWidth(targetLeft, screenWidth, screenHeight);
        if (available <= 0f)
        {
            targetLeft = BarPolicy.StaggerRowMargin(screenHeight);
            available = BarPolicy.StaggerAvailableWidth(targetLeft, screenWidth, screenHeight);
            if (available <= 0f) return;
        }

        string language = _partLanguage ?? "zh";
        BarPolicy.StaggerTokens(_stagger, language, out string limbToken, out string torsoToken, out string normalToken);
        string full = limbToken + torsoToken + normalToken;
        int font = baseFont;
        while (font > minFont && MeasureAt(full, font) > available) font--;
        float fullWidth = MeasureAt(full, font);
        int rows = BarPolicy.StaggerRows(available, fullWidth,
            MeasureAt(limbToken + torsoToken, font),
            Mathf.Max(MeasureAt(limbToken, font), Mathf.Max(MeasureAt(torsoToken, font), MeasureAt(normalToken, font))));
        string[] lines = rows == 1 ? new[] { full }
            : rows == 2 ? new[] { limbToken + torsoToken, normalToken }
            : new[] { limbToken, torsoToken, normalToken };
        float width = 0f;
        for (int i = 0; i < lines.Length; i++)
        {
            lines[i] = ClampToWidth(lines[i], font, available);
            float measured = MeasureAt(lines[i], font);
            if (measured > width) width = measured;
        }

        _staggerStyle.fontSize = font;
        float rowHeight = font + 8f * scale;
        float totalHeight = rowHeight * lines.Length;
        float y = BarPolicy.StaggerRowY(bar.yMax, screenHeight);
        float bottom = screenHeight - BarPolicy.StaggerRowMargin(screenHeight);
        if (y + totalHeight > bottom) y = Mathf.Max(bar.yMax + 1f, bottom - totalHeight);
        float x = BarPolicy.StaggerRowLeft(targetLeft, width, screenWidth, screenHeight)
            - (_staggerStyle.padding.left + _staggerStyle.contentOffset.x);
        if (x < 0f) x = 0f;
        BarPolicy.StaggerInk(out float staggerRed, out float staggerGreen, out float staggerBlue);
        Color body = new Color(staggerRed, staggerGreen, staggerBlue, 1f);
        for (int i = 0; i < lines.Length; i++)
        {
            StaggerContent.text = lines[i];
            DrawLabelText(StaggerContent, new Rect(x, y + i * rowHeight, width + scale, rowHeight),
                _staggerStyle, body, shadowColor, opacity, scale);
        }
    }

    private static float MeasureAt(string text, int font)
    {
        if (string.IsNullOrEmpty(text)) return 0f;
        _staggerStyle.fontSize = font;
        StaggerContent.text = text;
        return _staggerStyle.CalcSize(StaggerContent).x;
    }

    private static string ClampToWidth(string text, int font, float maxWidth)
    {
        if (string.IsNullOrEmpty(text) || !KillPolicy.IsFinite(maxWidth) || maxWidth <= 0f) return string.Empty;
        if (MeasureAt(text, font) <= maxWidth) return text;
        int low = 0, high = text.Length;
        while (low < high)
        {
            int mid = (low + high + 1) / 2;
            StaggerContent.text = text.Substring(0, mid) + "…";
            if (_staggerStyle.CalcSize(StaggerContent).x <= maxWidth) low = mid;
            else high = mid - 1;
        }
        return low <= 0 ? "…" : text.Substring(0, low) + "…";
    }

    // 1.2.22/1.2.23: the two part groups and the normal hitreact, read from the focused enemy only (no scene scan,
    // no network).
    //   limbs/torso - every live limb the GAME treats as destroyable: the same predicate the shipped dismemberment
    //            path uses (CD_CharacterDestructionCollider.HasDestruction() and not IsDestroyed). The group comes
    //            from that same collider's anatomical field m_bodyPart (CharacterDestruction.CD_BodypartType):
    //            upper/lower arm and upper/lower leg -> 四肢, chest/stomach/hips -> 躯体. The head limb is skipped by
    //            pointer AND by part; Unspecified/Head/any mod value is refused, never guessed from a number, an
    //            array index, a GameObject name or the enemy id. Current durability (m_health) and fixed capacity
    //            (m_healthMax) stay separate; a group with no live breakable part is the dash, so a body with only
    //            whole-body hitreact cannot fake a torso value.
    //   normal - the game's own normal-hitreact budget, read live: GameData.HealthData.DamageUntilHitreact minus the
    //            running accumulator Dam_EnemyDamageBase.m_damBuildToHitreact (both through the same properties a
    //            MTFO/EEC-edited install uses). Native proof that this is an ABSOLUTE damage amount compared against
    //            an accumulated damage sum, with no ratio and no HealthMax factor, is quoted in BarPolicy
    //            (ProcessReceivedDamage RVA 0x137E570: addss [rbx+0x150] ... comiss [rax+0x14] ... jae ... reset).
    //            The row therefore prints the remaining damage; if either side is unreadable the row prints the
    //            no-data dash and one bounded diagnostic carries the raw values for later reconciliation.
    private static BarPolicy.StaggerValues ReadStagger(EnemyAgent enemy)
    {
        var limbGroup = new BarPolicy.StaggerGroupBuilder();
        var torsoGroup = new BarPolicy.StaggerGroupBuilder();
        try
        {
            Dam_EnemyDamageBase damage = enemy.Damage;
            var limbs = damage != null ? damage.DamageLimbs : null;
            long headPointer = 0;
            try { var head = enemy.m_headLimb; if (head != null && head.Pointer != IntPtr.Zero) headPointer = head.Pointer.ToInt64(); }
            catch (Exception error) { Warn("stagger-head", error.Message); }
            if (limbs != null)
            {
                for (int i = 0; i < limbs.Length; i++)
                {
                    var limb = limbs[i];
                    if (limb == null) continue;
                    bool destroyed;
                    float remaining, capacity;
                    int bodyPart;
                    try
                    {
                        if (headPointer != 0 && limb.Pointer != IntPtr.Zero && limb.Pointer.ToInt64() == headPointer) continue;
                        destroyed = limb.IsDestroyed;
                        var destruction = limb.m_destruction;
                        if (destruction == null || !destruction.HasDestruction()) continue;   // not a destroyable limb
                        bodyPart = (int)destruction.m_bodyPart;                                 // anatomical enum, not eLimbDamageType
                        remaining = limb.m_health;
                        capacity = limb.m_healthMax;
                    }
                    catch (Exception error) { Warn("stagger-limb", error.Message); continue; }
                    if (destroyed) continue;
                    BarPolicy.StaggerBodyGroup group = BarPolicy.BodyGroup(bodyPart);
                    if (group == BarPolicy.StaggerBodyGroup.Limbs) limbGroup.Add(remaining, capacity);
                    else if (group == BarPolicy.StaggerBodyGroup.Torso) torsoGroup.Add(remaining, capacity);
                    else if (LogStaggerPartBudget.TryUse(4))
                        Plugin.Instance?.Log.LogInfo($"[BAR] stagger=part;grouped=no;bodyPart={bodyPart};limbIndex={i};note=unspecified-head-or-mod-part-not-a-limb-or-torso");
                }
            }
        }
        catch (Exception error) { Warn("stagger-limbs", error.Message); }

        bool hasNormal = false;
        float threshold = 0f, accumulated = 0f, normalLeft = 0f;
        try
        {
            GameData.EnemyBalancingDataBlock balancing = enemy.EnemyBalancingData;
            GameData.HealthData data = balancing != null ? balancing.Health : null;
            Dam_EnemyDamageBase damage = enemy.Damage;
            if (data != null && damage != null)
            {
                threshold = data.DamageUntilHitreact;
                accumulated = damage.m_damBuildToHitreact;
                // The game's own formula: the field is an absolute damage budget, so what a hit still needs is
                // max(0, threshold - accumulator). No ratio, no HealthMax factor, no ceiling rounding.
                hasNormal = BarPolicy.StaggerRemaining(threshold, accumulated, out normalLeft);
            }
        }
        catch (Exception error) { Warn("stagger-hitreact", error.Message); }
        if (!hasNormal && LogStaggerBudget != null && LogStaggerBudget.TryUse(4))
            Plugin.Instance?.Log.LogInfo($"[BAR] stagger=normal;shown=no;reason={(threshold > 0f ? "accumulator-unreadable" : "config-absent")};damageUntilHitreact={threshold:0.###};damBuildToHitreact={accumulated:0.###};note=dash-instead-no-conversion-applied");
        return new BarPolicy.StaggerValues(limbGroup.Build(), torsoGroup.Build(), hasNormal, normalLeft);
    }


    // Single read path, unchanged since 1.2.9: the bar mirrors the enemy's own Damage.Health/HealthMax.
    // With DamageSync installed on both machines the client's Health is host-authoritative, so no
    // local source-selection seam is needed here.
    private static void ReadHealth(EnemyAgent enemy, out float health, out float maxHealth)
    {
        health = 0f;
        maxHealth = 0f;
        try
        {
            Dam_EnemyDamageBase damage = enemy.Damage;
            if (damage == null) return;
            health = damage.Health;
            maxHealth = damage.HealthMax;
        }
        catch { }
    }

    private static int SafeId(EnemyAgent enemy)
    {
        try { return enemy.GetInstanceID(); } catch { return int.MinValue; }
    }

    private static uint SafeDataId(EnemyAgent enemy)
    {
        try { return enemy.EnemyDataID; } catch { return 0u; }
    }

    // Hard stop: no target, no cached anchor, fade already finished, so nothing can be drawn.
    private static void Drop()
    {
        _target = null;
        _haveTarget = false;
        _hasAnchor = false;
        _shownFor = 0f;
        _lostFor = BarPolicy.DroppedLostSeconds;
    }

    private static void LogFocus(bool focused)
    {
        if (focused == _wasFocused) return;
        _wasFocused = focused;
        if (Plugin.EnemyBarLogFocus == null || !Plugin.EnemyBarLogFocus.Value) return;
        if (FocusChangeBudget.TryUse(16))
            Plugin.Instance?.Log.LogInfo($"[BAR] reason={EnemyFocus.Reason};focused={focused};hits={EnemyFocus.HitsSeen};enemyHits={EnemyFocus.EnemyHits};mask=0x{EnemyFocus.MaskValue:X8};distance={EnemyFocus.Distance:0.0}m");
    }

    private static void Warn(string stage, string detail)
    {
        if (WarningBudget.TryUse(5)) Plugin.Instance?.Log.LogWarning("[BAR] " + stage + ": " + detail);
    }
}
