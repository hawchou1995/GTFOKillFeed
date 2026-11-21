[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PluginDll,
    [Parameter(Mandatory = $true)][string]$CecilPath,
    [Parameter(Mandatory = $true)][string]$InteropDir
)
# Compiled-IL guard for the 1.2.24 "this hit really staggered" green digits.
# Text checks cannot prove which pass of the shipped draw really takes the green, whether the green is the SAME triple as
# the focused-enemy row, or whether the receipt hook exists without detouring a hitreact state machine. This asserts the
# emitted IL of the real assembly:
#   * StaggerHitPolicy.Decide is the one rule; IsStaggerType accepts only the decided Light(3)/Heavy(4);
#   * StaggerHitPolicy.TriggerInk delegates to BarPolicy.StaggerInk (one green), and BarPolicy.StaggerInk really carries
#     #78DCA0 (120/220/160);
#   * EnemyBar.DrawStaggerRow uses that same accessor, so the row and the digits cannot drift apart;
#   * DamageNumbers.Add builds both texts through StaggerHitPolicy.Parts; DamageNumbers.Draw fills the pass plan (both
#     halves measured through Measure, one explicit TriggerInk call) and hands it to the ONE production submission helper
#     DamageNumberPaint.Emit; Emit itself paints the green body AFTER the rim/halo and emboss passes (so the green can
#     never replace a rim, a prismatic halo, a tier ring or the emboss), gives every pass its own RGBA (so a weight stamp
#     can never inherit the previous pass's ink) and is the only thing Draw submits through;
#   * KillTracker reads the accumulator + reaction before the settlement, asks StaggerHitPolicy.Decide after it, and
#     reports a bounded receipt; the nested EnemyAgent.OnTakeDamage prefix exists and no ES_* state machine is patched;
#   * the installed Modules-ASM interop really defines ES_HitreactType with the pinned values, EnemyAgent.OnTakeDamage
#     with that enum last, ES_HitreactBase.CurrentReactionType/HitreactForbidden, Dam_EnemyDamageBase.m_damBuildToHitreact
#     and EnemyLocomotion.Hitreact.
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $PluginDll -PathType Leaf)) { throw "Plugin assembly is missing: $PluginDll" }
if (-not (Test-Path -LiteralPath $CecilPath -PathType Leaf)) { throw "Mono.Cecil is required to inspect compiled IL: $CecilPath" }
if (-not (Test-Path -LiteralPath $InteropDir -PathType Container)) { throw "GTFO interop directory is missing: $InteropDir" }
$modulesPath = Join-Path $InteropDir 'Modules-ASM.dll'
if (-not (Test-Path -LiteralPath $modulesPath -PathType Leaf)) { throw "GTFO Modules interop is missing: $modulesPath" }
Add-Type -Path (Resolve-Path -LiteralPath $CecilPath).Path

function Show($instructions) {
    $all = @($instructions | ForEach-Object { $_.ToString() })
    $visible = @($all | Select-Object -First 20)
    $text = $visible -join ' | '
    if ($all.Count -gt $visible.Count) { $text += " | ... ($($all.Count) instructions)" }
    return $text
}
function Find-CallIndices($instructions, [string]$declaringType, [string]$name) {
    $found = @()
    for ($i = 0; $i -lt $instructions.Count; $i++) {
        $operand = $instructions[$i].Operand
        if ($operand -is [Mono.Cecil.MethodReference] -and $operand.Name -eq $name -and
            $null -ne $operand.DeclaringType -and $operand.DeclaringType.FullName -eq $declaringType) { $found += $i }
    }
    return $found
}
function Find-FieldIndices($instructions, [string]$declaringType, [string]$name) {
    $found = @()
    for ($i = 0; $i -lt $instructions.Count; $i++) {
        $operand = $instructions[$i].Operand
        if ($operand -is [Mono.Cecil.FieldReference] -and $operand.Name -eq $name -and
            $null -ne $operand.DeclaringType -and $operand.DeclaringType.FullName -eq $declaringType) { $found += $i }
    }
    return $found
}
function Get-Method($type, [string]$name) {
    $methods = @($type.Methods | Where-Object { $_.Name -eq $name -and $_.HasBody })
    if ($methods.Count -ne 1) { throw "$($type.FullName).$name must exist exactly once with a body." }
    return $methods[0]
}
function Get-IntConstants($instructions) {
    @($instructions | Where-Object { $_.OpCode.Name -like 'ldc.i4*' -or $_.OpCode.Name -eq 'ldc.i4' } |
        ForEach-Object {
            if ($_.OpCode.Name -eq 'ldc.i4.m1') { -1 }
            elseif ($_.OpCode.Name -match '^ldc\.i4\.(\d)$') { [int]$Matches[1] }
            elseif ($_.OpCode.Name -eq 'ldc.i4.s') { [int]$_.Operand }
            else { [int]$_.Operand }
        })
}
function Get-FloatConstants($instructions) {
    @($instructions | Where-Object { $_.OpCode.Name -eq 'ldc.r4' } | ForEach-Object { [single]$_.Operand })
}

