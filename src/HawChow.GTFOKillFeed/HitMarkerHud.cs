using System;
using System.IO;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace HawChow.GTFOKillFeed;

public sealed class HitMarkerHud : MonoBehaviour
{
	private struct Marker
	{
		internal Vector3 World;

		internal HitMarkerKind Kind;

		internal float At;

		internal int Identity;
	}

	internal static HitMarkerHud Instance;

	private readonly ImpactScopeStack _impacts = new ImpactScopeStack();

	private readonly IconTextureCache _icons = new IconTextureCache();

	private readonly HexColorCache _normalColor = new HexColorCache();

	private readonly HexColorCache _weakColor = new HexColorCache();

	private readonly HexColorCache _armorColor = new HexColorCache();

	private readonly HexColorCache _killColor = new HexColorCache();

	private string _root;

	private string _normalPath;

	private string _weakPath;

	private string _armorPath;

	private string _killPath;

	private Texture2D _normal;

	private Texture2D _weak;

	private Texture2D _armor;

	private Texture2D _kill;

	private bool _iconsLogged;

	private int _warnings;

	private readonly Marker[] _markers = new Marker[24];

	private int _count;

	private int _fired;

	internal static int Fired
	{
		get
		{
			if (!(Instance != null))
			{
				return 0;
			}
			return Instance._fired;
		}
	}

	internal static int ActiveMarkers
	{
		get
		{
			if (!(Instance != null))
			{
				return 0;
			}
			return Instance._count;
		}
	}

	public HitMarkerHud(System.IntPtr pointer)
		: base(pointer)
	{
	}

	internal static bool Enabled()
	{
		if (Plugin.HitMarkerEnabled != null)
		{
			return Plugin.HitMarkerEnabled.Value;
		}
		return true;
	}

	internal static void Reset(string reason)
	{
		HitMarkerHud instance = Instance;
		if (!(instance == null))
		{
			instance._count = 0;
			instance._impacts.Reset();
			Plugin.Instance?.Log.LogInfo("[HITMARKER] cleared (" + reason + ")");
		}
	}

	internal static int BeginImpact(Vector3 world, HitKind kind, bool local)
	{
		HitMarkerHud instance = Instance;
		if (instance == null || !local)
		{
			return 0;
		}
		return instance._impacts.Push(world.x, world.y, world.z, kind, Time.frameCount);
	}

	internal static void EndImpact(int id)
	{
		HitMarkerHud instance = Instance;
		if (instance != null)
		{
			instance._impacts.Pop(id);
		}
	}

	internal static bool TryShow(Vector3 world, HitMarkerKind kind, int identity)
	{
		if (Instance != null)
		{
			return Instance.Submit(world, kind, identity);
		}
		return false;
	}

	internal static bool TryShowKill(Vector3 world)
	{
		HitMarkerHud instance = Instance;
		if (instance == null)
		{
			return false;
		}
		int id;
		int identity = (instance._impacts.TryIdentify(Time.frameCount, world.x, world.y, world.z, out id) ? id : 0);
		return instance.Submit(world, HitMarkerKind.Kill, identity);
	}

	private bool Submit(Vector3 world, HitMarkerKind kind, int identity)
	{
		if (!HitMarkerPolicy.AcceptsSubmission(Plugin.MarkerChannel(kind), kind))
		{
			return false;
		}
		if (!TryProject(world, out var _))
		{
			return false;
		}
		if (!IconReady(kind))
		{
			return false;
		}
		if (Resolve(world, kind, identity, Time.unscaledTime) == HitMarkerAction.Dropped)
		{
			return false;
		}
		_fired++;
		return true;
	}

