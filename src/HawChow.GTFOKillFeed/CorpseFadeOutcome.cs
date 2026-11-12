namespace HawChow.GTFOKillFeed;

internal enum CorpseFadeOutcome
{
	Started,
	StartedProbeUnknown,
	TargetMissing,
	ConcurrencyCap,
	InstanceBusy,
	InstanceTracked,
	InstanceGone,
	PoolMissing,
	NoFreeInstance,
	BorrowError,
	ProbeUnknown,
	InitializeFailed,
	InitializeThrew,
	PlayThrew,
	PlayNotObserved,
	Unexpected
}
