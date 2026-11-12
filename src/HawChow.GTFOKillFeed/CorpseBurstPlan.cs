namespace HawChow.GTFOKillFeed;

internal readonly struct CorpseBurstPlan
{
	internal readonly int Bursts;

	internal readonly bool Sound;

	internal CorpseBurstPlan(int bursts, bool sound)
	{
		Bursts = bursts;
		Sound = sound;
	}
}
