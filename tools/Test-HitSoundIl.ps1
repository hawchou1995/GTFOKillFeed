[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PluginDll,
    [Parameter(Mandatory = $true)][string]$CecilPath,
    [string]$InteropDir
)
# Compiled-IL guard for 1.2.12's hit-sound replacement, prismatic/tier damage numbers and focus outline.
# Source comments cannot prove any of this, so every claim is read out of the shipped assembly:
#   * the two CellSound.Post prefixes exist and are the only mute entry points;
#   * the mute decision reads Plugin.HitSoundMuteVanillaIndicator and NOTHING else - HitSound/Enabled and
#     HitSound/Volume must not be able to un-mute the vanilla layer, so a branch on them may not exist
#     anywhere in the mute path (contract AC10);
#   * the whitelist compile-time array holds exactly the three indicator event names;
#   * the audio path has no per-frame entry point at all (no Update/LateUpdate/FixedUpdate), no scene
#     search and no Unity object lookup, and its only allocation site is the one-time device open;
#   * the voice pool, buffer ceiling and throttle bounds are compile-time constants;
#   * DamageNumbers.Draw still applies NumberTextAlpha once per body pass and keeps the original dark outline
#     and the original per-kind colours as the final fallback, so Opacity=1 with no prismatic hit and no
#     tier is byte-for-byte the 1.2.11 draw path (contract AC12).
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $PluginDll -PathType Leaf)) { throw "Plugin assembly is missing: $PluginDll" }
if (-not (Test-Path -LiteralPath $CecilPath -PathType Leaf)) { throw "Mono.Cecil is required to inspect compiled IL: $CecilPath" }
Add-Type -Path (Resolve-Path -LiteralPath $CecilPath).Path

$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path -LiteralPath $PluginDll).Path)
function Fail([string]$message) { throw "HIT_SOUND_MUTE_IL=FAIL: $message" }

function Get-Type([string]$name) {
    $type = $assembly.MainModule.GetType($name)
    if ($null -eq $type) { $type = $assembly.MainModule.GetType('HawChow.GTFOKillFeed.' + $name) }
    if ($null -eq $type) { Fail "compiled plugin is missing $name" }
    return $type
}

function Get-Method($type, [string]$name) {
    $method = $type.Methods | Where-Object { $_.Name -eq $name } | Select-Object -First 1
    if ($null -eq $method) { Fail "$($type.FullName).$name is missing" }
    return $method
}

# Flattens an instruction stream into display strings (operands included) for reference counting.
function Get-Text($method) {
    $lines = New-Object System.Collections.Generic.List[string]
    if (-not $method.HasBody) { return $lines }
    foreach ($instruction in $method.Body.Instructions) {
        $operand = $instruction.Operand
        if ($null -ne $operand) { $lines.Add($instruction.OpCode.Name + ' ' + $operand.ToString()) }
        else { $lines.Add($instruction.OpCode.Name) }
    }
    return $lines
}

function Count-Match($text, [string]$pattern) {
    return @($text | Where-Object { $_ -match $pattern }).Count
}

# ---- 1. the mute patches ------------------------------------------------------------------------
$postId = Get-Type 'CellSoundPostMutePatch'
$postPos = Get-Type 'CellSoundPostMutePositionPatch'
foreach ($patch in @($postId, $postPos)) {
    $attributes = $patch.CustomAttributes | Where-Object { $_.AttributeType.Name -eq 'HarmonyPatch' }
    if ($null -eq $attributes) { Fail "$($patch.Name) carries no [HarmonyPatch]" }
    $args = @($attributes[0].ConstructorArguments | ForEach-Object { $_.Value })
    $targetType = if ($args.Count -ge 1) { [string]$args[0] } else { '' }
    $targetMethod = if ($args.Count -ge 2) { [string]$args[1] } else { '' }
    if ($targetType -notmatch 'CellSound') { Fail "$($patch.Name) does not target CellSound" }
    if ($targetMethod -ne 'Post') { Fail "$($patch.Name) does not target CellSound.Post" }
    if ($args.Count -lt 3) { Fail "$($patch.Name) must pin a Post overload" }
    $prefix = Get-Method $patch 'Prefix'
    if ($prefix.ReturnType.FullName -ne 'System.Boolean') { Fail "$($patch.Name).Prefix must return bool" }
    $text = Get-Text $prefix
    if ((Count-Match $text 'HitSound::ShouldRunPost') -ne 1) { Fail "$($patch.Name).Prefix must call HitSound.ShouldRunPost exactly once" }
}
$idPrefix = Get-Method $postId 'Prefix'
$idParameters = @($idPrefix.Parameters | ForEach-Object { $_.ParameterType.FullName })
if (-not ($idParameters -contains 'System.UInt32')) { Fail 'the id-only prefix must take the uint event id' }
$posParameters = @((Get-Method $postPos 'Prefix').Parameters | ForEach-Object { $_.ParameterType.FullName })
if (-not ($posParameters -contains 'System.UInt32') -or -not ($posParameters -contains 'UnityEngine.Vector3')) {
    Fail 'the positioned prefix must take the uint event id and the position'
}

