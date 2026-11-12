using System;

namespace HawChow.GTFOKillFeed;

internal struct KillIconSlot
{
	internal System.IntPtr Pointer;

	internal float X;

	internal float Y;

	internal float Z;

	internal float Clearance;

	internal KillTier Tier;

	internal KillFlight Flight;

	internal float At;

	internal bool Preview;

	internal int Token;
}
