using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace HawChow.GTFOKillFeed;

internal sealed class CorpseFadeCoordinator
{
	private sealed class Entry
	{
		internal object Handle;
	}

	private readonly List<Entry> _entries = new List<Entry>();

	internal int Active => _entries.Count;

	internal int ManualReleases
	{
		get;
		private set;
	}

	internal int GameReleases
	{
		get;
		private set;
	}

	internal int DestroyedReleases
	{
		get;
		private set;
	}

	internal int Skips
	{
		get;
		private set;
	}

	internal int Failures
	{
		get;
		private set;
	}

	internal int Recovered
	{
		get;
		private set;
	}

	internal int UnknownStates
	{
		get;
		private set;
	}

	internal int BorrowPolls
	{
		get;
		private set;
	}

	internal int BorrowMisses
	{
		get;
		private set;
	}

	internal int LastPolls
	{
		get;
		private set;
	}

	internal CorpseFadeOutcome LastOutcome
	{
		get;
		private set;
	}

	internal System.Exception LastError
	{
		get;
		private set;
	}

	internal bool TryStart(ICorpseFadeTarget target, object renderer)
	{
		CorpseFadeOutcome outcome;
		return TryStart(target, renderer, out outcome);
	}

	internal bool TryStart(ICorpseFadeTarget target, object renderer, out CorpseFadeOutcome outcome)
	{
		outcome = CorpseFadeOutcome.TargetMissing;
		LastPolls = 0;
		LastError = null;
		if (target == null)
		{
			Skips++;
			LastOutcome = outcome;
			return false;
		}
		if (_entries.Count >= 2)
		{
			Skips++;
			outcome = CorpseFadeOutcome.ConcurrencyCap;
			LastOutcome = outcome;
			return false;
		}
		int num = 1;
		while (true)
		{
			LastPolls = num;
			if (TryOnce(target, renderer, out outcome))
			{
				LastOutcome = outcome;
				return true;
			}
			LastOutcome = outcome;
			if (outcome != CorpseFadeOutcome.NoFreeInstance || num >= 3)
			{
				break;
			}
			num++;
		}
		return false;
	}

	private bool TryOnce(ICorpseFadeTarget target, object renderer, out CorpseFadeOutcome outcome)
	{
		outcome = CorpseFadeOutcome.Unexpected;
		object handle = null;
		try
		{
			BorrowPolls++;
			CorpseFadeBorrow corpseFadeBorrow;
			try
			{
				corpseFadeBorrow = target.TryBorrow(out handle);
			}
			catch (System.Exception lastError)
			{
				LastError = lastError;
				Skips++;
				outcome = CorpseFadeOutcome.BorrowError;
				return false;
			}
			switch (corpseFadeBorrow)
			{
			case CorpseFadeBorrow.PoolMissing:
				Skips++;
				outcome = CorpseFadeOutcome.PoolMissing;
				return false;
			default:
				if (handle != null)
				{
					if (corpseFadeBorrow != 0)
					{
						Skips++;
						outcome = CorpseFadeOutcome.BorrowError;
						return false;
					}
					if (Contains(handle))
					{
						Skips++;
						outcome = CorpseFadeOutcome.InstanceTracked;
						return false;
					}
					switch (Probe(target, handle))
					{
					case CorpseFadeState.Unknown:
						Skips++;
						outcome = CorpseFadeOutcome.ProbeUnknown;
						return false;
					case CorpseFadeState.Gone:
						Skips++;
						outcome = CorpseFadeOutcome.InstanceGone;
						return false;
					default:
						Skips++;
						outcome = CorpseFadeOutcome.InstanceBusy;
						return false;
					case CorpseFadeState.Idle:
					{
						bool flag;
						try
						{
							flag = target.Initialize(handle, renderer);
						}
						catch (System.Exception lastError2)
						{
							LastError = lastError2;
							Failures++;
							ReleaseManual(target, handle);
							outcome = CorpseFadeOutcome.InitializeThrew;
							return false;
						}
						if (!flag)
						{
							Failures++;
							ReleaseManual(target, handle);
							outcome = CorpseFadeOutcome.InitializeFailed;
							return false;
						}
						bool flag2 = false;
						try
						{
							target.Play(handle, 0.5f);
						}
						catch (System.Exception lastError3)
						{
							flag2 = true;
							LastError = lastError3;
						}
						switch (Probe(target, handle))
						{
						case CorpseFadeState.Playing:
							_entries.Add(new Entry
							{
								Handle = handle
							});
							outcome = CorpseFadeOutcome.Started;
							return true;
						case CorpseFadeState.Unknown:
							UnknownStates++;
							_entries.Add(new Entry
							{
								Handle = handle
							});
							outcome = CorpseFadeOutcome.StartedProbeUnknown;
							return true;
						default:
							Failures++;
							ReleaseManual(target, handle);
							outcome = (flag2 ? CorpseFadeOutcome.PlayThrew : CorpseFadeOutcome.PlayNotObserved);
							return false;
						}
					}
					}
				}
				goto case CorpseFadeBorrow.NoFreeInstance;
			case CorpseFadeBorrow.NoFreeInstance:
				Skips++;
				BorrowMisses++;
				outcome = CorpseFadeOutcome.NoFreeInstance;
				return false;
			}
		}
		catch (System.Exception lastError4)
		{
			LastError = lastError4;
			Failures++;
			ReleaseManual(target, handle);
			outcome = CorpseFadeOutcome.Unexpected;
			return false;
		}
	}

	private bool Contains(object handle)
	{
		for (int i = 0; i < _entries.Count; i++)
		{
			if (_entries[i].Handle == handle)
			{
				return true;
			}
		}
		return false;
	}

	private static CorpseFadeState Probe(ICorpseFadeTarget target, object handle)
	{
		if (target == null || handle == null)
		{
			return CorpseFadeState.Gone;
		}
		try
		{
			return target.Probe(handle);
		}
		catch (System.Exception)
		{
			return CorpseFadeState.Unknown;
		}
	}

	private void ReleaseManual(ICorpseFadeTarget target, object handle)
	{
		try
		{
			target.Cleanup(handle);
		}
		catch (System.Exception)
		{
		}
		try
		{
			target.Return(handle);
		}
		catch (System.Exception)
		{
		}
		ManualReleases++;
		Recovered++;
	}

	internal int Sweep(ICorpseFadeTarget target)
	{
		return Prune(target);
	}

	internal int Reset(ICorpseFadeTarget target)
	{
		return Prune(target);
	}

	private int Prune(ICorpseFadeTarget target)
	{
		if (_entries.Count == 0)
		{
			return 0;
		}
		int num = 0;
		for (int num2 = _entries.Count - 1; num2 >= 0; num2--)
		{
			CorpseFadeState corpseFadeState = Probe(target, _entries[num2].Handle);
			if (corpseFadeState != 0)
			{
				switch (corpseFadeState)
				{
				case CorpseFadeState.Unknown:
					num++;
					continue;
				case CorpseFadeState.Idle:
					GameReleases++;
					break;
				default:
					DestroyedReleases++;
					break;
				}
				_entries.RemoveAt(num2);
			}
		}
		return num;
	}

	internal void ResetCounters()
	{
		ManualReleases = 0;
		GameReleases = 0;
		DestroyedReleases = 0;
		Skips = 0;
		Failures = 0;
		Recovered = 0;
		UnknownStates = 0;
		BorrowPolls = 0;
		BorrowMisses = 0;
		LastPolls = 0;
		LastOutcome = CorpseFadeOutcome.Started;
		LastError = null;
	}
}
