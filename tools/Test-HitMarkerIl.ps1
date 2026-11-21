[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PluginDll,
    [Parameter(Mandatory = $true)][string]$CecilPath,
    [string]$InteropDir
)
# Compiled-IL guard for the world-anchored BFV hit/kill lines, shipped as 1.2.16.
# (The internal phase label 1.2.17 in older comments was folded into the 1.2.16 release; the CHANGELOG states
#  that mapping, so this header and the shipped version can never disagree.)
# The claims that matter cannot be proven from source text, so every one is read out of the shipped
# assembly and out of the real interop assembly:
#   * the patched methods are the game's own world-position indicator entries, with the exact parameter
#     names (Harmony binds by name) and types;
#   * the old unconditional CrosshairHitIndicator.PlayHit/PlayDeath suppression is gone, so an event we do
#     not replace still reaches the stock indicator;
#   * the replacement is only announced (prefix returns false) when the same event was accepted by the HUD,
#     which itself requires the feature, the per-kind icon, a camera and a projectable impact point;
#   * the local-damage ticket that carries the armour verdict only accepts a real impact point (the
#     bullet-data path uses an estimated pivot and must not seed it);
#   * the kill line is its own resource and its own queue entry, not the head-top kill icon.
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $PluginDll -PathType Leaf)) { throw "Plugin assembly is missing: $PluginDll" }
if (-not (Test-Path -LiteralPath $CecilPath -PathType Leaf)) { throw "Mono.Cecil is required to inspect compiled IL: $CecilPath" }
if ([string]::IsNullOrWhiteSpace($InteropDir)) { throw 'InteropDir is required: the patched signatures can only be proven against the real game assemblies' }
Add-Type -Path (Resolve-Path -LiteralPath $CecilPath).Path

function Fail([string]$message) { throw "HIT_MARKER_IL=FAIL: $message" }

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
function Count-Match($text, [string]$pattern) { return @($text | Where-Object { $_ -match $pattern }).Count }

# ---- 1. the real interop signatures --------------------------------------------------------------
$interopDll = Join-Path $InteropDir 'Modules-ASM.dll'
if (-not (Test-Path -LiteralPath $interopDll -PathType Leaf)) { Fail "interop assembly is missing: $interopDll" }
$interop = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path -LiteralPath $interopDll).Path)
$realHit = $null
$realDeath = $null
try {
    $layer = $interop.MainModule.GetType('CrosshairGuiLayer')
    if ($null -eq $layer) { Fail 'CrosshairGuiLayer is missing from the game interop' }
    $realHit = $layer.Methods | Where-Object {
        $_.Name -eq 'ShowHitIndicator' -and
        (@($_.Parameters | ForEach-Object { $_.ParameterType.FullName }) -join '|') -eq 'UnityEngine.Vector3|System.Boolean|System.Boolean|System.Boolean'
    } | Select-Object -First 1
    $realDeath = $layer.Methods | Where-Object {
        $_.Name -eq 'ShowDeathIndicator' -and
        (@($_.Parameters | ForEach-Object { $_.ParameterType.FullName }) -join '|') -eq 'UnityEngine.Vector3'
    } | Select-Object -First 1
    if ($null -eq $realHit) { Fail 'CrosshairGuiLayer.ShowHitIndicator(Vector3,Boolean,Boolean,Boolean) is missing' }
    if ($null -eq $realDeath) { Fail 'CrosshairGuiLayer.ShowDeathIndicator(Vector3) is missing' }
} finally { $interop.Dispose() }
Write-Host ("HIT_MARKER_INTEROP=PASS;HIT={0};DEATH={1}" -f
    (($realHit.Parameters | ForEach-Object { $_.ParameterType.Name + ' ' + $_.Name }) -join ', '),
    (($realDeath.Parameters | ForEach-Object { $_.ParameterType.Name + ' ' + $_.Name }) -join ', '))

