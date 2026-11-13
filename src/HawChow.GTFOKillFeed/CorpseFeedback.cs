using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Agents;
using CharacterDestruction;
using Enemies;
using FX_EffectSystem;
using HarmonyLib;
using UnityEngine;

namespace HawChow.GTFOKillFeed;

public sealed class CorpseVisualLease : MonoBehaviour
{
    private Renderer[] _renderers;
    private bool[] _enabled, _forceRenderingOff;
    private Rigidbody[] _bodies;
    private bool[] _kinematic, _gravity, _detectCollisions;
    private float[] _drag;
    // Review C3: the collision detection mode is captured per body and restored on release, so a high-speed
    // flight can use ContinuousDynamic without leaving the pooled ragdoll modified.
    private CollisionDetectionMode[] _detection;
    private Collider[] _colliders;
    private bool[] _colliderEnabled;
    internal Vector3[] Origins;
    internal int Generation;
    public CorpseVisualLease(IntPtr pointer) : base(pointer) { }
    internal void Capture()
    {
        RestoreState();
        _renderers = GetComponentsInChildren<Renderer>(true);
        _enabled = new bool[_renderers.Length]; _forceRenderingOff = new bool[_renderers.Length];
        for (int i = 0; i < _renderers.Length; i++)
        {
            _enabled[i] = _renderers[i].enabled;
            _forceRenderingOff[i] = _renderers[i].forceRenderingOff;
        }
        _bodies = GetComponentsInChildren<Rigidbody>(true);
        Origins = new Vector3[_bodies.Length]; _kinematic = new bool[_bodies.Length]; _gravity = new bool[_bodies.Length];
        _detectCollisions = new bool[_bodies.Length]; _drag = new float[_bodies.Length]; _detection = new CollisionDetectionMode[_bodies.Length];
        for (int i = 0; i < _bodies.Length; i++)
        {
            var body = _bodies[i]; Origins[i] = body.position; _kinematic[i] = body.isKinematic;
            _gravity[i] = body.useGravity; _detectCollisions[i] = body.detectCollisions; _drag[i] = body.drag;
            _detection[i] = body.collisionDetectionMode;
        }
        _colliders = GetComponentsInChildren<Collider>(true);
        _colliderEnabled = new bool[_colliders.Length];
        for (int i = 0; i < _colliders.Length; i++) _colliderEnabled[i] = _colliders[i].enabled;
    }
    internal int BodyCount => _bodies?.Length ?? 0;
    internal int RendererCount => _renderers?.Length ?? 0;
    internal int ColliderCount => _colliders?.Length ?? 0;
    // 1.2.19: the leased object's own transform. The game commonly arms its Decay on the RAGDOLL hierarchy
    // after a death (the corpse model carries none - the real log's reason=no-decay-component), so the native
    // dissolve has to be able to look there too. Read-only, and a destroyed lease answers null.
    internal Transform Root
    {
        get { try { return gameObject.transform; } catch { return null; } }
    }
    // B (1.2.19 real-machine): is this leased object ACTIVE in the hierarchy? A velocity written into an inactive
    // rigidbody is discarded by the next physics step, which is exactly why `launch=applied` moved nothing and the
    // later Hide() turned the corpse into "vanished in place". Fail closed: an unreadable/ destroyed lease is not live.
    internal bool Live
    {
        get { try { return gameObject.activeInHierarchy; } catch { return false; } }
    }
    internal Vector3 Center
    {
        get { if (_bodies != null && _bodies.Length > 0 && _bodies[0] != null) return _bodies[0].position; return transform.position; }
    }
    // The horizontal launch speed is handed in from the travel budget (FeedbackPolicy.LaunchSpeedFor); the
    // vertical kick keeps the historical 1.6 + strength. Nothing here clamps at 9 m/s any more: the caller
    // passes the budget-derived cap to Limit, so a configured 8.5 m budget is no longer flattened.
    // Review C3: a body that would move more than MaxDiscreteTravelPerStep in one fixed step is switched to
    // ContinuousDynamic for the flight (a 30 m budget is ~0.73 m/step at 36.7 m/s, i.e. straight through a
    // wall). Everything below the threshold keeps the mode the lease captured - a pooled ragdoll may ship with
    // ContinuousSpeculative, and forcing Discrete there would silently downgrade the game's own setting.
    // RestoreState puts the captured mode back on release, so the pooled ragdoll is left exactly as it was.
    internal void Launch(Vector3 direction, float strength, float speed)
    {
        if (_bodies == null || _bodies.Length == 0) return;
        float horizontal = speed > 0f ? speed : 0f;
        float vertical = FeedbackPolicy.LaunchUpSpeed(strength);
        bool continuous = FeedbackPolicy.NeedsContinuousCollision(horizontal);
        Vector3 center = Center;
        for (int i = 0; i < _bodies.Length; i++)
        {
            var body = _bodies[i];
            if (body == null) continue;
            body.isKinematic = false; body.useGravity = true; body.drag = FeedbackPolicy.CorpseDrag;
            body.collisionDetectionMode = continuous ? CollisionDetectionMode.ContinuousDynamic : CapturedDetection(i);
            Vector3 outward = body.position - center;
            if (outward.sqrMagnitude < 0.01f) outward = direction;
            else
            {
                outward.y = Mathf.Max(outward.y, 0.15f);
                outward = (outward.normalized * 0.8f + direction * 0.2f).normalized;
            }
            body.velocity = outward * horizontal + Vector3.up * vertical;
            body.angularVelocity = Vector3.Cross(outward, Vector3.up) * (1f + strength * 2f);
        }
    }
    // The collision detection mode this body had when the lease captured it. Discrete is only the fallback for a
    // body we never captured (or an out-of-range index), never a substitute for the captured value.
    private CollisionDetectionMode CapturedDetection(int index)
        => _detection != null && index >= 0 && index < _detection.Length ? _detection[index] : CollisionDetectionMode.Discrete;
    // Position safety net first (a body that tunnels through geometry is snapped back inside the budget
    // radius), then a velocity cap that follows the budget instead of the old flat 9 m/s. The snap-back is
    // decided by FeedbackPolicy.ShouldClampTravel, so the smoke tests exercise the production comparison.
    internal void Limit(float maxDistance, float maxSpeed)
    {
        if (_bodies == null) return;
        float cap = maxSpeed > 0f ? maxSpeed : FeedbackPolicy.LegacyVelocityCap;
        for (int i = 0; i < _bodies.Length; i++)
        {
            var body = _bodies[i]; if (body == null) continue;
            Vector3 delta = body.position - Origins[i];
            if (FeedbackPolicy.ShouldClampTravel(delta.sqrMagnitude, maxDistance))
            {
                body.position = Origins[i] + delta.normalized * maxDistance;
                body.velocity = Vector3.zero; body.angularVelocity = Vector3.zero;
            }
            if (body.velocity.sqrMagnitude > cap * cap) body.velocity = body.velocity.normalized * cap;
        }
    }
    // First visible renderer of the captured set: the target for the optional native dissolve. Null
    // after RestoreState (lease released) or when the model exposes nothing drawable.
    internal Renderer PrimaryRenderer()
    {
        if (_renderers == null) return null;
        Renderer fallback = null;
        for (int i = 0; i < _renderers.Length; i++)
        {
            Renderer candidate = _renderers[i];
            try
            {
                if (candidate == null || !candidate.enabled) continue;
                // 1.2.18 (fade root cause): these elements are `Renderer` wrappers, so `is SkinnedMeshRenderer` never
                // matched and the "prefer the skinned renderer" rule had never once fired. Ask the native class.
                if (FadeRendererCast.IsSkinned(candidate)) return candidate;
                if (fallback == null) fallback = candidate;
            }
            catch { /* one dead renderer must never abort the caller's cleanup */ }
        }
        return fallback;
    }
    internal void Hide()
    {
        if (_renderers == null) return;
        foreach (var renderer in _renderers)
            if (renderer != null) { renderer.forceRenderingOff = true; renderer.enabled = false; }
    }
    internal void RecyclePhysics()
    {
        if (_bodies != null) for (int i = 0; i < _bodies.Length; i++)
            try
            {
                var body = _bodies[i]; if (body == null) continue;
                body.velocity = Vector3.zero; body.angularVelocity = Vector3.zero;
                body.isKinematic = true; body.detectCollisions = false; body.Sleep();
            }
            catch { }
        if (_colliders != null) for (int i = 0; i < _colliders.Length; i++)
            try { if (_colliders[i] != null) _colliders[i].enabled = false; } catch { }
    }
    internal void RestoreState()
    {
        if (_renderers != null) for (int i = 0; i < _renderers.Length; i++)
            try { if (_renderers[i] != null) { _renderers[i].enabled = _enabled[i]; _renderers[i].forceRenderingOff = _forceRenderingOff[i]; } } catch { }
        if (_bodies != null) for (int i = 0; i < _bodies.Length; i++)
            try
            {
                var body = _bodies[i]; if (body == null) continue;
                body.velocity = Vector3.zero; body.angularVelocity = Vector3.zero;
                body.isKinematic = _kinematic[i]; body.useGravity = _gravity[i]; body.drag = _drag[i]; body.detectCollisions = _detectCollisions[i];
                body.collisionDetectionMode = CapturedDetection(i);
            }
            catch { }
        if (_colliders != null) for (int i = 0; i < _colliders.Length; i++)
            try { if (_colliders[i] != null) _colliders[i].enabled = _colliderEnabled[i]; } catch { }
        _renderers = null; _enabled = null; _forceRenderingOff = null; _bodies = null; _kinematic = null; _gravity = null;
        _detectCollisions = null; _drag = null; _detection = null; _colliders = null; _colliderEnabled = null; Origins = null;
        Generation = Generation == int.MaxValue ? 1 : Generation + 1;
    }
    private void OnDisable() => RestoreState();
}

