using System;
using BepInEx.Configuration;
using BepInEx.Core.Logging.Interpolation;
using BepInEx.Logging;
using Enemies;
using UnityEngine;

namespace HawChow.GTFOKillFeed;

internal static class EnemyBar
{
	private static readonly Color DefaultBorder = new Color(0.043f, 0.055f, 0.062f, 1f);

	private static readonly Color DefaultTrack = new Color(0.086f, 0.1f, 0.11f, 1f);

	private static readonly Color DefaultGhost = new Color(0.66f, 0.69f, 0.7f, 1f);

	private static readonly Color DefaultText = new Color(0.91f, 0.925f, 0.92f, 1f);

	private static readonly Color DefaultWeakspot = new Color(0.937f, 0.298f, 0.298f, 1f);

	private static readonly Color DefaultCfoamTag = new Color(0.9490196f, 0.6509804f, 0.3529412f, 1f);

	private static readonly Color DefaultFillYellow = new Color(0.902f, 0.722f, 0.298f, 1f);

	private const float WeakspotRefreshSeconds = 0.35f;

	private static readonly Color DefaultFillRed = new Color(0.937f, 0.298f, 0.298f, 1f);

	private static Color _nameColor = DefaultText;

	private static Color _weakspotColor = DefaultWeakspot;

	private static Color _healthTextColor = DefaultText;

	private static Color _fillColor = DefaultText;

	private static Color _fillYellowColor = DefaultFillYellow;

	private static Color _fillRedColor = DefaultFillRed;

	private static Color _ghostColor = DefaultGhost;

	private static Color _trackColor = DefaultTrack;

	private static Color _borderColor = DefaultBorder;

	private static string _nameColorSource;

	private static string _weakspotColorSource;

	private static string _healthTextColorSource;

	private static string _fillColorSource;

	private static string _fillYellowColorSource;

	private static string _fillRedColorSource;

	private static string _ghostColorSource;

	private static string _trackColorSource;

	private static string _borderColorSource;

	private static GUIStyle _nameStyle;

	private static GUIStyle _weakspotStyle;

	private static GUIStyle _numbersStyle;

	private static readonly GUIContent NameContent = new GUIContent();

	private static readonly GUIContent PartContent = new GUIContent();

	private static readonly GUIContent NumbersContent = new GUIContent();

	private static EnemyAgent _target;

	private static string _name = string.Empty;

	private static string _weakspotPart = string.Empty;

	private static string _partLanguage;

	private static string _foamTag = string.Empty;

	private static bool _glueMode;

	private static int _nameRevision = -1;

	private static float _weakspotRefreshAt = -1f / 0f;

	private static int _targetId = -2147483648;

	private static float _health;

	private static float _maxHealth;

	private static float _ghost;

	private static float _shownFor;

	private static float _lostFor;

	private static bool _haveTarget;

	private static bool _wasFocused;

	private static float _lastNow;

	private static readonly DiagnosticBudget WarningBudget = new DiagnosticBudget();

	private static readonly DiagnosticBudget FocusChangeBudget = new DiagnosticBudget();

	private static readonly DiagnosticBudget FocusAcquiredBudget = new DiagnosticBudget();

	private static bool _hasAnchor;

	private static float _anchorX;

	private static float _anchorY;

	private static string _labelName = string.Empty;

	private static string _labelPart = string.Empty;

	private static string _labelNumbers = string.Empty;

	private static string _labelSourceName;

	private static string _labelSourcePart;

	private static int _labelHealth = -2147483648;

	private static int _labelMaxHealth = -2147483648;

	private static bool _labelNamed;

	private static bool _labelPartVisible;

	private static bool _labelNumbered;

	private static bool _labelFoamMode;

	internal static void Reset()
	{
		_target = null;
		_name = string.Empty;
		_weakspotPart = string.Empty;
		_foamTag = string.Empty;
		_glueMode = false;
		_partLanguage = null;
		_weakspotRefreshAt = -1f / 0f;
		_nameRevision = -1;
		_targetId = -2147483648;
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
		_labelHealth = -2147483648;
		_labelMaxHealth = -2147483648;
		_labelNamed = false;
		_labelPartVisible = false;
		_labelNumbered = false;
		_labelFoamMode = false;
		EnemyFocus.Reset();
	}

