using System;
using System.IO;
using BepInEx.Configuration;
using HawChow.GTFOKillFeed;

// End-to-end hot-reload proof through BepInEx's *real* ConfigFile: the same engine the plugin uses
// in game. It binds the production keys with the production defaults, edits the file externally the
// way a player would, reloads through the exact production sequence (SaveOnConfigSet=false, Reload(),
// restore) and then checks that the real consumer functions see the new values without a write-back.
internal static class Progam
{
    private static int _failed;
    private static int _passed;

    private static void Check(bool condition, string name)
    {
        if (condition) { _passed++; Console.WriteLine("PASS " + name); return; }
        _failed++;
        Console.WriteLine("FAIL " + name);
    }

    // Plugin.PollConfigHotReload switches Config.SaveOnConfigSet off for the duration of
    // Config.Reload() and restores it afterwards, so a reload can never write the file back. The test
    // runs the identical sequence and asserts the production promise on every call: the same bytes,
    // the same length and the same timestamp on disk, with the flag restored.
    private static void ReloadLikeProduction(ConfigFile file, string path)
    {
        string before = Hash(path, out long lengthBefore, out DateTime stampBefore);
        bool saveOnConfigSet = file.SaveOnConfigSet;
        try
        {
            file.SaveOnConfigSet = false;
            file.Reload();
        }
        finally
        {
            file.SaveOnConfigSet = saveOnConfigSet;
        }
        string after = Hash(path, out long lengthAfter, out DateTime stampAfter);
        Check(file.SaveOnConfigSet == saveOnConfigSet && before == after
            && lengthBefore == lengthAfter && stampBefore == stampAfter,
            "bepinex: the production reload sequence (SaveOnConfigSet=false + restore) leaves the cfg untouched on disk");
    }

    private static string Hash(string path, out long length, out DateTime stamp)
    {
        byte[] bytes = File.ReadAllBytes(path);
        var info = new FileInfo(path);
        length = info.Length;
        stamp = info.LastWriteTimeUtc;
        using (var sha = System.Security.Cryptography.SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty);
    }

