using System;
using System.IO;
using System.Reflection;
using Enemies;
using UnityEngine;

namespace HawChow.GTFOKillFeed;

internal static class KillIconHud
{
	private static readonly KillIconQueue Queue = new KillIconQueue(12);

	private static readonly ScreenBox[] Occluders = new ScreenBox[KillIconOccluders.MaxCandidates];

	private static bool _occludersComplete;

	private static int _occluderCount;

	private static readonly int[] HiddenTokens = new int[32];

	private static int _hiddenCount;

	private static readonly IconTextureCache Icons = new IconTextureCache();

	private static readonly HexColorCache WingColor = new HexColorCache();

	private static readonly HexColorCache PlateColor = new HexColorCache();

	private static string _root;

	private static string _lightPath;

	private static string _mediumPath;

	private static string _heavyPath;

	private static Texture2D _light;

	private static Texture2D _medium;

	private static Texture2D _heavy;

	private static bool _iconsLogged;

	private static bool _frozenLogged;

	private static bool _fallbackLogged;

	private static int _warnings;

	private static int _shown;

	internal static int ActiveCount => Queue.Count;

	internal static int Shown => _shown;

	internal static int RealCount => Queue.RealCount;

	internal static int PreviewCount => Queue.PreviewCount;

	internal static void Reset(string reason)
	{
		Queue.Clear();
		Plugin.Instance?.Log.LogInfo("[KILLICON] cleared (" + reason + ")");
	}

	internal static int Add(EnemyAgent agent, Vector3 anchor, KillPresentation presentation, float clearance, float now, string anchorSource)
	{
		if (!KillPolicy.IsFinite(anchor.x) || !KillPolicy.IsFinite(anchor.y) || !KillPolicy.IsFinite(anchor.z))
		{
			return 0;
		}
		System.IntPtr pointer = System.IntPtr.Zero;
		try
		{
			if (agent != null)
			{
				pointer = agent.Pointer;
			}
		}
		catch (System.Exception)
		{
			pointer = System.IntPtr.Zero;
		}
		int num = Push(pointer, anchor, clearance, presentation, now, preview: false);
		if (num > 0)
		{
			LogFrozenAnchor(anchorSource, presentation, clearance);
		}
		return num;
	}

	private static void LogFrozenAnchor(string source, KillPresentation presentation, float clearance)
	{
		string text = (string.IsNullOrWhiteSpace(source) ? "head-world" : source);
		if (!_frozenLogged)
		{
			_frozenLogged = true;
			Plugin.Instance?.Log.LogInfo(string.Concat(new string[11]
			{
				"[KILLICON] anchor=frozen;when=credit;source=",
				text,
				";tier=",
				((object)presentation.Tier).ToString(),
				";flight=",
				((object)presentation.Flight).ToString(),
				";kind=",
				((object)presentation.Kind).ToString(),
				";clearance=",
				clearance.ToString("0.00"),
				"m;follow=none;projection=per-frame;budget=once"
			}));
		}
		else if (!(text == "head-world") && !_fallbackLogged)
		{
			_fallbackLogged = true;
			Plugin.Instance?.Log.LogInfo("[KILLICON] anchor=fallback;source=" + text + ";reason=head-unavailable-at-credit;note=lethal-event-point,never-screen-centre,budget=once");
		}
	}

	internal static void AddPreview(float now)
	{
		Camera currentCamera = CameraManager.GetCurrentCamera();
		if (currentCamera == null)
		{
			return;
		}
		try
		{
			Transform transform = currentCamera.transform;
			Vector3 forward = transform.forward;
			Vector3 right = transform.right;
			Vector3 up = transform.up;
			Vector3 vector = transform.position + forward * 6f;
			Queue.ResetPreviews();
			for (int i = 0; i < 9; i++)
			{
				KillIconPolicy.PreviewCombo(i, out var tier, out var flight);
				Vector3 anchor = vector + right * ((float)(tier - 1) * 1.15f) + up * (0.35f - (float)flight * 0.35f);
				Push(System.IntPtr.Zero, anchor, 0f, new KillPresentation(KillKind.Normal, tier, flight), now, preview: true);
			}
		}
		catch (System.Exception ex)
		{
			Warn("preview", ((MemberInfo)ex.GetType()).Name);
		}
	}

	private static int Push(System.IntPtr pointer, Vector3 anchor, float clearance, KillPresentation presentation, float now, bool preview)
	{
		KillIconSlot killIconSlot = default(KillIconSlot);
		killIconSlot.Pointer = pointer;
		killIconSlot.X = anchor.x;
		killIconSlot.Y = anchor.y;
		killIconSlot.Z = anchor.z;
		killIconSlot.Clearance = clearance;
		killIconSlot.Tier = presentation.Tier;
		killIconSlot.Flight = presentation.Flight;
		killIconSlot.At = now;
		killIconSlot.Preview = preview;
		killIconSlot.Token = Queue.NextToken();
		KillIconSlot slot = killIconSlot;
		if (preview)
		{
			if (!Queue.PushPreview(in slot))
			{
				return 0;
			}
			return slot.Token;
		}
		Queue.PushReal(in slot, KillIconPolicy.MaxIcons((Plugin.KillIconMax != null) ? Plugin.KillIconMax.Value : 6));
		return slot.Token;
	}

