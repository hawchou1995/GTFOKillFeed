using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Enemies;
using GameData;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Player;
using UnityEngine;

namespace HawChow.GTFOKillFeed;

internal static class EnemyFocus
{
	private const int RayBufferSize = 48;

	private const float BlockerTolerance = 0.05f;

	private const float HeadPaddingMetres = 0.12f;

	private const float MaxBoundsAboveHeadMetres = 2f;

	private const float MaxBoundsAboveRootMetres = 6f;

	private const float MaxBoundsBelowAnchorMetres = 0.6f;

	private const float FallbackBodyHeightMetres = 1.6f;

	private static readonly Il2CppStructArray<RaycastHit> Hits = new Il2CppStructArray<RaycastHit>(new RaycastHit[48]);

	private static Transform _playerRoot;

	private static readonly HeadBoundsCache Bounds = new HeadBoundsCache();

	internal static EnemyAgent Target
	{
		get;
		private set;
	}

	internal static float Distance
	{
		get;
		private set;
	}

	internal static string Reason
	{
		get;
		private set;
	} = "idle";


	internal static int MaskValue
	{
		get;
		private set;
	}

	internal static int HitsSeen
	{
		get;
		private set;
	}

	internal static int EnemyHits
	{
		get;
		private set;
	}

	internal static bool GlueGunWielded
	{
		get;
		private set;
	}

	internal static Vector3 HitPoint
	{
		get;
		private set;
	}

	internal static bool HitIsWeakspot
	{
		get;
		private set;
	}

	internal static float BackDot
	{
		get;
		private set;
	} = 0f / 0f;


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
		BackDot = 0f / 0f;
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
		BackDot = 0f / 0f;
		float maxDistance = BarPolicy.MaxDistance(configuredMaxDistance);
		try
		{
			if (!PlayerManager.HasLocalPlayerAgent())
			{
				Reason = "no-local-player";
				return;
			}
			PlayerAgent localPlayerAgent = PlayerManager.GetLocalPlayerAgent();
			if (localPlayerAgent == null)
			{
				Reason = "no-local-player";
				return;
			}
			if (!localPlayerAgent.IsLocallyOwned)
			{
				Reason = "not-local-owner";
				return;
			}
			try
			{
				GlueGunWielded = GlueGunWield.IsHeld(localPlayerAgent.FPItemHolder);
			}
			catch
			{
				GlueGunWielded = false;
			}
			FPSCamera fPSCamera = localPlayerAgent.FPSCamera;
			if ((object)(object)fPSCamera == null)
			{
				Reason = "no-camera";
				return;
			}
			Vector3 origin = fPSCamera.CameraRayPos;
			if (!KillPolicy.IsFinite(origin.x) || !KillPolicy.IsFinite(origin.y) || !KillPolicy.IsFinite(origin.z) || origin.sqrMagnitude < 0.0001f)
			{
				origin = fPSCamera.Position;
			}
			Vector3 vector = fPSCamera.CameraRayDir;
			if (!KillPolicy.IsFinite(vector.x) || !KillPolicy.IsFinite(vector.y) || !KillPolicy.IsFinite(vector.z) || vector.sqrMagnitude < 0.0001f)
			{
				vector = fPSCamera.Forward;
			}
			if (!KillPolicy.IsFinite(origin.x) || !KillPolicy.IsFinite(origin.y) || !KillPolicy.IsFinite(origin.z) || !KillPolicy.IsFinite(vector.x) || !KillPolicy.IsFinite(vector.y) || !KillPolicy.IsFinite(vector.z) || vector.sqrMagnitude < 0.0001f)
			{
				Reason = "no-ray";
				return;
			}
			vector = vector.normalized;
			_playerRoot = localPlayerAgent.transform;
			Scan(mask: MaskValue = LayerManager.MASK_BULLETWEAPON_RAY | LayerManager.MASK_CAMERA_RAY | LayerManager.MASK_ENEMY | LayerManager.MASK_ENEMY_DAMAGABLE, origin: origin, direction: vector, maxDistance: maxDistance, nearest: out var nearest, nearestDistance: out var nearestDistance, blocker: out var blocker, nearestPoint: out var nearestPoint, nearestCollider: out var nearestCollider);
			if (nearest == null)
			{
				float num2 = blocker;
				int mask2 = LayerBit(LayerManager.LAYER_ENEMY) | LayerBit(LayerManager.LAYER_ENEMY_DAMAGABLE) | LayerManager.MASK_ENEMY | LayerManager.MASK_ENEMY_DAMAGABLE;
				Scan(origin, vector, maxDistance, mask2, out var nearest2, out var nearestDistance2, out var _, out var nearestPoint2, out var nearestCollider2);
				if (nearest2 != null && nearestDistance2 <= num2 + 0.05f)
				{
					nearest = nearest2;
					nearestDistance = nearestDistance2;
					blocker = num2;
					nearestPoint = nearestPoint2;
					nearestCollider = nearestCollider2;
				}
			}
			if (nearest == null)
			{
				Reason = "no-enemy";
				return;
			}
			if (nearestDistance > blocker + 0.05f)
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
		catch (System.Exception ex)
		{
			Target = null;
			Distance = 0f;
			Reason = "error:" + ((MemberInfo)ex.GetType()).Name;
		}
	}

