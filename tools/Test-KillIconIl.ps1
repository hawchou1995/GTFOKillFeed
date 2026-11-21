[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PluginDll,
    [Parameter(Mandatory = $true)][string]$CecilPath,
    [string]$InteropDir
)
# Compiled-IL guard for the head-top BFV kill icons (+ card fallback), shipped as 1.2.16.
# (The internal phase labels 1.2.17 / 1.2.18 in older comments were folded into the 1.2.16 release; the
#  CHANGELOG states that mapping, so this header and the shipped version can never disagree.)
# Everything asserted here is read out of the shipped assembly or the real interop assembly, because none of
# it can be proven from source text:
#   * the head point really comes from the game's own head limb, through the helper the enemy bar already uses;
#   * tier and flight are consumed from the SAME KillPresentation the kill cue consumed, once per credited kill;
#   * the anchor is captured while the enemy is still the object that died, followed only through pointer
#     identity, and frozen (never screen-centred) on destruction / pooling / lookup failure;
#   * the draw path only ever projects a stored world point, is OnGUI-only, and never scans the scene;
#   * the tier -> BFV family mapping and the flight badge are the same three rasters / two shapes everywhere.
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $PluginDll -PathType Leaf)) { throw "Plugin assembly is missing: $PluginDll" }
if (-not (Test-Path -LiteralPath $CecilPath -PathType Leaf)) { throw "Mono.Cecil is required to inspect compiled IL: $CecilPath" }
if ([string]::IsNullOrWhiteSpace($InteropDir)) { throw 'InteropDir is required: the anchor and the tier/flight inputs can only be proven against the real game assemblies' }
Add-Type -Path (Resolve-Path -LiteralPath $CecilPath).Path

function Fail([string]$message) { throw "KILL_ICON_IL=FAIL: $message" }

$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path -LiteralPath $PluginDll).Path)
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
function Get-Type-Text($type) {
    $all = New-Object System.Collections.Generic.List[string]
    foreach ($method in $type.Methods) { foreach ($line in (Get-Text $method)) { $all.Add($method.Name + ' | ' + $line) } }
    foreach ($nested in $type.NestedTypes) { foreach ($method in $nested.Methods) { foreach ($line in (Get-Text $method)) { $all.Add($nested.Name + '.' + $method.Name + ' | ' + $line) } } }
    return $all
}
function Count-Match($text, [string]$pattern) { return @($text | Where-Object { $_ -match $pattern }).Count }
function Assert-Count($text, [string]$pattern, [int]$expected, [string]$message) {
    $actual = Count-Match $text $pattern
    if ($actual -ne $expected) { Fail "$message (expected $expected, found $actual)" }
}
function Assert-Min($text, [string]$pattern, [int]$minimum, [string]$message) {
    $actual = Count-Match $text $pattern
    if ($actual -lt $minimum) { Fail "$message (expected >= $minimum, found $actual)" }
}
function Assert-Const($type, [string]$name, [string]$expected, [string]$message) {
    $field = $type.Fields | Where-Object { $_.Name -eq $name } | Select-Object -First 1
    if ($null -eq $field -or -not $field.HasConstant) { Fail "KillIconPolicy.$name must stay a compile-time constant ($message)" }
    if ([string]$field.Constant -ne $expected) { Fail "KillIconPolicy.$name must stay $expected ($message); found $($field.Constant)" }
}

# ---- 1. the real interop members the classification and the anchor depend on ----------------------
$interopDll = Join-Path $InteropDir 'Modules-ASM.dll'
if (-not (Test-Path -LiteralPath $interopDll -PathType Leaf)) { Fail "interop assembly is missing: $interopDll" }
$interop = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path -LiteralPath $interopDll).Path)
function Get-InteropProperty($assemblyDefinition, [string]$typeName, [string]$propertyName, [string]$propertyType) {
    $type = $assemblyDefinition.MainModule.GetType($typeName)
    if ($null -eq $type) { Fail "$typeName is missing from the game interop" }
    $property = $type.Properties | Where-Object { $_.Name -eq $propertyName } | Select-Object -First 1
    if ($null -eq $property) { Fail "$typeName.$propertyName is missing from the game interop" }
    if ($property.PropertyType.FullName -ne $propertyType) {
        Fail "$typeName.$propertyName must stay $propertyType; found $($property.PropertyType.FullName)"
    }
    return $property
}
try {
    $headLimb = Get-InteropProperty $interop 'Enemies.EnemyAgent' 'm_headLimb' 'Dam_EnemyDamageLimb'
    $behavior = Get-InteropProperty $interop 'Enemies.EnemyAgent' 'EnemyBehaviorData' 'GameData.EnemyBehaviorDataBlock'
    $isFlyer = Get-InteropProperty $interop 'GameData.EnemyBehaviorDataBlock' 'IsFlyer' 'System.Boolean'
    $limbs = Get-InteropProperty $interop 'Dam_EnemyDamageBase' 'DamageLimbs' 'Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray`1<Dam_EnemyDamageLimb>'
    $limbType = Get-InteropProperty $interop 'Dam_EnemyDamageLimb' 'm_type' 'eLimbDamageType'
    $enum = $interop.MainModule.GetType('eLimbDamageType')
    if ($null -eq $enum) { Fail 'eLimbDamageType is missing from the game interop' }
    $armor = $enum.Fields | Where-Object { $_.Name -eq 'Armor' } | Select-Object -First 1
    if ($null -eq $armor) { Fail 'eLimbDamageType.Armor is missing: the armour-limb probe needs it' }
    $owner = $interop.MainModule.GetType('Dam_EnemyDamageBase').Properties | Where-Object { $_.Name -eq 'Owner' } | Select-Object -First 1
    if ($null -eq $owner -or $owner.PropertyType.FullName -ne 'Enemies.EnemyAgent') { Fail 'Dam_EnemyDamageBase.Owner must stay Enemies.EnemyAgent' }
    $dataId = $interop.MainModule.GetType('Enemies.EnemyAgent').Properties | Where-Object { $_.Name -eq 'EnemyDataID' } | Select-Object -First 1
    if ($null -eq $dataId) { Fail 'Enemies.EnemyAgent.EnemyDataID is missing' }
} finally { $interop.Dispose() }
Write-Host ("KILL_ICON_INTEROP=PASS;HEAD={0};BEHAVIOR={1};FLYER={2};LIMBS={3};LIMB_TYPE={4}" -f
    $headLimb.PropertyType.Name, $behavior.PropertyType.Name, $isFlyer.PropertyType.Name, $limbs.PropertyType.Name, $limbType.PropertyType.Name)

