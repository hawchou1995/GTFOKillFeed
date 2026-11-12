namespace HawChow.GTFOKillFeed;

internal interface ICorpseFadeTarget
{
	CorpseFadeBorrow TryBorrow(out object handle);

	bool Initialize(object handle, object renderer);

	void Play(object handle, float seconds);

	CorpseFadeState Probe(object handle);

	void Cleanup(object handle);

	void Return(object handle);
}