	private static void Scan(Vector3 origin, Vector3 direction, float maxDistance, int mask, out EnemyAgent nearest, out float nearestDistance, out float blocker, out Vector3 nearestPoint, out Collider nearestCollider)
	{
		nearest = null;
		nearestDistance = 1f / 0f;
		blocker = 1f / 0f;
		nearestPoint = Vector3.zero;
		nearestCollider = null;
		int num = Physics.RaycastNonAlloc(origin, direction, Hits, maxDistance, mask, QueryTriggerInteraction.Ignore);
		if (num <= 0)
		{
			return;
		}
		HitsSeen += num;
		int num2 = ((num < Hits.Length) ? num : Hits.Length);
		for (int i = 0; i < num2; i++)
		{
			RaycastHit raycastHit = Hits[i];
			Collider collider = raycastHit.collider;
			if (collider == null)
			{
				continue;
			}
			Transform transform = collider.transform;
			if (transform == null || IsSelf(transform))
			{
				continue;
			}
			float distance = raycastHit.distance;
			if (!KillPolicy.IsFinite(distance) || distance < 0f)
			{
				continue;
			}
			if (distance < blocker)
			{
				blocker = distance;
			}
			EnemyAgent enemyAgent = Resolve(collider);
			if (!(enemyAgent == null))
			{
				EnemyHits++;
				if (distance < nearestDistance)
				{
					nearestDistance = distance;
					nearest = enemyAgent;
					nearestPoint = raycastHit.point;
					nearestCollider = collider;
				}
			}
		}
	}

	private static void CaptureHitGeometry(EnemyAgent enemy, Vector3 point, Collider collider)
	{
		HitPoint = point;
		bool hitIsWeakspot = false;
		try
		{
			Dam_EnemyDamageLimb dam_EnemyDamageLimb = ((collider != null) ? collider.GetComponentInParent<Dam_EnemyDamageLimb>() : null);
			if (dam_EnemyDamageLimb != null)
			{
				hitIsWeakspot = dam_EnemyDamageLimb.m_type == eLimbDamageType.Weakspot;
			}
		}
		catch (System.Exception)
		{
			hitIsWeakspot = false;
		}
		HitIsWeakspot = hitIsWeakspot;
		float backDot = 0f / 0f;
		try
		{
			if (enemy != null)
			{
				Vector3 forward = enemy.transform.forward;
				Vector3 vector = point - enemy.transform.position;
				backDot = FocusOutlinePolicy.DirectionDot(forward.x, forward.y, forward.z, vector.x, vector.y, vector.z);
			}
		}
		catch (System.Exception)
		{
			backDot = 0f / 0f;
		}
		BackDot = backDot;
	}

	private static EnemyAgent Resolve(Collider collider)
	{
		try
		{
			return collider.GetComponentInParent<EnemyAgent>();
		}
		catch
		{
			return null;
		}
	}

