using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Configuration;
using Il2CppSystem.Collections.Generic;
using Player;
using SNetwork;
using UnityEngine;

namespace HawChow.GTFOKillFeed;

internal static class TeammateMarks
{
	private sealed class Runtime
	{
		internal PlayerAgent Agent;

		internal Transform Anchor;

		internal string AnchorKind = "none";

		internal float AnchorAt;
	}

	private const float AnchorRefreshSeconds = 0.5f;

	private const float ScreenMargin = 8f;

	private const float EyeLift = 0.18f;

	private static readonly TeammateMarkLedger Ledger = new TeammateMarkLedger();

	private static readonly System.Collections.Generic.Dictionary<long, Runtime> Runtimes = new System.Collections.Generic.Dictionary<long, Runtime>();

	private static readonly System.Collections.Generic.List<long> Sweep = new System.Collections.Generic.List<long>(4);

	private static readonly IconTextureCache Textures = new IconTextureCache();

	private static readonly DiagnosticBudget LogBudget = new DiagnosticBudget();

	private static readonly DiagnosticBudget WarnBudget = new DiagnosticBudget();

	private static GUIStyle _glyphStyle;

	private static readonly GUIContent GlyphContent = new GUIContent();

	private static PlayerAgent _local;

	private static bool _inLevel;

	private static float _emptySince = -1f;

	private static string _lastGate;

	private static bool _empLogHooked;

	internal static int Count => Ledger.Count;

	internal static void EnterLevel()
	{
		_inLevel = true;
		_emptySince = -1f;
		Clear("level-enter");
		Log("level=enter");
	}

	internal static void ObserveState(eGameStateName state)
	{
		if (state == eGameStateName.InLevel)
		{
			EnterLevel();
		}
		else
		{
			Reset("state:" + ((object)state).ToString());
		}
	}

	internal static void Reset(string reason)
	{
		_inLevel = false;
		_emptySince = -1f;
		EmpBridge.Reset(0f);
		Clear(reason);
	}

	private static void Clear(string reason)
	{
		bool num = Ledger.Count > 0 || Runtimes.Count > 0;
		Ledger.Clear();
		Runtimes.Clear();
		Sweep.Clear();
		_local = null;
		if (num)
		{
			Log("cleared;reason=" + reason);
		}
	}

	internal static void Tick(float now, bool hudVisible)
	{
		try
		{
			bool flag = Plugin.TeammateMarkEnabled != null && Plugin.TeammateMarkEnabled.Value;
			bool flag2 = Plugin.TeammateMarkHideDuringEmp != null && Plugin.TeammateMarkHideDuringEmp.Value;
			if (!_empLogHooked)
			{
				_empLogHooked = true;
				EmpBridge.Diagnostics = delegate(string message)
				{
					Log(message);
				};
			}
			EmpBridge.Poll(now, flag && flag2);
			if (flag)
			{
				TickTextures(now);
			}
			if (!flag || !hudVisible || !_inLevel || Plugin.Enabled == null || !Plugin.Enabled.Value)
			{
				Gate("gate");
				return;
			}
			if (!PlayerManager.HasLocalPlayerAgent())
			{
				Gate("no-local");
				return;
			}
			_local = PlayerManager.GetLocalPlayerAgent();
			Il2CppSystem.Collections.Generic.List<PlayerAgent> playerAgentsInLevel = PlayerManager.PlayerAgentsInLevel;
			if (playerAgentsInLevel == null || playerAgentsInLevel.Count == 0)
			{
				if (_emptySince < 0f)
				{
					_emptySince = now;
				}
				else if (now - _emptySince >= 0.75f)
				{
					Gate("empty-roster");
				}
				return;
			}
			_emptySince = -1f;
			_lastGate = null;
			bool flag3 = Plugin.TeammateMarkLogMarks != null && Plugin.TeammateMarkLogMarks.Value;
			int count = playerAgentsInLevel.Count;
			for (int i = 0; i < count; i++)
			{
				PlayerAgent playerAgent = playerAgentsInLevel[i];
				if (playerAgent == null || SafeIsLocal(playerAgent))
				{
					continue;
				}
				long num = KeyOf(playerAgent);
				if (num == 0L)
				{
					continue;
				}
				Runtime runtime = RuntimeFor(num);
				if (runtime == null)
				{
					continue;
				}
				runtime.Agent = playerAgent;
				ReadState(playerAgent, out var downed, out var reviving);
				if (!downed)
				{
					runtime.Anchor = null;
					runtime.AnchorKind = "none";
					runtime.AnchorAt = 0f;
				}
				else if (runtime.Anchor == null || now - runtime.AnchorAt >= 0.5f)
				{
					runtime.Anchor = ResolveAnchor(playerAgent, out runtime.AnchorKind);
					runtime.AnchorAt = now;
					if (flag3)
					{
						Log($"anchor;key={num};kind={runtime.AnchorKind};downed={downed};reviving={reviving}");
					}
				}
				Ledger.MarkSeen(num, downed, reviving, now);
			}
			int num2 = Ledger.ForgetUnseen(now, Sweep);
			if (num2 > 0)
			{
				for (int j = 0; j < Sweep.Count; j++)
				{
					Runtimes.Remove(Sweep[j]);
				}
				Log($"cleared=unseen;count={num2};active={Ledger.Count}");
			}
		}
		catch (System.Exception error)
		{
			Warn(error);
			Clear("tick-exception");
		}
	}

