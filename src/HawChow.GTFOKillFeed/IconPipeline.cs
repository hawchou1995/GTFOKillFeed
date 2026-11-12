using System;
using System.Collections.Generic;
using System.Reflection;

namespace HawChow.GTFOKillFeed;

internal sealed class IconPipeline
{
	private sealed class Slot
	{
		internal readonly AssetReloadState State = new AssetReloadState();

		internal object Texture;

		internal string LoadedPath;

		internal float NextProbeAt;

		internal bool Warned;

		internal System.DateTime PendingWriteUtc;

		internal long PendingLength = -1L;
	}

	private sealed class Job
	{
		internal string Key;

		internal string Path;

		internal bool ReadPhase;

		internal System.DateTime WriteUtc;

		internal long Length;

		internal bool Ok;

		internal byte[] Bytes;

		internal string Error;

		internal int Generation;
	}

	internal const long MaxIconBytes = 8388608L;

	private const int MaxResultsPerTick = 4;

	private readonly IIconIo _io;

	private readonly IIconSink _sink;

	private readonly IIconJobRunner _runner;

	private readonly Action<string> _warn;

	private readonly Dictionary<string, Slot> _slots = new Dictionary<string, Slot>();

	private readonly Queue<Job> _done = new Queue<Job>();

	private readonly object _gate = new object();

	private bool _inFlight;

	private int _generation;

	internal bool InFlight
	{
		get
		{
			lock (_gate)
			{
				return _inFlight;
			}
		}
	}

	internal int SlotCount => _slots.Count;

	internal IconPipeline(IIconIo io, IIconSink sink, IIconJobRunner runner, Action<string> warn)
	{
		_io = io;
		_sink = sink;
		_runner = runner ?? ThreadPoolIconJobRunner.Instance;
		_warn = warn;
	}

	internal object Texture(string key)
	{
		Slot slot = default(Slot);
		if (key == null || !_slots.TryGetValue(key, out slot))
		{
			return null;
		}
		return slot.Texture;
	}

	internal string LoadedPath(string key)
	{
		Slot slot = default(Slot);
		if (key == null || !_slots.TryGetValue(key, out slot))
		{
			return null;
		}
		return slot.LoadedPath;
	}

	internal int FailureCount(string key)
	{
		Slot slot = default(Slot);
		if (key == null || !_slots.TryGetValue(key, out slot))
		{
			return 0;
		}
		return slot.State.Failures;
	}

	internal void Tick(string key, string path, float now)
	{
		if (string.IsNullOrWhiteSpace(key))
		{
			return;
		}
		Slot slot = SlotFor(key);
		DrainResults(now);
		if (string.IsNullOrWhiteSpace(path))
		{
			return;
		}
		if (!string.Equals(slot.State.Path, path, (StringComparison)4))
		{
			GenerationFor(key, path);
		}
		if (!_inFlight && HotReloadPolicy.ShouldProbe(now, slot.NextProbeAt))
		{
			if (TeammateMarkPolicy.IsNetworkPath(path))
			{
				WarnOnce(slot, "network icon path refused: " + HotReloadPolicy.SafeFileName(path));
				slot.NextProbeAt = now + 5f;
			}
			else
			{
				slot.NextProbeAt = now + ProbeInterval(slot.State);
				StartProbe(key, path);
			}
		}
	}

	private static float ProbeInterval(AssetReloadState state)
	{
		float num = ((state.Failures <= 0) ? 0f : AssetReloadState.RetryDelay(state.Failures));
		if (!(num > 0.5f))
		{
			return 0.5f;
		}
		return num;
	}

	private Slot SlotFor(string key)
	{
		Slot result = default(Slot);
		if (_slots.TryGetValue(key, out result))
		{
			return result;
		}
		result = new Slot();
		_slots[key] = result;
		return result;
	}

	private void StartProbe(string key, string path)
	{
		//IL_0064: Unknown result type (might be due to invalid IL or missing references)
		//IL_006e: Expected O, but got Unknown
		int generation = GenerationFor(key, path);
		lock (_gate)
		{
			_inFlight = true;
		}
		_runner.Run((Action)delegate
		{
			Job job = new Job
			{
				Key = key,
				Path = path,
				Generation = generation
			};
			try
			{
				job.Ok = _io.TryProbe(path, out var writeUtc, out var length, out var error);
				job.WriteUtc = writeUtc;
				job.Length = length;
				job.Error = error;
			}
			catch (System.Exception ex)
			{
				job.Ok = false;
				job.Error = ((MemberInfo)ex.GetType()).Name;
			}
			Complete(job);
		});
	}

