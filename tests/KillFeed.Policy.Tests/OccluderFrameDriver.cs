using System;
using System.Collections.Generic;

namespace HawChow.GTFOKillFeed.Tests;

// Drives the production recovery state (KillOccluderRecovery), the production bounded
// registry (KillOccluderRegistry<T>) and the production candidate policy in the same
// order as KillIconOccluders.Collect. Unity is not available here, so camera projection
// and collider bounds are replaced by one fixed screen box per enemy and the scene scan
// is replaced by a list; every recovery decision under test is the shipped code.
internal sealed class OccluderFrameDriver
{
	internal sealed class FakeEnemy
	{
		internal readonly int Id;

		internal bool Alive = true;

		internal int Colliders = 2;

		internal bool CaptureThrows;

		internal FakeEnemy(int id)
		{
			Id = id;
		}
	}

	private static readonly Func<FakeEnemy, bool> AlivePredicate = (FakeEnemy enemy) => enemy.Alive;

	private readonly KillOccluderRecovery _recovery = new KillOccluderRecovery();

	private readonly KillOccluderRegistry<FakeEnemy> _registry;

	private readonly List<FakeEnemy> _scene = new List<FakeEnemy>();

	private readonly ScreenBox _box = new ScreenBox(100f, 100f, 146f, 146f);

	internal float Now;

	internal int CaptureAttempts;

	internal int Scans;

	internal int FailScans;

	internal OccluderFrameDriver()
	{
		_registry = new KillOccluderRegistry<FakeEnemy>(_recovery, KillOccluderRecovery.MaxEnemies);
	}

	internal KillOccluderRecovery Recovery => _recovery;

	internal KillOccluderRegistry<FakeEnemy> Registry => _registry;

	internal FakeEnemy Spawn()
	{
		FakeEnemy enemy = new FakeEnemy(_scene.Count + 1);
		while (FindById(enemy.Id) != null)
		{
			enemy = new FakeEnemy(enemy.Id + 1);
		}
		_scene.Add(enemy);
		_registry.Register(enemy, enemy, new IntPtr(enemy.Id), Now);
		return enemy;
	}

	internal void Despawn(FakeEnemy enemy)
	{
		enemy.Alive = false;
		_scene.Remove(enemy);
		_registry.Unregister(enemy, new IntPtr(enemy.Id));
	}

	internal void Reset()
	{
		_registry.Clear();
		_recovery.Clear();
		Now = 0f;
		CaptureAttempts = 0;
		Scans = 0;
		FailScans = 0;
	}

	// Returns what KillIconHud would treat as "candidate set complete"; false hides every
	// head icon for that frame.
	internal bool Step(float deltaSeconds, int bufferSize, out int count)
	{
		Now += deltaSeconds;
		Recovery.BeginFrame();
		if (!Recovery.Seeded)
		{
			Seed();
		}
		Registry.PruneDead(AlivePredicate);
		if (!Recovery.RegistryComplete)
		{
			if (Recovery.RescanDue(Now))
			{
				Rebuild();
			}
			if (!Recovery.RegistryComplete)
			{
				count = 0;
				return false;
			}
		}
		ScreenBox[] into = new ScreenBox[bufferSize];
		count = 0;
		for (int i = Registry.Count - 1; i >= 0; i--)
		{
			KillOccluderRegistry<FakeEnemy>.Entry entry = Registry.At(i);
			FakeEnemy enemy = entry.Value;
			if (!enemy.Alive)
			{
				Registry.RemoveAt(i);
				continue;
			}
			if (!entry.Captured || Now < entry.CapturedAt || Now - entry.CapturedAt >= KillOccluderRecovery.ColliderRefreshSeconds)
			{
				if (!KillOccluderRecovery.MayRetry(entry.Retry, Now) || !Capture(enemy, entry))
				{
					Recovery.NoteFrameIncomplete();
					count = 0;
					return false;
				}
				entry.Captured = true;
				entry.CapturedAt = Now;
			}
			if (!KillViewPolicy.TryAppendCandidate(into, ref count, isEnemy: true, _box))
			{
				Recovery.NoteFrameIncomplete();
				count = 0;
				return false;
			}
		}
		return Recovery.Complete;
	}

	private bool Capture(FakeEnemy enemy, KillOccluderRegistry<FakeEnemy>.Entry entry)
	{
		CaptureAttempts++;
		if (enemy.CaptureThrows || enemy.Colliders > KillOccluderRecovery.MaxCollidersPerEnemy)
		{
			KillOccluderRecovery.NoteCaptureFailure(ref entry.Retry, Now);
			return false;
		}
		KillOccluderRecovery.NoteCaptureSuccess(ref entry.Retry);
		return true;
	}

	private void Seed()
	{
		Recovery.MarkSeeded();
		Registry.Clear();
		Scans++;
		if (FailScans > 0)
		{
			FailScans--;
			Recovery.NoteRegistryIncomplete(Now);
			return;
		}
		if (_scene.Count > KillOccluderRecovery.MaxEnemies)
		{
			Recovery.NoteRegistryIncomplete(Now);
		}
		int limit = Math.Min(_scene.Count, KillOccluderRecovery.MaxEnemies);
		for (int i = 0; i < limit; i++)
		{
			Registry.Register(_scene[i], _scene[i], new IntPtr(_scene[i].Id), Now);
		}
	}

	private void Rebuild()
	{
		Recovery.NoteRescanAttempt();
		Scans++;
		if (FailScans > 0)
		{
			FailScans--;
			Recovery.NoteRescanFailed(Now);
			return;
		}
		if (_scene.Count > KillOccluderRecovery.MaxEnemies)
		{
			Recovery.NoteRescanFailed(Now);
			return;
		}
		bool rejected = false;
		for (int i = 0; i < _scene.Count; i++)
		{
			if (Registry.Register(_scene[i], _scene[i], new IntPtr(_scene[i].Id), Now) == OccluderRegisterResult.Rejected)
			{
				rejected = true;
			}
		}
		if (rejected)
		{
			Recovery.NoteRescanFailed(Now);
			return;
		}
		Recovery.NoteRegistryRebuilt();
	}

	private FakeEnemy FindById(int id)
	{
		for (int i = 0; i < _scene.Count; i++)
		{
			if (_scene[i].Id == id)
			{
				return _scene[i];
			}
		}
		return null;
	}
}

// Negative control: the 0f72b86 behaviour of KillIconOccluders. `_complete` was cleared by
// a seed/collider/registration failure (old lines 172-178, 204-208, 72-76), by one capture
// exception (old lines 134-138) and by a saturating candidate buffer (old lines 152-156),
// and only Seed/Reset set it back to true. Collect returned early on `_complete == false`
// (old lines 107-110), so one transient fault hid the icons for the rest of the level.
internal sealed class LegacyStickyModel
{
	private bool _complete;

	internal bool Complete => _complete;

	internal void Seed(int liveEnemies)
	{
		_complete = liveEnemies <= 96;
	}

	internal void CaptureFailed()
	{
		_complete = false;
	}

	internal void CollidersOverCap()
	{
		_complete = false;
	}

	internal void RegisterOverflow()
	{
		_complete = false;
	}

	internal void Frame(int liveEnemies)
	{
		if (_complete && liveEnemies > 96)
		{
			_complete = false;
		}
	}

	internal void Reset()
	{
		_complete = false;
	}
}