# ---- 1b. prefix parameters must match the real target parameters BY NAME ------------------------
# Harmony binds prefix parameters by name. A prefix that declares `position` while the game calls it
# `pos` does not "fail softly": PatchAll throws and the whole plugin fails to load, which is exactly
# how a shipped build once lost the kill cards, the damage numbers and the corpse FX at the same time.
# This gate resolves the real method out of the interop assembly, so the names can never drift again.
if ([string]::IsNullOrWhiteSpace($InteropDir)) {
    Fail 'InteropDir is required: prefix parameter names can only be proven against the real CellSound.Post'
}
$interopDll = Join-Path $InteropDir 'CellSound-ASM.dll'
if (-not (Test-Path -LiteralPath $interopDll -PathType Leaf)) { Fail "CellSound interop assembly is missing: $interopDll" }
$interop = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path -LiteralPath $interopDll).Path)
try {
    $cellSound = $interop.MainModule.GetType('CellSound')
    if ($null -eq $cellSound) { Fail 'CellSound-ASM.dll does not declare the CellSound type' }
    foreach ($patch in @($postId, $postPos)) {
        $prefix = Get-Method $patch 'Prefix'
        # The overload is picked from the compiled prefix's own parameter TYPES (special Harmony
        # parameters excluded), so this check never depends on how the attribute literal was written.
        $own = @($prefix.Parameters | Where-Object { $_.Name -notlike '__*' })
        $ownTypes = ($own | ForEach-Object { $_.ParameterType.FullName }) -join '|'
        $candidate = $cellSound.Methods | Where-Object {
            $_.Name -eq 'Post' -and
            (@($_.Parameters | ForEach-Object { $_.ParameterType.FullName }) -join '|') -eq $ownTypes
        } | Select-Object -First 1
        if ($null -eq $candidate) {
            Fail "$($patch.Name).Prefix declares no CellSound.Post overload that exists in the game ($ownTypes)"
        }
        $targetParameters = @($candidate.Parameters)
        foreach ($parameter in $own) {
            if (-not ($targetParameters | Where-Object { $_.Name -eq $parameter.Name })) {
                Fail ("{0}.Prefix parameter '{1}' is not a parameter of {2} (real names: {3}) - Harmony binds by name and would abort PatchAll" -f
                    $patch.Name, $parameter.Name, 'Post', (($targetParameters | ForEach-Object { $_.Name }) -join ', '))
            }
        }
        foreach ($targetParameter in $targetParameters) {
            if (-not ($own | Where-Object { $_.Name -eq $targetParameter.Name })) {
                Fail "$($patch.Name).Prefix does not declare the target parameter '$($targetParameter.Name)'"
            }
        }
    }
} finally {
    $interop.Dispose()
}
Write-Host 'HIT_SOUND_PATCH_NAMES=PASS;TARGET=CellSound.Post;MODE=interop-verified'

