namespace HawChow.GTFOKillFeed;

internal readonly struct CorpseSwitches
{
	internal readonly bool Effects;

	internal readonly bool Launch;

	internal readonly bool Dismember;

	internal readonly bool Chunks;

	internal readonly bool ExplosionFx;

	internal readonly bool BurstVisual;

	internal readonly bool BurstSound;

	internal CorpseSwitches(bool effects, bool launch, bool dismember, bool chunks, bool explosionFx, bool burstVisual, bool burstSound)
	{
		Effects = effects;
		Launch = launch;
		Dismember = dismember;
		Chunks = chunks;
		ExplosionFx = explosionFx;
		BurstVisual = burstVisual;
		BurstSound = burstSound;
	}

	internal bool LaunchActive => Effects && Launch;

	internal bool DismemberActive => Effects && Dismember;

	internal bool ChunksActive => Effects && Chunks;

	internal bool BurstVisualActive => Effects && ExplosionFx && BurstVisual;

	internal bool BurstSoundActive => Effects && ExplosionFx && BurstSound;

	internal bool AnyBurstActive => BurstVisualActive || BurstSoundActive;

	internal string Describe()
	{
		return "effects=" + Effects + ";launch=" + LaunchActive + ";dismember=" + DismemberActive + ";chunks=" + ChunksActive + ";burstFx=" + BurstVisualActive + ";burstSound=" + BurstSoundActive;
	}
}
