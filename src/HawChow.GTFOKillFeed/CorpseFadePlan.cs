namespace HawChow.GTFOKillFeed;

internal readonly struct CorpseFadePlan
{
	internal readonly bool FadeEnabled;

	internal readonly bool CorpseEffects;

	internal readonly bool InLevel;

	internal readonly bool Paused;

	internal readonly bool PlayPatched;

	internal readonly bool PrefabSafe;

	internal readonly CorpseFadePoolState Pool;

	internal readonly bool HaveRenderer;

	internal readonly bool OffScene;

	internal readonly int ActiveFades;

	internal CorpseFadePlan(bool fadeEnabled, bool corpseEffects, bool inLevel, bool paused, bool playPatched, bool prefabSafe, CorpseFadePoolState pool, bool haveRenderer, bool offScene, int activeFades)
	{
		FadeEnabled = fadeEnabled;
		CorpseEffects = corpseEffects;
		InLevel = inLevel;
		Paused = paused;
		PlayPatched = playPatched;
		PrefabSafe = prefabSafe;
		Pool = pool;
		HaveRenderer = haveRenderer;
		OffScene = offScene;
		ActiveFades = activeFades;
	}
}
