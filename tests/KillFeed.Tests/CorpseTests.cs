using HawChow.GTFOKillFeed;

namespace KillFeed.Tests;

internal static class CorpseTests
{
	internal static void Run()
	{
		ParentChild();
		BurstPlanMatrix();
		SubSwitchIndependence();
	}

	private static CorpseSwitches All(bool value)
	{
		return new CorpseSwitches(value, value, value, value, value, value, value);
	}

	private static void ParentChild()
	{
		CorpseSwitches corpseSwitches = All(false);
		Harness.Check("corpse.parent.effects-off-disables-all", !corpseSwitches.LaunchActive && !corpseSwitches.DismemberActive && !corpseSwitches.ChunksActive && !corpseSwitches.BurstVisualActive && !corpseSwitches.BurstSoundActive && !corpseSwitches.AnyBurstActive);
		corpseSwitches = new CorpseSwitches(effects: true, launch: true, dismember: true, chunks: true, explosionFx: false, burstVisual: true, burstSound: true);
		Harness.Check("corpse.parent.explosionfx-off-disables-burst", !corpseSwitches.BurstVisualActive && !corpseSwitches.BurstSoundActive && !corpseSwitches.AnyBurstActive);
		Harness.Check("corpse.parent.explosionfx-off-keeps-launch-dismember-chunks", corpseSwitches.LaunchActive && corpseSwitches.DismemberActive && corpseSwitches.ChunksActive);
		corpseSwitches = All(true);
		Harness.Check("corpse.parent.all-on", corpseSwitches.LaunchActive && corpseSwitches.DismemberActive && corpseSwitches.ChunksActive && corpseSwitches.BurstVisualActive && corpseSwitches.BurstSoundActive);
	}

	private static void BurstPlanMatrix()
	{
		CorpseSwitches corpseSwitches = new CorpseSwitches(true, true, true, true, true, burstVisual: true, burstSound: true);
		CorpseBurstPlan corpseBurstPlan = FeedbackPolicy.BurstPlan(corpseSwitches, 1f, 1f);
		Harness.Check("corpse.burst.both-on.fx", corpseBurstPlan.Bursts > 0);
		Harness.Check("corpse.burst.both-on.sound", corpseBurstPlan.Sound);
		corpseSwitches = new CorpseSwitches(true, true, true, true, true, burstVisual: true, burstSound: false);
		corpseBurstPlan = FeedbackPolicy.BurstPlan(corpseSwitches, 1f, 1f);
		Harness.Check("corpse.burst.fx-only.fx", corpseBurstPlan.Bursts > 0);
		Harness.Check("corpse.burst.fx-only.no-sound", !corpseBurstPlan.Sound);
		corpseSwitches = new CorpseSwitches(true, true, true, true, true, burstVisual: false, burstSound: true);
		corpseBurstPlan = FeedbackPolicy.BurstPlan(corpseSwitches, 1f, 1f);
		Harness.Eq("corpse.burst.sound-only.no-fx", 0, corpseBurstPlan.Bursts);
		Harness.Check("corpse.burst.sound-only.sound", corpseBurstPlan.Sound);
		corpseSwitches = new CorpseSwitches(true, true, true, true, true, burstVisual: false, burstSound: false);
		corpseBurstPlan = FeedbackPolicy.BurstPlan(corpseSwitches, 1f, 1f);
		Harness.Eq("corpse.burst.both-off.no-fx", 0, corpseBurstPlan.Bursts);
		Harness.Check("corpse.burst.both-off.no-sound", !corpseBurstPlan.Sound);
		corpseSwitches = new CorpseSwitches(true, true, true, true, false, true, true);
		corpseBurstPlan = FeedbackPolicy.BurstPlan(corpseSwitches, 1f, 1f);
		Harness.Eq("corpse.burst.legacy-explosionfx-off.no-fx", 0, corpseBurstPlan.Bursts);
		Harness.Check("corpse.burst.legacy-explosionfx-off.no-sound", !corpseBurstPlan.Sound);
		corpseSwitches = All(true);
		Harness.Eq("corpse.burst.flush-gate.on", true, FeedbackPolicy.ShouldFlushCorpseSound(corpseSwitches.BurstSoundActive));
		corpseSwitches = new CorpseSwitches(true, true, true, true, true, true, false);
		Harness.Eq("corpse.burst.flush-gate.sound-off", false, FeedbackPolicy.ShouldFlushCorpseSound(corpseSwitches.BurstSoundActive));
	}

