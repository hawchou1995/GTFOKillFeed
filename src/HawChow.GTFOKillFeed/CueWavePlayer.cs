using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace HawChow.GTFOKillFeed;

internal static class CueWavePlayer
{
	private struct WaveFormatEx
	{
		internal ushort FormatTag;

		internal ushort ChannelCount;

		internal uint SamplesPerSecond;

		internal uint AverageBytesPerSecond;

		internal ushort BlockAlign;

		internal ushort BitsPerSample;

		internal ushort ExtraSize;
	}

	private struct WaveHeader
	{
		internal System.IntPtr Data;

		internal uint BufferLength;

		internal uint BytesRecorded;

		internal System.IntPtr User;

		internal uint Flags;

		internal uint Loops;

		internal System.IntPtr Next;

		internal System.IntPtr Reserved;
	}

	private sealed class Voice
	{
		internal System.IntPtr Handle;

		internal System.IntPtr Header;

		internal System.IntPtr Data;

		internal bool Ready;

		internal long EndsAt;
	}

	internal const int VoiceCount = 6;

	internal const int MaxCueSamples = 30720;

	internal const int SampleRate = 48000;

	internal const int Channels = 1;

	internal const int BitsPerSample = 16;

	private const uint WaveMapper = 4294967295u;

	private const uint CallbackNull = 0u;

	private const int RetryDelayMs = 5000;

	private static readonly Voice[] Voices = new Voice[6];

	private static readonly short[] Mix = new short[30720];

	private static readonly object Gate = new object();

	private static int _nextVoice;

	private static int _headerSize;

	private static bool _unavailable;

	private static long _retryAfter;

	internal static bool Available => !_unavailable;

	internal static int Failures
	{
		get;
		private set;
	}

	[DllImport("winmm.dll", SetLastError = true)]
	private static extern int waveOutOpen(out System.IntPtr handle, uint deviceId, ref WaveFormatEx format, System.IntPtr callback, System.IntPtr instance, uint flags);

	[DllImport("winmm.dll", SetLastError = true)]
	private static extern int waveOutPrepareHeader(System.IntPtr handle, System.IntPtr header, int headerSize);

	[DllImport("winmm.dll", SetLastError = true)]
	private static extern int waveOutWrite(System.IntPtr handle, System.IntPtr header, int headerSize);

	[DllImport("winmm.dll", SetLastError = true)]
	private static extern int waveOutReset(System.IntPtr handle);

	[DllImport("winmm.dll", SetLastError = true)]
	private static extern int waveOutUnprepareHeader(System.IntPtr handle, System.IntPtr header, int headerSize);

	[DllImport("winmm.dll", SetLastError = true)]
	private static extern int waveOutClose(System.IntPtr handle);

	internal static void Warmup()
	{
		lock (Gate)
		{
			EnsureOpen();
		}
	}

	private static void EnsureOpen()
	{
		if (Voices[0] != null && Voices[0].Ready)
		{
			return;
		}
		long tickCount = Environment.TickCount64;
		if (_unavailable && tickCount < _retryAfter)
		{
			return;
		}
		_unavailable = false;
		ReleaseVoices();
		try
		{
			_headerSize = Marshal.SizeOf<WaveHeader>();
			WaveFormatEx waveFormatEx = default(WaveFormatEx);
			waveFormatEx.FormatTag = 1;
			waveFormatEx.ChannelCount = 1;
			waveFormatEx.SamplesPerSecond = 48000u;
			waveFormatEx.AverageBytesPerSecond = 96000u;
			waveFormatEx.BlockAlign = 2;
			waveFormatEx.BitsPerSample = 16;
			waveFormatEx.ExtraSize = 0;
			WaveFormatEx format = waveFormatEx;
			for (int i = 0; i < 6; i++)
			{
				Voice voice = new Voice();
				Voices[i] = voice;
				if (waveOutOpen(out voice.Handle, 4294967295u, ref format, System.IntPtr.Zero, System.IntPtr.Zero, 0u) != 0)
				{
					Fail();
					break;
				}
				voice.Data = Marshal.AllocHGlobal(61440);
				voice.Header = Marshal.AllocHGlobal(_headerSize);
				WaveHeader waveHeader = default(WaveHeader);
				waveHeader.Data = voice.Data;
				waveHeader.BufferLength = 61440u;
				Marshal.StructureToPtr<WaveHeader>(waveHeader, voice.Header, false);
				if (waveOutPrepareHeader(voice.Handle, voice.Header, _headerSize) != 0)
				{
					Fail();
					break;
				}
				voice.Ready = true;
			}
		}
		catch (System.Exception)
		{
			Fail();
		}
	}

