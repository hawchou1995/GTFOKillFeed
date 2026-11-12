namespace HawChow.GTFOKillFeed;

internal readonly struct VisibleKillCard
{
	internal readonly KillCard Card;

	internal readonly float Alpha;

	internal VisibleKillCard(KillCard card, float alpha)
	{
		Card = card;
		Alpha = alpha;
	}
}
