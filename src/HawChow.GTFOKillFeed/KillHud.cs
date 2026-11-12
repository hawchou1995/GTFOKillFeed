using System;
using System.IO;
using BepInEx.Core.Logging.Interpolation;
using BepInEx.Logging;
using UnityEngine;

namespace HawChow.GTFOKillFeed;

public sealed class KillHud : MonoBehaviour
{
	internal static readonly Color Ink = new Color(0.91f, 0.925f, 0.92f);

	internal static readonly Color Accent = new Color(0.9f, 0.67f, 0.34f);

	internal static readonly Color HeavyTone = new Color(0.73f, 0.16f, 0.16f);

	private Texture2D _normalIcon;

	private Texture2D _weakIcon;

	private Texture2D _heavyIcon;

	private readonly IconTextureCache _icons = new IconTextureCache();

	private string _iconRoot;

	private string _normalPath;

	private string _weakPath;

	private string _heavyPath;

	private GUIStyle _titleStyle;

	private GUIStyle _detailStyle;

	private bool _visible = true;

	private bool _iconsLogged;

	private int _warnings;

	private int _layoutLogs;

	internal static KillHud Instance;

	public KillHud(System.IntPtr pointer)
		: base(pointer)
	{
	}

	private void Update()
	{
		try
		{
			float unscaledTime = Time.unscaledTime;
			EnemyNames.Tick(unscaledTime);
			Plugin.TickConfigHotReload(unscaledTime);
			DamageNumbers.Tick(unscaledTime);
			CorpseFeedback.Tick(unscaledTime);
			TeammateMarks.Tick(unscaledTime, _visible);
			TickCardIcons(unscaledTime);
			KillIconHud.Tick(unscaledTime);
			if (Plugin.Enabled.Value)
			{
				if (Plugin.ToggleKey.Value != 0 && Input.GetKeyDown(Plugin.ToggleKey.Value))
				{
					_visible = !_visible;
				}
				if (Plugin.PreviewKey.Value != 0 && Input.GetKeyDown(Plugin.PreviewKey.Value))
				{
					KillTracker.AddPreview(weakspot: false);
					KillTracker.AddPreview(weakspot: true);
					DamageNumbers.Preview();
				}
				EnemyBar.Tick(unscaledTime, _visible);
			}
		}
		catch (System.Exception ex)
		{
			Warn("update", ex.Message);
		}
	}