# ---- 2. the mute decision reads only the mute switch ---------------------------------------------
$hitSound = Get-Type 'HitSound'
$policy = Get-Type 'HitSoundPolicy'
$shouldRunPost = Get-Text (Get-Method $hitSound 'ShouldRunPost')
$shouldMute = Get-Text (Get-Method $policy 'ShouldMute')
$mutePath = @($shouldRunPost) + @($shouldMute)
if ((Count-Match $mutePath 'HitSoundMuteVanillaIndicator') -lt 1) { Fail 'the mute path never reads HitSound/MuteVanillaIndicator' }
if ((Count-Match $mutePath 'HitSoundEnabled') -ne 0) { Fail 'HitSound/Enabled must not gate the vanilla mute (AC10)' }
if ((Count-Match $mutePath 'HitSoundVolume') -ne 0) { Fail 'HitSound/Volume must not gate the vanilla mute (AC10)' }
if ((Count-Match $mutePath 'HitSoundStyle') -ne 0) { Fail 'the cue style must not gate the vanilla mute' }
if ((Count-Match $shouldRunPost 'ShouldMute') -ne 1) { Fail 'ShouldRunPost must delegate the decision to HitSoundPolicy.ShouldMute' }

# ---- 3. whitelist constants ---------------------------------------------------------------------
$cctor = Get-Method $policy '.cctor'
$whitelistText = Get-Text $cctor
foreach ($name in @('BULLETHITENEMYINDICATOR', 'BULLETHITENEMYLOCALINDICATOR', 'BULLETHITENEMYWEAKSPOTLOCALINDICATOR')) {
    if ((Count-Match $whitelistText ([regex]::Escape($name))) -lt 1) { Fail "the compiled whitelist is missing $name" }
}
foreach ($name in @('BULLETHITENEMYLOCAL', 'BULLETHITENEMYSYNC', 'BULLETHITENEMYWEAKSPOTLOCAL', 'BULLETHITENEMYWEAKSPOTSYNC')) {
    $exact = 'ldstr ' + $name + '$'
    if ((Count-Match $whitelistText $exact) -ne 0) { Fail "the compiled whitelist must not contain the body event $name" }
}
$whitelistField = $policy.Fields | Where-Object { $_.Name -eq 'MutedEventNames' } | Select-Object -First 1
if ($null -eq $whitelistField) { Fail 'HitSoundPolicy.MutedEventNames is missing' }
# 1.2.18: the raw `ldstr` count stopped being a valid proxy once the same name is also a named constant used by
# the resolution receipt. The contract is the ARRAY SHAPE itself: one string[] of exactly three elements.
$stelemCount = Count-Match $whitelistText 'stelem\.ref'
if ((Count-Match $whitelistText 'newarr System\.String') -lt 1 -or $stelemCount -ne 3) {
    Fail "MutedEventNames must be a three-element string array in IL (newarr System.String + exactly 3 stelem.ref); found stelem=$stelemCount"
}

# ---- 4. no per-frame entry point, no scene search, allocation only at device open -----------------
foreach ($type in @($hitSound, (Get-Type 'CueWavePlayer'), $policy)) {
    foreach ($frame in @('Update', 'LateUpdate', 'FixedUpdate', 'OnGUI')) {
        if (($type.Methods | Where-Object { $_.Name -eq $frame }).Count -ne 0) {
            Fail "$($type.FullName).$frame would run per frame; the audio path must be event driven only"
        }
    }
}
foreach ($type in @($hitSound, (Get-Type 'CueWavePlayer'))) {
    foreach ($method in $type.Methods) {
        $text = Get-Text $method
        if ((Count-Match $text 'FindObjectsOfType|FindObjectsByType|FindObjectOfType|Resources::FindObjectsOfTypeAll|SceneManager') -ne 0) {
            Fail "$($type.Name).$($method.Name) performs a scene/object search"
        }
    }
}
$player = Get-Type 'CueWavePlayer'
$playText = Get-Text (Get-Method $player 'Play')
if ((Count-Match $playText 'newobj') -ne 0) { Fail 'CueWavePlayer.Play allocates an object per cue' }
if ((Count-Match $playText 'AllocHGlobal') -ne 0) { Fail 'CueWavePlayer.Play must reuse the pre-allocated buffer' }
$openText = Get-Text (Get-Method $player 'EnsureOpen')
if ((Count-Match $openText 'AllocHGlobal') -lt 1) { Fail 'CueWavePlayer.EnsureOpen must own the one-time allocation' }
$voiceField = $player.Fields | Where-Object { $_.Name -eq 'VoiceCount' } | Select-Object -First 1
if ($null -eq $voiceField -or -not $voiceField.HasConstant -or [int]$voiceField.Constant -ne 6) {
    Fail 'CueWavePlayer.VoiceCount must stay a compile-time constant of 6'
}
$bufferField = $player.Fields | Where-Object { $_.Name -eq 'MaxCueSamples' } | Select-Object -First 1
if ($null -eq $bufferField -or -not $bufferField.HasConstant -or [int]$bufferField.Constant -le 0) {
    Fail 'CueWavePlayer.MaxCueSamples must stay a positive compile-time constant'
}
foreach ($constant in @('MinIntervalFloor', 'MinIntervalCeiling', 'DefaultMinIntervalSeconds')) {
    $field = $policy.Fields | Where-Object { $_.Name -eq $constant } | Select-Object -First 1
    if ($null -eq $field -or -not $field.HasConstant) { Fail "HitSoundPolicy.$constant must stay a compile-time constant" }
}
if ((Count-Match (Get-Text (Get-Method $policy 'IntervalAllows')) 'MinInterval') -lt 1) {
    Fail 'the throttle must run through the clamped interval, not the raw config value'
}