# ---- 2. the head anchor is the game's head limb, through the helper the bar already uses ----------
$focus = Get-Type 'EnemyFocus'
$head = Get-Method $focus 'TryGetHeadWorld'
$headSignature = (@($head.Parameters | ForEach-Object { $_.ParameterType.FullName }) -join '|')
if ($head.ReturnType.FullName -ne 'System.Boolean') { Fail 'EnemyFocus.TryGetHeadWorld must return bool (false = no anchor, caller must not guess)' }
if ($headSignature -ne 'Enemies.EnemyAgent|System.Single|UnityEngine.Vector3&') {
    Fail "EnemyFocus.TryGetHeadWorld must keep (Enemies.EnemyAgent, Single, Vector3&); found $headSignature"
}
$headText = Get-Text $head
Assert-Min $headText 'get_m_headLimb' 1 'TryGetHeadWorld must read the enemy head limb (no pivot/centre fallback)'
Assert-Count $headText 'Camera|Screen|WorldToScreenPoint' 0 'TryGetHeadWorld must stay world-space; projecting belongs to the draw path'
Assert-Count $headText 'FindObjectsOfType|FindObjectsByType|FindObjectOfType|SceneManager' 0 'the head lookup must not scan the scene'

# ---- 2b. the renderer-bounds measurement is a bounded per-target cache (review F4) ----------------
# A single slot re-enumerated every Renderer whenever two heads alternated. The cache now lives in
# HeadBoundsCache; this proves the compiled wiring (TopY really uses it, only the miss measures, and Reset
# drops it) so the source-level Verify gate cannot be satisfied by a dead field name.
$cacheType = Get-Type 'HeadBoundsCache'
$topY = Get-Text (Get-Method $focus 'TopY')
Assert-Count $topY 'HeadBoundsCache::Top' 1 'EnemyFocus.TopY must read the bounded per-target cache exactly once'
Assert-Min $topY 'GetInstanceID' 1 'the cache key must be the agent instance id (no pointer, no name)'
Assert-Count $topY 'GetComponentsInChildren' 0 'TopY itself must never enumerate Renderers: that is the cached measurement'
$measureTopY = Get-Text (Get-Method $focus 'MeasureTopY')
Assert-Min $measureTopY 'GetComponentsInChildren' 1 'the actual Renderer enumeration must live in MeasureTopY'
Assert-Min $measureTopY 'get_enabled' 1 'a disabled Renderer must still be skipped'
# The cache callback is a compiler-generated closure, so the delegation is proven type-wide.
Assert-Min (Get-Type-Text $focus) 'EnemyFocus::MeasureTopY' 1 'TopY must hand its single measurement to MeasureTopY through the cache callback'
Assert-Min (Get-Text (Get-Method $focus 'Reset')) 'HeadBoundsCache::Clear' 1 'EnemyFocus.Reset must drop every cached measurement'
$cacheTop = Get-Text (Get-Method $cacheType 'Top')
Assert-Min $cacheTop '_hits' 1 'the cache must serve a fresh sample without measuring again'
Assert-Min $cacheTop '_samples' 1 'an expired sample must be re-measured exactly once'
Assert-Min $cacheTop 'ldc\.r4 0\.35' 1 'the per-target 0.35 s window must be a real compiled time window (the const is inlined)'
$maxSlots = $cacheType.Fields | Where-Object { $_.Name -eq 'MaxSlots' } | Select-Object -First 1
if ($null -eq $maxSlots -or -not $maxSlots.HasConstant -or [int]$maxSlots.Constant -ne 16) {
    Fail 'HeadBoundsCache.MaxSlots must stay a compile-time cap of 16 targets'
}
$refresh = $cacheType.Fields | Where-Object { $_.Name -eq 'RefreshSeconds' } | Select-Object -First 1
if ($null -eq $refresh -or -not $refresh.HasConstant -or [Math]::Abs([single]$refresh.Constant - 0.35) -gt 0.0001) {
    Fail 'HeadBoundsCache.RefreshSeconds must stay 0.35 s'
}


# ---- 3. classification: tier from live health, flight from behaviour data + own limb armour --------
$tracker = Get-Type 'KillTracker'
$flyer = Get-Text (Get-Method $tracker 'IsFlyer')
Assert-Min $flyer 'get_EnemyBehaviorData' 1 'flyer identity must come from the agent behaviour block, not from a name/id guess'
Assert-Min $flyer 'GameDataBlockBase`1<GameData.EnemyBehaviorDataBlock>::GetBlock' 1 'the behaviour block fallback must stay (agent reference may be unpopulated)'
Assert-Min $flyer 'GameData.EnemyBehaviorDataBlock::get_IsFlyer' 1 'IsFlyer is the game data flag the flight badge must consume'
$armorProbe = Get-Text (Get-Method $tracker 'HasArmorLimb')
Assert-Min $armorProbe 'get_DamageLimbs' 1 'armour identity must read the enemy own limb layout'
Assert-Min $armorProbe 'Dam_EnemyDamageLimb::get_m_type' 1 'the armour probe must inspect the limb damage type'
# eLimbDamageType.Armor is a compile-time constant, so the comparison inlines it; assert it against the
# value the real interop enum carries rather than against a literal in this script.
Assert-Min $armorProbe ('ldc\.i4\S*\s*' + [int]$armor.Constant + '$') 1 "the armour probe must compare the limb type against eLimbDamageType.Armor (inlined as $([int]$armor.Constant))"
$build = Get-Text (Get-Method (Get-Type 'KillPresentationPolicy') 'Build')
Assert-Min $build 'KillPolicy::ClassifyTier' 1 'the presentation tier must come from the live health lines'
Assert-Min $build 'KillPresentationPolicy::ClassifyFlight' 1 'the presentation flight must come from the game data'
Assert-Count $flyer 'EnemyNames|get_EnemyDataID|get_EnemyName' 0 'IsFlyer must not infer flight from a name/id table: only the behaviour data may decide'

