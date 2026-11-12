using System;
using System.Collections.Generic;

namespace HawChow.GTFOKillFeed;

internal sealed class KillCardQueue
{
	private readonly List<KillCard> _cards = new List<KillCard>();

	internal int Count => _cards.Count;

	internal bool Enqueue(KillCard card, float now, float lifetime, float fadeSeconds, int capacity)
	{
		if (card == null || capacity <= 0 || !KillPolicy.IsFinite(now))
		{
			return false;
		}
		Trim(now, lifetime, fadeSeconds);
		while (_cards.Count >= capacity)
		{
			_cards.RemoveAt(0);
		}
		_cards.Add(card);
		return true;
	}

	internal VisibleKillCard[] Snapshot(float now, float lifetime, float fadeSeconds, int capacity)
	{
		if (!KillPolicy.IsFinite(now))
		{
			return System.Array.Empty<VisibleKillCard>();
		}
		Trim(now, lifetime, fadeSeconds);
		int num = Math.Max(0, _cards.Count - Math.Max(0, capacity));
		VisibleKillCard[] array = new VisibleKillCard[_cards.Count - num];
		for (int i = num; i < _cards.Count; i++)
		{
			KillCard killCard = _cards[i];
			float expiresAt = KillCardPolicy.ExpiresAt(killCard.CreatedAt, killCard.IconToken, killCard.IconExpiresAt, lifetime, fadeSeconds);
			array[i - num] = new VisibleKillCard(killCard, KillCardPolicy.Alpha(now, expiresAt, fadeSeconds, lifetime));
		}
		return array;
	}

	internal void Clear()
	{
		_cards.Clear();
	}

	private void Trim(float now, float lifetime, float fadeSeconds)
	{
		for (int num = _cards.Count - 1; num >= 0; num--)
		{
			KillCard killCard = _cards[num];
			float num2 = KillCardPolicy.ExpiresAt(killCard.CreatedAt, killCard.IconToken, killCard.IconExpiresAt, lifetime, fadeSeconds);
			if (now >= num2)
			{
				_cards.RemoveAt(num);
			}
		}
	}
}