	private HitMarkerAction Resolve(Vector3 world, HitMarkerKind kind, int identity, float now)
	{
		int num = -1;
		if (identity > 0)
		{
			for (int i = 0; i < _count; i++)
			{
				if (_markers[i].Identity == identity)
				{
					num = i;
					break;
				}
			}
		}
		HitMarkerKind existing = ((num >= 0) ? _markers[num].Kind : HitMarkerKind.None);
		int existingIdentity = ((num >= 0) ? _markers[num].Identity : 0);
		HitMarkerAction hitMarkerAction = HitMarkerPolicy.Resolve(existing, existingIdentity, kind, identity);
		switch (hitMarkerAction)
		{
		case HitMarkerAction.Added:
			if (_count >= _markers.Length)
			{
				EvictOldest();
			}
			_markers[_count++] = new Marker
			{
				World = world,
				Kind = kind,
				At = now,
				Identity = identity
			};
			return hitMarkerAction;
		case HitMarkerAction.Upgraded:
		case HitMarkerAction.Merged:
			if (num < 0)
			{
				return HitMarkerAction.Dropped;
			}
			_markers[num] = new Marker
			{
				World = world,
				Kind = kind,
				At = now,
				Identity = identity
			};
			return hitMarkerAction;
		default:
			return HitMarkerAction.Dropped;
		}
	}

	private void EvictOldest()
	{
		int num = 0;
		for (int i = 1; i < _count; i++)
		{
			if (_markers[i].At < _markers[num].At)
			{
				num = i;
			}
		}
		for (int j = num; j < _count - 1; j++)
		{
			_markers[j] = _markers[j + 1];
		}
		_count--;
	}

	private static bool TryProject(Vector3 world, out Vector3 screen)
	{
		screen = default(Vector3);
		if (!HitMarkerPolicy.IsFinite(world.x) || !HitMarkerPolicy.IsFinite(world.y) || !HitMarkerPolicy.IsFinite(world.z))
		{
			return false;
		}
		try
		{
			Camera currentCamera = CameraManager.GetCurrentCamera();
			if (currentCamera == null)
			{
				return false;
			}
			screen = currentCamera.WorldToScreenPoint(world);
		}
		catch (System.Exception)
		{
			return false;
		}
		float y = (float)Screen.height - screen.y;
		return HitMarkerPolicy.IsProjectable(screen.z, screen.x, y, Screen.width, Screen.height, 0f);
	}

	private bool IconReady(HitMarkerKind kind)
	{
		return kind switch
		{
			HitMarkerKind.Kill => _kill != null, 
			HitMarkerKind.Armor => _armor != null, 
			HitMarkerKind.Weakspot => _weak != null, 
			_ => _normal != null, 
		};
	}

	private Texture2D IconFor(HitMarkerKind kind)
	{
		return kind switch
		{
			HitMarkerKind.Kill => _kill, 
			HitMarkerKind.Armor => _armor, 
			HitMarkerKind.Weakspot => _weak, 
			_ => _normal, 
		};
	}

	private void Update()
	{
		TickIcons(Time.unscaledTime);
	}

	private void TickIcons(float now)
	{
		try
		{
			if (_root == null)
			{
				_root = IconTextureCache.IconRoot();
				_normalPath = Path.Combine(_root, HitMarkerPolicy.IconFile(HitMarkerKind.Normal));
				_weakPath = Path.Combine(_root, HitMarkerPolicy.IconFile(HitMarkerKind.Weakspot));
				_armorPath = Path.Combine(_root, HitMarkerPolicy.IconFile(HitMarkerKind.Armor));
				_killPath = Path.Combine(_root, HitMarkerPolicy.IconFile(HitMarkerKind.Kill));
			}
			_icons.Tick(HitMarkerPolicy.IconKey(HitMarkerKind.Normal), _normalPath, now);
			_icons.Tick(HitMarkerPolicy.IconKey(HitMarkerKind.Weakspot), _weakPath, now);
			_icons.Tick(HitMarkerPolicy.IconKey(HitMarkerKind.Armor), _armorPath, now);
			_icons.Tick(HitMarkerPolicy.IconKey(HitMarkerKind.Kill), _killPath, now);
			_normal = _icons.Texture(HitMarkerPolicy.IconKey(HitMarkerKind.Normal));
			_weak = _icons.Texture(HitMarkerPolicy.IconKey(HitMarkerKind.Weakspot));
			_armor = _icons.Texture(HitMarkerPolicy.IconKey(HitMarkerKind.Armor));
			_kill = _icons.Texture(HitMarkerPolicy.IconKey(HitMarkerKind.Kill));
			for (int num = _count - 1; num >= 0; num--)
			{
				if (now - _markers[num].At >= HitMarkerPolicy.Lifetime(_markers[num].Kind))
				{
					RemoveAt(num);
				}
			}
			if (!_iconsLogged)
			{
				_iconsLogged = true;
				Plugin.Instance?.Log.LogInfo(string.Concat(new string[9]
				{
					"[HITMARKER] icon slots registered normal=",
					HotReloadPolicy.SafeFileName(_normalPath),
					";weakspot=",
					HotReloadPolicy.SafeFileName(_weakPath),
					";armor=",
					HotReloadPolicy.SafeFileName(_armorPath),
					";kill=",
					HotReloadPolicy.SafeFileName(_killPath),
					";anchor=world-impact"
				}));
			}
		}
		catch (System.Exception ex)
		{
			if (_warnings++ < 5)
			{
				Plugin.Instance?.Log.LogWarning("[HITMARKER] icon tick failed: " + ((MemberInfo)ex.GetType()).Name);
			}
		}
	}

