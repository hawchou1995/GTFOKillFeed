[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PluginDll,
    [Parameter(Mandatory = $true)][string]$CecilPath
)
# Compiled-IL guard for the 1.2.7 C-Foam regression and the [BAR] diagnostic budgets.
#   M1: EnemyFocus.Update calls the shared native-class probe; EnemyBar.Tick feeds EnemyFocus.GlueGunWielded
#       into the _glueMode state and EnemyBar.DrawLabel passes that state into BarPolicy.SelectEnemyTag.
#   M3: EnemyBar.LogFocus and EnemyBar.Warn spend two different DiagnosticBudget fields with the 16 / 5 limits.
# All checks read emitted IL, not source text or comments.
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $PluginDll -PathType Leaf)) { throw "Plugin assembly is missing: $PluginDll" }
if (-not (Test-Path -LiteralPath $CecilPath -PathType Leaf)) { throw "Mono.Cecil is required to inspect compiled IL: $CecilPath" }
Add-Type -Path (Resolve-Path -LiteralPath $CecilPath).Path

# Bounded IL excerpt so a failure stays readable in a console log.
function Show($instructions) {
    $all = @($instructions | ForEach-Object { $_.ToString() })
    $visible = @($all | Select-Object -First 24)
    $text = $visible -join ' | '
    if ($all.Count -gt $visible.Count) { $text += " | ... ($($all.Count) instructions)" }
    return $text
}

function Get-Method($type, [string]$name) {
    $method = $type.Methods | Where-Object { $_.Name -eq $name } | Select-Object -First 1
    if ($null -eq $method -or -not $method.HasBody) { throw "Compiled plugin is missing the body of $($type.FullName)::$name." }
    return $method
}

function Get-Instructions($method) { return @($method.Body.Instructions) }

function Test-FieldOperand($instruction, [string]$fullName) {
    return $instruction.Operand -is [Mono.Cecil.FieldReference] -and $instruction.Operand.FullName -eq $fullName
}

function Test-MethodOperand($instruction, [string]$declaringType, [string]$name) {
    return $instruction.Operand -is [Mono.Cecil.MethodReference] -and
        $instruction.Operand.Name -eq $name -and
        $instruction.Operand.DeclaringType.FullName -eq $declaringType
}

function Test-IntConstant($instruction, [int]$value) {
    $opcode = $instruction.OpCode.Name
    if ($opcode -eq 'ldc.i4.m1') { return $value -eq -1 }
    if ($opcode -match '^ldc\.i4\.[0-8]$') { return $value -eq [int]$opcode.Substring(7) }
    if ($opcode -eq 'ldc.i4.s') { return $instruction.Operand -is [sbyte] -and [int]$instruction.Operand -eq $value }
    if ($opcode -eq 'ldc.i4') { return $instruction.Operand -is [int] -and [int]$instruction.Operand -eq $value }
    return $false
}

function Find-Indices($instructions, [scriptblock]$match) {
    $found = @()
    for ($i = 0; $i -lt $instructions.Count; $i++) { if (& $match $instructions[$i]) { $found += $i } }
    return $found
}