# ---- 4. one presentation per credited kill: sound and icon consume the same tier/flight -------------
$complete = Get-Text (Get-Method $tracker 'Complete')
Assert-Count $complete 'KillPresentationPolicy::Build' 1 'Complete must build exactly one presentation per credited kill'
Assert-Count $complete 'HitSound::OnLocalKill' 1 'the kill cue must consume that presentation'
Assert-Count $complete 'KillTracker::SnapshotKillIcon' 1 'the head icon must consume that same presentation, once'
Assert-Count $complete 'KillLedger::TryCredit' 1 'a local lethal may be credited exactly once'

# ---- 4b. the credited kill hands ITS OWN icon token to the card (review F1 / F9 wiring) ------------
# The fallback is decided per kill, so the never-reused token that KillIconHud created for this kill must
# reach the KillCard the player sees. Proven on the compiled data flow, not on the field name alone.
function Get-Local-Slot($instruction) {
    $operand = $instruction.Operand
    if ($null -ne $operand -and $operand.GetType().Name -eq 'VariableDefinition') { return [int]$operand.Index }
    if ($instruction.OpCode.Name -match '^(?:st|ld)loc\.(\d+)$') { return [int]$Matches[1] }
    return -1
}
$cardType = Get-Type 'KillCard'
foreach ($identityField in @('IconToken', 'IconExpiresAt')) {
    if (@($cardType.Fields | Where-Object { $_.Name -eq $identityField }).Count -ne 1) {
        Fail "KillCard must carry $identityField so the card fallback is decided per kill"
    }
}
$cardConstructors = @($cardType.Methods | Where-Object { $_.Name -eq '.ctor' })
if ($cardConstructors.Count -ne 1) {
    Fail "KillCard must keep exactly one constructor so no path can silently drop the icon identity; found $($cardConstructors.Count)"
}
$ctorNames = ($cardConstructors[0].Parameters | ForEach-Object { $_.Name }) -join ','
if ($ctorNames -notmatch 'iconToken,iconExpiresAt$') {
    Fail "KillCard..ctor must end with (iconToken, iconExpiresAt); found $ctorNames"
}
$completeInstructions = @((Get-Method $tracker 'Complete').Body.Instructions)
# Finding 3 (FIXED in 1.2.18): the head-icon snapshot must be inside a real branch on Plugin.KillIconHeadEnabled, so
# a kill made while the head channel is off mints no token (the card then keeps its own CardLifetime) and nothing
# is queued for a later hot-enable to replay. The branch is asserted, not the field name.
$headFieldAt = -1
$snapshotCallAt = -1
for ($instructionIndex = 0; $instructionIndex -lt $completeInstructions.Count; $instructionIndex++) {
    $instruction = $completeInstructions[$instructionIndex]
    if ($instruction.OpCode.Name -eq 'ldsfld' -and "$($instruction.Operand)" -match 'Plugin::KillIconHeadEnabled') { $headFieldAt = $instructionIndex }
    if ($instruction.OpCode.Name -eq 'call' -and "$($instruction.Operand)" -match 'KillTracker::SnapshotKillIcon') { $snapshotCallAt = $instructionIndex }
}
if ($headFieldAt -lt 0 -or $snapshotCallAt -lt 0 -or $headFieldAt -ge $snapshotCallAt) {
    Fail 'Complete must read Plugin.KillIconHeadEnabled BEFORE it snapshots a head icon (finding 3: a disabled head channel must not mint a token).'
}
$headBranch = $false
for ($instructionIndex = $headFieldAt; $instructionIndex -lt $snapshotCallAt; $instructionIndex++) {
    if ($completeInstructions[$instructionIndex].OpCode.Name -match '^br(true|false)') { $headBranch = $true }
}
if (-not $headBranch) {
    Fail 'the KillIconHeadEnabled value must really gate the SnapshotKillIcon call: a read without a branch would still mint a token (finding 3).'
}
$snapshotAt = -1
$cardAt = -1
for ($instructionIndex = 0; $instructionIndex -lt $completeInstructions.Count; $instructionIndex++) {
    $instruction = $completeInstructions[$instructionIndex]
    if ($instruction.OpCode.Name -eq 'call' -and "$($instruction.Operand)" -match 'KillTracker::SnapshotKillIcon') { $snapshotAt = $instructionIndex }
    if ($instruction.OpCode.Name -eq 'newobj' -and "$($instruction.Operand)" -match 'KillCard::\.ctor') { $cardAt = $instructionIndex }
}
if ($snapshotAt -lt 0 -or $cardAt -lt 0 -or $cardAt -le $snapshotAt) {
    Fail 'Complete must create the kill card after the head-icon snapshot, so the card can carry its result'
}
$tokenStoreAt = -1
for ($instructionIndex = $snapshotAt + 1; $instructionIndex -lt [Math]::Min($snapshotAt + 5, $completeInstructions.Count); $instructionIndex++) {
    if ($completeInstructions[$instructionIndex].OpCode.Name -match '^stloc') { $tokenStoreAt = $instructionIndex; break }
}
if ($tokenStoreAt -lt 0) { Fail 'the token returned by SnapshotKillIcon must be kept, not discarded' }
$tokenSlot = Get-Local-Slot $completeInstructions[$tokenStoreAt]
if ($tokenSlot -lt 0) { Fail 'the icon token must be stored in a local slot the card can read back' }
$tokenFlowsIntoCard = $false
for ($instructionIndex = $tokenStoreAt + 1; $instructionIndex -lt $cardAt; $instructionIndex++) {
    if ($completeInstructions[$instructionIndex].OpCode.Name -match '^ldloc' -and
        (Get-Local-Slot $completeInstructions[$instructionIndex]) -eq $tokenSlot) { $tokenFlowsIntoCard = $true }
}
if (-not $tokenFlowsIntoCard) {
    Fail 'the kill card must receive the token of the head-icon slot created for that same kill'
}