internal static class CorpseFeedback
{
    private sealed class Corpse
    {
        internal EnemyAgent Enemy;
        internal IntPtr Identity;
        internal int ModelGeneration, RagdollGeneration;
        internal float At, Strength, BurstAt, EndAt;
        internal int Sequence;
        internal uint EnemyDataId;
        internal string Source;
        internal float BodySize = 1f;
        internal Vector3 Direction, Origin, BurstOrigin;
        internal bool Broken, Exploded, OffSceneCleanupApplied;
        // 1.2.16 (second real-machine pass): the launch state is closed out explicitly instead of being an
        // implicit "did we try yet" flag. `LaunchApplications` counts impulses that were really applied,
        // `LaunchLease`+`LaunchLeaseGeneration` remember WHICH capture received them (review R2: the game's
        // Capture() bumps the generation of the same object, so a pointer-only identity swallowed the
        // replacement impulse) and `LaunchClosed` means the decision is final - applied, or reported as
        // unavailable. Every removal path has to leave it closed, so a corpse can never vanish silently.
        internal bool LaunchClosed, LaunchUnavailableLogged;
        internal int LaunchApplications;
        internal IntPtr LaunchLease;
        // B (1.2.19 real-machine): the lease was usable but the game had not activated it yet. `RagdollInactive`
        // stays set once that happened (it is the terminal-state reason and the "do not hide this corpse" fact) and
        // `RagdollWaitStart` is the age the wait began, so a receipt can print how long the body stayed inactive.
        internal bool RagdollInactive;
        internal float RagdollWaitStart = -1f;
        internal int LaunchLeaseGeneration;
        internal int GibDataCalls, GibDataSetupMisses;
        // 1.2.16 launch bookkeeping: the budget frozen at the schedule (so the log, the applied speed and the
        // position clamp all describe the same number), the speed derived from it, and where the ragdoll came
        // from ("ragdoll-at-death" / "late-lookup" / "no-ragdoll-instance" / "no-rigidbody" / "capture-failed").
        internal float Travel, Speed;
        // `RagdollSource` is the ladder step that produced the lease ("ragdoll-at-death:ragdoll-instance",
        // "late-lookup:enemy-ragdoll-ref", ...) or the reason nothing was leased ("no-ragdoll-instance",
        // "no-rigidbody", "capture-failed", "enemy-destroyed"). `Ladder` is the last step that was tried, so
        // an unavailable launch names both the reason and the rung.
        internal string RagdollSource, Ladder, LadderTried, LadderHit;
        // Review C1: how many late lookups this corpse already spent (bounded by MaxRagdollLookupAttempts),
        // and the age of the FIRST one (a real-machine log can tell "never retried" from "retried at 0.04 s").
        internal int RagdollLookupAttempts;
        // R3: the first refused rung (fail-closed) and whether it was already reported, so the reason is in the
        // log once per corpse instead of once per tick.
        internal string Rejected;
        internal bool RejectLogged;
        // R1b: the post-deadline observation ran (at most once per corpse, always before EndAt).
        internal bool FinalProbeDone;
        internal float FirstLookupAge = -1f;
        // Snapshot of the hierarchy taken in the death call, so a late lookup can still search when the
        // EnemyAgent wrapper has already been destroyed/pooled (the wrapper turns into `null`, the transform
        // and the model GameObject usually survive a few hundred ms longer).
        internal Transform EnemyRoot, ModelRoot;
        internal CorpseVisualLease Model, Ragdoll;
        internal readonly List<CD_Gib> Gibs = new();
    }
    private static int _nextSequence;
    private static readonly List<Corpse> Active = new();
    private static readonly Dictionary<IntPtr, Corpse> Hidden = new();
    private static readonly Dictionary<IntPtr, Corpse> GibOwners = new();
    private static readonly Dictionary<IntPtr, Vector3> GibOrigins = new();
    private static readonly Dictionary<IntPtr, Vector3> GibScales = new();
    private static readonly CorpseDeathLedger DeathLedger = new();
    private static readonly HashSet<IntPtr> ActiveIdentities = new();
    private static readonly List<(IntPtr Identity, string Reason)> PruneKeys = new();
    private static FX_Pool _deathPool;
    private static Corpse _fragmentOwner;
    private static int _warnings;
    private static bool _outsideLevelSkipLogged;
    private static float _pruneAt;
    private static readonly CorpseSoundThrottle ExplosionSound = new();
    private static Vector3 _pendingSoundPosition;
    private static AudioClip _customExplosionClip;
    private static bool _customExplosionLoadAttempted;
    // Noise budgets (1.2.16): both of these reasons are EXPECTED on a real machine and were drowning the log
    // (163 duplicate-death-callback, 43 hidden=pruned/enemy-null). They are counted and logged sparsely; the
    // de-duplication and the pruning themselves are untouched.
    private static readonly LogTally _duplicateSkips = new();
    private static readonly LogTally _noisyPrunes = new();
    private static bool _gibUnavailableLogged;
    private static void Log(string message) => Plugin.Instance?.Log.LogInfo("[CORPSE] " + message);
    private static int NextSequence()
    {
        _nextSequence = _nextSequence == int.MaxValue ? 1 : _nextSequence + 1;
        return _nextSequence;
    }
    // Closure rule (1.2.16): a corpse may only leave the queue with its launch state resolved. If the
    // impulse was never applied and never reported, report it HERE, with the real reason - this is the
    // single choke point every removal path (stale lease, effects off, despawn, reset, tick exception)
    // goes through, so `launch=` can no longer be missing from a real-machine log.
    // R4: the EndAt stage removes its entry without RemoveQueued, so it calls the same CloseLaunch before
    // Active.RemoveAt - the receipt no longer depends on EndAt(0.75 s) happening to be later than the lookup
    // deadline (0.30 s).
    private static void CloseLaunch(Corpse item, float age)
    {
        if (!FeedbackPolicy.ShouldReportUnavailableLaunch(item.LaunchApplications, item.LaunchUnavailableLogged, Current().LaunchActive)) return;
        LogUnavailableLaunch(item, age);
    }
    // The five corpse sub-switches are resolved once per tick (and once per schedule) so a hot edit
    // cannot flip a channel in the middle of one corpse's burst.
    internal static CorpseSwitches Current()
        => new(Plugin.CorpseEffects == null || Plugin.CorpseEffects.Value,
            Plugin.CorpseLaunch == null || Plugin.CorpseLaunch.Value,
            Plugin.CorpseDismember == null || Plugin.CorpseDismember.Value,
            Plugin.CorpseChunks == null || Plugin.CorpseChunks.Value,
            Plugin.CorpseExplosionFx == null || Plugin.CorpseExplosionFx.Value,
            Plugin.CorpseBurstVisual == null || Plugin.CorpseBurstVisual.Value,
            Plugin.CorpseBurstSound == null || Plugin.CorpseBurstSound.Value);
    private static void RemoveQueued(Corpse item, string reason, float now)
    {
        CloseLaunch(item, Mathf.Max(0f, now - item.At));
        Log($"queue=removed;scheduleId={item.Sequence};source={item.Source};identity=0x{item.Identity.ToInt64():X};reason={reason};phase={(item.Exploded ? "post-burst" : "pre-burst")};age={Mathf.Max(0f, now - item.At):0.00}s;launch={LaunchState(item)}");
        Release(item, true);
    }
    internal static void Schedule(EnemyAgent enemy, Vector3 direction, float strength, string source)
    {
        if (!Plugin.Enabled.Value || !Plugin.CorpseEffects.Value) return;
        if (!DeathLedger.InLevel)
        {
            if (!_outsideLevelSkipLogged) { Log("schedule=skipped;reason=outside-level"); _outsideLevelSkipLogged = true; }
            return;
        }
        if (enemy == null) { Log("schedule=skipped;reason=enemy-null"); return; }
        IntPtr identity = enemy.Pointer;
        if (identity == IntPtr.Zero) { Log($"schedule=skipped;enemyId={enemy.EnemyDataID};reason=zero-identity"); return; }
        long key = identity.ToInt64();
        if (!DeathLedger.TrySchedule(key))
        {
            // Expected noise: InstantDead, OnDead and the lethal-damage finalizer all schedule the same death.
            // R2 (1.2.18): a death that was already handled to the end is a DIFFERENT reason - it is the pooled
            // wrapper's late OnDead callback for a corpse we already saw through, not a duplicate of a live one.
            string reason = DeathLedger.HasCompleted(key) ? "duplicate-after-close" : "duplicate-death-callback";
            int seen = _duplicateSkips.Add(reason);
            if (LogTally.ShouldLog(seen, 3, 50))
                Log($"schedule=skipped;enemyId={enemy.EnemyDataID};reason={reason};seen={seen};note={(reason == "duplicate-after-close" ? "same-wrapper-already-handled-this-death" : "expected-dedupe")}");
            return;
        }
        try
        {
            // 1.2.19 fix: the heading used to be "flatten the direction, and when the flattened part collapses (a
            // point-blank kill at your feet, |pitch| > 84.26 deg) use world +Z" - a direction with no relation to the
            // player's facing. FeedbackPolicy.LaunchHeading owns the fallback chain now (damage heading -> camera YAW
            // heading -> world +Z) and names the rung it used, so the receipt can prove which one happened.
            // Review R5: the pitch is measured on the vector the game handed us, BEFORE any camera fallback, and is
            // reported as `unavailable` when there is no usable vector - a zero direction is not "0 deg".
            bool haveRawPitch = FeedbackPolicy.TryLaunchPitchDegrees(direction.x, direction.y, direction.z, out float rawPitch);
            if (direction.sqrMagnitude < 0.01f)
            {
                var sourceCamera = CameraManager.GetCurrentCamera();
                direction = sourceCamera != null ? sourceCamera.transform.forward : Vector3.forward;
            }
            // Review R2: the camera yaw is only a heading when a camera really exists. The flag travels with the vector,
            // so a null or throwing CameraManager ends at world-forward instead of claiming `heading=camera-yaw`.
            bool cameraAvailable = false;
            Vector3 cameraYaw = Vector3.zero;
            try
            {
                var camera = CameraManager.GetCurrentCamera();
                if (camera != null)
                {
                    cameraYaw = Quaternion.Euler(0f, camera.transform.eulerAngles.y, 0f) * Vector3.forward;
                    cameraAvailable = true;
                }
            }
            catch (Exception error) { Warn(error); }
            LaunchHeadingSource heading = FeedbackPolicy.LaunchHeading(direction.x, direction.z, cameraAvailable,
                cameraYaw.x, cameraYaw.z, out float headingX, out float headingZ);
            direction = new Vector3(headingX, 0f, headingZ);
            float delay = FeedbackPolicy.Clamp(Plugin.CorpseExplosionDelay.Value, FeedbackPolicy.MinCorpseBurstDelay, FeedbackPolicy.MaxCorpseBurstDelay, FeedbackPolicy.CorpseBurstDelay);
            float bodySize = MeasureSize(enemy, out float volume);
            int sequence = NextSequence();
            float scheduledAt = Time.unscaledTime;
            source = string.IsNullOrWhiteSpace(source) ? "unknown" : source;
            // The budget is resolved here, from the player's own MinLaunchDistance/MaxLaunchDistance, burst
            // strength, body size bias and TravelScale, and then frozen for this corpse: launch, clamp and log
            // all describe THIS number (1.2.15 clamped it to a 12 m floor and never used it for the speed).
            float travel = FeedbackPolicy.Travel(FeedbackPolicy.Unit(strength), Plugin.MinLaunchDistance.Value, Plugin.MaxLaunchDistance.Value, bodySize)
                * FeedbackPolicy.Clamp(Plugin.CorpseTravelScale.Value, 0.5f, 1.5f, 1f);
            var item = new Corpse { Sequence = sequence, EnemyDataId = enemy.EnemyDataID, Source = source, Identity = identity, Enemy = enemy, At = scheduledAt, Strength = FeedbackPolicy.Unit(strength), BodySize = bodySize, Direction = direction.normalized, Origin = enemy.transform.position, BurstOrigin = Chest(enemy), BurstAt = delay, EndAt = delay + FeedbackPolicy.CorpsePostBurstHold, Travel = travel, EnemyRoot = SafeTransform(enemy) };
            try { var model = enemy.MainModelGO; if (model != null) item.ModelRoot = model.transform; } catch (Exception error) { Warn(error); }
            // Lease the ragdoll NOW, in the death call: 1.2.15 asked a destroyed/pooled EnemyAgent 40 ms later
            // and applied nothing on 97 of 97 corpses. The ladder below still has to retry (the game spawns the
            // ragdoll from a coroutine), so the snapshot taken here is what a late lookup searches when the
            // EnemyAgent wrapper is already gone.
            CaptureRagdoll(item, enemy);
            Active.Add(item);
            // Review C6: `budget` is the configured/biased budget, `appliedTravel` is the safety clamp it
            // produced (capped at the absolute ceiling and floored at the body size) - the same two numbers
            // the launch receipt prints, so the line can no longer claim 60 m while the body is clamped at 40.
            // Three-state ragdoll receipt: leased in the death call, or explicitly null here (with the rung).
            string ragdoll = item.Ragdoll != null
                ? $"at-death;ladder={LadderTried(item)};ladderHit={LadderHit(item)}"
                : $"null@death;reason={item.RagdollSource};ladder={LadderTried(item)}";
            Log($"schedule=accepted;scheduleId={sequence};source={source};identity=0x{identity.ToInt64():X};enemyId={enemy.EnemyDataID};strength={FeedbackPolicy.Unit(strength):0.##};delay={delay:0.00}s;scheduledAt={scheduledAt:0.00}s;bodyVolume={volume:0.###}m3;sizeFactor={bodySize:0.##};budget={travel:0.00}m;appliedTravel={FeedbackPolicy.AppliedTravel(travel, bodySize):0.00}m;ragdoll={ragdoll};lookupWindow={FeedbackPolicy.RagdollLookupDeadline(delay):0.00}s;heading={FeedbackPolicy.HeadingText(heading)};pitch={FeedbackPolicy.PitchText(haveRawPitch, rawPitch)};dir={direction.x:0.##},{direction.z:0.##};active={Active.Count}");
        }
        catch { DeathLedger.Unlock(key); throw; }
    }
    internal static void Tick(float now)
    {
        // Bookkeeping only for the optional native dissolve (never returns/recycles a borrowed instance).
        CorpseFade.Sweep();
        CorpseSwitches switches = Current();
        if (now >= _pruneAt)
        {
            _pruneAt = now + 2f;
            ActiveIdentities.Clear();
            foreach (var active in Active) ActiveIdentities.Add(active.Identity);
            PruneKeys.Clear();
            foreach (var hidden in Hidden.Values)
            {
                if (ActiveIdentities.Contains(hidden.Identity)) continue;
                try
                {
                    string removalReason = null;
                    if (hidden.Enemy == null) removalReason = "enemy-null";
                    else if (hidden.Enemy.Alive) removalReason = "enemy-alive";
                    else if (hidden.Model != null && !FeedbackPolicy.IsLeaseCurrent(hidden.ModelGeneration, hidden.Model.Generation)) removalReason = "model-lease-stale";
                    else if (hidden.Ragdoll != null && !FeedbackPolicy.IsLeaseCurrent(hidden.RagdollGeneration, hidden.Ragdoll.Generation)) removalReason = "ragdoll-lease-stale";
                    if (removalReason != null) PruneKeys.Add((hidden.Identity, removalReason));
                }
                catch (Exception error) { Warn(error); PruneKeys.Add((hidden.Identity, "prune-exception:" + error.GetType().Name)); }
            }
            foreach (var candidate in PruneKeys)
                if (Hidden.TryGetValue(candidate.Identity, out var item))
                {
                    // Expected noise: a pooled enemy is destroyed/despawned long after its corpse finished, so
                    // "enemy-null" dominates the log. Counted and printed sparsely; the pruning is unchanged.
                    int seen = _noisyPrunes.Add(candidate.Reason);
                    if (LogTally.ShouldLog(seen, 3, 25))
                        Log($"hidden=pruned;scheduleId={item.Sequence};source={item.Source};identity=0x{candidate.Identity.ToInt64():X};reason={candidate.Reason};seen={seen}");
                    Release(item, true);
                }
        }
        for (int i = Active.Count - 1; i >= 0; i--)
        {
            var item = Active[i];
            try
            {
                // The model lease is only the visual/hide handle. The game disables the living model when the
                // corpse switches to its spawned ragdoll (the real log removed the entry 0.19 s after death),
                // and 1.2.16 deleted the whole queue entry at that moment - which is exactly where a pending
                // launch disappeared. Drop the handle, keep the corpse: EndAt still ends it and RemoveQueued
                // closes the launch state.
                if (item.Model != null && !FeedbackPolicy.IsLeaseCurrent(item.ModelGeneration, item.Model.Generation))
                {
                    Log($"queue=model-lease-dropped;scheduleId={item.Sequence};source={item.Source};reason=model-lease-stale;launch={LaunchState(item)};phase={(item.Exploded ? "post-burst" : "pre-burst")};age={now - item.At:0.00}s");
                    item.Model = null;
                    item.ModelGeneration = 0;
                }
                // A stale ragdoll lease is not a removal reason either: it becomes a Missing/Empty lease, so the
                // ladder runs again and the decision ends in a real launch=applied or launch=unavailable.
                if (item.Ragdoll != null && !FeedbackPolicy.IsLeaseCurrent(item.RagdollGeneration, item.Ragdoll.Generation))
                {
                    item.Ragdoll = null;
                    item.RagdollGeneration = 0;
                    item.RagdollSource = "lease-stale";
                    // R5 (2nd review): the lease is gone, so "the ragdoll was inactive" can no longer be claimed.
                    item.RagdollInactive = false;
                    item.RagdollWaitStart = -1f;
                }
                float age = now - item.At;
                if (!Plugin.Enabled.Value || !Plugin.CorpseEffects.Value) { RemoveQueued(item, "effects-disabled", now); Active.RemoveAt(i); continue; }
                float enemyDistanceSquared = item.Enemy != null ? (item.Enemy.transform.position - item.Origin).sqrMagnitude : 0f;
                bool enemyOffScene = item.Enemy != null && FeedbackPolicy.IsCorpseOffSceneDistanceSquared(enemyDistanceSquared);
                if (enemyOffScene && !item.OffSceneCleanupApplied)
                {
                    int releasedGibs = item.Gibs.Count;
                    ReleaseGibs(item);
                    item.Model?.RecyclePhysics(); item.Ragdoll?.RecyclePhysics();
                    item.OffSceneCleanupApplied = true;
                    Log($"cleanup=offscene-recycled;scheduleId={item.Sequence};enemyId={item.EnemyDataId};releasedGibs={releasedGibs};ragdollBodies={item.Ragdoll?.BodyCount ?? 0};age={age:0.00}s");
                }
                // One launch decision, at 0.02 s and then on every tick until it is closed out - never a branch
                // that can fall through silently (1.2.16: `BodyCount == 0` did exactly that, 19 times).
                if (FeedbackPolicy.ShouldApplyLaunch(switches.LaunchActive, item.LaunchClosed, age)) TryLaunchOrAbandon(item, age);
                if (FeedbackPolicy.ShouldLimitRagdoll(switches.LaunchActive, item.OffSceneCleanupApplied))
                {
                    // Position safety net (never a floor, and never smaller than the body - review C2) plus a
                    // velocity cap derived from the applied speed: 1.2.15 cut every body at 9 m/s, which
                    // flattened every configured budget above ~5 m.
                    item.Ragdoll?.Limit(FeedbackPolicy.AppliedTravel(item.Travel, item.BodySize), FeedbackPolicy.LaunchVelocityCap(item.Speed));
                }
                if (FeedbackPolicy.ShouldTrackChunks(switches.ChunksActive) && !item.OffSceneCleanupApplied)
                {
                    LimitGibs(item);
                }
                if (!item.Broken && age >= 0.12f && switches.DismemberActive)
                {
                    item.Broken = true;
                    int broken = item.Enemy != null && FeedbackPolicy.ShouldBreakCorpseLimbs(item.OffSceneCleanupApplied, switches.DismemberActive) ? BreakLimbs(item) : 0;
                    string reason = item.OffSceneCleanupApplied ? "enemy-offscene-cleanup" : item.Enemy == null ? "enemy-null-fallback" : "attempted";
                    // GibData absence is a KNOWN environment property, not a failure of this plugin: every enemy
                    // family observed so far has a matched destruction setup with `entries=0`. So the per-corpse
                    // line stays compact (and stops printing gibDataCalls=0;gibsCaptured=0 as if something broke),
                    // and the situation is explained ONCE per level in `gibdata=unavailable`.
                    if (item.GibDataCalls == 0 && item.Gibs.Count == 0)
                        Log($"dismemberment=complete;scheduleId={item.Sequence};enemyId={item.EnemyDataId};limbsBroken={broken};reason={reason};gibs=none;attemptBudget={FeedbackPolicy.Fragments(item.Strength)}");
                    else
                        Log($"dismemberment=complete;scheduleId={item.Sequence};enemyId={item.EnemyDataId};limbsBroken={broken};reason={reason};gibDataCalls={item.GibDataCalls};setupIdMisses={item.GibDataSetupMisses};gibsCaptured={item.Gibs.Count};attemptBudget={FeedbackPolicy.Fragments(item.Strength)}");
                    if (!_gibUnavailableLogged && item.GibDataCalls == 0 && item.Gibs.Count == 0 && broken > 0)
                    {
                        _gibUnavailableLogged = true;
                        Log($"gibdata=unavailable;reason=matched-setup-has-no-gibdata;note=known-environment-no-GibData;effect=local-mesh-destruction+fx-only;budget=once-per-level");
                    }
                }
                if (!item.Exploded && age >= item.BurstAt)
                {
                    item.Exploded = true;
                    Vector3 center = item.Ragdoll != null ? item.Ragdoll.Center : item.Enemy != null ? Chest(item.Enemy) : item.BurstOrigin;
                    float bodyDistanceSquared = (center - item.Origin).sqrMagnitude;
                    bool useCapturedOrigin = item.OffSceneCleanupApplied
                        || FeedbackPolicy.ShouldUseCapturedBurstOrigin(enemyDistanceSquared, bodyDistanceSquared);
                    if (useCapturedOrigin) center = item.BurstOrigin;
                    CorpseBurstPlan plan = FeedbackPolicy.BurstPlan(switches, item.BodySize, item.Strength);
                    int bursts = plan.Bursts, played = 0;
                    float spread = FeedbackPolicy.BurstSpread(item.Strength, item.BodySize);
                    for (int burst = 0; burst < bursts; burst++)
                    {
                        Vector3 position = center;
                        if (burst > 0)
                        {
                            float angle = burst * 2.39996f;
                            position += new Vector3(Mathf.Cos(angle) * spread, 0.3f + 0.25f * burst, Mathf.Sin(angle) * spread);
                        }
                        if (Explode(position)) played++;
                    }
                    string sound = plan.Sound ? RequestExplosionSound(center) : "disabled";
                    if (!plan.Sound)
                    {
                        // Never leave a coalesced burst sound queued while the effect is off: it would
                        // otherwise play up to 120 ms after the switch was flipped.
                        ExplosionSound.Reset();
                        _pendingSoundPosition = Vector3.zero;
                    }
                    Log($"burst=complete;scheduleId={item.Sequence};enemyId={item.EnemyDataId};burstAge={age:0.00}s;burstAt={item.BurstAt:0.00}s;spreadM={spread:0.00};anchor={(useCapturedOrigin ? "captured" : "live")};fx={played}/{bursts};sound={sound};ragdollBodies={item.Ragdoll?.BodyCount ?? 0};dismembermentStage={(item.Broken ? "attempted" : "not-run")};gibDataCalls={item.GibDataCalls};setupIdMisses={item.GibDataSetupMisses};gibsCaptured={item.Gibs.Count};sizeFactor={item.BodySize:0.##}");
                    // Re-lease the model purely as a visual/hide handle, and never let a despawned or destroyed
                    // model root throw out of the whole corpse tick.
                    TryLeaseModel(item);
                    Hidden[item.Identity] = item;
                }
                if (age >= item.EndAt)
                {
                    ReleaseGibs(item);
                    // R4: this branch removes the entry itself, so it closes the launch state through the same
                    // helper RemoveQueued uses (a pending impulse can never leave the queue without a receipt).
                    CloseLaunch(item, age);
                    // B + R2/R3 (2nd review): ONE fact decides the whole release (FeedbackPolicy.ShouldHideCorpse):
                    // a corpse whose ragdoll never activated keeps the game's own presentation (no fade/hide/recycle).
                    bool releaseVisual = FeedbackPolicy.ShouldHideCorpse(item.LaunchApplications, item.RagdollInactive);
                    if (releaseVisual)
                    {
                        // Optional native dissolve, started before Hide so the clone keeps the corpse's last pose.
                        // CorpseFadePrep owns the exception barrier: a throwing probe cannot skip Hide/Recycle/RemoveAt.
                        CorpseFadePrep.Attempt(
                            () => FadeRenderer(item),
                            fadeTarget =>
                            {
                                CorpseFade.TryBegin(fadeTarget as Renderer, item.OffSceneCleanupApplied, DeathLedger.InLevel, item.Sequence, NativeDecay(item));
                                return true;
                            },
                            Warn);
                        item.Model?.Hide(); item.Ragdoll?.Hide();
                        bool physicsAlreadyRecycled = item.OffSceneCleanupApplied;
                        if (!physicsAlreadyRecycled)
                        {
                            item.Model?.RecyclePhysics(); item.Ragdoll?.RecyclePhysics();
                        }
                        Log($"cleanup=physics-recycled;scheduleId={item.Sequence};enemyId={item.EnemyDataId};modelRenderers={item.Model?.RendererCount ?? 0};ragdollRenderers={item.Ragdoll?.RendererCount ?? 0};modelColliders={item.Model?.ColliderCount ?? 0};ragdollColliders={item.Ragdoll?.ColliderCount ?? 0};early={physicsAlreadyRecycled};visual=hidden;applications={item.LaunchApplications}");
                    }
                    else
                    {
                        // R3 (2nd review): nothing was hidden or recycled on this path, so the receipt must not claim
                        // it was - `early=` stays the real off-scene fact. R2: a kept corpse is not dissolved either;
                        // this is still its one and only terminal fade= line.
                        Log($"fade=skipped;reason=corpse-kept-inactive;scheduleId={item.Sequence};enemyId={item.EnemyDataId};applications={item.LaunchApplications};age={age:0.00}s;note=game-corpse-presentation-kept");
                        Log($"cleanup=physics-kept;scheduleId={item.Sequence};enemyId={item.EnemyDataId};modelRenderers={item.Model?.RendererCount ?? 0};ragdollRenderers={item.Ragdoll?.RendererCount ?? 0};early={item.OffSceneCleanupApplied};visual=kept-ragdoll-inactive;applications={item.LaunchApplications}");
                    }
                    Active.RemoveAt(i);
                }
            }
            catch (Exception error) { Log($"tick=failed;scheduleId={item.Sequence};error={error.GetType().Name}"); Warn(error); RemoveQueued(item, "tick-exception", now); Active.RemoveAt(i); }
        }
        if (!Plugin.Enabled.Value || !FeedbackPolicy.ShouldFlushCorpseSound(switches.BurstSoundActive))
        {
            ExplosionSound.Reset();
            _pendingSoundPosition = Vector3.zero;
        }
        else FlushExplosionSound(now);
    }
    private static string RequestExplosionSound(Vector3 position)
    {
        if (!ExplosionSound.Request(Time.unscaledTime)) { _pendingSoundPosition = position; return "coalesced"; }
        return PostExplosionSound(position);
    }
    private static void FlushExplosionSound(float now)
    {
        if (ExplosionSound.TryFlush(now))
            Log($"sound={PostExplosionSound(_pendingSoundPosition)};coalesced=true");
    }
    private static string PostExplosionSound(Vector3 position)
    {
        AudioClip clip = LoadExplosionSound();
        if (clip != null)
        {
            try { AudioSource.PlayClipAtPoint(clip, position, 0.82f); return "custom"; }
            catch (Exception error) { Warn(error); }
        }
        try { CellSound.Post(AK.EVENTS.ENEMYEXPLODE, position); return "game-fallback"; }
        catch (Exception error) { Warn(error); return "failed"; }
    }
    private static AudioClip LoadExplosionSound()
    {
        if (_customExplosionLoadAttempted) return _customExplosionClip;
        _customExplosionLoadAttempted = true;
        try
        {
            using var stream = typeof(Plugin).Assembly.GetManifestResourceStream("KillFeed.Audio.corpse-burst.wav");
            if (stream == null) throw new InvalidOperationException("Embedded corpse burst WAV is missing.");
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            byte[] wav = buffer.ToArray();
            if (wav.Length < 12 || Encoding.ASCII.GetString(wav, 0, 4) != "RIFF" || Encoding.ASCII.GetString(wav, 8, 4) != "WAVE")
                throw new InvalidOperationException("Embedded corpse burst WAV has an invalid RIFF header.");

            int format = 0, channels = 0, sampleRate = 0, bitsPerSample = 0, dataOffset = -1, dataLength = 0;
            for (int offset = 12; offset <= wav.Length - 8;)
            {
                string chunk = Encoding.ASCII.GetString(wav, offset, 4);
                int length = BitConverter.ToInt32(wav, offset + 4);
                int content = offset + 8;
                if (length < 0 || content > wav.Length - length) throw new InvalidOperationException("Embedded corpse burst WAV has a malformed chunk.");
                if (chunk == "fmt ")
                {
                    if (length < 16) throw new InvalidOperationException("Embedded corpse burst WAV format chunk is incomplete.");
                    format = BitConverter.ToUInt16(wav, content);
                    channels = BitConverter.ToUInt16(wav, content + 2);
                    sampleRate = BitConverter.ToInt32(wav, content + 4);
                    bitsPerSample = BitConverter.ToUInt16(wav, content + 14);
                }
                else if (chunk == "data") { dataOffset = content; dataLength = length; }
                long next = (long)content + length + (length & 1);
                if (next > wav.Length) throw new InvalidOperationException("Embedded corpse burst WAV chunk exceeds the file length.");
                offset = (int)next;
            }
            if (format != 1 || channels != 1 || sampleRate < 8000 || sampleRate > 192000 || bitsPerSample != 16
                || dataOffset < 0 || dataLength <= 0 || dataLength % (channels * (bitsPerSample / 8)) != 0)
                throw new InvalidOperationException("Embedded corpse burst WAV must be mono PCM16 with a valid sample rate.");

            int sampleCount = dataLength / 2;
            var samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                int sampleOffset = dataOffset + i * 2;
                short sample = unchecked((short)(wav[sampleOffset] | wav[sampleOffset + 1] << 8));
                samples[i] = sample / 32768f;
            }
            AudioClip clip = AudioClip.Create("KillFeed_CorpseBurst", sampleCount, channels, sampleRate, false);
            if (clip == null || !clip.SetData(new Il2CppStructArray<float>(samples), 0))
                throw new InvalidOperationException("Unity could not load the embedded corpse burst samples.");
            _customExplosionClip = clip;
        }
        catch (Exception error) { Warn(error); }
        return _customExplosionClip;
    }
    private static CorpseVisualLease Lease(GameObject obj)
    {
        var lease = obj.GetComponent<CorpseVisualLease>();
        if (lease == null) lease = obj.AddComponent<CorpseVisualLease>();
        lease.Capture();
        return lease;
    }
    // ---- ragdoll resolution ladder (1.2.16) ----------------------------------------------------------
    // The corpse ragdoll is spawned by EnemyRagdollBodyController.LinkToSpawnedRagdoll, a coroutine, so
    // EnemyAgent.RagdollInstance is still null inside the death call. 1.2.16 read it once and then retried
    // only inside [40 ms, burst delay) - three frames at 60 fps, zero on a stuttering frame - which is why the
    // real log still showed `launch=unavailable;reason=no-ragdoll-instance;attempts=0` and no applied launch.
    // The window now ends at burst + grace (FeedbackPolicy.RagdollLookupDeadline) and every attempt walks
    // this ladder until one rung really owns a rigidbody:
    //   1. EnemyAgent.RagdollInstance                       (the game's own reference)
    //   2. RagdollScript                                    (spawned ragdoll root; `ragdolled` first)
    //   3. EnemyRagdollRef + m_rigidBodyLookup               (the ragdoll's own body registry)
    //   4. GetComponentsInChildren<Rigidbody>(true)         (last resort, never the living model root)
    // The first accepted lease is cached for the rest of the corpse, so one kill resolves at most once.
    private static void CaptureRagdoll(Corpse item, EnemyAgent enemy) => ResolveRagdoll(item, enemy, true);
    private static void TryLateRagdoll(Corpse item) => ResolveRagdoll(item, item.Enemy, false);
    private static void ResolveRagdoll(Corpse item, EnemyAgent enemy, bool atDeath)
    {
        // A lease that lost its bodies is not a ragdoll: 1.2.16 returned here on `Ragdoll != null` alone, so a
        // corpse whose captured object had been emptied by the game never looked again (19 silent corpses).
        if (item.Ragdoll != null && item.Ragdoll.BodyCount > 0) return;
        string failure = "no-ragdoll-instance";
        bool searched = false;
        try
        {
            if (enemy != null)
            {
                NoteRung(item, "ragdoll-instance");
                GameObject instance = null;
                bool instanceUnreadable = false;
                try
                {
                    instance = enemy.RagdollInstance;
                    // A wrapper whose native object is already gone is unreadable, not a candidate.
                    if (instance != null && instance.Pointer == IntPtr.Zero) { instance = null; instanceUnreadable = true; }
                }
                catch (Exception error) { Warn(error); instanceUnreadable = true; failure = "capture-failed"; }
                // 1.2.19 (real-machine fix): rung 1 is the game's OWN reference, so the living-hierarchy
                // heuristic no longer applies to it. After a death the game parents its ragdoll under the model,
                // which is exactly why the 1.2.18 R3 guard refused 3 of 9 corpses with
                // `rung=ragdoll-instance;reason=instance-under-living-model` and then reported
                // `launch=unavailable;attempts=17`. Only provable nonsense is refused, each with its own name:
                // the reference being null (the normal coroutine state - no line of its own), the living
                // model/enemy root ITSELF, an unreadable reference, and a lease that carries no rigidbody.
                bool instanceIsRoot = instance != null && IsExactLivingRoot(instance, item);
                var verdict = FeedbackPolicy.ClassifyRagdollInstance(instance != null, instanceIsRoot, instanceUnreadable);
                if (FeedbackPolicy.AcceptRagdollInstance(verdict))
                {
                    if (AcceptRagdoll(item, instance, atDeath)) return;
                    // The lease just measured the instance for us: an empty one is a named refusal, and rungs 2-4
                    // still get their chance (they are the only remaining source of a real ragdoll).
                    verdict = FeedbackPolicy.ClassifyRagdollBodies(verdict, item.RagdollSource == "capture-failed" ? -1 : 0);
                }
                if (instance != null && !FeedbackPolicy.AcceptRagdollInstance(verdict))
                    NoteReject(item, FeedbackPolicy.Reason(verdict), "ragdoll-instance");
            }
            for (int pass = 0; pass < 2; pass++)
            {
                Transform root = pass == 0 ? item.EnemyRoot : item.ModelRoot;
                if (root == null || (pass == 1 && item.ModelRoot == item.EnemyRoot)) continue;
                searched = true;
                SearchRagdollRoot(item, root, atDeath);
                if (item.Ragdoll != null) return;
            }
            if (!searched) NoteRung(item, enemy == null ? "enemy-wrapper-and-hierarchy-gone" : "no-hierarchy");
        }
        catch (Exception error) { Warn(error); failure = "capture-failed"; }
        // Nothing was leased: report why, never a silent skip. `no-rigidbody`/`capture-failed` (set by a rung
        // that did find a candidate) win over the generic reason, because they are the actionable detail.
        if (item.Ragdoll == null)
        {
            if (item.RagdollSource == "capture-failed") failure = "capture-failed";
            else if (item.RagdollSource == "no-rigidbody" && failure != "capture-failed") failure = "no-rigidbody";
            item.RagdollSource = searched || enemy != null ? failure : "enemy-destroyed";
        }
    }
    // Rungs 2-4 for one search root (the agent root, then the model root when it is a different object).
    // Rungs 2-4 for one search root (the agent root, then the model root when it is a different object).
    // R3 (1.2.18): every rung now reports evidence to FeedbackPolicy.AcceptRagdollCandidate, which fails
    // closed. A RagdollScript is only usable when its `ragdolled` flag was READ and was TRUE (1.2.16 pass 0
    // accepted exactly the non-ragdolled scripts), the raw rung requires a non-kinematic body, and any host
    // that is / contains the living enemy or its model is rejected outright.
    private static void SearchRagdollRoot(Corpse item, Transform root, bool atDeath)
    {
        RagdollScript[] scripts = Components<RagdollScript>(root);
        for (int i = 0; i < scripts.Length; i++)
        {
            var script = scripts[i];
            if (script == null) continue;
            bool known = false, ragdolled = false;
            try { ragdolled = script.ragdolled; known = true; } catch (Exception error) { Warn(error); }
            GameObject host = SafeGameObject(script);
            NoteRung(item, "ragdoll-script");
            var evidence = new FeedbackPolicy.RagdollCandidate(IsLivingModelHost(host, item), known, ragdolled, false);
            if (!FeedbackPolicy.AcceptRagdollCandidate(FeedbackPolicy.RagdollRung.RagdollScript, evidence))
            {
                NoteReject(item, known ? "script-not-ragdolled" : "script-flag-unreadable", "ragdoll-script");
                continue;
            }
            if (AcceptRagdoll(item, host, atDeath)) return;
        }
        EnemyRagdollRef[] refs = Components<EnemyRagdollRef>(root);
        for (int i = 0; i < refs.Length; i++)
        {
            var reference = refs[i];
            if (reference == null) continue;
            try { if (reference.m_rigidBodyLookup == null) continue; } catch (Exception error) { Warn(error); continue; }
            GameObject host = SafeGameObject(reference);
            NoteRung(item, "enemy-ragdoll-ref");
            var evidence = new FeedbackPolicy.RagdollCandidate(IsLivingModelHost(host, item), false, false, false);
            if (!FeedbackPolicy.AcceptRagdollCandidate(FeedbackPolicy.RagdollRung.EnemyRagdollRef, evidence))
            {
                NoteReject(item, "ref-held-by-living-host", "enemy-ragdoll-ref");
                continue;
            }
            if (AcceptRagdoll(item, host, atDeath)) return;
        }
        Rigidbody[] bodies = Components<Rigidbody>(root);
        for (int i = 0; i < bodies.Length; i++)
        {
            var body = bodies[i];
            GameObject host = SafeHost(body);
            bool moving = false;
            try { moving = body != null && !body.isKinematic; } catch (Exception error) { Warn(error); moving = false; }
            NoteRung(item, "raw-rigidbodies");
            var evidence = new FeedbackPolicy.RagdollCandidate(IsLivingModelHost(host, item), false, false, moving);
            if (!FeedbackPolicy.AcceptRagdollCandidate(FeedbackPolicy.RagdollRung.RawBodies, evidence))
            {
                NoteReject(item, moving ? "raw-host-is-living" : "raw-kinematic-only", "raw-rigidbodies");
                continue;
            }
            if (AcceptRagdoll(item, host, atDeath)) return;
        }
    }
    // R1b: the observation-only probe for a frame that jumped past deadline + slack. It lives in its own method so
    // TryLaunchOrAbandon keeps exactly ONE late-lookup call site (review C1) and can never gain a second impulse
    // path: this probe only refreshes the lease facts printed on the launch=unavailable receipt.
    private static void FinalRagdollProbe(Corpse item, float age)
    {
        item.FinalProbeDone = true;
        TryLateRagdoll(item);
        Log($"ragdoll=final-probe;scheduleId={item.Sequence};enemyId={item.EnemyDataId};attempts={item.RagdollLookupAttempts};age={age:0.00}s;window={FeedbackPolicy.RagdollLookupDeadline(item.BurstAt):0.00}s;endAt={FeedbackPolicy.CorpseEndAt(item.BurstAt):0.00}s;haveInstance={(item.Ragdoll != null ? "yes" : "no")};bodies={RagdollBodies(item)};note=probe-after-hitch");
    }
    // One bounded line per corpse: which rung was refused and why. Fail-closed is only useful if the log says so.
    private static void NoteReject(Corpse item, string reason, string rung)
    {
        if (item.RejectLogged) return;
        item.RejectLogged = true;
        item.Rejected = reason;
        Log($"candidate=rejected;scheduleId={item.Sequence};enemyId={item.EnemyDataId};rung={rung};reason={reason};note=fail-closed-no-lease");
    }
    // One candidate -> one lease. A candidate without any rigidbody is rejected (and remembered as
    // `no-rigidbody`, so the next rung still runs but the log keeps the detail), an exception as
    // `capture-failed`.
    private static bool AcceptRagdoll(Corpse item, GameObject host, bool atDeath)
    {
        if (host == null) return false;
        try
        {
            var lease = Lease(host);
            if (lease == null || lease.BodyCount <= 0)
            {
                lease?.RestoreState();
                if (item.RagdollSource != "capture-failed") item.RagdollSource = "no-rigidbody";
                return false;
            }
            item.Ragdoll = lease;
            item.RagdollGeneration = lease.Generation;
            item.LadderHit = item.Ladder;
            item.RagdollSource = (atDeath ? "ragdoll-at-death:" : "late-lookup:") + item.Ladder;
            // R5 (2nd review): a NEW lease is a new activation question - the inactive/waited state belongs to the
            // previous object and must never leak into this one's receipt.
            item.RagdollInactive = false;
            item.RagdollWaitStart = -1f;
            return true;
        }
        catch (Exception error) { Warn(error); item.RagdollSource = "capture-failed"; return false; }
    }
    private static T[] Components<T>(Transform root) where T : Component
    {
        try { return root != null ? root.GetComponentsInChildren<T>(true) : Array.Empty<T>(); }
        catch (Exception error) { Warn(error); return Array.Empty<T>(); }
    }
    private static GameObject SafeGameObject(Component component)
    {
        try { return component != null ? component.gameObject : null; } catch (Exception error) { Warn(error); return null; }
    }
    // A ragdoll limb's parent is the ragdoll root; the enemy/model root itself is never a ragdoll.
    private static GameObject SafeHost(Rigidbody body)
    {
        try
        {
            var transform = body != null ? body.transform : null;
            if (transform == null) return null;
            return transform.parent != null ? transform.parent.gameObject : transform.gameObject;
        }
        catch (Exception error) { Warn(error); return null; }
    }
    // 1.2.19: rung 1 only refuses the living model/enemy root ITSELF. The ancestor/descendant heuristic stays
    // with rungs 2-4 (IsLivingModelHost), where the candidate really is an arbitrary hierarchy node - applying
    // it to the game's own RagdollInstance is what killed 3 of 9 real-machine launches.
    private static bool IsExactLivingRoot(GameObject host, Corpse item)
    {
        if (host == null) return false;
        try
        {
            if (item.ModelRoot != null && host == item.ModelRoot.gameObject) return true;
            if (item.EnemyRoot != null && host == item.EnemyRoot.gameObject) return true;
        }
        catch (Exception error) { Warn(error); return true; }
        return false;
    }
    private static bool IsLivingModelHost(GameObject host, Corpse item)
    {
        if (host == null) return true;
        try
        {
            if (item.ModelRoot != null && host == item.ModelRoot.gameObject) return true;
            if (item.EnemyRoot != null && host == item.EnemyRoot.gameObject) return true;
            // R3: a host that CONTAINS the living model is the enemy wrapper (or an ancestor of it), never a
            // ragdoll. 1.2.16 only compared against the two exact roots, so a child of the living model was
            // accepted as a corpse.
            if (item.ModelRoot != null && item.ModelRoot.IsChildOf(host.transform)) return true;
            // F2 (1.2.18 review): the ANCESTOR direction alone left the mirror case open - a host that is a
            // DESCENDANT of the living model (a bone / limb collider) passed the raw-rigidbody and ref rungs and
            // could be pushed. Reject that direction too; EnemyRoot is deliberately NOT used here, because a
            // real ragdoll is commonly parented under the enemy root and must stay acceptable.
            if (item.ModelRoot != null && host.transform.IsChildOf(item.ModelRoot)) return true;
        }
        catch (Exception error) { Warn(error); return true; }
        return false;
    }
    private static Transform SafeTransform(EnemyAgent enemy)
    {
        try { return enemy != null ? enemy.transform : null; } catch (Exception error) { Warn(error); return null; }
    }
    private static string FirstLookupAge(Corpse item) => item.FirstLookupAge >= 0f ? $"{item.FirstLookupAge:0.00}s" : "never";
    // The probe receipt needs the body count, but TryLaunchOrAbandon must not read BodyCount itself: that decision
    // belongs to ClassifyRagdoll/LeaseState (the 1.2.16 silent hole). One helper, called from the diagnostic line.
    private static int RagdollBodies(Corpse item) => item.Ragdoll != null ? item.Ragdoll.BodyCount : 0;
    // B (1.2.19 real-machine): a usable lease that never became live is its own terminal reason - it was NOT "no
    // ragdoll", so the receipt has to say so (reason=ragdoll-inactive) instead of naming the last search result.
    private static string UnavailableReason(Corpse item)
        => FeedbackPolicy.IsInactiveRagdollLaunch(item.LaunchApplications, item.RagdollInactive, LeaseLive(item))
            ? "ragdoll-inactive"
            : string.IsNullOrEmpty(item.RagdollSource) ? "no-ragdoll-instance" : item.RagdollSource;
    // The launch state as it appears in any log line - one implementation, in the policy, so a corpse can never
    // be `applied#N` and `pending` at the same time (R5).
    private static string LaunchState(Corpse item) => FeedbackPolicy.LaunchState(item.LaunchApplications, item.LaunchClosed);
    // The three observable lease facts in one place (present? how many bodies? still the lease we captured?).
    private static FeedbackPolicy.RagdollLease LeaseState(Corpse item)
    {
        bool current = item.Ragdoll != null && FeedbackPolicy.IsLeaseCurrent(item.RagdollGeneration, item.Ragdoll.Generation);
        int bodies = current ? item.Ragdoll.BodyCount : 0;
        return FeedbackPolicy.ClassifyRagdoll(item.Ragdoll != null, bodies, current);
    }
    // B (1.2.19 real-machine): the one "can this body really take an impulse" fact the launch gate is asked with.
    // Fail closed through the policy: an unreadable or destroyed lease is not live, so nothing is pushed into it.
    private static bool LeaseLive(Corpse item)
    {
        try { return FeedbackPolicy.IsRagdollLive(item.Ragdoll != null && item.Ragdoll.Live); }
        catch (Exception error) { Warn(error); return false; }
    }
    // B: how long the impulse waited for the game to activate the leased body (0 when it was live on the first try).
    private static float WaitedSeconds(Corpse item, float age)
        => item.RagdollWaitStart >= 0f && KillPolicy.IsFinite(age)
            ? Mathf.Max(0f, age - item.RagdollWaitStart)
            : 0f;
    // The ONLY place that decides what a corpse's launch is. Every exit either applies the impulse (and
    // records it) or closes the corpse out with a launch=unavailable receipt - there is no third branch, which
    // is exactly the hole the real machine hit 19 times (a lease with BodyCount == 0 fell through both).
    // The impulse is applied on the FIRST tick that owns a usable lease (0.02 s), never at the burst, so the
    // burst FX and the launch are decoupled.
    // Review R1/R2: there is exactly ONE impulse site and it is guarded by FeedbackPolicy.CanApplyLaunch, which
    // owns the budget (applications <= 0 or ShouldReapplyLaunch) and takes the lease identity from
    // IsSameLaunchLease (pointer AND generation). The second ApplyLaunch in 1.2.16 bypassed both, so one corpse
    // could be pushed on every retry, and a re-captured lease swallowed its replacement impulse while the log
    // still reported it as applied.
    private static void TryLaunchOrAbandon(Corpse item, float age)
    {
        FeedbackPolicy.RagdollLease lease = LeaseState(item);
        if (FeedbackPolicy.ShouldReplaceRagdollLease(lease))
        {
            // The object we captured lost its bodies (the game switches the corpse over to its spawned
            // ragdoll and disables what we leased). That is not "no ragdoll": drop it and search again.
            Log($"ragdoll=discarded;scheduleId={item.Sequence};enemyId={item.EnemyDataId};reason=lease-empty;attempts={item.RagdollLookupAttempts};ladder={LadderTried(item)};age={age:0.00}s");
            item.Ragdoll = null;
            item.RagdollGeneration = 0;
            item.RagdollSource = "no-bodies";
            // R5 (2nd review): the discarded object's activation state goes with it.
            item.RagdollInactive = false;
            item.RagdollWaitStart = -1f;
            lease = FeedbackPolicy.RagdollLease.Missing;
        }
        // Both predicates take `haveRagdoll`; the retry is only reached WITHOUT a usable lease, so it is asked
        // with `false`. Passing true would disable the retry AND the give-up receipt - exactly the silent hole
        // this version is fixing.
        // R4 (2nd review): a USABLE lease that the game never activated is the second retry reason. Without it the
        // ladder was locked for the rest of the corpse and a live replacement ragdoll could never be picked up. The
        // re-resolve stays inside the existing time-paced schedule (no per-tick scan), the same attempt budget and
        // the same impulse gate - it only decides WHICH object the one impulse may be applied to.
        bool inactiveLease = FeedbackPolicy.ShouldApplyLaunch(lease) && !LeaseLive(item)
            && FeedbackPolicy.ShouldRetryInactiveLease(item.RagdollLookupAttempts, item.RagdollWaitStart, age);
        if ((!FeedbackPolicy.ShouldApplyLaunch(lease) || inactiveLease)
            && FeedbackPolicy.ShouldRetryRagdollLookup(false, item.RagdollLookupAttempts, age, item.BurstAt))
        {
            if (item.FirstLookupAge < 0f) item.FirstLookupAge = age;
            item.RagdollLookupAttempts++;
            TryLateRagdoll(item);
            if (item.Ragdoll != null)
                Log($"ragdoll=late@attempts={item.RagdollLookupAttempts};scheduleId={item.Sequence};enemyId={item.EnemyDataId};firstLookupAge={FirstLookupAge(item)};ladder={LadderTried(item)};ladderHit={LadderHit(item)};age={age:0.00}s;note=coroutine-spawned-ragdoll");
            lease = LeaseState(item);
            // R1 (1.2.18): the schedule is time-paced, so this line also marks the cross-deadline probe.
            if (item.Ragdoll == null && item.RagdollLookupAttempts > FeedbackPolicy.MaxRagdollLookupAttempts)
                Log($"ragdoll=final-probe;scheduleId={item.Sequence};enemyId={item.EnemyDataId};attempts={item.RagdollLookupAttempts};age={age:0.00}s;window={FeedbackPolicy.RagdollLookupDeadline(item.BurstAt):0.00}s;note=scheduled-final-point");
        }
        // B (1.2.19 real-machine): the gate is asked with the LIVE fact as well - a usable lease the game has not
        // activated yet cannot take an impulse (the velocity would be discarded), so nothing is applied into it.
        if (FeedbackPolicy.CanApplyLaunch(lease, LeaseLive(item), item.LaunchApplications, SameLease(item), age, item.BurstAt))
        {
            ApplyLaunch(item, age);
            return;
        }
        if (FeedbackPolicy.ShouldApplyLaunch(lease))
        {
            if (!LeaseLive(item))
            {
                // B: the body exists but is still inactive. Keep the launch PENDING (the same tick retries it, the
                // same time bound still ends it) and remember when the wait started so the receipts can say how long.
                // Nothing is closed here: if the body never becomes live, the EndAt stage reports ragdoll-inactive.
                item.RagdollInactive = true;
                if (item.RagdollWaitStart < 0f) item.RagdollWaitStart = age;
                return;
            }
            // Already pushed, and this lease either received it or is inside the window: nothing left to decide
            // until the budget is spent or the deadline passes.
            if (FeedbackPolicy.ShouldCloseAppliedLaunch(item.LaunchApplications, age, item.BurstAt)) item.LaunchClosed = true;
            return;
        }
        if (!FeedbackPolicy.ShouldAbandonRagdollLookup(false, item.RagdollLookupAttempts, age, item.BurstAt)) return;
        // R1b: a single frame that jumped from inside the window to past deadline + slack would otherwise close
        // the corpse without one last look. This probe is observation-only (no impulse: CanApplyLaunch owns that
        // window), bounded to once per corpse and to before EndAt, so the receipt can say whether a body was
        // there after all (launch=unavailable;haveRagdoll=yes;bodies=N).
        if (FeedbackPolicy.ShouldRunFinalRagdollProbe(false, item.RagdollLookupAttempts, item.FinalProbeDone, age, item.BurstAt))
        {
            FinalRagdollProbe(item, age);
            lease = LeaseState(item);
            // B (1.2.18 review): the terminal probe is not a dead end - it re-enters the SAME single impulse gate.
            // A body the game only handed over after the hitch is launched here (once, before EndAt); an object
            // that already received the impulse is never pushed again (same pointer), and the gate itself refuses
            // anything at/after EndAt or with the launch channel off.
            if (FeedbackPolicy.CanApplyLaunch(lease, LeaseLive(item), item.LaunchApplications, SameLease(item), age, item.BurstAt))
            {
                ApplyLaunch(item, age);
                return;
            }
        }
        item.LaunchClosed = true;
        CloseLaunch(item, age);
    }
    // R2: the identity of the lease that received the impulse. Pointer AND generation, because the game's
    // Capture() bumps the generation of the same object every time it is re-captured.
    private static bool SameLease(Corpse item)
        => item.Ragdoll != null
            && FeedbackPolicy.IsSameLaunchLease(item.LaunchLease, item.LaunchLeaseGeneration, item.Ragdoll.Pointer, item.Ragdoll.Generation);
    // The single unavailable receipt: the real reason, the attempt count, BOTH ladder facts, and the same
    // budget/appliedTravel/speed/distance set the applied line prints, so a cfg can be reconciled line by line.
    private static void LogUnavailableLaunch(Corpse item, float age)
    {
        // R5: closing the launch state here is what stops a `launch=unavailable` line from being followed by a
        // `launch=pending` line for the same entity (the removal log reads the same field).
        item.LaunchClosed = true;
        item.LaunchUnavailableLogged = true;
        Log($"launch=unavailable;scheduleId={item.Sequence};enemyId={item.EnemyDataId};reason={UnavailableReason(item)};attempts={item.RagdollLookupAttempts};firstLookupAge={FirstLookupAge(item)};haveRagdoll={(item.Ragdoll != null ? "yes" : "no")};bodies={(item.Ragdoll != null ? item.Ragdoll.BodyCount : 0)};ladder={LadderTried(item)};ladderHit={LadderHit(item)};budget={item.Travel:0.00}m;appliedTravel={FeedbackPolicy.AppliedTravel(item.Travel, item.BodySize):0.00}m;speed=0.00m/s;distance=0.00m;age={age:0.00}s;window={FeedbackPolicy.RagdollLookupDeadline(item.BurstAt):0.00}s;wait={WaitedSeconds(item, age):0.00}s;active={(LeaseLive(item) ? "True" : "False")};note={(item.LaunchApplications > 0 ? (item.Ragdoll != null ? "already-launched-this-body" : "launched-body-recycled") : "no-usable-ragdoll")};burst-and-dismemberment-unaffected");
    }
    // R8: `ladder=` never prints an empty field - the policy falls back to the last rung that was really tried, and
    // a corpse whose lease went stale prints that reason instead of an empty value.
    private static string LadderTried(Corpse item) => FeedbackPolicy.LadderText(item.LadderTried, item.Ladder);
    private static string LadderHit(Corpse item) => string.IsNullOrEmpty(item.LadderHit) ? "none" : item.LadderHit;
    // Records a rung that was really tried, without duplicates: `ladder=ragdoll-instance|ragdoll-script` plus
    // `ladderHit=ragdoll-script`. 1.2.16 printed only the LAST rung, which read as "only one rung was tried".
    private static void NoteRung(Corpse item, string rung)
    {
        item.Ladder = rung;
        if (string.IsNullOrEmpty(item.LadderTried)) { item.LadderTried = rung; return; }
        if (item.LadderTried.IndexOf(rung, StringComparison.Ordinal) < 0) item.LadderTried += "|" + rung;
    }
    // A renderer for the optional dissolve: the ragdoll's when we have one, then the model's, and finally any
    // live renderer in the snapshotted hierarchy. That last step is what a corpse WITHOUT a ragdoll needs -
    // 1.2.16 could only answer "no-renderer" for those, which is why it never dissolved them.
    private static object FadeRenderer(Corpse item)
    {
        Renderer renderer = item.Ragdoll?.PrimaryRenderer() ?? item.Model?.PrimaryRenderer();
        if (renderer != null) return renderer;
        return HierarchyRenderer(item.ModelRoot) ?? HierarchyRenderer(item.EnemyRoot);
    }
    private static Renderer HierarchyRenderer(Transform root)
    {
        if (root == null) return null;
        try
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            Renderer fallback = null;
            for (int i = 0; i < renderers.Length; i++)
            {
                var candidate = renderers[i];
                if (candidate == null || !candidate.enabled) continue;
                // 1.2.18 (fade root cause): these elements are `Renderer` wrappers, so `is SkinnedMeshRenderer` never
                // matched and the "prefer the skinned renderer" rule had never once fired. Ask the native class.
                if (FadeRendererCast.IsSkinned(candidate)) return candidate;
                if (fallback == null) fallback = candidate;
            }
            return fallback;
        }
        catch (Exception error) { Warn(error); return null; }
    }
    // The corpse's own native Decay component (the one the game's ES_Dead drives). Read-only lookup over the
    // snapshotted roots AND the ragdoll lease root: nothing is created, no pool is touched, and a destroyed
    // hierarchy answers null.
    // 1.2.19: the ragdoll comes first. The real log printed `fade=native-unavailable;reason=no-decay-component`
    // on every corpse because the game arms Decay on the RAGDOLL hierarchy after a death, which the 1.2.18
    // lookup (model root + enemy root only) never searched.
    private static Decay NativeDecay(Corpse item)
    {
        Transform ragdoll = item.Ragdoll != null ? item.Ragdoll.Root : null;
        Transform[] roots = { ragdoll, item.ModelRoot, item.EnemyRoot };
        for (int i = 0; i < roots.Length; i++)
        {
            if (roots[i] == null) continue;
            try
            {
                var decay = roots[i].GetComponentInChildren<Decay>(true);
                if (decay != null) return decay;
            }
            catch (Exception error) { Warn(error); }
        }
        return null;
    }
    // Re-lease the model as a visual/hide handle only, inside its own barrier: a destroyed or pooled model
    // root must never throw out of the corpse tick.
    private static void TryLeaseModel(Corpse item)
    {
        try
        {
            var model = item.Enemy != null ? item.Enemy.MainModelGO : null;
            if (model == null) return;
            var existing = model.GetComponent<CorpseVisualLease>();
            item.Model = existing != null && item.Ragdoll != null && existing.Pointer == item.Ragdoll.Pointer ? item.Ragdoll : Lease(model);
            item.ModelGeneration = item.Model.Generation;
            if (item.OffSceneCleanupApplied) item.Model.RecyclePhysics();
        }
        catch (Exception error) { Warn(error); }
    }
    // One launch = one receipt: the configured budget, the safety clamp it produced, the speed derived from
    // it and the forward-model distance, so a real-machine log can be reconciled with the cfg. Review C4:
    // `path` names where the ragdoll actually came from (RagdollSource) - never a give-up reason.
    private static void ApplyLaunch(Corpse item, float age)
    {
        int bodies = item.Ragdoll.BodyCount;
        item.Speed = FeedbackPolicy.LaunchSpeedFor(item.Travel, item.Strength);
        item.Ragdoll.Launch(item.Direction, item.Strength, item.Speed);
        item.RagdollGeneration = item.Ragdoll.Generation;
        item.LaunchApplications++;
        // R2: remember the generation with the pointer, so a re-captured lease is a NEW addressee for the impulse.
        item.LaunchLease = item.Ragdoll.Pointer;
        item.LaunchLeaseGeneration = item.RagdollGeneration;
        string path = string.IsNullOrEmpty(item.RagdollSource) ? "late-lookup" : item.RagdollSource;
        Log($"launch=applied;scheduleId={item.Sequence};enemyId={item.EnemyDataId};path={path};application=#{item.LaunchApplications};ragdollBodies={bodies};attempts={item.RagdollLookupAttempts};firstLookupAge={FirstLookupAge(item)};ladder={LadderTried(item)};ladderHit={LadderHit(item)};budget={item.Travel:0.00}m;appliedTravel={FeedbackPolicy.AppliedTravel(item.Travel, item.BodySize):0.00}m;speed={item.Speed:0.00}m/s;distance={FeedbackPolicy.LaunchDistance(item.Speed, item.Strength):0.00}m;capped={(FeedbackPolicy.LaunchSpeedCapped(item.Travel, item.Strength) ? "speed" : "no")};collision={(FeedbackPolicy.NeedsContinuousCollision(item.Speed) ? "continuous" : "discrete")};mode=radial;live={(item.Ragdoll.Live ? "True" : "False")};waited={WaitedSeconds(item, age):0.00}s;age={age:0.00}s");
    }
    private static Vector3 Chest(EnemyAgent enemy)
    {
        var chest = enemy.ModelRef?.m_chestBone;
        return chest != null ? chest.position : enemy.transform.position + Vector3.up;
    }
    private static float MeasureSize(EnemyAgent enemy, out float volume)
    {
        volume = 0f;
        try
        {
            var burnable = enemy.GetComponentInChildren<BurnableSkinnedMesh>(true);
            var mesh = burnable != null ? burnable.m_skinnedMesh : null;
            if (mesh != null)
            {
                Vector3 size = mesh.bounds.size;
                volume = Math.Abs(size.x * size.y * size.z);
            }
        }
        catch (Exception error) { Warn(error); }
        return FeedbackPolicy.SizeFactor(volume);
    }
    private static int ActivateGibData(CD_CharacterDestructionCollider destruction, byte destructionId, Vector3 position, Vector3 force, int maxCalls, out bool setupMatched, out int setupMatches, out int dataEntries)
    {
        setupMatched = false;
        setupMatches = 0;
        dataEntries = 0;
        if (destruction == null || maxCalls <= 0) return 0;
        var setups = destruction.m_allowedDestructions;
        if (setups == null) return 0;
        int activated = 0;
        for (int i = 0; i < setups.Count && activated < maxCalls; i++)
        {
            var setup = setups[i];
            if (setup == null || setup.m_id != destructionId) continue;
            setupMatched = true;
            setupMatches++;
            var gibData = setup.m_gibDatas;
            if (gibData == null || gibData.Length == 0) continue;
            dataEntries += gibData.Length;
            for (int j = 0; j < gibData.Length && activated < maxCalls; j++)
            {
                var chunk = gibData[j];
                if (chunk == null || chunk.m_gibPrefab == null) continue;
                try { chunk.Activate(position, force); activated++; }
                catch (Exception error) { Warn(error); }
            }
        }
        return activated;
    }
    private static int BreakLimbs(Corpse item)
    {
        var limbs = item.Enemy.Damage?.DamageLimbs;
        if (limbs == null) return 0;
        int budget = FeedbackPolicy.Fragments(item.Strength), broken = 0;
        _fragmentOwner = item;
        try
        {
            for (int i = limbs.Length - 1; i >= 0 && broken < budget; i--)
            {
                var limb = limbs[i];
                var destruction = limb?.m_destruction;
                if (limb == null || limb.IsDestroyed || destruction == null || !destruction.HasDestruction()) continue;
                float angle = (broken + 1) * 2.39996f;
                var scatter = new Vector3(Mathf.Cos(angle), 0.65f, Mathf.Sin(angle));
                Vector3 force = scatter.normalized * (1.8f + 3f * item.Strength);
                if (destruction.TryDoDestruction(ImpactDirection.Front, item.Direction, Vector3.zero, force,
                    item.Strength > 0.45f ? CD_DestructionSeverity.Death : CD_DestructionSeverity.Severe, false, out var data))
                {
                    Vector3 impactPosition = limb.transform.position;
                    int allowedSetups = 0;
                    try { var allowed = destruction.m_allowedDestructions; allowedSetups = allowed != null ? allowed.Count : 0; }
                    catch (Exception error) { Warn(error); }
                    var root = destruction.m_characterDestruction;
                    bool meshDestroyed = false;
                    if (root != null)
                    {
                        try { root.ApplyDestruction(item.Enemy, data); meshDestroyed = true; }
                        catch (Exception error) { Warn(error); }
                    }
                    if (item.GibDataCalls < 24)
                    {
                        int calls = ActivateGibData(destruction, data.m_destructionID, impactPosition,
                            scatter.normalized * FeedbackPolicy.GibLaunchSpeed(item.Strength), 24 - item.GibDataCalls,
                            out bool setupMatched, out int setupMatches, out int dataEntries);
                        item.GibDataCalls += calls;
                        if (!setupMatched) item.GibDataSetupMisses++;
                        Log($"gibdata=resolved;enemyId={item.Enemy.EnemyDataID};destructionId={data.m_destructionID};setupMatched={setupMatched};setupMatches={setupMatches};entries={dataEntries};activateCalls={calls}");
                    }
                    limb.DestroyLimb(data);
                    FX_Manager.PlayEffect(true, FX_GroupName.Impact_EnemyBody, impactPosition, Quaternion.identity, false);
                    Log($"destruction=local;enemyId={item.Enemy.EnemyDataID};limb={i};destructionId={data.m_destructionID};meshDestroyed={meshDestroyed};setups={allowedSetups};gibs={item.Gibs.Count}");
                    broken++;
                }
            }
        }
        finally { _fragmentOwner = null; }
        return broken;
    }
    internal static void GibReused(CD_Gib gib)
    {
        if (gib == null) return;
        if (GibOwners.TryGetValue(gib.Pointer, out var owner))
            for (int i = owner.Gibs.Count - 1; i >= 0; i--) if (owner.Gibs[i].Pointer == gib.Pointer) owner.Gibs.RemoveAt(i);
        GibOwners.Remove(gib.Pointer); GibOrigins.Remove(gib.Pointer);
        if (GibScales.TryGetValue(gib.Pointer, out var originalScale))
        {
            var reuseBody = gib.m_rigidbody;
            if (reuseBody != null && reuseBody.transform != null) reuseBody.transform.localScale = originalScale;
            GibScales.Remove(gib.Pointer);
        }
    }
    internal static void CaptureGib(CD_Gib gib)
    {
        if (!FeedbackPolicy.ShouldTrackChunks(Current().ChunksActive) || _fragmentOwner == null || gib == null || _fragmentOwner.Gibs.Count >= 24) return;
        foreach (var existing in _fragmentOwner.Gibs) if (existing.Pointer == gib.Pointer) return;
        _fragmentOwner.Gibs.Add(gib);
        GibOwners[gib.Pointer] = _fragmentOwner;
        var body = gib.m_rigidbody;
        if (body == null) return;
        GibOrigins[gib.Pointer] = body.position;
        var gibTransform = body.transform;
        if (gibTransform != null && !GibScales.ContainsKey(gib.Pointer))
        {
            GibScales[gib.Pointer] = gibTransform.localScale;
            gibTransform.localScale = gibTransform.localScale * FeedbackPolicy.GibScale(_fragmentOwner.BodySize);
        }
        float angle = _fragmentOwner.Gibs.Count * 2.39996f;
        Vector3 scatter = new Vector3(Mathf.Cos(angle), 0.7f, Mathf.Sin(angle)).normalized;
        Vector3 radial = body.position - _fragmentOwner.Origin;
        if (radial.sqrMagnitude > 0.01f)
        {
            radial.y = Mathf.Max(radial.y, 0.25f);
            scatter = (radial.normalized * 0.7f + scatter * 0.3f).normalized;
        }
        body.velocity = scatter * FeedbackPolicy.GibLaunchSpeed(_fragmentOwner.Strength);
    }
    private static void LimitGibs(Corpse owner)
    {
        float radius = FeedbackPolicy.FragmentRadius(owner.Strength, owner.BodySize);
        foreach (var gib in owner.Gibs)
        {
            if (gib == null || !GibOwners.TryGetValue(gib.Pointer, out var current) || !ReferenceEquals(current, owner)) continue;
            var body = gib.m_rigidbody;
            if (body == null || !GibOrigins.TryGetValue(gib.Pointer, out var origin)) continue;
            Vector3 delta = body.position - origin;
            if (delta.sqrMagnitude > radius * radius) { body.position = origin + delta.normalized * radius; body.velocity = Vector3.zero; }
        }
    }
    private static bool Explode(Vector3 position)
    {
        if (_deathPool == null)
        {
            var prefab = Resources.Load<GameObject>("FXC_FlyerDeath");
            if (prefab == null) { Warn(new InvalidOperationException("FXC_FlyerDeath resource unavailable")); return false; }
            _deathPool = FX_Manager.CreatePool(prefab);
        }
        if (_deathPool == null) { Warn(new InvalidOperationException("FXC_FlyerDeath pool unavailable")); return false; }
        var effect = _deathPool.AquireEffect();
        if (effect == null) { Warn(new InvalidOperationException("FXC_FlyerDeath pool returned no effect")); return false; }
        effect.Play(null, position, Quaternion.identity);
        return true;
    }
    private static void ReleaseGibs(Corpse item)
    {
        while (item.Gibs.Count > 0)
        {
            int before = item.Gibs.Count;
            var gib = item.Gibs[before - 1];
            if (gib == null) { item.Gibs.RemoveAt(before - 1); continue; }
            try
            {
                bool owned = GibOwners.TryGetValue(gib.Pointer, out var owner) && ReferenceEquals(owner, item);
                if (owned) GibReused(gib); else item.Gibs.RemoveAt(before - 1);
                if (owned) gib.Deactivate();
            }
            catch (Exception error) { Warn(error); if (item.Gibs.Count == before) item.Gibs.RemoveAt(before - 1); }
        }
    }
    private static void Release(Corpse item, bool removeHidden)
    {
        try { ReleaseGibs(item); } catch (Exception error) { Warn(error); }
        try { if (item.Model != null && FeedbackPolicy.IsLeaseCurrent(item.ModelGeneration, item.Model.Generation)) item.Model.RestoreState(); } catch (Exception error) { Warn(error); }
        try { if (item.Ragdoll != null && FeedbackPolicy.IsLeaseCurrent(item.RagdollGeneration, item.Ragdoll.Generation)) item.Ragdoll.RestoreState(); } catch (Exception error) { Warn(error); }
        if (removeHidden) { Hidden.Remove(item.Identity); DeathLedger.Close(item.Identity.ToInt64()); }
    }
    internal static void Restore(EnemyAgent enemy, string reason)
    {
        if (enemy == null) return;
        IntPtr identity = enemy.Pointer;
        long ledgerKey = identity.ToInt64();
        // R2/F1 (1.2.18 review): a despawn must NOT forget the identity - that is exactly how sid1 @377.12 s
        // turned into a second, full schedule (sid3 @385.48 s) for the same corpse. Only a real wrapper REUSE
        // (EnemyAgent.Setup hands the pooled object to a new living enemy) unlocks it again.
        // F1: the cleanup below RE-CLOSES the identity (Release(hidden, true) and every RemoveQueued go through
        // DeathLedger.Close), so an Unlock issued before it was silently undone and the NEXT death of the reused
        // wrapper was rejected as "already handled". Order: cleanup first, Unlock last - and even if it throws.
        bool unlockAfterCleanup = reason != FeedbackPolicy.CorpseDespawnReason;
        try { CleanupRestored(identity, reason); }
        finally { if (unlockAfterCleanup) DeathLedger.Unlock(ledgerKey); }
    }
    // Everything that must happen before the ledger may be unlocked: release the hidden corpse of this identity,
    // then keep or remove its queued entries. Split out only so Restore can order it before the Unlock (F1).
    private static void CleanupRestored(IntPtr identity, string reason)
    {
        Corpse hidden = Hidden.TryGetValue(identity, out var existing) ? existing : null;
        bool hiddenIsActive = false;
        for (int i = Active.Count - 1; i >= 0; i--)
            if (Active[i].Identity == identity && ReferenceEquals(Active[i], hidden)) hiddenIsActive = true;
        if (hidden != null && !hiddenIsActive)
        {
            Log($"hidden=restored;scheduleId={hidden.Sequence};source={hidden.Source};identity=0x{identity.ToInt64():X};reason={reason}");
            Release(hidden, true);
        }
        for (int i = Active.Count - 1; i >= 0; i--)
        {
            var item = Active[i];
            if (item.Identity != identity) continue;
            // 1.2.16 (second real-machine pass): a despawn of the WRAPPER must not cancel a launch that is still
            // pending. The lease separation is the point: the ragdoll lease and its bodies are not the model
            // lease, and the game pools the agent before its ragdoll coroutine has linked them. A corpse with a
            // usable ragdoll therefore survives the despawn and is closed out normally by EndAt - where
            // CloseLaunch guarantees the receipt.
            // Review R3: only a real `enemy-despawn` may keep the corpse, and keeping it drops EVERY handle that
            // can reach a GameObject (Enemy/EnemyRoot/ModelRoot/model lease). The pool hands the same EnemyAgent
            // wrapper to the next spawn, so BreakLimbs/TryLeaseModel/NativeDecay/FadeRenderer would otherwise
            // write to a LIVING enemy (limbs, renderers, kinematic bodies, Decay.Play).
            if (FeedbackPolicy.ShouldKeepCorpseForPendingLaunch(LeaseState(item), item.LaunchApplications, reason))
            {
                Log($"queue=kept;scheduleId={item.Sequence};source={item.Source};identity=0x{identity.ToInt64():X};reason={reason};launch={LaunchState(item)};bodies={item.Ragdoll.BodyCount};note=pending-launch-survives-despawn;handles=dropped");
                item.Enemy = null;
                item.EnemyRoot = null;
                item.ModelRoot = null;
                item.Model = null;
                item.ModelGeneration = 0;
                continue;
            }
            RemoveQueued(item, reason, Time.unscaledTime);
            Active.RemoveAt(i);
        }
    }
    internal static void EnterLevel()
    {
        DeathLedger.EnterLevel();
        _outsideLevelSkipLogged = false;
    }
    internal static void Reset(string reason)
    {
        float now = Time.unscaledTime;
        Log($"queue=reset;reason={reason};active={Active.Count};hidden={Hidden.Count};dedupe={DeathLedger.Count};inLevel={DeathLedger.InLevel}");
        foreach (var item in Active) RemoveQueued(item, reason, now);
        foreach (var item in Hidden.Values)
        {
            Log($"hidden=restored;scheduleId={item.Sequence};source={item.Source};identity=0x{item.Identity.ToInt64():X};reason={reason}");
            Release(item, false);
        }
        Active.Clear(); Hidden.Clear(); ActiveIdentities.Clear(); GibOwners.Clear(); GibOrigins.Clear(); GibScales.Clear(); DeathLedger.Reset(); PruneKeys.Clear(); _deathPool = null; _fragmentOwner = null; _pruneAt = 0f; _outsideLevelSkipLogged = false;
        ExplosionSound.Reset(); _pendingSoundPosition = Vector3.zero;
        _duplicateSkips.Reset(); _noisyPrunes.Reset(); _gibUnavailableLogged = false;
        CorpseFade.Reset();
    }
    private static void Warn(Exception error) { if (_warnings++ < 6) Plugin.Instance?.Log.LogWarning("[CORPSE] " + error.Message); }
}

[HarmonyPatch(typeof(CD_Gib), "Activate", new Type[] { typeof(Vector3), typeof(Vector3) })]
internal static class GibActivatePatch
{
    [HarmonyPrefix] private static void Prefix(CD_Gib __instance) => KillTracker.GuardAction(() => CorpseFeedback.GibReused(__instance));
    [HarmonyPostfix] private static void Postfix(CD_Gib __instance) => KillTracker.GuardAction(() => CorpseFeedback.CaptureGib(__instance));
}
[HarmonyPatch(typeof(CD_Gib), "Activate", new Type[] { typeof(Vector3) })]
internal static class GibActivateSimplePatch
{
    [HarmonyPrefix] private static void Prefix(CD_Gib __instance) => KillTracker.GuardAction(() => CorpseFeedback.GibReused(__instance));
    [HarmonyPostfix] private static void Postfix(CD_Gib __instance) => KillTracker.GuardAction(() => CorpseFeedback.CaptureGib(__instance));
}

[HarmonyPatch(typeof(CD_Gib), "Deactivate")]
internal static class GibDeactivatePatch
{
    [HarmonyPrefix] private static void Prefix(CD_Gib __instance) => KillTracker.GuardAction(() => CorpseFeedback.GibReused(__instance));
}
