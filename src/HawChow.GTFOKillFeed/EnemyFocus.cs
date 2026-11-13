using System;
using System.Collections.Generic;
using Enemies;
using GameData;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Player;
using UnityEngine;

namespace HawChow.GTFOKillFeed;

// Resolves the one enemy the crosshair is actually pointing at, and the world point above its head.
// Everything here is read-only: no damage calls, no network packets, no Harmony patches.
internal static class EnemyFocus
{
    private const int RayBufferSize = 48;
    private const float BlockerTolerance = 0.05f;
    private const float HeadPaddingMetres = 0.12f;
    // Renderer bounds in GTFO can be inflated by clothing and effect renderers to several metres,
    // sometimes tens of metres, so they are only trusted inside this window around the anchor.
    private const float MaxBoundsAboveHeadMetres = 2f;
    private const float MaxBoundsAboveRootMetres = 6f;
    private const float MaxBoundsBelowAnchorMetres = 0.6f;
    private const float FallbackBodyHeightMetres = 1.6f;

    private static readonly Il2CppStructArray<RaycastHit> Hits = new(new RaycastHit[RayBufferSize]);
    private static Transform _playerRoot;
    // Bounded per-target cache (review F4): several head consumers run every frame (up to twelve head
    // icons, the focus bar, the downed teammate marks), so a single slot re-enumerated every Renderer
    // whenever two targets alternated. HeadBoundsCache keeps one sample per target for its window.
    private static readonly HeadBoundsCache Bounds = new();

    internal static EnemyAgent Target { get; private set; }
    internal static float Distance { get; private set; }
    internal static string Reason { get; private set; } = "idle";
    internal static int MaskValue { get; private set; }
    internal static int HitsSeen { get; private set; }
    internal static int EnemyHits { get; private set; }
    internal static bool GlueGunWielded { get; private set; }
    // Where the crosshair ray actually landed on the focused enemy, and what that collider belongs to.
    // The bar's focus outline is decided from these three values only.
    internal static Vector3 HitPoint { get; private set; }
    internal static bool HitIsWeakspot { get; private set; }
    internal static float BackDot { get; private set; } = float.NaN;
    internal static void Reset()
    {
        Target = null;
        GlueGunWielded = false;
        Distance = 0f;
        Reason = "idle";
        HitsSeen = 0;
        EnemyHits = 0;
        Bounds.Clear();
        HitPoint = Vector3.zero;
        HitIsWeakspot = false;
        BackDot = float.NaN;
    }