	internal static void Tick(float now, bool hudVisible)
	{
		try
		{
			float num = ((_lastNow <= 0f) ? 0f : Mathf.Clamp(now - _lastNow, 0f, 0.25f));
			_lastNow = now;
			if (Plugin.Enabled == null || !Plugin.Enabled.Value || Plugin.EnemyBarEnabled == null || !Plugin.EnemyBarEnabled.Value || !hudVisible)
			{
				Drop();
				return;
			}
			EnemyFocus.Update(Plugin.EnemyBarMaxDistance.Value);
			EnemyAgent target = EnemyFocus.Target;
			bool num2 = target != null && BarPolicy.ShouldShow(hasTarget: true, EnemyFocus.Distance, Plugin.EnemyBarMaxDistance.Value);
			LogFocus(num2);
			if (!num2)
			{
				_target = null;
				if (_haveTarget)
				{
					_haveTarget = false;
					_lostFor = 0f;
				}
				else if (_lostFor < 0.35000002f)
				{
					_lostFor += num;
				}
				return;
			}
			int num3 = SafeId(target);
			bool num4 = !_haveTarget || (num3 != -2147483648 && _targetId != -2147483648 && num3 != _targetId);
			string text = ((Plugin.Language != null) ? Plugin.Language.Value : "zh");
			if (num4)
			{
				_shownFor = 0f;
				_hasAnchor = false;
				ReadHealth(target, out _health, out _maxHealth);
				_ghost = BarPolicy.FillFraction(_health, _maxHealth);
			}
			if (num4 || _nameRevision != EnemyNames.Revision)
			{
				_name = EnemyNames.Resolve(SafeDataId(target), EnemyFocus.RawNameOf(target));
				_nameRevision = EnemyNames.Revision;
			}
			bool flag = !string.Equals(_partLanguage, text, (StringComparison)5);
			bool glueGunWielded = EnemyFocus.GlueGunWielded;
			bool flag2 = glueGunWielded != _glueMode;
			if (num4 || flag || flag2 || now - _weakspotRefreshAt >= 0.35f)
			{
				_glueMode = glueGunWielded;
				if (_glueMode)
				{
					_weakspotPart = string.Empty;
					_foamTag = BarPolicy.CfoamTag(EnemyFocus.GlueToleranceOf(target));
				}
				else
				{
					string text2 = EnemyFocus.ResolveWeakspotParts(target, text);
					if (!string.Equals(_weakspotPart, text2, (StringComparison)4))
					{
						_weakspotPart = text2;
					}
					_foamTag = string.Empty;
				}
				_partLanguage = text;
				_weakspotRefreshAt = now;
			}
			if (num4 && FocusAcquiredBudget.TryUse(12))
			{
				Plugin instance = Plugin.Instance;
				if (instance != null)
				{
					ManualLogSource log = instance.Log;
					bool isEnabled;
					BepInExInfoLogInterpolatedStringHandler bepInExInfoLogInterpolatedStringHandler = new BepInExInfoLogInterpolatedStringHandler(79, 6, out isEnabled);
					if (isEnabled)
					{
						bepInExInfoLogInterpolatedStringHandler.AppendLiteral("[BAR] focus=acquired;enemyId=");
						bepInExInfoLogInterpolatedStringHandler.AppendFormatted(SafeDataId(target));
						bepInExInfoLogInterpolatedStringHandler.AppendLiteral(";name=");
						bepInExInfoLogInterpolatedStringHandler.AppendFormatted(_name);
						bepInExInfoLogInterpolatedStringHandler.AppendLiteral(";weakspotTags=");
						bepInExInfoLogInterpolatedStringHandler.AppendFormatted(_weakspotPart);
						bepInExInfoLogInterpolatedStringHandler.AppendLiteral(";distance=");
						((BepInExLogInterpolatedStringHandler)bepInExInfoLogInterpolatedStringHandler).AppendFormatted<float>(EnemyFocus.Distance, "0.0");
						bepInExInfoLogInterpolatedStringHandler.AppendLiteral("m;health=");
						bepInExInfoLogInterpolatedStringHandler.AppendFormatted(BarPolicy.DisplayValue(_health));
						bepInExInfoLogInterpolatedStringHandler.AppendLiteral(";maxHealth=");
						bepInExInfoLogInterpolatedStringHandler.AppendFormatted(BarPolicy.DisplayValue(_maxHealth));
					}
					log.LogInfo(bepInExInfoLogInterpolatedStringHandler);
				}
			}
			_targetId = num3;
			_target = target;
			_haveTarget = true;
			_lostFor = 0f;
			_shownFor += num;
			ReadHealth(target, out _health, out _maxHealth);
			float num5 = BarPolicy.FillFraction(_health, _maxHealth);
			_ghost = BarPolicy.GhostDecay(Mathf.Max(_ghost, num5), num5, num);
		}
		catch (System.Exception ex)
		{
			Drop();
			Warn("tick", ex.Message);
		}
	}

