using System;
using System.Collections.Generic;

namespace HawChow.GTFOKillFeed;

internal struct OccluderRetry
{
	internal int Failures;

	internal float RetryAt;
}

internal enum OccluderRegisterResult
{
	Added,
	Duplicate,
	Rejected
}

// Unity-free recovery state for the head icon occluder registry. Two different kinds of
// "incomplete" are kept apart:
//   * frame incomplete  - this frame's candidate set cannot be trusted (one capture
//     failure, a collider/registration cap hit while collecting, a saturating candidate
//     buffer). Only the current frame hides.
//   * registry incomplete - a live enemy is known to be missing from the registry
//     (seed or registration overflow). Icons stay hidden until a bounded, rate limited
//     rebuild proves that every live enemy fits again.
internal sealed class KillOccluderRecovery
{
	internal const int MaxEnemies = 96;

	internal const int MaxCollidersPerEnemy = 48;

	internal const float ColliderRefreshSeconds = 0.5f;

	internal const int FreeCaptureRetries = 2;

	internal const float CaptureRetryBaseSeconds = 0.25f;

	internal const float CaptureRetryMaxSeconds = 2f;

	internal const float RescanBaseSeconds = 0.5f;

	internal const float RescanMaxSeconds = 2f;

	private bool _seeded;

	private bool _registryComplete = true;

	private bool _frameComplete = true;

	private bool _rescanPending;

	private int _failedRescans;

	private int _rescanAttempts;

	private float _nextRescanAt;

	internal bool Seeded => _seeded;

	internal bool RegistryComplete => _registryComplete;

	internal bool FrameComplete => _frameComplete;

	internal bool Complete => _registryComplete && _frameComplete;

	internal bool RescanPending => _rescanPending;

	internal int FailedRescans => _failedRescans;

	internal int RescanAttempts => _rescanAttempts;

	internal float NextRescanAt => _nextRescanAt;

	internal void Clear()
	{
		_seeded = false;
		_registryComplete = true;
		_frameComplete = true;
		_rescanPending = false;
		_failedRescans = 0;
		_rescanAttempts = 0;
		_nextRescanAt = 0f;
	}

	internal void MarkSeeded()
	{
		_seeded = true;
	}

	internal void BeginFrame()
	{
		_frameComplete = true;
	}

	internal void NoteFrameIncomplete()
	{
		_frameComplete = false;
	}

	// A live enemy could not be registered (or the seed scan overflowed): the registry is
	// known to be missing members. Schedule the first bounded rebuild attempt.
	internal void NoteRegistryIncomplete(float now)
	{
		_frameComplete = false;
		_registryComplete = false;
		if (_rescanPending)
		{
			return;
		}
		_rescanPending = true;
		_failedRescans = 0;
		_nextRescanAt = Sanitize(now) + RescanBaseSeconds;
	}

	// A bounded rebuild ran and still could not prove completeness (too many enemies,
	// seed/registration still over capacity, or the scan threw). Back off and retry later.
	internal void NoteRescanFailed(float now)
	{
		_frameComplete = false;
		_registryComplete = false;
		_rescanPending = true;
		if (_failedRescans < int.MaxValue)
		{
			_failedRescans++;
		}
		_nextRescanAt = Sanitize(now) + RescanDelay(_failedRescans);
	}

	internal void NoteRescanAttempt()
	{
		if (_rescanAttempts < int.MaxValue)
		{
			_rescanAttempts++;
		}
	}

	// Evidence of recovered completeness: one bounded scan saw every live enemy and the
	// bounded registry could hold all of them.
	internal void NoteRegistryRebuilt()
	{
		_registryComplete = true;
		_rescanPending = false;
		_failedRescans = 0;
		_nextRescanAt = 0f;
	}

	internal bool RescanDue(float now)
	{
		if (!_rescanPending)
		{
			return false;
		}
		if (!KillViewPolicy.IsFinite(now))
		{
			return false;
		}
		return now >= _nextRescanAt;
	}

	internal static float RescanDelay(int failedRescans)
	{
		int steps = ((failedRescans < 1) ? 0 : (failedRescans - 1));
		steps = Math.Min(steps, 16);
		double delay = (double)RescanBaseSeconds * Math.Pow(2.0, steps);
		return (float)Math.Min(delay, (double)RescanMaxSeconds);
	}