	private void RemoveAt(int index)
	{
		for (int i = index; i < _count - 1; i++)
		{
			_markers[i] = _markers[i + 1];
		}
		_count--;
	}

	private void OnGUI()
	{
		if (Event.current == null || Event.current.type != EventType.Repaint || !Enabled() || _count == 0)
		{
			return;
		}
		float unscaledTime = Time.unscaledTime;
		float num = Mathf.Clamp((float)Screen.height / 1080f, 0.75f, 1.6f);
		float num2 = HitMarkerPolicy.Size((Plugin.HitMarkerSize != null) ? Plugin.HitMarkerSize.Value : 64f);
		Color color = GUI.color;
		try
		{
			Camera currentCamera = CameraManager.GetCurrentCamera();
			if (currentCamera == null)
			{
				return;
			}
			for (int i = 0; i < _count; i++)
			{
				Marker marker = _markers[i];
				float num3 = HitMarkerPolicy.Alpha(unscaledTime - marker.At, HitMarkerPolicy.Lifetime(marker.Kind));
				if (!HitMarkerPolicy.ShouldDraw(enabled: true, marker.Kind, num3))
				{
					continue;
				}
				Vector3 vector;
				try
				{
					vector = currentCamera.WorldToScreenPoint(marker.World);
				}
				catch (System.Exception)
				{
					continue;
				}
				float num4 = (float)Screen.height - vector.y;
				if (HitMarkerPolicy.IsProjectable(vector.z, vector.x, num4, Screen.width, Screen.height, 0f))
				{
					Texture2D texture2D = IconFor(marker.Kind);
					if (!(texture2D == null))
					{
						float num5 = num2 * HitMarkerPolicy.SizeScale(marker.Kind) * num;
						Rect position = new Rect(vector.x - num5 * 0.5f, num4 - num5 * 0.5f, num5, num5);
						Color color2 = TintFor(marker.Kind);
						GUI.color = new Color(color2.r, color2.g, color2.b, num3);
						GUI.DrawTexture(position, texture2D, ScaleMode.ScaleToFit, alphaBlend: true);
					}
				}
			}
		}
		finally
		{
			GUI.color = color;
		}
	}

	private Color TintFor(HitMarkerKind kind)
	{
		switch (kind)
		{
		case HitMarkerKind.Weakspot:
			_weakColor.Resolve((Plugin.HitMarkerWeakspotColor != null) ? Plugin.HitMarkerWeakspotColor.Value : null, 1f, 0.549f, 0f, 1f);
			return new Color(_weakColor.Red, _weakColor.Green, _weakColor.Blue, 1f);
		case HitMarkerKind.Armor:
			_armorColor.Resolve((Plugin.HitMarkerArmorColor != null) ? Plugin.HitMarkerArmorColor.Value : null, 0.286f, 0.678f, 0.788f, 1f);
			return new Color(_armorColor.Red, _armorColor.Green, _armorColor.Blue, 1f);
		case HitMarkerKind.Kill:
			_killColor.Resolve((Plugin.HitMarkerKillColor != null) ? Plugin.HitMarkerKillColor.Value : null, 0.725f, 0.157f, 0.157f, 1f);
			return new Color(_killColor.Red, _killColor.Green, _killColor.Blue, 1f);
		default:
			_normalColor.Resolve((Plugin.HitMarkerNormalColor != null) ? Plugin.HitMarkerNormalColor.Value : null, 1f, 1f, 1f, 1f);
			return new Color(_normalColor.Red, _normalColor.Green, _normalColor.Blue, 1f);
		}
	}

