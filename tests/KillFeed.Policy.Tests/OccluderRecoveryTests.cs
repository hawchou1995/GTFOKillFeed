using System;

namespace HawChow.GTFOKillFeed.Tests;

// Recovery contract for the head icon occluder registry: a transient fault hides the
// icons for one frame, a registry overflow keeps them hidden until a bounded rebuild
// proves completeness again, and repeated capture failures back off instead of scanning
// the scene or enumerating colliders every frame.
internal static class OccluderRecoveryTests
{
	internal static void Run()
	{
		CapsMatchTheDocumentedContract();
		TransientCaptureFailureRecoversNextFrame();
		SaturatedCandidatesHideOneFrameThenRecover();
		ColliderCapHidesThenRecovers();
		RegistryOverflowStaysHiddenUntilBoundedRescan();
		SustainedCaptureFailuresBackOff();
		FailedSeedRetriesAreRateLimitedAndRecover();
		ResetAndLevelEnterClearRecoveryState();
		LegacyStickyControlWouldFailTheFix();
	}

	private static bool StepOnce(OccluderFrameDriver driver, int bufferSize)
	{
		int count;
		return driver.Step(1f / 60f, bufferSize, out count);
	}

	private static void CapsMatchTheDocumentedContract()
	{
		Program.Eq("registry cap", 96, KillOccluderRecovery.MaxEnemies);
		Program.Eq("collider cap", 48, KillOccluderRecovery.MaxCollidersPerEnemy);
		Program.Eq("capture retries before backoff", 2, KillOccluderRecovery.FreeCaptureRetries);
		Program.Near("first capture backoff", 0.25f, KillOccluderRecovery.CaptureRetryDelay(3));
		Program.Near("capture backoff cap", 2f, KillOccluderRecovery.CaptureRetryDelay(64));
		Program.Near("first rescan delay", 0.5f, KillOccluderRecovery.RescanDelay(1));
		Program.Near("rescan delay cap", 2f, KillOccluderRecovery.RescanDelay(64));
	}

	private static void TransientCaptureFailureRecoversNextFrame()
	{
		OccluderFrameDriver driver = new OccluderFrameDriver();
		OccluderFrameDriver.FakeEnemy first = driver.Spawn();
		driver.Spawn();
		first.CaptureThrows = true;
		Program.True("the first capture failure hides that frame", !StepOnce(driver, 96));
		Program.True("capture failure is frame-local", !driver.Recovery.FrameComplete);
		Program.True("capture failure does not poison the registry", driver.Recovery.RegistryComplete);
		first.CaptureThrows = false;
		Program.True("next frame recovers", StepOnce(driver, 96));
		Program.Eq("capture failure never triggers a scene rescan", 0, driver.Recovery.RescanAttempts);
		Program.Eq("registry still holds both enemies", 2, driver.Registry.Count);
	}

	private static void SaturatedCandidatesHideOneFrameThenRecover()
	{
		OccluderFrameDriver driver = new OccluderFrameDriver();
		driver.Spawn();
		driver.Spawn();
		OccluderFrameDriver.FakeEnemy third = driver.Spawn();
		Program.True("saturated buffer hides the frame", !StepOnce(driver, 2));
		Program.True("saturation is frame-local", !driver.Recovery.FrameComplete);
		Program.True("saturation does not poison the registry", driver.Recovery.RegistryComplete);
		driver.Despawn(third);
		Program.True("next frame with fewer candidates recovers", StepOnce(driver, 2));
		Program.Eq("saturation never triggers a scene rescan", 0, driver.Recovery.RescanAttempts);
	}

	private static void ColliderCapHidesThenRecovers()
	{
		OccluderFrameDriver driver = new OccluderFrameDriver();
		OccluderFrameDriver.FakeEnemy enemy = driver.Spawn();
		enemy.Colliders = KillOccluderRecovery.MaxCollidersPerEnemy + 1;
		Program.True("over-cap collider count hides the frame", !StepOnce(driver, 96));
		Program.True("over-cap collider count is frame-local", driver.Recovery.RegistryComplete && !driver.Recovery.FrameComplete);
		enemy.Colliders = 3;
		Program.True("collider count back in range recovers", StepOnce(driver, 96));
		Program.True("normal collider count stays complete", StepOnce(driver, 96));
		Program.Eq("collider cap never triggers a scene rescan", 0, driver.Recovery.RescanAttempts);
	}