	private static bool IsAlive(EnemyAgent enemy)
	{
		try
		{
			if (enemy == null || !enemy.m_alive)
			{
				return false;
			}
			Dam_EnemyDamageBase damage = enemy.Damage;
			if (damage == null)
			{
				return false;
			}
			float health = damage.Health;
			return KillPolicy.IsFinite(health) && health > 0f;
		}
		catch
		{
			return false;
		}
	}

	private static bool IsSelf(Transform candidate)
	{
		if (_playerRoot == null || candidate == null)
		{
			return false;
		}
		try
		{
			return candidate == _playerRoot || candidate.IsChildOf(_playerRoot);
		}
		catch
		{
			return false;
		}
	}

	private static int LayerBit(int layer)
	{
		if (layer < 0 || layer >= 32)
		{
			return 0;
		}
		return 1 << layer;
	}

	internal static string RawNameOf(EnemyAgent enemy)
	{
		if (enemy == null)
		{
			return "Unknown";
		}
		try
		{
			EnemyDataBlock block = GameDataBlockBase<EnemyDataBlock>.GetBlock(enemy.EnemyDataID);
			if (block != null && !string.IsNullOrWhiteSpace(block.name))
			{
				return block.name;
			}
		}
		catch
		{
		}
		try
		{
			return enemy.name;
		}
		catch
		{
			return "Unknown";
		}
	}

	internal static string ResolveWeakspotParts(EnemyAgent enemy, string language)
	{
		if (enemy == null)
		{
			return string.Empty;
		}
		try
		{
			List<WeakspotPartMultiplier> val = new List<WeakspotPartMultiplier>();
			HashSet<System.IntPtr> val2 = new HashSet<System.IntPtr>();
			Dam_EnemyDamageLimb dam_EnemyDamageLimb = null;
			try
			{
				dam_EnemyDamageLimb = enemy.m_headLimb;
			}
			catch
			{
			}
			Dam_EnemyDamageBase damage = enemy.Damage;
			Il2CppReferenceArray<Dam_EnemyDamageLimb> il2CppReferenceArray = ((damage != null) ? damage.DamageLimbs : null);
			bool flag = false;
			if (il2CppReferenceArray != null)
			{
				for (int i = 0; i < il2CppReferenceArray.Length; i++)
				{
					Dam_EnemyDamageLimb dam_EnemyDamageLimb2 = il2CppReferenceArray[i];
					if (dam_EnemyDamageLimb2 == null || dam_EnemyDamageLimb2.m_type != eLimbDamageType.Weakspot)
					{
						continue;
					}
					System.IntPtr intPtr = SafeLimbPointer(dam_EnemyDamageLimb2);
					if (intPtr != System.IntPtr.Zero && !val2.Add(intPtr))
					{
						continue;
					}
					int num;
					if (dam_EnemyDamageLimb != null)
					{
						num = (SameLimb(dam_EnemyDamageLimb2, dam_EnemyDamageLimb) ? 1 : 0);
						if (num != 0)
						{
							flag = true;
						}
					}
					else
					{
						num = 0;
					}
					string part = ((num != 0) ? BarPolicy.HeadPartLabel(language) : BarPolicy.PartLabel(WeakspotDebugName(dam_EnemyDamageLimb2), language));
					float multiplier;
					try
					{
						multiplier = dam_EnemyDamageLimb2.m_weakspotDamageMulti;
					}
					catch
					{
						multiplier = 0f / 0f;
					}
					val.Add(new WeakspotPartMultiplier(part, multiplier));
				}
			}
			if (!flag && dam_EnemyDamageLimb != null && dam_EnemyDamageLimb.m_type == eLimbDamageType.Weakspot)
			{
				float multiplier2;
				try
				{
					multiplier2 = dam_EnemyDamageLimb.m_weakspotDamageMulti;
				}
				catch
				{
					multiplier2 = 0f / 0f;
				}
				val.Add(new WeakspotPartMultiplier(BarPolicy.HeadPartLabel(language), multiplier2));
			}
			return BarPolicy.WeakspotTags((System.Collections.Generic.IEnumerable<WeakspotPartMultiplier>)val, language);
		}
		catch
		{
			return string.Empty;
		}
	}