	internal static void Draw()
	{
		//IL_0136: Unknown result type (might be due to invalid IL or missing references)
		//IL_013b: Unknown result type (might be due to invalid IL or missing references)
		if (!_inLevel || Ledger.Count == 0 || Plugin.Enabled == null || !Plugin.Enabled.Value || Plugin.TeammateMarkEnabled == null || !Plugin.TeammateMarkEnabled.Value)
		{
			return;
		}
		Camera currentCamera;
		try
		{
			currentCamera = CameraManager.GetCurrentCamera();
		}
		catch (System.Exception error)
		{
			Warn(error);
			return;
		}
		if (currentCamera == null)
		{
			return;
		}
		Color color = GUI.color;
		try
		{
			_ = Time.unscaledTime;
			float num = BarPolicy.ResolutionScale(Screen.height);
			bool flag = TeammateMarkPolicy.ShouldHideForEmp(Plugin.TeammateMarkHideDuringEmp != null && Plugin.TeammateMarkHideDuringEmp.Value, EmpBridge.Available, EmpBridge.IsEmped);
			if (flag)
			{
				return;
			}
			float clearance = FeedbackPolicy.Clamp(Float(Plugin.TeammateMarkHeadClearance, 0.05f), 0f, 2f, 0.05f);
			float refDistance = Float(Plugin.TeammateMarkRefDistance, 8f);
			float minPixels = Float(Plugin.TeammateMarkMinPixels, 26f);
			float maxPixels = Float(Plugin.TeammateMarkMaxPixels, 42f);
			float maxDistance = Float(Plugin.TeammateMarkMaxDistance, 60f);
			TeammateMarkIcon icon = TeammateMarkPolicy.ParseIcon((Plugin.TeammateMarkIcon != null) ? Plugin.TeammateMarkIcon.Value : null);
			Texture2D texture2D = CurrentTexture();
			string glyph = TeammateMarkPolicy.IconGlyph(icon);
			var enumerator = Ledger.Values.GetEnumerator();
			try
			{
				Runtime runtime = default(Runtime);
				while (enumerator.MoveNext())
				{
					TeammateMarkLedger.Entry current = enumerator.Current;
					if (!current.Downed || !Runtimes.TryGetValue(current.Key, out runtime) || runtime == null || runtime.Agent == null || !TryWorld(runtime, clearance, out var world))
					{
						continue;
					}
					Vector3 vector;
					try
					{
						vector = currentCamera.WorldToScreenPoint(world);
					}
					catch (System.Exception error2)
					{
						Warn(error2);
						continue;
					}
					if (vector.z <= 0.1f)
					{
						continue;
					}
					float x = vector.x;
					float num2 = (float)Screen.height - vector.y;
					if (!TeammateMarkPolicy.OnScreen(x, num2, Screen.width, Screen.height, 8f * num))
					{
						continue;
					}
					float distance = DistanceTo(world);
					if (TeammateMarkPolicy.ShouldDrawMark(featureEnabled: true, _inLevel, hudVisible: true, isLocalPlayer: false, current.Downed, flag, hasScreenPoint: true, distance, maxDistance))
					{
						int num3 = TeammateMarkPolicy.FontPixels(30f, distance, refDistance, Screen.height, minPixels, maxPixels);
						Rect rect = new Rect(x - (float)num3 * 0.5f, num2 - (float)num3 - 4f * num, num3, num3);
						if (texture2D != null)
						{
							GUI.color = new Color(1f, 1f, 1f, 0.98f);
							GUI.DrawTexture(rect, texture2D, ScaleMode.ScaleToFit, alphaBlend: true);
						}
						else
						{
							DrawGlyphFallback(rect, glyph, num, num3);
						}
					}
				}
			}
			finally
			{
				((System.IDisposable)enumerator).Dispose();
			}
		}
		catch (System.Exception error3)
		{
			Warn(error3);
		}
		finally
		{
			GUI.color = color;
		}
	}

	private static void DrawGlyphFallback(Rect box, string glyph, float resolution, int pixels)
	{
		if (string.IsNullOrEmpty(glyph))
		{
			return;
		}
		if (_glyphStyle == null)
		{
			_glyphStyle = new GUIStyle(GUI.skin.label)
			{
				alignment = TextAnchor.MiddleCenter,
				fontStyle = FontStyle.Bold,
				clipping = TextClipping.Overflow,
				richText = false
			};
		}
		_glyphStyle.fontSize = pixels;
		GlyphContent.text = glyph;
		float num = Mathf.Max(1f, 1.4f * resolution);
		for (int i = -1; i <= 1; i++)
		{
			for (int j = -1; j <= 1; j++)
			{
				if (i != 0 || j != 0)
				{
					GUI.color = new Color(0.055f, 0.01f, 0.012f, 0.92f);
					GUI.Label(new Rect(box.x + (float)i * num, box.y + (float)j * num, box.width, box.height), GlyphContent, _glyphStyle);
				}
			}
		}
		GUI.color = new Color(0.878f, 0.282f, 0.302f, 0.98f);
		GUI.Label(box, GlyphContent, _glyphStyle);
	}

