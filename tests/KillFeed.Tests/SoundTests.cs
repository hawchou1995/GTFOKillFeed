using System;
using HawChow.GTFOKillFeed;

namespace KillFeed.Tests;

internal static class SoundTests
{
	internal static void Run()
	{
		NewInstallDefaults();
		SwitchCombinations();
		VolumeEdges();
		Throttle();
		VanillaMuteIndependence();
		MasterGain();
	}

	private static void NewInstallDefaults()
	{
		Harness.Eq("sound.const.hit-default-off", false, HitSoundPolicy.DefaultHitEnabled);
		Harness.Eq("sound.const.kill-default-off", false, HitSoundPolicy.DefaultKillEnabled);
		Harness.Eq("sound.const.plan-new-install-channel", false, SoundConfigPlan.NewInstallChannelEnabled);
		Harness.Near("sound.const.plan-channel-volume", 0.4f, SoundConfigPlan.DefaultChannelVolume);
		Harness.Eq("sound.const.plan-legacy-master", true, SoundConfigPlan.LegacyMasterDefault);
		Harness.Near("sound.const.plan-legacy-volume", 0.8f, SoundConfigPlan.LegacyVolumeDefault);
		Harness.Near("sound.const.hit-volume", 0.4f, HitSoundPolicy.DefaultHitVolume);
		Harness.Near("sound.const.kill-volume", 0.4f, HitSoundPolicy.DefaultKillVolume);
		SoundConfigPlan soundConfigPlan = SoundConfigPlan.FromText(null, fileExisted: false);
		Harness.Eq("sound.plan.missing-file-is-new-install", SoundConfigMode.NewInstall, soundConfigPlan.Mode);
		SoundSettings soundSettings = soundConfigPlan.Defaults();
		Harness.Eq("sound.newinstall.master-on", true, soundSettings.Master);
		Harness.Eq("sound.newinstall.hit-off", false, soundSettings.HitEnabled);
		Harness.Eq("sound.newinstall.kill-off", false, soundSettings.KillEnabled);
		Harness.Near("sound.newinstall.hitvolume", 0.4f, soundSettings.HitVolume);
		Harness.Near("sound.newinstall.killvolume", 0.4f, soundSettings.KillVolume);
		Harness.Check("sound.newinstall.silent", !soundSettings.HitPlays && !soundSettings.KillPlays && !soundSettings.AnyPlays);
	}

	private static void SwitchCombinations()
	{
		SoundSettings soundSettings = SoundSettings.Resolve(master: true, hitEnabled: false, killEnabled: false, hitVolume: 0.4f, killVolume: 0.4f, masterVolume: 0.8f, channelFallback: 0.4f);
		Harness.Check("sound.combo.off-off.none", !soundSettings.HitPlays && !soundSettings.KillPlays);
		soundSettings = SoundSettings.Resolve(master: true, hitEnabled: true, killEnabled: false, hitVolume: 0.4f, killVolume: 0.4f, masterVolume: 0.8f, channelFallback: 0.4f);
		Harness.Check("sound.combo.on-off.hit-only", soundSettings.HitPlays && !soundSettings.KillPlays);
		soundSettings = SoundSettings.Resolve(master: true, hitEnabled: false, killEnabled: true, hitVolume: 0.4f, killVolume: 0.4f, masterVolume: 0.8f, channelFallback: 0.4f);
		Harness.Check("sound.combo.off-on.kill-only", !soundSettings.HitPlays && soundSettings.KillPlays);
		soundSettings = SoundSettings.Resolve(master: true, hitEnabled: true, killEnabled: true, hitVolume: 0.4f, killVolume: 0.4f, masterVolume: 0.8f, channelFallback: 0.4f);
		Harness.Check("sound.combo.on-on.both", soundSettings.HitPlays && soundSettings.KillPlays);
		soundSettings = SoundSettings.Resolve(master: false, hitEnabled: true, killEnabled: true, hitVolume: 0.4f, killVolume: 0.4f, masterVolume: 0.8f, channelFallback: 0.4f);
		Harness.Check("sound.combo.master-off.none", !soundSettings.HitPlays && !soundSettings.KillPlays);
		soundSettings = SoundSettings.Resolve(master: true, hitEnabled: true, killEnabled: true, hitVolume: 0f, killVolume: 0.7f, masterVolume: 0.8f, channelFallback: 0.4f);
		Harness.Check("sound.volume.hit-zero-mutes-only-hit", !soundSettings.HitPlays && soundSettings.KillPlays);
		soundSettings = SoundSettings.Resolve(master: true, hitEnabled: true, killEnabled: true, hitVolume: 0.7f, killVolume: 0f, masterVolume: 0.8f, channelFallback: 0.4f);
		Harness.Check("sound.volume.kill-zero-mutes-only-kill", soundSettings.HitPlays && !soundSettings.KillPlays);
		soundSettings = SoundSettings.Resolve(master: true, hitEnabled: true, killEnabled: true, hitVolume: 0.25f, killVolume: 0.9f, masterVolume: 0.8f, channelFallback: 0.4f);
		Harness.Near("sound.volume.channels-kept-apart.hit", 0.25f, soundSettings.HitVolume);
		Harness.Near("sound.volume.channels-kept-apart.kill", 0.9f, soundSettings.KillVolume);
		Harness.Check("sound.channel.master-and", SoundSettings.ChannelEnabled(true, true) && !SoundSettings.ChannelEnabled(true, false) && !SoundSettings.ChannelEnabled(false, true) && !SoundSettings.ChannelEnabled(false, false));
	}

