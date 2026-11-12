namespace HawChow.GTFOKillFeed;

internal readonly struct KillDecision
{
	internal readonly bool Credited;

	internal readonly KillKind Kind;

	internal KillDecision(bool credited, KillKind kind)
	{
		Credited = credited;
		Kind = kind;
	}
}
