[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PluginDll,
    [Parameter(Mandatory = $true)][string]$CecilPath
)
# Compiled-IL guard for the configurable damage-number opacity.
# Source/comment checks cannot prove that the shipped binary really routes the opacity into the
# drawing maths, so this asserts the emitted IL of the real assembly:
#   * FeedbackPolicy.DefaultNumberOpacity is a compile-time constant 1f (existing .cfg keeps the
#     previous look, and 1f * fade reproduces the old alpha exactly);
#   * DamageNumbers.Draw reads the live Plugin.DamageNumberOpacity field and normalises it through
#     FeedbackPolicy.NumberOpacity exactly once - the raw config is never used as the multiplier;
#   * the zero gate returns before the first GUI.color write, so Opacity=0 submits no label at all;
#   * FeedbackPolicy.Alpha is not called from Draw (that would bypass the opacity), and the text
#     alpha goes through FeedbackPolicy.NumberTextAlpha exactly once so the colour alpha and the
#     total opacity are each applied once.
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $PluginDll -PathType Leaf)) { throw "Plugin assembly is missing: $PluginDll" }
if (-not (Test-Path -LiteralPath $CecilPath -PathType Leaf)) { throw "Mono.Cecil is required to inspect compiled IL: $CecilPath" }
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

$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path -LiteralPath $PluginDll).Path)
try {
    $module = $assembly.MainModule
    $policy = $module.GetType('HawChow.GTFOKillFeed.FeedbackPolicy')
    if ($null -eq $policy) { throw 'Compiled plugin is missing HawChow.GTFOKillFeed.FeedbackPolicy.' }
    $defaultField = $policy.Fields | Where-Object { $_.Name -eq 'DefaultNumberOpacity' }
    if ($null -eq $defaultField -or -not $defaultField.HasConstant -or [single]$defaultField.Constant -ne 1.0) {
        throw 'FeedbackPolicy.DefaultNumberOpacity must stay a compile-time constant 1.0 so an existing .cfg without the key keeps the shipped look.'
    }

    $numbers = $module.GetType('HawChow.GTFOKillFeed.DamageNumbers')
    if ($null -eq $numbers) { throw 'Compiled plugin is missing HawChow.GTFOKillFeed.DamageNumbers.' }
    $draw = @($numbers.Methods | Where-Object { $_.Name -eq 'Draw' })
    if ($draw.Count -ne 1 -or -not $draw[0].HasBody) { throw 'DamageNumbers must keep exactly one Draw method with a body.' }
    $il = @($draw[0].Body.Instructions)

    $opacityCalls = Find-CallIndices $il 'HawChow.GTFOKillFeed.FeedbackPolicy' 'NumberOpacity'
    $alphaCalls = Find-CallIndices $il 'HawChow.GTFOKillFeed.FeedbackPolicy' 'Alpha'
    $numberAlphaCalls = Find-CallIndices $il 'HawChow.GTFOKillFeed.FeedbackPolicy' 'NumberAlpha'
    $textAlphaCalls = Find-CallIndices $il 'HawChow.GTFOKillFeed.FeedbackPolicy' 'NumberTextAlpha'
    $configReads = Find-FieldIndices $il 'HawChow.GTFOKillFeed.Plugin' 'DamageNumberOpacity'
    $colorWrites = Find-CallIndices $il 'UnityEngine.GUI' 'set_color'
    # 1.2.24 review R1: the labels are submitted by the production helper DamageNumberPaint.Emit through
    # DamageNumbers.SubmitPass, so "the first paint" of a frame is the Emit call (SubmitPass owns GUI.color).
    $emitCalls = Find-CallIndices $il 'HawChow.GTFOKillFeed.DamageNumberPaint' 'Emit'

    # The null-safe config read compiles to a null test plus the value read, so one or two ldsfld of
    # Plugin.DamageNumberOpacity are expected - never a cached copy elsewhere and never none.
    if ($configReads.Count -lt 1 -or $configReads.Count -gt 2) { throw "DamageNumbers.Draw must read Plugin.DamageNumberOpacity once per frame so a reload reaches numbers already on screen. IL: $(Show $il)" }
    if ($opacityCalls.Count -ne 1) { throw "DamageNumbers.Draw must normalise the live config through FeedbackPolicy.NumberOpacity exactly once. IL: $(Show $il)" }
    if ($opacityCalls[0] -lt $configReads[0]) { throw 'FeedbackPolicy.NumberOpacity must receive the live config value, not a cached one.' }
    if ($alphaCalls.Count -ne 0) { throw "DamageNumbers.Draw must not call FeedbackPolicy.Alpha directly: the fade has to go through NumberAlpha so the opacity cannot be bypassed. IL: $(Show $il)" }
    # The fade*opacity alpha is now built by the SHARED placement function (review A5): Draw passes the live
    # opacity in, FeedbackPolicy.NumberPose applies NumberAlpha exactly once, and neither path may call Alpha
    # directly. Either shape is accepted, but exactly one of them has to hold.
    $poseCalls = Find-CallIndices $il 'HawChow.GTFOKillFeed.FeedbackPolicy' 'NumberPose'
    if ($poseCalls.Count -eq 1) {
        $poseMethod = @($module.GetType('HawChow.GTFOKillFeed.FeedbackPolicy').Methods | Where-Object { $_.Name -eq 'NumberPose' })[0]
        if ($null -eq $poseMethod -or -not $poseMethod.HasBody) { throw 'FeedbackPolicy.NumberPose must exist with a body.' }
        $poseIl = @($poseMethod.Body.Instructions)
        if ((Find-CallIndices $poseIl 'HawChow.GTFOKillFeed.FeedbackPolicy' 'NumberAlpha').Count -ne 1) {
            throw 'FeedbackPolicy.NumberPose must build the shared fade*opacity alpha through NumberAlpha exactly once.'
        }
        if ((Find-CallIndices $poseIl 'HawChow.GTFOKillFeed.FeedbackPolicy' 'Alpha').Count -ne 0) {
            throw 'FeedbackPolicy.NumberPose must not bypass the opacity by calling Alpha directly.'
        }
    } elseif ($numberAlphaCalls.Count -ne 1) {
        throw "DamageNumbers.Draw must build the shared fade*opacity alpha through FeedbackPolicy.NumberAlpha exactly once (or delegate to NumberPose). IL: $(Show $il)"
    }
    # 1.2.24: there are two body passes now - the number's historical ink and the part the game proved it staggered with
    # (the stagger green). BOTH take their alpha through NumberTextAlpha, so fade*opacity is still applied once per pass
    # and a half-transparent number stays half transparent, green part included. No raw alpha may reach GUI.color.
    if ($textAlphaCalls.Count -ne 2) { throw "DamageNumbers.Draw must apply the text alpha through FeedbackPolicy.NumberTextAlpha once per body pass (main + stagger green). IL: $(Show $il)" }
    if ($emitCalls.Count -ne 1) { throw "DamageNumbers.Draw must submit every label through the ONE production helper DamageNumberPaint.Emit. IL: $(Show $il)" }
    if ($colorWrites.Count -ne 1) { throw "DamageNumbers.Draw must not write GUI.color per label any more: the only write is the save/restore around the passes. IL: $(Show $il)" }
    if ($opacityCalls[0] -gt $emitCalls[0]) { throw 'The opacity must be resolved before the first submitted pass, otherwise the outline pass cannot honour it.' }

    # Opacity 0 must submit nothing: a ret has to sit between the opacity read and the first paint.
    $gate = $false
    for ($i = $opacityCalls[0]; $i -lt $emitCalls[0]; $i++) { if ($il[$i].OpCode.Name -eq 'ret') { $gate = $true; break } }
    if (-not $gate) { throw "DamageNumbers.Draw must return before the first submitted pass when the opacity is 0. IL: $(Show $il)" }

    # ... and the helper hands every pass to DamageNumbers.SubmitPass, which sets GUI.color from that pass's own RGBA
    # before the label - no pass may be drawn with the alpha (or the ink) of the pass before it.
    $submitPass = @($numbers.Methods | Where-Object { $_.Name -eq 'SubmitPass' -and $_.HasBody })
    if ($submitPass.Count -ne 1) { throw 'DamageNumbers.SubmitPass must exist exactly once: it is the only GUI boundary of a damage number.' }
    if ((Find-CallIndices $il 'HawChow.GTFOKillFeed.DamageNumbers' 'SubmitPass').Count -eq 0) {
        throw "DamageNumbers.Draw must hand its own SubmitPass to the helper. IL: $(Show $il)"
    }
    $submitIl = @($submitPass[0].Body.Instructions)
    $submitSetColor = Find-CallIndices $submitIl 'UnityEngine.GUI' 'set_color'
    $submitLabels = Find-CallIndices $submitIl 'UnityEngine.GUI' 'Label'
    $submitAlpha = Find-FieldIndices $submitIl 'HawChow.GTFOKillFeed.DamageNumberPaint/Pass' 'Alpha'
    if ($submitSetColor.Count -ne 1 -or $submitLabels.Count -ne 1 -or $submitAlpha.Count -ne 1) {
        throw "DamageNumbers.SubmitPass must set GUI.color from the pass's own Alpha (and ink) and then submit one label. IL: $(Show $submitIl)"
    }
    if ($submitSetColor[0] -gt $submitLabels[0]) { throw 'DamageNumbers.SubmitPass must set the colour BEFORE the label.' }

    $plugin = $module.GetType('HawChow.GTFOKillFeed.Plugin')
    $configField = if ($null -ne $plugin) { $plugin.Fields | Where-Object { $_.Name -eq 'DamageNumberOpacity' } } else { $null }
    if ($null -eq $configField -or $configField.FieldType.FullName -notmatch '^BepInEx\.Configuration\.ConfigEntry`1<System\.Single>$') {
        throw 'Plugin.DamageNumberOpacity must stay a BepInEx ConfigEntry<float> so the existing hot reload owns it.'
    }

    Write-Host "DAMAGE_NUMBER_OPACITY_IL=PASS;DEFAULT=1.0;CONFIG_READS=$($configReads.Count);NORMALISE=$($opacityCalls.Count);FADE_MULTIPLY=$($numberAlphaCalls.Count);TEXT_ALPHA=$($textAlphaCalls.Count);DIRECT_FADE_CALLS=$($alphaCalls.Count);ZERO_GATE=ret-before-paint"
}
finally { $assembly.Dispose() }
