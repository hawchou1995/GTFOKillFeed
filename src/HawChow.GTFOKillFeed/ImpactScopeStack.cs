namespace HawChow.GTFOKillFeed;

internal sealed class ImpactScopeStack
{
	private readonly ImpactScope[] _scopes = new ImpactScope[8];

	private int _count;

	private int _nextId;

	internal int Depth => _count;

	internal int Push(float x, float y, float z, HitKind kind, int frame)
	{
		if (!HitMarkerPolicy.IsFinite(x) || !HitMarkerPolicy.IsFinite(y) || !HitMarkerPolicy.IsFinite(z))
		{
			return 0;
		}
		PruneExpired(frame);
		if (_count >= _scopes.Length)
		{
			return 0;
		}
		int num = ++_nextId;
		if (num == 0)
		{
			num = ++_nextId;
		}
		_scopes[_count++] = new ImpactScope
		{
			Id = num,
			X = x,
			Y = y,
			Z = z,
			Kind = kind,
			Frame = frame
		};
		return num;
	}

	internal bool Pop(int id)
	{
		if (id == 0)
		{
			return false;
		}
		for (int num = _count - 1; num >= 0; num--)
		{
			if (_scopes[num].Id == id)
			{
				RemoveAt(num);
				return true;
			}
		}
		return false;
	}

	internal bool TryClaim(int frame, float x, float y, float z, bool armourOnly, out HitKind kind, out int id)
	{
		kind = HitKind.Normal;
		id = 0;
		int num = Find(frame, x, y, z, armourOnly, unclaimedOnly: true);
		if (num < 0)
		{
			return false;
		}
		kind = _scopes[num].Kind;
		id = _scopes[num].Id;
		_scopes[num].Claimed = true;
		return true;
	}

	internal bool TryIdentify(int frame, float x, float y, float z, out int id)
	{
		id = 0;
		int num = Find(frame, x, y, z, armourOnly: false, unclaimedOnly: false);
		if (num < 0)
		{
			return false;
		}
		id = _scopes[num].Id;
		return true;
	}

	internal void Reset()
	{
		_count = 0;
	}

	private int Find(int frame, float x, float y, float z, bool armourOnly, bool unclaimedOnly)
	{
		for (int num = _count - 1; num >= 0; num--)
		{
			ImpactScope impactScope = _scopes[num];
			if ((!unclaimedOnly || !impactScope.Claimed) && (!armourOnly || impactScope.Kind == HitKind.Armor))
			{
				float distanceSquared = HitMarkerPolicy.DistanceSquared(impactScope.X, impactScope.Y, impactScope.Z, x, y, z);
				if (HitMarkerPolicy.ImpactMatches(frame, impactScope.Frame, distanceSquared))
				{
					return num;
				}
			}
		}
		return -1;
	}

	private void PruneExpired(int frame)
	{
		for (int num = _count - 1; num >= 0; num--)
		{
			if (!HitMarkerPolicy.ImpactFrameCurrent(frame, _scopes[num].Frame))
			{
				RemoveAt(num);
			}
		}
	}

	private void RemoveAt(int index)
	{
		for (int i = index; i < _count - 1; i++)
		{
			_scopes[i] = _scopes[i + 1];
		}
		_count--;
	}
}