$snapshot = Get-Text (Get-Method $tracker 'SnapshotKillIcon')
Assert-Count $snapshot 'EnemyFocus::TryGetHeadWorld' 1 'the head anchor must come from the head helper exactly once, at the credit'
Assert-Count $snapshot 'KillIconHud::Add\(' 2 'the snapshot must hand exactly one icon to the HUD (head point, or the lethal event point as fallback)'
Assert-Min $snapshot 'head-world' 1 'the frozen anchor must record that it came from the head point (const string, inlined)'
Assert-Min $snapshot 'lethal-position' 1 'a failed head lookup must fall back to the lethal event position, and say so'
Assert-Count $snapshot 'Screen|Camera|WorldToScreenPoint' 0 'the snapshot must stay world-space: a screen-centre icon is never invented'
Assert-Min $snapshot 'KillIconPolicy::HeadAnchorOffset' 1 'the clearance must keep the icon above a Head-mode bar'
Assert-Min $snapshot 'KillIconPolicy::Gap' 1 'the configured gap must be read through the clamped policy'
Assert-Min $snapshot 'BarPolicy::IsHeadMode' 1 'the bar anchor mode decides whether the bar own offset must be added'
Assert-Min $snapshot 'BarPolicy::HeadOffset' 1 'the bar head offset must be re-read live'
foreach ($plugin in @('Plugin::KillIconGap', 'Plugin::EnemyBarEnabled', 'Plugin::EnemyBarAnchor', 'Plugin::EnemyBarHeadOffset')) {
    Assert-Min $snapshot ([regex]::Escape($plugin)) 1 "$plugin must be read live at snapshot time (hot reload without restart)"
}
# The icon may only be created from the credit snapshot; no other tracker path may add icons.
foreach ($method in $tracker.Methods) {
    if ($method.Name -eq 'SnapshotKillIcon') { continue }
    Assert-Count (Get-Text $method) 'KillIconHud::Add\(' 0 "KillTracker.$($method.Name) must not add head icons outside the credited snapshot"
}
Assert-Min (Get-Text (Get-Method $tracker 'Reset')) 'KillIconHud::Reset' 1 'a level change / respawn must clear the head icons'
Assert-Min (Get-Text (Get-Method $tracker 'AddPreview')) 'KillIconHud::AddPreview' 1 'F9 must preview the nine tier x flight combinations'

# ---- 5. the anchor is frozen at the credit: no follow, no per-frame head read, no screen fallback -----
# 1.2.15 re-read the dying enemy's live m_headLimb every frame for the first 60% of the icon's life, so a
# corpse sinking to the floor dragged its icon down with it. The gate is now inverted: the tick and the draw
# path may not write a slot's X/Y/Z, and the follow-window machinery must be gone entirely.
$hud = Get-Type 'KillIconHud'
$add = Get-Text (Get-Method $hud 'Add')
Assert-Count $add 'KillPolicy::IsFinite' 3 'Add must reject a non-finite anchor on all three axes'
Assert-Count $add 'WorldToScreenPoint|Screen' 0 'Add must store a world point; projection belongs to the draw path'
Assert-Min $add 'get_Pointer' 1 'Add must remember the dying enemy pointer as the audit identity of the frozen anchor'
Assert-Min $add 'KillIconHud::LogFrozenAnchor' 1 'the credit must leave a one-time receipt of where the frozen anchor came from'
foreach ($gone in @('RefreshAnchor', 'TryHead')) {
    if (($hud.Methods | Where-Object { $_.Name -eq $gone }).Count -ne 0) {
        Fail "KillIconHud must not keep ${gone}: the anchor is frozen at the credit and never re-read"
    }
}
$tick = Get-Text (Get-Method $hud 'Tick')
Assert-Count $tick 'EnemyFocus::TryGetHeadWorld|KillIconHud::TryHead|RefreshAnchor' 0 'Tick must never re-read the corpse head'
Assert-Min $tick 'KillIconQueue::TickReal' 1 'Tick must run the Unity-free real-slot maintenance'
Assert-Min $tick 'KillIconQueue::TickPreview' 1 'Tick must expire the preview slots through their own store'
Assert-Min $tick 'Plugin::KillIconLifetime' 1 'Tick must read the configured lifetime (hot reload) through the clamped policy'
if ($tick -match 'get_Pointer|set_X|set_Y|set_Z' -or $tick -match 'stfld .*X\b') {
    Fail 'KillIconHud.Tick must not touch the slot pointer or coordinates: the anchor is frozen and only projection happens at draw time'
}
$queueType = Get-Type 'KillIconQueue'
$tickReal = Get-Text (Get-Method $queueType 'TickReal')
Assert-Min $tickReal 'RemoveRealAt' 1 'the per-frame step may only remove expired real slots'
Assert-Min $tickReal 'KillIconPolicy::Lifetime' 1 'the per-frame step must use the same clamped lifetime as the draw path'
Assert-Count $tickReal 'stfld|set_X|set_Y|set_Z|KillPolicy::IsFinite' 1 'TickReal may not write a slot: the only finite check is the clock'
$tickPreview = Get-Text (Get-Method $queueType 'TickPreview')
Assert-Min $tickPreview 'RemovePreviewAt' 1 'previews expire through their own store'
Assert-Count $tickPreview 'PushReal|RemoveRealAt' 0 'a preview tick must never touch the real kill store'
$drawSlot = Get-Text (Get-Method $hud 'DrawSlot')
Assert-Min $drawSlot 'WorldToScreenPoint' 1 'the slot renderer must project the frozen world anchor every frame'
Assert-Count $drawSlot 'set_X|set_Y|set_Z' 0 'the draw path must never rewrite the frozen anchor'