# ---- 2. the compiled prefixes must match those signatures by name and type ----------------------
$hud = Get-Type 'HitMarkerHud'
$policy = Get-Type 'HitMarkerPolicy'
$tracker = Get-Type 'KillTracker'
$prefixSpecs = @(
    @{ Prefix = 'ShowHitIndicatorPrefix'; Target = $realHit; HudCalls = @('HitMarkerPolicy::TryDecide', 'HitMarkerHud::TryShow') },
    @{ Prefix = 'ShowDeathIndicatorPrefix'; Target = $realDeath; HudCalls = @('HitMarkerHud::TryShowKill') }
)
foreach ($spec in $prefixSpecs) {
    $prefix = Get-Method $hud $spec.Prefix
    if ($prefix.ReturnType.FullName -ne 'System.Boolean') { Fail "$($spec.Prefix) must return bool (true = let the vanilla indicator run)" }
    $own = @($prefix.Parameters | Where-Object { $_.Name -notlike '__*' })
    $targetParameters = @($spec.Target.Parameters)
    $ownTypes = ($own | ForEach-Object { $_.ParameterType.FullName }) -join '|'
    $targetTypes = ($targetParameters | ForEach-Object { $_.ParameterType.FullName }) -join '|'
    if ($ownTypes -ne $targetTypes) {
        Fail "$($spec.Prefix) parameter types '$ownTypes' do not match the game method '$targetTypes'"
    }
    foreach ($parameter in $targetParameters) {
        if (-not ($own | Where-Object { $_.Name -eq $parameter.Name })) {
            Fail ("{0} does not declare the game parameter '{1}' - Harmony binds by name and would abort PatchAll" -f $spec.Prefix, $parameter.Name)
        }
    }
    # Branch targets are rendered inline by the flattener, so presence is the honest assertion here.
    $text = Get-Text $prefix
    foreach ($call in $spec.HudCalls) {
        if ((Count-Match $text ([regex]::Escape($call))) -lt 1) {
            Fail "$($spec.Prefix) must call $call for this exact event"
        }
    }
}

# ---- 3. the patch table: both world entries, and no blanket PlayHit/PlayDeath suppression --------
$apply = Get-Text (Get-Method $hud 'ApplyPatches')
if ((Count-Match $apply 'ldstr CrosshairGuiLayer') -ne 1) { Fail 'ApplyPatches must resolve CrosshairGuiLayer' }
if ((Count-Match $apply 'ldstr ShowHitIndicator$') -ne 1) { Fail 'ApplyPatches must patch CrosshairGuiLayer.ShowHitIndicator' }
if ((Count-Match $apply 'ldstr ShowDeathIndicator$') -ne 1) { Fail 'ApplyPatches must patch CrosshairGuiLayer.ShowDeathIndicator' }
if ((Count-Match $apply 'Harmony::Patch') -ne 2) { Fail 'ApplyPatches must patch exactly the two world-position entry points' }
foreach ($method in $hud.Methods) {
    foreach ($forbidden in @('CrosshairHitIndicator::PlayHit', 'CrosshairHitIndicator::PlayDeath', 'CrosshairHitIndicator')) {
        if ((Count-Match (Get-Text $method) ([regex]::Escape($forbidden))) -ne 0) {
            Fail "HitMarkerHud.$($method.Name) still references $forbidden; the old context-free suppression must stay removed"
        }
    }
}