# ---- 5. damage-number draw equivalence ----------------------------------------------------------
$damageNumbers = Get-Type 'DamageNumbers'
$draw = Get-Text (Get-Method $damageNumbers 'Draw')
# 1.2.24: two body passes (the number's own ink and the proven-stagger green) both take their alpha through the
# same NumberTextAlpha, so fade*opacity still applies exactly once per pass and no raw alpha reaches GUI.color.
if ((Count-Match $draw 'FeedbackPolicy::NumberTextAlpha') -ne 2) { Fail 'DamageNumbers.Draw must apply NumberTextAlpha exactly once per body pass (main + stagger green)' }
if ((Count-Match $draw 'IsPrismatic') -ne 1) { Fail 'DamageNumbers.Draw must ask the policy about the prismatic flare exactly once' }
if ((Count-Match $draw 'DamageFx::Tier\(\)') -ne 1) { Fail 'DamageNumbers.Draw must read the tier exactly once' }
foreach ($literal in @('0.035', '0.04', '0.045')) {
    if ((Count-Match $draw ([regex]::Escape($literal))) -lt 1) { Fail "the original dark outline constant $literal is gone from Draw" }
}
if ((Count-Match $draw 'KillHud::Ink') -lt 1) { Fail 'the original normal-number colour must remain the final fallback' }
if ((Count-Match $draw 'ResolveWeakspotColor') -lt 1) { Fail 'the weakspot-number colour path is gone from Draw' }
foreach ($grey in @('0.65', '0.72', '0.77')) {
    if ((Count-Match $draw ([regex]::Escape($grey))) -lt 1) { Fail "the armor grey constant $grey is gone from Draw" }
}
if ((Count-Match $draw 'NumberOpacity') -lt 1) { Fail 'the 1.2.11 opacity path is gone from Draw' }

# ---- 6. the per-level total can only be cleared by the level-change path ------------------------
$resetCalls = New-Object System.Collections.Generic.List[string]
foreach ($type in $assembly.MainModule.GetTypes()) {
    foreach ($method in $type.Methods) {
        $text = Get-Text $method
        if ((Count-Match $text 'DamageFx::Reset') -gt 0) { $resetCalls.Add($type.Name + '.' + $method.Name) }
    }
}
if ($resetCalls.Count -ne 1 -or $resetCalls[0] -ne 'KillTracker.Reset') {
    Fail ("damage total must be cleared only from KillTracker.Reset; found: " + ($resetCalls -join ', '))
}
$observe = Get-Text (Get-Method (Get-Type 'KillTracker') 'ObserveState')
if ((Count-Match $observe 'KillTracker::Reset') -lt 1) {
    Fail 'KillTracker.ObserveState must clear the per-level state on a level transition'
}