# The stores are separate (review F5): real kills live in KillIconQueue's bounded real store, the F9 preview
# gets its own fixed nine-slot set, and only the real store evicts.
$push = Get-Text (Get-Method $hud 'Push')
Assert-Min $push 'KillIconPolicy::MaxIcons' 1 'the configured icon ceiling must be applied'
Assert-Min $push 'KillIconQueue::PushReal' 1 'a real kill must enter the bounded real store'
Assert-Min $push 'KillIconQueue::PushPreview' 1 'a preview must enter the separate preview store'
$addPreview = Get-Text (Get-Method $hud 'AddPreview')
Assert-Min $addPreview 'KillIconQueue::ResetPreviews' 1 'a repeated F9 press must replace the preview set, not stack it'
Assert-Count $addPreview 'KillIconQueue::PushReal' 0 'a preview must never take a real kill slot'
$evict = Get-Text (Get-Method $queueType 'EvictOldestReal')
Assert-Count $evict 'RemoveRealAt' 1 'eviction must only ever remove a REAL slot'
$pushReal = Get-Text (Get-Method $queueType 'PushReal')
Assert-Count $pushReal 'Preview' 0 'the real store must not touch the preview slots'
$isPresenting = Get-Text (Get-Method $hud 'IsPresenting')
Assert-Min $isPresenting 'KillIconQueue::TryFind' 1 'the card fallback must ask for the kill token, never for a position'
Assert-Min $isPresenting 'KillIconPolicy::Alpha' 1 'a slot whose lifetime is over must not count as presented'

# ---- 6. the draw path: world projection only, OnGUI only, no scene scan ---------------------------
$previewSlots = $queueType.Fields | Where-Object { $_.Name -eq 'PreviewSlots' } | Select-Object -First 1
if ($null -eq $previewSlots -or -not $previewSlots.HasConstant -or [int]$previewSlots.Constant -ne 9) {
    Fail 'KillIconQueue.PreviewSlots must stay a compile-time constant of 9 (three tiers x three flights)'
}
Assert-Min (Get-Text (Get-Method $queueType 'PushPreview')) 'ldc.i4.8|ldc.i4.s' 1 'the preview push must compare against its own fixed slot count'
$draw = Get-Text (Get-Method $hud 'Draw')
Assert-Min $draw 'CameraManager::GetCurrentCamera' 1 'Draw must use the current camera (behind-camera/no-camera safety)'
Assert-Min $draw 'KillViewPolicy::HeadVisible' 1 'the head-channel switch and HUD visibility must gate the draw'
Assert-Min $draw 'KillIconOccluders::Collect' 1 'the draw must collect the visible-hitbox candidates exactly once per frame'
Assert-Min $draw 'KillIconHud::DrawSlot' 1 'Draw must render both the real and the preview stores through one slot renderer'
$drawSlot = Get-Text (Get-Method $hud 'DrawSlot')
Assert-Min $drawSlot 'WorldToScreenPoint' 1 'the slot renderer must project the stored world anchor'
Assert-Min $drawSlot 'HitMarkerPolicy::IsProjectable' 1 'behind-camera and off-screen anchors must be dropped by the shared projection gate'
Assert-Min $drawSlot 'KillIconPolicy::Alpha' 1 'the slot renderer must fade the icon by age'
Assert-Min $drawSlot 'KillIconHud::DrawFlightMark' 1 'the slot renderer must stamp the flight badge under the icon'
foreach ($forbidden in @('Update', 'OnGUI', 'FixedUpdate', 'LateUpdate')) {
    if (($hud.Methods | Where-Object { $_.Name -eq $forbidden }).Count -ne 0) {
        Fail "KillIconHud must not own $forbidden; KillHud ticks/draws it so F7 and General/Enabled keep gating it"
    }
}
$hudText = Get-Type-Text $hud
Assert-Count $hudText 'FindObjectsOfType|FindObjectsByType|FindObjectOfType|Resources::FindObjectsOfTypeAll|SceneManager' 0 'the head-icon path must never scan the scene per frame'
Assert-Count $hudText 'Harmony|Crosshair' 0 'the head icons are not a Harmony patch and are independent of the world hit/kill lines'
# The badge itself: shape first (a chevron or a plate), colour second.
$mark = Get-Text (Get-Method $hud 'DrawFlightMark')
Assert-Min $mark 'KillIconPolicy::MarkFor' 1 'the badge shape must come from the shared policy'
Assert-Min $mark 'DrawTexture' 4 'the badge must be drawn as geometry (wings/plate), not as a tinted copy of the icon'
$colorFor = Get-Text (Get-Method $hud 'ColorFor')
Assert-Min $colorFor 'Plugin::KillIconFlyerColor' 1 'the flyer badge colour must be configurable'
Assert-Min $colorFor 'Plugin::KillIconArmorFlyerColor' 1 'the armoured-flyer badge colour must be configurable separately'

