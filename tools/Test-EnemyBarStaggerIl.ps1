[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PluginDll,
    [Parameter(Mandatory = $true)][string]$CecilPath,
    [Parameter(Mandatory = $true)][string]$InteropDir
)
# Compiled-IL guard for the 1.2.23 stagger row and the corpse-own clone proof.
# Text/comment checks cannot prove that the shipped binary really paints the row under the monster name, really
# classifies a limb through the anatomical enum, and really refuses a dead existing copy. This asserts the emitted IL:
#   * EnemyBar.Draw calls DrawStaggerRow exactly once, after DrawLabel;
#   * DrawLabel computes the name's real first-glyph origin through BarPolicy.TextOriginX (rect + alignment +
#     padding + content offset) and stores it for the row;
#   * DrawStaggerRow uses that origin (or the bar's left edge when ShowName is off), owns its style/content/font size,
#     scales through StaggerFontPixels/StaggerMinFontPixels, wraps through StaggerRows, clamps through
#     StaggerAvailableWidth/StaggerRowLeft/StaggerRowY, and colours the text from BarPolicy.StaggerText* - it never
#     touches NameContent/PartContent/NumbersContent, the weakspot colour or the C-Foam amber;
#   * EnemyBar.Tick refreshes through ReadStagger + BarPolicy.StaggerLine exactly once;
#   * ReadStagger reads the game's destroyable-limb facts (m_destruction + HasDestruction + IsDestroyed + m_healthMax,
#     and NOT the current m_health), classifies the limb through CD_CharacterDestructionCollider.m_bodyPart and
#     BarPolicy.BodyGroup (never eLimbDamageType/m_type), aggregates the two groups through StaggerGroupBuilder,
#     excludes the head limb, gates both part items on GameData.HealthData.HitreactOnLimbDestruction through
#     BarPolicy.LimbBreakIsHitreact, and reads the normal item as DamageUntilHitreact ALONE - it must not read the
#     Dam_EnemyDamageBase.m_damBuildToHitreact accumulator (that stays with the 1.2.24 green digits);
#   * the installed Modules-ASM interop really defines CharacterDestruction.CD_BodypartType with the pinned values,
#     CD_CharacterDestructionCollider.m_bodyPart of that type, and Dam_EnemyDamageLimb.m_health/m_healthMax/
#     m_destruction of the expected types; eLimbDamageType is pinned as the unrelated Normal/Weakspot/Armor enum;
#   * CorpseFeedback.FadeRenderer asks CorpseFade.IsSafeInput (so a dangerous candidate cannot be armed), and
#     FadeRendererCast.Observe really scans the GameObject AND its children for registrations;
#   * CorpseFade.TryNative reads Decay.m_decayRenderer and refuses through FadeRendererCast.HasLiveCopy before Play.
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
function Find-TypeMentions($instructions, [string]$typeName) {
    $found = @()
    for ($i = 0; $i -lt $instructions.Count; $i++) {
        $operand = $instructions[$i].Operand
        if ($null -eq $operand) { continue }
        $text = $operand.ToString()
        if ($text -like "*$typeName*") { $found += $i }
    }
    return $found
}

