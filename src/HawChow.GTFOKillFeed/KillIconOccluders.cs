using System;
using Enemies;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace HawChow.GTFOKillFeed;

internal static class KillIconOccluders
{
	private sealed class EnemySlot
	{
		internal EnemyAgent Agent;

		internal Collider[] Colliders;
	}

	internal const int MaxCandidates = KillOccluderRecovery.MaxEnemies;

	internal const int MaxCollidersPerEnemy = KillOccluderRecovery.MaxCollidersPerEnemy;

	internal const float ColliderRefreshSeconds = KillOccluderRecovery.ColliderRefreshSeconds;

	private static readonly KillOccluderRecovery Recovery = new KillOccluderRecovery();

	private static readonly KillOccluderRegistry<EnemySlot> Registry = new KillOccluderRegistry<EnemySlot>(Recovery, KillOccluderRecovery.MaxEnemies);

	private static readonly Func<EnemySlot, bool> StillAlive = IsSlotAlive;

	internal static bool Complete => Recovery.Complete;

	internal static int Count => Registry.Count;

	internal static int RescanAttempts => Recovery.RescanAttempts;

	internal static void Reset()
	{
		Registry.Clear();
		Recovery.Clear();
	}

	internal static void EnterLevel()
	{
		Reset();
	}

	internal static void Register(EnemyAgent agent)
	{
		RegisterInternal(agent, SafeNow());
	}

	internal static void Unregister(EnemyAgent agent)
	{
		if (agent == null)
		{
			return;
		}
		Registry.Unregister(agent, SafeKey(agent));
	}

	internal static int Collect(Camera camera, ScreenBox[] into, out bool complete)
	{
		complete = false;
		float now = SafeNow();
		Recovery.BeginFrame();
		if (!Recovery.Seeded)
		{
			Seed(now);
		}
		Registry.PruneDead(StillAlive);
		if (!Recovery.RegistryComplete)
		{
			if (Recovery.RescanDue(now))
			{
				Rebuild(now);
			}
			if (!Recovery.RegistryComplete)
			{
				return 0;
			}
		}
		if (camera == null || into == null || into.Length == 0)
		{
			Recovery.NoteFrameIncomplete();
			return 0;
		}
		int screenWidth = Screen.width;
		int screenHeight = Screen.height;
		if (screenWidth <= 0 || screenHeight <= 0)
		{
			Recovery.NoteFrameIncomplete();
			return 0;
		}
		float nearClip = SafeNearClip(camera);
		int count = 0;
		for (int i = Registry.Count - 1; i >= 0; i--)
		{
			KillOccluderRegistry<EnemySlot>.Entry entry = Registry.At(i);
			EnemySlot slot = entry.Value;
			if (!IsAlive(slot.Agent))
			{
				Registry.RemoveAt(i);
				continue;
			}
			if (!entry.Captured || !KillViewPolicy.IsFinite(now) || now < entry.CapturedAt || now - entry.CapturedAt >= ColliderRefreshSeconds)
			{
				if (!KillOccluderRecovery.MayRetry(entry.Retry, now) || !Capture(slot, ref entry.Retry, now))
				{
					Recovery.NoteFrameIncomplete();
					return 0;
				}
				entry.Captured = true;
				entry.CapturedAt = now;
			}
			if (!TryUnionBounds(slot, out Bounds bounds))
			{
				continue;
			}
			if (!Project(camera, bounds, nearClip, screenWidth, screenHeight, out ScreenBox box))
			{
				continue;
			}
			if (!KillViewPolicy.ShouldConsiderCandidate(isEnemy: true, box.IsUsable))
			{
				continue;
			}
			if (!KillViewPolicy.TryAppendCandidate(into, ref count, isEnemy: true, box))
			{
				Recovery.NoteFrameIncomplete();
				return 0;
			}
		}
		complete = Recovery.Complete;
		return count;
	}

	private static void Seed(float now)
	{
		Recovery.MarkSeeded();
		Registry.Clear();
		try
		{
			EnemyAgent[] found = UnityEngine.Object.FindObjectsOfType<EnemyAgent>();
			if (found == null)
			{
				Recovery.NoteRegistryIncomplete(now);
				return;
			}
			if (found.Length > MaxCandidates)
			{
				Recovery.NoteRegistryIncomplete(now);
			}
			int limit = Math.Min(found.Length, MaxCandidates);
			for (int i = 0; i < limit; i++)
			{
				RegisterInternal(found[i], now);
			}
		}
		catch (System.Exception)
		{
			Recovery.NoteRegistryIncomplete(now);
		}
	}

	// Bounded rebuild: one scene scan, gated by KillOccluderRecovery.RescanDue so it can
	// never run per frame. Succeeds only when the scan saw every live enemy and all of
	// them fit in the bounded registry.
	private static void Rebuild(float now)
	{
		Recovery.NoteRescanAttempt();
		try
		{
			EnemyAgent[] found = UnityEngine.Object.FindObjectsOfType<EnemyAgent>();
			if (found == null || found.Length > MaxCandidates)
			{
				Recovery.NoteRescanFailed(now);
				return;
			}
			bool rejected = false;
			for (int i = 0; i < found.Length; i++)
			{
				if (RegisterInternal(found[i], now) == OccluderRegisterResult.Rejected)
				{
					rejected = true;
				}
			}
			if (rejected)
			{
				Recovery.NoteRescanFailed(now);
				return;
			}
			Recovery.NoteRegistryRebuilt();
		}
		catch (System.Exception)
		{
			Recovery.NoteRescanFailed(now);
		}
	}