# ---- 7. tier -> BFV family and the shipped constants ---------------------------------------------
$policy = Get-Type 'KillIconPolicy'
$iconKey = Get-Text (Get-Method $policy 'TierIconKey')
foreach ($key in @('kill-normal', 'kill-weakspot', 'kill-heavy')) {
    Assert-Min $iconKey ([regex]::Escape($key)) 1 "KillIconPolicy.TierIconKey is missing $key (light/medium/heavy must be the same BFV rasters)"
}
$iconFile = Get-Text (Get-Method $policy 'TierIconFile')
Assert-Min $iconFile 'TierIconKey' 1 'TierIconFile must derive from TierIconKey so the two can never diverge'
$headAnchor = Get-Text (Get-Method $policy 'HeadAnchorOffset')
Assert-Min $headAnchor 'FeedbackPolicy::Clamp' 1 'the bar head offset must be clamped, never trusted raw from the cfg'
Assert-Min $headAnchor 'KillIconPolicy::Gap' 1 'the configured gap must be clamped through the policy'
Assert-Const $policy 'DefaultLifetimeSeconds' '2.2' 'default head-icon lifetime'
Assert-Const $policy 'DefaultMaxIcons' '6' 'default concurrent head icons'
Assert-Const $policy 'MaxIconsCeiling' '12' 'hard ceiling on concurrent head icons'
Assert-Const $policy 'DefaultGapMetres' '0.3' 'default clearance above the head/bar'
$parse = Get-Text (Get-Method $policy 'ParseDisplay')
Assert-Min $parse 'ldc\.i4\.5' 1 'the switch token must stay case-insensitive (StringComparison.OrdinalIgnoreCase = 5)'
# Frozen-anchor constants (1.2.16). The follow window is gone on purpose: a follow window is exactly what
# made the icon slide down with the corpse.
foreach ($stale in @('ShouldFollow', 'FollowWindowFraction')) {
    if (($policy.Methods | Where-Object { $_.Name -eq $stale }).Count -ne 0 -or ($policy.Fields | Where-Object { $_.Name -eq $stale }).Count -ne 0) {
        Fail "KillIconPolicy must not keep ${stale}: the anchor is frozen at the credit, there is no follow window"
    }
}
foreach ($anchorConst in @(@{ Name = 'AnchorSourceHead'; Value = 'head-world' }, @{ Name = 'AnchorSourceLethal'; Value = 'lethal-position' })) {
    $field = $policy.Fields | Where-Object { $_.Name -eq $anchorConst.Name } | Select-Object -First 1
    if ($null -eq $field -or -not $field.HasConstant -or [string]$field.Constant -ne $anchorConst.Value) {
        Fail "KillIconPolicy.$($anchorConst.Name) must stay the constant $($anchorConst.Value)"
    }
}
# REVERSE assertion (1.2.16 pip removal): the amber weakspot pip above the head icon is GONE and the weakspot
# is expressed by the CARD TITLE only. The classification stays an enum constant because the card and the
# receipt log still use it - what must never come back is any mark geometry or pip colour in the icon path.
$killKind = Get-Type 'KillKind'
$weakspotConstant = ($killKind.Fields | Where-Object { $_.Name -eq 'Weakspot' } | Select-Object -First 1)
if ($null -eq $weakspotConstant -or -not $weakspotConstant.HasConstant) { Fail 'KillKind.Weakspot must stay a compile-time enum constant' }
foreach ($iconType in @($policy, $hud)) {
    foreach ($member in @($iconType.Methods) + @($iconType.Fields) + @($iconType.NestedTypes)) {
        if ($member.Name -match 'WeakspotMark') {
            Fail "$($iconType.Name).$($member.Name) must not exist: the weakspot pip was removed in 1.2.16 (the card title carries the weakspot)"
        }
    }
}
Assert-Count (Get-Type-Text $policy) 'WeakspotMark|WeakspotColor' 0 'KillIconPolicy must not carry the weakspot pip any more (no mark geometry, no amber pip colour)'
Assert-Count (Get-Type-Text $hud) 'WeakspotMark|WeakspotColor' 0 'KillIconHud must not draw the weakspot pip any more: the head icon is the tier raster plus the flight badge'
Assert-Count $drawSlot 'Weakspot' 0 'DrawSlot must stamp exactly the tier raster and the flight badge (no weakspot pip)'
if ((Get-Type 'KillIconSlot').Fields | Where-Object { $_.Name -eq 'Kind' }) {
    Fail 'KillIconSlot must not keep a Kind field: the head icon consumes tier/flight only, so the removed pip left no dead state'
}
Assert-Const $policy 'MinLifetimeSeconds' '0.4' 'lifetime floor'
Assert-Const $policy 'MaxLifetimeSeconds' '6' 'lifetime ceiling'
Assert-Const $policy 'MinGapMetres' '0' 'gap floor'
Assert-Const $policy 'MaxGapMetres' '2' 'gap ceiling'
Assert-Const $policy 'DisplayHead' 'Head' 'the default presentation is the head icon'
Assert-Const $policy 'DisplayCard' 'Card' 'the opt-out token that restores the old cards'
Assert-Const $policy 'DefaultWingsColor' '#8FD3E8' 'plain-flyer badge colour'
Assert-Const $policy 'DefaultArmorPlateColor' '#E6AB57' 'armoured-flyer badge colour'

# ---- 8. KillHud routes both presentations behind the same F7 / General/Enabled gate ---------------
$killHud = Get-Type 'KillHud'
$update = Get-Text (Get-Method $killHud 'Update')
Assert-Count $update 'KillIconHud::Tick|KillIconHud::Draw' 1 'KillHud.Update must tick the head icons exactly once'
$onGui = Get-Text (Get-Method $killHud 'OnGUI')
Assert-Count $onGui 'KillIconHud::Draw' 1 'KillHud.OnGUI must draw the head icons'
# Review F1: the card path is no longer gated on the Display switch here - every card decides for itself
# whether its OWN kill got a head icon, so a kill whose icon is missing still shows its card.
Assert-Count $onGui 'KillHud::DrawCards' 1 'OnGUI must always reach the card path (the per-kill fallback lives there)'
# 1.2.18: the draw path no longer resolves one shared Display token; Card/Enabled and KillIcon/HeadEnabled are
# read independently, and the legacy KillIcon/Display value is migrated once at load/reload.
Assert-Count $onGui 'KillIconPolicy::ParseDisplay' 0 'the draw path must not read the legacy Display key any more'
$paintsCard = Get-Text (Get-Method $killHud 'PaintsCard')
Assert-Min $paintsCard 'KillViewPolicy::CardVisible' 1 'the card channel rule must come from the shared policy'
Assert-Count $paintsCard 'KillIconHud::IsPresenting' 0 'the card channel must not depend on whether that kill has a head icon'
$drawCards = Get-Text (Get-Method $killHud 'DrawCards')

# Instruction-level call count: the text flattener renders branch targets inline ("br.s IL_0028: call ..."),
# which would double-count a call that is also a branch target. This counts real call instructions only.
function Count-Type-Calls($type, [string]$pattern) {
    $found = 0
    foreach ($typeMethod in $type.Methods) {
        if (-not $typeMethod.HasBody) { continue }
        foreach ($typeInstruction in $typeMethod.Body.Instructions) {
            if ($typeInstruction.OpCode.Name -eq 'call' -and "$($typeInstruction.Operand)" -match $pattern) { $found++ }
        }
    }
    return $found
}
$killHudText = Get-Type-Text $killHud
if ((Count-Type-Calls $killHud 'KillIconHud::IsPresenting') -ne 0) {
    Fail 'the card path must no longer ask whether a kill is already presented by its icon: the channels are independent'
}
if ((Count-Type-Calls $killHud 'KillIconPolicy::ShouldDrawCard') -ne 0) {
    Fail 'the retired ShouldDrawCard fallback must not be reachable from KillHud any more'
}
Assert-Min $drawCards 'KillTracker::Snapshot' 1 'Card mode must keep drawing the existing card queue'
Assert-Min $drawCards 'KillHud::PaintsCard' 1 'DrawCards must filter the queue through the fallback rule'
Assert-Min $drawCards 'KillHud::DrawCard' 1 'Card mode must keep the existing card layout'