	internal static float GlueToleranceOf(EnemyAgent enemy)
	{
		if (enemy == null)
		{
			return 0f / 0f;
		}
		try
		{
			return enemy.EnemyBalancingData?.GlueTolerance ?? (0f / 0f);
		}
		catch
		{
			return 0f / 0f;
		}
	}

	private static string WeakspotDebugName(Dam_EnemyDamageLimb limb)
	{
		try
		{
			if (!string.IsNullOrWhiteSpace(limb.DebugName))
			{
				return limb.DebugName;
			}
		}
		catch
		{
		}
		try
		{
			return (limb.transform != null) ? limb.transform.name : string.Empty;
		}
		catch
		{
			return string.Empty;
		}
	}

	private static System.IntPtr SafeLimbPointer(Dam_EnemyDamageLimb limb)
	{
		try
		{
			return (limb != null) ? limb.Pointer : System.IntPtr.Zero;
		}
		catch
		{
			return System.IntPtr.Zero;
		}
	}

	private static bool SameLimb(Dam_EnemyDamageLimb left, Dam_EnemyDamageLimb right)
	{
		if ((object)left == right)
		{
			return true;
		}
		System.IntPtr intPtr = SafeLimbPointer(left);
		System.IntPtr intPtr2 = SafeLimbPointer(right);
		if (intPtr != System.IntPtr.Zero)
		{
			return intPtr == intPtr2;
		}
		return false;
	}

	internal static bool TryGetHeadWorld(EnemyAgent enemy, float headOffsetMetres, out Vector3 world)
	{
		world = Vector3.zero;
		if (enemy == null)
		{
			return false;
		}
		try
		{
			Dam_EnemyDamageLimb headLimb = enemy.m_headLimb;
			Vector3 vector;
			bool flag;
			if (headLimb != null)
			{
				Transform transform = headLimb.transform;
				if (transform == null)
				{
					return false;
				}
				vector = transform.position;
				flag = true;
			}
			else
			{
				Transform mainModelTransform = enemy.MainModelTransform;
				vector = ((mainModelTransform != null) ? mainModelTransform.position : enemy.transform.position);
				flag = false;
			}
			if (!KillPolicy.IsFinite(vector.x) || !KillPolicy.IsFinite(vector.y) || !KillPolicy.IsFinite(vector.z))
			{
				return false;
			}
			float num = (flag ? vector.y : (vector.y + 1.6f));
			float num2 = TopY(enemy);
			float num3 = vector.y - 0.6f;
			float num4 = vector.y + (flag ? 2f : 6f);
			if (num2 >= num3 && num2 <= num4 && num2 > num)
			{
				num = num2;
			}
			world = new Vector3(vector.x, num + 0.12f + headOffsetMetres, vector.z);
			return KillPolicy.IsFinite(world.x) && KillPolicy.IsFinite(world.y) && KillPolicy.IsFinite(world.z);
		}
		catch
		{
			return false;
		}
	}

	private static float TopY(EnemyAgent enemy)
	{
		int id;
		try
		{
			id = enemy.GetInstanceID();
		}
		catch
		{
			id = -2147483648;
		}
		return Bounds.Top(id, Time.unscaledTime, () => MeasureTopY(enemy));
	}

	private static float MeasureTopY(EnemyAgent enemy)
	{
		float num = -1f / 0f;
		try
		{
			Il2CppArrayBase<Renderer> componentsInChildren = enemy.GetComponentsInChildren<Renderer>(includeInactive: true);
			if (componentsInChildren != null)
			{
				for (int i = 0; i < componentsInChildren.Length; i++)
				{
					Renderer renderer = componentsInChildren[i];
					if (!(renderer == null) && renderer.enabled)
					{
						float y = renderer.bounds.max.y;
						if (KillPolicy.IsFinite(y) && y > num)
						{
							num = y;
						}
					}
				}
			}
		}
		catch
		{
			num = -1f / 0f;
		}
		if (num != -1f / 0f)
		{
			return num;
		}
		return 0f;
	}
}
