[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PluginDll,
    [Parameter(Mandatory = $true)][string]$CecilPath
)
# Compiled-IL guard for the 1.2.19 pellet volley (cross-frame shotgun damage-number merge).
#
# The 1.2.17 real-machine log printed 12 pellet numbers (2.85-3.31) on 12 consecutive frames for ONE
# shotgun shot, so the merge scope that required `fireFrame == Time.frameCount` was released far too
# early. Source text cannot prove the shipped binary really stopped using the frame, so this asserts the
# emitted IL of the real assembly:
#   * the two new Fire entry points (Gear.BulletWeapon / Gear.BulletWeaponSynced) are patched, and their
#     prefix takes that exact instance type;
#   * FeedbackPolicy.IsShotgunFireScopeCurrent never reads Time.frameCount and its two window constants
#     are compile-time 0.5 s / 0.05 s;
#   * the only ShotgunDamageNumberBatch.Drain() call in the whole assembly is the one draw site in
#     ShotgunDamageNumberCapture.DrainVolley, with the only DamageNumbers.AddMergedShotgun call;
#   * the end conditions are driven from DamageNumbers.Tick -> ShotgunDamageNumberCapture.Tick ->
#     ShotgunVolleyState.ShouldEnd (no coroutine, no per-pellet scan);
#   * none of the volley types starts a coroutine, destroys anything or writes Time.timeScale.
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $PluginDll -PathType Leaf)) { throw "Plugin assembly is missing: $PluginDll" }
if (-not (Test-Path -LiteralPath $CecilPath -PathType Leaf)) { throw "Mono.Cecil is required to inspect compiled IL: $CecilPath" }
Add-Type -Path (Resolve-Path -LiteralPath $CecilPath).Path

function Show($instructions) {
    $all = @($instructions | ForEach-Object { $_.ToString() })
    $visible = @($all | Select-Object -First 24)
    $text = $visible -join ' | '
    if ($all.Count -gt $visible.Count) { $text += " | ... ($($all.Count) instructions)" }
    return $text
}

function Get-Type([string]$fullName) {
    $type = $module.GetType($fullName)
    if ($null -eq $type) { throw "Compiled plugin is missing $fullName." }
    return $type
}

function Get-Body($type, [string]$name) {
    $method = $type.Methods | Where-Object { $_.Name -eq $name } | Select-Object -First 1
    if ($null -eq $method -or -not $method.HasBody) { throw "Compiled plugin is missing the body of $($type.FullName)::$name." }
    return $method
}

function Find-Calls($instructions, [string]$declaringType, [string]$name) {
    $found = @()
    for ($i = 0; $i -lt $instructions.Count; $i++) {
        $operand = $instructions[$i].Operand
        if ($operand -is [Mono.Cecil.MethodReference] -and $operand.Name -eq $name -and
            $null -ne $operand.DeclaringType -and $operand.DeclaringType.FullName -eq $declaringType) { $found += $i }
    }
    return $found
}

# Which argument index does this instruction read? 0..3 are the short ldarg.0..3 opcodes; every later argument is
# emitted as `ldarg.s <parameter>` (Cecil keeps the ParameterDefinition as the operand), so the name is resolved
# against the method's own parameter list. -1 = "not an argument read", -2 = "an ldarg.s whose name is not a parameter".
function Get-ArgIndex($instruction, $parameters) {
    if ($null -eq $instruction) { return -1 }
    $name = $instruction.OpCode.Name
    if ($name -match '^ldarg\.([0-3])$') { return [int]$Matches[1] }
    if ($name -eq 'ldarg.s' -and $null -ne $instruction.Operand) {
        for ($i = 0; $i -lt $parameters.Count; $i++) {
            if ($parameters[$i].Name -eq $instruction.Operand.Name) { return $i }
        }
        return -2
    }
    return -1
}

# How many times the argument at `index` is read by the method.
function Count-ArgReads($instructions, $parameters, [int]$index) {
    $count = 0
    foreach ($instruction in $instructions) {
        if ((Get-ArgIndex $instruction $parameters) -eq $index) { $count++ }
    }
    return $count
}

# How many branches are GATED on the argument at `index`: a brtrue/brfalse whose previous instruction is that
# argument's read. That is the only shape the category/identity arms can take inside this leaf predicate.
function Count-ArgBranches($instructions, $parameters, [int]$index) {
    $count = 0
    for ($i = 1; $i -lt $instructions.Count; $i++) {
        if ($instructions[$i].OpCode.Name -notmatch '^br(false|true)(\.s)?$') { continue }
        if ((Get-ArgIndex $instructions[$i - 1] $parameters) -eq $index) { $count++ }
    }
    return $count
}

# Index of the first branch gated on the argument at `index`, -1 when there is none.
function Find-ArgBranch($instructions, $parameters, [int]$index) {
    for ($i = 1; $i -lt $instructions.Count; $i++) {
        if ($instructions[$i].OpCode.Name -notmatch '^br(false|true)(\.s)?$') { continue }
        if ((Get-ArgIndex $instructions[$i - 1] $parameters) -eq $index) { return $i }
    }
    return -1
}