	internal static void Draw()
	{
		float num = BarPolicy.AlphaFor(_haveTarget, _shownFor, _lostFor);
		if (!BarPolicy.ShouldDraw(_haveTarget, _hasAnchor, num))
		{
			return;
		}
		int width = Screen.width;
		int height = Screen.height;
		if (width <= 0 || height <= 0)
		{
			return;
		}
		Color color = GUI.color;
		try
		{
			EnsureStyles();
			Color color2 = ConfigColor(Plugin.EnemyBarBorderColor, ref _borderColorSource, ref _borderColor, DefaultBorder);
			Color color3 = ConfigColor(Plugin.EnemyBarTrackColor, ref _trackColorSource, ref _trackColor, DefaultTrack);
			Color color4 = ConfigColor(Plugin.EnemyBarGhostColor, ref _ghostColorSource, ref _ghostColor, DefaultGhost);
			Color color5 = ConfigColor(Plugin.EnemyBarFillColor, ref _fillColorSource, ref _fillColor, DefaultText);
			Color color6 = ConfigColor(Plugin.EnemyBarFillYellowColor, ref _fillYellowColorSource, ref _fillYellowColor, DefaultFillYellow);
			Color color7 = ConfigColor(Plugin.EnemyBarFillRedColor, ref _fillRedColorSource, ref _fillRedColor, DefaultFillRed);
			float num2 = BarPolicy.Width(Plugin.EnemyBarWidth.Value, height, width);
			float num3 = BarPolicy.Height(Plugin.EnemyBarHeight.Value, height);
			float num4 = BarPolicy.Opacity(Plugin.EnemyBarOpacity.Value) * num;
			float offset = BarPolicy.VerticalOffset(Plugin.EnemyBarVerticalOffset.Value);
			if (!TryResolveAnchor(width, height, offset, out var centreX, out var topY))
			{
				return;
			}
			topY = BarPolicy.AnchorY(topY, height, Plugin.EnemyBarMinScreenFraction.Value, Plugin.EnemyBarMaxScreenFraction.Value);
			centreX = BarPolicy.AnchorX(centreX, num2, width, height);
			if (_haveTarget)
			{
				_anchorX = centreX;
				_anchorY = topY;
				_hasAnchor = true;
			}
			Rect rect = new Rect(centreX - num2 * 0.5f, topY, num2, num3);
			float num5 = BarPolicy.FillFraction(_health, _maxHealth);
			float fraction = ((KillPolicy.IsFinite(_health) && KillPolicy.IsFinite(_maxHealth) && _maxHealth > 0f) ? Mathf.Clamp01(_health / _maxHealth) : (0f / 0f));
			float yellowThresholdPercent = ((Plugin.EnemyBarYellowThresholdPercent != null) ? Plugin.EnemyBarYellowThresholdPercent.Value : 66f);
			float redThresholdPercent = ((Plugin.EnemyBarRedThresholdPercent != null) ? Plugin.EnemyBarRedThresholdPercent.Value : 33f);
			Color color8 = BarPolicy.HealthColorBand(fraction, yellowThresholdPercent, redThresholdPercent) switch
			{
				BarPolicy.HealthBand.Yellow => color6, 
				BarPolicy.HealthBand.Red => color7, 
				_ => color5, 
			};
			float num6 = Mathf.Clamp(_ghost, num5, 1f);
			float num7 = Mathf.Max(1f, Mathf.Round(num3 * 0.36f));
			Fill(new Rect(rect.x - num7, rect.y - num7, rect.width + num7 * 2f, rect.height + num7 * 2f), WithAlpha(color2, 0.45f * num4));
			Fill(rect, WithAlpha(color3, 0.28f * num4));
			if (num6 > num5)
			{
				Fill(new Rect(rect.x, rect.y, rect.width * num6, rect.height), WithAlpha(color4, 0.22f * num4));
			}
			if (num5 > 0f)
			{
				Fill(new Rect(rect.x, rect.y, rect.width * num5, rect.height), WithAlpha(color8, 0.9f * num4));
			}
			for (int i = 1; i < 4; i++)
			{
				float num8 = BarPolicy.TickFraction(i, 4);
				if (!(num8 < 0f))
				{
					Fill(new Rect(rect.x + rect.width * num8, rect.y, num7, rect.height), WithAlpha(color2, 0.35f * num4));
				}
			}
			DrawFocusOutline(rect, num7, num3, num4);
			DrawLabel(rect, width, height, num4, color2);
		}
		catch (System.Exception ex)
		{
			Warn("draw", ex.Message);
		}
		finally
		{
			GUI.color = color;
		}
	}

