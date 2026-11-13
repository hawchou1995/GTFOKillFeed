using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using Player;
using UnityEngine;

namespace HawChow.GTFOKillFeed;

// Downed-teammate head marker ("菜" by default, "鶸" as the alternative). Purely local and
// read-only: it enumerates the same PlayerManager roster the game uses for teammate status, reads
// the locomotion enum, projects the game's own head name-marker anchor and draws through the
// existing KillHud IMGUI pass. No packets, no Harmony patches of its own, no writes to native HUD
// state, and no EMP dependency: the optional EOSExt_EMP probe is reflection only.
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

    private static readonly TeammateMarkLedger Ledger = new();
    private static readonly Dictionary<long, Runtime> Runtimes = new();
    private static readonly List<long> Sweep = new(4);
    private static readonly IconTextureCache Textures = new();
    private static readonly DiagnosticBudget LogBudget = new();
    private static readonly DiagnosticBudget WarnBudget = new();
    private static GUIStyle _glyphStyle;
    private static readonly GUIContent GlyphContent = new();
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
        if (state == eGameStateName.InLevel) EnterLevel();
        else Reset("state:" + state);
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
        bool had = Ledger.Count > 0 || Runtimes.Count > 0;
        Ledger.Clear();
        Runtimes.Clear();
        Sweep.Clear();
        _local = null;
        if (!had) return;
        Log($"cleared;reason={reason}");
    }

    internal static void Tick(float now, bool hudVisible)
    {
        try
        {
            bool enabled = Plugin.TeammateMarkEnabled != null && Plugin.TeammateMarkEnabled.Value;
            bool hideDuringEmp = Plugin.TeammateMarkHideDuringEmp != null && Plugin.TeammateMarkHideDuringEmp.Value;
            if (!_empLogHooked)
            {
                _empLogHooked = true;
                EmpBridge.Diagnostics = message => Log(message);
            }
            EmpBridge.Poll(now, enabled && hideDuringEmp);
            if (enabled) TickTextures(now);
            if (!enabled || !hudVisible || !_inLevel || Plugin.Enabled == null || !Plugin.Enabled.Value)
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
            var roster = PlayerManager.PlayerAgentsInLevel;
            if (roster == null || roster.Count == 0)
            {
                // A single empty frame happens while the level roster is rebuilt; only a sustained
                // empty window is treated as "left the level".
                if (_emptySince < 0f) _emptySince = now;
                else if (now - _emptySince >= TeammateMarkPolicy.EmptyRosterSeconds) Gate("empty-roster");
                return;
            }
            _emptySince = -1f;
            _lastGate = null;
            bool log = Plugin.TeammateMarkLogMarks != null && Plugin.TeammateMarkLogMarks.Value;
            int count = roster.Count;
            for (int i = 0; i < count; i++)
            {
                PlayerAgent agent = roster[i];
                if (agent == null) continue;
                if (SafeIsLocal(agent)) continue;
                long key = KeyOf(agent);
                if (key == 0L) continue;
                var runtime = RuntimeFor(key);
                if (runtime == null) continue;
                runtime.Agent = agent;
                ReadState(agent, out bool downed, out bool reviving);
                if (!downed)
                {
                    runtime.Anchor = null;
                    runtime.AnchorKind = "none";
                    runtime.AnchorAt = 0f;
                }
                else if (runtime.Anchor == null || now - runtime.AnchorAt >= AnchorRefreshSeconds)
                {
                    runtime.Anchor = ResolveAnchor(agent, out runtime.AnchorKind);
                    runtime.AnchorAt = now;
                    if (log) Log($"anchor;key={key};kind={runtime.AnchorKind};downed={downed};reviving={reviving}");
                }
                Ledger.MarkSeen(key, downed, reviving, now);
            }

            int removed = Ledger.ForgetUnseen(now, Sweep);
            if (removed > 0)
            {
                for (int i = 0; i < Sweep.Count; i++) Runtimes.Remove(Sweep[i]);
                Log($"cleared=unseen;count={removed};active={Ledger.Count}");
            }
        }
        catch (Exception error)
        {
            Warn(error);
            Clear("tick-exception");
        }
    }

    internal static void Draw()
    {
        if (!_inLevel || Ledger.Count == 0) return;
        if (Plugin.Enabled == null || !Plugin.Enabled.Value) return;
        if (Plugin.TeammateMarkEnabled == null || !Plugin.TeammateMarkEnabled.Value) return;
        Camera camera;
        try { camera = CameraManager.GetCurrentCamera(); }
        catch (Exception error) { Warn(error); return; }
        if (camera == null) return;

        Color previous = GUI.color;
        try
        {
            float now = Time.unscaledTime;
            float resolution = BarPolicy.ResolutionScale(Screen.height);
            bool hideDuringEmp = Plugin.TeammateMarkHideDuringEmp != null && Plugin.TeammateMarkHideDuringEmp.Value;
            bool empSuppressed = TeammateMarkPolicy.ShouldHideForEmp(hideDuringEmp, EmpBridge.Available, EmpBridge.IsEmped);
            if (empSuppressed) return;

            float clearance = FeedbackPolicy.Clamp(Float(Plugin.TeammateMarkHeadClearance, TeammateMarkPolicy.DefaultHeadClearance), 0f, 2f, TeammateMarkPolicy.DefaultHeadClearance);
            float refDistance = Float(Plugin.TeammateMarkRefDistance, TeammateMarkPolicy.DefaultRefDistance);
            float minPixels = Float(Plugin.TeammateMarkMinPixels, TeammateMarkPolicy.DefaultMinPixels);
            float maxPixels = Float(Plugin.TeammateMarkMaxPixels, TeammateMarkPolicy.DefaultMaxPixels);
            float maxDistance = Float(Plugin.TeammateMarkMaxDistance, TeammateMarkPolicy.DefaultMaxDistance);
            TeammateMarkIcon icon = TeammateMarkPolicy.ParseIcon(Plugin.TeammateMarkIcon != null ? Plugin.TeammateMarkIcon.Value : null);
            Texture2D texture = CurrentTexture();
            string glyph = TeammateMarkPolicy.IconGlyph(icon);

            foreach (var entry in Ledger.Values)
            {
                // m_isRevived is NOT a proven "stop drawing" signal (see ReadState), so a teammate
                // that is still in PLOC_State.Downed keeps its mark; only leaving Downed removes it.
                if (!entry.Downed) continue;
                if (!Runtimes.TryGetValue(entry.Key, out var runtime) || runtime == null || runtime.Agent == null) continue;
                if (!TryWorld(runtime, clearance, out Vector3 world)) continue;
                Vector3 screen;
                try { screen = camera.WorldToScreenPoint(world); }
                catch (Exception error) { Warn(error); continue; }
                if (screen.z <= 0.1f) continue;
                float x = screen.x;
                float y = Screen.height - screen.y;
                if (!TeammateMarkPolicy.OnScreen(x, y, Screen.width, Screen.height, ScreenMargin * resolution)) continue;
                float distance = DistanceTo(world);
                if (!TeammateMarkPolicy.ShouldDrawMark(true, _inLevel, true, false, entry.Downed,
                    empSuppressed, true, distance, maxDistance)) continue;
                int pixels = TeammateMarkPolicy.FontPixels(TeammateMarkPolicy.DefaultRefPixels, distance, refDistance,
                    Screen.height, minPixels, maxPixels);
                var box = new Rect(x - pixels * 0.5f, y - pixels - 4f * resolution, pixels, pixels);
                if (texture != null)
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.98f);
                    GUI.DrawTexture(box, texture, ScaleMode.ScaleToFit, true);
                }
                else
                {
                    DrawGlyphFallback(box, glyph, resolution, pixels);
                }
            }
        }
        catch (Exception error) { Warn(error); }
        finally { GUI.color = previous; }
    }

    // The bundled PNG already carries its own deep-red core, dark keyline and light rim. If it is
    // missing or a user replacement is unreadable the glyph is drawn as text in the same palette, so
    // the marker never becomes an empty white quad.
    private static void DrawGlyphFallback(Rect box, string glyph, float resolution, int pixels)
    {
        if (string.IsNullOrEmpty(glyph)) return;
        if (_glyphStyle == null)
            _glyphStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                clipping = TextClipping.Overflow,
                richText = false
            };
        _glyphStyle.fontSize = pixels;
        GlyphContent.text = glyph;
        float outline = Mathf.Max(1f, 1.4f * resolution);
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                GUI.color = new Color(0.055f, 0.01f, 0.012f, 0.92f);
                GUI.Label(new Rect(box.x + dx * outline, box.y + dy * outline, box.width, box.height), GlyphContent, _glyphStyle);
            }
        GUI.color = new Color(0.878f, 0.282f, 0.302f, 0.98f);
        GUI.Label(box, GlyphContent, _glyphStyle);
    }

    // Update-side only. Two independent slots: the bundled PNG that always ships, and an optional
    // user absolute path. No file-system call happens on this thread: the pipeline probes and reads
    // on a worker, and the paint pass only looks up what was applied.
    private static void TickTextures(float now)
    {
        TeammateMarkIcon icon = TeammateMarkPolicy.ParseIcon(Plugin.TeammateMarkIcon != null ? Plugin.TeammateMarkIcon.Value : null);
        string bundled = System.IO.Path.Combine(IconTextureCache.IconRoot(), TeammateMarkPolicy.IconFileName(icon));
        Textures.Tick("mark-bundled", bundled, now);
        string custom = Plugin.TeammateMarkCustomIcon != null ? Plugin.TeammateMarkCustomIcon.Value : null;
        string probe = TeammateMarkPolicy.CustomProbePath(custom, IconTextureCache.IconRoot());
        if (probe != null) Textures.Tick("mark-custom", probe, now);
    }

    private static Texture2D CurrentTexture()
        => Textures.Texture("mark-custom") ?? Textures.Texture("mark-bundled");

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
                if (up.sqrMagnitude < 0.0001f) up = Vector3.up;
                world += up * clearance;
                return true;
            }
            PlayerAgent agent = runtime.Agent;
            if (agent == null) return false;
            world = agent.EyePosition + Vector3.up * (EyeLift + clearance);
            return true;
        }
        catch (Exception error)
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
            if (_local != null) return Vector3.Distance(_local.transform.position, world);
        }
        catch { }
        return float.NaN;
    }

    private static Transform ResolveAnchor(PlayerAgent agent, out string kind)
    {
        kind = "none";
        try
        {
            var model = agent.PlayerSyncModel;
            if (model != null)
            {
                var align = model.m_nameNavMarkerAlign;
                if (align != null) { kind = "name-align"; return align; }
                var neck = model.m_neckBone;
                if (neck != null) { kind = "neck"; return neck; }
            }
        }
        catch (Exception error) { Warn(error); }
        return null;
    }

    // Only PLOC_State.Downed decides "downed". PLOC_Downed.m_isRevived is read for diagnostics only:
    // its real semantics (does it mean "being revived right now" or "was revived at some point") is
    // not proven by the decompiled interop, so hiding a still-Downed teammate on that opaque flag
    // would silently drop a valid mark. Leaving Downed is the only hide signal.
    private static void ReadState(PlayerAgent agent, out bool downed, out bool reviving)
    {
        downed = false;
        reviving = false;
        try
        {
            var locomotion = agent.Locomotion;
            if (locomotion == null) return;
            PlayerLocomotion.PLOC_State state = locomotion.m_currentStateEnum;
            if (state != PlayerLocomotion.PLOC_State.Downed) return;
            downed = true;
            var downedState = locomotion.Downed;
            if (downedState != null) reviving = downedState.m_isRevived;
        }
        catch (Exception error)
        {
            // An unreadable agent is treated as "state unknown" instead of staying marked.
            downed = false;
            reviving = false;
            Warn(error);
        }
    }

    private static bool SafeIsLocal(PlayerAgent agent)
    {
        try { return PlayerManager.IsLocalAgent(agent); }
        catch { return true; }
    }

    // SNet_Player.Lookup is the only stable per-level identity; the instance id is a fallback that is
    // deliberately negated so it can never collide with a real lookup (or with the 0 sentinel).
    private static long KeyOf(PlayerAgent agent)
    {
        try
        {
            var owner = agent.Owner;
            if (owner != null && owner.Lookup != 0UL) return (long)owner.Lookup;
        }
        catch { }
        try
        {
            int instance = agent.GetInstanceID();
            if (instance != 0) return -(long)instance;
        }
        catch { }
        return 0L;
    }

    private static Runtime RuntimeFor(long key)
    {
        if (Runtimes.TryGetValue(key, out var runtime)) return runtime;
        runtime = new Runtime();
        Runtimes[key] = runtime;
        return runtime;
    }

    private static void Gate(string reason)
    {
        Clear(reason);
        _lastGate = reason;
    }

    private static float Float(ConfigEntry<float> entry, float fallback)
        => entry != null && KillPolicy.IsFinite(entry.Value) ? entry.Value : fallback;

    private static void Log(string message)
    {
        if (LogBudget.TryUse(32)) Plugin.Instance?.Log.LogInfo("[MARK] " + message);
    }

    private static void Warn(Exception error)
    {
        if (WarnBudget.TryUse(6)) Plugin.Instance?.Log.LogWarning("[MARK] " + error.Message);
    }
}