	private static void VolumeEdges()
	{
		Harness.Near("sound.volume.negative-clamps-to-zero", 0f, SoundSettings.Volume(-0.01f, 0.4f));
		Harness.Near("sound.volume.zero-kept", 0f, SoundSettings.Volume(0f, 0.4f));
		Harness.Near("sound.volume.one-kept", 1f, SoundSettings.Volume(1f, 0.4f));
		Harness.Near("sound.volume.above-one-clamps", 1f, SoundSettings.Volume(1.5f, 0.4f));
		Harness.Near("sound.volume.nan-falls-back-to-channel-default", 0.4f, SoundSettings.Volume(float.NaN, 0.4f));
		Harness.Near("sound.volume.positive-infinity-falls-back", 0.4f, SoundSettings.Volume(float.PositiveInfinity, 0.4f));
		Harness.Near("sound.volume.negative-infinity-falls-back", 0.4f, SoundSettings.Volume(float.NegativeInfinity, 0.4f));
		Harness.Near("sound.volume.legacy-fallback-kept", 0.8f, SoundSettings.Volume(float.NaN, 0.8f));
		Harness.Near("sound.volume.fallback-clamped", 1f, SoundSettings.Volume(float.NaN, 7f));
		SoundSettings soundSettings = SoundSettings.Resolve(true, true, true, float.NaN, float.NaN, 0.8f, 0.4f);
		Harness.Near("sound.resolve.nan-hit-volume", 0.4f, soundSettings.HitVolume);
		Harness.Near("sound.resolve.nan-kill-volume", 0.4f, soundSettings.KillVolume);
		Harness.Check("sound.resolve.nan-still-plays", soundSettings.HitPlays && soundSettings.KillPlays);
		Harness.Check("sound.interval.nan", Math.Abs(HitSoundPolicy.MinInterval(double.NaN) - 0.05) < 1e-9);
		Harness.Check("sound.interval.floor", Math.Abs(HitSoundPolicy.MinInterval(0.0) - 0.02) < 1e-9);
		Harness.Check("sound.interval.ceiling", Math.Abs(HitSoundPolicy.MinInterval(9.0) - 3.0) < 1e-9);
	}

	private static void Throttle()
	{
		HitSoundState hitSoundState = new HitSoundState();
		int plays = 0;
		Func<bool> func = () => { plays++; return true; };
		Harness.Check("sound.throttle.first-hit-plays", hitSoundState.TryPlay(kill: false, now: 10.0, interval: 0.05, play: func));
		Harness.Check("sound.throttle.second-hit-inside-interval", !hitSoundState.TryPlay(kill: false, now: 10.01, interval: 0.05, play: func));
		Harness.Check("sound.throttle.kill-channel-independent", hitSoundState.TryPlay(kill: true, now: 10.01, interval: 0.05, play: func));
		Harness.Check("sound.throttle.second-kill-inside-interval", !hitSoundState.TryPlay(kill: true, now: 10.02, interval: 0.05, play: func));
		Harness.Check("sound.throttle.hit-after-interval", hitSoundState.TryPlay(kill: false, now: 10.06, interval: 0.05, play: func));
		Harness.Eq("sound.throttle.play-count", 3, plays);
		Harness.Eq("sound.throttle.played-counter", 3, hitSoundState.Played);
		Harness.Check("sound.throttle.nan-now-blocked", !hitSoundState.TryPlay(kill: false, now: double.NaN, interval: 0.05, play: func));
		Harness.Check("sound.throttle.infinity-now-blocked", !hitSoundState.TryPlay(kill: true, now: double.PositiveInfinity, interval: 0.05, play: func));
		HitSoundState hitSoundState2 = new HitSoundState();
		int failed = 0;
		Func<bool> func2 = () => { failed++; return false; };
		Harness.Check("sound.throttle.failed-play-not-counted", !hitSoundState2.TryPlay(kill: false, now: 5.0, interval: 1.0, play: func2));
		Harness.Check("sound.throttle.failed-play-does-not-arm-interval", hitSoundState2.TryPlay(kill: false, now: 5.01, interval: 1.0, play: () => true));
		Harness.Eq("sound.throttle.failed-play-attempts", 1, failed);
		hitSoundState.Reset();
		Harness.Check("sound.throttle.reset-clears-hit", hitSoundState.TryPlay(kill: false, now: 10.061, interval: 3.0, play: () => true));
		Harness.Check("sound.throttle.null-delegate", !new HitSoundState().TryPlay(kill: false, now: 0.0, interval: 0.05, play: null));
	}