	private static void DrawFocusOutline(Rect rect, float edge, float barHeight, float opacity)
	{
		bool enabled = Plugin.EnemyBarFocusOutlineEnabled == null || Plugin.EnemyBarFocusOutlineEnabled.Value;
		FocusOutlineKind focusOutlineKind = FocusOutlinePolicy.Classify(FocusOutlinePolicy.IsBehind(EnemyFocus.BackDot), EnemyFocus.HitIsWeakspot);
		if (!FocusOutlinePolicy.ShouldDraw(enabled, focusOutlineKind))
		{
			return;
		}
		float num = ((focusOutlineKind == FocusOutlineKind.Prismatic) ? FocusOutlinePolicy.FoilThickness(barHeight) : FocusOutlinePolicy.Thickness(barHeight));
		float phase = DamageFxPolicy.PhaseFor(Time.unscaledTime);
		float num2 = rect.x - edge - num;
		float num3 = rect.y - edge - num;
		float num4 = rect.width + (edge + num) * 2f;
		float num5 = rect.height + (edge + num) * 2f;
		if (focusOutlineKind != FocusOutlineKind.Prismatic)
		{
			FocusOutlinePolicy.OutlineColor(focusOutlineKind, phase, out var red, out var green, out var blue);
			Color color = WithAlpha(new Color(red, green, blue), opacity);
			Fill(new Rect(num2, num3, num4, num), color);
			Fill(new Rect(num2, num3 + num5 - num, num4, num), color);
			Fill(new Rect(num2, num3, num, num5), color);
			Fill(new Rect(num2 + num4 - num, num3, num, num5), color);
			return;
		}
		int num6 = 5;
		int total = num6 * 4;
		float num7 = num4 / (float)num6;
		float num8 = num5 / (float)num6;
		for (int i = 0; i < num6; i++)
		{
			FocusOutlinePolicy.FoilFrameColor(i, total, phase, out var red2, out var green2, out var blue2);
			Fill(new Rect(num2 + (float)i * num7, num3, num7 + 0.5f, num), WithAlpha(new Color(red2, green2, blue2), opacity));
			FocusOutlinePolicy.FoilFrameColor(num6 + i, total, phase, out red2, out green2, out blue2);
			Fill(new Rect(num2 + num4 - (float)(i + 1) * num7, num3 + num5 - num, num7 + 0.5f, num), WithAlpha(new Color(red2, green2, blue2), opacity));
			FocusOutlinePolicy.FoilFrameColor(2 * num6 + i, total, phase, out red2, out green2, out blue2);
			Fill(new Rect(num2 + num4 - num, num3 + (float)i * num8, num, num8 + 0.5f), WithAlpha(new Color(red2, green2, blue2), opacity));
			FocusOutlinePolicy.FoilFrameColor(3 * num6 + i, total, phase, out red2, out green2, out blue2);
			Fill(new Rect(num2, num3 + num5 - (float)(i + 1) * num8, num, num8 + 0.5f), WithAlpha(new Color(red2, green2, blue2), opacity));
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
				centreX = (float)width * 0.5f;
				topY = BarPolicy.ScreenAnchorY(Plugin.EnemyBarScreenAnchor.Value, height) + offset;
				return true;
			}
			try
			{
				Camera currentCamera = CameraManager.GetCurrentCamera();
				if (currentCamera == null)
				{
					return false;
				}
				if (!EnemyFocus.TryGetHeadWorld(_target, BarPolicy.HeadOffset(Plugin.EnemyBarHeadOffset.Value), out var world))
				{
					return false;
				}
				Vector3 vector = currentCamera.WorldToScreenPoint(world);
				if (vector.z <= 0.1f)
				{
					return false;
				}
				centreX = vector.x;
				topY = (float)height - vector.y + offset;
				return true;
			}
			catch
			{
				return false;
			}
		}
		if (!_hasAnchor)
		{
			return false;
		}
		centreX = _anchorX;
		topY = _anchorY;
		return true;
	}

	private static void DrawLabel(Rect bar, int screenWidth, int screenHeight, float opacity, Color shadowColor)
	{
		bool num = Plugin.EnemyBarShowName == null || Plugin.EnemyBarShowName.Value;
		bool showWeakspotPart = Plugin.EnemyBarShowWeakspotPart == null || Plugin.EnemyBarShowWeakspotPart.Value;
		bool showNumbers = Plugin.EnemyBarShowNumbers == null || Plugin.EnemyBarShowNumbers.Value;
		string text = BarPolicy.SelectEnemyTag(_glueMode, showWeakspotPart, _weakspotPart, _foamTag);
		bool showPart = num && text.Length > 0;
		EnsureLabel(num, showPart, showNumbers, text);
		if (_labelName.Length == 0 && _labelPart.Length == 0 && _labelNumbers.Length == 0)
		{
			return;
		}
		float num2 = BarPolicy.ResolutionScale(screenHeight);
		int fontSize = Mathf.RoundToInt(BarPolicy.NumberSize(Plugin.EnemyBarNumberSize.Value, screenHeight));
		int num3 = Mathf.Max(7, Mathf.RoundToInt(8f * num2));
		SetFontSize(fontSize);
		NameContent.text = _labelName;
		PartContent.text = _labelPart;
		NumbersContent.text = _labelNumbers;
		float num4 = 7f * num2;
		float num5 = 6f * num2;
		float num6 = 6f * num2;
		float num7 = Mathf.Max(1f, (float)screenWidth - num6 * 2f);
		float num8 = Measure(NameContent, _nameStyle);
		float num9 = Measure(PartContent, _weakspotStyle);
		float num10 = Measure(NumbersContent, _numbersStyle);
		float num11 = BarPolicy.HeaderWidth(0f, num8, num9, num10, num4, num5, 1f / 0f);
		while (num11 > num7 && _nameStyle.fontSize > num3)
		{
			SetFontSize(_nameStyle.fontSize - 1);
			num8 = Measure(NameContent, _nameStyle);
			num9 = Measure(PartContent, _weakspotStyle);
			num10 = Measure(NumbersContent, _numbersStyle);
			num11 = BarPolicy.HeaderWidth(0f, num8, num9, num10, num4, num5, 1f / 0f);
		}
		if (num8 > 0f && num11 > num7)
		{
			float num12 = num9 + num10 + num4 * 2f;
			int b = ((num9 > 0f) ? 1 : 0) + ((num10 > 0f) ? 1 : 0);
			float num13 = Mathf.Max(0f, num7 - num12 - (float)Mathf.Max(0, b) * num5);
			int num14 = _labelName.Length;
			while (num14 > 1 && num8 > num13)
			{
				num14--;
				NameContent.text = BarPolicy.TrimName(_name, num14);
				num8 = Measure(NameContent, _nameStyle);
			}
		}
		Color color = ConfigColor(Plugin.EnemyBarNameColor, ref _nameColorSource, ref _nameColor, DefaultText);
		Color color2 = (_glueMode ? DefaultCfoamTag : ConfigColor(Plugin.EnemyBarWeakspotColor, ref _weakspotColorSource, ref _weakspotColor, DefaultWeakspot));
		Color color3 = ConfigColor(Plugin.EnemyBarHealthTextColor, ref _healthTextColorSource, ref _healthTextColor, DefaultText);
		num10 = Measure(NumbersContent, _numbersStyle);
		float num15 = BarPolicy.HeaderWidth(bar.width, num8, num9, num10, num4, num5, num7);
		Rect rect = new Rect(bar.center.x - num15 * 0.5f, bar.y - ((float)_nameStyle.fontSize + 8f * num2) - 2f * num2, num15, (float)_nameStyle.fontSize + 8f * num2);
		rect.x = Mathf.Clamp(rect.x, num6, Mathf.Max(num6, (float)screenWidth - num6 - num15));
		rect.y = Mathf.Max(rect.y, 4f * num2);
		float num16 = rect.x + num4;
		if (NameContent.text.Length > 0)
		{
			DrawLabelText(NameContent, new Rect(num16, rect.y, num8 + num2, rect.height), _nameStyle, color, shadowColor, opacity, num2);
			num16 += num8 + ((PartContent.text.Length > 0) ? num5 : 0f);
		}
		if (PartContent.text.Length > 0)
		{
			DrawLabelText(PartContent, new Rect(num16, rect.y, num9 + num2, rect.height), _weakspotStyle, color2, shadowColor, opacity, num2);
		}
		if (NumbersContent.text.Length > 0)
		{
			float x = rect.xMax - num4 - num10;
			DrawLabelText(NumbersContent, new Rect(x, rect.y, num10 + num2, rect.height), _numbersStyle, color3, shadowColor, opacity, num2);
		}
	}

	private static void EnsureLabel(bool showName, bool showPart, bool showNumbers, string targetTag)
	{
		int num = BarPolicy.DisplayValue(_health);
		int num2 = BarPolicy.DisplayValue(_maxHealth);
		if (num != _labelHealth || num2 != _labelMaxHealth || showName != _labelNamed || showPart != _labelPartVisible || showNumbers != _labelNumbered || _glueMode != _labelFoamMode || !string.Equals(_labelSourceName, _name, (StringComparison)4) || !string.Equals(_labelSourcePart, targetTag, (StringComparison)4))
		{
			_labelHealth = num;
			_labelMaxHealth = num2;
			_labelNamed = showName;
			_labelPartVisible = showPart;
			_labelNumbered = showNumbers;
			_labelFoamMode = _glueMode;
			_labelSourceName = _name;
			_labelSourcePart = targetTag;
			_labelName = (showName ? BarPolicy.TrimName(_name) : string.Empty);
			_labelPart = (showPart ? targetTag : string.Empty);
			_labelNumbers = (showNumbers ? BarPolicy.Readout(_health, _maxHealth) : string.Empty);
		}
	}

	private static void EnsureStyles()
	{
		if (_nameStyle == null)
		{
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
		}
	}

	private static void SetFontSize(int size)
	{
		_nameStyle.fontSize = size;
		_weakspotStyle.fontSize = size;
		_numbersStyle.fontSize = size;
	}

	private static float Measure(GUIContent content, GUIStyle style)
	{
		if (content != null && !string.IsNullOrEmpty(content.text))
		{
			return style.CalcSize(content).x;
		}
		return 0f;
	}

	private static void DrawLabelText(GUIContent content, Rect rect, GUIStyle style, Color color, Color shadow, float opacity, float scale)
	{
		if (content != null && !string.IsNullOrEmpty(content.text) && !(rect.width <= 0f))
		{
			GUI.color = WithAlpha(shadow, 0.76f * opacity);
			GUI.Label(new Rect(rect.x + scale, rect.y + scale, rect.width, rect.height), content, style);
			GUI.color = WithAlpha(color, 0.96f * opacity);
			GUI.Label(rect, content, style);
		}
	}

	private static Color ConfigColor(ConfigEntry<string> entry, ref string source, ref Color cached, Color fallback)
	{
		string text = entry?.Value;
		if (string.Equals(text, source, (StringComparison)4))
		{
			return cached;
		}
		source = text;
		if (!BarPolicy.TryParseHexColor(text, out var red, out var green, out var blue, out var alpha))
		{
			cached = fallback;
			return cached;
		}
		cached = new Color(red, green, blue, alpha);
		return cached;
	}

	private static Color WithAlpha(Color color, float alpha)
	{
		return new Color(color.r, color.g, color.b, color.a * alpha);
	}

	private static void Fill(Rect rect, Color color)
	{
		if (!(rect.width <= 0f) && !(rect.height <= 0f))
		{
			GUI.color = color;
			GUI.DrawTexture(rect, Texture2D.whiteTexture);
		}
	}

	private static void ReadHealth(EnemyAgent enemy, out float health, out float maxHealth)
	{
		health = 0f;
		maxHealth = 0f;
		try
		{
			Dam_EnemyDamageBase damage = enemy.Damage;
			if (!(damage == null))
			{
				health = damage.Health;
				maxHealth = damage.HealthMax;
			}
		}
		catch
		{
		}
	}

	private static int SafeId(EnemyAgent enemy)
	{
		try
		{
			return enemy.GetInstanceID();
		}
		catch
		{
			return -2147483648;
		}
	}

	private static uint SafeDataId(EnemyAgent enemy)
	{
		try
		{
			return enemy.EnemyDataID;
		}
		catch
		{
			return 0u;
		}
	}

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
		if (focused == _wasFocused)
		{
			return;
		}
		_wasFocused = focused;
		if (Plugin.EnemyBarLogFocus == null || !Plugin.EnemyBarLogFocus.Value || !FocusChangeBudget.TryUse(16))
		{
			return;
		}
		Plugin instance = Plugin.Instance;
		if (instance != null)
		{
			ManualLogSource log = instance.Log;
			bool isEnabled;
			BepInExInfoLogInterpolatedStringHandler bepInExInfoLogInterpolatedStringHandler = new BepInExInfoLogInterpolatedStringHandler(58, 6, out isEnabled);
			if (isEnabled)
			{
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral("[BAR] reason=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(EnemyFocus.Reason);
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral(";focused=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(focused);
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral(";hits=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(EnemyFocus.HitsSeen);
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral(";enemyHits=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(EnemyFocus.EnemyHits);
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral(";mask=0x");
				((BepInExLogInterpolatedStringHandler)bepInExInfoLogInterpolatedStringHandler).AppendFormatted<int>(EnemyFocus.MaskValue, "X8");
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral(";distance=");
				((BepInExLogInterpolatedStringHandler)bepInExInfoLogInterpolatedStringHandler).AppendFormatted<float>(EnemyFocus.Distance, "0.0");
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral("m");
			}
			log.LogInfo(bepInExInfoLogInterpolatedStringHandler);
		}
	}

	private static void Warn(string stage, string detail)
	{
		if (WarningBudget.TryUse(5))
		{
			Plugin.Instance?.Log.LogWarning("[BAR] " + stage + ": " + detail);
		}
	}
}