	private static void TickTextures(float now)
	{
		TeammateMarkIcon icon = TeammateMarkPolicy.ParseIcon((Plugin.TeammateMarkIcon != null) ? Plugin.TeammateMarkIcon.Value : null);
		string path = Path.Combine(IconTextureCache.IconRoot(), TeammateMarkPolicy.IconFileName(icon));
		Textures.Tick("mark-bundled", path, now);
		string text = TeammateMarkPolicy.CustomProbePath((Plugin.TeammateMarkCustomIcon != null) ? Plugin.TeammateMarkCustomIcon.Value : null, IconTextureCache.IconRoot());
		if (text != null)
		{
			Textures.Tick("mark-custom", text, now);
		}
	}

	private static Texture2D CurrentTexture()
	{
		return Textures.Texture("mark-custom") ?? Textures.Texture("mark-bundled");
	}

	private static bool TryWorld(Runtime runtime, float clearance, out Vector3 world)
	{
		world = Vector3.zero;
		try
		{
			Transform anchor = runtime.Anchor;
			if (anchor != null)
			{
				world = anchor.position;
				Vector3 up = anchor.up;
				if (up.sqrMagnitude < 0.0001f)
				{
					up = Vector3.up;
				}
				world += up * clearance;
				return true;
			}
			PlayerAgent agent = runtime.Agent;
			if (agent == null)
			{
				return false;
			}
			world = agent.EyePosition + Vector3.up * (0.18f + clearance);
			return true;
		}
		catch (System.Exception error)
		{
			runtime.Anchor = null;
			runtime.AnchorKind = "dead";
			Warn(error);
			return false;
		}
	}

	private static float DistanceTo(Vector3 world)
	{
		try
		{
			if (_local != null)
			{
				return Vector3.Distance(_local.transform.position, world);
			}
		}
		catch
		{
		}
		return 0f / 0f;
	}

	private static Transform ResolveAnchor(PlayerAgent agent, out string kind)
	{
		kind = "none";
		try
		{
			PlayerSyncModelData playerSyncModel = agent.PlayerSyncModel;
			if (playerSyncModel != null)
			{
				Transform nameNavMarkerAlign = playerSyncModel.m_nameNavMarkerAlign;
				if (nameNavMarkerAlign != null)
				{
					kind = "name-align";
					return nameNavMarkerAlign;
				}
				Transform neckBone = playerSyncModel.m_neckBone;
				if (neckBone != null)
				{
					kind = "neck";
					return neckBone;
				}
			}
		}
		catch (System.Exception error)
		{
			Warn(error);
		}
		return null;
	}

	private static void ReadState(PlayerAgent agent, out bool downed, out bool reviving)
	{
		downed = false;
		reviving = false;
		try
		{
			PlayerLocomotion locomotion = agent.Locomotion;
			if (!((object)(object)locomotion == null) && locomotion.m_currentStateEnum == PlayerLocomotion.PLOC_State.Downed)
			{
				downed = true;
				PLOC_Downed downed2 = locomotion.Downed;
				if (downed2 != null)
				{
					reviving = downed2.m_isRevived;
				}
			}
		}
		catch (System.Exception error)
		{
			downed = false;
			reviving = false;
			Warn(error);
		}
	}

	private static bool SafeIsLocal(PlayerAgent agent)
	{
		try
		{
			return PlayerManager.IsLocalAgent(agent);
		}
		catch
		{
			return true;
		}
	}

	private static long KeyOf(PlayerAgent agent)
	{
		try
		{
			SNet_Player owner = agent.Owner;
			if (owner != null && owner.Lookup != 0L)
			{
				return (long)owner.Lookup;
			}
		}
		catch
		{
		}
		try
		{
			int instanceID = agent.GetInstanceID();
			if (instanceID != 0)
			{
				return -instanceID;
			}
		}
		catch
		{
		}
		return 0L;
	}

	private static Runtime RuntimeFor(long key)
	{
		Runtime result = default(Runtime);
		if (Runtimes.TryGetValue(key, out result))
		{
			return result;
		}
		result = new Runtime();
		Runtimes[key] = result;
		return result;
	}

	private static void Gate(string reason)
	{
		Clear(reason);
		_lastGate = reason;
	}

	private static float Float(ConfigEntry<float> entry, float fallback)
	{
		if (entry == null || !KillPolicy.IsFinite(entry.Value))
		{
			return fallback;
		}
		return entry.Value;
	}

	private static void Log(string message)
	{
		if (LogBudget.TryUse(32))
		{
			Plugin.Instance?.Log.LogInfo("[MARK] " + message);
		}
	}

	private static void Warn(System.Exception error)
	{
		if (WarnBudget.TryUse(6))
		{
			Plugin.Instance?.Log.LogWarning("[MARK] " + error.Message);
		}
	}
}