	private void OnGUI()
	{
		if (!_visible || Plugin.Enabled == null || !Plugin.Enabled.Value || Event.current.type != EventType.Repaint)
		{
			return;
		}
		Color color = GUI.color;
		try
		{
			EnsureStyles();
			VisibleIcons();
			DamageNumbers.Draw();
			EnemyBar.Draw();
			TeammateMarks.Draw();
			KillIconDisplay display = KillIconPolicy.ParseDisplay((Plugin.KillIconDisplay != null) ? Plugin.KillIconDisplay.Value : null);
			KillIconHud.Draw();
			DrawCards(display);
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

	private static string CardTitle(KillCard card)
	{
		bool flag = card.Kind == KillKind.Weakspot;
		bool flag2 = card.Tier == KillTier.Boss;
		if (!KillPolicy.IsChinese(Plugin.Language.Value))
		{
			if (!flag2)
			{
				if (!flag)
				{
					return "KILL CONFIRMED";
				}
				return "WEAKPOINT KILL";
			}
			return "HEAVY KILL";
		}
		if (!flag2)
		{
			if (!flag)
			{
				return "击杀确认";
			}
			return "弱点击杀";
		}
		return "重装击杀";
	}

	private float MeasureCardWidth(VisibleKillCard visible, float iconSize, float scale, float maxWidth)
	{
		KillCard card = visible.Card;
		string text = CardTitle(card);
		string text2 = FeedbackPolicy.CardDetail(card, Plugin.ShowDistance.Value, Plugin.ShowEnemyHealth.Value);
		_titleStyle.fontSize = Mathf.RoundToInt(13f * scale);
		_detailStyle.fontSize = Mathf.RoundToInt(12f * scale);
		float textWidth = Mathf.Max(_titleStyle.CalcSize(new GUIContent(text)).x, _detailStyle.CalcSize(new GUIContent(text2)).x);
		return FeedbackPolicy.CardWidth(iconSize, textWidth, scale, maxWidth);
	}

	private static float CardHeight(float iconSize, float scale)
	{
		float num = Mathf.Max(50f * scale, iconSize + 10f * scale);
		float num2 = KillIconPolicy.BadgeSize(iconSize);
		if (!(num2 > 1f))
		{
			return num;
		}
		return num + num2 + KillIconPolicy.BadgeGap(iconSize);
	}

	private void DrawCard(VisibleKillCard visible, Rect rect, float iconSize, float scale)
	{
		KillCard card = visible.Card;
		bool flag = card.Kind == KillKind.Weakspot;
		float alpha = visible.Alpha;
		float num = FeedbackPolicy.PanelAlpha((Plugin.BackgroundOpacity != null) ? Plugin.BackgroundOpacity.Value : 0f);
		GUI.color = new Color(0.055f, 0.064f, 0.068f, num * alpha);
		GUI.DrawTexture(rect, Texture2D.whiteTexture);
		GUI.color = new Color(0.28f, 0.31f, 0.32f, FeedbackPolicy.RuleAlpha(num) * alpha);
		GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, scale), Texture2D.whiteTexture);
		Color color = ((card.Tier == KillTier.Boss) ? HeavyTone : (flag ? Accent : Ink));
		Rect position = new Rect(rect.x + 8f * scale, rect.y + (rect.height - iconSize) * 0.5f, iconSize, iconSize);
		GUI.color = new Color(1f, 1f, 1f, alpha);
		Texture2D texture2D = TierIcon(card.Tier);
		if (texture2D != null)
		{
			GUI.DrawTexture(position, texture2D, ScaleMode.ScaleToFit, alphaBlend: true);
		}
		float badge = KillIconPolicy.BadgeSize(iconSize);
		KillIconHud.DrawFlightMark(card.Flight, position.center.x, position.yMax + KillIconPolicy.BadgeGap(iconSize), badge, alpha);
		float num2 = position.xMax + 10f * scale;
		float num3 = rect.xMax - num2 - 12f * scale;
		string text = CardTitle(card);
		_titleStyle.fontSize = Mathf.RoundToInt(13f * scale);
		Rect position2 = new Rect(num2, rect.center.y - 20f * scale, num3, 22f * scale);
		int num4 = Mathf.RoundToInt(8f * scale);
		while (_titleStyle.fontSize > num4 && _titleStyle.CalcSize(new GUIContent(text)).x > num3)
		{
			_titleStyle.fontSize--;
		}
		GUI.color = new Color(0f, 0f, 0f, 0.65f * alpha);
		GUI.Label(new Rect(position2.x + scale, position2.y + scale, position2.width, position2.height), text, _titleStyle);
		GUI.color = new Color(color.r, color.g, color.b, alpha);
		GUI.Label(position2, text, _titleStyle);
		string text2 = FeedbackPolicy.CardDetail(card, Plugin.ShowDistance.Value, Plugin.ShowEnemyHealth.Value);
		_detailStyle.fontSize = Mathf.RoundToInt(12f * scale);
		GUIContent content = new GUIContent(text2);
		int num5 = Mathf.RoundToInt(8f * scale);
		while (_detailStyle.fontSize > num5 && _detailStyle.CalcSize(content).x > num3)
		{
			_detailStyle.fontSize--;
		}
		Rect position3 = new Rect(num2, rect.center.y + 1f * scale, num3, 19f * scale);
		GUI.color = new Color(0f, 0f, 0f, 0.65f * alpha);
		GUI.Label(new Rect(position3.x + scale, position3.y + scale, position3.width, position3.height), text2, _detailStyle);
		GUI.color = new Color(Ink.r, Ink.g, Ink.b, 0.96f * alpha);
		GUI.Label(position3, text2, _detailStyle);
	}