# ---- 4. replacement is announced only when the same event is drawable ---------------------------
$tryShow = Get-Text (Get-Method $hud 'TryShow')
$tryKill = Get-Text (Get-Method $hud 'TryShowKill')
$submit = Get-Text (Get-Method $hud 'Submit')
foreach ($pair in @(@('TryShow', $tryShow), @('TryShowKill', $tryKill))) {
    if ((Count-Match $pair[1] 'Submit') -lt 1) { Fail "$($pair[0]) must submit the marker for the event" }
}
if ((Count-Match $submit 'HitMarkerHud::Resolve') -ne 1) { Fail 'Submit must run the queue merge/precedence step exactly once' }
$resolve = Get-Text (Get-Method $hud 'Resolve')
if ((Count-Match $resolve 'HitMarkerPolicy::Resolve') -ne 1) { Fail 'the queue step must delegate the precedence rule to HitMarkerPolicy.Resolve' }
if ((Count-Match $resolve 'EvictOldest') -lt 1) { Fail 'a full marker queue must evict the oldest line instead of growing' }
if ((Count-Match $submit 'HitMarkerHud::TryProject') -ne 1) {
    Fail 'Submit must reject an impact it could not draw (projection gate before the vanilla is skipped)'
}
$project = Get-Text (Get-Method $hud 'TryProject')
# Branch targets are rendered inline by the flattener, so presence (>= 1) is the honest assertion here.
if ((Count-Match $project 'WorldToScreenPoint') -lt 1 -or (Count-Match $project 'HitMarkerPolicy::IsProjectable') -lt 1) {
    Fail 'TryProject must project the real world point and gate it through HitMarkerPolicy.IsProjectable'
}
if ((Count-Match $project 'CameraManager::GetCurrentCamera') -lt 1) {
    Fail 'TryProject must use the current camera (no guessed screen centre fallback)'
}
if ((Count-Match $submit 'IconReady') -lt 1) { Fail 'Submit must require the per-kind icon before replacing anything' }