	private static void RegistryOverflowStaysHiddenUntilBoundedRescan()
	{
		OccluderFrameDriver driver = new OccluderFrameDriver();
		OccluderFrameDriver.FakeEnemy[] enemies = new OccluderFrameDriver.FakeEnemy[KillOccluderRecovery.MaxEnemies];
		for (int i = 0; i < enemies.Length; i++)
		{
			enemies[i] = driver.Spawn();
		}
		Program.True("a full registry is still complete", StepOnce(driver, 96));
		Program.Eq("registry holds the cap", 96, driver.Registry.Count);
		OccluderFrameDriver.FakeEnemy overflow = driver.Spawn();
		Program.Eq("the 97th enemy is rejected", 96, driver.Registry.Count);
		Program.True("the 97th enemy marks the registry incomplete", !driver.Recovery.RegistryComplete);
		Program.True("the overflow frame hides", !StepOnce(driver, 96));
		int scansBefore = driver.Scans;
		bool hiddenWhileFull = true;
		for (int i = 0; i < 600; i++)
		{
			if (StepOnce(driver, 96))
			{
				hiddenWhileFull = false;
			}
		}
		Program.True("stays hidden while the live count exceeds the cap", hiddenWhileFull);
		Program.True("bounded retries run while still full", driver.Scans - scansBefore >= 2);
		Program.True("retries stay rate limited while still full", driver.Scans - scansBefore <= 8);
		driver.Despawn(overflow);
		driver.Despawn(enemies[0]);
		bool recovered = false;
		int framesToRecover = 0;
		for (int i = 0; i < 240; i++)
		{
			if (StepOnce(driver, 96))
			{
				recovered = true;
				framesToRecover = i + 1;
				break;
			}
		}
		Program.True("bounded rescan recovers after the live count drops", recovered);
		Program.True("recovery is bounded by the rescan backoff", framesToRecover > 0 && framesToRecover <= 180);
		Program.True("recovery clears the registry flag", driver.Recovery.RegistryComplete);
		int scansAfterRecovery = driver.Scans;
		bool stable = true;
		for (int i = 0; i < 120; i++)
		{
			stable &= StepOnce(driver, 96);
		}
		Program.True("stays visible after recovery", stable);
		Program.Eq("no rescans once the registry is complete again", scansAfterRecovery, driver.Scans);
	}

	private static void SustainedCaptureFailuresBackOff()
	{
		OccluderFrameDriver driver = new OccluderFrameDriver();
		OccluderFrameDriver.FakeEnemy enemy = driver.Spawn();
		enemy.CaptureThrows = true;
		bool hiddenThroughout = true;
		for (int i = 0; i < 120; i++)
		{
			hiddenThroughout &= !StepOnce(driver, 96);
		}
		Program.True("a persistently broken enemy keeps the icons hidden", hiddenThroughout);
		Program.True("capture retries back off instead of running every frame", driver.CaptureAttempts <= 8);
		Program.True("capture retries still happen while broken", driver.CaptureAttempts >= 4);
		enemy.CaptureThrows = false;
		bool recovered = false;
		int frames = 0;
		for (int i = 0; i < 180; i++)
		{
			frames = i + 1;
			if (StepOnce(driver, 96))
			{
				recovered = true;
				break;
			}
		}
		Program.True("recovery after sustained failures is bounded", recovered);
		Program.True("recovery happens within the capture backoff cap", frames <= 150);
		Program.True("recovery resets the frame flag", driver.Recovery.FrameComplete);
	}

	private static void FailedSeedRetriesAreRateLimitedAndRecover()
	{
		OccluderFrameDriver driver = new OccluderFrameDriver();
		driver.Spawn();
		driver.Spawn();
		driver.FailScans = 2;
		Program.True("a failed seed hides the frame", !StepOnce(driver, 96));
		Program.True("a failed seed leaves the registry incomplete", !driver.Recovery.RegistryComplete);
		int scansAtStart = driver.Scans;
		for (int i = 0; i < 24; i++)
		{
			StepOnce(driver, 96);
		}
		Program.Eq("no retry within the first rescan window", scansAtStart, driver.Scans);
		bool recovered = false;
		int frames = 0;
		for (int i = 0; i < 240; i++)
		{
			frames = i + 1;
			if (StepOnce(driver, 96))
			{
				recovered = true;
				break;
			}
		}
		Program.True("a failed seed recovers through the bounded rescan", recovered);
		Program.True("seed recovery stays rate limited", driver.Scans <= 4);
		Program.True("seed recovery is bounded in time", frames <= 180);
	}

	private static void ResetAndLevelEnterClearRecoveryState()
	{
		OccluderFrameDriver driver = new OccluderFrameDriver();
		for (int i = 0; i < KillOccluderRecovery.MaxEnemies + 1; i++)
		{
			driver.Spawn();
		}
		StepOnce(driver, 96);
		Program.True("overflow state is set before the reset", !driver.Recovery.RegistryComplete);
		driver.Reset();
		Program.True("reset forgets that the registry was seeded", !driver.Recovery.Seeded);
		Program.True("reset clears the registry flag", driver.Recovery.RegistryComplete);
		Program.True("reset clears the frame flag", driver.Recovery.FrameComplete);
		Program.True("reset clears the rescan schedule", !driver.Recovery.RescanPending);
		Program.Eq("reset clears the rescan counter", 0, driver.Recovery.RescanAttempts);
		Program.Eq("reset empties the registry", 0, driver.Registry.Count);
		OccluderFrameDriver fresh = new OccluderFrameDriver();
		fresh.Spawn();
		fresh.Spawn();
		Program.True("a fresh level seeds and completes", StepOnce(fresh, 96));
	}

	private static void LegacyStickyControlWouldFailTheFix()
	{
		LegacyStickyModel legacy = new LegacyStickyModel();
		legacy.Seed(2);
		legacy.CaptureFailed();
		for (int i = 0; i < 120; i++)
		{
			legacy.Frame(2);
		}
		Program.True("negative control: the old sticky flag never recovers", !legacy.Complete);

		OccluderFrameDriver driver = new OccluderFrameDriver();
		OccluderFrameDriver.FakeEnemy first = driver.Spawn();
		driver.Spawn();
		first.CaptureThrows = true;
		Program.True("production hides the faulty frame", !StepOnce(driver, 96));
		first.CaptureThrows = false;
		Program.True("production recovers on the next frame", StepOnce(driver, 96));
		bool stable = true;
		for (int i = 0; i < 120; i++)
		{
			stable &= StepOnce(driver, 96);
		}
		Program.True("production stays recovered after the transient fault", stable);
	}
}
