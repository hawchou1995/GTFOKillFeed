using System;
using System.Collections.Generic;
using UnityEngine;

namespace HawChow.GTFOKillFeed;

internal static class DamageNumbers
{
	private sealed class Number
	{
		internal Vector3 World;

		internal float At;

		internal float Side;

		internal HitKind Kind;

		internal float Damage;

		internal string Text;

		internal bool Preview;

		internal int FlySeed;
	}

	private static readonly List<Number> Active = new List<Number>(64);

	private static int _sequence;

	private static GUIStyle _style;

	private static readonly HexColorCache WeakspotColor = new HexColorCache();

	private static Color ResolveWeakspotColor()
	{
		string value = ((Plugin.DamageNumberWeakspotColor != null) ? Plugin.DamageNumberWeakspotColor.Value : null);
		WeakspotColor.Resolve(value, KillHud.Accent.r, KillHud.Accent.g, KillHud.Accent.b, KillHud.Accent.a);
		return new Color(WeakspotColor.Red, WeakspotColor.Green, WeakspotColor.Blue, WeakspotColor.Alpha);
	}

	internal static void Add(Vector3 world, float damage, HitKind kind, bool preview = false, uint gearCategoryId = 0u)
	{
		bool flag = !preview && (ShotgunDamageNumberCapture.IsShotgunCategory(gearCategoryId) || ShotgunDamageNumberCapture.IsActiveFireCategory(gearCategoryId));
		bool masterEnabled = Plugin.ShowDamageNumbers != null && Plugin.ShowDamageNumbers.Value && Plugin.Enabled != null && Plugin.Enabled.Value;
		bool showShotgunNumbers = Plugin.ShowShotgunDamageNumbers?.Value ?? true;
		if (FeedbackPolicy.ShouldDisplayDamageNumber(masterEnabled, showShotgunNumbers, flag) && (!flag || !ShotgunDamageNumberCapture.TryCapture(world, damage, kind, preview, gearCategoryId)))
		{
			AddImmediate(world, damage, kind, preview);
		}
	}

	internal static void AddMergedShotgun(Vector3 world, float damage, HitKind kind)
	{
		AddImmediate(world, damage, kind, preview: false);
	}

	private static void AddImmediate(Vector3 world, float damage, HitKind kind, bool preview)
	{
		if (Plugin.ShowDamageNumbers == null || !Plugin.ShowDamageNumbers.Value || !Plugin.Enabled.Value)
		{
			return;
		}
		string text = FeedbackPolicy.DamageText(damage, kind);
		if (text.Length != 0 && KillPolicy.IsFinite(world.x) && KillPolicy.IsFinite(world.y) && KillPolicy.IsFinite(world.z))
		{
			while (Active.Count >= 64)
			{
				Active.RemoveAt(0);
			}
			int num = _sequence++;
			Active.Add(new Number
			{
				World = world,
				At = Time.unscaledTime,
				Side = FeedbackPolicy.NumberSide(num),
				Kind = kind,
				Text = text,
				Preview = preview,
				Damage = damage,
				FlySeed = num
			});
		}
	}

	internal static void Tick(float now)
	{
		float num = ((Plugin.DamageNumberLifetime != null) ? FeedbackPolicy.Clamp(Plugin.DamageNumberLifetime.Value, 0.35f, 1.2f, 0.72f) : 0.72f);
		for (int num2 = Active.Count - 1; num2 >= 0; num2--)
		{
			if (now - Active[num2].At >= num)
			{
				Active.RemoveAt(num2);
			}
		}
	}