	private void StartRead(string key, string path, System.DateTime writeUtc, long length)
	{
		//IL_007e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0088: Expected O, but got Unknown
		int generation = GenerationFor(key, path);
		Slot slot = SlotFor(key);
		slot.PendingWriteUtc = writeUtc;
		slot.PendingLength = length;
		lock (_gate)
		{
			_inFlight = true;
		}
		_runner.Run((Action)delegate
		{
			Job job = new Job
			{
				Key = key,
				Path = path,
				ReadPhase = true,
				Generation = generation
			};
			try
			{
				job.Bytes = _io.TryRead(path, 8388608L, out var error);
				job.Error = error;
				job.Ok = job.Bytes != null && job.Bytes.Length != 0;
			}
			catch (System.Exception ex)
			{
				job.Ok = false;
				job.Error = ((MemberInfo)ex.GetType()).Name;
			}
			Complete(job);
		});
	}

	private void Complete(Job job)
	{
		lock (_gate)
		{
			_inFlight = false;
			if (_done.Count < 32)
			{
				_done.Enqueue(job);
			}
		}
	}

	private int GenerationFor(string key, string path)
	{
		Slot slot = SlotFor(key);
		if (!string.Equals(slot.State.Path, path, (StringComparison)4))
		{
			_generation++;
			slot.State.Reset();
			slot.State.Path = path;
		}
		return _generation;
	}

	private void DrainResults(float now)
	{
		for (int i = 0; i < 4; i++)
		{
			Job job;
			lock (_gate)
			{
				if (_done.Count == 0)
				{
					break;
				}
				job = _done.Dequeue();
			}
			if (job == null || job.Key == null)
			{
				continue;
			}
			Slot slot = SlotFor(job.Key);
			if (job.Generation == _generation && string.Equals(slot.State.Path, job.Path, (StringComparison)4))
			{
				if (job.ReadPhase)
				{
					ApplyRead(slot, job, now);
				}
				else
				{
					ApplyProbe(job, now);
				}
			}
		}
	}

	private void ApplyProbe(Job job, float now)
	{
		Slot slot = SlotFor(job.Key);
		if (!job.Ok)
		{
			MarkFailure(slot, now, string.IsNullOrEmpty(job.Error) ? "probe failed" : job.Error);
		}
		else if (slot.State.Evaluate(job.Path, job.WriteUtc, job.Length, now) == AssetReloadAction.Reload)
		{
			slot.State.MarkAttempt(now);
			StartRead(job.Key, job.Path, job.WriteUtc, job.Length);
		}
	}

	private void ApplyRead(Slot slot, Job job, float now)
	{
		if (!job.Ok)
		{
			MarkFailure(slot, now, string.IsNullOrEmpty(job.Error) ? "read failed" : job.Error);
			return;
		}
		object obj = null;
		try
		{
			obj = _sink.Create(job.Bytes);
		}
		catch (System.Exception ex)
		{
			MarkFailure(slot, now, "decode threw " + ((MemberInfo)ex.GetType()).Name);
			return;
		}
		if (obj == null)
		{
			MarkFailure(slot, now, "not a decodable PNG");
			return;
		}
		object texture = slot.Texture;
		slot.Texture = obj;
		slot.LoadedPath = job.Path;
		slot.State.MarkLoaded(job.Path, slot.PendingWriteUtc, slot.PendingLength, now);
		if (texture != null)
		{
			try
			{
				_sink.Release(texture);
			}
			catch
			{
			}
		}
		slot.Warned = false;
	}

	private void MarkFailure(Slot slot, float now, string reason)
	{
		slot.State.MarkFailed(now);
		slot.NextProbeAt = now + ProbeInterval(slot.State);
		WarnOnce(slot, "icon reload failed: " + reason);
	}

	private void WarnOnce(Slot slot, string message)
	{
		if (slot.Warned)
		{
			return;
		}
		slot.Warned = true;
		try
		{
			_warn?.Invoke(message);
		}
		catch
		{
		}
	}
}