$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path -LiteralPath $PluginDll).Path)
$interop = $null
try {
    $module = $assembly.MainModule
    $policy = $module.GetType('HawChow.GTFOKillFeed.StaggerHitPolicy')
    if ($null -eq $policy) { throw 'Compiled plugin is missing HawChow.GTFOKillFeed.StaggerHitPolicy.' }
    $capture = $module.GetType('HawChow.GTFOKillFeed.StaggerHitCapture')
    if ($null -eq $capture) { throw 'Compiled plugin is missing HawChow.GTFOKillFeed.StaggerHitCapture.' }
    $barPolicy = $module.GetType('HawChow.GTFOKillFeed.BarPolicy')
    if ($null -eq $barPolicy) { throw 'Compiled plugin is missing HawChow.GTFOKillFeed.BarPolicy.' }

    # ---- the decision: only the DECIDED Light/Heavy may paint ---------------------------------------------------
    $isStagger = @($policy.Methods | Where-Object { $_.Name -eq 'IsStaggerType' -and $_.HasBody })
    if ($isStagger.Count -ne 1) { throw 'StaggerHitPolicy.IsStaggerType must exist exactly once with a body.' }
    $staggerIl = @($isStagger[0].Body.Instructions)
    $staggerConstants = @(Get-IntConstants $staggerIl)
    if ($staggerConstants -notcontains 3 -or $staggerConstants -notcontains 4) {
        throw "IsStaggerType must compare against Light=3 and Heavy=4. IL: $(Show $staggerIl)"
    }
    # 0/1 are allowed: they are the boolean result of the comparison (ldc.i4.0/ldc.i4.1), not a hitreact type.
    if ($staggerConstants -contains 2 -or $staggerConstants -contains 5 -or $staggerConstants -contains 6) {
        throw "IsStaggerType must not accept Micro(2)/ToDeath(5)/InstantRagdollDeath(6). IL: $(Show $staggerIl)"
    }
    $decide = @($policy.Methods | Where-Object { $_.Name -eq 'Decide' -and $_.HasBody })
    if ($decide.Count -ne 1) { throw 'StaggerHitPolicy.Decide must exist exactly once with a body.' }
    $decideIl = @($decide[0].Body.Instructions)
    # The facts are readonly FIELDS, so every gate shows up as a ldfld of StaggerHitFacts; IsStaggerType is a call.
    foreach ($requiredField in @('ReceiptSeen', 'AliveAfter', 'LocalSource', 'Forbidden', 'AccumulatorKnown',
        'AccumulatorAfter', 'ReactionKnown', 'ReactionBefore', 'ReactionAfter', 'DecidedType')) {
        if ((Find-FieldIndices $decideIl 'HawChow.GTFOKillFeed.StaggerHitFacts' $requiredField).Count -eq 0) {
            throw "StaggerHitPolicy.Decide must consult $requiredField; a gate that is never read is a gate that does not exist. IL: $(Show $decideIl)"
        }
    }
    if ((Find-CallIndices $decideIl 'HawChow.GTFOKillFeed.StaggerHitPolicy' 'IsStaggerType').Count -eq 0) {
        throw "StaggerHitPolicy.Decide must classify the decided type through IsStaggerType. IL: $(Show $decideIl)"
    }

    # ---- one green: TriggerInk -> BarPolicy.StaggerInk -> #78DCA0 -------------------------------------------------
    $triggerInk = @($policy.Methods | Where-Object { $_.Name -eq 'TriggerInk' -and $_.HasBody })
    if ($triggerInk.Count -ne 1) { throw 'StaggerHitPolicy.TriggerInk must exist exactly once with a body.' }
    $inkIl = @($triggerInk[0].Body.Instructions)
    if ((Find-CallIndices $inkIl 'HawChow.GTFOKillFeed.BarPolicy' 'StaggerInk').Count -ne 1) {
        throw "StaggerHitPolicy.TriggerInk must delegate to BarPolicy.StaggerInk exactly once (one shared green). IL: $(Show $inkIl)"
    }
    $staggerInk = @($barPolicy.Methods | Where-Object { $_.Name -eq 'StaggerInk' -and $_.HasBody })
    if ($staggerInk.Count -ne 1) { throw 'BarPolicy.StaggerInk must exist exactly once with a body.' }
    $inkFloats = @(Get-FloatConstants $staggerInk[0].Body.Instructions)
    foreach ($expected in @((120.0 / 255.0), (220.0 / 255.0), (160.0 / 255.0))) {
        if (-not ($inkFloats | Where-Object { [Math]::Abs([double]$_ - [double]$expected) -lt 0.00001 })) {
            throw "BarPolicy.StaggerInk must emit #78DCA0 ($expected). Found: $($inkFloats -join ',')."
        }
    }
    $enemyBar = $module.GetType('HawChow.GTFOKillFeed.EnemyBar')
    if ($null -eq $enemyBar) { throw 'Compiled plugin is missing HawChow.GTFOKillFeed.EnemyBar.' }
    $row = @($enemyBar.Methods | Where-Object { $_.Name -eq 'DrawStaggerRow' -and $_.HasBody })
    if ($row.Count -ne 1) { throw 'EnemyBar.DrawStaggerRow must exist exactly once with a body.' }
    $rowIl = @($row[0].Body.Instructions)
    if ((Find-CallIndices $rowIl 'HawChow.GTFOKillFeed.BarPolicy' 'StaggerInk').Count -ne 1) {
        throw "EnemyBar.DrawStaggerRow must take its colour from BarPolicy.StaggerInk (the same green the digits use). IL: $(Show $rowIl)"
    }
    foreach ($field in @('StaggerTextRed', 'StaggerTextGreen', 'StaggerTextBlue')) {
        if ((Find-FieldIndices $rowIl 'HawChow.GTFOKillFeed.BarPolicy' $field).Count -ne 0) {
            throw "EnemyBar.DrawStaggerRow must not build its own $field triple any more."
        }
    }

    # ---- the texts come from the one production call -------------------------------------------------------------
    $parts = @($policy.Methods | Where-Object { $_.Name -eq 'Parts' -and $_.HasBody })
    if ($parts.Count -ne 1) { throw 'StaggerHitPolicy.Parts must exist exactly once with a body.' }
    $partsIl = @($parts[0].Body.Instructions)
    if ((Find-CallIndices $partsIl 'HawChow.GTFOKillFeed.FeedbackPolicy' 'DamageText').Count -eq 0) {
        throw "StaggerHitPolicy.Parts must reuse FeedbackPolicy.DamageText so a number keeps its historical text/decoration. IL: $(Show $partsIl)"
    }
    $numbers = $module.GetType('HawChow.GTFOKillFeed.DamageNumbers')
    if ($null -eq $numbers) { throw 'Compiled plugin is missing HawChow.GTFOKillFeed.DamageNumbers.' }
    $addImmediate = @($numbers.Methods | Where-Object { $_.Name -eq 'AddImmediate' -and $_.HasBody })
    if ($addImmediate.Count -ne 1) { throw 'DamageNumbers.AddImmediate must exist exactly once with a body.' }
    $addIl = @($addImmediate[0].Body.Instructions)
    if ((Find-CallIndices $addIl 'HawChow.GTFOKillFeed.StaggerHitPolicy' 'Parts').Count -ne 1) {
        throw "DamageNumbers.AddImmediate must build both texts through StaggerHitPolicy.Parts exactly once. IL: $(Show $addIl)"
    }

    # ---- the drawing: green body only, after rim/halo/emboss, both halves measured, one submission helper -------
    $draw = @($numbers.Methods | Where-Object { $_.Name -eq 'Draw' -and $_.HasBody })
    if ($draw.Count -ne 1) { throw 'DamageNumbers.Draw must exist exactly once with a body.' }
    $drawIl = @($draw[0].Body.Instructions)
    $triggers = @(Find-CallIndices $drawIl 'HawChow.GTFOKillFeed.StaggerHitPolicy' 'TriggerInk')
    if ($triggers.Count -ne 1) {
        throw "DamageNumbers.Draw must take the green from StaggerHitPolicy.TriggerInk exactly once. IL: $(Show $drawIl)"
    }
    if ((Find-CallIndices $drawIl 'HawChow.GTFOKillFeed.DamageNumbers' 'Measure').Count -eq 0) {
        throw "DamageNumbers.Draw must measure both halves (Measure/CalcSize) before laying them out. IL: $(Show $drawIl)"
    }
    $numberType = $module.GetType('HawChow.GTFOKillFeed.DamageNumbers/Number')
    if ($null -eq $numberType) { throw 'DamageNumbers must keep its nested Number type.' }
    foreach ($field in @('Text', 'GreenText', 'Trigger')) {
        if (@($numberType.Fields | Where-Object { $_.Name -eq $field }).Count -ne 1) {
            throw "DamageNumbers.Number must keep the $field field (main text, green text, trigger amount)."
        }
    }
    if ((Find-FieldIndices $drawIl 'HawChow.GTFOKillFeed.DamageNumbers/Number' 'GreenText').Count -eq 0) {
        throw "DamageNumbers.Draw must read the GreenText part it paints. IL: $(Show $drawIl)"
    }
    # 1.2.24 review R1: the whole submission was extracted into DamageNumberPaint.Emit so every pass carries its own RGBA
    # - the previous shape mutated GUI.color and let the weight>1 stamps inherit the stagger green. Draw must submit
    # EXCLUSIVELY through that helper, otherwise the recorded/asserted passes would no longer be the shipped ones.
    $emitCalls = @(Find-CallIndices $drawIl 'HawChow.GTFOKillFeed.DamageNumberPaint' 'Emit')
    if ($emitCalls.Count -ne 1) {
        throw "DamageNumbers.Draw must submit every label through the ONE production helper DamageNumberPaint.Emit. IL: $(Show $drawIl)"
    }
    $submitPass = @($numbers.Methods | Where-Object { $_.Name -eq 'SubmitPass' -and $_.HasBody })
    if ($submitPass.Count -ne 1) { throw 'DamageNumbers.SubmitPass must exist exactly once: it is the only GUI boundary of a damage number.' }
    if ((Find-CallIndices $drawIl 'HawChow.GTFOKillFeed.DamageNumbers' 'SubmitPass').Count -eq 0) {
        throw "DamageNumbers.Draw must hand its own SubmitPass to the helper (never a different sink). IL: $(Show $drawIl)"
    }
    if ((Find-CallIndices $drawIl 'UnityEngine.GUI' 'Label').Count -ne 0 -or
        (Find-CallIndices $drawIl 'UnityEngine.GUI' 'set_color').Count -ne 1) {
        throw "DamageNumbers.Draw must not submit labels itself any more: GUI.color is only written once (the save/restore), all labels go through SubmitPass. IL: $(Show $drawIl)"
    }
    $submitIl = @($submitPass[0].Body.Instructions)
    $submitColors = @(Find-FieldIndices $submitIl 'HawChow.GTFOKillFeed.DamageNumberPaint/Pass' 'Red') +
        @(Find-FieldIndices $submitIl 'HawChow.GTFOKillFeed.DamageNumberPaint/Pass' 'Green') +
        @(Find-FieldIndices $submitIl 'HawChow.GTFOKillFeed.DamageNumberPaint/Pass' 'Blue') +
        @(Find-FieldIndices $submitIl 'HawChow.GTFOKillFeed.DamageNumberPaint/Pass' 'Alpha')
    $submitColorWrites = @(Find-CallIndices $submitIl 'UnityEngine.GUI' 'set_color')
    $submitLabels = @(Find-CallIndices $submitIl 'UnityEngine.GUI' 'Label')
    if ($submitColors.Count -lt 4 -or $submitColorWrites.Count -ne 1 -or $submitLabels.Count -ne 1) {
        throw "DamageNumbers.SubmitPass must set GUI.color from the pass's own RGBA and then submit exactly one label. IL: $(Show $submitIl)"
    }
    if ($submitColorWrites[0] -gt $submitLabels[0]) {
        throw "DamageNumbers.SubmitPass must set the colour BEFORE the label, so no pass is ever drawn with the previous pass's ink. IL: $(Show $submitIl)"
    }

    # ---- the production helper itself: pass order + explicit ink per pass ---------------------------------------
    $paint = $module.GetType('HawChow.GTFOKillFeed.DamageNumberPaint')
    if ($null -eq $paint) { throw 'Compiled plugin is missing HawChow.GTFOKillFeed.DamageNumberPaint.' }
    $emit = @($paint.Methods | Where-Object { $_.Name -eq 'Emit' -and $_.HasBody })
    if ($emit.Count -ne 1) { throw 'DamageNumberPaint.Emit must exist exactly once with a body.' }
    $emitIl = @($emit[0].Body.Instructions)
    $rimCalls = @(Find-CallIndices $emitIl 'HawChow.GTFOKillFeed.DamageFxPolicy' 'FoilRimColor')
    # The emboss OFFSET is computed once per number by Draw (the plan owns the resolution scaling); Emit must consume it
    # for its own emboss pass, and Draw must still ask DamageFxPolicy for it (so the highlight cannot silently disappear).
    $embossReads = @(Find-FieldIndices $emitIl 'HawChow.GTFOKillFeed.DamageNumberPaint/Plan' 'EmbossX') +
        @(Find-FieldIndices $emitIl 'HawChow.GTFOKillFeed.DamageNumberPaint/Plan' 'EmbossY')
    if ($rimCalls.Count -eq 0 -or $embossReads.Count -eq 0) {
        throw "DamageNumberPaint.Emit must keep the prismatic rim (FoilRimColor) and the emboss pass (Plan.EmbossX/Y). IL: $(Show $emitIl)"
    }
    if ((Find-CallIndices $drawIl 'HawChow.GTFOKillFeed.DamageFxPolicy' 'FoilEmbossOffset').Count -ne 1) {
        throw "DamageNumbers.Draw must compute the foil emboss offset exactly once per number (the plan feeds Emit). IL: $(Show $drawIl)"
    }
    $triggerReads = @(Find-FieldIndices $emitIl 'HawChow.GTFOKillFeed.DamageNumberPaint/Plan' 'TriggerRed') +
        @(Find-FieldIndices $emitIl 'HawChow.GTFOKillFeed.DamageNumberPaint/Plan' 'TriggerGreen') +
        @(Find-FieldIndices $emitIl 'HawChow.GTFOKillFeed.DamageNumberPaint/Plan' 'TriggerBlue')
    if ($triggerReads.Count -eq 0) {
        throw "DamageNumberPaint.Emit must read the green triple from the plan (the one StaggerHitPolicy.TriggerInk source). IL: $(Show $emitIl)"
    }
    $lastRim = ($rimCalls | Measure-Object -Maximum).Maximum
    $lastEmboss = ($embossReads | Measure-Object -Maximum).Maximum
    if (($triggerReads | Measure-Object -Minimum).Minimum -lt $lastRim -or
        ($triggerReads | Measure-Object -Minimum).Minimum -lt $lastEmboss) {
        throw "DamageNumberPaint.Emit must paint the green body AFTER the rim/halo and the emboss, so the green can never replace them. IL: $(Show $emitIl)"
    }
    $bodyReads = @(Find-FieldIndices $emitIl 'HawChow.GTFOKillFeed.DamageNumberPaint/Plan' 'BodyRed') +
        @(Find-FieldIndices $emitIl 'HawChow.GTFOKillFeed.DamageNumberPaint/Plan' 'BodyGreen') +
        @(Find-FieldIndices $emitIl 'HawChow.GTFOKillFeed.DamageNumberPaint/Plan' 'BodyBlue')
    # The weight>1 stamps must name the MAIN body ink AND the green ink separately: that is exactly the pair that used to
    # be one inherited GUI.color. Both triples have to be read at least twice (body pass + every stamp).
    if ($bodyReads.Count -lt 6 -or $triggerReads.Count -lt 6) {
        throw "DamageNumberPaint.Emit must set the main ink and the green ink per stamp instead of inheriting GUI.color. IL: $(Show $emitIl)"
    }

    # ---- the receipt: before/after reads, Decide, bounded report ------------------------------------------------
    $tracker = $module.GetType('HawChow.GTFOKillFeed.KillTracker')
    if ($null -eq $tracker) { throw 'Compiled plugin is missing HawChow.GTFOKillFeed.KillTracker.' }
    $begin = @($tracker.Methods | Where-Object { $_.Name -eq 'Begin' -and $_.HasBody })
    if ($begin.Count -ne 1) { throw 'KillTracker.Begin must exist exactly once with a body.' }
    $beginIl = @($begin[0].Body.Instructions)
    foreach ($read in @('TryReadAccumulator', 'TryReadReaction')) {
        if ((Find-CallIndices $beginIl 'HawChow.GTFOKillFeed.StaggerHitCapture' $read).Count -ne 1) {
            throw "KillTracker.Begin must snapshot $read exactly once, before the settlement runs. IL: $(Show $beginIl)"
        }
    }
    $record = @($tracker.Methods | Where-Object { $_.Name -eq 'RecordHit' -and $_.HasBody })
    if ($record.Count -ne 1) { throw 'KillTracker.RecordHit must exist exactly once with a body.' }
    $recordIl = @($record[0].Body.Instructions)
    if ((Find-CallIndices $recordIl 'HawChow.GTFOKillFeed.StaggerHitPolicy' 'Decide').Count -ne 1) {
        throw "KillTracker.RecordHit must ask StaggerHitPolicy.Decide exactly once. IL: $(Show $recordIl)"
    }
    if ((Find-CallIndices $recordIl 'HawChow.GTFOKillFeed.StaggerHitCapture' 'Report').Count -ne 1) {
        throw "KillTracker.RecordHit must emit the bounded stagger receipt through StaggerHitCapture.Report. IL: $(Show $recordIl)"
    }
    if ((Find-CallIndices $recordIl 'HawChow.GTFOKillFeed.StaggerHitCapture' 'TryReadAccumulator').Count -lt 1 -or
        (Find-CallIndices $recordIl 'HawChow.GTFOKillFeed.StaggerHitCapture' 'TryReadReaction').Count -lt 1) {
        throw "KillTracker.RecordHit must read the post-settlement accumulator and reaction state. IL: $(Show $recordIl)"
    }

    # ---- the hook: a non-ES_* end method, never a state machine -------------------------------------------------
    $patchedTypes = @($module.Types | Where-Object {
        @($_.CustomAttributes | Where-Object { $_.AttributeType.FullName -eq 'HarmonyLib.HarmonyPatch' }).Count -gt 0
    })
    foreach ($type in $patchedTypes) {
        foreach ($attribute in @($type.CustomAttributes | Where-Object { $_.AttributeType.FullName -eq 'HarmonyLib.HarmonyPatch' })) {
            foreach ($argument in @($attribute.ConstructorArguments)) {
                $text = [string]$argument.Value
                if ($text -match '(^|\.)ES_') {
                    throw "The plugin must not detour a hitreact/animation state machine: $($type.FullName) patches $text."
                }
            }
        }
    }
    $hook = $module.GetType('HawChow.GTFOKillFeed.EnemyAgentTakeDamagePatch')
    if ($null -eq $hook) { throw 'Compiled plugin is missing the EnemyAgentTakeDamagePatch receipt hook.' }
    $targets = @($hook.CustomAttributes | Where-Object { $_.AttributeType.FullName -eq 'HarmonyLib.HarmonyPatch' })
    if ($targets.Count -ne 1) { throw 'EnemyAgentTakeDamagePatch must carry exactly one HarmonyPatch attribute.' }
    $targetText = (@($targets[0].ConstructorArguments) | ForEach-Object { [string]$_.Value }) -join ','
    if ($targetText -notmatch 'EnemyAgent' -or $targetText -notmatch 'OnTakeDamage') {
        throw "EnemyAgentTakeDamagePatch must patch EnemyAgent.OnTakeDamage; found $targetText."
    }
    $prefix = @($hook.Methods | Where-Object { $_.Name -eq 'Prefix' -and $_.HasBody })
    if ($prefix.Count -ne 1) { throw 'EnemyAgentTakeDamagePatch.Prefix must exist exactly once with a body.' }
    if (@($prefix[0].Parameters | Where-Object { $_.ParameterType.FullName -eq 'ES_HitreactType' }).Count -ne 1) {
        throw 'The receipt prefix must take the DECIDED ES_HitreactType argument (the game passes the decided value).'
    }
    if ((Find-CallIndices @($prefix[0].Body.Instructions) 'HawChow.GTFOKillFeed.KillTracker' 'GuardAction').Count -ne 1) {
        throw 'The receipt prefix must run through KillTracker.GuardAction exactly once (a patch can never throw into the game).'
    }
    # The forwarding call lives in the compiler-generated lambda the guard takes, so look at the hook type and its nested types.
    $forwarded = 0
    foreach ($holder in @($hook) + @($hook.NestedTypes)) {
        foreach ($method in @($holder.Methods | Where-Object { $_.HasBody })) {
            $forwarded += (Find-CallIndices @($method.Body.Instructions) 'HawChow.GTFOKillFeed.StaggerHitCapture' 'NoteDecided').Count
        }
    }
    if ($forwarded -ne 1) {
        throw "The receipt prefix must forward the decided type to StaggerHitCapture.NoteDecided exactly once; found $forwarded."
    }

    # ---- installed interop really defines the facts the receipt reads -------------------------------------------
    $interop = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path -LiteralPath $modulesPath).Path)
    $interopModule = $interop.MainModule
    function Get-InteropEnumValue($enumType, [string]$name) {
        $field = $enumType.Fields | Where-Object { $_.Name -eq $name -and $_.HasConstant }
        if ($null -eq $field) { throw "Interop enum $($enumType.FullName) is missing $name." }
        return [int]$field.Constant
    }
    function Get-PluginConstInt([string]$typeName, [string]$fieldName) {
        $type = $module.GetType($typeName)
        if ($null -eq $type) { throw "Compiled plugin is missing $typeName." }
        $field = $type.Fields | Where-Object { $_.Name -eq $fieldName -and $_.HasConstant }
        if ($null -eq $field) { throw "Missing compile-time constant $typeName.$fieldName." }
        return [int]$field.Constant
    }
    $hitreactEnum = $interopModule.GetType('ES_HitreactType')
    if ($null -eq $hitreactEnum -or -not $hitreactEnum.IsEnum) { throw 'Installed interop is missing the ES_HitreactType enum.' }
    $expectedTypes = [ordered]@{
        'Unspecified'         = 0
        'None'                = 1
        'Micro'               = 2
        'Light'               = 3
        'Heavy'               = 4
        'ToDeath'             = 5
        'InstantRagdollDeath' = 6
    }
    foreach ($name in $expectedTypes.Keys) {
        $interopValue = Get-InteropEnumValue $hitreactEnum $name
        if ($interopValue -ne $expectedTypes[$name]) {
            throw "Interop ES_HitreactType.$name is $interopValue, expected $($expectedTypes[$name])."
        }
        $pluginValue = Get-PluginConstInt 'HawChow.GTFOKillFeed.StaggerHitPolicy' ('Hitreact' + $name)
        if ($pluginValue -ne $interopValue) {
            throw "StaggerHitPolicy.Hitreact$name ($pluginValue) does not match the installed interop value ($interopValue)."
        }
    }
    $agent = $interopModule.GetType('Enemies.EnemyAgent')
    if ($null -eq $agent) { throw 'Installed interop is missing Enemies.EnemyAgent.' }
    $takeDamage = @($agent.Methods | Where-Object { $_.Name -eq 'OnTakeDamage' })
    if ($takeDamage.Count -eq 0) { throw 'Installed interop is missing EnemyAgent.OnTakeDamage.' }
    $parameterTypes = @($takeDamage[0].Parameters | ForEach-Object { $_.ParameterType.FullName })
    if ($parameterTypes[-1] -ne 'ES_HitreactType') {
        throw "EnemyAgent.OnTakeDamage must take ES_HitreactType as its last parameter; found $($parameterTypes -join ',')."
    }
    $hitreactBase = $interopModule.GetType('ES_HitreactBase')
    if ($null -eq $hitreactBase) { throw 'Installed interop is missing ES_HitreactBase.' }
    $reactionProperty = $hitreactBase.Properties | Where-Object { $_.Name -eq 'CurrentReactionType' }
    if ($null -eq $reactionProperty -or $reactionProperty.PropertyType.FullName -ne 'ES_HitreactType') {
        throw 'ES_HitreactBase.CurrentReactionType must be the ES_HitreactType property.'
    }
    $forbiddenProperty = $hitreactBase.Properties | Where-Object { $_.Name -eq 'HitreactForbidden' }
    if ($null -eq $forbiddenProperty -or $forbiddenProperty.PropertyType.FullName -ne 'System.Boolean') {
        throw 'ES_HitreactBase.HitreactForbidden must be the System.Boolean property.'
    }
    $locomotion = $interopModule.GetType('Enemies.EnemyLocomotion')
    if ($null -eq $locomotion) { throw 'Installed interop is missing Enemies.EnemyLocomotion.' }
    $hitreactProperty = $locomotion.Properties | Where-Object { $_.Name -eq 'Hitreact' }
    if ($null -eq $hitreactProperty -or $hitreactProperty.PropertyType.FullName -ne 'ES_HitreactBase') {
        throw 'EnemyLocomotion.Hitreact must be the ES_HitreactBase property the receipt reads through.'
    }
    $damageBase = $interopModule.GetType('Dam_EnemyDamageBase')
    if ($null -eq $damageBase) { throw 'Installed interop is missing Dam_EnemyDamageBase.' }
    $accumulator = $damageBase.Properties | Where-Object { $_.Name -eq 'm_damBuildToHitreact' }
    if ($null -eq $accumulator -or $accumulator.PropertyType.FullName -ne 'System.Single') {
        throw 'Dam_EnemyDamageBase.m_damBuildToHitreact must be the System.Single accumulator the receipt compares.'
    }

    $summary = 'STAGGER_INK_IL=PASS;TYPE=Light(3)+Heavy(4)-only;GREEN=BarPolicy.StaggerInk(#78DCA0);' +
    'SHARED=EnemyBar.DrawStaggerRow+DamageNumbers.Draw;TEXT=StaggerHitPolicy.Parts->FeedbackPolicy.DamageText;' +
    'PAINT=DamageNumberPaint.Emit(per-pass-RGBA,green-body-after-rim+emboss,measured,seam);SUBMIT=DamageNumbers.SubmitPass-once;' +
    'RECEIPT=OnTakeDamage(ES_HitreactType)+build-before/after+reaction;' +
        'ES_DETOURS=0;INTEROP=ES_HitreactType+OnTakeDamage+m_damBuildToHitreact+CurrentReactionType+HitreactForbidden+Hitreact'
    Write-Host $summary
}
finally {
    if ($null -ne $interop) { $interop.Dispose() }
    $assembly.Dispose()
}