	// A capture that failed once is retried on the very next frame; only a persistently
	// failing enemy is throttled, so a transient glitch cannot outlive the frame.
	internal static bool MayRetry(in OccluderRetry retry, float now)
	{
		if (retry.Failures <= FreeCaptureRetries)
		{
			return true;
		}
		if (!KillViewPolicy.IsFinite(now))
		{
			return false;
		}
		return now >= retry.RetryAt;
	}

	internal static void NoteCaptureSuccess(ref OccluderRetry retry)
	{
		retry.Failures = 0;
		retry.RetryAt = 0f;
	}

	internal static void NoteCaptureFailure(ref OccluderRetry retry, float now)
	{
		if (retry.Failures < int.MaxValue)
		{
			retry.Failures++;
		}
		if (retry.Failures > FreeCaptureRetries)
		{
			retry.RetryAt = Sanitize(now) + CaptureRetryDelay(retry.Failures);
		}
	}

	internal static float CaptureRetryDelay(int failures)
	{
		int over = failures - FreeCaptureRetries - 1;
		if (over < 0)
		{
			over = 0;
		}
		over = Math.Min(over, 16);
		double delay = (double)CaptureRetryBaseSeconds * Math.Pow(2.0, over);
		return (float)Math.Min(delay, (double)CaptureRetryMaxSeconds);
	}

	private static float Sanitize(float now)
	{
		return KillViewPolicy.IsFinite(now) ? now : 0f;
	}
}

// Bounded enemy registry. Capacity, de-duplication, pruning and the overflow signal live
// here so the same code drives the game collector and the tests.
internal sealed class KillOccluderRegistry<T> where T : class
{
	internal sealed class Entry
	{
		internal readonly T Value;

		internal readonly object Identity;

		internal readonly System.IntPtr Key;

		internal float CapturedAt;

		internal bool Captured;

		internal OccluderRetry Retry;

		internal Entry(T value, object identity, System.IntPtr key)
		{
			Value = value;
			Identity = identity;
			Key = key;
		}
	}

	private readonly List<Entry> _entries;

	private readonly KillOccluderRecovery _recovery;

	private readonly int _capacity;

	internal KillOccluderRegistry(KillOccluderRecovery recovery, int capacity)
	{
		_recovery = recovery ?? new KillOccluderRecovery();
		_capacity = Math.Max(1, capacity);
		_entries = new List<Entry>(_capacity);
	}

	internal KillOccluderRecovery Recovery => _recovery;

	internal int Capacity => _capacity;

	internal int Count => _entries.Count;

	internal Entry At(int index)
	{
		return _entries[index];
	}

	internal OccluderRegisterResult Register(T value, object identity, System.IntPtr key, float now)
	{
		if (value == null || identity == null)
		{
			return OccluderRegisterResult.Duplicate;
		}
		for (int i = 0; i < _entries.Count; i++)
		{
			if (Same(_entries[i], identity, key))
			{
				return OccluderRegisterResult.Duplicate;
			}
		}
		if (_entries.Count >= _capacity)
		{
			_recovery.NoteRegistryIncomplete(now);
			return OccluderRegisterResult.Rejected;
		}
		_entries.Add(new Entry(value, identity, key));
		return OccluderRegisterResult.Added;
	}

	internal bool Unregister(object identity, System.IntPtr key)
	{
		if (identity == null)
		{
			return false;
		}
		for (int i = _entries.Count - 1; i >= 0; i--)
		{
			if (Same(_entries[i], identity, key))
			{
				_entries.RemoveAt(i);
				return true;
			}
		}
		return false;
	}

	internal void RemoveAt(int index)
	{
		if (index >= 0 && index < _entries.Count)
		{
			_entries.RemoveAt(index);
		}
	}

	internal int PruneDead(Func<T, bool> alive)
	{
		if (alive == null)
		{
			return 0;
		}
		int removed = 0;
		for (int i = _entries.Count - 1; i >= 0; i--)
		{
			if (!alive(_entries[i].Value))
			{
				_entries.RemoveAt(i);
				removed++;
			}
		}
		return removed;
	}

	internal void Clear()
	{
		_entries.Clear();
	}

	private static bool Same(Entry entry, object identity, System.IntPtr key)
	{
		if (key != System.IntPtr.Zero && entry.Key != System.IntPtr.Zero)
		{
			return entry.Key == key;
		}
		return object.ReferenceEquals(entry.Identity, identity);
	}
}