$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path -LiteralPath $PluginDll).Path)
try {
    $module = $assembly.MainModule

    # ---------------------------------------------------------------- 1) the four Fire entry points
    $capture = Get-Type 'HawChow.GTFOKillFeed.ShotgunDamageNumberCapture'
    $targets = [ordered]@{
        'HawChow.GTFOKillFeed.ShotgunFireDamageNumberPatch'            = 'Gear.Shotgun'
        'HawChow.GTFOKillFeed.ShotgunSyncedFireDamageNumberPatch'      = 'Gear.ShotgunSynced'
        'HawChow.GTFOKillFeed.BulletWeaponFireDamageNumberPatch'       = 'Gear.BulletWeapon'
        'HawChow.GTFOKillFeed.BulletWeaponSyncedFireDamageNumberPatch' = 'Gear.BulletWeaponSynced'
    }
    foreach ($patchName in $targets.Keys) {
        $patch = Get-Type $patchName
        $expected = $targets[$patchName]
        $attribute = @($patch.CustomAttributes | Where-Object { $_.AttributeType.FullName -eq 'HarmonyLib.HarmonyPatch' })
        if ($attribute.Count -ne 1) { throw "$patchName must carry exactly one [HarmonyPatch] attribute; found $($attribute.Count)." }
        $patchTarget = $null
        $patchMethod = $null
        foreach ($argument in $attribute[0].ConstructorArguments) {
            if ($argument.Value -is [Mono.Cecil.TypeReference]) { $patchTarget = $argument.Value.FullName }
            if ($argument.Value -is [string]) { $patchMethod = $argument.Value }
        }
        if ($patchTarget -ne $expected -or $patchMethod -ne 'Fire') {
            throw "$patchName must patch $expected::Fire(bool); found target='$patchTarget' method='$patchMethod'."
        }
        $prefix = Get-Body $patch 'Prefix'
        if ($prefix.Parameters.Count -lt 1 -or $prefix.Parameters[0].ParameterType.FullName -ne $expected) {
            throw "$patchName.Prefix must take the fired weapon instance ($expected) as its first parameter."
        }
    }

    # ---------------------------------------------------------------- 1b) the fire identity gate (fix A)
    # 1.2.19 real-machine: the pellet's own gear category is 0, so FeedbackPolicy.IsVolleyHit wildcards it and the
    # category can no longer separate "this shotgun" from "another weapon". The identity gate does: every one of the
    # four Fire prefixes reaches SettleForeignVolley, which settles the open volley when a DIFFERENT weapon instance
    # fires. Without this the wildcard would swallow a rifle/melee hit into the shotgun batch.
    foreach ($patchName in $targets.Keys) {
        $patch = Get-Type $patchName
        $prefixIl = @((Get-Body $patch 'Prefix').Body.Instructions)
        $entryCalls = (Find-Calls $prefixIl 'HawChow.GTFOKillFeed.ShotgunDamageNumberCapture' 'BeginFire').Count +
            (Find-Calls $prefixIl 'HawChow.GTFOKillFeed.ShotgunDamageNumberCapture' 'BeginMultiPelletFire').Count
        if ($entryCalls -ne 1) {
            throw "$patchName.Prefix must enter the volley capture exactly once (BeginFire for Shotgun/ShotgunSynced, BeginMultiPelletFire for BulletWeapon/BulletWeaponSynced); found $entryCalls. IL: $(Show $prefixIl)"
        }
    }
    $beginFireEntryIl = @((Get-Body $capture 'BeginFire').Body.Instructions)
    $multiEntryIl = @((Get-Body $capture 'BeginMultiPelletFire').Body.Instructions)
    if ((Find-Calls $beginFireEntryIl 'HawChow.GTFOKillFeed.ShotgunVolleyState' 'BeginShot').Count -ne 1 -or
        (Find-Calls $multiEntryIl 'HawChow.GTFOKillFeed.ShotgunDamageNumberCapture' 'BeginFire').Count -ne 1 -or
        (Find-Calls $multiEntryIl 'HawChow.GTFOKillFeed.ShotgunVolleyState' 'BeginShot').Count -ne 0) {
        throw 'Both Fire entry paths must enter the ONE shared Fire order: BeginFire calls ShotgunVolleyState.BeginShot once and BeginMultiPelletFire delegates to BeginFire (with the > 1 pellet gate), so all four Fire prefixes are covered and no second copy of the order can appear.'
    }
    $beginFireIl = @((Get-Body $capture 'BeginFire').Body.Instructions)
    if ((Find-Calls $beginFireIl 'HawChow.GTFOKillFeed.ShotgunDamageNumberCapture' 'DrawSettled').Count -ne 1 -or
        (Find-Calls $beginFireIl 'HawChow.GTFOKillFeed.ShotgunVolleyState' 'Adopt').Count -ne 0 -or
        (Find-Calls $beginFireIl 'HawChow.GTFOKillFeed.ShotgunDamageNumberCapture' 'LogVolley').Count -ne 1) {
        throw "BeginFire must ask BeginShot, draw what it settled and adopt nothing itself (the order lives in the state). IL: $(Show $beginFireIl)"
    }
    # 1.2.19 review R3: the order itself - settle a foreign volley BEFORE the same-shot check, then settle the previous
    # volley - is production code inside BeginShot and is asserted here.
    $beginShotIl = @((Get-Body (Get-Type 'HawChow.GTFOKillFeed.ShotgunVolleyState') 'BeginShot').Body.Instructions)
    $openAt = (Find-Calls $beginShotIl 'HawChow.GTFOKillFeed.ShotgunVolleyState' 'CanOpenVolley')
    $foreignAt = (Find-Calls $beginShotIl 'HawChow.GTFOKillFeed.ShotgunVolleyState' 'IsForeignShot')
    $sameAt = (Find-Calls $beginShotIl 'HawChow.GTFOKillFeed.ShotgunVolleyState' 'IsSameShot')
    $endAt = (Find-Calls $beginShotIl 'HawChow.GTFOKillFeed.ShotgunVolleyState' 'End')
    if ($openAt.Count -ne 1 -or $foreignAt.Count -ne 1 -or $sameAt.Count -ne 1 -or $endAt.Count -lt 1 -or
        $openAt[0] -gt $foreignAt[0] -or $foreignAt[0] -gt $sameAt[0]) {
        throw "BeginShot must decide in the production order: attributability (CanOpenVolley) -> foreign (IsForeignShot) -> same-shot (IsSameShot) -> settle (End). IL: $(Show $beginShotIl)"
    }
    $foreignShotIl = @((Get-Body (Get-Type 'HawChow.GTFOKillFeed.ShotgunVolleyState') 'IsForeignShot').Body.Instructions)
    if ((Find-Calls $foreignShotIl 'HawChow.GTFOKillFeed.FeedbackPolicy' 'IsForeignVolleyOwner').Count -ne 1) {
        throw "ShotgunVolleyState.IsForeignShot must decide the identity through FeedbackPolicy.IsForeignVolleyOwner. IL: $(Show $foreignShotIl)"
    }

    # ---------------------------------------------------------------- 2) the window is not the frame
    $policy = Get-Type 'HawChow.GTFOKillFeed.FeedbackPolicy'
    $scopeMethod = @($policy.Methods | Where-Object { $_.Name -eq 'IsShotgunFireScopeCurrent' })
    if ($scopeMethod.Count -ne 1) { throw "FeedbackPolicy must keep exactly one IsShotgunFireScopeCurrent overload; found $($scopeMethod.Count)." }
    $scopeParams = @($scopeMethod[0].Parameters | ForEach-Object { $_.ParameterType.FullName })
    if (($scopeParams -join ',') -ne 'System.Single,System.Single,System.Boolean') {
        throw "FeedbackPolicy.IsShotgunFireScopeCurrent must take (float volleyStartTime, float now, bool drained); found ($($scopeParams -join ','))."
    }
    $scopeIl = @($scopeMethod[0].Body.Instructions)
    if ((Find-Calls $scopeIl 'UnityEngine.Time' 'get_frameCount').Count -gt 0) {
        throw "FeedbackPolicy.IsShotgunFireScopeCurrent must not read Time.frameCount: the volley is time-boxed, not frame-boxed. IL: $(Show $scopeIl)"
    }
    foreach ($constant in @(@{ Name = 'MaxVolleySeconds'; Value = 0.5 }, @{ Name = 'VolleySilenceSeconds'; Value = 0.05 })) {
        $field = $policy.Fields | Where-Object { $_.Name -eq $constant.Name }
        if ($null -eq $field -or -not $field.HasConstant -or [math]::Abs([single]$field.Constant - [single]$constant.Value) -gt 0.000001) {
            throw "FeedbackPolicy.$($constant.Name) must stay a compile-time constant $($constant.Value) so the smoke suite and the IL share one number."
        }
    }
    # F3 (1.2.19 review): the declared pellet count must not even be a parameter - ending on it split one shot.
    # 1.2.19 fix D2: the end decision now also takes the number of CONSECUTIVE quiet end checks, so a single long frame
    # (every frame is longer than 50 ms below 20 fps) can no longer split one shot across its own pellet stream.
    $drain = @($policy.Methods | Where-Object { $_.Name -eq 'ShouldDrainVolley' })
    $drainParams = if ($drain.Count -eq 1) { @($drain[0].Parameters | ForEach-Object { $_.ParameterType.FullName }) } else { @() }
    if ($drain.Count -ne 1 -or ($drainParams -join ',') -ne 'System.Int32,System.Single,System.Single,System.Single,System.Boolean,System.Int32') {
        throw "FeedbackPolicy.ShouldDrainVolley must be the single SIX-argument end-of-volley decision (pellets, startTime, lastPelletAt, now, drained, quietTicks); the declared pellet count is diagnostic only (F3), and the silence has to hold for two consecutive end checks (1.2.19 fix D2). Found ($($drainParams -join ','))."
    }
    $quietField = $policy.Fields | Where-Object { $_.Name -eq 'VolleyQuietTicks' }
    if ($null -eq $quietField -or -not $quietField.HasConstant -or [int]$quietField.Constant -ne 2) {
        throw 'FeedbackPolicy.VolleyQuietTicks must stay the compile-time constant 2 (two consecutive quiet end checks).'
    }
    $drainIl = @($drain[0].Body.Instructions)
    if ((Find-Calls $drainIl 'UnityEngine.Time' 'get_frameCount').Count -gt 0) {
        throw "FeedbackPolicy.ShouldDrainVolley must decide on time, never on a frame number. IL: $(Show $drainIl)"
    }
    if ((Find-Calls $drainIl 'HawChow.GTFOKillFeed.FeedbackPolicy' 'VolleySilenceSeconds').Count -gt 0) {
        throw 'VolleySilenceSeconds is a literal: ShouldDrainVolley must not depend on a field read for it.'
    }
    # F1 (1.2.19 review) + 1.2.19 Survival retest: the hit membership predicate is the ONE place that decides
    # "is this hit a pellet of this shot". It takes the eight facts the production callers can prove:
    #   (bool windowOpen, uint volleyCategoryId, uint learnedPelletCategoryId, uint hitCategoryId,
    #    DamageOrigin hitOrigin, bool nestedSameShot, ulong volleyShooterLookup, ulong hitShooterLookup)
    # The learned pellet category is the HIT-side category this one volley already took; the weapon's
    # GearCategoryData.persistentID is a different id space (the Survival log printed 6/16 on the volley line and 2
    # on every [FEEDBACK] hitCat of the same shots), so the two are never compared for equality.
    $volleyHit = @($policy.Methods | Where-Object { $_.Name -eq 'IsVolleyHit' })
    if ($volleyHit.Count -ne 1) { throw 'FeedbackPolicy.IsVolleyHit must exist exactly once (F1).' }
    $volleyHitParams = @($volleyHit[0].Parameters | ForEach-Object { $_.ParameterType.FullName })
    if (($volleyHitParams -join ',') -ne 'System.Boolean,System.UInt32,System.UInt32,System.UInt32,HawChow.GTFOKillFeed.DamageOrigin,System.Boolean,System.UInt64,System.UInt64') {
        throw "FeedbackPolicy.IsVolleyHit must take (bool windowOpen, uint volleyCategoryId, uint learnedPelletCategoryId, uint hitCategoryId, DamageOrigin hitOrigin, bool nestedSameShot, ulong volleyShooterLookup, ulong hitShooterLookup); found ($($volleyHitParams -join ','))."
    }
    $volleyHitIl = @($volleyHit[0].Body.Instructions)
    if ((Find-Calls $volleyHitIl 'UnityEngine.Time' 'get_frameCount').Count -gt 0) {
        throw 'FeedbackPolicy.IsVolleyHit must not consult a frame number.'
    }
    # 1.2.19 path proof: the predicate must read the hit category argument (slot 4) AND the DamageOrigin argument
    # (slot 5, emitted as `ldarg.s hitOrigin` - there is no ldarg.4 opcode, so slot 5 is the `ldarg.4` argument), and
    # compare the origin against the Bullet member (a compile-time 1), so "window + category" alone can never accept a
    # hit again.
    if ((Count-ArgReads $volleyHitIl $volleyHit[0].Parameters 3) -lt 1 -or
        (Count-ArgReads $volleyHitIl $volleyHit[0].Parameters 4) -lt 1 -or
        @($volleyHitIl | Where-Object { $_.OpCode.Name -eq 'ldc.i4.1' }).Count -lt 1) {
        throw "FeedbackPolicy.IsVolleyHit must read the hit category argument and the DamageOrigin argument (ldarg.s hitOrigin, the 4th argument slot) and require DamageOrigin.Bullet (1.2.19 path proof). IL: $(Show $volleyHitIl)"
    }
    # 1.2.19 Survival retest: the wildcard set is FOUR arms and each is gated on its OWN argument. The guard is
    # strictly stronger than the previous "exactly one wildcard" count, because the nested proof is now gated by the
    # learned==0 read as well:
    #   * nestedSameShot && learnedPelletCategoryId == 0  -> ONE brfalse on the nested argument (5), immediately
    #     followed by the learned==0 read (2) and its branch. The nested proof therefore cannot be granted once this
    #     volley has learned a pellet category, so a nested hit of another KNOWN weapon is never merged into the batch;
    #   * hitCategoryId == 0                              -> ONE brfalse on the hit argument (4);
    #   * hitCategoryId == volleyCategoryId               -> ONE beq (not a branch) on argument 1;
    #   * learnedPelletCategoryId != 0 && hitCategoryId == learnedPelletCategoryId
    #                                                     -> ONE brfalse on the learned argument (2) + ONE ceq.
    # The unknown VOLLEY category branch must stay gone (no branch on argument 1: a melee/explosion/DoT/EWC hit never
    # passes a Fire prefix, so the identity gate is never asked for it and that wildcard is what let it into the batch).
    $hitWildcards = Count-ArgBranches $volleyHitIl $volleyHit[0].Parameters 3
    $volleyWildcards = Count-ArgBranches $volleyHitIl $volleyHit[0].Parameters 1
    $nestedWildcards = Count-ArgBranches $volleyHitIl $volleyHit[0].Parameters 5
    $learnedWildcards = Count-ArgBranches $volleyHitIl $volleyHit[0].Parameters 2
    $hitEqualsVolley = 0
    $hitEqualsLearned = 0
    for ($i = 1; $i -lt $volleyHitIl.Count; $i++) {
        if ($volleyHitIl[$i].OpCode.Name -match '^beq(\.s)?$' -and (Get-ArgIndex $volleyHitIl[$i - 1] $volleyHit[0].Parameters) -eq 1) { $hitEqualsVolley++ }
        if ($volleyHitIl[$i].OpCode.Name -eq 'ceq' -and (Get-ArgIndex $volleyHitIl[$i - 1] $volleyHit[0].Parameters) -eq 2) { $hitEqualsLearned++ }
    }
    if ($hitWildcards -ne 1 -or $volleyWildcards -ne 0 -or $nestedWildcards -ne 1 -or $learnedWildcards -ne 2 -or
        $hitEqualsVolley -ne 1 -or $hitEqualsLearned -ne 1) {
        throw "FeedbackPolicy.IsVolleyHit must keep the FOUR documented arms (nested+learned==0, unknown hit, hit==volley, learned!=0 and hit==learned) and no unknown-volley-category wildcard: found hit=$hitWildcards volley=$volleyWildcards nested=$nestedWildcards learned=$learnedWildcards hitEqualsVolley=$hitEqualsVolley hitEqualsLearned=$hitEqualsLearned (1.2.19 Survival retest). IL: $(Show $volleyHitIl)"
    }
    # The nested arm must be structurally gated by the learned==0 read: the branch on the nested argument is followed
    # by a read of the learned argument and then by a branch on it. If the nested proof came first, a nested hit of
    # another known weapon would be merged even though the volley already proved its own pellet category.
    $nestedBranch = Find-ArgBranch $volleyHitIl $volleyHit[0].Parameters 5
    if ($nestedBranch -lt 0 -or ($nestedBranch + 2) -ge $volleyHitIl.Count -or
        (Get-ArgIndex $volleyHitIl[$nestedBranch + 1] $volleyHit[0].Parameters) -ne 2 -or
        $volleyHitIl[$nestedBranch + 2].OpCode.Name -notmatch '^br(false|true)(\.s)?$') {
        throw "FeedbackPolicy.IsVolleyHit must gate the same-shot proof on learnedPelletCategoryId == 0 (the nested branch is immediately followed by the learned==0 read and branch), so a nested hit of a known foreign weapon is never merged (1.2.19 Survival retest). IL: $(Show $volleyHitIl)"
    }
    # The shooter arguments take part in the decision: two KNOWN and different shooters are two different shots, and
    # an unreadable identity on either side must prove nothing (both are read, neither is guessed).
    if ((Count-ArgReads $volleyHitIl $volleyHit[0].Parameters 6) -lt 2 -or
        (Count-ArgReads $volleyHitIl $volleyHit[0].Parameters 7) -lt 2) {
        throw "FeedbackPolicy.IsVolleyHit must read BOTH shooter identities (volleyShooterLookup and hitShooterLookup) and refuse only two KNOWN and different ones (1.2.19 Survival retest). IL: $(Show $volleyHitIl)"
    }
    # Every call site must pass the eight arguments: a leftover 4-argument call would silently change the meaning.
    $wrongVolleyHitArity = @()
    foreach ($arityType in $module.Types) {
        foreach ($arityMethod in $arityType.Methods) {
            if (-not $arityMethod.HasBody) { continue }
            foreach ($arityInstruction in $arityMethod.Body.Instructions) {
                $arityOperand = $arityInstruction.Operand
                if ($arityOperand -is [Mono.Cecil.MethodReference] -and $arityOperand.Name -eq 'IsVolleyHit' -and
                    $null -ne $arityOperand.DeclaringType -and $arityOperand.DeclaringType.FullName -eq 'HawChow.GTFOKillFeed.FeedbackPolicy' -and
                    $arityOperand.Parameters.Count -ne 8) {
                    $wrongVolleyHitArity += ($arityType.FullName + '::' + $arityMethod.Name + ' (' + $arityOperand.Parameters.Count + ')')
                }
            }
        }
    }
    if ($wrongVolleyHitArity.Count -gt 0) {
        throw "FeedbackPolicy.IsVolleyHit must be called with its eight arguments everywhere (window, volley category, learned pellet category, hit category, hit origin, nested, volley shooter, hit shooter); found: $($wrongVolleyHitArity -join ', ')"
    }
    if ((Find-Calls $volleyHitIl 'HawChow.GTFOKillFeed.FeedbackPolicy' 'IsVolleyHit').Count -ne 0) {
        throw 'FeedbackPolicy.IsVolleyHit must stay a leaf predicate (no recursive call).'
    }
    # R1: the capture window is the volley window narrowed by MaxPelletCaptureLag - a compiled constant, not a literal
    # repeated at the call sites.
    $lagField = $policy.Fields | Where-Object { $_.Name -eq 'MaxPelletCaptureLag' }
    if ($null -eq $lagField -or -not $lagField.HasConstant -or [math]::Abs([single]$lagField.Constant - 0.30) -gt 0.0001) {
        throw 'FeedbackPolicy.MaxPelletCaptureLag must stay a compile-time constant 0.30 s so the smoke suite, the source gate and the IL share one number (2nd review R1).'
    }
    $captureWindow = @($policy.Methods | Where-Object { $_.Name -eq 'IsPelletCaptureWindow' })
    if ($captureWindow.Count -ne 1) { throw 'FeedbackPolicy.IsPelletCaptureWindow must exist exactly once (2nd review R1).' }
    $captureWindowIl = @($captureWindow[0].Body.Instructions)
    if ((Find-Calls $captureWindowIl 'HawChow.GTFOKillFeed.FeedbackPolicy' 'IsShotgunFireScopeCurrent').Count -ne 1 -or
        (Find-Calls $captureWindowIl 'HawChow.GTFOKillFeed.FeedbackPolicy' 'MaxPelletCaptureLag').Count -gt 0 -or
        @($captureWindowIl | Where-Object { $_.OpCode.Name -eq 'ldc.r4' }).Count -lt 1) {
        throw "FeedbackPolicy.IsPelletCaptureWindow must be the volley window AND the pinned lag (inlined constant), so a single change cannot desync them. IL: $(Show $captureWindowIl)"
    }
    # 1.2.19 fix D1: the ONE pellet decision moved into ShotgunVolleyState.Capture (TryCapture is its bool wrapper),
    # and it is the only place that asks the capture window. The Unity adapter must not carry a second window of its own.
    $state = Get-Type 'HawChow.GTFOKillFeed.ShotgunVolleyState'
    foreach ($stateMember in @('Capture', 'IsWindowOpen')) {
        $memberIl = @((Get-Body $state $stateMember).Body.Instructions)
        if ((Find-Calls $memberIl 'HawChow.GTFOKillFeed.FeedbackPolicy' 'IsPelletCaptureWindow').Count -ne 1 -or
            (Find-Calls $memberIl 'HawChow.GTFOKillFeed.FeedbackPolicy' 'IsShotgunFireScopeCurrent').Count -ne 0) {
            throw "ShotgunVolleyState.$stateMember must ask FeedbackPolicy.IsPelletCaptureWindow (never the raw 0.5 s volley window): a late 0-category hit has to keep its own number (2nd review R1 + 1.2.19 fix D1). IL: $(Show $memberIl)"
        }
    }
    # 1.2.19 review R3/D1: the adapter is gone; the receipt reporter must not decide anything, and the route itself is
    # asserted on DamageNumberRouter.Decide (see the classifier gate below).
    $adapterReport = @($capture.Methods | Where-Object { $_.Name -eq 'ReportRefusedPellet' })
    if ($adapterReport.Count -ne 1) { throw 'ShotgunDamageNumberCapture.ReportRefusedPellet must exist exactly once (review R3).' }
    $adapterReportIl = @($adapterReport[0].Body.Instructions)
    if ((Find-Calls $adapterReportIl 'HawChow.GTFOKillFeed.FeedbackPolicy' 'IsShotgunFireScopeCurrent').Count -ne 0 -or
        (Find-Calls $adapterReportIl 'HawChow.GTFOKillFeed.FeedbackPolicy' 'IsVolleyHit').Count -ne 0 -or
        (Find-Calls $adapterReportIl 'HawChow.GTFOKillFeed.ShotgunVolleyState' 'Capture').Count -ne 0) {
        throw "ShotgunDamageNumberCapture.ReportRefusedPellet must only report: no window/category predicate and no volley capture of its own (1.2.19 fix D1). IL: $(Show $adapterReportIl)"
    }
    if ((Find-Calls $volleyHitIl 'HawChow.GTFOKillFeed.FeedbackPolicy' 'IsVolleyHit').Count -ne 0) {
        throw 'FeedbackPolicy.IsVolleyHit must stay a leaf predicate (no recursive call).'
    }
    $foreignOwner = @($policy.Methods | Where-Object { $_.Name -eq 'IsForeignVolleyOwner' })
    if ($foreignOwner.Count -ne 1) { throw 'FeedbackPolicy.IsForeignVolleyOwner must exist exactly once (fix A).' }
    if ((@($foreignOwner[0].Parameters | ForEach-Object { $_.ParameterType.FullName }) -join ',') -ne 'System.Int64,System.Int64') {
        throw 'FeedbackPolicy.IsForeignVolleyOwner must take the two weapon instance handles (Int64 owner, Int64 fire).'
    }
    $foreignOwnerIl = @($foreignOwner[0].Body.Instructions)
    if ((@($foreignOwnerIl | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] })).Count -ne 0) {
        throw "FeedbackPolicy.IsForeignVolleyOwner must stay a pure pointer comparison (it may call nothing). IL: $(Show $foreignOwnerIl)"
    }
    $state = Get-Type 'HawChow.GTFOKillFeed.ShotgunVolleyState'
    # 1.2.19 fix D1: the decision lives in Capture (TryCapture is its bool wrapper), so the signature and the single
    # IsVolleyHit call are asserted there; the wrapper must not decide anything itself.
    # 1.2.19 Survival retest: Capture and IsWindowOpen now also take the shooter identity and the same-shot proof, so
    # both callers hand the SAME facts to the ONE predicate - the classifier can never be stricter than the volley.
    # 1.2.24: Capture also carries the per-pellet stagger amount (`staggerDamage`), so a merged volley can keep the
    # triggering subset apart from the rest of the damage instead of guessing it from the merged total.
    foreach ($member in @(@{ Name = 'Capture'; Params = 'System.Int64,System.Single,System.Single,System.Single,System.Single,HawChow.GTFOKillFeed.HitKind,System.UInt32,HawChow.GTFOKillFeed.DamageOrigin,System.UInt64,System.Boolean,System.Single,System.Int32,System.Single' },
            @{ Name = 'IsWindowOpen'; Params = 'System.Single,System.UInt32,HawChow.GTFOKillFeed.DamageOrigin,System.UInt64,System.Boolean' })) {
        $method = @($state.Methods | Where-Object { $_.Name -eq $member.Name })
        if ($method.Count -ne 1) { throw "ShotgunVolleyState.$($member.Name) must exist exactly once (F1)." }
        $signature = (@($method[0].Parameters | ForEach-Object { $_.ParameterType.FullName }) -join ',')
        if ($signature -ne $member.Params) {
            throw "ShotgunVolleyState.$($member.Name) must take ($($member.Params)) so every hit carries its gear category, its origin, the shooter identity and the same-shot proof; found ($signature) (F1 + 1.2.19 Survival retest)."
        }
        if ((Find-Calls @($method[0].Body.Instructions) 'HawChow.GTFOKillFeed.FeedbackPolicy' 'IsVolleyHit').Count -ne 1) {
            throw "ShotgunVolleyState.$($member.Name) must decide membership through FeedbackPolicy.IsVolleyHit exactly once (F1)."
        }
    }
    $boolWrapper = @($state.Methods | Where-Object { $_.Name -eq 'TryCapture' })
    if ($boolWrapper.Count -ne 1 -or
        (@($boolWrapper[0].Parameters | ForEach-Object { $_.ParameterType.FullName }) -join ',') -ne 'System.Int64,System.Single,System.Single,System.Single,System.Single,HawChow.GTFOKillFeed.HitKind,System.UInt32,HawChow.GTFOKillFeed.DamageOrigin,System.Single,System.UInt64,System.Boolean,System.Single') {
        throw 'ShotgunVolleyState.TryCapture must stay the bool wrapper over Capture and forward the same facts (identity, position, damage, kind, category, origin, time, shooter, same-shot); found a different signature (F1 + 1.2.19 Survival retest).'
    }
    if ((Find-Calls @($boolWrapper[0].Body.Instructions) 'HawChow.GTFOKillFeed.FeedbackPolicy' 'IsVolleyHit').Count -ne 0 -or
        (Find-Calls @($boolWrapper[0].Body.Instructions) 'HawChow.GTFOKillFeed.ShotgunVolleyState' 'Capture').Count -ne 1) {
        throw 'ShotgunVolleyState.TryCapture must only forward to Capture (no second membership decision, 1.2.19 fix D1).'
    }
    # 1.2.19 Survival retest: the learned pellet category lives and dies with ONE volley. It is a mutable instance field
    # of ShotgunDamageNumberScope, never a static field of the state or the capture, and it is never handed to the global
    # ShotgunCategoryRegistry: "this shot's pellets are 2" must not become "2 is a shotgun category for the process"
    # (that would also hide another weapon's numbers while ShowShotgunDamageNumbers is off).
    $scopeType = Get-Type 'HawChow.GTFOKillFeed.ShotgunDamageNumberScope'
    $pelletFields = @($scopeType.Fields | Where-Object { $_.Name -eq 'PelletCategoryId' })
    if ($pelletFields.Count -ne 1 -or $pelletFields[0].FieldType.FullName -ne 'System.UInt32' -or $pelletFields[0].IsStatic) {
        throw "ShotgunDamageNumberScope.PelletCategoryId must be the ONE mutable uint instance field that remembers the learned pellet category (not static, so it cannot outlive its volley); found $($pelletFields.Count) (1.2.19 Survival retest)."
    }
    foreach ($learnedType in @($state, $capture)) {
        if (@($learnedType.Fields | Where-Object { $_.Name -eq 'PelletCategoryId' }).Count -ne 0) {
            throw "ShotgunVolleyState/ShotgunDamageNumberCapture must not carry a learned pellet category field: the learned category lives on the per-volley scope only ($($learnedType.FullName))."
        }
    }
    # The registry must be fed by exactly ONE place: RegisterCategory, called from BeginFire with the weapon's own
    # GearCategoryData.persistentID. A hit-side category (the learned 2) must never reach it.
    $registerSites = @()
    $registerCategorySites = @()
    foreach ($registerType in $module.Types) {
        foreach ($registerMethod in $registerType.Methods) {
            if (-not $registerMethod.HasBody) { continue }
            foreach ($registerInstruction in $registerMethod.Body.Instructions) {
                $registerOperand = $registerInstruction.Operand
                if (-not ($registerOperand -is [Mono.Cecil.MethodReference])) { continue }
                if ($null -eq $registerOperand.DeclaringType) { continue }
                if ($registerOperand.DeclaringType.FullName -eq 'HawChow.GTFOKillFeed.ShotgunCategoryRegistry' -and $registerOperand.Name -eq 'Register') {
                    $registerSites += ($registerType.FullName + '::' + $registerMethod.Name)
                }
                if ($registerOperand.DeclaringType.FullName -eq 'HawChow.GTFOKillFeed.ShotgunDamageNumberCapture' -and $registerOperand.Name -eq 'RegisterCategory') {
                    $registerCategorySites += ($registerType.FullName + '::' + $registerMethod.Name)
                }
            }
        }
    }
    if ($registerSites.Count -ne 1 -or $registerSites[0] -ne 'HawChow.GTFOKillFeed.ShotgunDamageNumberCapture::RegisterCategory' -or
        $registerCategorySites.Count -ne 1 -or $registerCategorySites[0] -ne 'HawChow.GTFOKillFeed.ShotgunDamageNumberCapture::BeginFire') {
        throw "ShotgunCategoryRegistry.Register may only be called from RegisterCategory, and RegisterCategory only from BeginFire (the weapon's own GearCategoryData.persistentID): a hit-side learned category must never become a global shotgun category. Found Register=[$($registerSites -join ', ')] RegisterCategory=[$($registerCategorySites -join ', ')]."
    }
    # The capture must read the learned category and the shooter from the volley's own scope, never from a global or
    # from another volley: that is what keeps the learned value per owner and per shot.
    $captureDecisionIl = @((Get-Body $state 'Capture').Body.Instructions)
    $captureLearnedReads = @($captureDecisionIl | Where-Object { $_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.Name -eq 'PelletCategoryId' }).Count
    $captureShooterReads = @($captureDecisionIl | Where-Object { $_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.Name -eq 'ShooterLookup' }).Count
    if ($captureLearnedReads -lt 2 -or $captureShooterReads -lt 1) {
        throw "ShotgunVolleyState.Capture must read the learned pellet category and the shooter identity from the volley's own scope (PelletCategoryId reads=$captureLearnedReads, ShooterLookup reads=$captureShooterReads): the learned category is per volley, never cross-owner (1.2.19 Survival retest)."
    }
    # The strongest structural form of "per volley, never cross-owner": the learned value may be WRITTEN by exactly
    # one method - ShotgunVolleyState.Capture, on the scope instance the open volley owns. Any second stfld site
    # would mean another volley (or another owner's Fire) could plant or overwrite a learned category.
    $learnedWriteSites = @()
    foreach ($writeType in $module.Types) {
        foreach ($writeMethod in $writeType.Methods) {
            if (-not $writeMethod.HasBody) { continue }
            foreach ($writeInstruction in $writeMethod.Body.Instructions) {
                if ($writeInstruction.OpCode.Name -ne 'stfld') { continue }
                if ($writeInstruction.Operand -is [Mono.Cecil.FieldReference] -and
                    $writeInstruction.Operand.Name -eq 'PelletCategoryId' -and
                    $null -ne $writeInstruction.Operand.DeclaringType -and
                    $writeInstruction.Operand.DeclaringType.FullName -eq 'HawChow.GTFOKillFeed.ShotgunDamageNumberScope') {
                    $learnedWriteSites += ($writeType.FullName + '::' + $writeMethod.Name)
                }
            }
        }
    }
    if ($learnedWriteSites.Count -ne 1 -or $learnedWriteSites[0] -ne 'HawChow.GTFOKillFeed.ShotgunVolleyState::Capture') {
        throw "ShotgunDamageNumberScope.PelletCategoryId may be written by exactly one method - ShotgunVolleyState.Capture, on the open volley's own scope instance - so a learned pellet category can never be shared with another volley or another owner: found [$($learnedWriteSites -join ', ')] (1.2.19 Survival retest)."
    }

    # ---------------------------------------------------------------- 3) one drain, one draw site
    $drainSites = @()
    $drawSites = @()
    foreach ($type in $module.Types) {
        foreach ($method in $type.Methods) {
            if (-not $method.HasBody) { continue }
            $il = @($method.Body.Instructions)
            if ((Find-Calls $il 'HawChow.GTFOKillFeed.ShotgunDamageNumberBatch' 'Drain').Count -gt 0) {
                $drainSites += ($type.FullName + '::' + $method.Name)
            }
            if ((Find-Calls $il 'HawChow.GTFOKillFeed.DamageNumbers' 'AddMergedShotgun').Count -gt 0) {
                $drawSites += ($type.FullName + '::' + $method.Name)
            }
        }
    }
    if ($drainSites.Count -ne 1 -or $drainSites[0] -ne 'HawChow.GTFOKillFeed.ShotgunDamageNumberCapture::DrawSettled') {
        throw "ShotgunDamageNumberBatch.Drain must be called from exactly one place (the end path ShotgunDamageNumberCapture::DrawSettled, reached from DrainVolley/Tick/BeginFire); found: $($drainSites -join ', ')"
    }
    if ($drawSites.Count -ne 1 -or $drawSites[0] -ne 'HawChow.GTFOKillFeed.ShotgunDamageNumberCapture::DrawSettled') {
        throw "DamageNumbers.AddMergedShotgun must have exactly one call site (the end path ShotgunDamageNumberCapture::DrawSettled); found: $($drawSites -join ', ')"
    }

    # ---------------------------------------------------------------- 4) the end check rides the HUD tick
    $numbers = Get-Type 'HawChow.GTFOKillFeed.DamageNumbers'
    $numbersTickIl = @((Get-Body $numbers 'Tick').Body.Instructions)
    if ((Find-Calls $numbersTickIl 'HawChow.GTFOKillFeed.ShotgunDamageNumberCapture' 'Tick').Count -ne 1) {
        throw "DamageNumbers.Tick must call ShotgunDamageNumberCapture.Tick exactly once (the existing main-thread pass). IL: $(Show $numbersTickIl)"
    }
    $captureTickIl = @((Get-Body $capture 'Tick').Body.Instructions)
    if ((Find-Calls $captureTickIl 'HawChow.GTFOKillFeed.ShotgunVolleyState' 'ShouldEnd').Count -ne 1 -or
        (Find-Calls $captureTickIl 'HawChow.GTFOKillFeed.ShotgunDamageNumberCapture' 'DrainVolley').Count -ne 1) {
        throw "ShotgunDamageNumberCapture.Tick must ask the volley state whether to end and then drain once. IL: $(Show $captureTickIl)"
    }

    # F6 (1.2.19 review): the diagnostic allowance is restored with the level, not once per session.
    $captureResetIl = @((Get-Body $capture 'Reset').Body.Instructions)
    # R6 (2nd review) + 1.2.19 fix D5: every diagnostic owns its allowance - Reset must restore ALL FOUR (volley,
    # merged, refused-pellet signatures, empty drain) and each log site must ask its own budget. A shared budget was
    # already spent by the volley lines alone once, which is exactly how the Survival log lost the receipt that mattered.
    if ((Find-Calls $captureResetIl 'HawChow.GTFOKillFeed.DiagnosticBudget' 'Reset').Count -ne 4) {
        throw "ShotgunDamageNumberCapture.Reset must reset ALL FOUR diagnostic budgets (`[DN] volley`, `[DN] merged`, `[DN] pellet=drop`, `[DN] drain`), so a new level gets a fresh allowance for each (F6/R6/D5). IL: $(Show $captureResetIl)"
    }
    $budgetFields = @($capture.Fields | Where-Object { $_.FieldType.FullName -eq 'HawChow.GTFOKillFeed.DiagnosticBudget' })
    if ($budgetFields.Count -ne 4) {
        throw "ShotgunDamageNumberCapture must hold exactly four DiagnosticBudget instances (volley + merged + refused pellet + empty drain); found $($budgetFields.Count) (R6/D5)."
    }
    $mergedLinesField = $capture.Fields | Where-Object { $_.Name -eq 'MergedLogLines' }
    if ($null -eq $mergedLinesField -or -not $mergedLinesField.HasConstant -or [int]$mergedLinesField.Constant -ne 8) {
        throw 'ShotgunDamageNumberCapture.MergedLogLines must stay a compile-time 8, so the merged receipt is as visible as the volley line (R6).'
    }
    $logMergedIl = @((Get-Body $capture 'LogMerged').Body.Instructions)
    $logVolleyIl = @((Get-Body $capture 'LogVolley').Body.Instructions)
    $logDropIl = @((Get-Body $capture 'LogPelletDrop').Body.Instructions)
    $logDrainIl = @((Get-Body $capture 'LogEmptyDrain').Body.Instructions)
    $mergedBudgetReads = @($logMergedIl | Where-Object { $_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.Name -eq 'MergedLog' }).Count
    $volleyBudgetReadsOnMerged = @($logMergedIl | Where-Object { $_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.Name -eq 'VolleyLog' }).Count
    $volleyBudgetReads = @($logVolleyIl | Where-Object { $_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.Name -eq 'VolleyLog' }).Count
    if ($mergedBudgetReads -ne 1 -or $volleyBudgetReadsOnMerged -ne 0 -or $volleyBudgetReads -ne 1) {
        throw "The merged line must spend its OWN allowance and the volley line its own (R6): LogMerged/MergedLog=$mergedBudgetReads, LogMerged/VolleyLog=$volleyBudgetReadsOnMerged, LogVolley/VolleyLog=$volleyBudgetReads."
    }
    $dropBudgetReads = @($logDropIl | Where-Object { $_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.Name -eq 'DropLog' }).Count
    $drainBudgetReads = @($logDrainIl | Where-Object { $_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.Name -eq 'DrainLog' }).Count
    if ($dropBudgetReads -ne 1 -or $drainBudgetReads -ne 1) {
        throw "The refused-pellet receipt and the empty-drain receipt must each spend their OWN allowance (D5): LogPelletDrop/DropLog=$dropBudgetReads, LogEmptyDrain/DrainLog=$drainBudgetReads."
    }

    # ---------------------------------------------------------------- 5) the > 1 pellet gate
    # 1.2.19 review R3: the gate now lives in BeginFire (`!requireMultiPellet || ReadShotgunBulletCount(weapon) > 1`) and
    # BeginMultiPelletFire passes requireMultiPellet = true, so both are asserted here.
    $multiIl = @((Get-Body $capture 'BeginMultiPelletFire').Body.Instructions)
    $multiDelegates = Find-Calls $multiIl 'HawChow.GTFOKillFeed.ShotgunDamageNumberCapture' 'BeginFire'
    if ($multiDelegates.Count -ne 1) { throw "BeginMultiPelletFire must delegate to BeginFire. IL: $(Show $multiIl)" }
    $countCall = Find-Calls $beginFireEntryIl 'HawChow.GTFOKillFeed.ShotgunDamageNumberCapture' 'ReadShotgunBulletCount'
    if ($countCall.Count -lt 1) { throw "BeginFire must read the weapon's own ShotgunBulletCount. IL: $(Show $beginFireEntryIl)" }
    $gate = $false
    for ($i = 0; $i -lt $beginFireEntryIl.Count - 1; $i++) {
        if ($beginFireEntryIl[$i].OpCode.Name -ne 'ldc.i4.1') { continue }
        # Roslyn emits either a `cgt` or a direct `bgt`/`bgt.s` against the literal 1; both mean `> 1`.
        if ($beginFireEntryIl[$i + 1].OpCode.Name -match '^(cgt|cgt\.un|bgt|bgt\.s|bgt\.un|bgt\.un\.s)$') { $gate = $true }
    }
    if (-not $gate) { throw "BeginFire must open a volley for a BulletWeapon only when ShotgunBulletCount > 1 (the > 1 gate). IL: $(Show $beginFireEntryIl)" }
    $readIl = @((Get-Body $capture 'ReadShotgunBulletCount').Body.Instructions)
    if ((Find-Calls $readIl 'Gear.BulletWeaponArchetype' 'get_m_archetypeData').Count -ne 1 -or
        (Find-Calls $readIl 'GameData.ArchetypeDataBlock' 'get_ShotgunBulletCount').Count -ne 1) {
        throw "ReadShotgunBulletCount must read m_archeType.m_archetypeData.ShotgunBulletCount (the verified interop path). IL: $(Show $readIl)"
    }

    # ---------------------------------------------------------------- 5b) the 1.2.19 path proof (DamageOrigin)
    # The volley membership used to be "gear category + 0.30 s window", so a melee / explosion / DoT / push hit landing
    # right after a shotgun blast could still be summed into the shot (and hidden by ShowShotgunDamageNumbers=false).
    # The damage root now carries the ENTRY that produced it and only DamageOrigin.Bullet may merge. This section pins the
    # compiled contract: the vocabulary, the entry -> origin literals, the reuse rule, and the cascade.
    $originType = Get-Type 'HawChow.GTFOKillFeed.DamageOrigin'
    if (-not $originType.IsEnum) { throw 'HawChow.GTFOKillFeed.DamageOrigin must stay an enum (pure data, no behaviour).' }
    $originValues = @{}
    foreach ($field in $originType.Fields) {
        if ($field.HasConstant -and $null -ne $field.Constant) { $originValues[$field.Name] = [int]$field.Constant }
    }
    foreach ($requiredOrigin in @('Other', 'Bullet', 'Melee', 'Fire', 'Push')) {
        if (-not $originValues.ContainsKey($requiredOrigin)) { throw "DamageOrigin is missing the $requiredOrigin member (1.2.19)." }
    }
    if ($originValues['Other'] -ne 0 -or $originValues['Bullet'] -ne 1 -or $originValues['Melee'] -ne 2 -or
        $originValues['Fire'] -ne 3 -or $originValues['Push'] -ne 5) {
        throw "DamageOrigin must keep Other=0 (the field default, so an unlabelled root is never a bullet), Bullet=1, Melee=2, Fire=3, Push=5; found Other=$($originValues['Other']) Bullet=$($originValues['Bullet']) Melee=$($originValues['Melee']) Fire=$($originValues['Fire']) Push=$($originValues['Push'])."
    }
    $rootType = Get-Type 'HawChow.GTFOKillFeed.DamageRoot'
    $originField = @($rootType.Fields | Where-Object { $_.Name -eq 'Origin' })
    if ($originField.Count -ne 1 -or $originField[0].FieldType.FullName -ne 'HawChow.GTFOKillFeed.DamageOrigin') {
        throw 'DamageRoot must carry exactly one DamageOrigin Origin field, so the entry survives the whole damage call (1.2.19).'
    }
    $resolveOrigin = @($policy.Methods | Where-Object { $_.Name -eq 'ResolveOrigin' })
    if ($resolveOrigin.Count -ne 1 -or
        (@($resolveOrigin[0].Parameters | ForEach-Object { $_.ParameterType.FullName }) -join ',') -ne 'System.Boolean,HawChow.GTFOKillFeed.DamageOrigin,HawChow.GTFOKillFeed.DamageOrigin,HawChow.GTFOKillFeed.DamageOrigin' -or
        $resolveOrigin[0].ReturnType.FullName -ne 'HawChow.GTFOKillFeed.DamageOrigin') {
        throw 'FeedbackPolicy.ResolveOrigin must stay the single (reused, existing, entry, enclosing) -> DamageOrigin decision.'
    }
    $resolveOriginIl = @($resolveOrigin[0].Body.Instructions)
    if ((@($resolveOriginIl | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] })).Count -ne 0) {
        throw "FeedbackPolicy.ResolveOrigin must stay a pure decision (it may call nothing). IL: $(Show $resolveOriginIl)"
    }
    $isShotgunNumber = @($policy.Methods | Where-Object { $_.Name -eq 'IsShotgunDamageNumber' })
    if ($isShotgunNumber.Count -ne 1 -or
        (@($isShotgunNumber[0].Parameters | ForEach-Object { $_.ParameterType.FullName }) -join ',') -ne 'System.Boolean,HawChow.GTFOKillFeed.DamageOrigin,System.Boolean,System.Boolean') {
        throw 'FeedbackPolicy.IsShotgunDamageNumber must take (bool preview, DamageOrigin origin, bool knownShotgunCategory, bool activeFireCategory): the classifier asks the path proof, not the category alone.'
    }
    $trackerType = Get-Type 'HawChow.GTFOKillFeed.KillTracker'
    $beginMethod = @($trackerType.Methods | Where-Object { $_.Name -eq 'Begin' -and $_.Parameters.Count -eq 6 })
    if ($beginMethod.Count -ne 1 -or $beginMethod[0].Parameters[5].ParameterType.FullName -ne 'HawChow.GTFOKillFeed.DamageOrigin') {
        throw 'KillTracker.Begin must take the entry DamageOrigin as its sixth parameter, so no call can create a root without naming its entry.'
    }
    $beginIl = @($beginMethod[0].Body.Instructions)
    $resolveAt = Find-Calls $beginIl 'HawChow.GTFOKillFeed.FeedbackPolicy' 'ResolveOrigin'
    if ($resolveAt.Count -ne 2) {
        throw "KillTracker.Begin must resolve the origin through FeedbackPolicy.ResolveOrigin exactly twice (the reuse branch and the fresh-root branch); found $($resolveAt.Count). IL: $(Show $beginIl)"
    }
    foreach ($callAt in $resolveAt) {
        $storeAt = -1
        for ($i = $callAt + 1; $i -lt [math]::Min($beginIl.Count, $callAt + 3); $i++) {
            $operand = $beginIl[$i].Operand
            if ($operand -is [Mono.Cecil.FieldReference] -and $operand.DeclaringType.FullName -eq 'HawChow.GTFOKillFeed.DamageRoot' -and $operand.Name -eq 'Origin') { $storeAt = $i }
        }
        if ($storeAt -lt 0) { throw "Each FeedbackPolicy.ResolveOrigin call in KillTracker.Begin must store straight into DamageRoot.Origin. IL: $(Show $beginIl)" }
    }
    # The reuse branch passes ResolveOrigin(true, root.Origin, entry, Other); the fresh-root branch passes
    # ResolveOrigin(false, Other, entry, EnclosingOrigin). In IL the argument pushed right before `entry` is therefore
    # `ldfld DamageRoot::Origin` on the reuse path and the literal `ldc.i4.0` (Other) on the fresh path - which is
    # exactly the fact a reused Bullet root can never be downgraded to Other.
    $reuseResolve = -1
    $freshResolve = -1
    $enclosingCalls = Find-Calls $beginIl 'HawChow.GTFOKillFeed.KillTracker' 'EnclosingOrigin'
    if ($enclosingCalls.Count -ne 1) {
        throw "The fresh root of an anonymous entry must inherit the enclosing proof (EnclosingOrigin) exactly once. IL: $(Show $beginIl)"
    }
    foreach ($callAt in $resolveAt) {
        $entryArgAt = -1
        for ($i = $callAt - 1; $i -ge [math]::Max(0, $callAt - 8); $i--) {
            if ($beginIl[$i].OpCode.Name -eq 'ldarg.s' -and $beginIl[$i].Operand.Name -eq 'entry') { $entryArgAt = $i; break }
        }
        if ($entryArgAt -lt 1) { throw "Each ResolveOrigin call in KillTracker.Begin must pass the entry origin argument. IL: $(Show $beginIl)" }
        $existingArg = $beginIl[$entryArgAt - 1]
        $reusedFlag = -1
        for ($i = $entryArgAt - 2; $i -ge [math]::Max(0, $entryArgAt - 5); $i--) {
            if ($beginIl[$i].OpCode.Name -eq 'ldc.i4.1' -or $beginIl[$i].OpCode.Name -eq 'ldc.i4.0') { $reusedFlag = $i; break }
        }
        $enclosingBetween = @($enclosingCalls | Where-Object { $_ -gt $entryArgAt -and $_ -lt $callAt }).Count -ge 1
        if ($existingArg.OpCode.Name -eq 'ldfld' -and $existingArg.Operand.Name -eq 'Origin' -and
            $reusedFlag -ge 0 -and $beginIl[$reusedFlag].OpCode.Name -eq 'ldc.i4.1') { $reuseResolve = $callAt }
        if ($existingArg.OpCode.Name -eq 'ldc.i4.0' -and $enclosingBetween) { $freshResolve = $callAt }
    }
    if ($reuseResolve -lt 0 -or $freshResolve -lt 0 -or $reuseResolve -eq $freshResolve) {
        throw "KillTracker.Begin must call ResolveOrigin(true, root.Origin, entry, Other) on the reuse path and ResolveOrigin(false, Other, entry, EnclosingOrigin) when the root is fresh, so a reused Bullet root can never be downgraded to Other. IL: $(Show $beginIl)"
    }
    # The three root entries: exactly one Begin call each, with the entry's own origin literal.
    $entryOrigins = @(
        @{ Name = 'BeginBullet'; Literal = 'ldc.i4.1' },
        @{ Name = 'BeginProcessedDamage'; Literal = 'ldc.i4.0' }
    )
    foreach ($entry in $entryOrigins) {
        $entryIl = @((Get-Body $trackerType $entry.Name).Body.Instructions)
        $callAt = Find-Calls $entryIl 'HawChow.GTFOKillFeed.KillTracker' 'Begin'
        if ($callAt.Count -ne 1) { throw "KillTracker.$($entry.Name) must enter the shared root exactly once; found $($callAt.Count). IL: $(Show $entryIl)" }
        if ($callAt[0] -lt 1 -or $entryIl[$callAt[0] - 1].OpCode.Name -ne $entry.Literal) {
            throw "KillTracker.$($entry.Name) must pass its origin literal ($($entry.Literal)) into Begin. IL: $(Show $entryIl)"
        }
    }
    $limbIl = @((Get-Body $trackerType 'BeginLimb').Body.Instructions)
    $limbCall = Find-Calls $limbIl 'HawChow.GTFOKillFeed.KillTracker' 'Begin'
    if ($limbCall.Count -ne 1) { throw "KillTracker.BeginLimb must enter the shared root exactly once; found $($limbCall.Count)." }
    $limbBullet = $false
    $limbMelee = $false
    $limbMeleeFlag = $false
    for ($i = 0; $i -lt $limbCall[0]; $i++) {
        if ($limbIl[$i].OpCode.Name -eq 'ldc.i4.1') { $limbBullet = $true }
        if ($limbIl[$i].OpCode.Name -eq 'ldc.i4.2') { $limbMelee = $true }
        if ($limbIl[$i].OpCode.Name -eq 'ldarg.s' -and $limbIl[$i].Operand.Name -eq 'melee') { $limbMeleeFlag = $true }
    }
    if (-not $limbBullet -or -not $limbMelee -or -not $limbMeleeFlag) {
        throw "KillTracker.BeginLimb must decide 'melee ? DamageOrigin.Melee (2) : DamageOrigin.Bullet (1)' before it calls Begin. IL: $(Show $limbIl)"
    }
    # The cascade (review R3): the classifier/route lives in DamageNumberRouter.Decide, which DamageNumbers.Add calls; the
    # Unity adapter no longer carries a second copy of it.
    $captureTry = @($capture.Methods | Where-Object { $_.Name -eq 'TryCapture' })
    if ($captureTry.Count -ne 0) {
        throw 'ShotgunDamageNumberCapture.TryCapture must not come back: the route belongs to DamageNumberRouter.Decide so the smoke suite can assert the production path.'
    }
    $router = Get-Type 'HawChow.GTFOKillFeed.DamageNumberRouter'
    $decide = @($router.Methods | Where-Object { $_.Name -eq 'Decide' })
    if ($decide.Count -ne 1) { throw 'DamageNumberRouter.Decide must exist exactly once (review R3).' }
    $decideIl = @($decide[0].Body.Instructions)
    $classifyAt = (Find-Calls $decideIl 'HawChow.GTFOKillFeed.FeedbackPolicy' 'IsShotgunDamageNumber')
    $gateAt = (Find-Calls $decideIl 'HawChow.GTFOKillFeed.FeedbackPolicy' 'ShouldDisplayDamageNumber')
    if ($classifyAt.Count -ne 1 -or $gateAt.Count -ne 1 -or $classifyAt[0] -gt $gateAt[0] -or
        (Find-Calls $decideIl 'HawChow.GTFOKillFeed.ShotgunVolleyState' 'Capture').Count -ne 1) {
        throw "DamageNumberRouter.Decide must classify (IsShotgunDamageNumber) BEFORE the visibility gate and ask the volley exactly once (review R3/D1). IL: $(Show $decideIl)"
    }
    $addNumbers = @((Get-Type 'HawChow.GTFOKillFeed.DamageNumbers').Methods | Where-Object { $_.Name -eq 'Add' })
    if ($addNumbers.Count -ne 1 -or $addNumbers[0].Parameters[5].ParameterType.FullName -ne 'HawChow.GTFOKillFeed.DamageOrigin') {
        throw 'DamageNumbers.Add must take the hit DamageOrigin (last parameter), so the classifier can never decide "shotgun" from a category alone.'
    }
    $addIl = @($addNumbers[0].Body.Instructions)
    if ((Find-Calls $addIl 'HawChow.GTFOKillFeed.DamageNumberRouter' 'Decide').Count -ne 1 -or
        (Find-Calls $addIl 'HawChow.GTFOKillFeed.ShotgunDamageNumberCapture' 'TryCapture').Count -ne 0 -or
        (Find-Calls $addIl 'HawChow.GTFOKillFeed.FeedbackPolicy' 'IsShotgunDamageNumber').Count -ne 0 -or
        (Find-Calls $addIl 'HawChow.GTFOKillFeed.ShotgunDamageNumberCapture' 'IsActiveFireCategory').Count -ne 1 -or
        (Find-Calls $addIl 'HawChow.GTFOKillFeed.ShotgunDamageNumberCapture' 'IsShotgunCategory').Count -ne 1) {
        throw "DamageNumbers.Add must be the thin Unity adapter: one DamageNumberRouter.Decide call with the live flags and the current target, plus the two category questions that feed it. IL: $(Show $addIl)"
    }
    # Fire / push: read-only call scopes, labelled Fire (3) and Push (5), popped by their own finalizer.
    foreach ($hintPatch in @(
        @{ Name = 'HawChow.GTFOKillFeed.EnemyFireDamagePatch'; Target = 'Dam_EnemyDamageBase'; Method = 'ReceiveFireDamage'; Literal = 'ldc.i4.3' },
        @{ Name = 'HawChow.GTFOKillFeed.EnemyPushDamagePatch'; Target = 'Dam_EnemyDamageBase'; Method = 'ReceivePushDamage'; Literal = 'ldc.i4.5' }
    )) {
        $patchType = Get-Type $hintPatch.Name
        $attribute = @($patchType.CustomAttributes | Where-Object { $_.AttributeType.FullName -eq 'HarmonyLib.HarmonyPatch' })
        if ($attribute.Count -ne 1) { throw "$($hintPatch.Name) must carry exactly one [HarmonyPatch] attribute." }
        $patchTarget = $null
        $patchMethod = $null
        foreach ($argument in $attribute[0].ConstructorArguments) {
            if ($argument.Value -is [Mono.Cecil.TypeReference]) { $patchTarget = $argument.Value.FullName }
            if ($argument.Value -is [string]) { $patchMethod = $argument.Value }
        }
        if ($patchTarget -ne $hintPatch.Target -or $patchMethod -ne $hintPatch.Method) {
            throw "$($hintPatch.Name) must patch $($hintPatch.Target)::$($hintPatch.Method); found target='$patchTarget' method='$patchMethod'."
        }
        $prefixIl = @((Get-Body $patchType 'Prefix').Body.Instructions)
        $beginHint = Find-Calls $prefixIl 'HawChow.GTFOKillFeed.KillTracker' 'BeginOriginScope'
        if ($beginHint.Count -ne 1 -or $beginHint[0] -lt 1 -or $prefixIl[$beginHint[0] - 1].OpCode.Name -ne $hintPatch.Literal) {
            throw "$($hintPatch.Name).Prefix must publish exactly one origin scope with $($hintPatch.Literal). IL: $(Show $prefixIl)"
        }
        if ((Find-Calls @((Get-Body $patchType 'Finalizer').Body.Instructions) 'HawChow.GTFOKillFeed.KillTracker' 'EndOriginScope').Count -ne 1) {
            throw "$($hintPatch.Name).Finalizer must pop the origin scope it pushed (a failed call may not relabel the next hit)."
        }
    }
    $originScopeField = @($trackerType.Fields | Where-Object { $_.Name -eq '_originScope' })
    if ($originScopeField.Count -ne 1 -or $originScopeField[0].FieldType.FullName -ne 'HawChow.GTFOKillFeed.DamageOriginScope') {
        throw 'KillTracker must keep its thread-static DamageOriginScope _originScope (one enclosing-call hint per thread).'
    }
    $endScopeIl = @((Get-Body $trackerType 'EndOriginScope').Body.Instructions)
    if ((@($endScopeIl | Where-Object { $_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.Name -eq '_originScope' })).Count -lt 2) {
        throw "KillTracker.EndOriginScope must compare and restore _originScope (LIFO pop, only when it is still ours). IL: $(Show $endScopeIl)"
    }
    # ---------------------------------------------------------------- 6) no coroutine / destroy / clock
    foreach ($typeName in @('HawChow.GTFOKillFeed.ShotgunDamageNumberCapture', 'HawChow.GTFOKillFeed.ShotgunDamageNumberBatch',
        'HawChow.GTFOKillFeed.ShotgunDamageNumberScope', 'HawChow.GTFOKillFeed.ShotgunVolleyState')) {
        $type = Get-Type $typeName
        foreach ($method in $type.Methods) {
            if (-not $method.HasBody) { continue }
            foreach ($instruction in $method.Body.Instructions) {
                $operand = $instruction.Operand
                if (-not ($operand -is [Mono.Cecil.MethodReference])) { continue }
                $name = $operand.Name
                if ($name -eq 'StartCoroutine' -or $name -eq 'StartCoroutine_Auto') {
                    throw "$typeName::$($method.Name) must not start a coroutine for the pellet volley."
                }
                if ($name -eq 'Destroy' -or $name -eq 'DestroyImmediate') {
                    throw "$typeName::$($method.Name) must not destroy anything for the pellet volley."
                }
                if ($name -eq 'set_timeScale') {
                    throw "$typeName::$($method.Name) must never write Time.timeScale."
                }
            }
        }
    }
    Write-Host "SHOTGUN_VOLLEY_IL=PASS;FIRE_TARGETS=Shotgun,ShotgunSynced,BulletWeapon,BulletWeaponSynced;WINDOW=0.5s;CAPTURE_LAG=0.30s;SILENCE=0.05s;FRAME_CRITERION=absent;PELLET_GATE=>1;CATEGORY_GATE=IsVolleyHit8(window,volleyCat,learnedCat,hitCat,origin,nested,volleyShooter,hitShooter);WILDCARDS=hit0:1,volley0:0,nested:1,learned:2,hitEqVolley:1,hitEqLearned:1;NESTED_GATED_BY=learnedCat==0;SHOOTER_GATE=two-known-and-different;LEARNED_SCOPE=per-volley(ShotgunDamageNumberScope.PelletCategoryId,never-registered);LEARNED_WRITE=Capture-only;REGISTRY_FEED=BeginFire-only;ORIGIN_GATE=Bullet-only(Other=0);ENTRY_ORIGINS=BulletDamage/Bullet->Bullet,Melee->Melee,ProcessedDamage->Other,Fire->Fire,Push->Push;ROOT_REUSE=ResolveOrigin(no-Bullet-downgrade);FIRE_IDENTITY=IsForeignVolleyOwner(all-4-prefixes);EXPECTED_COUNT=diagnostic-only;DRAIN_SITES=1;HUD_TICK=True;LOG_BUDGET=volley8+merged8/level-reset;COROUTINE=0;DESTROY=0;TIMESCALE_WRITES=0"
}
finally { $assembly.Dispose() }