$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path -LiteralPath $PluginDll).Path)
$interop = $null
try {
    $module = $assembly.MainModule
    $bar = $module.GetType('HawChow.GTFOKillFeed.EnemyBar')
    if ($null -eq $bar) { throw 'Compiled plugin is missing HawChow.GTFOKillFeed.EnemyBar.' }
    $barPolicy = $module.GetType('HawChow.GTFOKillFeed.BarPolicy')
    if ($null -eq $barPolicy) { throw 'Compiled plugin is missing HawChow.GTFOKillFeed.BarPolicy.' }

    # ---- Draw order ----------------------------------------------------------------------------------------------
    $draw = @($bar.Methods | Where-Object { $_.Name -eq 'Draw' -and $_.HasBody })
    if ($draw.Count -ne 1) { throw 'EnemyBar.Draw must exist exactly once with a body.' }
    $drawIl = @($draw[0].Body.Instructions)
    $drawRow = Find-CallIndices $drawIl 'HawChow.GTFOKillFeed.EnemyBar' 'DrawStaggerRow'
    $drawLabel = Find-CallIndices $drawIl 'HawChow.GTFOKillFeed.EnemyBar' 'DrawLabel'
    if ($drawRow.Count -ne 1 -or $drawLabel.Count -ne 1 -or $drawRow[0] -lt $drawLabel[0]) {
        throw "EnemyBar.Draw must call DrawStaggerRow exactly once and after DrawLabel. IL: $(Show $drawIl)"
    }

    # ---- The row is left-anchored to the name's real first glyph ------------------------------------------------
    foreach ($requiredMethod in @('StaggerFontPixels', 'StaggerMinFontPixels', 'StaggerTokens', 'StaggerRows',
        'StaggerAvailableWidth', 'StaggerRowLeft', 'StaggerRowY', 'TextOriginX', 'LimbBreakIsHitreact',
        'ThresholdValue', 'IsUsableThreshold', 'StaggerTokenCount')) {
        if (@($barPolicy.Methods | Where-Object { $_.Name -eq $requiredMethod -and $_.HasBody }).Count -ne 1) {
            throw "BarPolicy.$requiredMethod must exist exactly once with a body."
        }
    }
    foreach ($removed in @('StaggerRowX', 'StaggerRowFits')) {
        if (@($barPolicy.Methods | Where-Object { $_.Name -eq $removed }).Count -ne 0) {
            throw "BarPolicy.$removed (the old centred/refuse-to-draw helper) must not exist any more."
        }
    }
    # 1.2.25: the remaining/current machinery must be GONE. A row that shows the theoretical maximum cannot subtract the
    # live accumulator and cannot print a current durability, so those helpers (and the dash they returned) may not exist.
    foreach ($gone in @('StaggerRemaining', 'RequiredDamage', 'DamageAmount', 'DamageSpan')) {
        if (@($barPolicy.Methods | Where-Object { $_.Name -eq $gone }).Count -ne 0) {
            throw "BarPolicy.$gone (the removed remaining/current formatter) must not exist any more: the row shows the theoretical maximum only."
        }
    }
    if ($null -ne ($barPolicy.Fields | Where-Object { $_.Name -eq 'NoStaggerValue' })) {
        throw 'BarPolicy.NoStaggerValue (the dash an omitted item used to print) must not exist any more.'
    }
    # One value in, so a caller physically cannot hand in a current durability next to the capacity.
    $builderType = $barPolicy.NestedTypes | Where-Object { $_.Name -eq 'StaggerGroupBuilder' }
    if ($null -eq $builderType) { throw 'BarPolicy must keep the nested StaggerGroupBuilder.' }
    $addMethods = @($builderType.Methods | Where-Object { $_.Name -eq 'Add' -and $_.HasBody })
    if ($addMethods.Count -ne 1 -or $addMethods[0].Parameters.Count -ne 1) {
        throw "BarPolicy.StaggerGroupBuilder.Add must take exactly ONE argument (the capacity); found $(if ($addMethods.Count -eq 1) { $addMethods[0].Parameters.Count } else { 'no method' })."
    }
    $rowsMethod = @($barPolicy.Methods | Where-Object { $_.Name -eq 'StaggerRows' -and $_.HasBody })
    if ($rowsMethod.Count -ne 1 -or $rowsMethod[0].Parameters.Count -ne 5) {
        throw "BarPolicy.StaggerRows must be the count-aware five-argument plan (available, fullWidth, firstLineWidth, widestTokenWidth, tokens); found $($rowsMethod[0].Parameters.Count) parameters."
    }
    if ($null -eq ($bar.Fields | Where-Object { $_.Name -eq '_nameOriginX' -and $_.FieldType.FullName -eq 'System.Single' })) {
        throw 'EnemyBar must keep the float _nameOriginX field shared with the green row.'
    }
    if ($null -eq ($bar.Fields | Where-Object { $_.Name -eq '_nameOriginValid' -and $_.FieldType.FullName -eq 'System.Boolean' })) {
        throw 'EnemyBar must keep the bool _nameOriginValid field (name hidden => bar left edge).'
    }
    if ($null -eq ($bar.Fields | Where-Object { $_.Name -eq 'StaggerLines' -and $_.FieldType.FullName -eq 'System.String[]' })) {
        throw 'EnemyBar must keep the string[] StaggerLines line buffer (the row wraps without allocating and without empty lines).'
    }
    # 1.2.25: ONE maximum per group. The value type keeps only HasValue + Max, and the builder only keeps a maximum, so a
    # minimum cannot come back and a group of 40 + 60 can only ever print 60 (no "40-60" range).
    $groupValues = $barPolicy.NestedTypes | Where-Object { $_.Name -eq 'StaggerGroupValues' }
    if ($null -eq $groupValues) { throw 'BarPolicy must keep the nested StaggerGroupValues.' }
    if ($null -eq ($groupValues.Fields | Where-Object { $_.Name -eq 'Max' -and $_.FieldType.FullName -eq 'System.Single' -and -not $_.IsStatic })) {
        throw 'BarPolicy.StaggerGroupValues must keep the single float Max field.'
    }
    foreach ($goneField in @('MaxMin', 'MaxMax', 'CurrentMin', 'CurrentMax', 'CapacityMin', 'CapacityMax')) {
        if ($null -ne ($groupValues.Fields | Where-Object { $_.Name -eq $goneField })) {
            throw "BarPolicy.StaggerGroupValues.$goneField (a range/current half) must not exist: a group prints one maximum only."
        }
    }
    foreach ($goneField in @('_min', '_currentMin', '_currentMax', '_capacityMin', '_capacityMax')) {
        if ($null -ne ($builderType.Fields | Where-Object { $_.Name -eq $goneField })) {
            throw "BarPolicy.StaggerGroupBuilder.$goneField must not exist: the builder keeps only the maximum."
        }
    }
    # 1.2.25: the green row may only ever describe the CURRENT target, and only while that target is alive.
    if ($null -eq ($bar.Fields | Where-Object { $_.Name -eq '_staggerTargetId' -and $_.FieldType.FullName -eq 'System.Int32' })) {
        throw 'EnemyBar must keep the int _staggerTargetId field that records which enemy the cached row describes.'
    }
    if ($null -eq ($bar.Fields | Where-Object { $_.Name -eq '_haveTarget' -and $_.FieldType.FullName -eq 'System.Boolean' })) {
        throw 'EnemyBar must keep the bool _haveTarget field the row is gated on.'
    }

    $label = @($bar.Methods | Where-Object { $_.Name -eq 'DrawLabel' -and $_.HasBody })
    if ($label.Count -ne 1) { throw 'EnemyBar.DrawLabel must exist exactly once with a body.' }
    $labelIl = @($label[0].Body.Instructions)
    if ((Find-CallIndices $labelIl 'HawChow.GTFOKillFeed.EnemyBar' 'TextOriginX').Count -ne 1) {
        throw "EnemyBar.DrawLabel must compute the name's real first-glyph origin through TextOriginX exactly once. IL: $(Show $labelIl)"
    }
    if ((Find-FieldIndices $labelIl 'HawChow.GTFOKillFeed.EnemyBar' '_nameOriginX').Count -lt 1 -or
        (Find-FieldIndices $labelIl 'HawChow.GTFOKillFeed.EnemyBar' '_nameOriginValid').Count -lt 1) {
        throw "EnemyBar.DrawLabel must store both _nameOriginX and _nameOriginValid for the row. IL: $(Show $labelIl)"
    }
    $origin = @($bar.Methods | Where-Object { $_.Name -eq 'TextOriginX' -and $_.HasBody })
    if ($origin.Count -ne 1) { throw 'EnemyBar.TextOriginX must exist exactly once with a body.' }
    $originIl = @($origin[0].Body.Instructions)
    if ((Find-CallIndices $originIl 'HawChow.GTFOKillFeed.BarPolicy' 'TextOriginX').Count -ne 1 -or
        (Find-CallIndices $originIl 'UnityEngine.GUIStyle' 'get_alignment').Count -ne 1) {
        throw "EnemyBar.TextOriginX must pass the style alignment into BarPolicy.TextOriginX; rect or text width alone is not the first glyph. IL: $(Show $originIl)"
    }

    $row = @($bar.Methods | Where-Object { $_.Name -eq 'DrawStaggerRow' -and $_.HasBody })
    if ($row.Count -ne 1) { throw 'EnemyBar.DrawStaggerRow must exist exactly once with a body.' }
    if ($row[0].Parameters.Count -ne 5) {
        throw "EnemyBar.DrawStaggerRow must take (bar, screenWidth, screenHeight, opacity, shadowColor); found $($row[0].Parameters.Count) parameters."
    }
    $rowIl = @($row[0].Body.Instructions)
    foreach ($required in @(
        @{ Type = 'BarPolicy'; Name = 'StaggerFontPixels'; Min = 1 },
        @{ Type = 'BarPolicy'; Name = 'StaggerMinFontPixels'; Min = 1 },
        @{ Type = 'BarPolicy'; Name = 'StaggerTokens'; Min = 1 },
        @{ Type = 'BarPolicy'; Name = 'StaggerRows'; Min = 1 },
        @{ Type = 'BarPolicy'; Name = 'StaggerAvailableWidth'; Min = 2 },
        @{ Type = 'BarPolicy'; Name = 'StaggerRowLeft'; Min = 1 },
        @{ Type = 'BarPolicy'; Name = 'StaggerRowY'; Min = 1 },
        @{ Type = 'EnemyBar'; Name = 'DrawLabelText'; Min = 1 }
    )) {
        $count = (Find-CallIndices $rowIl "HawChow.GTFOKillFeed.$($required.Type)" $required.Name).Count
        if ($count -lt $required.Min) {
            throw "EnemyBar.DrawStaggerRow must call $($required.Type).$($required.Name) at least $($required.Min) time(s); found $count. IL: $(Show $rowIl)"
        }
    }
    # The row owns its font size: it must write GUIStyle.fontSize itself instead of inheriting DrawLabel's pass.
    if ((Find-CallIndices $rowIl 'UnityEngine.GUIStyle' 'set_fontSize').Count -lt 1) {
        throw "EnemyBar.DrawStaggerRow must set its own font size (GUIStyle.set_fontSize); it cannot rely on DrawLabel. IL: $(Show $rowIl)"
    }
    # The first glyph is rect + padding + content offset, so the row must read the style padding and offset when it
    # converts the name origin back into a rect x. A plain MiddleCenter -> MiddleLeft swap would not read either.
    if ((Find-CallIndices $rowIl 'UnityEngine.GUIStyle' 'get_padding').Count -lt 1 -or
        (Find-CallIndices $rowIl 'UnityEngine.GUIStyle' 'get_contentOffset').Count -lt 1) {
        throw "EnemyBar.DrawStaggerRow must place the rect from the style padding + content offset (real first-glyph origin). IL: $(Show $rowIl)"
    }
    if ((Find-CallIndices $rowIl 'HawChow.GTFOKillFeed.EnemyBar' 'DrawLabel').Count -ne 0) {
        throw 'EnemyBar.DrawStaggerRow must not call DrawLabel: the row owns its layout.'
    }
    # The source of the anchor: the stored name origin when the name is shown, otherwise the bar's own left edge.
    if ((Find-FieldIndices $rowIl 'HawChow.GTFOKillFeed.EnemyBar' '_nameOriginX').Count -lt 1 -or
        (Find-FieldIndices $rowIl 'HawChow.GTFOKillFeed.EnemyBar' '_nameOriginValid').Count -lt 1) {
        throw "EnemyBar.DrawStaggerRow must read _nameOriginX/_nameOriginValid to start under the monster name. IL: $(Show $rowIl)"
    }
    if ((Find-FieldIndices $rowIl 'HawChow.GTFOKillFeed.Plugin' 'EnemyBarShowName').Count -lt 1) {
        throw "EnemyBar.DrawStaggerRow must read EnemyBar/ShowName to fall back to the health bar's left edge. IL: $(Show $rowIl)"
    }
    if ((Find-CallIndices $rowIl 'UnityEngine.Rect' 'get_x').Count -lt 1) {
        throw "EnemyBar.DrawStaggerRow must use the bar's Rect.x when the name is hidden. IL: $(Show $rowIl)"
    }
    # Head and Screen are the same row: it must never branch on the bar anchor mode.
    if ((Find-FieldIndices $rowIl 'HawChow.GTFOKillFeed.Plugin' 'EnemyBarAnchor').Count -ne 0) {
        throw 'EnemyBar.DrawStaggerRow must not read EnemyBar/Anchor: the row works identically in Head and Screen mode.'
    }
    # const float fields are inlined by the compiler, so the colour is asserted on the emitted literals: the three
    # #78DCA0 components must be present, and none of the C-Foam amber components may be.
    function Get-ConstSingle([string]$typeName, [string]$fieldName) {
        $type = $module.GetType($typeName)
        if ($null -eq $type) { throw "Compiled plugin is missing $typeName." }
        $field = $type.Fields | Where-Object { $_.Name -eq $fieldName -and $_.HasConstant }
        if ($null -eq $field) { throw "Missing compile-time constant $typeName.$fieldName." }
        return [single]$field.Constant
    }
    function Count-R4($instructions, [single]$value) {
        $count = 0
        for ($i = 0; $i -lt $instructions.Count; $i++) {
            if ($instructions[$i].OpCode.Name -ne 'ldc.r4') { continue }
            $operand = [single]$instructions[$i].Operand
            if ([Math]::Abs($operand - $value) -lt 0.000001) { $count++ }
        }
        return $count
    }
    # 1.2.24: the row and the damage digits take the fixed green from ONE accessor (BarPolicy.StaggerInk), so a second,
    # slightly different green cannot appear. The accessor itself still emits the pinned #78DCA0 triple - asserted by
    # scripts/Test-StaggerInkIl.ps1 on this same assembly.
    $inkCalls = 0
    for ($i = 0; $i -lt $rowIl.Count; $i++) {
        $inkOperand = $rowIl[$i].Operand
        if ($inkOperand -is [Mono.Cecil.MethodReference] -and $inkOperand.Name -eq 'StaggerInk' -and
            $null -ne $inkOperand.DeclaringType -and $inkOperand.DeclaringType.FullName -eq 'HawChow.GTFOKillFeed.BarPolicy') {
            $inkCalls++
        }
    }
    if ($inkCalls -ne 1) {
        throw "EnemyBar.DrawStaggerRow must take its colour from BarPolicy.StaggerInk exactly once. IL: $(Show $rowIl)"
    }
    foreach ($amber in @('CfoamTagRed', 'CfoamTagGreen', 'CfoamTagBlue')) {
        $value = Get-ConstSingle 'HawChow.GTFOKillFeed.BarPolicy' $amber
        if ((Count-R4 $rowIl $value) -ne 0) {
            throw "EnemyBar.DrawStaggerRow must never borrow the C-Foam amber component BarPolicy.$amber ($value). IL: $(Show $rowIl)"
        }
    }
    foreach ($foreign in @('NameContent', 'PartContent', 'NumbersContent')) {
        if ((Find-FieldIndices $rowIl 'HawChow.GTFOKillFeed.EnemyBar' $foreign).Count -ne 0) {
            throw "EnemyBar.DrawStaggerRow must not touch ${foreign}: the row has its own GUIContent."
        }
    }
    foreach ($foreignColor in @('_weakspotColor', '_nameColor', '_healthTextColor', '_fillColor')) {
        if ((Find-FieldIndices $rowIl 'HawChow.GTFOKillFeed.EnemyBar' $foreignColor).Count -ne 0) {
            throw "EnemyBar.DrawStaggerRow must not read ${foreignColor}: the green is a fixed constant."
        }
    }
    if ((Find-FieldIndices $rowIl 'HawChow.GTFOKillFeed.EnemyBar' '_labelStagger').Count -lt 1 -or
        (Find-FieldIndices $rowIl 'HawChow.GTFOKillFeed.EnemyBar' '_stagger').Count -lt 1) {
        throw "EnemyBar.DrawStaggerRow must paint the cached string and tokens (_labelStagger/_stagger). IL: $(Show $rowIl)"
    }
    # 1.2.25: the row is painted only for a LIVE target, so a lost target's row disappears with the target instead of
    # lingering through the bar's fade-out with the previous enemy's numbers.
    if ((Find-FieldIndices $rowIl 'HawChow.GTFOKillFeed.EnemyBar' '_haveTarget').Count -lt 1) {
        throw "EnemyBar.DrawStaggerRow must read _haveTarget and draw nothing once the target is gone. IL: $(Show $rowIl)"
    }
    # The row style itself must be MiddleLeft, not MiddleCenter. TextAnchor.MiddleLeft is ldc.i4.3.
    $styles = @($bar.Methods | Where-Object { $_.Name -eq 'EnsureStyles' -and $_.HasBody })
    if ($styles.Count -ne 1) { throw 'EnemyBar.EnsureStyles must exist exactly once with a body.' }
    $stylesIl = @($styles[0].Body.Instructions)
    $staggerStore = Find-FieldIndices $stylesIl 'HawChow.GTFOKillFeed.EnemyBar' '_staggerStyle'
    if ($staggerStore.Count -ne 1) { throw "EnemyBar.EnsureStyles must assign _staggerStyle exactly once. IL: $(Show $stylesIl)" }
    $styleWindow = @($stylesIl[[Math]::Max(0, $staggerStore[0] - 20)..$staggerStore[0]])
    $leftAlignment = $false
    for ($i = 0; $i + 1 -lt $styleWindow.Count; $i++) {
        if ($styleWindow[$i].OpCode.Name -eq 'ldc.i4.3' -and
            $styleWindow[$i + 1].Operand -is [Mono.Cecil.MethodReference] -and
            $styleWindow[$i + 1].Operand.Name -eq 'set_alignment') { $leftAlignment = $true }
    }
    if (-not $leftAlignment) {
        throw "EnemyBar.EnsureStyles must give _staggerStyle TextAnchor.MiddleLeft (ldc.i4.3 + set_alignment). IL: $(Show $styleWindow)"
    }
    for ($i = 0; $i -lt $stylesIl.Count; $i++) {
        if ($stylesIl[$i].OpCode.Name -eq 'ldc.i4.4') {
            throw "EnemyBar.EnsureStyles must not use TextAnchor.MiddleCenter (ldc.i4.4) anywhere. IL: $(Show $stylesIl)"
        }
    }

    # ---- Refresh: one ReadStagger + one StaggerLine per refresh pass, inside the 0.35 s block that already exists.
    $tick = @($bar.Methods | Where-Object { $_.Name -eq 'Tick' -and $_.HasBody })
    if ($tick.Count -ne 1) { throw 'EnemyBar.Tick must exist exactly once with a body.' }
    $tickIl = @($tick[0].Body.Instructions)
    if ((Find-CallIndices $tickIl 'HawChow.GTFOKillFeed.EnemyBar' 'ReadStagger').Count -ne 1) {
        throw "EnemyBar.Tick must refresh the stagger values through ReadStagger exactly once. IL: $(Show $tickIl)"
    }
    if ((Find-CallIndices $tickIl 'HawChow.GTFOKillFeed.BarPolicy' 'StaggerLine').Count -ne 1) {
        throw "EnemyBar.Tick must format the row through BarPolicy.StaggerLine exactly once. IL: $(Show $tickIl)"
    }

    # 1.2.25: the cached row belongs to ONE enemy. Tick records which enemy it just read and drops the row's text on a
    # switch, so the previous target's numbers can never survive into the new target or into a fade-out.
    function Count-FieldStore($instructions, [string]$declaringType, [string]$name) {
        $count = 0
        for ($i = 0; $i -lt $instructions.Count; $i++) {
            $opCode = $instructions[$i].OpCode.Name
            if ($opCode -ne 'stsfld' -and $opCode -ne 'stfld') { continue }
            $operand = $instructions[$i].Operand
            if ($operand -is [Mono.Cecil.FieldReference] -and $operand.Name -eq $name -and
                $null -ne $operand.DeclaringType -and $operand.DeclaringType.FullName -eq $declaringType) { $count++ }
        }
        return $count
    }
    if ((Count-FieldStore $tickIl 'HawChow.GTFOKillFeed.EnemyBar' '_staggerTargetId') -lt 1) {
        throw "EnemyBar.Tick must record the enemy the row describes in _staggerTargetId. IL: $(Show $tickIl)"
    }
    if ((Count-FieldStore $tickIl 'HawChow.GTFOKillFeed.EnemyBar' '_labelStagger') -lt 2) {
        throw "EnemyBar.Tick must clear _labelStagger on a target switch and then store the new row (2 stores). IL: $(Show $tickIl)"
    }

    # ---- ReadStagger: game facts only, no invented scaling or body-part guess ------------------------------------
    $read = @($bar.Methods | Where-Object { $_.Name -eq 'ReadStagger' -and $_.HasBody })
    if ($read.Count -ne 1) { throw 'EnemyBar.ReadStagger must exist exactly once with a body.' }
    $readIl = @($read[0].Body.Instructions)
    foreach ($required in @(
        @{ Declaring = 'Dam_EnemyDamageLimb'; Name = 'get_m_destruction' },
        @{ Declaring = 'CharacterDestruction.CD_CharacterDestructionCollider'; Name = 'HasDestruction' },
        @{ Declaring = 'CharacterDestruction.CD_CharacterDestructionCollider'; Name = 'get_m_bodyPart' },
        @{ Declaring = 'Dam_EnemyDamageLimb'; Name = 'get_IsDestroyed' },
        @{ Declaring = 'Dam_EnemyDamageLimb'; Name = 'get_m_healthMax' },
        @{ Declaring = 'Enemies.EnemyAgent'; Name = 'get_m_headLimb' },
        @{ Declaring = 'Enemies.EnemyAgent'; Name = 'get_EnemyBalancingData' },
        @{ Declaring = 'GameData.HealthData'; Name = 'get_DamageUntilHitreact' },
        @{ Declaring = 'GameData.HealthData'; Name = 'get_HitreactOnLimbDestruction' }
    )) {
        if ((Find-CallIndices $readIl $required.Declaring $required.Name).Count -lt 1) {
            throw "EnemyBar.ReadStagger must read $($required.Declaring).$($required.Name). IL: $(Show $readIl)"
        }
    }
    if ((Find-CallIndices $readIl 'CharacterDestruction.CD_CharacterDestructionCollider' 'get_m_bodyPart').Count -ne 1) {
        throw "EnemyBar.ReadStagger must read the anatomical m_bodyPart exactly once per limb. IL: $(Show $readIl)"
    }
    if ((Find-CallIndices $readIl 'HawChow.GTFOKillFeed.BarPolicy' 'BodyGroup').Count -ne 1) {
        throw "EnemyBar.ReadStagger must classify the part through BarPolicy.BodyGroup exactly once. IL: $(Show $readIl)"
    }
    if ((Find-CallIndices $readIl 'HawChow.GTFOKillFeed.BarPolicy/StaggerGroupBuilder' 'Add').Count -ne 2 -or
        (Find-CallIndices $readIl 'HawChow.GTFOKillFeed.BarPolicy/StaggerGroupBuilder' 'Build').Count -ne 2) {
        throw "EnemyBar.ReadStagger must aggregate the limb and torso groups through StaggerGroupBuilder.Add/Build (2 each). IL: $(Show $readIl)"
    }
    # eLimbDamageType only means Normal/Weakspot/Armor and is never a body part.
    if ((Find-CallIndices $readIl 'Dam_EnemyDamageLimb' 'get_m_type').Count -ne 0) {
        throw 'EnemyBar.ReadStagger must never classify a body part from Dam_EnemyDamageLimb.m_type (eLimbDamageType).'
    }
    # 1.2.25: the normal item is the game-proven THRESHOLD itself (HealthData.DamageUntilHitreact). The live accumulator
    # must NOT be read here any more: subtracting it would print a remaining amount, and it is still required - but only
    # by the real-hit green digits (1.2.24), which is a different question with its own read path.
    if ((Find-CallIndices $readIl 'Dam_EnemyDamageBase' 'get_m_damBuildToHitreact').Count -ne 0) {
        throw "EnemyBar.ReadStagger must not read Dam_EnemyDamageBase.m_damBuildToHitreact: the row shows the theoretical maximum, never a remaining amount. IL: $(Show $readIl)"
    }
    if ((Find-CallIndices $readIl 'Dam_EnemyDamageLimb' 'get_m_health').Count -ne 0) {
        throw "EnemyBar.ReadStagger must not read the CURRENT limb durability (m_health): only the fixed capacity is a threshold. IL: $(Show $readIl)"
    }
    if ((Find-CallIndices $readIl 'HawChow.GTFOKillFeed.BarPolicy' 'StaggerRemaining').Count -ne 0) {
        throw "EnemyBar.ReadStagger must not call a remaining/threshold-minus-accumulator helper. IL: $(Show $readIl)"
    }
    if ((Find-CallIndices $readIl 'HawChow.GTFOKillFeed.BarPolicy' 'IsUsableThreshold').Count -ne 1) {
        throw "EnemyBar.ReadStagger must accept the normal threshold through BarPolicy.IsUsableThreshold exactly once. IL: $(Show $readIl)"
    }
    if ((Find-CallIndices $readIl 'HawChow.GTFOKillFeed.BarPolicy' 'LimbBreakIsHitreact').Count -ne 1) {
        throw "EnemyBar.ReadStagger must gate the part items on the game's own limb-destruction hitreact switch through BarPolicy.LimbBreakIsHitreact exactly once. IL: $(Show $readIl)"
    }
    $removedGuess = @($barPolicy.Methods | Where-Object { $_.Name -eq 'NormalHitreactIsDamage' })
    if ($removedGuess.Count -ne 0) {
        throw 'BarPolicy.NormalHitreactIsDamage (the removed magnitude guess) must not exist any more.'
    }
    if ((Find-CallIndices $readIl 'GameData.HealthData' 'get_HealthMax').Count -ne 0) {
        throw 'EnemyBar.ReadStagger must not gate the normal threshold on HealthMax: the unit is proven by the native formula.'
    }
    if ((Find-FieldIndices $readIl 'HawChow.GTFOKillFeed.BarPolicy' 'CfoamImmuneTolerance').Count -ne 0) {
        throw 'EnemyBar.ReadStagger must never reuse the C-Foam immune sentinel for the stagger row.'
    }
    $partBudget = $bar.Fields | Where-Object { $_.Name -eq 'LogStaggerPartBudget' -and $_.FieldType.FullName -eq 'HawChow.GTFOKillFeed.DiagnosticBudget' }
    if ($null -eq $partBudget) {
        throw 'EnemyBar must keep a separate LogStaggerPartBudget so an unknown part cannot silence the normal-hitreact evidence.'
    }
    if ((Find-FieldIndices $readIl 'HawChow.GTFOKillFeed.EnemyBar' 'LogStaggerPartBudget').Count -lt 1) {
        throw "EnemyBar.ReadStagger must report an ungrouped/unknown part through its own bounded budget. IL: $(Show $readIl)"
    }

    # ---- Installed interop really defines the fields and values the classification uses ---------------------------
    $interop = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path -LiteralPath $modulesPath).Path)
    $interopModule = $interop.MainModule
    $partEnum = $interopModule.GetType('CharacterDestruction.CD_BodypartType')
    if ($null -eq $partEnum -or -not $partEnum.IsEnum) {
        throw 'Installed interop is missing the CharacterDestruction.CD_BodypartType enum.'
    }
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
    $expectedParts = [ordered]@{
        'Unspecified'   = 0
        'Head'          = 1
        'ArmUpperRight' = 2
        'ArmLowerRight' = 3
        'ArmUpperLeft'  = 4
        'ArmLowerLeft'  = 5
        'Chest'         = 6
        'Stomach'       = 7
        'Hips'          = 8
        'LegUpperRight' = 9
        'LegLowerRight' = 10
        'LegUpperLeft'  = 11
        'LegLowerLeft'  = 12
    }
    foreach ($name in $expectedParts.Keys) {
        $interopValue = Get-InteropEnumValue $partEnum $name
        if ($interopValue -ne $expectedParts[$name]) {
            throw "Interop CD_BodypartType.$name is $interopValue, expected $($expectedParts[$name])."
        }
        $pluginValue = Get-PluginConstInt 'HawChow.GTFOKillFeed.BarPolicy' ('Part' + $name)
        if ($pluginValue -ne $interopValue) {
            throw "BarPolicy.Part$name ($pluginValue) does not match the installed interop value ($interopValue)."
        }
    }
    foreach ($absentName in @('Back', 'Waist')) {
        if (@($partEnum.Fields | Where-Object { $_.Name -eq $absentName }).Count -ne 0) {
            throw "Interop CD_BodypartType unexpectedly has $absentName; the mapping must be reviewed before using it."
        }
    }
    $collider = $interopModule.GetType('CharacterDestruction.CD_CharacterDestructionCollider')
    if ($null -eq $collider) { throw 'Installed interop is missing CharacterDestruction.CD_CharacterDestructionCollider.' }
    $bodyPartProperty = $collider.Properties | Where-Object { $_.Name -eq 'm_bodyPart' }
    if ($null -eq $bodyPartProperty -or $bodyPartProperty.PropertyType.FullName -ne 'CharacterDestruction.CD_BodypartType') {
        throw 'CD_CharacterDestructionCollider.m_bodyPart must be the CharacterDestruction.CD_BodypartType property.'
    }
    $limb = $interopModule.GetType('Dam_EnemyDamageLimb')
    if ($null -eq $limb) { throw 'Installed interop is missing Dam_EnemyDamageLimb.' }
    foreach ($expected in @(
        @{ Name = 'm_destruction'; Type = 'CharacterDestruction.CD_CharacterDestructionCollider' },
        @{ Name = 'm_health'; Type = 'System.Single' },
        @{ Name = 'm_healthMax'; Type = 'System.Single' }
    )) {
        $property = $limb.Properties | Where-Object { $_.Name -eq $expected.Name }
        if ($null -eq $property -or $property.PropertyType.FullName -ne $expected.Type) {
            throw "Dam_EnemyDamageLimb.$($expected.Name) must be $($expected.Type) in the installed interop."
        }
    }
    $limbTypeEnum = $interopModule.GetType('eLimbDamageType')
    if ($null -eq $limbTypeEnum -or -not $limbTypeEnum.IsEnum) { throw 'Installed interop is missing eLimbDamageType.' }
    if ((Get-InteropEnumValue $limbTypeEnum 'Normal') -ne 0 -or
        (Get-InteropEnumValue $limbTypeEnum 'Weakspot') -ne 1 -or
        (Get-InteropEnumValue $limbTypeEnum 'Armor') -ne 2) {
        throw 'eLimbDamageType must stay Normal=0/Weakspot=1/Armor=2; it is not a body-part enum.'
    }
    # 1.2.25: the limb-destruction hitreact switch the part items are gated on, and the enum values it is compared against.
    $hitreactEnum = $interopModule.GetType('ES_HitreactType')
    if ($null -eq $hitreactEnum -or -not $hitreactEnum.IsEnum) { throw 'Installed interop is missing the ES_HitreactType enum.' }
    $expectedHitreacts = [ordered]@{
        'Unspecified'         = 0
        'None'                = 1
        'Micro'               = 2
        'Light'               = 3
        'Heavy'               = 4
        'ToDeath'             = 5
        'InstantRagdollDeath' = 6
    }
    foreach ($name in $expectedHitreacts.Keys) {
        $interopValue = Get-InteropEnumValue $hitreactEnum $name
        if ($interopValue -ne $expectedHitreacts[$name]) {
            throw "Interop ES_HitreactType.$name is $interopValue, expected $($expectedHitreacts[$name])."
        }
        $pluginValue = Get-PluginConstInt 'HawChow.GTFOKillFeed.BarPolicy' ('Hitreact' + $name)
        if ($pluginValue -ne $interopValue) {
            throw "BarPolicy.Hitreact$name ($pluginValue) does not match the installed interop value ($interopValue)."
        }
    }
    $healthData = $interopModule.GetType('GameData.HealthData')
    if ($null -eq $healthData) { throw 'Installed interop is missing GameData.HealthData.' }
    foreach ($expected in @(
        @{ Name = 'HitreactOnLimbDestruction'; Type = 'ES_HitreactType' },
        @{ Name = 'TryForceHitreactOnLimbDestruction'; Type = 'System.Boolean' },
        @{ Name = 'DamageUntilHitreact'; Type = 'System.Single' }
    )) {
        $property = $healthData.Properties | Where-Object { $_.Name -eq $expected.Name }
        if ($null -eq $property -or $property.PropertyType.FullName -ne $expected.Type) {
            throw "GameData.HealthData.$($expected.Name) must be $($expected.Type) in the installed interop."
        }
    }

    # ---- Selection and clone proof ------------------------------------------------------------------------------
    $feedback = $module.GetType('HawChow.GTFOKillFeed.CorpseFeedback')
    if ($null -eq $feedback) { throw 'Compiled plugin is missing HawChow.GTFOKillFeed.CorpseFeedback.' }
    $fadeRenderer = @($feedback.Methods | Where-Object { $_.Name -eq 'FadeRenderer' -and $_.HasBody })
    if ($fadeRenderer.Count -ne 1) { throw 'CorpseFeedback.FadeRenderer must exist exactly once with a body.' }
    $safeInFade = @($feedback.Methods | Where-Object { $_.HasBody -and $_.Name -like 'TrySafe*' })
    if ($safeInFade.Count -lt 1) { throw 'CorpseFeedback must keep its TrySafe* candidate helpers.' }
    $safeIl = @($safeInFade | ForEach-Object { $_.Body.Instructions })
    if ((Find-CallIndices $safeIl 'HawChow.GTFOKillFeed.CorpseFade' 'IsSafeInput').Count -lt 1) {
        throw 'Every corpse fade candidate must be classified through CorpseFade.IsSafeInput before it is offered.'
    }

    $corpseFade = $module.GetType('HawChow.GTFOKillFeed.CorpseFade')
    if ($null -eq $corpseFade) { throw 'Compiled plugin is missing HawChow.GTFOKillFeed.CorpseFade.' }
    $isSafe = @($corpseFade.Methods | Where-Object { $_.Name -eq 'IsSafeInput' -and $_.HasBody })
    if ($isSafe.Count -ne 1 -or (Find-CallIndices @($isSafe[0].Body.Instructions) 'HawChow.GTFOKillFeed.CorpseFadePolicy' 'IsHazardous').Count -ne 1) {
        throw 'CorpseFade.IsSafeInput must be exactly the policy classification (CorpseFadePolicy.IsHazardous).'
    }
    $cast = $module.GetType('HawChow.GTFOKillFeed.FadeRendererCast')
    if ($null -eq $cast) { throw 'Compiled plugin is missing HawChow.GTFOKillFeed.FadeRendererCast.' }
    $observe = @($cast.Methods | Where-Object { $_.Name -eq 'Observe' -and $_.HasBody })
    if ($observe.Count -ne 1) { throw 'FadeRendererCast.Observe must exist exactly once with a body.' }
    $observeIl = @($observe[0].Body.Instructions)
    if ((Find-CallIndices $observeIl 'UnityEngine.Transform' 'get_childCount').Count -lt 1 -or
        (Find-CallIndices $observeIl 'UnityEngine.Transform' 'GetChild').Count -lt 1) {
        throw "FadeRendererCast.Observe must scan the source's children (Transform.childCount + GetChild), not only the GameObject itself. IL: $(Show $observeIl)"
    }
    $castIl = @()
    foreach ($castMethod in $cast.Methods) { if ($castMethod.HasBody) { $castIl += @($castMethod.Body.Instructions) } }
    if ((Find-TypeMentions $castIl 'ShadowEnemyRenderer').Count -lt 1) {
        throw 'FadeRendererCast must classify the shadow-registration component (ShadowEnemyRenderer) in the Observe path.'
    }
    $scan = @($cast.Methods | Where-Object { $_.Name -eq 'ScanForRegistrations' -and $_.HasBody })
    if ($scan.Count -ne 1) { throw 'FadeRendererCast must keep exactly one bounded subtree scan (ScanForRegistrations).' }
    $scanIl = @($scan[0].Body.Instructions)
    if ((Find-CallIndices $scanIl 'UnityEngine.Transform' 'get_childCount').Count -lt 1 -or
        (Find-CallIndices $scanIl 'UnityEngine.Transform' 'GetChild').Count -lt 1 -or
        (Find-TypeMentions $scanIl 'ShadowEnemyRenderer').Count -lt 1) {
        throw "FadeRendererCast.ScanForRegistrations must walk Transform.childCount/GetChild and test ShadowEnemyRenderer, so an inactive child's registration is found too. IL: $(Show $scanIl)"
    }
    if ((Find-CallIndices $scanIl 'HawChow.GTFOKillFeed.CorpseFadePolicy' 'SubtreeVerdict').Count -lt 1) {
        throw "FadeRendererCast.ScanForRegistrations must decide depth/node budgets through CorpseFadePolicy.SubtreeVerdict. IL: $(Show $scanIl)"
    }
    if ((Find-CallIndices $observeIl 'HawChow.GTFOKillFeed.FadeRendererCast' 'ScanForRegistrations').Count -ne 1) {
        throw "FadeRendererCast.Observe must run the bounded scan exactly once. IL: $(Show $observeIl)"
    }
    $inputType = $module.GetType('HawChow.GTFOKillFeed.CorpseFadeInput')
    if ($null -eq $inputType) { throw 'Compiled plugin is missing HawChow.GTFOKillFeed.CorpseFadeInput.' }
    $truncatedField = @($inputType.Fields | Where-Object { $_.Name -eq 'Truncated' -and $_.FieldType.FullName -eq 'System.Boolean' })
    if ($truncatedField.Count -ne 1) { throw 'CorpseFadeInput must keep a single bool Truncated field: an unfinished scan is unproven, not safe.' }
    $hazardType = $module.GetType('HawChow.GTFOKillFeed.CorpseFadeHazard')
    if ($null -eq $hazardType) { throw 'Compiled plugin is missing HawChow.GTFOKillFeed.CorpseFadeHazard.' }
    if (@($hazardType.Fields | Where-Object { $_.Name -eq 'HierarchyUnproven' }).Count -ne 1) {
        throw 'CorpseFadeHazard must keep HierarchyUnproven so a truncated scan is a named refusal.'
    }
    $hazardMethod = @($module.GetType('HawChow.GTFOKillFeed.CorpseFadePolicy').Methods | Where-Object { $_.Name -eq 'Hazard' -and $_.HasBody })
    if ($hazardMethod.Count -ne 1) { throw 'CorpseFadePolicy.Hazard must exist exactly once with a body.' }
    $hazardIl = @($hazardMethod[0].Body.Instructions)
    if ((Find-FieldIndices $hazardIl 'HawChow.GTFOKillFeed.CorpseFadeInput' 'Truncated').Count -ne 1) {
        throw "CorpseFadePolicy.Hazard must consult CorpseFadeInput.Truncated exactly once. IL: $(Show $hazardIl)"
    }
    $tryNative = @($corpseFade.Methods | Where-Object { $_.Name -eq 'TryNative' -and $_.HasBody })
    if ($tryNative.Count -ne 1) { throw 'CorpseFade.TryNative must exist exactly once with a body.' }
    $nativeIl = @($tryNative[0].Body.Instructions)
    if ((Find-CallIndices $nativeIl 'Decay' 'get_m_decayRenderer').Count -ne 1) {
        throw "CorpseFade.TryNative must read Decay.m_decayRenderer exactly once: it proves what Play would start. IL: $(Show $nativeIl)"
    }
    if ((Find-CallIndices $nativeIl 'HawChow.GTFOKillFeed.FadeRendererCast' 'HasLiveCopy').Count -ne 1) {
        throw "CorpseFade.TryNative must refuse a dead existing copy through FadeRendererCast.HasLiveCopy exactly once. IL: $(Show $nativeIl)"
    }
    if ((Find-CallIndices $nativeIl 'Decay' 'Initialize').Count -ne 0) {
        throw 'CorpseFade.TryNative must never Initialize: that is the pooled arm, and it would build a second copy.'
    }

    $summary = "STAGGER_ROW_IL=PASS;DRAW_CALLS=$($drawRow.Count);ROW_PARAMS=$($row[0].Parameters.Count);" +
        'ANCHOR=name-first-glyph(TextOriginX)+bar-left;OWN_FONT=1;WRAP=StaggerRows(count-aware);CLAMP=StaggerRowLeft;' +
        'COLOR=BarPolicy.StaggerText*;FOREIGN_CONTENT=0;REFRESH=ReadStagger+StaggerLine;STALE=_staggerTargetId+_haveTarget;' +
        'PART=m_bodyPart+BodyGroup+m_healthMax(max-only)+HitreactOnLimbDestruction(Micro/Light/Heavy-only);' +
        'NORMAL=DamageUntilHitreact-only(no-accumulator);EMPTY_ITEM=omitted;NUMBER=roundtrip-G;SELECT=FadeRenderer->IsSafeInput;' +
        'OBSERVE=subtree(GetChild);TRY_NATIVE=m_decayRenderer+HasLiveCopy(no-Initialize)'
    Write-Host $summary
}
finally {
    if ($null -ne $interop) { $interop.Dispose() }
    $assembly.Dispose()
}