	private static void Fail()
	{
		Failures++;
		_unavailable = true;
		_retryAfter = Environment.TickCount64 + 5000;
		ReleaseVoices();
	}

	private static void ReleaseVoices()
	{
		for (int i = 0; i < 6; i++)
		{
			Voice voice = Voices[i];
			Voices[i] = null;
			if (voice == null)
			{
				continue;
			}
			try
			{
				if (voice.Handle != System.IntPtr.Zero)
				{
					waveOutReset(voice.Handle);
					if (voice.Header != System.IntPtr.Zero)
					{
						waveOutUnprepareHeader(voice.Handle, voice.Header, _headerSize);
					}
					waveOutClose(voice.Handle);
					voice.Handle = System.IntPtr.Zero;
				}
			}
			catch (System.Exception)
			{
			}
			if (voice.Header != System.IntPtr.Zero)
			{
				try
				{
					Marshal.FreeHGlobal(voice.Header);
				}
				catch (System.Exception)
				{
				}
				voice.Header = System.IntPtr.Zero;
			}
			if (voice.Data != System.IntPtr.Zero)
			{
				try
				{
					Marshal.FreeHGlobal(voice.Data);
				}
				catch (System.Exception)
				{
				}
				voice.Data = System.IntPtr.Zero;
			}
			voice.Ready = false;
			voice.EndsAt = 0L;
		}
		_nextVoice = 0;
	}

	internal static bool Play(short[] samples, int count, float volume)
	{
		if (samples == null || count <= 0)
		{
			return false;
		}
		lock (Gate)
		{
			EnsureOpen();
			if (_unavailable)
			{
				return false;
			}
			if (count > 30720)
			{
				count = 30720;
			}
			if (count > samples.Length)
			{
				count = samples.Length;
			}
			Voice voice = PickVoice(Environment.TickCount64);
			if (voice == null)
			{
				return false;
			}
			try
			{
				waveOutReset(voice.Handle);
				for (int i = 0; i < count; i++)
				{
					int num = (int)((float)samples[i] * volume);
					if (num > 32767)
					{
						num = 32767;
					}
					else if (num < -32768)
					{
						num = -32768;
					}
					Mix[i] = (short)num;
				}
				Marshal.Copy(Mix, 0, voice.Data, count);
				WaveHeader waveHeader = Marshal.PtrToStructure<WaveHeader>(voice.Header);
				waveHeader.BufferLength = (uint)(count * 2);
				waveHeader.BytesRecorded = 0u;
				Marshal.StructureToPtr<WaveHeader>(waveHeader, voice.Header, false);
				if (waveOutWrite(voice.Handle, voice.Header, _headerSize) != 0)
				{
					Failures++;
					return false;
				}
				voice.EndsAt = Environment.TickCount64 + (long)count * 1000L / 48000 + 40;
				return true;
			}
			catch (System.Exception)
			{
				Failures++;
				return false;
			}
		}
	}

	private static Voice PickVoice(long now)
	{
		for (int i = 0; i < 6; i++)
		{
			int num = (_nextVoice + i) % 6;
			Voice voice = Voices[num];
			if (voice != null && voice.Ready && voice.EndsAt <= now)
			{
				_nextVoice = (num + 1) % 6;
				return voice;
			}
		}
		Voice voice2 = null;
		int num2 = -1;
		for (int j = 0; j < 6; j++)
		{
			Voice voice3 = Voices[j];
			if (voice3 != null && voice3.Ready && (voice2 == null || voice3.EndsAt < voice2.EndsAt))
			{
				voice2 = voice3;
				num2 = j;
			}
		}
		if (num2 >= 0)
		{
			_nextVoice = (num2 + 1) % 6;
		}
		return voice2;
	}

	internal static void Shutdown()
	{
		lock (Gate)
		{
			ReleaseVoices();
			_unavailable = false;
			_retryAfter = 0L;
		}
	}
}