	private static OccluderRegisterResult RegisterInternal(EnemyAgent agent, float now)
	{
		if (agent == null)
		{
			return OccluderRegisterResult.Duplicate;
		}
		EnemySlot slot = new EnemySlot();
		slot.Agent = agent;
		return Registry.Register(slot, agent, SafeKey(agent), now);
	}

	// Returns false when the collider set cannot be trusted for this frame; the retry
	// bookkeeping decides whether the next frame tries again or backs off.
	private static bool Capture(EnemySlot slot, ref OccluderRetry retry, float now)
	{
		try
		{
			Il2CppArrayBase<Collider> found = slot.Agent.GetComponentsInChildren<Collider>(true);
			if (found == null)
			{
				slot.Colliders = new Collider[0];
				KillOccluderRecovery.NoteCaptureSuccess(ref retry);
				return true;
			}
			int length = found.Length;
			if (length > MaxCollidersPerEnemy)
			{
				KillOccluderRecovery.NoteCaptureFailure(ref retry, now);
				return false;
			}
			Collider[] copy = new Collider[length];
			for (int i = 0; i < length; i++)
			{
				copy[i] = found[i];
			}
			slot.Colliders = copy;
			KillOccluderRecovery.NoteCaptureSuccess(ref retry);
			return true;
		}
		catch (System.Exception)
		{
			KillOccluderRecovery.NoteCaptureFailure(ref retry, now);
			return false;
		}
	}

	private static bool IsSlotAlive(EnemySlot slot)
	{
		return slot != null && IsAlive(slot.Agent);
	}

	private static bool TryUnionBounds(EnemySlot slot, out Bounds bounds)
	{
		bounds = default(Bounds);
		Collider[] colliders = slot.Colliders;
		if (colliders == null || colliders.Length == 0)
		{
			return false;
		}
		bool has = false;
		for (int i = 0; i < colliders.Length; i++)
		{
			Collider collider = colliders[i];
			if (collider == null || !collider.enabled)
			{
				continue;
			}
			Bounds current;
			try
			{
				if (!collider.gameObject.activeInHierarchy)
				{
					continue;
				}
				current = collider.bounds;
			}
			catch (System.Exception)
			{
				continue;
			}
			Vector3 centre = current.center;
			Vector3 extents = current.extents;
			if (!KillViewPolicy.IsFinite(centre.x) || !KillViewPolicy.IsFinite(centre.y) || !KillViewPolicy.IsFinite(centre.z) || !KillViewPolicy.IsFinite(extents.x) || !KillViewPolicy.IsFinite(extents.y) || !KillViewPolicy.IsFinite(extents.z))
			{
				continue;
			}
			if (!has)
			{
				bounds = current;
				has = true;
			}
			else
			{
				bounds.Encapsulate(current);
			}
		}
		return has;
	}

	private static readonly float[] CornerX = new float[8];

	private static readonly float[] CornerY = new float[8];

	private static readonly float[] CornerZ = new float[8];

	private static bool Project(Camera camera, Bounds bounds, float nearClip, int screenWidth, int screenHeight, out ScreenBox box)
	{
		box = default(ScreenBox);
		Vector3 centre = bounds.center;
		Vector3 extents = bounds.extents;
		FillCorners(centre, extents);
		try
		{
			for (int i = 0; i < 8; i++)
			{
				Vector3 projected = camera.WorldToScreenPoint(new Vector3(CornerX[i], CornerY[i], CornerZ[i]));
				CornerX[i] = projected.x;
				CornerY[i] = (float)screenHeight - projected.y;
				CornerZ[i] = projected.z;
			}
		}
		catch (System.Exception)
		{
			return false;
		}
		return KillViewPolicy.ProjectBox(CornerX, CornerY, CornerZ, 8, nearClip, screenWidth, screenHeight, out box);
	}

	private static void FillCorners(Vector3 centre, Vector3 extents)
	{
		float x0 = centre.x - extents.x;
		float x1 = centre.x + extents.x;
		float y0 = centre.y - extents.y;
		float y1 = centre.y + extents.y;
		float z0 = centre.z - extents.z;
		float z1 = centre.z + extents.z;
		CornerX[0] = x0;
		CornerY[0] = y0;
		CornerZ[0] = z0;
		CornerX[1] = x1;
		CornerY[1] = y0;
		CornerZ[1] = z0;
		CornerX[2] = x0;
		CornerY[2] = y1;
		CornerZ[2] = z0;
		CornerX[3] = x1;
		CornerY[3] = y1;
		CornerZ[3] = z0;
		CornerX[4] = x0;
		CornerY[4] = y0;
		CornerZ[4] = z1;
		CornerX[5] = x1;
		CornerY[5] = y0;
		CornerZ[5] = z1;
		CornerX[6] = x0;
		CornerY[6] = y1;
		CornerZ[6] = z1;
		CornerX[7] = x1;
		CornerY[7] = y1;
		CornerZ[7] = z1;
	}

	private static float SafeNearClip(Camera camera)
	{
		try
		{
			float value = camera.nearClipPlane;
			return (KillViewPolicy.IsFinite(value) && value > 0f) ? value : 0f;
		}
		catch (System.Exception)
		{
			return 0f;
		}
	}

	private static float SafeNow()
	{
		try
		{
			return Time.unscaledTime;
		}
		catch (System.Exception)
		{
			return 0f;
		}
	}

	private static bool IsAlive(EnemyAgent agent)
	{
		if (agent == null)
		{
			return false;
		}
		try
		{
			System.IntPtr pointer = agent.Pointer;
			return pointer != System.IntPtr.Zero;
		}
		catch (System.Exception)
		{
			return false;
		}
	}

	private static System.IntPtr SafeKey(EnemyAgent agent)
	{
		try
		{
			return agent.Pointer;
		}
		catch (System.Exception)
		{
			return System.IntPtr.Zero;
		}
	}
}