	internal static void Draw()
	{
		//IL_00f9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fe: Unknown result type (might be due to invalid IL or missing references)
		if (!Plugin.ShowDamageNumbers.Value || Active.Count == 0)
		{
			return;
		}
		float num = FeedbackPolicy.NumberOpacity((Plugin.DamageNumberOpacity != null) ? Plugin.DamageNumberOpacity.Value : 1f);
		if (num <= 0f)
		{
			return;
		}
		Camera currentCamera = CameraManager.GetCurrentCamera();
		if (_style == null)
		{
			_style = new GUIStyle(GUI.skin.label)
			{
				alignment = TextAnchor.MiddleCenter,
				fontStyle = FontStyle.Bold,
				clipping = TextClipping.Overflow,
				richText = false
			};
		}
		float configured = FeedbackPolicy.NumberWeight((Plugin.DamageNumberFontWeight != null) ? Plugin.DamageNumberFontWeight.Value : 1f);
		_style.fontStyle = (FeedbackPolicy.NumberUsesBold(configured) ? FontStyle.Bold : FontStyle.Normal);
		float num2 = Mathf.Clamp((float)Screen.height / 1080f, 0.75f, 1.6f);
		float num3 = FeedbackPolicy.Clamp(Plugin.DamageNumberLifetime.Value, 0.35f, 1.2f, 0.72f);
		Color color = GUI.color;
		try
		{
			var enumerator = Active.GetEnumerator();
			try
			{
				while (enumerator.MoveNext())
				{
					Number current = enumerator.Current;
					float num4 = Time.unscaledTime - current.At;
					if (num4 < 0f || num4 >= num3)
					{
						continue;
					}
					Vector3 vector;
					if (current.Preview)
					{
						vector = new Vector3((float)Screen.width * 0.5f + current.World.x * num2, (float)Screen.height * 0.5f, 1f);
					}
					else
					{
						if (currentCamera == null)
						{
							continue;
						}
						vector = currentCamera.WorldToScreenPoint(current.World);
						if (vector.z <= 0.05f)
						{
							continue;
						}
						vector.y = (float)Screen.height - vector.y;
					}
					if (vector.x < 0f || vector.x > (float)Screen.width || vector.y < 0f || vector.y > (float)Screen.height)
					{
						continue;
					}
					bool prismatic = DamageFx.IsPrismatic(current.Damage);
					bool approach = Plugin.DamageNumberApproachEffect != null && Plugin.DamageNumberApproachEffect.Value;
					FeedbackPolicy.NumberPose(prismatic, approach, current.FlySeed, current.Side, num4, num3, num, vector.x, vector.y, Screen.width, Screen.height, num2, current.Kind, Plugin.DamageNumberSize.Value, out var x, out var y, out var size, out var alpha, out var _, out var _);
					_style.fontSize = Mathf.RoundToInt(size);
					Rect position = new Rect(x - 100f * num2, y - size, 200f * num2, size * 2f);
					DamageTier tier = DamageFx.Tier();
					DamageFxPolicy.NumberPaintPlan numberPaintPlan = DamageFxPolicy.Paint(prismatic, DamageFx.TiersEnabled(), tier);
					Color color2 = new Color(0.035f, 0.04f, 0.045f, alpha);
					if (numberPaintPlan.TierInk)
					{
						Color color3 = DamageFx.TierColor(tier);
						color2 = new Color(color3.r, color3.g, color3.b, alpha * DamageFxPolicy.OutlineStrength(tier));
					}
					float num5 = FeedbackPolicy.NumberOutlineRadius(configured) * num2;
					float phase = (numberPaintPlan.Foil ? DamageFxPolicy.WrapPhase(DamageFx.Phase() + current.Side * 0.01f) : 0f);
					int rings = numberPaintPlan.Rings;
					for (int i = 0; i < rings; i++)
					{
						float num6 = ((i == 0) ? num5 : (3.1f * num2));
						float num7 = ((i == 0) ? 0.95f : 0.42f);
						for (int j = 0; j < 8; j++)
						{
							DamageFxPolicy.FoilDirection(j, out var dx, out var dy);
							if (numberPaintPlan.Foil && (i != 0 || numberPaintPlan.FoilRim))
							{
								DamageFxPolicy.FoilRimColor(j, phase, out var red, out var green, out var blue);
								GUI.color = new Color(red, green, blue, alpha * num7);
							}
							else
							{
								GUI.color = color2;
							}
							GUI.Label(new Rect(position.x + (float)dx * num6, position.y + (float)dy * num6, position.width, position.height), current.Text, _style);
						}
					}
					if (numberPaintPlan.Foil)
					{
						DamageFxPolicy.FoilEmbossOffset(phase, out var dx2, out var dy2);
						GUI.color = new Color(1f, 1f, 1f, alpha * 0.45f);
						GUI.Label(new Rect(position.x + dx2 * num2, position.y + dy2 * num2, position.width, position.height), current.Text, _style);
					}
					Color color4;
					if (!numberPaintPlan.Foil)
					{
						color4 = (numberPaintPlan.TierInk ? DamageFx.TierColor(tier) : ((current.Kind == HitKind.Weakspot) ? ResolveWeakspotColor() : ((current.Kind != HitKind.Armor) ? KillHud.Ink : new Color(0.65f, 0.72f, 0.77f))));
					}
					else
					{
						DamageFxPolicy.FoilBodyColor(phase, out var red2, out var green2, out var blue2);
						color4 = new Color(red2, green2, blue2);
					}
					GUI.color = new Color(color4.r, color4.g, color4.b, FeedbackPolicy.NumberTextAlpha(color4.a, alpha));
					GUI.Label(position, current.Text, _style);
					int num8 = FeedbackPolicy.NumberThickenPasses(configured);
					if (num8 > 0)
					{
						float num9 = FeedbackPolicy.NumberThickenRadius(configured) * num2;
						for (int k = 0; k < num8; k++)
						{
							float f = (float)k / (float)num8 * (float)Math.PI * 2f;
							GUI.Label(new Rect(position.x + Mathf.Cos(f) * num9, position.y + Mathf.Sin(f) * num9, position.width, position.height), current.Text, _style);
						}
					}
				}
			}
			finally
			{
				((System.IDisposable)enumerator).Dispose();
			}
		}
		finally
		{
			GUI.color = color;
		}
	}

	internal static void Preview()
	{
		Add(new Vector3(-90f, 0f, 0f), 6.75f, HitKind.Armor, preview: true);
		Add(Vector3.zero, 24.35f, HitKind.Normal, preview: true);
		Add(new Vector3(90f, 0f, 0f), 81.2f, HitKind.Weakspot, preview: true);
	}

	internal static void Clear()
	{
		Active.Clear();
	}
}