	private static void VanillaMuteIndependence()
	{
		uint[] mutedIds = new uint[2] { 11u, 22u };
		Harness.Check("sound.mute.default-false", !HitSoundPolicy.MuteRequested(null));
		Harness.Check("sound.mute.false-does-not-mute", !HitSoundPolicy.ShouldMute(false, mutedIds, 11u));
		Harness.Check("sound.mute.true-mutes-listed-id", HitSoundPolicy.ShouldMute(true, mutedIds, 11u));
		Harness.Check("sound.mute.true-leaves-other-id", !HitSoundPolicy.ShouldMute(true, mutedIds, 33u));
		Harness.Check("sound.mute.empty-set-never-mutes", !HitSoundPolicy.ShouldMute(true, Array.Empty<uint>(), 11u));
		SoundSettings soundSettings = SoundSettings.Resolve(true, true, true, 0.4f, 0.4f, 0.8f, 0.4f);
		Harness.Check("sound.mute.custom-unaffected-by-vanilla-mute", soundSettings.HitPlays && soundSettings.KillPlays && soundSettings.HitVolume == 0.4f && soundSettings.KillVolume == 0.4f);
		Harness.Check("sound.mute.state-independent-of-channels", !HitSoundPolicy.MuteRequested(false) && !HitSoundPolicy.MuteRequested(null));
	}

	private static void MasterGain()
	{
		Harness.Near("sound.gain.unity-at-0.8", 1f, SoundSettings.Gain(0.8f));
		Harness.Near("sound.gain.legacy-0.3", 0.375f, SoundSettings.Gain(0.3f));
		Harness.Near("sound.gain.nan-is-unity", 1f, SoundSettings.Gain(float.NaN));
		Harness.Near("sound.gain.over-range", 1.25f, SoundSettings.Gain(2f));
		Harness.Near("sound.gain.zero", 0f, SoundSettings.Gain(0f));
		SoundSettings migrated = SoundSettings.Resolve(true, true, true, 0.8f, 0.8f, 0.3f, 0.8f);
		Harness.Near("sound.master.migrated-0.3-hit", 0.3f, migrated.HitVolume);
		Harness.Near("sound.master.migrated-0.3-kill", 0.3f, migrated.KillVolume);
		Harness.Check("sound.master.migrated-0.3-plays", migrated.HitPlays && migrated.KillPlays);
		SoundSettings turnedUp = SoundSettings.Resolve(true, true, true, 0.8f, 0.8f, 0.6f, 0.8f);
		Harness.Near("sound.master.0.6-drives-both-hit", 0.6f, turnedUp.HitVolume);
		Harness.Near("sound.master.0.6-drives-both-kill", 0.6f, turnedUp.KillVolume);
		SoundSettings newInstall = SoundSettings.Resolve(true, true, true, 0.4f, 0.4f, 0.8f, 0.4f);
		Harness.Near("sound.master.new-install-0.4-hit", 0.4f, newInstall.HitVolume);
		Harness.Near("sound.master.new-install-0.4-kill", 0.4f, newInstall.KillVolume);
		SoundSettings independent = SoundSettings.Resolve(true, true, true, 0.4f, 0.8f, 0.6f, 0.4f);
		Harness.Near("sound.master.independent-hit", 0.3f, independent.HitVolume);
		Harness.Near("sound.master.independent-kill", 0.6f, independent.KillVolume);
		SoundSettings clamped = SoundSettings.Resolve(true, true, true, 1f, 0.8f, 1f, 0.4f);
		Harness.Near("sound.master.clamped-hit", 1f, clamped.HitVolume);
		Harness.Near("sound.master.clamped-kill", 1f, clamped.KillVolume);
		SoundSettings silent = SoundSettings.Resolve(true, true, true, 0.8f, 0.8f, 0f, 0.8f);
		Harness.Check("sound.master.zero-silences-both", !silent.HitPlays && !silent.KillPlays && silent.HitVolume == 0f && silent.KillVolume == 0f);
		SoundSettings legacyNan = SoundSettings.Resolve(true, true, true, float.NaN, float.NaN, 0.4f, 0.8f);
		Harness.Near("sound.master.legacy-nan-channel-uses-unity-seed", 0.4f, legacyNan.HitVolume);
		SoundSettings masterNan = SoundSettings.Resolve(true, true, true, 0.4f, 0.4f, float.NaN, 0.4f);
		Harness.Near("sound.master.nan-master-is-unity", 0.4f, masterNan.HitVolume);
	}
}