$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path -LiteralPath $PluginDll).Path)
try {
    $module = $assembly.MainModule

    # ---------------------------------------------------------------- glue-gun recognition (1.2.7 regression)
    $probe = $module.GetType('HawChow.GTFOKillFeed.GlueGunWield')
    if ($null -eq $probe) { throw 'Compiled plugin is missing HawChow.GTFOKillFeed.GlueGunWield.' }
    $isHeld = Get-Method $probe 'IsHeld'
    $isHeldIl = Get-Instructions $isHeld

    $nativeCast = Find-Indices $isHeldIl {
        param($ins)
        $ins.Operand -is [Mono.Cecil.GenericInstanceMethod] -and
            $ins.Operand.ElementMethod.Name -eq 'TryCast' -and
            $ins.Operand.GenericArguments.Count -eq 1 -and
            $ins.Operand.GenericArguments[0].FullName -eq 'GlueGun' -and
            $ins.Operand.DeclaringType.FullName -eq 'Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase'
    }
    if ($nativeCast.Count -lt 1) {
        throw "GlueGunWield.IsHeld must call Il2CppObjectBase.TryCast<GlueGun>() so the native class decides glue mode. IL: $(Show $isHeldIl)"
    }
    if ((Find-Indices $isHeldIl { param($ins) $ins.OpCode.Name -eq 'isinst' -and $ins.Operand -is [Mono.Cecil.TypeReference] -and $ins.Operand.FullName -eq 'GlueGun' }).Count -gt 0) {
        throw 'GlueGunWield.IsHeld must not fall back to the managed `is GlueGun` test.'
    }

    $focus = $module.GetType('HawChow.GTFOKillFeed.EnemyFocus')
    if ($null -eq $focus) { throw 'Compiled plugin is missing HawChow.GTFOKillFeed.EnemyFocus.' }
    $update = Get-Method $focus 'Update'
    $updateIl = Get-Instructions $update

    if ((Find-Indices $updateIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.GlueGunWield' 'IsHeld' }).Count -lt 1) {
        throw "EnemyFocus.Update must call GlueGunWield.IsHeld so the native-class probe is the only glue-mode source. IL: $(Show $updateIl)"
    }
    if ((Find-Indices $updateIl { param($ins) $ins.OpCode.Name -eq 'isinst' -and $ins.Operand -is [Mono.Cecil.TypeReference] -and $ins.Operand.FullName -eq 'GlueGun' }).Count -gt 0) {
        throw 'EnemyFocus.Update must not keep the legacy managed `is GlueGun` test.'
    }
    if ((Find-Indices $updateIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.GlueGunWield' 'IsHeld' }).Count -ne 1) {
        throw 'EnemyFocus.Update must call GlueGunWield.IsHeld exactly once.'
    }

    # ---------------------------------------------------------------- M1: glue mode reaches the drawn tag
    $bar = $module.GetType('HawChow.GTFOKillFeed.EnemyBar')
    if ($null -eq $bar) { throw 'Compiled plugin is missing HawChow.GTFOKillFeed.EnemyBar.' }
    $modeField = $bar.Fields | Where-Object { $_.Name -eq '_glueMode' } | Select-Object -First 1
    if ($null -eq $modeField) { throw 'EnemyBar must keep the _glueMode state field.' }
    $modeFieldName = $modeField.FullName

    $tick = Get-Method $bar 'Tick'
    $tickIl = Get-Instructions $tick
    $reset = Get-Method $bar 'Reset'
    $resetIl = Get-Instructions $reset
    $draw = Get-Method $bar 'DrawLabel'
    $drawIl = Get-Instructions $draw

    # The mode field may only be written by Tick (from the probe) and Reset (to false).
    $modeStores = @()
    foreach ($method in $bar.Methods) {
        if (-not $method.HasBody) { continue }
        $methodIl = Get-Instructions $method
        for ($i = 0; $i -lt $methodIl.Count; $i++) {
            if ($methodIl[$i].OpCode.Name -eq 'stsfld' -and (Test-FieldOperand $methodIl[$i] $modeFieldName)) { $modeStores += , @($method.Name, $i) }
        }
    }
    $tickStores = @($modeStores | Where-Object { $_[0] -eq 'Tick' })
    $resetStores = @($modeStores | Where-Object { $_[0] -eq 'Reset' })
    if ($modeStores.Count -ne 2 -or $tickStores.Count -ne 1 -or $resetStores.Count -ne 1) {
        throw "EnemyBar._glueMode must be written once by Tick and once by Reset; found $($modeStores.Count) stores."
    }
    $resetIndex = [int]$resetStores[0][1]
    if ($resetIndex -lt 1 -or -not (Test-IntConstant $resetIl[$resetIndex - 1] 0)) {
        throw 'EnemyBar.Reset must clear _glueMode with a literal false.'
    }

    $getterCalls = Find-Indices $tickIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.EnemyFocus' 'get_GlueGunWielded' }
    if ($getterCalls.Count -ne 1) {
        throw "EnemyBar.Tick must read EnemyFocus.GlueGunWielded exactly once; found $($getterCalls.Count). IL: $(Show $tickIl)"
    }
    $getterIndex = $getterCalls[0]
    $tickStoreIndex = [int]$tickStores[0][1]
    if ($getterIndex + 1 -ge $tickIl.Count -or $tickIl[$getterIndex + 1].OpCode.Name -notmatch '^stloc') {
        throw "EnemyBar.Tick must store EnemyFocus.GlueGunWielded into a local. IL: $(Show $tickIl)"
    }
    $modeLocal = $tickIl[$getterIndex + 1].Operand
    if ($tickStoreIndex -lt 2 -or $tickIl[$tickStoreIndex - 1].OpCode.Name -notmatch '^ldloc' -or -not [object]::ReferenceEquals($tickIl[$tickStoreIndex - 1].Operand, $modeLocal)) {
        throw "EnemyBar.Tick must write _glueMode from the value read out of EnemyFocus.GlueGunWielded. IL: $(Show $tickIl)"
    }
    if ($tickStoreIndex -le $getterIndex) {
        throw 'EnemyBar.Tick must read EnemyFocus.GlueGunWielded before writing _glueMode.'
    }

    # DrawLabel must hand that same state to the tag selector as the first argument.
    $tagCalls = Find-Indices $drawIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.BarPolicy' 'SelectEnemyTag' }
    if ($tagCalls.Count -ne 1) { throw "EnemyBar.DrawLabel must call BarPolicy.SelectEnemyTag exactly once; found $($tagCalls.Count)." }
    $tagIndex = $tagCalls[0]
    if ($tagIndex -lt 4 -or -not (Test-FieldOperand $drawIl[$tagIndex - 4] $modeFieldName)) {
        throw "EnemyBar.DrawLabel must pass its _glueMode state as SelectEnemyTag's first argument. IL: $(Show $drawIl)"
    }
    if ((Find-Indices ($drawIl[($tagIndex - 4)..$tagIndex]) { param($ins) $ins.OpCode.Name -match '^ldc\.i4(\.[0-9]|\.s|\.m1)?$' }).Count -gt 0) {
        throw 'EnemyBar.DrawLabel must not replace the _glueMode argument with a constant.'
    }


    # ---------------------------------------------------------------- M2: glue-mode tag colour
    $plugin = $module.GetType('HawChow.GTFOKillFeed.Plugin')
    if ($null -eq $plugin) { throw 'Compiled plugin is missing HawChow.GTFOKillFeed.Plugin.' }
    $weakspotConfigField = $plugin.Fields | Where-Object { $_.Name -eq 'EnemyBarWeakspotColor' } | Select-Object -First 1
    $nameConfigField = $plugin.Fields | Where-Object { $_.Name -eq 'EnemyBarNameColor' } | Select-Object -First 1
    if ($null -eq $weakspotConfigField -or $null -eq $nameConfigField) { throw 'The configurable weakspot and name colours must stay bound on Plugin.' }
    $weakspotConfigName = $weakspotConfigField.FullName
    $nameConfigName = $nameConfigField.FullName

    $foamField = $bar.Fields | Where-Object { $_.Name -eq 'DefaultCfoamTag' } | Select-Object -First 1
    if ($null -eq $foamField) { throw 'EnemyBar must keep the fixed DefaultCfoamTag colour field.' }
    if ($foamField.FieldType.FullName -ne 'UnityEngine.Color' -or -not $foamField.IsInitOnly) {
        throw "EnemyBar.DefaultCfoamTag must stay a readonly UnityEngine.Color; got $($foamField.FieldType.FullName)."
    }
    $foamFieldName = $foamField.FullName

    # The colour literal itself: #F2A65A at alpha 1, baked into the static constructor.
    $typeInitMethod = $bar.Methods | Where-Object { $_.Name -eq '.cctor' } | Select-Object -First 1
    if ($null -eq $typeInitMethod -or -not $typeInitMethod.HasBody) { throw 'EnemyBar must keep a static constructor that builds the C-Foam amber.' }
    $typeInitIl = Get-Instructions $typeInitMethod
    $foamStores = Find-Indices $typeInitIl { param($ins) $ins.OpCode.Name -eq 'stsfld' -and (Test-FieldOperand $ins $foamFieldName) }
    if ($foamStores.Count -ne 1) { throw "EnemyBar's static constructor must assign DefaultCfoamTag exactly once; found $($foamStores.Count)." }
    $foamStore = $foamStores[0]
    if ($foamStore -lt 5) { throw "EnemyBar.DefaultCfoamTag must be built from four channel literals. IL: $(Show $typeInitIl)" }
    $foamCtor = $typeInitIl[$foamStore - 1]
    if (-not ($foamCtor.Operand -is [Mono.Cecil.MethodReference] -and $foamCtor.Operand.Name -eq '.ctor' -and $foamCtor.Operand.DeclaringType.FullName -eq 'UnityEngine.Color')) {
        throw "EnemyBar.DefaultCfoamTag must construct a UnityEngine.Color. IL: $(Show $typeInitIl)"
    }
    $expectedChannels = @((242.0 / 255.0), (166.0 / 255.0), (90.0 / 255.0), 1.0)
    for ($channel = 0; $channel -lt $expectedChannels.Count; $channel++) {
        $load = $typeInitIl[$foamStore - 5 + $channel]
        if ($load.OpCode.Name -ne 'ldc.r4' -or [math]::Abs([double]$load.Operand - [double]$expectedChannels[$channel]) -gt 0.000001) {
            throw "EnemyBar.DefaultCfoamTag must stay #F2A65A at alpha 1 (242/166/90/1); channel $channel is '$($load.OpCode.Name) $($load.Operand)'. IL: $(Show $typeInitIl)"
        }
    }

    # The branch: glue mode picks the amber, every other weapon keeps the configurable weakspot colour.
    $foamReads = Find-Indices $drawIl { param($ins) Test-FieldOperand $ins $foamFieldName }
    if ($foamReads.Count -ne 1) { throw "EnemyBar.DrawLabel must read DefaultCfoamTag exactly once; found $($foamReads.Count)." }
    $foamRead = $foamReads[0]
    $glueRead = -1
    for ($i = 0; $i -lt $foamRead; $i++) {
        if ((Test-FieldOperand $drawIl[$i] $modeFieldName) -and $drawIl[$i + 1].OpCode.Name -match '^br(true|false)(\.s)?$') { $glueRead = $i }
    }
    if ($glueRead -lt 0) { throw "EnemyBar.DrawLabel must branch on its _glueMode state when it picks the tag colour. IL: $(Show $drawIl)" }
    $colourBranch = $drawIl[$glueRead + 1]
    $branchTarget = -1
    for ($i = 0; $i -lt $drawIl.Count; $i++) { if ([object]::ReferenceEquals($drawIl[$i], $colourBranch.Operand)) { $branchTarget = $i; break } }
    if ($branchTarget -lt 0) { throw 'The glue-mode colour branch target could not be located in EnemyBar.DrawLabel.' }
    $fallthrough = $glueRead + 2
    if ($colourBranch.OpCode.Name -match '^brtrue') { $glueArm = $branchTarget; $otherArm = $fallthrough }
    else { $glueArm = $fallthrough; $otherArm = $branchTarget }
    if ($glueArm -ne $foamRead) {
        throw "EnemyBar.DrawLabel must load DefaultCfoamTag on the glue-mode arm of its _glueMode colour branch. IL: $(Show $drawIl)"
    }

    $otherArmEnd = $otherArm
    while ($otherArmEnd -lt $drawIl.Count -and $drawIl[$otherArmEnd].OpCode.Name -notmatch '^br(\.s)?$') { $otherArmEnd++ }
    $otherArmSlice = @($drawIl[$otherArm..([int][math]::Min($otherArmEnd, $drawIl.Count - 1))])
    if ((Find-Indices $otherArmSlice { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.EnemyBar' 'ConfigColor' }).Count -ne 1) {
        throw 'The non-glue arm must still resolve the tag colour through EnemyBar.ConfigColor.'
    }
    if ((Find-Indices $otherArmSlice { param($ins) Test-FieldOperand $ins $weakspotConfigName }).Count -ne 1) {
        throw 'The non-glue arm must still read Plugin.EnemyBarWeakspotColor so ordinary weapons keep their configurable weakspot colour.'
    }

    $joinStart = [math]::Max($foamRead, $otherArmEnd)
    $joinStore = -1
    for ($i = $joinStart; $i -lt [int][math]::Min($drawIl.Count, $joinStart + 3); $i++) {
        if ($drawIl[$i].OpCode.Name -match '^stloc') { $joinStore = $i; break }
    }
    if ($joinStore -lt 0) { throw "The glue-mode colour branch must store its result in the tag-colour local. IL: $(Show $drawIl)" }
    $tagColorLocal = $drawIl[$joinStore].Operand

    $nameStyleName = ($bar.Fields | Where-Object { $_.Name -eq '_nameStyle' } | Select-Object -First 1).FullName
    $weakspotStyleName = ($bar.Fields | Where-Object { $_.Name -eq '_weakspotStyle' } | Select-Object -First 1).FullName
    if ([string]::IsNullOrEmpty($nameStyleName) -or [string]::IsNullOrEmpty($weakspotStyleName)) { throw 'EnemyBar must keep its name and tag GUI styles.' }

    function Get-LabelColorLocal($instructions, [string]$styleFieldName, [string]$labelName) {
        $styleLoads = Find-Indices $instructions { param($ins) Test-FieldOperand $ins $styleFieldName }
        if ($styleLoads.Count -lt 1) { throw "EnemyBar.DrawLabel must load $styleFieldName before drawing the $labelName label." }
        $styleAt = $styleLoads[$styleLoads.Count - 1]
        $callAt = -1
        for ($i = $styleAt + 1; $i -lt $instructions.Count; $i++) {
            if (Test-MethodOperand $instructions[$i] 'HawChow.GTFOKillFeed.EnemyBar' 'DrawLabelText') { $callAt = $i; break }
        }
        if ($callAt -lt 0) { throw "EnemyBar.DrawLabel must draw the $labelName label through DrawLabelText." }
        for ($i = $styleAt + 1; $i -lt $callAt; $i++) {
            if ($instructions[$i].OpCode.Name -match '^ldloc') { return $instructions[$i] }
        }
        throw "The $labelName label colour must come from a local resolved before the draw call."
    }

    $nameColorLoad = Get-LabelColorLocal $drawIl $nameStyleName 'monster-name'
    $tagColorLoad = Get-LabelColorLocal $drawIl $weakspotStyleName 'tag'
    if (-not [object]::ReferenceEquals($tagColorLoad.Operand, $tagColorLocal)) {
        throw "The tag label must be drawn with the colour local produced by the _glueMode branch. IL: $(Show $drawIl)"
    }
    if ([object]::ReferenceEquals($nameColorLoad.Operand, $tagColorLocal)) {
        throw 'The monster name must keep its own configured colour instead of inheriting the C-Foam amber.'
    }
    $nameConfigResolved = $false
    foreach ($callAt in (Find-Indices $drawIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.EnemyBar' 'ConfigColor' })) {
        $window = @($drawIl[[math]::Max(0, $callAt - 5)..$callAt])
        if ((Find-Indices $window { param($ins) Test-FieldOperand $ins $nameConfigName }).Count -eq 1 -and
            $callAt + 1 -lt $drawIl.Count -and $drawIl[$callAt + 1].OpCode.Name -match '^stloc' -and
            [object]::ReferenceEquals($drawIl[$callAt + 1].Operand, $nameColorLoad.Operand)) { $nameConfigResolved = $true }
    }
    if (-not $nameConfigResolved) { throw 'The monster name must keep resolving Plugin.EnemyBarNameColor through EnemyBar.ConfigColor.' }

    # The fade chain is untouched: outline and glyph still ride the shared opacity multiplier.
    $drawTextMethod = Get-Method $bar 'DrawLabelText'
    $drawTextIl = Get-Instructions $drawTextMethod
    $alphaCalls = Find-Indices $drawTextIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.EnemyBar' 'WithAlpha' }
    if ($alphaCalls.Count -ne 2) { throw "EnemyBar.DrawLabelText must fade the outline and the glyph through WithAlpha exactly twice; found $($alphaCalls.Count)." }
    $fadeConstants = @($drawTextIl | Where-Object { $_.OpCode.Name -eq 'ldc.r4' } | ForEach-Object { [math]::Round([double]$_.Operand, 3) })
    if (-not ($fadeConstants -contains 0.76) -or -not ($fadeConstants -contains 0.96)) {
        throw 'The label fade must keep the existing 0.76 outline / 0.96 glyph opacity chain.'
    }
    # ---------------------------------------------------------------- M3: independent diagnostic budgets
    $budgetName = 'HawChow.GTFOKillFeed.DiagnosticBudget'
    $budgetFields = @($bar.Fields | Where-Object { $_.FieldType.FullName -eq $budgetName })
    # 1.2.22 added the bounded "[BAR] stagger=normal;shown=no" evidence line. 1.2.23 adds its OWN budget for the
    # anatomical-part refusal, so an unknown/mod part can never spend (or silence) the normal-hitreact evidence.
    $expectedBudgets = @('WarningBudget', 'FocusChangeBudget', 'FocusAcquiredBudget', 'LogStaggerBudget', 'LogStaggerPartBudget')
    foreach ($name in $expectedBudgets) {
        if (@($budgetFields | Where-Object { $_.Name -eq $name }).Count -ne 1) { throw "EnemyBar must keep the $name budget field." }
    }
    if ($budgetFields.Count -ne $expectedBudgets.Count) { throw "EnemyBar must keep exactly five diagnostic budgets; found $($budgetFields.Count)." }
    if (@($bar.Fields | Where-Object { $_.Name -in @('_warnings', '_focusLogs') }).Count -gt 0) {
        throw 'EnemyBar must not reintroduce a shared warning counter.'
    }

    $logFocus = Get-Method $bar 'LogFocus'
    $logFocusIl = Get-Instructions $logFocus
    $warn = Get-Method $bar 'Warn'
    $warnIl = Get-Instructions $warn

    $warningFieldName = ($bar.Fields | Where-Object { $_.Name -eq 'WarningBudget' }).FullName
    $focusFieldName = ($bar.Fields | Where-Object { $_.Name -eq 'FocusChangeBudget' }).FullName

    function Assert-BudgetSpend($methodName, $instructions, $fieldName, $otherFieldName, [int]$limit) {
        $spends = Find-Indices $instructions { param($ins) Test-FieldOperand $ins $fieldName }
        if ($spends.Count -ne 1) { throw "EnemyBar.$methodName must spend exactly its own budget field; found $($spends.Count) reads." }
        $at = $spends[0]
        if ($at + 2 -ge $instructions.Count -or
            -not (Test-IntConstant $instructions[$at + 1] $limit) -or
            -not (Test-MethodOperand $instructions[$at + 2] 'HawChow.GTFOKillFeed.DiagnosticBudget' 'TryUse')) {
            throw "EnemyBar.$methodName must call DiagnosticBudget.TryUse($limit) on its own budget. IL: $(Show $instructions)"
        }
        if ((Find-Indices $instructions { param($ins) Test-FieldOperand $ins $otherFieldName }).Count -gt 0) {
            throw "EnemyBar.$methodName must not touch another method's diagnostic budget."
        }
    }

    Assert-BudgetSpend 'LogFocus' $logFocusIl $focusFieldName $warningFieldName 16
    Assert-BudgetSpend 'Warn' $warnIl $warningFieldName $focusFieldName 5

    Write-Host "GLUE_WIELD_WIRING=PASS;PROBE=GlueGunWield.IsHeld;TRYCAST=TryCast<GlueGun>;TICK_MODE_FROM_PROBE=True;DRAWLABEL_MODE_TO_SELECTENEMYTAG=True;CFOAM_TAG=#F2A65A@1.0;NON_GLUE_TAG=Plugin.EnemyBarWeakspotColor;NAME_COLOR=Plugin.EnemyBarNameColor;FADE=0.76/0.96;LOG_BUDGETS=FocusChange:16+Warning:5"
}
finally { $assembly.Dispose() }