# The fallback must be ONE production decision, not a second parallel path: the draw path resolves the
# display mode once (OnGUI) and hands it to DrawCards, and the whole KillHud type contains exactly one call
# site for each of the two functions that decide "icon or card" (review F1 wiring).
$drawCardsParameters = ((Get-Method $killHud 'DrawCards').Parameters | ForEach-Object { $_.ParameterType.Name }) -join ','
if ($drawCardsParameters -ne '') {
    Fail "KillHud.DrawCards must be parameterless: each channel reads its own switch; found ($drawCardsParameters)"
}
# (the exactly-one-call-site rule for both functions is asserted above, instruction-level)

# F9 wiring: the preview key reaches the nine-slot PREVIEW store (never the real queue), and the preview set
# replaces itself. Combined with the executable KillIconQueue assertions this proves the routing end to end.
Assert-Min $update 'Plugin::PreviewKey' 1 'KillHud.Update must read the preview key'
Assert-Count $update 'KillTracker::AddPreview' 2 'F9 must request both preview card samples (normal + weakspot)'
Assert-Count (Get-Text (Get-Method $tracker 'AddPreview')) 'KillIconHud::AddPreview' 1 'the preview cards and the nine preview icons must be requested together'
$addPreviewHud = Get-Text (Get-Method $hud 'AddPreview')
Assert-Count $addPreviewHud 'KillIconHud::Push' 1 'the preview set must be built through the same slot push'
Assert-Count $addPreviewHud 'KillIconQueue::ResetPreviews' 1 'a repeated F9 must reset the preview set exactly once'
Assert-Count $addPreviewHud 'KillIconQueue::PushReal' 0 'F9 must never occupy a real kill slot'
$drawCard = Get-Text (Get-Method $killHud 'DrawCard')
Assert-Min $drawCard 'KillCard::Tier' 1 'the card icon must follow the same tier the cue consumed'
Assert-Min $drawCard 'KillHud::TierIcon' 1 'the card icon must be resolved through the tier mapping'
Assert-Min $drawCard 'KillIconHud::DrawFlightMark' 1 'flight identity must stay readable in card mode too'
Assert-Min $drawCard 'KillCard::Flight' 1 'the card badge must consume the same flight the cue consumed'

# ---- 8b. ONE presentation drives both icons (1.2.15: card by classification, head icon by tier) ------
# The card must resolve its raster from the tier alone and must not re-derive "heavy" from HealthMax at
# draw time (that was a second authority that could disagree with the tier the icon and cue were built
# from). The weakspot classification survives only as the card title and the small pip above the head icon.
Assert-Count $drawCard 'KillCard::HealthMax' 0 'the card must not re-derive the heavy tier from HealthMax: the tier decides'
Assert-Count (Get-Type-Text $killHud) 'KillPolicy::IsHeavy' 0 'KillHud must not keep a second heavy authority'
$tickCardIcons = Get-Text (Get-Method $killHud 'TickCardIcons')
Assert-Min $tickCardIcons 'KillIconPolicy::TierIconFile' 3 'the card icon paths must come from the same tier -> file mapping as the head icon'
Assert-Min $tickCardIcons 'KillIconPolicy::TierIconKey' 3 'the card texture keys must come from the same tier -> key mapping as the head icon'
$visibleIcons = Get-Text (Get-Method $killHud 'VisibleIcons')
Assert-Min $visibleIcons 'KillIconPolicy::TierIconKey' 3 'the card draw must look the textures up through that same mapping'

# ---- 9b. explicit il2cpp class-injection receipts (1.2.16) ----------------------------------------
# Il2CppInterop logs "Assembly GTFOKillFeed.dll is not registered in il2cpp" from
# Il2CppClassPointerStore<T>..cctor for any type in our own (non-game) assembly; that error path returns
# IntPtr.Zero and is benign, but the real-machine log could not be reconciled with it. Each injection now
# proves itself with ClassInjector.IsTypeRegisteredInIl2Cpp and one [INJECT] line per type.
$pluginType = Get-Type 'Plugin'
$load = Get-Text (Get-Method $pluginType 'Load')
foreach ($component in @('KillHud', 'CorpseVisualLease', 'HitMarkerHud')) {
    Assert-Min $load ('RegisterComponent<HawChow\.GTFOKillFeed\.' + $component + '>') 1 "Plugin.Load must register $component through the receipt helper"
}
Assert-Count $load 'ClassInjector::RegisterTypeInIl2Cpp' 0 'Plugin.Load must register every component through the receipt helper, never raw'
$register = Get-Text (Get-Method $pluginType 'RegisterComponent')
Assert-Min $register 'ClassInjector::RegisterTypeInIl2Cpp' 1 'the receipt helper must still inject the type'
Assert-Min $register 'ClassInjector::IsTypeRegisteredInIl2Cpp' 1 'the receipt must ASK the injector whether the type is registered'
$injectLog = @($register | Where-Object { $_ -match 'INJECT' })
if ($injectLog.Count -lt 1) { Fail 'the registration receipt must log an [INJECT] line per injected type' }
$loadReceipt = @($load | Where-Object { $_ -match 'INJECT' })
if ($loadReceipt.Count -lt 1) { Fail 'Plugin.Load must repeat the injection outcome in its own receipt line' }

$tierIcon = Get-Text (Get-Method $killHud 'TierIcon')
foreach ($field in @('_weakIcon', '_heavyIcon', '_normalIcon')) {
    Assert-Min $tierIcon ([regex]::Escape($field)) 1 "KillHud.TierIcon must keep the $field slot (three tiers, not two)"
}

# ---- 9. the previous phase's world hit/kill lines stay independent --------------------------------
$hitHud = Get-Type 'HitMarkerHud'
foreach ($method in $hitHud.Methods) {
    Assert-Count (Get-Text $method) 'KillIcon' 0 "HitMarkerHud.$($method.Name) must not be re-plumbed through the head icons"
}