# ---- 5. the armour verdict comes from the PRE-CALL impact scope, never from a late ticket --------
# RecordHit runs from the finalizer (End), i.e. AFTER the original damage method - and the game may show
# its hit indicator from inside that method. So the verdict has to be published by the KillTracker prefix
# (BeginLimb / BeginProcessedDamage) and popped by the finalizer; a late ticket can never match.
$beginLimb = Get-Text (Get-Method $tracker 'BeginLimb')
if ((Count-Match $beginLimb 'HitMarkerHud::BeginImpact') -ne 1) {
    Fail 'BeginLimb (a Harmony prefix, so before the original damage method) must publish exactly one impact scope'
}
$processed = Get-Text (Get-Method $tracker 'BeginProcessedDamage')
if ((Count-Match $processed 'HitMarkerHud::BeginImpact') -ne 1) {
    Fail 'BeginProcessedDamage must publish exactly one impact scope'
}
$bullet = Get-Text (Get-Method $tracker 'BeginBullet')
if ((Count-Match $bullet 'HitMarkerHud::BeginImpact') -ne 0) {
    Fail 'BeginBullet only knows an estimated pivot, so it must never publish an impact scope'
}
$end = Get-Text (Get-Method $tracker 'End')
if ((Count-Match $end 'HitMarkerHud::EndImpact') -lt 1) {
    Fail 'End must pop the impact scope on every path (finalizer), including a throwing original method'
}
$record = Get-Text (Get-Method $tracker 'RecordHit')
foreach ($forbidden in @('HitMarkerHud::BeginImpact', 'HitMarkerHud::NoteLocalHit', 'HitMarkerHud::Fire', 'HitMarkerHud::SuppressPrefix')) {
    if ((Count-Match $record ([regex]::Escape($forbidden))) -ne 0) {
        Fail "RecordHit must not touch the marker context ($forbidden); the verdict has to exist before the original method runs"
    }
}
foreach ($gone in @('NoteLocalHit', 'ConsumeArmorTicket')) {
    if (($hud.Methods | Where-Object { $_.Name -eq $gone }).Count -ne 0) {
        Fail "HitMarkerHud.$gone is the late ticket mechanism that cannot match an in-method indicator event; it must stay removed"
    }
}
# The publishing entry points really are Harmony prefixes on the damage methods. The prefix bodies are
# expression-bodied lambdas, so the call lives in the compiler-generated closure of the same patch type;
# the attribute check plus the type-wide call count is what proves "this is a prefix of the damage method".
function Get-Wrapper-Text($type) {
    $all = New-Object System.Collections.Generic.List[string]
    foreach ($method in $type.Methods) { foreach ($line in (Get-Text $method)) { $all.Add($line) } }
    foreach ($nested in $type.NestedTypes) { foreach ($method in $nested.Methods) { foreach ($line in (Get-Text $method)) { $all.Add($line) } } }
    return $all
}
foreach ($pair in @(@('LocalBulletPatch', 'BeginLimb'), @('LocalMeleePatch', 'BeginLimb'), @('EnemyProcessedDamagePatch', 'BeginProcessedDamage'))) {
    $patchType = Get-Type $pair[0]
    $prefixMethod = Get-Method $patchType 'Prefix'
    if (-not ($prefixMethod.CustomAttributes | Where-Object { $_.AttributeType.Name -eq 'HarmonyPrefix' })) {
        Fail "$($pair[0]).Prefix must stay a [HarmonyPrefix] so it runs before the original damage method"
    }
    $text = Get-Wrapper-Text $patchType
    if ((Count-Match $text ('KillTracker::' + $pair[1])) -ne 1) {
        Fail "$($pair[0]).Prefix must call KillTracker.$($pair[1]) before the original method runs"
    }
}
# The hit prefix reads the armour verdict once and the event IDENTITY from the same scope stack; the
# identity (a never-reused generation number of one damage call) is the only thing that may merge two
# events into one line (review F3).
$hitPrefix = Get-Text (Get-Method $hud 'ShowHitIndicatorPrefix')
if ((Count-Match $hitPrefix 'ImpactScopeStack::TryClaim') -lt 1) {
    Fail 'the hit prefix must claim the armour verdict from the active impact scope'
}
if ((Count-Match $hitPrefix 'ImpactScopeStack::TryIdentify') -lt 1) {
    Fail 'the hit prefix must read the event identity from the same impact scope'
}
if ((Count-Match $hitPrefix 'HitMarkerPolicy::TryDecide') -ne 1) {
    Fail 'the hit prefix must route the event through HitMarkerPolicy.TryDecide (the noDamage/armour rule)'
}
$scopeStack = Get-Type 'ImpactScopeStack'
foreach ($gone in @('Consume', 'TryTake')) {
    if (($scopeStack.Methods | Where-Object { $_.Name -eq $gone }).Count -ne 0) {
        Fail "ImpactScopeStack.$gone must stay removed: the verdict is claimed once and the identity stays readable for the paired death event"
    }
}
foreach ($needed in @('TryClaim', 'TryIdentify', 'Push', 'Pop', 'Reset')) {
    if (($scopeStack.Methods | Where-Object { $_.Name -eq $needed }).Count -eq 0) {
        Fail "ImpactScopeStack.$needed is missing from the compiled plugin"
    }
}
$tryShowMethod = Get-Method $hud 'TryShow'
$tryShowTypes = ($tryShowMethod.Parameters | ForEach-Object { $_.ParameterType.Name }) -join ','
if ($tryShowTypes -ne 'Vector3,HitMarkerKind,Int32') {
    Fail "HitMarkerHud.TryShow must carry the event identity (Vector3,HitMarkerKind,Int32); found $tryShowTypes"
}
$tryShowKillText = Get-Text (Get-Method $hud 'TryShowKill')
if ((Count-Match $tryShowKillText 'ImpactScopeStack::TryIdentify') -lt 1) {
    Fail 'the death indicator must resolve the same impact identity as its hit indicator (one lethal blow = one line)'
}
$resolveText = Get-Text (Get-Method $hud 'Resolve')
if ((Count-Match $resolveText 'Identity') -lt 1) {
    Fail 'the queue step must look the marker up by event identity, never by distance'
}
$beginImpact = Get-Text (Get-Method $hud 'BeginImpact')
if ((Count-Match $beginImpact 'ImpactScopeStack::Push') -ne 1) { Fail 'BeginImpact must push exactly one scope' }
$endImpact = Get-Text (Get-Method $hud 'EndImpact')
if ((Count-Match $endImpact 'ImpactScopeStack::Pop') -ne 1) { Fail 'EndImpact must pop exactly the scope it was given' }
$resetText = Get-Text (Get-Method $hud 'Reset')
if ((Count-Match $resetText 'ImpactScopeStack::Reset') -lt 1) { Fail 'Reset must clear every impact scope on a level change' }
# The rule's inputs are pinned by NAME, so the noDamage/armour decision cannot be silently re-plumbed.
$decide = Get-Method $policy 'TryDecide'
$decideNames = ($decide.Parameters | ForEach-Object { $_.Name }) -join ','
if ($decideNames -ne 'enabled,friendly,noDamage,scopeMatched,scopeKind,weakspot,kind') {
    Fail "HitMarkerPolicy.TryDecide must keep its rule inputs (enabled,friendly,noDamage,scopeMatched,scopeKind,weakspot,kind); found $decideNames"
}