    internal static void Update(float configuredMaxDistance)
    {
        GlueGunWielded = false;
        Target = null;
        Distance = 0f;
        HitsSeen = 0;
        EnemyHits = 0;
        HitPoint = Vector3.zero;
        HitIsWeakspot = false;
        BackDot = float.NaN;
        float maxDistance = BarPolicy.MaxDistance(configuredMaxDistance);
        try
        {
            if (!PlayerManager.HasLocalPlayerAgent())
            {
                Reason = "no-local-player";
                return;
            }
            PlayerAgent player = PlayerManager.GetLocalPlayerAgent();
            if (player == null)
            {
                Reason = "no-local-player";
                return;
            }
            if (!player.IsLocallyOwned)
            {
                Reason = "not-local-owner";
                return;
            }
            try
            {
                // WieldedItem is typed ItemEquippable, so the shared probe must compare native classes.
                GlueGunWielded = GlueGunWield.IsHeld(player.FPItemHolder);
            }
            catch { GlueGunWielded = false; }
            FPSCamera fpsCamera = player.FPSCamera;
            if (fpsCamera == null)
            {
                Reason = "no-camera";
                return;
            }
            Vector3 origin = fpsCamera.CameraRayPos;
            if (!KillPolicy.IsFinite(origin.x) || !KillPolicy.IsFinite(origin.y) || !KillPolicy.IsFinite(origin.z) || origin.sqrMagnitude < 0.0001f)
                origin = fpsCamera.Position;
            Vector3 direction = fpsCamera.CameraRayDir;
            if (!KillPolicy.IsFinite(direction.x) || !KillPolicy.IsFinite(direction.y) || !KillPolicy.IsFinite(direction.z) || direction.sqrMagnitude < 0.0001f)
                direction = fpsCamera.Forward;
            if (!KillPolicy.IsFinite(origin.x) || !KillPolicy.IsFinite(origin.y) || !KillPolicy.IsFinite(origin.z) ||
                !KillPolicy.IsFinite(direction.x) || !KillPolicy.IsFinite(direction.y) || !KillPolicy.IsFinite(direction.z) ||
                direction.sqrMagnitude < 0.0001f)
            {
                Reason = "no-ray";
                return;
            }
            direction = direction.normalized;
            _playerRoot = player.transform;

            int mask = LayerManager.MASK_BULLETWEAPON_RAY
                | LayerManager.MASK_CAMERA_RAY
                | LayerManager.MASK_ENEMY
                | LayerManager.MASK_ENEMY_DAMAGABLE;
            MaskValue = mask;

            Scan(origin, direction, maxDistance, mask, out EnemyAgent nearest, out float nearestDistance, out float blocker, out Vector3 nearestPoint, out Collider nearestCollider);
            if (nearest == null)
            {
                // No enemy collider answered the curated masks, so probe the enemy layers directly.
                // The curated pass still supplies the blocker distance, so the fallback can never
                // reveal an enemy that is actually behind geometry.
                float curatedBlocker = blocker;
                int enemyMask = LayerBit(LayerManager.LAYER_ENEMY)
                    | LayerBit(LayerManager.LAYER_ENEMY_DAMAGABLE)
                    | LayerManager.MASK_ENEMY
                    | LayerManager.MASK_ENEMY_DAMAGABLE;
                Scan(origin, direction, maxDistance, enemyMask, out EnemyAgent fallback, out float fallbackDistance, out _, out Vector3 fallbackPoint, out Collider fallbackCollider);
                if (fallback != null && fallbackDistance <= curatedBlocker + BlockerTolerance)
                {
                    nearest = fallback;
                    nearestDistance = fallbackDistance;
                    blocker = curatedBlocker;
                    nearestPoint = fallbackPoint;
                    nearestCollider = fallbackCollider;
                }
            }

            if (nearest == null)
            {
                Reason = "no-enemy";
                return;
            }
            if (nearestDistance > blocker + BlockerTolerance)
            {
                Reason = "blocked";
                return;
            }
            if (!IsAlive(nearest))
            {
                Reason = "target-not-alive";
                return;
            }
            Target = nearest;
            CaptureHitGeometry(nearest, nearestPoint, nearestCollider);
            Distance = nearestDistance;
            Reason = "enemy";
        }
        catch (Exception error)
        {
            Target = null;
            Distance = 0f;
            Reason = "error:" + error.GetType().Name;
        }
    }