	internal static void ApplyPatches(Harmony harmony, ManualLogSource log)
	{
		try
		{
			System.Type type = AccessTools.TypeByName("CrosshairGuiLayer");
			if (type == (System.Type)null)
			{
				log?.LogWarning("[HITMARKER] CrosshairGuiLayer not found; the vanilla indicators stay visible");
				return;
			}
			int num = 0;
			MethodInfo val = AccessTools.Method(type, "ShowHitIndicator", new System.Type[4]
			{
				typeof(Vector3),
				typeof(bool),
				typeof(bool),
				typeof(bool)
			});
			MethodInfo val2 = AccessTools.Method(type, "ShowDeathIndicator", new System.Type[1] { typeof(Vector3) });
			MethodInfo method = typeof(HitMarkerHud).GetMethod("ShowHitIndicatorPrefix", (BindingFlags)40);
			MethodInfo method2 = typeof(HitMarkerHud).GetMethod("ShowDeathIndicatorPrefix", (BindingFlags)40);
			if (val != (MethodInfo)null && method != (MethodInfo)null)
			{
				harmony.Patch((MethodBase)(object)val, new HarmonyMethod(method));
				num++;
			}
			else
			{
				log?.LogWarning("[HITMARKER] CrosshairGuiLayer.ShowHitIndicator missing; hit lines keep the vanilla indicator");
			}
			if (val2 != (MethodInfo)null && method2 != (MethodInfo)null)
			{
				harmony.Patch((MethodBase)(object)val2, new HarmonyMethod(method2));
				num++;
			}
			else
			{
				log?.LogWarning("[HITMARKER] CrosshairGuiLayer.ShowDeathIndicator missing; the vanilla kill line stays");
			}
			log?.LogInfo((num == 2) ? "[HITMARKER] vanilla hit/kill indicators replaced on 2 world-position entry points (anchor=impact)" : ("[HITMARKER] only " + num + " of 2 world-position entry points could be replaced; the rest stays vanilla"));
		}
		catch (System.Exception ex)
		{
			log?.LogWarning("[HITMARKER] vanilla indicator replacement unavailable (" + ((MemberInfo)ex.GetType()).Name + "); the stock indicators stay visible");
		}
	}

	private static bool ShowHitIndicatorPrefix(Vector3 worldPos, bool isWeakspot, bool isFriendly, bool noDamage)
	{
		try
		{
			HitMarkerHud instance = Instance;
			if (instance == null)
			{
				return true;
			}
			HitKind kind;
			int id;
			bool scopeMatched = instance._impacts.TryClaim(Time.frameCount, worldPos.x, worldPos.y, worldPos.z, noDamage, out kind, out id);
			int id2;
			int identity = (instance._impacts.TryIdentify(Time.frameCount, worldPos.x, worldPos.y, worldPos.z, out id2) ? id2 : 0);
			if (!HitMarkerPolicy.TryDecide(Enabled(), isFriendly, noDamage, scopeMatched, kind, isWeakspot, out var kind2))
			{
				return true;
			}
			return !TryShow(worldPos, kind2, identity);
		}
		catch (System.Exception ex)
		{
			Report("hit-indicator", ((MemberInfo)ex.GetType()).Name);
			return true;
		}
	}

	private static bool ShowDeathIndicatorPrefix(Vector3 worldPos)
	{
		try
		{
			return !TryShowKill(worldPos);
		}
		catch (System.Exception ex)
		{
			Report("kill-indicator", ((MemberInfo)ex.GetType()).Name);
			return true;
		}
	}

	private static void Report(string stage, string detail)
	{
		HitMarkerHud instance = Instance;
		if (!(instance == null) && instance._warnings++ < 5)
		{
			Plugin.Instance?.Log.LogWarning(string.Concat(new string[5] { "[HITMARKER] ", stage, " failed (", detail, "); that event keeps the vanilla indicator" }));
		}
	}
}