# ---- 7. 1.2.16 kill presentation + cue matrix wiring --------------------------------------------
# The tier and the flight identity must be computed ONCE, at the credited lethal hit, and the same record
# must feed the cue and the card. Nothing here may re-derive the tier from the enemy name or id.
$tracker = Get-Type 'KillTracker'
$complete = Get-Text (Get-Method $tracker 'Complete')
if ((Count-Match $complete 'KillPresentationPolicy::Build') -ne 1) {
    Fail 'KillTracker.Complete must build exactly one KillPresentation per credited kill'
}
if ((Count-Match $complete 'HitSound::OnLocalKill') -ne 1) {
    Fail 'KillTracker.Complete must fire the kill cue exactly once per credited kill'
}
foreach ($key in @('KillMinorHealthThreshold', 'KillHeavyHealthThreshold')) {
    if ((Count-Match $complete $key) -lt 1) { Fail "the kill tier must read the live $key config value" }
}
$tierType = Get-Type 'KillTierBoundaries'
foreach ($field in @('Minor', 'Boss')) {
    if (($tierType.Fields | Where-Object { $_.Name -eq $field }).Count -ne 1) {
        Fail "KillTierBoundaries must expose the $field line (Boss is the existing heavy threshold)"
    }
}
if (($tierType.Fields | Where-Object { $_.Name -eq 'Elite' }).Count -ne 0) {
    Fail 'KillTierBoundaries must not carry a second BOSS line named Elite; the heavy threshold is the only authority'
}
if ((Count-Match $complete 'KillHeavyHealthThreshold') -lt 1) {
    Fail 'the BOSS line must be read from the existing KillIcon/HeavyHealthThreshold key'
}
if ((Count-Match $complete 'KillPresentation') -lt 1 -or (Count-Match $complete 'KillCard::.ctor') -lt 1) {
    Fail 'the kill card must be created from the same presentation (tier + flight) the cue used'
}
# Flight and armor come from game data, never from the killing limb.
$isFlyer = Get-Text (Get-Method $tracker 'IsFlyer')
if ((Count-Match $isFlyer 'get_IsFlyer') -lt 1) { Fail 'KillTracker.IsFlyer must read EnemyBehaviorDataBlock.IsFlyer' }
if ((Count-Match $isFlyer 'EnemyBehaviorData') -lt 1) { Fail 'KillTracker.IsFlyer must use the agent behaviour data' }
if ((Count-Match $isFlyer 'GetBlock') -lt 1) { Fail 'KillTracker.IsFlyer must fall back to the behaviour data block lookup' }
$armorProbe = Get-Text (Get-Method $tracker 'HasArmorLimb')
if ((Count-Match $armorProbe 'get_DamageLimbs') -lt 1 -or (Count-Match $armorProbe 'get_m_type') -lt 1) {
    Fail 'KillTracker.HasArmorLimb must scan the enemy limb layout, not the killing limb'
}
if ((Count-Match $armorProbe 'eLimbDamageType') -lt 1) { Fail 'the armor probe must compare against eLimbDamageType.Armor' }
$presentation = Get-Type 'KillPresentationPolicy'
$build = Get-Text (Get-Method $presentation 'Build')
if ((Count-Match $build 'ClassifyTier') -lt 1 -or (Count-Match $build 'ClassifyFlight') -lt 1) {
    Fail 'KillPresentationPolicy.Build must resolve both the tier and the flight'
}
$flightEnum = Get-Type 'KillFlight'
foreach ($pair in @(@('None', 0), @('Flying', 1), @('ArmoredFlying', 2))) {
    $member = $flightEnum.Fields | Where-Object { $_.Name -eq $pair[0] } | Select-Object -First 1
    if ($null -eq $member -or -not $member.HasConstant -or [int]$member.Constant -ne $pair[1]) {
        Fail "KillFlight must keep the enum member $($pair[0])=$($pair[1])"
    }
}
# The enum names never appear in IL, so the returned values are what gets asserted: the classifier must be
# able to return 2 (ArmoredFlying) as well as 0/1.
$classifyFlight = Get-Text (Get-Method $presentation 'ClassifyFlight')
if ((Count-Match $classifyFlight 'ldc.i4.2') -lt 1) { Fail 'flight classification must be able to report ArmoredFlying (2)' }
if ((Count-Match $classifyFlight 'brtrue|brfalse') -lt 1) { Fail 'flight classification must branch on the flyer flag' }
$cardType = Get-Type 'KillCard'
$flightField = $cardType.Fields | Where-Object { $_.Name -eq 'Flight' } | Select-Object -First 1
if ($null -eq $flightField -or $flightField.FieldType.Name -ne 'KillFlight') {
    Fail 'KillCard must carry a KillFlight so the later head HUD reads one record instead of re-deriving it'
}
# Cue selection must branch on tier and flight (kill-<flight>-<tier> naming).
$cueFile = Get-Text (Get-Method $policy 'CueFile')
foreach ($token in @('TierToken', 'FlightToken', '-weakspot.wav', '-armor.wav', 'kill-')) {
    if ((Count-Match $cueFile ([regex]::Escape($token))) -lt 1) { Fail "HitSoundPolicy.CueFile is missing $token" }
}
$tierToken = Get-Text (Get-Method $policy 'TierToken')
foreach ($token in @('minor', 'elite', 'boss')) {
    if ((Count-Match $tierToken ([regex]::Escape($token))) -lt 1) { Fail "HitSoundPolicy.TierToken is missing the $token line" }
}
$flightToken = Get-Text (Get-Method $policy 'FlightToken')
foreach ($token in @('armor-flyer', 'flyer')) {
    if ((Count-Match $flightToken ([regex]::Escape($token))) -lt 1) { Fail "HitSoundPolicy.FlightToken is missing $token" }
}
# The indicator filter is an EVENT-id filter: it may READ only AK.EVENTS. AK.BUSSES is a bus table whose ids live in
# a different namespace (both tables expose them only through native getters, so the values cannot be compared
# offline). 1.2.18: the bounded info line must NAME the table (a missing BULLETHITENEMYINDICATOR is a bus property,
# not a broken event), but no code may READ it - a bus number fed into an event filter could silence an unrelated event.
if (($hitSound.Fields | Where-Object { $_.Name -eq 'MutedEventTypeNames' }).Count -ne 0) {
    Fail 'HitSound must not carry a bus-table scan list; the mute filter is event ids only'
}
$busAccess = 0
foreach ($method in $hitSound.Methods) {
    if (-not $method.HasBody) { continue }
    foreach ($instruction in $method.Body.Instructions) {
        $operand = $instruction.Operand
        $declaring = $null
        if ($operand -is [Mono.Cecil.MemberReference]) { $declaring = $operand.DeclaringType.FullName }
        elseif ($operand -is [Mono.Cecil.TypeReference]) { $declaring = $operand.FullName }
        if ($declaring -eq 'AK.BUSSES') { $busAccess++ }
    }
}
if ($busAccess -ne 0) {
    Fail "HitSound must never read the AK.BUSSES table (found $busAccess member/type references); a bus number is not an event id"
}
if ((Count-Match (Get-Text (Get-Method $hitSound 'ResolveMutedIds')) 'AK\.BUSSES') -lt 1) {
    Fail 'ResolveMutedIds must name the bus table in one bounded info line, so a missing BULLETHITENEMYINDICATOR is never reported as a broken event id'
}
if ((Count-Match (Get-Text (Get-Method $hitSound 'ResolveMutedIds')) 'AK.EVENTS') -lt 1) {
    Fail 'ResolveMutedIds must resolve its ids from AK.EVENTS'
}
$killCue = Get-Method $hitSound 'OnLocalKill'
$killCueParameters = @($killCue.Parameters | ForEach-Object { $_.ParameterType.Name })
if (($killCueParameters -join ',') -ne 'KillTier,KillFlight') {
    Fail "HitSound.OnLocalKill must take the resolved tier and flight; found ($($killCueParameters -join ','))"
}
$maxSamplesField = $player.Fields | Where-Object { $_.Name -eq 'MaxCueSamples' } | Select-Object -First 1
if ($null -eq $maxSamplesField -or -not $maxSamplesField.HasConstant -or [int]$maxSamplesField.Constant -ne 30720) {
    Fail 'CueWavePlayer.MaxCueSamples must stay exactly 30720 (640 ms at 48 kHz)'
}
Write-Host 'KILL_PRESENTATION_IL=PASS;TIER=KillPresentationPolicy.Build(BOSS=HeavyHealthThreshold);FLIGHT=EnemyBehaviorDataBlock.IsFlyer;ARMOR=limb-layout;CUES=kill-<flight>-<tier>;MUTE_TABLE=AK.EVENTS'

