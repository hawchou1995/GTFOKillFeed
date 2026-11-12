using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using AK;
using UnityEngine;

namespace HawChow.GTFOKillFeed;

internal static class HitSound
{
	private static readonly HitSoundState State = new HitSoundState();

	private static readonly ConcurrentDictionary<string, short[]> CueCache = new ConcurrentDictionary<string, short[]>((IEqualityComparer<string>)(object)StringComparer.Ordinal);

	private static readonly ConcurrentDictionary<string, byte> ReportedMissing = new ConcurrentDictionary<string, byte>((IEqualityComparer<string>)(object)StringComparer.Ordinal);

	private static int _deviceFailures;

	internal static int Played => State.Played;

	internal static int MutedIdCount => State.MutedIds.Length;

	internal static int UnresolvedEventIds => State.ResolveFailures;

	private static void ResolveMutedIds()
	{
		if (!State.TryBeginResolve())
		{
			return;
		}
		try
		{
			System.Type type = null;
			try
			{
				type = typeof(EVENTS);
			}
			catch (System.Exception)
			{
				type = null;
			}
			if (type == (System.Type)null)
			{
				State.PublishResolved(System.Array.Empty<uint>(), HitSoundPolicy.MutedEventNames.Length);
				LogWarn("[HITSOUND] AK.EVENTS is not resolvable; the vanilla indicator layer stays audible");
				return;
			}
			List<uint> val = new List<uint>(HitSoundPolicy.MutedEventNames.Length);
			HashSet<string> val2 = new HashSet<string>((IEqualityComparer<string>)(object)StringComparer.Ordinal);
			for (int i = 0; i < HitSoundPolicy.MutedEventNames.Length; i++)
			{
				string text = HitSoundPolicy.MutedEventNames[i];
				if (TryReadEventId(type, text, out var id))
				{
					val2.Add(text);
					if (!val.Contains(id))
					{
						val.Add(id);
					}
				}
			}
			int num = HitSoundPolicy.MutedEventNames.Length - val2.Count;
			State.PublishResolved(val.ToArray(), num);
			if (num > 0)
			{
				LogWarn("[HITSOUND] " + num + " indicator event id(s) not found on AK.EVENTS; those events stay audible");
			}
			else
			{
				LogInfo("[HITSOUND] muted vanilla indicator events: " + string.Join(",", HitSoundPolicy.MutedEventNames));
			}
		}
		catch (System.Exception ex2)
		{
			State.PublishResolved(System.Array.Empty<uint>(), HitSoundPolicy.MutedEventNames.Length);
			LogWarn("[HITSOUND] indicator id resolution failed (" + ((MemberInfo)ex2.GetType()).Name + "); the vanilla layer stays audible");
		}
	}

	private static bool TryReadEventId(System.Type events, string name, out uint id)
	{
		id = 0u;
		try
		{
			PropertyInfo property = events.GetProperty(name, (BindingFlags)24);
			if (property != (PropertyInfo)null && property.PropertyType == typeof(uint))
			{
				id = (uint)property.GetValue((object)null);
				return true;
			}
			FieldInfo field = events.GetField(name, (BindingFlags)24);
			if (field != (FieldInfo)null && field.FieldType == typeof(uint))
			{
				id = (uint)field.GetValue((object)null);
				return true;
			}
		}
		catch (System.Exception)
		{
		}
		return false;
	}

	internal static bool ShouldRunPost(uint eventId)
	{
		ResolveMutedIds();
		uint[] mutedIds = State.MutedIds;
		if (mutedIds.Length == 0)
		{
			return true;
		}
		return !HitSoundPolicy.ShouldMute(HitSoundPolicy.MuteRequested((Plugin.HitSoundMuteVanillaIndicator != null) ? new bool?(Plugin.HitSoundMuteVanillaIndicator.Value) : null), mutedIds, eventId);
	}

	internal static void OnLocalHit(HitKind kind)
	{
		if (Enabled())
		{
			PlayThrottled(kill: false, HitSoundPolicy.KindOf(kind, kill: false), KillTier.Minor, KillFlight.None);
		}
	}

	internal static void OnLocalKill(KillTier tier, KillFlight flight)
	{
		if (Enabled())
		{
			PlayThrottled(kill: true, HitCueKind.Kill, tier, flight);
		}
	}

	private static void PlayThrottled(bool kill, HitCueKind kind, KillTier tier, KillFlight flight)
	{
		double now = Now();
		double interval = HitSoundPolicy.MinInterval((Plugin.HitSoundMinIntervalSeconds != null) ? ((double)Plugin.HitSoundMinIntervalSeconds.Value) : 0.05);
		State.TryPlay(kill, now, interval, () => PlayCue(kind, tier, flight));
	}

	private static bool Enabled()
	{
		bool enabled = Plugin.HitSoundEnabled == null || Plugin.HitSoundEnabled.Value;
		float volume = HitSoundPolicy.Volume((Plugin.HitSoundVolume != null) ? Plugin.HitSoundVolume.Value : 0.8f);
		return HitSoundPolicy.ShouldPlay(enabled, volume);
	}