	private static void SubSwitchIndependence()
	{
		CorpseSwitches corpseSwitches = All(true);
		Harness.Check("corpse.launch.applies", FeedbackPolicy.ShouldApplyLaunch(corpseSwitches.LaunchActive, launchClosed: false, age: 0.02f));
		Harness.Check("corpse.launch.limits-ragdoll", FeedbackPolicy.ShouldLimitRagdoll(corpseSwitches.LaunchActive, offSceneCleanupApplied: false));
		corpseSwitches = new CorpseSwitches(true, false, true, true, true, true, true);
		Harness.Check("corpse.launch.off-no-launch", !FeedbackPolicy.ShouldApplyLaunch(corpseSwitches.LaunchActive, launchClosed: false, age: 5f));
		Harness.Check("corpse.launch.off-no-ragdoll-limit", !FeedbackPolicy.ShouldLimitRagdoll(corpseSwitches.LaunchActive, offSceneCleanupApplied: false));
		Harness.Check("corpse.launch.off-keeps-dismemberment", FeedbackPolicy.ShouldBreakCorpseLimbs(offSceneCleanupApplied: false, corpseSwitches.DismemberActive));
		Harness.Check("corpse.launch.off-keeps-chunks", FeedbackPolicy.ShouldTrackChunks(corpseSwitches.ChunksActive));
		corpseSwitches = new CorpseSwitches(true, true, false, true, true, true, true);
		Harness.Check("corpse.dismember.off-no-break", !FeedbackPolicy.ShouldBreakCorpseLimbs(offSceneCleanupApplied: false, corpseSwitches.DismemberActive));
		Harness.Check("corpse.dismember.off-keeps-launch", FeedbackPolicy.ShouldApplyLaunch(corpseSwitches.LaunchActive, launchClosed: false, age: 0.5f));
		corpseSwitches = new CorpseSwitches(true, true, true, false, true, true, true);
		Harness.Check("corpse.chunks.off-no-tracking", !FeedbackPolicy.ShouldTrackChunks(corpseSwitches.ChunksActive));
		Harness.Check("corpse.chunks.off-keeps-dismemberment", FeedbackPolicy.ShouldBreakCorpseLimbs(offSceneCleanupApplied: false, corpseSwitches.DismemberActive));
		Harness.Check("corpse.chunks.off-keeps-burst", FeedbackPolicy.BurstPlan(corpseSwitches, 1f, 1f).Bursts > 0);
		Harness.Check("corpse.offscene.still-blocks-break", !FeedbackPolicy.ShouldBreakCorpseLimbs(offSceneCleanupApplied: true, dismemberActive: true));
		Harness.Check("corpse.launch.closed-no-retry", !FeedbackPolicy.ShouldApplyLaunch(launchActive: true, launchClosed: true, age: 9f));
		Harness.Check("corpse.launch.too-early", !FeedbackPolicy.ShouldApplyLaunch(launchActive: true, launchClosed: false, age: 0.01f));
		Harness.Check("corpse.launch.disabled-no-unavailable-log", !FeedbackPolicy.ShouldReportUnavailableLaunch(applications: 0, alreadyReported: false, launchActive: false));
		Harness.Check("corpse.launch.enabled-reports-missing-ragdoll", FeedbackPolicy.ShouldReportUnavailableLaunch(applications: 0, alreadyReported: false, launchActive: true));
		Harness.Check("corpse.launch.report-once", !FeedbackPolicy.ShouldReportUnavailableLaunch(applications: 0, alreadyReported: true, launchActive: true));
		Harness.Check("corpse.launch.applied-never-reports", !FeedbackPolicy.ShouldReportUnavailableLaunch(applications: 2, alreadyReported: false, launchActive: true));
	}
}
