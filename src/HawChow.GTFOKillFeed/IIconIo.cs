using System;

namespace HawChow.GTFOKillFeed;

internal interface IIconIo
{
	bool TryProbe(string path, out System.DateTime writeUtc, out long length, out string error);

	byte[] TryRead(string path, long maximumBytes, out string error);
}