# ---- 5b. the prefixes are total: any failure keeps the vanilla indicator (review F2) --------------
# A Harmony prefix that throws does not "fail softly": the original method throws with it, so a broken
# replacement would eat the game's own hit/kill feedback. Each prefix must therefore CATCH System.Exception
# and RETURN TRUE (= run the original). The handler body is asserted, not just its existence.
function Get-Handler-Instructions($method, $handler) {
    $instructions = New-Object System.Collections.Generic.List[object]
    $endOffset = if ($null -eq $handler.HandlerEnd) { [int]::MaxValue } else { $handler.HandlerEnd.Offset }
    foreach ($instruction in $method.Body.Instructions) {
        if ($instruction.Offset -ge $handler.HandlerStart.Offset -and $instruction.Offset -lt $endOffset) {
            $instructions.Add($instruction)
        }
    }
    return $instructions
}
foreach ($prefixName in @('ShowHitIndicatorPrefix', 'ShowDeathIndicatorPrefix')) {
    $prefixMethod = Get-Method $hud $prefixName
    if (-not $prefixMethod.HasBody) { Fail "$prefixName has no body" }
    $handlers = @($prefixMethod.Body.ExceptionHandlers)
    if ($handlers.Count -ne 1) { Fail "$prefixName must guard its whole replacement with exactly one exception handler (found $($handlers.Count))" }
    if ($handlers[0].HandlerType.ToString() -ne 'Catch') { Fail "$prefixName must CATCH, so a failure cannot escape into the game method" }
    if ($null -eq $handlers[0].CatchType -or $handlers[0].CatchType.FullName -ne 'System.Exception') {
        Fail "$prefixName must catch System.Exception (found $($handlers[0].CatchType))"
    }
    # Release-mode C# returns through the shared epilogue: "ldc.i4.1; stloc <result>; leave <epilogue>".
    $handlerOps = @((Get-Handler-Instructions $prefixMethod $handlers[0]) | ForEach-Object { $_.OpCode.Name })
    $trueAt = [Array]::IndexOf($handlerOps, 'ldc.i4.1')
    if ($trueAt -lt 0) { Fail "$prefixName's catch handler must return true (run the vanilla indicator for that event)" }
    $storesTrue = ($trueAt + 2) -lt $handlerOps.Count
    if ($storesTrue) { $storesTrue = ($handlerOps[$trueAt + 1] -match '^stloc') }
    if ($storesTrue) { $storesTrue = ($handlerOps[$trueAt + 2] -match '^leave') }
    if (-not $storesTrue) {
        Fail "$prefixName's catch handler must return true through the shared return (ldc.i4.1 / stloc / leave)"
    }
    $allOps = @($prefixMethod.Body.Instructions | ForEach-Object { $_.OpCode.Name })
    if ($allOps.Count -lt 2 -or $allOps[$allOps.Count - 1] -ne 'ret') { Fail "$prefixName must end in ret" }
}

