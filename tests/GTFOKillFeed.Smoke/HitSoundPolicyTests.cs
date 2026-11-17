using System;
using System.Collections.Generic;
using HawChow.GTFOKillFeed;

// The cue/mute rules without Unity: the whitelist must be exactly the three vanilla indicator events,
// the (style, hit kind, kill tier, flight) matrix must resolve to a real resource for every combination,
// a missing file must fall back along a stated chain without changing the tier, and the two switches stay
// independent.
internal static class HitSoundPolicyTests
{
    internal static void Run(Action<bool, string> check)
    {
        check(HitSoundPolicy.MutedEventNames.Length == 3, "hit-sound: the mute whitelist holds exactly three events");
        check(HitSoundPolicy.MutedEventNames[0] == "BULLETHITENEMYINDICATOR"
            && HitSoundPolicy.MutedEventNames[1] == "BULLETHITENEMYLOCALINDICATOR"
            && HitSoundPolicy.MutedEventNames[2] == "BULLETHITENEMYWEAKSPOTLOCALINDICATOR",
            "hit-sound: the whitelist is exactly the three indicator layers, in the documented order");

        // ---- 1.2.18: the known BUS-table name must not be reported as a broken event ---------------------
        // BULLETHITENEMYINDICATOR is an AK.BUSSES property in this build; the other two are AK.EVENTS event ids.
        // The resolution plan is the production code path (HitSound.ResolveMutedIds feeds it the compiled lookup).
        var knownEvents = new Dictionary<string, uint>(StringComparer.Ordinal)
        {
            ["BULLETHITENEMYLOCALINDICATOR"] = 22u,
            ["BULLETHITENEMYWEAKSPOTLOCALINDICATOR"] = 33u,
        };
        var twoOfThree = HitSoundPolicy.ResolveIndicators(HitSoundPolicy.MutedEventNames,
            name => knownEvents.TryGetValue(name, out uint known) ? (uint?)known : null);
        check(twoOfThree.Unresolved == 1 && twoOfThree.OptionalBusMissing && !twoOfThree.HasMissingEvent,
            "hit-sound: the known BULLETHITENEMYINDICATOR bus name missing from AK.EVENTS is expected - counted as unresolved, but it is NOT a missing event (no warning)");
        check(twoOfThree.Ids.Length == 2 && twoOfThree.ResolvedNames.Length == 2
            && Array.IndexOf(twoOfThree.Ids, 22u) >= 0 && Array.IndexOf(twoOfThree.Ids, 33u) >= 0
            && Array.IndexOf(twoOfThree.ResolvedNames, "BULLETHITENEMYINDICATOR") < 0,
            "hit-sound: only the two real AK.EVENTS ids enter the mute set - a bus name never becomes an event id");
        var missingRealEvent = HitSoundPolicy.ResolveIndicators(HitSoundPolicy.MutedEventNames,
            name => string.Equals(name, "BULLETHITENEMYWEAKSPOTLOCALINDICATOR", StringComparison.Ordinal) ? (uint?)7u : null);
        check(missingRealEvent.Unresolved == 2 && missingRealEvent.OptionalBusMissing
            && missingRealEvent.MissingEvents.Length == 1
            && missingRealEvent.MissingEvents[0] == "BULLETHITENEMYLOCALINDICATOR",
            "hit-sound: a missing REAL event id is warned about by name, while the unresolved bus name stays info-only in the same pass");
        var promotedButRealMissing = HitSoundPolicy.ResolveIndicators(HitSoundPolicy.MutedEventNames,
            name => string.Equals(name, "BULLETHITENEMYINDICATOR", StringComparison.Ordinal) ? (uint?)9u : null);
        check(!promotedButRealMissing.OptionalBusMissing && promotedButRealMissing.HasMissingEvent
            && promotedButRealMissing.Unresolved == 2 && promotedButRealMissing.MissingEvents.Length == 2
            && Array.IndexOf(promotedButRealMissing.MissingEvents, "BULLETHITENEMYLOCALINDICATOR") >= 0
            && Array.IndexOf(promotedButRealMissing.MissingEvents, "BULLETHITENEMYWEAKSPOTLOCALINDICATOR") >= 0
            && Array.IndexOf(promotedButRealMissing.Ids, 9u) >= 0,
            "hit-sound: once the bus name is a real event the other two missing events are the only warning, and the promoted name is muted like any event");
        var allResolved = HitSoundPolicy.ResolveIndicators(HitSoundPolicy.MutedEventNames, name => (uint)(name.Length));
        check(allResolved.Unresolved == 0 && !allResolved.OptionalBusMissing && !allResolved.HasMissingEvent
            && allResolved.Ids.Length == 3 && allResolved.ResolvedNames.Length == 3,
            "hit-sound: if a future build promotes the third name to a real event it is resolved and muted with the other two, with no code change");
        var noneResolved = HitSoundPolicy.ResolveIndicators(HitSoundPolicy.MutedEventNames, name => null);
        check(noneResolved.Ids.Length == 0 && noneResolved.Unresolved == 3 && noneResolved.OptionalBusMissing
            && noneResolved.MissingEvents.Length == 2,
            "hit-sound: an AK.EVENTS table that resolves none of them mutes nothing, reports three unresolved and warns about exactly the two real events");
        check(HitSoundPolicy.IsKnownOptionalBusIndicator("BULLETHITENEMYINDICATOR")
            && !HitSoundPolicy.IsKnownOptionalBusIndicator("BULLETHITENEMYLOCALINDICATOR")
            && !HitSoundPolicy.IsKnownOptionalBusIndicator("BULLETHITENEMYWEAKSPOTLOCALINDICATOR")
            && !HitSoundPolicy.IsKnownOptionalBusIndicator(null),
            "hit-sound: exactly one of the three names is the known optional bus property - the other two are always treated as events");

        var body = new HashSet<string>(HitSoundPolicy.MutedEventNames);
        check(!body.Contains("BULLETHITENEMYLOCAL"), "hit-sound: the local body-impact event stays audible");
        check(!body.Contains("BULLETHITENEMYSYNC"), "hit-sound: the synced body-impact event stays audible");
        check(!body.Contains("BULLETHITENEMYWEAKSPOTLOCAL"), "hit-sound: the local weakspot impact event stays audible");
        check(!body.Contains("BULLETHITENEMYWEAKSPOTSYNC"), "hit-sound: the synced weakspot impact event stays audible");
        check(!body.Contains("ENEMYVOICEDEATH") && !body.Contains("MELEE_HIT") && !body.Contains("GLUEGUN_SHOT"),
            "hit-sound: an arbitrary other Wwise event is never in the whitelist");

        // ---- shipped switches ---------------------------------------------------------------
        check(HitSoundPolicy.DefaultEnabled, "hit-sound: 1.2.16 ships with the cue player enabled by default");
        check(!HitSoundPolicy.DefaultMuteVanillaIndicator,
            "hit-sound: the vanilla indicator stays audible by default (MuteVanillaIndicator=false)");
        check(HitSoundPolicy.DefaultEnabled && !HitSoundPolicy.DefaultMuteVanillaIndicator,
            "hit-sound: the two defaults are independent - enabling the cues does not mute the vanilla layer");

        check(HitSoundPolicy.ParseStyle("BF1") == HitSoundStyle.Bf1, "hit-sound: BF1 selects the BF1 style");
        check(HitSoundPolicy.ParseStyle("BF5") == HitSoundStyle.Bf5, "hit-sound: BF5 selects the BF5 style");
        check(HitSoundPolicy.ParseStyle("BF6") == HitSoundStyle.Bf6, "hit-sound: BF6 selects the BF6 style");
        check(HitSoundPolicy.ParseStyle("bf6") == HitSoundStyle.Bf6, "hit-sound: the style token is case insensitive");
        check(HitSoundPolicy.ParseStyle("  BF1 ") == HitSoundStyle.Bf1, "hit-sound: surrounding whitespace is trimmed");
        check(HitSoundPolicy.ParseStyle("BF7") == HitSoundStyle.Bf5, "hit-sound: an unknown style falls back to BF5");
        check(HitSoundPolicy.ParseStyle("") == HitSoundStyle.Bf5 && HitSoundPolicy.ParseStyle(null) == HitSoundStyle.Bf5,
            "hit-sound: empty or missing style falls back to BF5");

        check(Math.Abs(HitSoundPolicy.Volume(float.NaN) - 0.8f) < 0.0001f, "hit-sound: NaN volume falls back to 0.8");
        check(Math.Abs(HitSoundPolicy.Volume(float.PositiveInfinity) - 0.8f) < 0.0001f, "hit-sound: infinite volume falls back to 0.8");
        check(HitSoundPolicy.Volume(-1f) == 0f, "hit-sound: a negative volume clamps to 0");
        check(HitSoundPolicy.Volume(2f) == 1f, "hit-sound: an over-range volume clamps to 1");
        check(Math.Abs(HitSoundPolicy.Volume(0.5f) - 0.5f) < 0.0001f, "hit-sound: an in-range volume is used as-is");

        check(Math.Abs(HitSoundPolicy.MinInterval(double.NaN) - 0.05) < 1e-9, "hit-sound: NaN interval falls back to 0.05");
        check(Math.Abs(HitSoundPolicy.MinInterval(0.0) - 0.02) < 1e-9, "hit-sound: a tiny interval clamps to the 0.02 floor");
        check(Math.Abs(HitSoundPolicy.MinInterval(10.0) - 3.0) < 1e-9, "hit-sound: a large interval clamps to the 3.0 ceiling");
        check(Math.Abs(HitSoundPolicy.MinInterval(0.05) - 0.05) < 1e-9, "hit-sound: an in-range interval is used as-is");

        // ---- hit kinds ---------------------------------------------------------------------
        check(HitSoundPolicy.KindOf(HitKind.Normal, false) == HitCueKind.Hit, "hit-sound: a normal hit selects the hit cue");
        check(HitSoundPolicy.KindOf(HitKind.Weakspot, false) == HitCueKind.WeakspotHit, "hit-sound: a weakspot hit selects the weakspot cue");
        check(HitSoundPolicy.KindOf(HitKind.Armor, false) == HitCueKind.ArmorHit, "hit-sound: an armor hit selects the armor cue");
        check(HitSoundPolicy.KindOf(HitKind.Normal, true) == HitCueKind.Kill
            && HitSoundPolicy.KindOf(HitKind.Weakspot, true) == HitCueKind.Kill
            && HitSoundPolicy.KindOf(HitKind.Armor, true) == HitCueKind.Kill,
            "hit-sound: a kill always selects the kill cue, whatever limb landed it");

        // ---- resource names ----------------------------------------------------------------
        check(HitSoundPolicy.ResourceName(HitSoundStyle.Bf5, HitCueKind.Hit) == "KillFeed.Audio.hit-bf5-hit.wav",
            "hit-sound: the BF5 hit cue resource name is the shipped one");
        check(HitSoundPolicy.ResourceName(HitSoundStyle.Bf1, HitCueKind.Hit) == "KillFeed.Audio.hit-bf1-hit.wav",
            "hit-sound: the BF1 hit cue keeps its own take");
        check(HitSoundPolicy.ResourceName(HitSoundStyle.Bf6, HitCueKind.Hit) == HitSoundPolicy.ResourceName(HitSoundStyle.Bf5, HitCueKind.Hit),
            "hit-sound: BF6 has no hit take of its own, so it collapses onto the BF5 file name instead of a duplicate");
        check(HitSoundPolicy.ResourceName(HitSoundStyle.Bf1, HitCueKind.WeakspotHit) == "KillFeed.Audio.hit-bf5-weakspot.wav"
            && HitSoundPolicy.ResourceName(HitSoundStyle.Bf6, HitCueKind.WeakspotHit) == "KillFeed.Audio.hit-bf5-weakspot.wav",
            "hit-sound: the derived weakspot cue is one file, not three copies behind different names");
        check(HitSoundPolicy.ResourceName(HitSoundStyle.Bf1, HitCueKind.ArmorHit) == "KillFeed.Audio.hit-bf5-armor.wav"
            && HitSoundPolicy.ResourceName(HitSoundStyle.Bf5, HitCueKind.ArmorHit) == "KillFeed.Audio.hit-bf5-armor.wav",
            "hit-sound: the derived armor cue is a single resource for every style");
        check(HitSoundPolicy.ResourceName(HitSoundStyle.Bf5, HitCueKind.Kill, KillTier.Minor, KillFlight.None) == "KillFeed.Audio.hit-bf5-kill-minor.wav"
            && HitSoundPolicy.ResourceName(HitSoundStyle.Bf5, HitCueKind.Kill, KillTier.Elite, KillFlight.None) == "KillFeed.Audio.hit-bf5-kill-elite.wav"
            && HitSoundPolicy.ResourceName(HitSoundStyle.Bf5, HitCueKind.Kill, KillTier.Boss, KillFlight.None) == "KillFeed.Audio.hit-bf5-kill-boss.wav",
            "hit-sound: the three kill tiers name three different cues");
        check(HitSoundPolicy.ResourceName(HitSoundStyle.Bf1, HitCueKind.Kill, KillTier.Minor, KillFlight.None) == "KillFeed.Audio.hit-bf1-kill-minor.wav"
            && HitSoundPolicy.ResourceName(HitSoundStyle.Bf6, HitCueKind.Kill, KillTier.Elite, KillFlight.None) == "KillFeed.Audio.hit-bf6-kill-elite.wav"
            && HitSoundPolicy.ResourceName(HitSoundStyle.Bf1, HitCueKind.Kill, KillTier.Boss, KillFlight.None) == "KillFeed.Audio.hit-bf5-kill-boss.wav",
            "hit-sound: styles keep their own light/medium takes, while the single heavy take collapses onto bf5");
        check(HitSoundPolicy.ResourceName(HitSoundStyle.Bf5, HitCueKind.Kill, KillTier.Minor, KillFlight.Flying) == "KillFeed.Audio.hit-bf5-kill-flyer-minor.wav"
            && HitSoundPolicy.ResourceName(HitSoundStyle.Bf5, HitCueKind.Kill, KillTier.Minor, KillFlight.ArmoredFlying) == "KillFeed.Audio.hit-bf5-kill-armor-flyer-minor.wav",
            "hit-sound: flying and armored-flying kills are separate cues and still carry their tier");
        check(HitSoundPolicy.ResourceName(HitSoundStyle.Bf5, HitCueKind.Kill, KillTier.Boss, KillFlight.ArmoredFlying) == "KillFeed.Audio.hit-bf5-kill-armor-flyer-boss.wav",
            "hit-sound: a boss armored flyer names the boss tier explicitly");

        // ---- the full matrix ---------------------------------------------------------------
        var styles = new[] { HitSoundStyle.Bf1, HitSoundStyle.Bf5, HitSoundStyle.Bf6 };
        var kinds = new[] { HitCueKind.Hit, HitCueKind.WeakspotHit, HitCueKind.ArmorHit, HitCueKind.Kill };
        var tiers = new[] { KillTier.Minor, KillTier.Elite, KillTier.Boss };
        var flights = new[] { KillFlight.None, KillFlight.Flying, KillFlight.ArmoredFlying };
        var names = new HashSet<string>();
        int combos = 0;
        bool chainsValid = true;
        string firstBadChain = string.Empty;
        foreach (var style in styles)
        {
            foreach (var kind in kinds)
            {
                foreach (var tier in tiers)
                {
                    foreach (var flight in flights)
                    {
                        combos++;
                        string name = HitSoundPolicy.ResourceName(style, kind, tier, flight);
                        names.Add(name);
                        string[] chain = HitSoundPolicy.CueChain(style, kind, tier, flight);
                        var distinct = new HashSet<string>(chain);
                        bool valid = chain.Length >= 1 && distinct.Count == chain.Length
                            && chain[chain.Length - 1] == "KillFeed.Audio.hit-bf5-hit.wav"
                            && chain[0] == name;
                        for (int i = 0; i < chain.Length; i++)
                        {
                            // A kill chain never swaps the tier it reports; only the final body-hit
                            // fallback is tier-less.
                            if (chain[i].Contains("-kill-") && !chain[i].Contains(HitSoundPolicy.TierToken(tier)))
                                valid = false;
                        }
                        if (!valid)
                        {
                            if (chainsValid) firstBadChain = style + "/" + kind + "/" + tier + "/" + flight + " => " + string.Join(",", chain);
                            chainsValid = false;
                        }
                    }
                }
            }
        }
        check(combos == 108 && chainsValid,
            "hit-sound: all 108 (style x kind x tier x flight) combinations resolve to a chain that ends at the BF5 hit cue and never swaps the kill tier"
            + (chainsValid ? string.Empty : " FIRST BAD " + firstBadChain));
        check(names.Count == 25 && names.Count == HitSoundPolicy.ShippedCueNames,
            "hit-sound: the matrix collapses onto exactly 25 shipped cue names (2 hit files - BF1 and BF5, BF6 has no hit take - plus 1 weakspot, 1 armor and 21 kill/flight files), and the code/log count agrees");

        // ---- chains -------------------------------------------------------------------------
        string[] bf5Hit = HitSoundPolicy.CueChain(HitSoundStyle.Bf5, HitCueKind.Hit);
        check(bf5Hit.Length == 1 && bf5Hit[0] == "KillFeed.Audio.hit-bf5-hit.wav",
            "hit-sound: the BF5 hit cue needs no fallback");
        string[] bf1Hit = HitSoundPolicy.CueChain(HitSoundStyle.Bf1, HitCueKind.Hit);
        check(bf1Hit.Length == 2 && bf1Hit[1] == "KillFeed.Audio.hit-bf5-hit.wav",
            "hit-sound: a missing BF1 hit cue falls back to the BF5 hit cue");
        string[] armor = HitSoundPolicy.CueChain(HitSoundStyle.Bf5, HitCueKind.ArmorHit);
        check(armor.Length == 2 && armor[0] == "KillFeed.Audio.hit-bf5-armor.wav" && armor[1] == "KillFeed.Audio.hit-bf5-hit.wav",
            "hit-sound: a missing armor cue falls back to the body hit cue");
        string[] flyerMinor = HitSoundPolicy.CueChain(HitSoundStyle.Bf1, HitCueKind.Kill, KillTier.Minor, KillFlight.Flying);
        check(flyerMinor.Length == 4
            && flyerMinor[0] == "KillFeed.Audio.hit-bf1-kill-flyer-minor.wav"
            && flyerMinor[1] == "KillFeed.Audio.hit-bf5-kill-flyer-minor.wav"
            && flyerMinor[2] == "KillFeed.Audio.hit-bf5-kill-minor.wav"
            && flyerMinor[3] == "KillFeed.Audio.hit-bf5-hit.wav",
            "hit-sound: a missing BF1 flying kill falls back to the BF5 flying kill and then to the same tier without the flight layer");
        string[] armorBoss = HitSoundPolicy.CueChain(HitSoundStyle.Bf6, HitCueKind.Kill, KillTier.Boss, KillFlight.ArmoredFlying);
        check(armorBoss.Length == 3
            && armorBoss[0] == "KillFeed.Audio.hit-bf5-kill-armor-flyer-boss.wav"
            && armorBoss[1] == "KillFeed.Audio.hit-bf5-kill-boss.wav"
            && armorBoss[2] == "KillFeed.Audio.hit-bf5-hit.wav",
            "hit-sound: an armored boss flyer collapses to BF5, then to the plain boss kill, then to the body hit cue");
        check(HitSoundPolicy.CueChain(HitSoundStyle.Bf5, HitCueKind.Kill, KillTier.Elite, KillFlight.None)[0] == "KillFeed.Audio.hit-bf5-kill-elite.wav",
            "hit-sound: the BF5 elite kill is its own first choice");

        // ---- play / throttle / mute ---------------------------------------------------------
        check(HitSoundPolicy.ShouldPlay(true, 0.8f), "hit-sound: enabled with volume plays");
        check(!HitSoundPolicy.ShouldPlay(false, 0.8f), "hit-sound: disabled never plays");
        check(!HitSoundPolicy.ShouldPlay(true, 0f), "hit-sound: zero volume never plays");

        check(HitSoundPolicy.IntervalAllows(5.0, -1.0, 0.05), "hit-sound: the first cue is never throttled");
        check(HitSoundPolicy.IntervalAllows(1.0, 0.9, 0.05), "hit-sound: a gap above the interval is allowed");
        check(!HitSoundPolicy.IntervalAllows(1.0, 0.98, 0.05), "hit-sound: a gap below the interval is dropped");
        check(!HitSoundPolicy.IntervalAllows(double.NaN, 0.5, 0.05), "hit-sound: a broken clock never plays");
        check(HitSoundPolicy.IntervalAllows(1.0, double.NaN, 0.05), "hit-sound: a missing timestamp counts as never played");

        var ids = new HashSet<uint> { 11u, 22u, 33u };
        check(HitSoundPolicy.ShouldMute(true, ids, 22u), "hit-sound: a whitelisted id is muted when the switch is on");
        check(!HitSoundPolicy.ShouldMute(false, ids, 22u), "hit-sound: the switch off restores every vanilla event");
        check(!HitSoundPolicy.ShouldMute(true, ids, 44u), "hit-sound: a non-whitelisted id is never muted");
        check(!HitSoundPolicy.ShouldMute(true, new HashSet<uint>(), 22u), "hit-sound: an unresolved id set mutes nothing");
        check(!HitSoundPolicy.ShouldMute(true, null, 22u), "hit-sound: a null id set mutes nothing");
        // 1.2.18: the filter can only ever touch the ids the whitelist really resolved; body/voice events and any
        // other Wwise event stay audible, and the decision is still the MuteVanillaIndicator switch alone.
        var resolvedSet = new HashSet<uint>(twoOfThree.Ids);
        check(!HitSoundPolicy.ShouldMute(true, resolvedSet, 1u) && !HitSoundPolicy.ShouldMute(true, resolvedSet, 12345u)
            && !HitSoundPolicy.ShouldMute(true, resolvedSet, 11u),
            "hit-sound: the mute filter never matches an id outside the resolved whitelist (body impact and voice events stay audible)");
        check(HitSoundPolicy.ShouldMute(true, resolvedSet, 22u) && HitSoundPolicy.ShouldMute(true, resolvedSet, 33u)
            && !HitSoundPolicy.ShouldMute(false, resolvedSet, 22u),
            "hit-sound: muting follows MuteVanillaIndicator alone - the custom cue switches are never consulted for it");
    }
}