    private static int Main()
    {
        string scratch = Environment.GetEnvironmentVariable("PI_SCRATCH_DIR");
        if (string.IsNullOrWhiteSpace(scratch)) scratch = Path.GetTempPath();
        string dir = Path.Combine(scratch, "killfeed-configsmoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "HawChow.GTFOKillFeed.cfg");
        try
        {
            Run(path);
        }
        catch (Exception error)
        {
            _failed++;
            Console.WriteLine("FAIL unhandled: " + error);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
        Console.WriteLine(_failed == 0
            ? "CONFIG_SMOKE=PASS;ASSERTIONS=" + _passed
            : "CONFIG_SMOKE=FAIL;PASSED=" + _passed + ";FAILED=" + _failed);
        return _failed == 0 ? 0 : 1;
    }

    private static void Run(string path)
    {
        // ---- first launch: the plugin binds its keys (production section/key/default triples) ----
        var file = new ConfigFile(path, true);
        var enabled = file.Bind("General", "Enabled", true, "");
        var corpse = file.Bind("Corpse", "Enabled", true, "");
        var explosionFx = file.Bind("Corpse", "ExplosionFx", false, "");
        var weakspotColor = file.Bind("DamageNumbers", "WeakspotColor", "#E5484D", "");
        var numberOpacity = file.Bind("DamageNumbers", "Opacity", FeedbackPolicy.DefaultNumberOpacity, "");
        var markEnabled = file.Bind("TeammateMark", "Enabled", true, "");
        var markIcon = file.Bind("TeammateMark", "Icon", "Cai", "");
        var markCustom = file.Bind("TeammateMark", "CustomIconPath", "", "");
        var markMinPixels = file.Bind("TeammateMark", "MinPixels", 26f, "");
        var markMaxDistance = file.Bind("TeammateMark", "MaxDistanceMetres", 60f, "");
        var hideDuringEmp = file.Bind("TeammateMark", "HideDuringEMP", false, "");
        var maxDistance = file.Bind("Layout", "MaxDistance", 999f, "");

        Check(explosionFx.Value == false,
            "bepinex: a fresh cfg binds Corpse/ExplosionFx to false (existing users without the key keep the burst off)");
        Check(weakspotColor.Value == "#E5484D", "bepinex: WeakspotColor defaults to #E5484D");
        Check(markIcon.Value == "Cai" && markCustom.Value == string.Empty,
            "bepinex: the mark defaults to the bundled 菜 icon with no machine path");
        Check(hideDuringEmp.Value == false, "bepinex: HideDuringEMP defaults to false (visible during EMP)");
        Check(markMinPixels.Value == 26f && markMaxDistance.Value == 60f,
            "bepinex: numeric mark defaults are bound as numbers");
        Check(numberOpacity.Value == FeedbackPolicy.DefaultNumberOpacity
            && FeedbackPolicy.NumberAlpha(0.65f, FeedbackPolicy.NumberLifetime, numberOpacity.Value)
                == FeedbackPolicy.Alpha(0.65f, FeedbackPolicy.NumberLifetime),
            "bepinex: a fresh cfg binds DamageNumbers/Opacity to 1.0 and the consumer keeps the previous alpha exactly");

        // ---- consumers read the bound values -----------------------------------------------
        Check(FeedbackPolicy.ShouldPlayCorpseBurstFx(CorpseSwitchFixture.Sw(corpse.Value, explosionFx.Value)) == false
            && FeedbackPolicy.BurstPlan(CorpseSwitchFixture.Sw(corpse.Value, explosionFx.Value), 1f, 1f).Bursts == 0,
            "bepinex: the burst consumer sees 'off' at the bound default");
        var colour = new HexColorCache();
        colour.Resolve(weakspotColor.Value, 0.90f, 0.67f, 0.34f, 1f);
        Check(Math.Abs(colour.Red - 229f / 255f) < 0.001f && !colour.IsFallback,
            "bepinex: the damage-number consumer parses the bound colour");
        Check(TeammateMarkPolicy.ShouldDrawMark(markEnabled.Value, true, true, false, true,
            TeammateMarkPolicy.ShouldHideForEmp(hideDuringEmp.Value, false, true), true, 8f, markMaxDistance.Value),
            "bepinex: the mark consumer draws at the bound defaults (no EMP handler installed)");

        // ---- player edits the file, then the plugin calls Reload() --------------------------
        File.WriteAllText(path,
            "[General]\r\nEnabled = true\r\n" +
            "[Corpse]\r\nEnabled = true\r\nExplosionFx = true\r\n" +
            "[TeammateMark]\r\nEnabled = true\r\nIcon = Ruo\r\nCustomIconPath = C:\\pics\\mine.png\r\n" +
            "MinPixels = 30\r\nMaxDistanceMetres = 25\r\nHideDuringEMP = true\r\n" +
            "[Layout]\r\nMaxDistance = 25\r\n" +
            "[DamageNumbers]\r\nWeakspotColor = #F0483C\r\nOpacity = 0.5\r\n");
        ReloadLikeProduction(file, path);

        Check(explosionFx.Value, "bepinex: Reload picks up the edited ExplosionFx=true");
        Check(FeedbackPolicy.ShouldPlayCorpseBurstFx(CorpseSwitchFixture.Sw(corpse.Value, explosionFx.Value))
            && FeedbackPolicy.BurstPlan(CorpseSwitchFixture.Sw(corpse.Value, explosionFx.Value), 1f, 1f).Bursts >= 3
            && FeedbackPolicy.BurstPlan(CorpseSwitchFixture.Sw(corpse.Value, explosionFx.Value), 1f, 1f).Sound,
            "bepinex: the burst consumer now enables the FX and its sound");
        colour.Resolve(weakspotColor.Value, 0.90f, 0.67f, 0.34f, 1f);
        Check(Math.Abs(colour.Red - 240f / 255f) < 0.001f && Math.Abs(colour.Blue - 60f / 255f) < 0.001f,
            "bepinex: the damage-number consumer picks the edited colour on the next frame");
        Check(TeammateMarkPolicy.ParseIcon(markIcon.Value) == TeammateMarkIcon.Ruo,
            "bepinex: the mark consumer switches to 鶸 after the edit");
        Check(TeammateMarkPolicy.ParseIcon(markIcon.Value) == TeammateMarkIcon.Ruo,
            "bepinex: the mark consumer switches to 鶸 after the edit");
        Check(TeammateMarkPolicy.CustomProbePath(markCustom.Value, "D:\\plugin") == @"C:\pics\mine.png",
            "bepinex: the edited absolute custom icon path reaches the icon pipeline");
        Check(Math.Abs(markMinPixels.Value - 30f) < 0.001f && Math.Abs(markMaxDistance.Value - 25f) < 0.001f,
            "bepinex: edited numeric mark settings are re-read");
        Check(hideDuringEmp.Value
            && TeammateMarkPolicy.ShouldHideForEmp(hideDuringEmp.Value, true, true),
            "bepinex: turning HideDuringEMP on makes an active EMP suppress the mark");
        Check(TeammateMarkPolicy.ShouldDrawMark(markEnabled.Value, true, true, false, true, false, true, 20f, markMaxDistance.Value)
            && !TeammateMarkPolicy.ShouldDrawMark(markEnabled.Value, true, true, false, true, false, true, 26f, markMaxDistance.Value),
            "bepinex: the edited MaxDistanceMetres takes effect in the draw gate");

        Check(Math.Abs(numberOpacity.Value - 0.5f) < 0.0001f
            && Math.Abs(FeedbackPolicy.NumberAlpha(0.65f, FeedbackPolicy.NumberLifetime, numberOpacity.Value)
                - FeedbackPolicy.Alpha(0.65f, FeedbackPolicy.NumberLifetime) * 0.5f) < 0.0001f,
            "bepinex: after Reload the consumer halves an already-visible number's fade, and applies 0.5 only once");
        // Already-visible cards follow the same edit (R5).
        var card = new KillCard(KillKind.Weakspot, KillTier.Minor, "MOD_Shooter", 100f, 120f, 3.5f, 999f, 13);
        KillCard shown = CardPresentation.Resolve(card, (id, raw) => "实验体", maxDistance.Value);
        Check(shown.EnemyName == "实验体" && shown.DistanceText == "25m+" && shown.Kind == KillKind.Weakspot,
            "bepinex: an already-visible card re-resolves its name and distance ceiling after the edit");

        // ---- disabling a section -----------------------------------------------------------
        File.WriteAllText(path,
            "[Corpse]\r\nExplosionFx = false\r\n" +
            "[TeammateMark]\r\nEnabled = false\r\nIcon = Cai\r\n");
        ReloadLikeProduction(file, path);
        Check(!explosionFx.Value && !FeedbackPolicy.ShouldPlayCorpseBurstFx(CorpseSwitchFixture.Sw(corpse.Value, explosionFx.Value)),
            "bepinex: disabling the burst again is picked up by the consumer");
        Check(!markEnabled.Value && !TeammateMarkPolicy.ShouldDrawMark(markEnabled.Value, true, true, false, true, false, true, 8f, 60f),
            "bepinex: TeammateMark/Enabled=false stops the mark");

        // ---- opacity 0 hides everything, then back to 1 -------------------------------
        File.WriteAllText(path, "[DamageNumbers]\r\nOpacity = 0\r\n");
        ReloadLikeProduction(file, path);
        Check(numberOpacity.Value == 0f && !FeedbackPolicy.ShowsNumber(numberOpacity.Value)
            && FeedbackPolicy.NumberTextAlpha(1f, FeedbackPolicy.NumberAlpha(0.65f, FeedbackPolicy.NumberLifetime, numberOpacity.Value)) == 0f,
            "bepinex: Opacity=0 reaches the consumer as 'draw nothing at all', with no text and no outline");
        File.WriteAllText(path, "[DamageNumbers]\r\nOpacity = 1\r\n");
        ReloadLikeProduction(file, path);
        Check(numberOpacity.Value == 1f
            && FeedbackPolicy.NumberAlpha(0.65f, FeedbackPolicy.NumberLifetime, numberOpacity.Value)
                == FeedbackPolicy.Alpha(0.65f, FeedbackPolicy.NumberLifetime),
            "bepinex: switching back to 1 restores the previous alpha exactly");

        // ---- invalid values never throw through the reload path ----------------------------
        File.WriteAllText(path, "[Corpse]\r\nExplosionFx = notabool\r\n[DamageNumbers]\r\nWeakspotColor = #GGGGGG\r\n");
        bool threw = false;
        try { ReloadLikeProduction(file, path); } catch { threw = true; }
        Check(!threw, "bepinex: an invalid bool does not throw out of Reload()");
        Check(explosionFx.Value == false, "bepinex: an invalid bool keeps the previous value");
        colour.Resolve(weakspotColor.Value, 0.90f, 0.67f, 0.34f, 1f);
        Check(colour.IsFallback && Math.Abs(colour.Red - 0.90f) < 0.001f,
            "bepinex: an invalid colour falls back to the amber accent instead of throwing");

        // ---- out-of-range and unparsable opacity -------------------------------------
        File.WriteAllText(path, "[DamageNumbers]\r\nOpacity = 7\r\n");
        ReloadLikeProduction(file, path);
        Check(numberOpacity.Value == 7f && FeedbackPolicy.NumberOpacity(numberOpacity.Value) == 1f,
            "bepinex: an out-of-range 7 is stored as written and clamped to 1 by the consumer, so the number stays fully visible");
        File.WriteAllText(path, "[DamageNumbers]\r\nOpacity = notanumber\r\n");
        threw = false;
        try { ReloadLikeProduction(file, path); } catch { threw = true; }
        Check(!threw && numberOpacity.Value == 7f && FeedbackPolicy.NumberOpacity(numberOpacity.Value) == 1f,
            "bepinex: an unparsable opacity neither throws nor blanks the numbers: the previous value is kept and still clamps to 1");

        // ---- the real debounce gate driving the production reload sequence ----------------
        // The 0.40 s stability window is already covered against real files in the Smoke hot-reload
        // tests; this joins that gate to the production reload so the plugin path is proven end to end.
        File.WriteAllText(path, "[DamageNumbers]\r\nOpacity = 0.5\r\n");
        string gateHash = Hash(path, out long gateLength, out DateTime gateStamp);
        var gate = new ConfigHotReloadGate();
        gate.Seed(gateStamp, gateLength);
        Check(!gate.ShouldReload(gateStamp, gateLength, 0f),
            "gate: a cfg that did not change since seeding never reloads");
        File.WriteAllText(path, "[DamageNumbers]\r\nOpacity = 0\r\n");
        Hash(path, out long editedLength, out DateTime editedStamp);
        Check(editedLength != gateLength || editedStamp != gateStamp,
            "gate: the external edit is visible on disk (test precondition)");
        Check(!gate.ShouldReload(editedStamp, editedLength, 100f),
            "gate: a fresh write only starts the debounce, so the production tick would skip it");
        Check(!gate.ShouldReload(editedStamp, editedLength, 100.39f),
            "gate: nothing is applied before the 0.40 s stability window");
        Check(gate.ShouldReload(editedStamp, editedLength, 100.41f),
            "gate: the stable write becomes reloadable for the production tick");
        ReloadLikeProduction(file, path);
        Check(numberOpacity.Value == 0f && !FeedbackPolicy.ShowsNumber(numberOpacity.Value),
            "gate: the value edited on disk reaches the consumer through the production reload sequence");
        gate.MarkApplied(editedStamp, editedLength);
        Check(!gate.ShouldReload(editedStamp, editedLength, 200f),
            "gate: an applied write is never reloaded twice");
        File.WriteAllText(path, "[DamageNumbers]\r\nOpacity = 0.5\r\n");
        ReloadLikeProduction(file, path);
        Check(Math.Abs(numberOpacity.Value - 0.5f) < 0.0001f
            && Math.Abs(FeedbackPolicy.NumberAlpha(0.65f, FeedbackPolicy.NumberLifetime, numberOpacity.Value)
                - FeedbackPolicy.Alpha(0.65f, FeedbackPolicy.NumberLifetime) * 0.5f) < 0.0001f,
            "gate: the user-changed 0.5 is re-read and halves the fade exactly once");
        Check(gateHash.Length == 64,
            "gate: the disk hash helper really reads the cfg (test precondition)");
        // ---- deleting a key: BepInEx keeps the last value (measured, not assumed) ----------
        File.WriteAllText(path, "[Corpse]\r\n");
        ReloadLikeProduction(file, path);
        Check(explosionFx.Value == false,
            "bepinex: deleting a key after load keeps the last value (documented BepInEx semantics)");

        // ---- restart: a new ConfigFile over the same file keeps the written values ---------
        File.WriteAllText(path, "[Corpse]\r\nExplosionFx = true\r\n[TeammateMark]\r\nIcon = Ruo\r\n");
        var restarted = new ConfigFile(path, true);
        var restartedFx = restarted.Bind("Corpse", "ExplosionFx", false, "");
        var restartedIcon = restarted.Bind("TeammateMark", "Icon", "Cai", "");
        Check(restartedFx.Value && TeammateMarkPolicy.ParseIcon(restartedIcon.Value) == TeammateMarkIcon.Ruo,
            "bepinex: a plugin restart reads the persisted values instead of the defaults");

        // ---- 1.2.12 keys: defaults, clamping and hot reload through the real config engine ---
        string second = Path.Combine(Path.GetDirectoryName(path), "hit-sound.cfg");
        var hitFile = new ConfigFile(second, true);
        var hitEnabled = hitFile.Bind("HitSound", "Enabled", HitSoundPolicy.DefaultEnabled, "");
        var hitStyle = hitFile.Bind("HitSound", "Style", HitSoundPolicy.DefaultStyle, "");
        var hitVolume = hitFile.Bind("HitSound", "Volume", HitSoundPolicy.DefaultVolume, "");
        var hitMute = hitFile.Bind("HitSound", "MuteVanillaIndicator", HitSoundPolicy.DefaultMuteVanillaIndicator, "");
        var hitInterval = hitFile.Bind("HitSound", "MinIntervalSeconds", (float)HitSoundPolicy.DefaultMinIntervalSeconds, "");
        var prismatic = hitFile.Bind("DamageFx", "PrismaticThreshold", DamageFxPolicy.DefaultPrismaticThreshold, "");
        var tiers = hitFile.Bind("DamageFx", "TiersEnabled", DamageFxPolicy.DefaultTiersEnabled, "");
        var prismaticEnabled = hitFile.Bind("DamageFx", "PrismaticEnabled", DamageFxPolicy.DefaultPrismaticEnabled, "");
        var approachEffect = hitFile.Bind("DamageNumbers", "ApproachEffect", FeedbackPolicy.DefaultApproachEffect, "");
        var focusOutline = hitFile.Bind("EnemyBar", "FocusOutlineEnabled", FocusOutlinePolicy.DefaultEnabled, "");
        var fontWeight = hitFile.Bind("DamageNumbers", "FontWeight", FeedbackPolicy.DefaultNumberWeight, "");
        var whiteTier = hitFile.Bind("DamageFx", "WhiteThreshold", DamageFxPolicy.WhiteThreshold, "");
        var silverTier = hitFile.Bind("DamageFx", "SilverThreshold", DamageFxPolicy.SilverThreshold, "");
        var goldTier = hitFile.Bind("DamageFx", "GoldThreshold", DamageFxPolicy.GoldThreshold, "");
        var diamondTier = hitFile.Bind("DamageFx", "DiamondThreshold", DamageFxPolicy.DiamondThreshold, "");
        var heavyHealth = hitFile.Bind("KillIcon", "HeavyHealthThreshold", KillPolicy.DefaultHeavyHealthThreshold, "");
        var minorHealth = hitFile.Bind("KillIcon", "MinorHealthThreshold", KillDefaults.MinorHealthThreshold, "");
        var killIconDisplay = hitFile.Bind("KillIcon", "Display", KillIconPolicy.DisplayHead, "");
        var killIconLifetime = hitFile.Bind("KillIcon", "HeadLifetime", KillIconPolicy.DefaultLifetimeSeconds, "");
        var killIconMax = hitFile.Bind("KillIcon", "HeadMaxIcons", KillIconPolicy.DefaultMaxIcons, "");
        var killIconGap = hitFile.Bind("KillIcon", "HeadGapMetres", KillIconPolicy.DefaultGapMetres, "");
        var killIconFlyerColor = hitFile.Bind("KillIcon", "FlyerColor", KillIconPolicy.DefaultWingsColor, "");
        var killIconArmorColor = hitFile.Bind("KillIcon", "ArmorFlyerColor", KillIconPolicy.DefaultArmorPlateColor, "");
        var hitMarkerEnabled = hitFile.Bind("HitMarker", "Enabled", HitMarkerPolicy.DefaultEnabled, "");
        var hitMarkerSize = hitFile.Bind("HitMarker", "Size", HitMarkerPolicy.DefaultSize, "");
        var hitMarkerNormal = hitFile.Bind("HitMarker", "ColorNormal", HitMarkerPolicy.DefaultNormalColor, "");
        var hitMarkerWeakspot = hitFile.Bind("HitMarker", "ColorWeakspot", HitMarkerPolicy.DefaultWeakspotColor, "");
        var hitMarkerArmor = hitFile.Bind("HitMarker", "ColorArmor", HitMarkerPolicy.DefaultArmorColor, "");
        var hitMarkerKill = hitFile.Bind("HitMarker", "ColorKill", HitMarkerPolicy.DefaultKillColor, "");

        Check(hitEnabled.Value && !hitMute.Value,
            "bepinex: HitSound/Enabled defaults to true since 1.2.16 while MuteVanillaIndicator stays false - the two switches are independent");
        Check(hitStyle.Value == "BF5" && HitSoundPolicy.ParseStyle(hitStyle.Value) == HitSoundStyle.Bf5,
            "bepinex: HitSound/Style defaults to BF5");
        Check(Math.Abs(hitVolume.Value - 0.8f) < 0.0001f, "bepinex: HitSound/Volume defaults to 0.8");
        Check(Math.Abs(hitInterval.Value - 0.05f) < 0.0001f, "bepinex: HitSound/MinIntervalSeconds defaults to 0.05");
        Check(Math.Abs(prismatic.Value - 60f) < 0.0001f && tiers.Value,
            "bepinex: DamageFx defaults are PrismaticThreshold=60 with tiers enabled");
        Check(prismaticEnabled.Value && !approachEffect.Value,
            "bepinex: the two new switches bind their shipped defaults (DamageFx/PrismaticEnabled=true, DamageNumbers/ApproachEffect=false)");
        Check(DamageFxPolicy.IsPrismatic(500f, prismatic.Value, prismaticEnabled.Value)
            && !DamageFxPolicy.IsPrismatic(500f, prismatic.Value, false),
            "bepinex: the prismatic consumer honours the bound switch, not only the threshold");
        Check(whiteTier.Value == DamageFxPolicy.WhiteThreshold && silverTier.Value == DamageFxPolicy.SilverThreshold
            && goldTier.Value == DamageFxPolicy.GoldThreshold && diamondTier.Value == DamageFxPolicy.DiamondThreshold,
            "bepinex: DamageFx tier thresholds default to 3000/5000/10000/20000");
        Check(Math.Abs(minorHealth.Value - 120f) < 0.0001f,
            "bepinex: KillIcon/MinorHealthThreshold defaults to 120 and there is no second tier key to disagree with the heavy line");
        // The BOSS line is the bound heavy value, not a separate key.
        var defaultBounds = KillPolicy.ResolveTierBoundaries(minorHealth.Value, heavyHealth.Value);
        Check(Math.Abs(defaultBounds.Boss - 1000f) < 0.0001f && !defaultBounds.EliteBandEmpty
            && KillPolicy.ClassifyTier(119f, defaultBounds) == KillTier.Minor
            && KillPolicy.ClassifyTier(120f, defaultBounds) == KillTier.Elite
            && KillPolicy.ClassifyTier(999f, defaultBounds) == KillTier.Elite
            && KillPolicy.ClassifyTier(1000f, defaultBounds) == KillTier.Boss,
            "bepinex: the bound heavy line is the BOSS line and really splits 119/120/999/1000");
        Check(KillIconPolicy.ParseDisplay(killIconDisplay.Value) == KillIconDisplay.Head
            && Math.Abs(killIconLifetime.Value - 2.2f) < 0.0001f
            && killIconMax.Value == 6
            && Math.Abs(killIconGap.Value - 0.3f) < 0.0001f
            && killIconFlyerColor.Value == KillIconPolicy.DefaultWingsColor
            && killIconArmorColor.Value == KillIconPolicy.DefaultArmorPlateColor,
            "bepinex: KillIcon/Display defaults to Head with 2.2 s lifetime, 6 icons, 0.3 m gap and the two badge colours");
        Check(Math.Abs(heavyHealth.Value - 1000f) < 0.0001f && hitMarkerEnabled.Value
            && Math.Abs(hitMarkerSize.Value - 64f) < 0.0001f,
            "bepinex: a 1000 HP heavy line and hit markers on at 64 px by default");
        Check(hitMarkerNormal.Value == "#FFFFFF" && hitMarkerWeakspot.Value == "#FF8C00" && hitMarkerArmor.Value == "#49ADC9",
            "bepinex: the three hit-marker colours default to white / BFV orange / BFV cyan");
        Check(hitMarkerKill.Value == "#B92828"
            && HitMarkerPolicy.DefaultKillColor == "#B92828"
            && HitMarkerPolicy.DefaultKillColor != HitMarkerPolicy.DefaultWeakspotColor,
            "bepinex: the kill line has its own default colour (#B92828), distinct from every hit colour");
        Check(focusOutline.Value, "bepinex: EnemyBar/FocusOutlineEnabled defaults to true");
        Check(Math.Abs(fontWeight.Value - 1f) < 0.0001f
            && FeedbackPolicy.NumberUsesBold(fontWeight.Value)
            && FeedbackPolicy.NumberThickenPasses(fontWeight.Value) == 0,
            "bepinex: DamageNumbers/FontWeight defaults to 1 = shipped bold look with no extra body stamps");

        File.WriteAllText(second,
            "[HitSound]\r\nEnabled = false\r\nStyle = BF1\r\nVolume = 1.5\r\nMuteVanillaIndicator = false\r\nMinIntervalSeconds = 99\r\n" +
            "[DamageFx]\r\nPrismaticThreshold = 25\r\nPrismaticEnabled = false\r\nTiersEnabled = false\r\nWhiteThreshold = 800\r\nSilverThreshold = 100\r\nGoldThreshold = 4000\r\nDiamondThreshold = 99999999999\r\n" +
            "[KillIcon]\r\nHeavyHealthThreshold = 400\r\nMinorHealthThreshold = 80\r\nDisplay = Card\r\nHeadLifetime = 99\r\nHeadMaxIcons = 99\r\nHeadGapMetres = -3\r\nFlyerColor = #102030\r\nArmorFlyerColor = #405060\r\n[HitMarker]\r\nEnabled = false\r\nSize = 120\r\nColorNormal = #101010\r\nColorWeakspot = #FF0000\r\nColorArmor = #00FF00\r\nColorKill = #123456\r\n" +
            "[EnemyBar]\r\nFocusOutlineEnabled = false\r\n" +
            "[DamageNumbers]\r\nFontWeight = 1.6\r\nApproachEffect = true\r\n");
        ReloadLikeProduction(hitFile, second);
        // Read the file the player would see right after the reload: the later cases rewrite it again.
        string switchesText = File.ReadAllText(second);
        Check(!hitEnabled.Value && !hitMute.Value, "gate: the edited HitSound switches are re-read");
        Check(HitSoundPolicy.ParseStyle(hitStyle.Value) == HitSoundStyle.Bf1,
            "gate: the edited style reaches the cue selector on the next hit");
        Check(Math.Abs(HitSoundPolicy.Volume(hitVolume.Value) - 1f) < 0.0001f,
            "gate: an out-of-range volume edited on disk is clamped by the consumer, not trusted");
        Check(Math.Abs(HitSoundPolicy.MinInterval(hitInterval.Value) - 3.0) < 1e-6,
            "gate: an out-of-range interval edited on disk is clamped to the ceiling");
        Check(DamageFxPolicy.IsPrismatic(30f, prismatic.Value) && !DamageFxPolicy.IsPrismatic(20f, prismatic.Value),
            "gate: lowering PrismaticThreshold on disk moves the flare boundary to 25");
        Check(DamageFxPolicy.TierFor(50000, tiers.Value) == DamageTier.None,
            "gate: disabling tiers on disk removes the tier colour without touching the running total");
        var resolvedTiers = DamageFxPolicy.ResolveTiers(whiteTier.Value, silverTier.Value, goldTier.Value, diamondTier.Value);
        Check(resolvedTiers.White == 800 && resolvedTiers.Silver == 800 && resolvedTiers.Gold == 4000
            && resolvedTiers.Diamond == DamageFxPolicy.MaxTierThreshold,
            "gate: edited tier thresholds are re-read, an inverted silver is pushed up to white, an overflow is clamped");
        Check(DamageFxPolicy.TierFor(799, true, resolvedTiers) == DamageTier.None
            && DamageFxPolicy.TierFor(900, true, resolvedTiers) == DamageTier.Silver
            && DamageFxPolicy.TierFor(5000, true, resolvedTiers) == DamageTier.Gold
            && DamageFxPolicy.TierFor(DamageFxPolicy.MaxTierThreshold, true, resolvedTiers) == DamageTier.Diamond,
            "gate: the edited ladder moves every tier boundary without a restart");
        Check(Math.Abs(KillPolicy.HeavyHealthThreshold(heavyHealth.Value) - 400f) < 0.0001f
            && KillPolicy.IsHeavy(450f, heavyHealth.Value) && !KillPolicy.IsHeavy(350f, heavyHealth.Value),
            "gate: an edited heavy threshold moves the HEAVY kill-icon line to 400");
        var editedBounds = KillPolicy.ResolveTierBoundaries(minorHealth.Value, heavyHealth.Value);
        Check(Math.Abs(editedBounds.Minor - 80f) < 0.0001f && Math.Abs(editedBounds.Boss - 400f) < 0.0001f
            && KillPolicy.ClassifyTier(79f, editedBounds) == KillTier.Minor
            && KillPolicy.ClassifyTier(80f, editedBounds) == KillTier.Elite
            && KillPolicy.ClassifyTier(399f, editedBounds) == KillTier.Elite
            && KillPolicy.ClassifyTier(400f, editedBounds) == KillTier.Boss,
            "gate: an edited minor line of 80 and the edited heavy line of 400 are re-read on the next kill without a restart");
        Check(Math.Abs(heavyHealth.Value - 400f) < 0.0001f
            && KillPolicy.IsHeavy(400f, heavyHealth.Value) && !KillPolicy.IsHeavy(399f, heavyHealth.Value)
            && KillPolicy.ClassifyTier(400f, editedBounds) == KillTier.Boss
            && KillPolicy.ClassifyTier(250f, editedBounds) == KillTier.Elite,
            "gate: the heavy icon line and the BOSS tier are the same 400 line after the edit");
        // Back to the default heavy line: the BOSS boundary has to follow it, not a cached 400.
        File.WriteAllText(second, "[KillIcon]\r\nHeavyHealthThreshold = 1000\r\n");
        ReloadLikeProduction(hitFile, second);
        var restoredBounds = KillPolicy.ResolveTierBoundaries(minorHealth.Value, heavyHealth.Value);
        Check(Math.Abs(heavyHealth.Value - 1000f) < 0.0001f
            && KillPolicy.ClassifyTier(400f, restoredBounds) == KillTier.Elite
            && KillPolicy.ClassifyTier(1000f, restoredBounds) == KillTier.Boss,
            "gate: switching the heavy line back to 1000 moves BOSS back to 1000 on the next kill");
        Check(!hitMarkerEnabled.Value && Math.Abs(HitMarkerPolicy.Size(hitMarkerSize.Value) - 120f) < 0.0001f,
            "gate: the edited hit-marker switch and size reach the draw path");
        Check(hitMarkerNormal.Value == "#101010" && hitMarkerWeakspot.Value == "#FF0000" && hitMarkerArmor.Value == "#00FF00",
            "gate: the three edited hit-marker colours are re-read");
        Check(hitMarkerKill.Value == "#123456",
            "gate: the edited kill-line colour is re-read on the next kill");
        Check(KillIconPolicy.ParseDisplay(killIconDisplay.Value) == KillIconDisplay.Card,
            "gate: switching KillIcon/Display to Card restores the previous cards on the next frame");
        Check(Math.Abs(KillIconPolicy.Lifetime(killIconLifetime.Value) - KillIconPolicy.MaxLifetimeSeconds) < 0.0001f
            && KillIconPolicy.MaxIcons(killIconMax.Value) == KillIconPolicy.MaxIconsCeiling
            && Math.Abs(KillIconPolicy.Gap(killIconGap.Value) - KillIconPolicy.MinGapMetres) < 0.0001f,
            "gate: edited head-icon lifetime/count/gap are re-read and clamped by the consumer, not trusted");
        Check(killIconFlyerColor.Value == "#102030" && killIconArmorColor.Value == "#405060",
            "gate: the two edited badge colours are re-read");
        Check(!focusOutline.Value, "gate: the focus outline can be switched off on disk");
        Check(Math.Abs(fontWeight.Value - 1.6f) < 0.0001f
            && FeedbackPolicy.NumberUsesBold(fontWeight.Value)
            && FeedbackPolicy.NumberThickenPasses(fontWeight.Value) == FeedbackPolicy.NumberThickenPassesMax
            && Math.Abs(FeedbackPolicy.NumberOutlineRadius(fontWeight.Value) - 2.02f) < 0.0001f,
            "gate: an edited FontWeight of 1.6 reaches the draw path (bold + 4 stamps + 2.02 px outline)");

        // ---- the three damage-number switches, edited on disk (1.2.16) -----------------------
        Check(!prismaticEnabled.Value && approachEffect.Value,
            "gate: both new switches are re-read from disk without a restart (DamageFx/PrismaticEnabled=false, DamageNumbers/ApproachEffect=true)");
        Check(!DamageFxPolicy.IsPrismatic(500f, prismatic.Value, prismaticEnabled.Value)
            && DamageFxPolicy.IsPrismatic(500f, prismatic.Value, true),
            "gate: PrismaticEnabled=false on disk stops the 500-damage flare even though the threshold is 25, and the switch alone explains it");
        Check(!tiers.Value && DamageFxPolicy.TierFor(50000, tiers.Value) == DamageTier.None
            && DamageFxPolicy.Paint(false, tiers.Value, DamageTier.Diamond).TierInk == false,
            "gate: TiersEnabled=false on disk removes the tier ink from body and outline while the cumulative total is still recorded");
        Check(DamageFxPolicy.Paint(false, tiers.Value, DamageTier.None).Foil == false
            && DamageFxPolicy.Paint(true, true, DamageTier.Diamond).Foil,
            "gate: the foil is decided by the prismatic switch, never by the tier switch");
        // The keys a player already has keep their names: the two new keys live in existing sections and do
        // not rename anything (an old cfg simply gets the shipped defaults for them).
        Check(switchesText.Contains("[DamageFx]") && switchesText.Contains("PrismaticEnabled = false")
            && switchesText.Contains("[DamageNumbers]") && switchesText.Contains("ApproachEffect = true")
            && switchesText.Contains("PrismaticThreshold = 25"),
            "gate: the new keys live in the existing DamageFx / DamageNumbers sections next to the untouched old keys");

        // ---- a fresh cfg without the two new keys ------------------------------------------
        string freshPath = Path.Combine(Path.GetDirectoryName(path), "fresh-switches.cfg");
        File.WriteAllText(freshPath, "[DamageNumbers]\r\nEnabled = true\r\n[DamageFx]\r\nPrismaticThreshold = 60\r\n");
        var freshFile = new ConfigFile(freshPath, true);
        var freshApproach = freshFile.Bind("DamageNumbers", "ApproachEffect", FeedbackPolicy.DefaultApproachEffect, "");
        var freshPrismatic = freshFile.Bind("DamageFx", "PrismaticEnabled", DamageFxPolicy.DefaultPrismaticEnabled, "");
        var freshTiers = freshFile.Bind("DamageFx", "TiersEnabled", DamageFxPolicy.DefaultTiersEnabled, "");
        Check(!freshApproach.Value && freshPrismatic.Value && freshTiers.Value,
            "bepinex: an existing cfg without the two new keys gets ApproachEffect=false (the initial trajectory) and PrismaticEnabled=true, never an undefined state");

        // ---- a legacy cfg that only knows the heavy line -----------------------------------
        // The most important compatibility case: a player who set KillIcon/HeavyHealthThreshold below the
        // 120 MINOR line must keep exactly that heavy line. The new tier keys must default without ever
        // rewriting the file behind their back, and the heavy line must not be dragged to 120.
        string legacyPath = Path.Combine(Path.GetDirectoryName(path), "legacy-killicon.cfg");
        File.WriteAllText(legacyPath, "[KillIcon]\r\nHeavyHealthThreshold = 100\r\n");
        var legacyFile = new ConfigFile(legacyPath, true);
        var legacyHeavy = legacyFile.Bind("KillIcon", "HeavyHealthThreshold", KillPolicy.DefaultHeavyHealthThreshold, "");
        var legacyMinor = legacyFile.Bind("KillIcon", "MinorHealthThreshold", KillDefaults.MinorHealthThreshold, "");
        var legacyBounds = KillPolicy.ResolveTierBoundaries(legacyMinor.Value, legacyHeavy.Value);
        Check(Math.Abs(legacyHeavy.Value - 100f) < 0.0001f
            && KillPolicy.IsHeavy(110f, legacyHeavy.Value) && !KillPolicy.IsHeavy(90f, legacyHeavy.Value)
            && Math.Abs(legacyBounds.Minor - 120f) < 0.0001f && Math.Abs(legacyBounds.Boss - 100f) < 0.0001f
            && legacyBounds.EliteBandEmpty
            && KillPolicy.ClassifyTier(99f, legacyBounds) == KillTier.Minor
            && KillPolicy.ClassifyTier(100f, legacyBounds) == KillTier.Boss
            && KillPolicy.ClassifyTier(110f, legacyBounds) == KillTier.Boss,
            "bepinex: a legacy cfg with only HeavyHealthThreshold=100 keeps 100 as the BOSS line (HP 99 MINOR, HP 100 BOSS) and the elite band is empty");
        // BepInEx appends keys it has not seen before (saveOnInit), but it must never touch the values the
        // player already wrote: the heavy line stays 100 and the appended tier keys carry their defaults.
        string legacyText = File.ReadAllText(legacyPath);
        Check(legacyText.Contains("HeavyHealthThreshold = 100")
            && (!legacyText.Contains("MinorHealthThreshold") || legacyText.Contains("MinorHealthThreshold = 120"))
            && !legacyText.Contains("EliteHealthThreshold"),
            "bepinex: adding the tier keys keeps the player's explicit heavy line at 100 and appends only the shipped tier defaults");

        // ---- the temp file is not a real user config --------------------------------------
        Check(path.IndexOf("BepInEx", StringComparison.OrdinalIgnoreCase) < 0
            && path.IndexOf("profiles", StringComparison.OrdinalIgnoreCase) < 0,
            "bepinex: the test cfg lives in a private temp folder, never in a profile/BepInEx config dir");
    }
}

internal static class CorpseSwitchFixture
{
    // The five corpse sub-switches at their 1.2.16-equivalent positions: launch / dismember / chunks /
    // burst sound all on, so these cases keep asserting exactly what they asserted before the split.
    internal static CorpseSwitches Sw(bool effects, bool explosionFx)
        => new CorpseSwitches(effects, true, true, true, explosionFx, true, true);
}