	private void DrawCards(KillIconDisplay display)
	{
		VisibleKillCard[] array = KillTracker.Snapshot(Time.unscaledTime);
		float unscaledTime = Time.unscaledTime;
		int num = 0;
		for (int i = 0; i < array.Length; i++)
		{
			if (PaintsCard(array[i].Card, display, unscaledTime))
			{
				array[num++] = array[i];
			}
		}
		if (num == 0)
		{
			return;
		}
		float num2 = Mathf.Clamp((float)Screen.height / 1080f, 0.75f, 1.6f);
		float iconSize = FeedbackPolicy.Clamp(Plugin.IconSize.Value, 26f, 76f, 46f) * num2;
		float num3 = 0f;
		for (int j = 0; j < num; j++)
		{
			num3 = Mathf.Max(num3, CardHeight(iconSize, num2));
		}
		float maxWidth = Mathf.Max(0f, Mathf.Min((float)Screen.width - 32f, 430f * num2));
		float num4 = Mathf.Clamp((float)Screen.height * 0.78f + Plugin.VerticalOffset.Value, num3 * (float)num, (float)Screen.height - num3 - 12f);
		float maxDistance = Plugin.MaxDistance?.Value ?? 999f;
		for (int k = 0; k < num; k++)
		{
			VisibleKillCard visible = new VisibleKillCard(CardPresentation.Resolve(array[k].Card, EnemyNames.Resolve, maxDistance), array[k].Alpha);
			float num5 = Mathf.Max(0f, unscaledTime - visible.Card.CreatedAt);
			float num6 = Mathf.Pow(1f - Mathf.Clamp01(num5 / 0.16f), 3f);
			float y = num4 - (float)(num - 1 - k) * (num3 + 5f * num2) + num6 * 7f * num2;
			float num7 = MeasureCardWidth(visible, iconSize, num2, maxWidth);
			float num8 = FeedbackPolicy.CenteredCardX(Screen.width, num7, Plugin.HorizontalOffset.Value);
			if (_layoutLogs++ < 8)
			{
				Plugin instance = Plugin.Instance;
				if (instance != null)
				{
					ManualLogSource log = instance.Log;
					bool isEnabled;
					BepInExInfoLogInterpolatedStringHandler bepInExInfoLogInterpolatedStringHandler = new BepInExInfoLogInterpolatedStringHandler(65, 7, out isEnabled);
					if (isEnabled)
					{
						bepInExInfoLogInterpolatedStringHandler.AppendLiteral("[HUD] card layout enemy=");
						bepInExInfoLogInterpolatedStringHandler.AppendFormatted(visible.Card.EnemyName);
						bepInExInfoLogInterpolatedStringHandler.AppendLiteral(";x=");
						((BepInExLogInterpolatedStringHandler)bepInExInfoLogInterpolatedStringHandler).AppendFormatted<float>(num8, "0.0");
						bepInExInfoLogInterpolatedStringHandler.AppendLiteral(";width=");
						((BepInExLogInterpolatedStringHandler)bepInExInfoLogInterpolatedStringHandler).AppendFormatted<float>(num7, "0.0");
						bepInExInfoLogInterpolatedStringHandler.AppendLiteral(";center=");
						((BepInExLogInterpolatedStringHandler)bepInExInfoLogInterpolatedStringHandler).AppendFormatted<float>(num8 + num7 * 0.5f, "0.0");
						bepInExInfoLogInterpolatedStringHandler.AppendLiteral(";screenCenter=");
						((BepInExLogInterpolatedStringHandler)bepInExInfoLogInterpolatedStringHandler).AppendFormatted<float>((float)Screen.width * 0.5f, "0.0");
						bepInExInfoLogInterpolatedStringHandler.AppendLiteral(";screen=");
						bepInExInfoLogInterpolatedStringHandler.AppendFormatted(Screen.width);
						bepInExInfoLogInterpolatedStringHandler.AppendLiteral("x");
						bepInExInfoLogInterpolatedStringHandler.AppendFormatted(Screen.height);
					}
					log.LogInfo(bepInExInfoLogInterpolatedStringHandler);
				}
			}
			DrawCard(visible, new Rect(num8, y, num7, num3), iconSize, num2);
		}
	}