    private static void Scan(Vector3 origin, Vector3 direction, float maxDistance, int mask,
        out EnemyAgent nearest, out float nearestDistance, out float blocker,
        out Vector3 nearestPoint, out Collider nearestCollider)
    {
        nearest = null;
        nearestDistance = float.PositiveInfinity;
        blocker = float.PositiveInfinity;
        nearestPoint = Vector3.zero;
        nearestCollider = null;
        int count = Physics.RaycastNonAlloc(origin, direction, Hits, maxDistance, mask, QueryTriggerInteraction.Ignore);
        if (count <= 0) return;
        HitsSeen += count;
        int bounded = count < Hits.Length ? count : Hits.Length;
        for (int i = 0; i < bounded; i++)
        {
            RaycastHit hit = Hits[i];
            Collider collider = hit.collider;
            if (collider == null) continue;
            Transform hitTransform = collider.transform;
            if (hitTransform == null || IsSelf(hitTransform)) continue;
            float distance = hit.distance;
            if (!KillPolicy.IsFinite(distance) || distance < 0f) continue;
            if (distance < blocker) blocker = distance;
            EnemyAgent enemy = Resolve(collider);
            if (enemy == null) continue;
            EnemyHits++;
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = enemy;
                nearestPoint = hit.point;
                nearestCollider = collider;
            }
        }
    }

    // Records the accepted hit for the focus outline. The weakspot test reads the limb the collider
    // belongs to; the back test is the dot of the enemy's forward axis against the direction from the
    // enemy to the hit point. Any failure leaves BackDot NaN, and the policy treats NaN as "not back",
    // so a broken transform can never paint a misleading orange outline.
    private static void CaptureHitGeometry(EnemyAgent enemy, Vector3 point, Collider collider)
    {
        HitPoint = point;
        bool weakspot = false;
        try
        {
            Dam_EnemyDamageLimb limb = collider != null ? collider.GetComponentInParent<Dam_EnemyDamageLimb>() : null;
            if (limb != null) weakspot = limb.m_type == eLimbDamageType.Weakspot;
        }
        catch (Exception) { weakspot = false; }
        HitIsWeakspot = weakspot;
        float dot = float.NaN;
        try
        {
            if (enemy != null)
            {
                Vector3 forward = enemy.transform.forward;
                Vector3 toHit = point - enemy.transform.position;
                dot = FocusOutlinePolicy.DirectionDot(forward.x, forward.y, forward.z, toHit.x, toHit.y, toHit.z);
            }
        }
        catch (Exception) { dot = float.NaN; }
        BackDot = dot;
    }

    private static EnemyAgent Resolve(Collider collider)
    {
        try { return collider.GetComponentInParent<EnemyAgent>(); }
        catch { return null; }
    }

    private static bool IsAlive(EnemyAgent enemy)
    {
        try
        {
            if (enemy == null || !enemy.m_alive) return false;
            Dam_EnemyDamageBase damage = enemy.Damage;
            if (damage == null) return false;
            float health = damage.Health;
            return KillPolicy.IsFinite(health) && health > 0f;
        }
        catch { return false; }
    }

    private static bool IsSelf(Transform candidate)
    {
        if (_playerRoot == null || candidate == null) return false;
        try { return candidate == _playerRoot || candidate.IsChildOf(_playerRoot); }
        catch { return false; }
    }

    private static int LayerBit(int layer) => layer >= 0 && layer < 32 ? 1 << layer : 0;

    // The game's own data-block name, matching what KillTracker already writes into kill cards.
    internal static string RawNameOf(EnemyAgent enemy)
    {
        if (enemy == null) return "Unknown";
        try
        {
            EnemyDataBlock block = GameDataBlockBase<EnemyDataBlock>.GetBlock(enemy.EnemyDataID);
            if (block != null && !string.IsNullOrWhiteSpace(block.name)) return block.name;
        }
        catch { }
        try { return enemy.name; } catch { return "Unknown"; }
    }

    // Reports the enemy's configured weakspot limb, independent of the crosshair's current hit point.
    // Reads every configured weakspot's current runtime multiplier; this includes live mod adjustments.
    internal static string ResolveWeakspotParts(EnemyAgent enemy, string language)
    {
        if (enemy == null) return string.Empty;
        try
        {
            var labels = new List<WeakspotPartMultiplier>();
            var seen = new HashSet<IntPtr>();
            Dam_EnemyDamageLimb head = null;
            try { head = enemy.m_headLimb; } catch { }
            Dam_EnemyDamageBase damage = enemy.Damage;
            var limbs = damage != null ? damage.DamageLimbs : null;
            bool headIncluded = false;
            if (limbs != null)
            {
                for (int i = 0; i < limbs.Length; i++)
                {
                    Dam_EnemyDamageLimb limb = limbs[i];
                    if (limb == null || limb.m_type != eLimbDamageType.Weakspot) continue;
                    IntPtr pointer = SafeLimbPointer(limb);
                    if (pointer != IntPtr.Zero && !seen.Add(pointer)) continue;
                    bool isHead = head != null && SameLimb(limb, head);
                    if (isHead) headIncluded = true;
                    string label = isHead ? BarPolicy.HeadPartLabel(language) : BarPolicy.PartLabel(WeakspotDebugName(limb), language);
                    float multiplier;
                    try { multiplier = limb.m_weakspotDamageMulti; } catch { multiplier = float.NaN; }
                    labels.Add(new WeakspotPartMultiplier(label, multiplier));
                }
            }
            if (!headIncluded && head != null && head.m_type == eLimbDamageType.Weakspot)
            {
                float multiplier;
                try { multiplier = head.m_weakspotDamageMulti; } catch { multiplier = float.NaN; }
                labels.Add(new WeakspotPartMultiplier(BarPolicy.HeadPartLabel(language), multiplier));
            }
            return BarPolicy.WeakspotTags(labels, language);
        }
        catch { return string.Empty; }
    }
    internal static float GlueToleranceOf(EnemyAgent enemy)
    {
        if (enemy == null) return float.NaN;
        try
        {
            GameData.EnemyBalancingDataBlock balancing = enemy.EnemyBalancingData;
            return balancing != null ? balancing.GlueTolerance : float.NaN;
        }
        catch { return float.NaN; }
    }

    private static string WeakspotDebugName(Dam_EnemyDamageLimb limb)
    {
        try { if (!string.IsNullOrWhiteSpace(limb.DebugName)) return limb.DebugName; } catch { }
        try { return limb.transform != null ? limb.transform.name : string.Empty; } catch { return string.Empty; }
    }

    private static IntPtr SafeLimbPointer(Dam_EnemyDamageLimb limb)
    {
        try { return limb != null ? limb.Pointer : IntPtr.Zero; } catch { return IntPtr.Zero; }
    }

    private static bool SameLimb(Dam_EnemyDamageLimb left, Dam_EnemyDamageLimb right)
    {
        if (ReferenceEquals(left, right)) return true;
        IntPtr leftPointer = SafeLimbPointer(left), rightPointer = SafeLimbPointer(right);
        return leftPointer != IntPtr.Zero && leftPointer == rightPointer;
    }

    // World-space top of the focused enemy. The head limb gives the reliable skull position;
    // renderer bounds are only trusted inside a small window around it.
    internal static bool TryGetHeadWorld(EnemyAgent enemy, float headOffsetMetres, out Vector3 world)
    {
        world = Vector3.zero;
        if (enemy == null) return false;
        try
        {
            Vector3 anchor;
            bool anchoredOnHead;
            Dam_EnemyDamageLimb head = enemy.m_headLimb;
            if (head != null)
            {
                Transform headTransform = head.transform;
                if (headTransform == null) return false;
                anchor = headTransform.position;
                anchoredOnHead = true;
            }
            else
            {
                Transform model = enemy.MainModelTransform;
                anchor = model != null ? model.position : enemy.transform.position;
                anchoredOnHead = false;
            }
            if (!KillPolicy.IsFinite(anchor.x) || !KillPolicy.IsFinite(anchor.y) || !KillPolicy.IsFinite(anchor.z)) return false;

            float baseY = anchoredOnHead ? anchor.y : anchor.y + FallbackBodyHeightMetres;
            float top = TopY(enemy);
            float lower = anchor.y - MaxBoundsBelowAnchorMetres;
            float upper = anchor.y + (anchoredOnHead ? MaxBoundsAboveHeadMetres : MaxBoundsAboveRootMetres);
            if (top >= lower && top <= upper && top > baseY) baseY = top;

            world = new Vector3(anchor.x, baseY + HeadPaddingMetres + headOffsetMetres, anchor.z);
            return KillPolicy.IsFinite(world.x) && KillPolicy.IsFinite(world.y) && KillPolicy.IsFinite(world.z);
        }
        catch { return false; }
    }

    // One measurement per target per refresh window (review F4). The cache itself is pure and bounded;
    // this method only supplies the measurement, and a thrown/empty measurement is cached as 0 exactly
    // like before, so an enemy without an enabled renderer never re-enumerates every frame.
    private static float TopY(EnemyAgent enemy)
    {
        int id;
        try { id = enemy.GetInstanceID(); } catch { id = int.MinValue; }
        return Bounds.Top(id, Time.unscaledTime, () => MeasureTopY(enemy));
    }

    private static float MeasureTopY(EnemyAgent enemy)
    {
        float top = float.NegativeInfinity;
        try
        {
            Il2CppArrayBase<Renderer> renderers = enemy.GetComponentsInChildren<Renderer>(true);
            if (renderers != null)
            {
                for (int i = 0; i < renderers.Length; i++)
                {
                    Renderer renderer = renderers[i];
                    if (renderer == null || !renderer.enabled) continue;
                    float maxY = renderer.bounds.max.y;
                    if (KillPolicy.IsFinite(maxY) && maxY > top) top = maxY;
                }
            }
        }
        catch { top = float.NegativeInfinity; }
        // An enemy with no enabled renderer (spawning, pooled, or invisible) caches the miss as 0.
        return top == float.NegativeInfinity ? 0f : top;
    }
}