# ---- 8. production wiring of the shared number placement and the cue parameters (review A5) -------
# DamageNumbers.Draw must compute the placement through the SAME policy function the locked prismatic
# tests drive, and it must pass every input explicitly: a forgotten seed/side/hit point/opacity/resolution/
# base size changes the argument list or the IL here, so a test can never pass against a private copy of
# the formula while the production call quietly differs.
$damageNumbers = Get-Type 'DamageNumbers'
$drawText = Get-Text (Get-Method $damageNumbers 'Draw')
if ((Count-Match $drawText 'FeedbackPolicy::NumberPose') -ne 1) {
    Fail 'DamageNumbers.Draw must place every number through FeedbackPolicy.NumberPose exactly once'
}
$pose = Get-Method (Get-Type 'FeedbackPolicy') 'NumberPose'
$poseNames = ($pose.Parameters | ForEach-Object { $_.Name }) -join ','
if ($poseNames -ne 'prismatic,approach,seed,side,age,lifetime,opacity,hitX,hitY,width,height,resolution,kind,baseSize,x,y,size,alpha,straightX,straightY') {
    Fail "FeedbackPolicy.NumberPose must keep its placement inputs (prismatic,approach,seed,side,age,lifetime,opacity,hitX,hitY,width,height,resolution,kind,baseSize,...); found $poseNames"
}
foreach ($argument in @('DamageNumbers/Number::FlySeed', 'DamageNumbers/Number::Side', 'Plugin::DamageNumberSize', 'Plugin::DamageNumberOpacity', 'Plugin::DamageNumberApproachEffect', 'Screen::get_width', 'Screen::get_height')) {
    if ((Count-Match $drawText ([regex]::Escape($argument))) -lt 1) {
        Fail "DamageNumbers.Draw must pass $argument into the shared placement instead of a hardcoded value"
    }
}
foreach ($gone in @('SegmentedAxis', 'FlightTarget', 'PrismaticFlightPoint', 'PrismaticApproachT', 'ApproachT', 'NumberDriftX', 'NumberDriftY')) {
    if ((Count-Match $drawText ([regex]::Escape($gone))) -ne 0) {
        Fail "DamageNumbers.Draw still re-derives $gone instead of using the shared placement"
    }
}
# 1.2.16 switches: ApproachEffect is read live in Draw (proven above), and the two DamageFx switches are
# read by their own single consumer, so a saved cfg reaches numbers already on the screen.
$fxType = Get-Type 'DamageFx'
$prismaticText = Get-Text (Get-Method $fxType 'IsPrismatic')
$prismaticSwitchText = Get-Text (Get-Method $fxType 'PrismaticEnabled')
if ((Count-Match $prismaticSwitchText 'Plugin::DamageFxPrismaticEnabled') -lt 1) {
    Fail 'DamageFx.PrismaticEnabled must read the live Plugin.DamageFxPrismaticEnabled field, otherwise PrismaticEnabled=false cannot reach a number already on screen'
}
if ((Count-Match $prismaticText 'DamageFx::PrismaticEnabled') -lt 1) {
    Fail 'DamageFx.IsPrismatic must combine the threshold with the live PrismaticEnabled switch'
}
if ((Count-Match $prismaticText 'DamageFxPolicy::IsPrismatic') -lt 1) {
    Fail 'DamageFx.IsPrismatic must route through the tested DamageFxPolicy.IsPrismatic(damage, threshold, enabled) overload'
}
$tiersText = Get-Text (Get-Method $fxType 'TiersEnabled')
if ((Count-Match $tiersText 'Plugin::DamageFxTiersEnabled') -lt 1) {
    Fail 'DamageFx.TiersEnabled must read the live Plugin.DamageFxTiersEnabled field so a saved cfg removes the tier ink immediately'
}
if ((Count-Match $drawText 'DamageFxPolicy::Paint') -lt 1) {
    Fail 'DamageNumbers.Draw must take the foil/tier composition from DamageFxPolicy.Paint instead of re-deciding it inline'
}
if ((Count-Match $drawText 'DamageFx::IsPrismatic') -lt 1) {
    Fail 'DamageNumbers.Draw must take the prismatic decision from DamageFx.IsPrismatic (threshold AND switch)'
}
# The cue routing: the hit cue carries the hit kind the tracker captured, and the kill cue carries the one
# KillPresentation the credit built (never a re-derived tier/flight).
$tracker = Get-Type 'KillTracker'
$recordHitText = Get-Text (Get-Method $tracker 'RecordHit')
if ((Count-Match $recordHitText 'HitSound::OnLocalHit') -ne 1) {
    Fail 'KillTracker.RecordHit must fire exactly one hit cue per credited local hit'
}
if ((Count-Match $recordHitText 'DamageRoot::Kind') -lt 1) {
    Fail 'the hit cue must receive the captured hit kind (armour/weakspot/normal), not a constant'
}
$completeText = Get-Text (Get-Method $tracker 'Complete')
if ((Count-Match $completeText 'KillPresentation::Tier') -lt 1 -or (Count-Match $completeText 'KillPresentation::Flight') -lt 1) {
    Fail 'the kill cue must receive the presentation tier and flight built at the credit'
}
if ((Count-Match $completeText 'HitSound::OnLocalKill') -ne 1) {
    Fail 'KillTracker.Complete must fire exactly one kill cue per credited kill'
}

