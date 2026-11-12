using System;
using System.IO;
using System.Reflection;

namespace HawChow.GTFOKillFeed;

internal sealed class FileIconIo : IIconIo
{
	private readonly long _fallbackMaximum;

	internal FileIconIo()
		: this(8388608L)
	{
	}

	internal FileIconIo(long fallbackMaximum)
	{
		_fallbackMaximum = ((fallbackMaximum > 0) ? fallbackMaximum : 8388608);
	}

	public bool TryProbe(string path, out System.DateTime writeUtc, out long length, out string error)
	{
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Expected O, but got Unknown
		writeUtc = System.DateTime.MinValue;
		length = -1L;
		error = null;
		if (string.IsNullOrWhiteSpace(path))
		{
			error = "empty path";
			return false;
		}
		try
		{
			FileInfo val = new FileInfo(path);
			if (!((FileSystemInfo)val).Exists)
			{
				error = "missing";
				return false;
			}
			writeUtc = ((FileSystemInfo)val).LastWriteTimeUtc;
			length = val.Length;
			if (length <= 0)
			{
				error = "empty file";
				return false;
			}
			if (length > _fallbackMaximum)
			{
				error = "file too large";
				return false;
			}
			return true;
		}
		catch (System.Exception ex)
		{
			error = ((MemberInfo)ex.GetType()).Name;
			return false;
		}
	}

	public byte[] TryRead(string path, long maximumBytes, out string error)
	{
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Expected O, but got Unknown
		error = null;
		if (string.IsNullOrWhiteSpace(path))
		{
			error = "empty path";
			return null;
		}
		long num = ((maximumBytes > 0) ? maximumBytes : _fallbackMaximum);
		try
		{
			FileStream val = new FileStream(path, (FileMode)3, (FileAccess)1, (FileShare)7);
			try
			{
				if (!((Stream)val).CanRead)
				{
					error = "unreadable";
					return null;
				}
				if (((Stream)val).Length <= 0)
				{
					error = "empty file";
					return null;
				}
				if (((Stream)val).Length > num)
				{
					error = "file too large";
					return null;
				}
				byte[] array = new byte[((Stream)val).Length];
				int i;
				int num2;
				for (i = 0; i < array.Length; i += num2)
				{
					num2 = ((Stream)val).Read(array, i, array.Length - i);
					if (num2 <= 0)
					{
						break;
					}
				}
				if (i != array.Length)
				{
					error = "short read (half-written file?)";
					return null;
				}
				return array;
			}
			finally
			{
				((System.IDisposable)val)?.Dispose();
			}
		}
		catch (System.Exception ex)
		{
			error = ((MemberInfo)ex.GetType()).Name;
			return null;
		}
	}
}
