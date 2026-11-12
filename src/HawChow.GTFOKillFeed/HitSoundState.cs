using System;

namespace HawChow.GTFOKillFeed;

internal sealed class HitSoundState
{
	private static readonly uint[] NoIds = System.Array.Empty<uint>();

	private readonly object _resolveGate = new object();

	private readonly object _throttleGate = new object();

	private uint[] _mutedIds = NoIds;

	private bool _resolveStarted;

	private bool _resolveFinished;

	private int _resolveFailures = -1;

	private double _lastHit = -1.0;

	private double _lastKill = -1.0;

	private int _played;

	internal uint[] MutedIds => _mutedIds;

	internal int MutedIdCount => _mutedIds.Length;

	internal int ResolveFailures => _resolveFailures;

	internal int Played => _played;

	internal bool ResolveStarted
	{
		get
		{
			lock (_resolveGate)
			{
				return _resolveStarted;
			}
		}
	}

	internal bool ResolveFinished
	{
		get
		{
			lock (_resolveGate)
			{
				return _resolveFinished;
			}
		}
	}

	internal bool TryBeginResolve()
	{
		lock (_resolveGate)
		{
			if (_resolveStarted)
			{
				return false;
			}
			_resolveStarted = true;
			return true;
		}
	}

	internal void PublishResolved(uint[] ids, int failures)
	{
		lock (_resolveGate)
		{
			_mutedIds = ids ?? NoIds;
			_resolveFailures = ((failures >= 0) ? failures : 0);
			_resolveFinished = true;
		}
	}

	internal bool TryPlay(bool kill, double now, double interval, Func<bool> play)
	{
		if (play == null)
		{
			return false;
		}
		lock (_throttleGate)
		{
			double lastPlayed = (kill ? _lastKill : _lastHit);
			if (!IntervalAllows(now, lastPlayed, interval))
			{
				return false;
			}
			if (!play.Invoke())
			{
				return false;
			}
			if (kill)
			{
				_lastKill = now;
			}
			else
			{
				_lastHit = now;
			}
			_played++;
			return true;
		}
	}

	internal void Reset()
	{
		lock (_throttleGate)
		{
			_lastHit = -1.0;
			_lastKill = -1.0;
		}
	}

	private static bool IntervalAllows(double now, double lastPlayed, double interval)
	{
		if (double.IsNaN(now) || double.IsInfinity(now))
		{
			return false;
		}
		if (double.IsNaN(lastPlayed) || double.IsInfinity(lastPlayed) || lastPlayed < 0.0)
		{
			return true;
		}
		return now - lastPlayed >= ((interval < 0.0 || double.IsNaN(interval)) ? 0.0 : interval);
	}
}