	private static float Volume()
	{
		return HitSoundPolicy.Volume((Plugin.HitSoundVolume != null) ? Plugin.HitSoundVolume.Value : 0.8f);
	}

	private static HitSoundStyle Style()
	{
		return HitSoundPolicy.ParseStyle((Plugin.HitSoundStyle != null) ? Plugin.HitSoundStyle.Value : "BF5");
	}

	private static double Now()
	{
		try
		{
			return Time.unscaledTime;
		}
		catch (System.Exception)
		{
			return 0.0;
		}
	}

	private static bool PlayCue(HitCueKind kind, KillTier tier, KillFlight flight)
	{
		string[] array = HitSoundPolicy.CueChain(Style(), kind, tier, flight);
		float volume = Volume();
		for (int i = 0; i < array.Length; i++)
		{
			short[] array2 = LoadCue(array[i]);
			if (array2 != null && array2.Length != 0)
			{
				if (CueWavePlayer.Play(array2, array2.Length, volume))
				{
					return true;
				}
				if (Interlocked.Increment(ref _deviceFailures) == 1)
				{
					LogWarn("[HITSOUND] no audio output could be opened; cues stay silent (game audio is unaffected)");
				}
				return false;
			}
		}
		return false;
	}

	private static short[] LoadCue(string resourceName)
	{
		short[] result = default(short[]);
		if (CueCache.TryGetValue(resourceName, out result))
		{
			return result;
		}
		short[] array = ReadEmbeddedCue(resourceName);
		short[] orAdd = CueCache.GetOrAdd(resourceName, array);
		if (orAdd == null && ReportedMissing.TryAdd(resourceName, (byte)0))
		{
			LogWarn("[HITSOUND] cue-missing: " + resourceName);
		}
		return orAdd;
	}

	private static short[] ReadEmbeddedCue(string resourceName)
	{
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0023: Expected O, but got Unknown
		try
		{
			Stream manifestResourceStream = typeof(HitSound).Assembly.GetManifestResourceStream(resourceName);
			try
			{
				if (manifestResourceStream == null)
				{
					return null;
				}
				MemoryStream val = new MemoryStream();
				try
				{
					manifestResourceStream.CopyTo((Stream)(object)val);
					return ParsePcm16Mono(val.ToArray());
				}
				finally
				{
					((System.IDisposable)val)?.Dispose();
				}
			}
			finally
			{
				((System.IDisposable)manifestResourceStream)?.Dispose();
			}
		}
		catch (System.Exception ex)
		{
			LogWarn("[HITSOUND] cue read failed (" + resourceName + "): " + ((MemberInfo)ex.GetType()).Name);
			return null;
		}
	}

	internal static short[] ParsePcm16Mono(byte[] bytes)
	{
		if (bytes == null || bytes.Length < 44)
		{
			return null;
		}
		if (bytes[0] != 82 || bytes[1] != 73 || bytes[2] != 70 || bytes[3] != 70)
		{
			return null;
		}
		if (bytes[8] != 87 || bytes[9] != 65 || bytes[10] != 86 || bytes[11] != 69)
		{
			return null;
		}
		int num = 12;
		int num2 = -1;
		int num3 = 0;
		int num4 = 0;
		int num5 = 0;
		int num6 = 0;
		int num7 = 0;
		while (num + 8 <= bytes.Length)
		{
			int num8 = BitConverter.ToInt32(bytes, num + 4);
			int num9 = num + 8;
			if (num8 < 0 || num9 + num8 > bytes.Length)
			{
				break;
			}
			if (bytes[num] == 102 && bytes[num + 1] == 109 && bytes[num + 2] == 116 && bytes[num + 3] == 32)
			{
				num7 = BitConverter.ToUInt16(bytes, num9);
				num4 = BitConverter.ToUInt16(bytes, num9 + 2);
				num5 = BitConverter.ToInt32(bytes, num9 + 4);
				num6 = BitConverter.ToUInt16(bytes, num9 + 14);
			}
			else if (bytes[num] == 100 && bytes[num + 1] == 97 && bytes[num + 2] == 116 && bytes[num + 3] == 97)
			{
				num2 = num9;
				num3 = num8;
			}
			num = num9 + num8 + (num8 & 1);
		}
		if (num2 < 0 || num3 < 2)
		{
			return null;
		}
		if (num7 != 1 || num4 != 1 || num6 != 16 || num5 != 48000)
		{
			return null;
		}
		int num10 = num3 / 2;
		short[] array = new short[num10];
		Buffer.BlockCopy((System.Array)bytes, num2, (System.Array)array, 0, num10 * 2);
		return array;
	}

	internal static void Reset(string reason)
	{
		State.Reset();
	}

	internal static void Shutdown()
	{
		try
		{
			CueWavePlayer.Shutdown();
		}
		catch (System.Exception)
		{
		}
		CueCache.Clear();
		ReportedMissing.Clear();
		State.Reset();
	}

	private static void LogInfo(string message)
	{
		Plugin.Instance?.Log.LogInfo(message);
	}

	private static void LogWarn(string message)
	{
		Plugin.Instance?.Log.LogWarning(message);
	}
}
