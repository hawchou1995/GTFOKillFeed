using System;

namespace HawChow.GTFOKillFeed;

internal static class CardPresentation
{
	internal static KillCard Resolve(KillCard card, Func<uint, string, string> nameResolver, float maxDistance)
	{
		if (card == null)
		{
			return null;
		}
		string displayName = card.EnemyName;
		if (nameResolver != null && !string.IsNullOrEmpty(card.RawName))
		{
			try
			{
				string text = nameResolver.Invoke(card.EnemyDataId, card.RawName);
				if (!string.IsNullOrWhiteSpace(text))
				{
					displayName = text;
				}
			}
			catch
			{
			}
		}
		return card.WithPresentation(displayName, maxDistance);
	}
}