	internal static bool PaintsCard(KillCard card, KillIconDisplay display, float now)
	{
		if (card != null)
		{
			return KillIconPolicy.ShouldDrawCard(display, card.IconToken, card.IconExpiresAt, now, card.IconToken > 0 && KillIconHud.IsPresenting(card.IconToken) && KillIconHud.CanDrawNow(display));
		}
		return false;
	}

	private Texture2D TierIcon(KillTier tier)
	{
		return tier switch
		{
			KillTier.Boss => _heavyIcon, 
			KillTier.Elite => _weakIcon, 
			_ => _normalIcon, 
		};
	}

	private void EnsureStyles()
	{
		if (_titleStyle == null)
		{
			_titleStyle = new GUIStyle(GUI.skin.label)
			{
				fontSize = 15,
				fontStyle = FontStyle.Bold,
				alignment = TextAnchor.MiddleLeft,
				clipping = TextClipping.Clip,
				richText = false
			};
			_detailStyle = new GUIStyle(GUI.skin.label)
			{
				fontSize = 13,
				alignment = TextAnchor.MiddleLeft,
				clipping = TextClipping.Clip,
				richText = false
			};
		}
	}

	private void TickCardIcons(float now)
	{
		if (_iconRoot == null)
		{
			_iconRoot = IconTextureCache.IconRoot();
		}
		string key = KillIconPolicy.TierIconKey(KillTier.Minor);
		string key2 = KillIconPolicy.TierIconKey(KillTier.Elite);
		string key3 = KillIconPolicy.TierIconKey(KillTier.Boss);
		_normalPath = Path.Combine(_iconRoot, KillIconPolicy.TierIconFile(KillTier.Minor));
		_weakPath = Path.Combine(_iconRoot, KillIconPolicy.TierIconFile(KillTier.Elite));
		_heavyPath = Path.Combine(_iconRoot, KillIconPolicy.TierIconFile(KillTier.Boss));
		_icons.Tick(key, _normalPath, now);
		_icons.Tick(key2, _weakPath, now);
		_icons.Tick(key3, _heavyPath, now);
		if (_iconsLogged)
		{
			return;
		}
		_iconsLogged = true;
		Plugin instance = Plugin.Instance;
		if (instance != null)
		{
			ManualLogSource log = instance.Log;
			bool isEnabled;
			BepInExInfoLogInterpolatedStringHandler bepInExInfoLogInterpolatedStringHandler = new BepInExInfoLogInterpolatedStringHandler(110, 3, out isEnabled);
			if (isEnabled)
			{
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral("[HUD] icon slots registered normal=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(HotReloadPolicy.SafeFileName(_normalPath));
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral(";weakspot=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(HotReloadPolicy.SafeFileName(_weakPath));
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral(";heavy=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(HotReloadPolicy.SafeFileName(_heavyPath));
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral(";layout=two-line;health-tiers=icon-family;hot-reload=stamp");
			}
			log.LogInfo(bepInExInfoLogInterpolatedStringHandler);
		}
	}

	private void VisibleIcons()
	{
		_normalIcon = _icons.Texture(KillIconPolicy.TierIconKey(KillTier.Minor));
		_weakIcon = _icons.Texture(KillIconPolicy.TierIconKey(KillTier.Elite));
		_heavyIcon = _icons.Texture(KillIconPolicy.TierIconKey(KillTier.Boss));
	}

	private void Warn(string stage, string detail)
	{
		if (_warnings++ < 5)
		{
			Plugin.Instance?.Log.LogWarning("[HUD] " + stage + ": " + detail);
		}
	}
}