# The camera lookup itself must already be inside a guard: an interop throw there would abort the game's own
# indicator method before we ever reach the prefix catch (review F2).
$projectBody = (Get-Method $hud 'TryProject').Body
$cameraCall = $projectBody.Instructions | Where-Object { $_.OpCode.Name -eq 'call' -and "$($_.Operand)" -match 'GetCurrentCamera' } | Select-Object -First 1
if ($null -eq $cameraCall) { Fail 'TryProject must resolve the current camera' }
$guarded = @($projectBody.ExceptionHandlers | Where-Object {
    $cameraCall.Offset -ge $_.TryStart.Offset -and ($null -eq $_.TryEnd -or $cameraCall.Offset -lt $_.TryEnd.Offset)
})
if ($guarded.Count -eq 0) {
    Fail 'TryProject must look the camera up INSIDE its exception guard: an interop throw there would abort the game indicator (review F2)'
}
# ---- 6. resources: the kill line is its own key, and the draw path is OnGUI-only ----------------
$policy = Get-Type 'HitMarkerPolicy'
$iconKey = Get-Text (Get-Method $policy 'IconKey')
foreach ($key in @('hitmark-normal', 'hitmark-weakspot', 'hitmark-armor', 'hitmark-kill')) {
    if ((Count-Match $iconKey ([regex]::Escape($key))) -lt 1) { Fail "HitMarkerPolicy.IconKey is missing $key" }
}
# Enum names never appear in IL, so the kill-line constants are asserted directly.
function Assert-Const([string]$name, [string]$expectedKind) {
    $field = $policy.Fields | Where-Object { $_.Name -eq $name } | Select-Object -First 1
    if ($null -eq $field -or -not $field.HasConstant) { Fail "HitMarkerPolicy.$name must stay a compile-time constant" }
    if ($expectedKind -eq 'float' -and [Math]::Abs([single]$field.Constant - [single]1.35) -gt 0.0001) {
        Fail "HitMarkerPolicy.$name must stay 1.35 (a kill line is visibly larger than a hit line)"
    }
    if ($expectedKind -eq 'string' -and [string]$field.Constant -ne '#B92828') {
        Fail "HitMarkerPolicy.$name must stay #B92828 (BFV heavy red, never a hit colour)"
    }
    if ($expectedKind -eq 'duration' -and [single]$field.Constant -le 0.18) {
        Fail "HitMarkerPolicy.$name must stay longer than the hit-line lifetime"
    }
}
Assert-Const 'KillSizeScale' 'float'
Assert-Const 'DefaultKillColor' 'string'
Assert-Const 'DefaultKillDurationSeconds' 'duration'
if ((Count-Match (Get-Text (Get-Method $policy 'Lifetime')) 'DefaultKillDurationSeconds') -lt 0) {
    Fail 'HitMarkerPolicy.Lifetime must expose the kill lifetime'
}
$tick = Get-Text (Get-Method $hud 'TickIcons')
if ((Count-Match $tick 'HitMarkerPolicy::IconKey') -lt 4 -or (Count-Match $tick 'HitMarkerPolicy::IconFile') -lt 4) {
    Fail 'the icon tick must register all four marker slots (normal/weakspot/armor/kill) through the policy keys'
}
$onGui = Get-Text (Get-Method $hud 'OnGUI')
if ((Count-Match $onGui 'GUI::DrawTexture') -ne 1) { Fail 'OnGUI must be the only place the marker raster is drawn' }
if ((Count-Match $onGui 'WorldToScreenPoint') -ne 1) { Fail 'the draw must project the stored world position' }
$update = Get-Text (Get-Method $hud 'Update')
if ((Count-Match $update 'DrawTexture') -ne 0) { Fail 'Update must not draw; drawing belongs to OnGUI' }
if ((Count-Match $update 'FindObjectsOfType|FindObjectsByType|FindObjectOfType|Resources::FindObjectsOfTypeAll|SceneManager') -ne 0) {
    Fail 'the marker path must not scan the scene'
}

$assembly.Dispose()
Write-Host 'HIT_MARKER_IL=PASS;PATCHES=ShowHitIndicator+ShowDeathIndicator;OLD_PLAYHIT_SUPPRESSION=removed;ANCHOR=world-impact;ARMOUR_SCOPE=pre-call+consumed-once;NODAMAGE_ARMOUR=scope-gated;KILL_RESOURCE=hitmark-kill.png'