	internal static bool IsPresenting(int token)
	{
		if (IsHidden(token))
		{
			return false;
		}
		if (!Queue.TryFind(token, out var index, out var preview))
		{
			return false;
		}
		KillIconSlot killIconSlot = (preview ? Queue.PreviewAt(index) : Queue.RealAt(index));
		if (TextureFor(killIconSlot.Tier) == null)
		{
			return false;
		}
		float lifetime = KillIconPolicy.Lifetime((Plugin.KillIconLifetime != null) ? Plugin.KillIconLifetime.Value : 2.2f);
		return KillIconPolicy.Alpha(Time.unscaledTime - killIconSlot.At, lifetime) > 0f;
	}

	internal static void Tick(float now)
	{
		TickIcons(now);
		float lifetime = ((Plugin.KillIconLifetime != null) ? Plugin.KillIconLifetime.Value : 2.2f);
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
				_lightPath = Path.Combine(_root, KillIconPolicy.TierIconFile(KillTier.Minor));
				_mediumPath = Path.Combine(_root, KillIconPolicy.TierIconFile(KillTier.Elite));
				_heavyPath = Path.Combine(_root, KillIconPolicy.TierIconFile(KillTier.Boss));
			}
			Icons.Tick(KillIconPolicy.TierIconKey(KillTier.Minor), _lightPath, now);
			Icons.Tick(KillIconPolicy.TierIconKey(KillTier.Elite), _mediumPath, now);
			Icons.Tick(KillIconPolicy.TierIconKey(KillTier.Boss), _heavyPath, now);
			_light = Icons.Texture(KillIconPolicy.TierIconKey(KillTier.Minor));
			_medium = Icons.Texture(KillIconPolicy.TierIconKey(KillTier.Elite));
			_heavy = Icons.Texture(KillIconPolicy.TierIconKey(KillTier.Boss));
			if (!_iconsLogged)
			{
				_iconsLogged = true;
				Plugin.Instance?.Log.LogInfo(string.Concat(new string[7]
				{
					"[KILLICON] icon slots registered light=",
					HotReloadPolicy.SafeFileName(_lightPath),
					";medium=",
					HotReloadPolicy.SafeFileName(_mediumPath),
					";heavy=",
					HotReloadPolicy.SafeFileName(_heavyPath),
					";anchor=frozen-at-credit"
				}));
			}
		}
		catch (System.Exception ex)
		{
			Warn("icon tick", ((MemberInfo)ex.GetType()).Name);
		}
	}

	private static Texture2D TextureFor(KillTier tier)
	{
		return tier switch
		{
			KillTier.Boss => _heavy, 
			KillTier.Elite => _medium, 
			_ => _light, 
		};
	}

	internal static void Draw()
	{
		ResetHidden();
		if (Queue.Count == 0)
		{
			return;
		}
		Camera currentCamera = CameraManager.GetCurrentCamera();
		bool headEnabled = Plugin.KillIconHeadEnabled != null && Plugin.KillIconHeadEnabled.Value;
		if (!KillViewPolicy.HeadVisible(headEnabled, gameVisible: true, cameraReady: currentCamera != null))
		{
			return;
		}
		_occluderCount = KillIconOccluders.Collect(currentCamera, Occluders, out _occludersComplete);
		float unscaledTime = Time.unscaledTime;
		float lifetime = KillIconPolicy.Lifetime((Plugin.KillIconLifetime != null) ? Plugin.KillIconLifetime.Value : 2.2f);
		float num = Mathf.Clamp((float)Screen.height / 1080f, 0.75f, 1.6f);
		float num2 = KillIconPolicy.Size((Plugin.IconSize != null) ? Plugin.IconSize.Value : 46f) * num;
		float badge = KillIconPolicy.BadgeSize(num2);
		float badgeGap = KillIconPolicy.BadgeGap(num2);
		Color color = GUI.color;
		try
		{
			for (int i = 0; i < Queue.RealCount; i++)
			{
				KillIconSlot slot = Queue.RealAt(i);
				DrawSlot(in slot, currentCamera, unscaledTime, lifetime, num2, badge, badgeGap);
			}
			for (int j = 0; j < Queue.PreviewCount; j++)
			{
				KillIconSlot slot = Queue.PreviewAt(j);
				DrawSlot(in slot, currentCamera, unscaledTime, lifetime, num2, badge, badgeGap);
			}
		}
		finally
		{
			GUI.color = color;
		}
	}

	private static void DrawSlot(in KillIconSlot slot, Camera camera, float now, float lifetime, float size, float badge, float badgeGap)
	{
		float num = KillViewPolicy.CompositeAlpha(KillIconPolicy.Alpha(now - slot.At, lifetime), (Plugin.KillIconHeadOpacity != null) ? Plugin.KillIconHeadOpacity.Value : KillViewPolicy.DefaultOpacity);
		if (num <= 0f)
		{
			return;
		}
		Texture2D texture2D = TextureFor(slot.Tier);
		if (!(texture2D == null))
		{
			Vector3 position = new Vector3(slot.X, slot.Y, slot.Z);
			Vector3 vector;
			try
			{
				vector = camera.WorldToScreenPoint(position);
			}
			catch (System.Exception)
			{
				return;
			}
			float num2 = (float)Screen.height - vector.y;
			if (HitMarkerPolicy.IsProjectable(vector.z, vector.x, num2, Screen.width, Screen.height, 0f))
			{
				if (!_occludersComplete)
				{
					Hide(slot.Token);
					return;
				}
				float badgeExtent = 0f;
				if (KillIconPolicy.MarkFor(slot.Flight) != FlightMark.None && badge > 1f)
				{
					badgeExtent = badgeGap + badge;
				}
				ScreenBox iconBox = new ScreenBox(vector.x - size * 0.5f, num2 - size * 0.5f, vector.x + size * 0.5f, num2 + size * 0.5f + badgeExtent);
				IconPlacement placement = KillViewPolicy.ResolvePlacement(iconBox, Occluders, _occluderCount, 0f, Screen.height);
				if (placement.Hidden)
				{
					Hide(slot.Token);
					return;
				}
				Rect drawRect = new Rect(iconBox.Left, placement.Top, size, size);
				GUI.color = new Color(1f, 1f, 1f, num);
				GUI.DrawTexture(drawRect, texture2D, ScaleMode.ScaleToFit, alphaBlend: true);
				DrawFlightMark(slot.Flight, iconBox.CentreX, drawRect.yMax + badgeGap, badge, num);
				_shown++;
			}
		}
	}

	internal static void DrawFlightMark(KillFlight flight, float centreX, float top, float badge, float alpha)
	{
		FlightMark flightMark = KillIconPolicy.MarkFor(flight);
		if (flightMark == FlightMark.None || badge <= 1f)
		{
			return;
		}
		Color color = ColorFor(flight);
		GUI.color = new Color(color.r, color.g, color.b, alpha);
		float num = Mathf.Max(1f, badge * 0.16f);
		if (flightMark == FlightMark.ArmorPlate)
		{
			float num2 = badge * 0.72f;
			GUI.DrawTexture(new Rect(centreX - badge * 0.5f, top, badge, num), Texture2D.whiteTexture);
			GUI.DrawTexture(new Rect(centreX - badge * 0.5f, top + num2 - num, badge, num), Texture2D.whiteTexture);
			GUI.DrawTexture(new Rect(centreX - badge * 0.5f, top, num, num2), Texture2D.whiteTexture);
			GUI.DrawTexture(new Rect(centreX + badge * 0.5f - num, top, num, num2), Texture2D.whiteTexture);
			return;
		}
		float num3 = badge * 0.2f;
		float num4 = badge * 0.16f;
		for (int i = 0; i < 3; i++)
		{
			float num5 = badge * 0.5f - num3 * (float)(i + 1);
			float num6 = num4 * (float)i;
			GUI.DrawTexture(new Rect(centreX - num5 - num3, top + num6, num3, num4), Texture2D.whiteTexture);
			GUI.DrawTexture(new Rect(centreX + num5, top + num6, num3, num4), Texture2D.whiteTexture);
		}
	}

	private static Color ColorFor(KillFlight flight)
	{
		if (flight == KillFlight.ArmoredFlying)
		{
			PlateColor.Resolve((Plugin.KillIconArmorFlyerColor != null) ? Plugin.KillIconArmorFlyerColor.Value : null, 0.902f, 0.671f, 0.341f, 1f);
			return new Color(PlateColor.Red, PlateColor.Green, PlateColor.Blue, 1f);
		}
		WingColor.Resolve((Plugin.KillIconFlyerColor != null) ? Plugin.KillIconFlyerColor.Value : null, 0.561f, 0.827f, 0.91f, 1f);
		return new Color(WingColor.Red, WingColor.Green, WingColor.Blue, 1f);
	}

	private static void ResetHidden()
	{
		_hiddenCount = 0;
	}

	private static void Hide(int token)
	{
		if (_hiddenCount < HiddenTokens.Length)
		{
			HiddenTokens[_hiddenCount++] = token;
		}
	}

	private static bool IsHidden(int token)
	{
		for (int i = 0; i < _hiddenCount; i++)
		{
			if (HiddenTokens[i] == token)
			{
				return true;
			}
		}
		return false;
	}

	private static void Warn(string stage, string detail)
	{
		if (_warnings++ < 5)
		{
			Plugin.Instance?.Log.LogWarning("[KILLICON] " + stage + ": " + detail);
		}
	}
}