# ---- 9. plugin teardown releases the audio device (review A2) ------------------------------------
# Compiled wiring only: BepInEx 6 calls BasePlugin.Unload on unload/reload, and HitSound.Shutdown frees the
# six waveOut handles, their WAVEHDRs and the unmanaged buffers. This asserts the call is really wired and
# guarded; it does NOT claim a runtime unload was exercised.
$pluginType = Get-Type 'Plugin'
$unloadMethod = Get-Method $pluginType 'Unload'
if (-not $unloadMethod.IsPublic -or -not $unloadMethod.IsVirtual) {
    Fail 'Plugin.Unload must override BasePlugin.Unload (public virtual) so BepInEx actually calls it'
}
if ($unloadMethod.ReturnType.FullName -ne 'System.Boolean') {
    Fail "Plugin.Unload must keep the BasePlugin bool signature; found $($unloadMethod.ReturnType.FullName)"
}
$unloadText = Get-Text $unloadMethod
if ((Count-Match $unloadText 'HitSound::Shutdown') -ne 1) {
    Fail 'Plugin.Unload must release the waveOut handles through HitSound.Shutdown exactly once'
}
if ((Count-Match $unloadText 'HitSoundEnabled|HitSoundMuteVanillaIndicator|HitSoundVolume|HitSoundStyle') -ne 0) {
    Fail 'audio teardown must not be gated by the independent cue/mute switches: both stay independent of the release'
}
if ((Count-Match $unloadText 'BasePlugin::Unload') -ne 1) {
    Fail 'Plugin.Unload must still call base.Unload() so the framework return semantics are unchanged'
}
$unloadCatches = @($unloadMethod.Body.ExceptionHandlers | Where-Object { $_.HandlerType.ToString() -eq 'Catch' })
if ($unloadCatches.Count -lt 1) {
    Fail 'Plugin.Unload must guard HitSound.Shutdown so a native failure cannot abort the plugin teardown'
}
$shutdownText = Get-Text (Get-Method (Get-Type 'HitSound') 'Shutdown')
if ((Count-Match $shutdownText 'CueWavePlayer::Shutdown') -ne 1) {
    Fail 'HitSound.Shutdown must release the device through CueWavePlayer.Shutdown exactly once'
}
foreach ($reset in @('CueWavePlayer::Shutdown', 'HitSoundState::Reset')) {
    if ((Count-Match $shutdownText ([regex]::Escape($reset))) -lt 1) {
        Fail "HitSound.Shutdown must also $reset so a reload starts from a clean state"
    }
}


$assembly.Dispose()
Write-Host 'HIT_SOUND_MUTE_IL=PASS'