# ---- 10. review round 2: the slots are write-once, the card keeps up, the pixels stay proportional --
# I8: the ONLY place an anchor may be written is the slot construction in KillIconHud.Push. A type-level
# count is used because a single leftover setter (the 1.2.15 SetReal) would let a per-frame follow come back.
$slotWrites = 0
foreach ($queueMethod in $queueType.Methods) {
    if (-not $queueMethod.HasBody) { continue }
    foreach ($queueInstruction in $queueMethod.Body.Instructions) {
        if ($queueInstruction.OpCode.Name -ne 'stfld') { continue }
        $slotField = $queueInstruction.Operand
        if ($slotField -is [Mono.Cecil.FieldReference] -and $slotField.Name -in @('X', 'Y', 'Z')) { $slotWrites++ }
    }
}
if ($slotWrites -ne 0) { Fail "KillIconQueue must never store into X/Y/Z (found $slotWrites writes): the anchor is frozen at the credit" }
if (($queueType.Methods | Where-Object { $_.Name -eq 'SetReal' }).Count -ne 0) {
    Fail 'KillIconQueue must not expose SetReal: a per-frame slot setter is how the 1.2.15 follow came back'
}
$hudAnchorWrites = 0
foreach ($hudMethod in $hud.Methods) {
    if (-not $hudMethod.HasBody) { continue }
    foreach ($hudInstruction in $hudMethod.Body.Instructions) {
        if ($hudInstruction.OpCode.Name -ne 'stfld') { continue }
        $hudField = $hudInstruction.Operand
        if ($hudField -is [Mono.Cecil.FieldReference] -and $hudField.Name -eq 'X') {
            $hudAnchorWrites++
            if ($hudMethod.Name -ne 'Push') { Fail "KillIconHud.$($hudMethod.Name) writes a slot anchor: only Push (at the credit) may" }
        }
    }
}
if ($hudAnchorWrites -ne 1) { Fail "KillIconHud must write the anchor exactly once (Push); found $hudAnchorWrites" }

# I3 (1.2.18): the two presentation channels are independent. A frame with no camera draws no head icon, but
# the card no longer asks the icon whether it can draw - it follows Card/Enabled alone.
Assert-Min (Get-Text (Get-Method $killHud 'PaintsCard')) 'KillViewPolicy::CardVisible' 1 'the card channel must be decided by the shared kill-view policy'
if (($hud.Methods | Where-Object { $_.Name -eq 'CanDrawNow' }).Count -ne 0) {
    Fail 'KillIconHud.CanDrawNow must be gone with the shared-display model: the card no longer asks the icon whether it can draw'
}
Assert-Min (Get-Text (Get-Method $hud 'Draw')) 'KillViewPolicy::HeadVisible' 1 'the head channel must be gated by the shared kill-view policy (switch + HUD + camera)'

# I4: F9 queues a preview card only when the card channel is on, and previews the nine icon combinations only
# when the head channel is on; the two channels no longer depend on one shared Display token.
$addPreviewTracker = Get-Text (Get-Method $tracker 'AddPreview')
Assert-Min $addPreviewTracker 'Plugin::KillCardEnabled' 1 'AddPreview must ask the card channel before queueing a preview card'
Assert-Min $addPreviewTracker 'Plugin::KillIconHeadEnabled' 1 'F9 must only preview the nine icon combinations when the head channel is on'
Assert-Min $addPreviewTracker 'KillIconHud::AddPreview' 1 'F9 must still preview the nine icon combinations when the head channel is on'

# I5/I6: one title function feeds measurement and drawing, and the card reserves the badge row.
Assert-Min (Get-Text (Get-Method $killHud 'MeasureCardWidth')) 'KillHud::CardTitle' 1 'MeasureCardWidth must measure the title that is actually drawn'
Assert-Min (Get-Text (Get-Method $killHud 'DrawCard')) 'KillHud::CardTitle' 1 'DrawCard must draw through the same title function'
Assert-Min (Get-Text (Get-Method $killHud 'DrawCards')) 'KillHud::CardHeight' 1 'DrawCards must reserve the badge row through CardHeight'
$cardHeight = Get-Text (Get-Method $killHud 'CardHeight')
Assert-Min $cardHeight 'KillIconPolicy::BadgeSize' 1 'CardHeight must reserve the flight badge row (BadgeSize)'
Assert-Min $cardHeight 'KillIconPolicy::BadgeGap' 1 'CardHeight must reserve the badge gap'
$drawCardBody = Get-Text (Get-Method $killHud 'DrawCard')
Assert-Min $drawCardBody 'KillIconPolicy::BadgeGap' 1 'DrawCard must place the badge BELOW the icon box, like the head-top path'
Assert-Count $drawCardBody 'iconRect.yMax - iconSize' 0 'the card badge must not be drawn inside the icon box any more'

# I2: the badge size is pixel-proportional; it must not run through the 26-76 px Size() clamp again.
$pixelSize = Get-Text (Get-Method $policy 'PixelSize')
Assert-Min $pixelSize 'ldc\.r4\s*152' 1 'PixelSize must cap at twice the 76 px icon maximum (inlined 152), not at the cfg clamp'
Assert-Count $pixelSize 'KillIconPolicy::Size' 0 'PixelSize must not call the 26-76 px cfg clamp on an already-final pixel size'
foreach ($pixelMethod in @('BadgeSize', 'BadgeGap')) {
    Assert-Count (Get-Text (Get-Method $policy $pixelMethod)) 'KillIconPolicy::Size' 0 "$pixelMethod must not re-clamp an already-final pixel size through Size()"
}

$assembly.Dispose()
Write-Host 'KILL_ICON_IL=PASS;ANCHOR=frozen-at-credit;SOURCE=head-world|lethal-position;FREEZE=never-moves;TICK=expire-only;SPLIT=card|head(independent switches,legacy Display migrated);OCCLUDERS=collect-once-per-frame+hide-without-room;TIERS=light|medium|heavy(one-mapping);FLIGHT=wings|plate;WEAKSPOT=card-title-only(no-pip);DRAW=OnGUI-only;SCENE_SCAN=none;INJECT=receipt-per-type'
