using HawChow.GTFOKillFeed;

namespace KillFeed.Tests;

internal static class MarkerTests
{
	internal static void Run()
	{
		Combinations();
	}

	private static void Combinations()
	{
		Harness.Check("marker.channel.master-off-hides-all", !HitMarkerPolicy.ChannelEnabled(false, true) && !HitMarkerPolicy.ChannelEnabled(false, false));
		Harness.Check("marker.channel.hit-only", HitMarkerPolicy.ChannelEnabled(true, true) && !HitMarkerPolicy.ChannelEnabled(true, false));
		Harness.Eq("marker.kind.kill-uses-kill-switch", false, HitMarkerPolicy.ChannelForKind(hitEnabled: true, killEnabled: false, HitMarkerKind.Kill));
		Harness.Eq("marker.kind.hit-uses-hit-switch", false, HitMarkerPolicy.ChannelForKind(hitEnabled: false, killEnabled: true, HitMarkerKind.Normal));
		Harness.Eq("marker.kind.weakspot-uses-hit-switch", false, HitMarkerPolicy.ChannelForKind(hitEnabled: false, killEnabled: true, HitMarkerKind.Weakspot));
		Harness.Eq("marker.kind.armor-uses-hit-switch", false, HitMarkerPolicy.ChannelForKind(hitEnabled: false, killEnabled: true, HitMarkerKind.Armor));
		Harness.Eq("marker.kind.kill-with-kill-switch", true, HitMarkerPolicy.ChannelForKind(hitEnabled: false, killEnabled: true, HitMarkerKind.Kill));
		Harness.Check("marker.kind.hit-swatch-both-on", HitMarkerPolicy.ChannelForKind(true, true, HitMarkerKind.Normal) && HitMarkerPolicy.ChannelForKind(true, true, HitMarkerKind.Kill));
		Harness.Check("marker.kind.both-off", !HitMarkerPolicy.ChannelForKind(false, false, HitMarkerKind.Normal) && !HitMarkerPolicy.ChannelForKind(false, false, HitMarkerKind.Kill));
		Harness.Check("marker.legacy-defaults-true", HitMarkerPolicy.DefaultEnabled);
		Harness.Check("marker.submit.none-rejected", !HitMarkerPolicy.AcceptsSubmission(channelEnabled: true, HitMarkerKind.None));
		Harness.Check("marker.submit.none-rejected-channel-off", !HitMarkerPolicy.AcceptsSubmission(channelEnabled: false, HitMarkerKind.None));
		Harness.Check("marker.submit.normal-accepted", HitMarkerPolicy.AcceptsSubmission(channelEnabled: true, HitMarkerKind.Normal));
		Harness.Check("marker.submit.weakspot-accepted", HitMarkerPolicy.AcceptsSubmission(channelEnabled: true, HitMarkerKind.Weakspot));
		Harness.Check("marker.submit.armor-accepted", HitMarkerPolicy.AcceptsSubmission(channelEnabled: true, HitMarkerKind.Armor));
		Harness.Check("marker.submit.kill-accepted", HitMarkerPolicy.AcceptsSubmission(channelEnabled: true, HitMarkerKind.Kill));
		Harness.Check("marker.submit.channel-off-rejects-kind", !HitMarkerPolicy.AcceptsSubmission(channelEnabled: false, HitMarkerKind.Normal) && !HitMarkerPolicy.AcceptsSubmission(channelEnabled: false, HitMarkerKind.Kill));
	}
}
