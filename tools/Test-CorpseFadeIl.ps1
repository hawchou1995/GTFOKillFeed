[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PluginDll,
    [Parameter(Mandatory = $true)][string]$CecilPath
)
# Compiled-IL guard for the optional native corpse dissolve (review F1/F2/F5).
# Text/comment guards can be tripped by a harmless comment, so the *dangerous* behaviours are asserted
# on the emitted IL of the real assembly:
#   * no Object.Destroy / DestroyImmediate call anywhere (destroying a Decay recycles the shared
#     sound player and silences every later decay);
#   * no Decay.EnsureInitialized / ObjectPooler.InitializePool / Resources.Load (never build the pool);
#   * no Decay.SetMaterialPropertyBlock (never write shared materials);
#   * no HarmonyPatch attribute targeting Decay or ES_Dead (never detour the native state machine);
#   * no write to UnityEngine.Time.timeScale;
#   * the EndAt cleanup calls CorpseFadePrep.Attempt and never CorpseFade.TryBegin directly, so the
#     exception barrier cannot be bypassed, and the borrow/release path really calls
#     Decay.GetInstance / Decay.Play / Decay.Cleanup / ObjectPooler.Pool.Return + reads m_playing;
#   * the slot sweep decides from the native state (ICorpseFadeTarget.Probe), not from a clock.
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

function Get-Method($type, [string]$name) {
    $method = $type.Methods | Where-Object { $_.Name -eq $name } | Select-Object -First 1
    if ($null -eq $method -or -not $method.HasBody) { throw "Compiled plugin is missing the body of $($type.FullName)::$name." }
    return $method
}

function Get-Instructions($method) { return @($method.Body.Instructions) }

function Test-MethodOperand($instruction, [string]$declaringType, [string]$name) {
    return $instruction.Operand -is [Mono.Cecil.MethodReference] -and
        $instruction.Operand.Name -eq $name -and
        $instruction.Operand.DeclaringType -ne $null -and
        $instruction.Operand.DeclaringType.FullName -eq $declaringType
}

function Find-Indices($instructions, [scriptblock]$match) {
    $found = @()
    for ($i = 0; $i -lt $instructions.Count; $i++) { if (& $match $instructions[$i]) { $found += $i } }
    return $found
}

# Review C7: a gate that only counts calls would stay green if the argument were a hard-coded literal. This looks
# BACKWARDS from a call site and requires one matching instruction inside the window that builds its arguments.
function Assert-OperandBefore($instructions, [int]$callIndex, [scriptblock]$match, [int]$window, [string]$message) {
    $start = [Math]::Max(0, $callIndex - $window)
    for ($i = $start; $i -lt $callIndex; $i++) {
        if (& $match $instructions[$i]) { return }
    }
    throw $message
}

$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path -LiteralPath $PluginDll).Path)
try {
    $module = $assembly.MainModule
    $allMethods = New-Object System.Collections.Generic.List[object]
    foreach ($type in $module.GetTypes()) {
        foreach ($method in $type.Methods) { if ($method.HasBody) { $allMethods.Add($method) } }
    }
    if ($allMethods.Count -eq 0) { throw 'The compiled plugin exposes no method bodies to inspect.' }

    # ---------------------------------------------------------------- forbidden actions (whole assembly)
    foreach ($method in $allMethods) {
        $il = Get-Instructions $method
        foreach ($ins in $il) {
            $operand = $ins.Operand
            if ($operand -is [Mono.Cecil.MethodReference]) {
                $declaring = if ($null -ne $operand.DeclaringType) { $operand.DeclaringType.FullName } else { '' }
                # Texture2D churn in the icon pipeline legitimately destroys its own textures, so the
                # no-destroy rule is scoped to the corpse/dissolve code that touches the native pool.
                $isCorpseType = $method.DeclaringType.FullName -match '^HawChow\.GTFOKillFeed\.(CorpseFade|CorpseFeedback|CorpseVisualLease)'
                if ($isCorpseType -and $operand.Name -in @('Destroy', 'DestroyImmediate') -and $declaring -in @('UnityEngine.Object', 'UnityEngine.Object[]')) {
                    throw "$($method.FullName) calls $declaring::$($operand.Name): destroying a pooled Decay/GameObject recycles the shared sound player. IL: $(Show $il)"
                }
                if ($operand.Name -in @('EnsureInitialized', 'InitializePool') -and $declaring -in @('Decay', 'ObjectPooler')) {
                    throw "$($method.FullName) calls $declaring::$($operand.Name): KillFeed must never create/initialise the native Decay pool."
                }
                if ($operand.Name -eq 'SetMaterialPropertyBlock' -and $declaring -eq 'Decay') {
                    throw "$($method.FullName) calls Decay::SetMaterialPropertyBlock: shared material blocks are off limits."
                }
                # R8: only CorpseFade.cs may drive the native Decay API. A Decay::Play/Initialize call inside
                # CorpseFeedback (or one of its lambdas) would bypass every safe-path check the dissolve owns
                # (state probe, stop action, m_playing proof, pool conservation).
                if ($method.DeclaringType.FullName -match '^HawChow\.GTFOKillFeed\.CorpseFeedback' -and
                    $declaring -eq 'Decay' -and $operand.Name -in @('Play', 'Initialize', 'Cleanup', 'GetInstance')) {
                    throw "$($method.FullName) calls Decay::$($operand.Name): only CorpseFade may drive the native Decay API (review R8)."
                }
                if ($operand.Name -eq 'set_timeScale' -and $declaring -eq 'UnityEngine.Time') {
                    throw "$($method.FullName) writes UnityEngine.Time.timeScale: the game clock must stay untouched."
                }
                # The corpse burst FX loads the game's own death prefab; that is the pre-existing 1.2.8
                # behaviour and is not this guard's business. Only the dissolve path must never load assets.
                if ($operand.Name -eq 'Load' -and $declaring -eq 'UnityEngine.Resources' -and
                    $ins.OpCode.Name -match '^(call|callvirt|ldftn)$' -and
                    $method.DeclaringType.FullName -match '^HawChow\.GTFOKillFeed\.CorpseFade') {
                    throw "$($method.FullName) calls Resources.Load: the native pool/prefab must already exist."
                }
            }
        }
    }

    # ---------------------------------------------------------------- no Harmony detour of the native decay
    $patchTargets = New-Object System.Collections.Generic.List[string]
    foreach ($type in $module.GetTypes()) {
        foreach ($attribute in $type.CustomAttributes) {
            if ($attribute.AttributeType.FullName -ne 'HarmonyLib.HarmonyPatch') { continue }
            foreach ($argument in $attribute.ConstructorArguments) {
                $values = @($argument.Value)
                foreach ($value in $values) {
                    if ($value -is [Mono.Cecil.TypeReference]) {
                        $patchTargets.Add($value.FullName)
                        # 1.2.16 (second real-machine pass): EVERY ES_* state machine is off limits, not only
                        # ES_Dead - a Harmony detour there is known to trip a native AccessViolation at load.
                        # The native corpse dissolve is therefore reached through the corpse's own Decay
                        # component (CorpseFade.TryNative), never by patching a state machine.
                        if ($value.FullName -eq 'Decay' -or $value.Name -like 'ES_*') {
                            throw "$($type.FullName) declares [HarmonyPatch(typeof($($value.FullName)))]: no ES_* state machine and no native Decay may ever be patched."
                        }
                    }
                    elseif ($value -is [string] -and ($value -eq 'Decay' -or $value -like 'ES_*')) {
                        throw "$($type.FullName) declares [HarmonyPatch(..., `"$value`")]: no ES_* state machine and no native Decay may ever be patched."
                    }
                }
            }
        }
    }
    # Explicit negative gate: zero ES_*/Decay Harmony targets, whatever their namespace.
    $esPatches = @($patchTargets | Where-Object { $_ -eq 'Decay' -or $_ -like '*ES_*' })
    if ($esPatches.Count -ne 0) {
        throw "ES_*/Decay Harmony patches must be 0 (native state-machine detours crash at load); found $($esPatches.Count): $($esPatches -join ', ')"
    }

    # ---------------------------------------------------------------- the conservation path is really compiled in
    $fade = $module.GetType('HawChow.GTFOKillFeed.CorpseFade')
    if ($null -eq $fade) { throw 'Compiled plugin is missing HawChow.GTFOKillFeed.CorpseFade.' }
    $targetType = $module.GetType('HawChow.GTFOKillFeed.CorpseFade/DecayFadeTarget')
    if ($null -eq $targetType) { throw 'Compiled plugin is missing the DecayFadeTarget adapter.' }
    $targetIl = @()
    foreach ($method in $targetType.Methods) { if ($method.HasBody) { $targetIl += (Get-Instructions $method) } }
    foreach ($expected in @(
    # The pooled path borrows, arms with a verified target, plays, probes, then hands ownership to the game.
    # Failed arming returns exactly once; playing/unknown handles retain their slot.
        @{ Declaring = 'Decay'; Name = 'GetInstance' },
        @{ Declaring = 'Decay'; Name = 'Play' },
        @{ Declaring = 'Decay'; Name = 'Cleanup' },
        @{ Declaring = 'ObjectPooler/Pool'; Name = 'Return' }
    )) {
        if ((Find-Indices $targetIl { param($ins) Test-MethodOperand $ins $expected.Declaring $expected.Name }).Count -lt 1) {
            throw "The native dissolve adapter must call $($expected.Declaring)::$($expected.Name) so the verified borrow/release path is the one that ships."
        }
    }
    # IL2CPP interop exposes the public field m_playing as a field-like property accessor.
    $playingField = Find-Indices $targetIl { param($ins) $ins.Operand -is [Mono.Cecil.MethodReference] -and $ins.Operand.Name -in @('get_m_playing', 'm_playing') -and $ins.Operand.DeclaringType.FullName -eq 'Decay' }
    if ($playingField.Count -lt 1) { throw 'The native dissolve adapter must read Decay.m_playing: it is the only ownership proof.' }
    # 1.2.20: the adapter's arm gate must call Decay::Initialize for both renderer overloads, read m_targetRenderer
    # back exactly once as the identity proof, and route the verdict through ArmVerdict - nothing else.
    $adapterReady = Get-Method $targetType 'Ready'
    $readyIl = Get-Instructions $adapterReady
    $readyInitialize = @(Find-Indices $readyIl { param($ins) $ins.Operand -is [Mono.Cecil.MethodReference] -and $ins.Operand.Name -eq 'Initialize' -and $null -ne $ins.Operand.DeclaringType -and $ins.Operand.DeclaringType.FullName -eq 'Decay' })
    if ($readyInitialize.Count -ne 2 -or
        @(Find-Indices $readyIl { param($ins) $ins.Operand -is [Mono.Cecil.MethodReference] -and $ins.Operand.Name -eq 'get_m_targetRenderer' }).Count -ne 1 -or
        @(Find-Indices $readyIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.CorpseFadePolicy' 'ArmVerdict' }).Count -ne 1) {
        throw "DecayFadeTarget.Ready must arm the borrowed instance (Decay.Initialize, both overloads), read m_targetRenderer once and answer through ArmVerdict (1.2.20). IL: $(Show $readyIl)"
    }
    $drawableGate = @(Find-Indices $readyIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.FadeRendererCast' 'HasDrawableMesh' })
    if ($drawableGate.Count -ne 1 -or $drawableGate[0] -gt $readyInitialize[0]) {
        throw 'Pooled arming must verify an active renderer with a non-empty sharedMesh before either native Initialize overload.'
    }
    if ($targetIl | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'HasTarget' }) {
        throw 'The pooled adapter must not call FadeRendererCast.HasTarget: identity is unprovable there (F2).'
    }

    # The sweep/reset bookkeeping may only be driven by the native state probe, never by a clock.
    $coordinator = $module.GetType('HawChow.GTFOKillFeed.CorpseFadeCoordinator')
    if ($null -eq $coordinator) { throw 'Compiled plugin is missing HawChow.GTFOKillFeed.CorpseFadeCoordinator.' }
    if (@($coordinator.Methods | Where-Object { $_.Name -eq 'Sweep' }).Count -ne 1) { throw 'CorpseFadeCoordinator must keep exactly one Sweep method.' }
    $prune = Get-Method $coordinator 'Prune'
    $pruneIl = Get-Instructions $prune
    if ((Find-Indices $pruneIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.CorpseFadeCoordinator' 'Probe' }).Count -ne 1) {
        throw "CorpseFadeCoordinator.Prune must decide every slot through the native state probe exactly once. IL: $(Show $pruneIl)"
    }
    $probe = Get-Method $coordinator 'Probe'
    $probeIl = Get-Instructions $probe
    if ((Find-Indices $probeIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.ICorpseFadeTarget' 'Probe' }).Count -ne 1) {
        throw "CorpseFadeCoordinator.Probe must ask ICorpseFadeTarget.Probe for the ownership state exactly once. IL: $(Show $probeIl)"
    }
    # 1.2.9 review: the borrow answer is now a three-state enum, so "pool missing" and "no free instance"
    # cannot collapse into one null handle again, and the coordinator's bounded re-poll must stay bounded.
    $tryBorrow = @($targetType.Methods | Where-Object { $_.Name -eq 'TryBorrow' })[0]
    if ($tryBorrow.ReturnType.FullName -ne 'HawChow.GTFOKillFeed.CorpseFadeBorrow') {
        throw "DecayFadeTarget.TryBorrow must return CorpseFadeBorrow so pool-missing and no-free-instance stay distinguishable; found $($tryBorrow.ReturnType.FullName)."
    }
    if ((Find-Indices $targetIl { param($ins) Test-MethodOperand $ins 'ObjectPooler/Pool' 'get_freeInstanceCount' }).Count -lt 1) {
        throw 'DecayFadeTarget must read ObjectPooler.Pool.freeInstanceCount before borrowing: it is the only proof that an instance is available.'
    }
    $tryStartWithOutcome = @($coordinator.Methods | Where-Object { $_.Name -eq 'TryStart' -and $_.Parameters.Count -eq 3 })
    if ($tryStartWithOutcome.Count -ne 1) { throw 'CorpseFadeCoordinator must keep exactly one TryStart(target, renderer, out outcome) overload for the reason split.' }
    $maxPollsField = $module.GetType('HawChow.GTFOKillFeed.CorpseFadePolicy').Fields | Where-Object { $_.Name -eq 'MaxBorrowPolls' }
    if ($null -eq $maxPollsField -or -not $maxPollsField.HasConstant -or [int]$maxPollsField.Constant -ne 3) {
        throw 'CorpseFadePolicy.MaxBorrowPolls must stay a compile-time constant 3 so the re-poll can never become unbounded.'
    }
    if ((Find-Indices $pruneIl { param($ins) $ins.OpCode.Name -eq 'ldc.r4' }).Count -gt 0) {
        throw 'CorpseFadeCoordinator.Prune must not compare clocks: slots are held from the native state only.'
    }
    $sweepMethods = @($coordinator.Methods | Where-Object { $_.Name -eq 'Sweep' })
    if ($sweepMethods.Count -ne 1 -or $sweepMethods[0].Parameters.Count -ne 1) {
        throw 'CorpseFadeCoordinator.Sweep must take only the fade target: no timestamp may drive the bookkeeping.'
    }
    $policy = $module.GetType('HawChow.GTFOKillFeed.CorpseFadePolicy')
    if ($null -eq $policy) { throw 'Compiled plugin is missing HawChow.GTFOKillFeed.CorpseFadePolicy.' }
    if (@($policy.Methods | Where-Object { $_.Name -eq 'Deadline' }).Count -gt 0) {
        throw 'CorpseFadePolicy must not expose a time deadline: an already-playing instance may never be timed out.'
    }

    # ---------------------------------------------------------------- F1: the EndAt barrier cannot be bypassed
    $prep = $module.GetType('HawChow.GTFOKillFeed.CorpseFadePrep')
    if ($null -eq $prep) { throw 'Compiled plugin is missing HawChow.GTFOKillFeed.CorpseFadePrep: the EndAt exception barrier is gone.' }
    $attempt = Get-Method $prep 'Attempt'
    if ($attempt.Parameters.Count -ne 3) { throw 'CorpseFadePrep.Attempt must take (selectRenderer, begin, onError).' }

    $feedback = $module.GetType('HawChow.GTFOKillFeed.CorpseFeedback')
    if ($null -eq $feedback) { throw 'Compiled plugin is missing HawChow.GTFOKillFeed.CorpseFeedback.' }
    $feedbackPolicy = $module.GetType('HawChow.GTFOKillFeed.FeedbackPolicy')
    if ($null -eq $feedbackPolicy) { throw 'Compiled plugin is missing HawChow.GTFOKillFeed.FeedbackPolicy.' }
    $tick = Get-Method $feedback 'Tick'
    $tickIl = Get-Instructions $tick
    if ((Find-Indices $tickIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.CorpseFadePrep' 'Attempt' }).Count -ne 2) {
        throw "CorpseFeedback.Tick must keep one bounded-preparation barrier and one EndAt barrier. IL: $(Show $tickIl)"
    }
    if ((Find-Indices $tickIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.CorpseFade' 'TryBegin' }).Count -gt 0) {
        throw 'CorpseFeedback.Tick must not call CorpseFade.TryBegin directly: the call has to sit inside the CorpseFadePrep barrier.'
    }
    if ((Find-Indices $tickIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.CorpseFade' 'Sweep' }).Count -ne 1) {
        throw 'CorpseFeedback.Tick must sweep the dissolve bookkeeping exactly once.'
    }
    if ((Find-Indices $tickIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.CorpseFadeRetry' 'CanPrepare' }).Count -ne 1) {
        throw 'The early fade barrier must be guarded by CorpseFadeRetry.CanPrepare exactly once.'
    }
    # The barrier is only useful if the delegate that calls TryBegin is compiled somewhere else (a
    # lambda) and the cleanup still happens in Tick afterwards.
    $deferArguments = @()
    foreach ($type in $feedback.NestedTypes) {
        foreach ($method in $type.Methods) {
            if (-not $method.HasBody) { continue }
            $lambdaIl = Get-Instructions $method
            foreach ($callAt in @(Find-Indices $lambdaIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.CorpseFade' 'TryBegin' })) {
                $call = $lambdaIl[$callAt].Operand
                if ($call.Parameters.Count -ne 6 -or $call.Parameters[5].ParameterType.FullName -ne 'System.Boolean' -or $callAt -lt 1) {
                    throw 'TryBegin must take deferTransient as its sixth Boolean argument.'
                }
                $flag = $lambdaIl[$callAt - 1].OpCode.Name
                if ($flag -notin @('ldc.i4.0','ldc.i4.1')) { throw 'TryBegin deferTransient must be an explicit phase-specific Boolean.' }
                $isFinal = @(Find-Indices $lambdaIl { param($ins) $ins.Operand -is [Mono.Cecil.FieldReference] -and $ins.Operand.DeclaringType.FullName -eq 'HawChow.GTFOKillFeed.CorpseFadeRetry' -and $ins.Operand.Name -eq 'Resolved' }).Count -eq 1
                $expectedFlag = if ($isFinal) { 'ldc.i4.0' } else { 'ldc.i4.1' }
                if ($flag -ne $expectedFlag) { throw 'Fade retry phase requires preparation=true, EndAt=false, selected from the real Resolved guard.' }
                $deferArguments += $flag
            }
        }
    }
    if ($deferArguments.Count -ne 2 -or @($deferArguments | Where-Object { $_ -eq 'ldc.i4.1' }).Count -ne 1 -or
        @($deferArguments | Where-Object { $_ -eq 'ldc.i4.0' }).Count -ne 1) {
        throw 'Fade retry wiring requires preparation=true, EndAt=false; transient preparation failures must remain retryable.'
    }
    if ((Find-Indices $tickIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.CorpseVisualLease' 'Hide' }).Count -lt 1 -or
        (Find-Indices $tickIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.CorpseVisualLease' 'RecyclePhysics' }).Count -lt 1) {
        throw 'CorpseFeedback.Tick must still hide and recycle the corpse leases after the barrier.'
    }

    # ---------------------------------------------------------------- 1.2.16: the launch really happens
    # 1.2.15 waited 40 ms and then asked a destroyed/pooled EnemyAgent for its RagdollInstance; the real log
    # had 97 schedule=accepted and 0 launch=applied. 1.2.16 leased in the death call but only retried inside
    # the burst window, and the game spawns the ragdoll from a coroutine - so the real machine still showed
    # `attempts=0` and `launch=applied` 0. The guard therefore has to see the whole ladder compiled in, in
    # order, plus the grace window that keeps the retries alive after the burst.
    function Get-GenericCallIndices($instructions, [string]$declaringType, [string]$name, [string]$argumentType) {
        $found = @()
        for ($i = 0; $i -lt $instructions.Count; $i++) {
            $operand = $instructions[$i].Operand
            if ($operand -isnot [Mono.Cecil.MethodReference]) { continue }
            if ($operand.Name -ne $name) { continue }
            if ($null -eq $operand.DeclaringType -or $operand.DeclaringType.FullName -ne $declaringType) { continue }
            if ($operand -is [Mono.Cecil.GenericInstanceMethod]) {
                $arguments = @($operand.GenericArguments)
                if ($arguments.Count -eq 1 -and $arguments[0].FullName -eq $argumentType) { $found += $i }
            }
        }
        return $found
    }
    $schedule = Get-Method $feedback 'Schedule'
    $scheduleIl = Get-Instructions $schedule
    if ((Find-Indices $scheduleIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.CorpseFeedback' 'CaptureRagdoll' }).Count -ne 1) {
        throw "CorpseFeedback.Schedule must lease the ragdoll once, in the death call. IL: $(Show $scheduleIl)"
    }
    # The hierarchy snapshot is what a late lookup searches once the EnemyAgent wrapper turns into null.
    if ((Find-Indices $scheduleIl { param($ins) $ins.OpCode.Name -eq 'stfld' -and $ins.Operand -is [Mono.Cecil.FieldReference] -and $ins.Operand.Name -in @('EnemyRoot', 'ModelRoot') }).Count -lt 1) {
        throw 'CorpseFeedback.Schedule must snapshot the enemy/model transform so a late ragdoll lookup survives a destroyed wrapper.'
    }
    foreach ($receipt in @('ragdoll=', 'null@death', 'at-death', 'ladder=', 'lookupWindow=')) {
        if (-not (@($scheduleIl | Where-Object { $_.Operand -is [string] -and $_.Operand.Contains($receipt) }).Count -ge 1)) {
            throw "The death-call receipt must report $receipt so a real-machine log can tell a null ragdoll from a leased one."
        }
    }
    if ((Find-Indices $scheduleIl { param($ins) $ins.Operand -is [Mono.Cecil.FieldReference] -and $ins.Operand.Name -in @('MinLaunchDistance', 'MaxLaunchDistance', 'CorpseTravelScale') -and $ins.Operand.DeclaringType.FullName -eq 'HawChow.GTFOKillFeed.Plugin' }).Count -ne 3) {
        throw 'CorpseFeedback.Schedule must resolve the budget from the player cfg (Min/MaxLaunchDistance + TravelScale) once per corpse.'
    }
    # ---- the ragdoll ladder: RagdollInstance -> RagdollScript -> EnemyRagdollRef -> raw Rigidbody ----
    $resolve = Get-Method $feedback 'ResolveRagdoll'
    $resolveIl = Get-Instructions $resolve
    if ((Find-Indices $resolveIl { param($ins) Test-MethodOperand $ins 'Enemies.EnemyAgent' 'get_RagdollInstance' }).Count -ne 1) {
        throw 'CorpseFeedback.ResolveRagdoll must read the enemy RagdollInstance (rung 1) exactly once per attempt.'
    }
    foreach ($delegate in @('CaptureRagdoll', 'TryLateRagdoll')) {
        if ((Find-Indices (Get-Instructions (Get-Method $feedback $delegate)) { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.CorpseFeedback' 'ResolveRagdoll' }).Count -ne 1) {
            throw "CorpseFeedback.$delegate must go through the shared ragdoll ladder (ResolveRagdoll) exactly once."
        }
    }
    $search = Get-Method $feedback 'SearchRagdollRoot'
    $searchIl = Get-Instructions $search
    $scriptRung = Get-GenericCallIndices $searchIl 'HawChow.GTFOKillFeed.CorpseFeedback' 'Components' 'RagdollScript'
    $refRung = Get-GenericCallIndices $searchIl 'HawChow.GTFOKillFeed.CorpseFeedback' 'Components' 'EnemyRagdollRef'
    $bodyRung = Get-GenericCallIndices $searchIl 'HawChow.GTFOKillFeed.CorpseFeedback' 'Components' 'UnityEngine.Rigidbody'
    if ($scriptRung.Count -ne 1 -or $refRung.Count -ne 1 -or $bodyRung.Count -ne 1) {
        throw "CorpseFeedback.SearchRagdollRoot must keep exactly one rung per source (RagdollScript=$(($scriptRung.Count)), EnemyRagdollRef=$(($refRung.Count)), Rigidbody=$(($bodyRung.Count)))."
    }
    if (-not ($scriptRung[0] -lt $refRung[0] -and $refRung[0] -lt $bodyRung[0])) {
        throw 'The ragdoll ladder order must stay RagdollScript -> EnemyRagdollRef -> raw Rigidbody (a silently reordered ladder would pick a worse source).'
    }
    if ((Find-Indices $searchIl { param($ins) Test-MethodOperand $ins 'RagdollScript' 'get_ragdolled' }).Count -ne 1) {
        throw 'The RagdollScript rung must prefer the instance the game already flagged `ragdolled`.'
    }
    if ((Find-Indices $searchIl { param($ins) Test-MethodOperand $ins 'EnemyRagdollRef' 'get_m_rigidBodyLookup' }).Count -ne 1) {
        throw 'The EnemyRagdollRef rung must be gated on its m_rigidBodyLookup before leasing.'
    }
    $accept = Get-Method $feedback 'AcceptRagdoll'
    $acceptIl = Get-Instructions $accept
    if ((Find-Indices $acceptIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.CorpseVisualLease' 'get_BodyCount' }).Count -ne 1) {
        throw 'CorpseFeedback.AcceptRagdoll must reject a candidate without rigidbodies (get_BodyCount) instead of launching nothing.'
    }
    if ((Find-Indices $acceptIl { param($ins) $ins.OpCode.Name -eq 'stfld' -and $ins.Operand -is [Mono.Cecil.FieldReference] -and $ins.Operand.Name -eq 'Ragdoll' }).Count -ne 1) {
        throw 'CorpseFeedback.AcceptRagdoll must cache the accepted lease once (item.Ragdoll), so a kill resolves at most once.'
    }
    $resolveStrings = (@($resolveIl | Where-Object { $_.Operand -is [string] } | ForEach-Object { $_.Operand }) -join '')
    $acceptStrings = (@($acceptIl | Where-Object { $_.Operand -is [string] } | ForEach-Object { $_.Operand }) -join '')
    foreach ($resolveReceipt in @('ragdoll-at-death:', 'late-lookup:', 'no-rigidbody', 'no-ragdoll-instance', 'enemy-destroyed')) {
        if (-not $resolveStrings.Contains($resolveReceipt) -and -not $acceptStrings.Contains($resolveReceipt)) {
            throw "The ragdoll ladder must record $resolveReceipt, so an unavailable launch names where the search stopped."
        }
    }
    $apply = Get-Method $feedback 'ApplyLaunch'
    $applyIl = Get-Instructions $apply
    if ((Find-Indices $applyIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.CorpseVisualLease' 'Launch' }).Count -ne 1) {
        throw "CorpseFeedback.ApplyLaunch must apply the impulse to the leased ragdoll exactly once. IL: $(Show $applyIl)"
    }
    # The receipt is built by interpolation, so the literals are separate ldstr fragments: join them first.
    $applyLiterals = (@($applyIl | Where-Object { $_.Operand -is [string] } | ForEach-Object { $_.Operand }) -join '')
    foreach ($expected in @('launch=applied', 'budget=', 'appliedTravel=', 'speed=', 'distance=', 'capped=', 'collision=', 'attempts=', 'firstLookupAge=')) {
        if (-not $applyLiterals.Contains($expected)) {
            throw "The applied-launch receipt must report $expected so a real-machine log can be reconciled with the cfg."
        }
    }
    # Review C6: `budget` (configured/biased) and `appliedTravel` (the safety clamp) are two separate numbers.
    if ($applyLiterals.Contains(';travel=')) {
        throw 'The applied-launch receipt still prints the ambiguous travel=: it must print budget= and appliedTravel= (review C6).'
    }
    # Review C4: the applied receipt names where the ragdoll came from, never the give-up reason.
    if ((Find-Indices $applyIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.CorpseFeedback' 'UnavailableReason' }).Count -gt 0) {
        throw 'The launch=applied receipt must not use UnavailableReason (a success could print path=enemy-destroyed, review C4).'
    }
    if ((Find-Indices $applyIl { param($ins) $ins.Operand -is [Mono.Cecil.FieldReference] -and $ins.Operand.Name -eq 'RagdollSource' }).Count -lt 1) {
        throw 'The launch=applied receipt must print the real ragdoll provenance (item.RagdollSource, review C4).'
    }
    if ((Find-Indices $applyIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.FeedbackPolicy' 'LaunchSpeedFor' }).Count -ne 1) {
        throw 'CorpseFeedback.ApplyLaunch must derive the horizontal speed from the travel budget (FeedbackPolicy.LaunchSpeedFor).'
    }
    if ((Find-Indices $applyIl { param($ins) $ins.OpCode.Name -eq 'stfld' -and $ins.Operand -is [Mono.Cecil.FieldReference] -and $ins.Operand.Name -eq 'Speed' }).Count -ne 1) {
        throw 'CorpseFeedback.ApplyLaunch must store the derived speed in item.Speed once (so the clamp and the log share it).'
    }
    # Review C7: the speed the ragdoll receives must BE that stored value - a hard-coded 4.2f here would still be green
    # on a call-existence check alone.
    $launchCalls = Find-Indices $applyIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.CorpseVisualLease' 'Launch' }
    if ($launchCalls.Count -ne 1) {
        throw "CorpseFeedback.ApplyLaunch must apply the impulse to the leased ragdoll exactly once. IL: $(Show $applyIl)"
    }
    Assert-OperandBefore $applyIl $launchCalls[0] { param($ins) $ins.Operand -is [Mono.Cecil.FieldReference] -and $ins.Operand.Name -eq 'Speed' } 14 'CorpseVisualLease.Launch must be called with item.Speed as its speed argument (review C7).'
    if ((Find-Indices $applyIl { param($ins) $ins.OpCode.Name -eq 'ldc.r4' }).Count -gt 0) {
        throw 'ApplyLaunch must not pass a literal speed to Launch (the number has to come from the budget).'
    }
    # 1.2.16 (second real-machine pass): the whole launch decision lives in ONE method, so no inline branch can
    # fall through silently again (the real log had 47 accepted corpses and 0 launch= lines: a lease with
    # BodyCount == 0 satisfied neither the apply nor the give-up test).
    $decide = Get-Method $feedback 'TryLaunchOrAbandon'
    $decideIl = Get-Instructions $decide
    $unavailIl = Get-Instructions (Get-Method $feedback 'LogUnavailableLaunch')
    if ((Find-Indices $tickIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.CorpseFeedback' 'ApplyLaunch' }).Count -ne 0) {
        throw 'CorpseFeedback.Tick must not call ApplyLaunch directly: the launch decision has to stay inside TryLaunchOrAbandon.'
    }
    if ((Find-Indices $tickIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.CorpseFeedback' 'TryLaunchOrAbandon' }).Count -ne 1) {
        throw 'CorpseFeedback.Tick must run the launch decision through TryLaunchOrAbandon exactly once.'
    }
    # R1: exactly ONE impulse site. 1.2.16 had a second ApplyLaunch inside the late-lookup branch that bypassed
    # MaxLaunchApplications/ShouldReapplyLaunch, so a corpse whose lease kept going stale could be pushed on every
    # retry (up to MaxRagdollLookupAttempts = 8) while the log still printed `application=#N`.
    # R1 + B (1.2.18 review): the contract is "no impulse without the gate", not one textual call. The regular
    # decision and the bounded terminal probe are the only two allowed sites, and each must be preceded by its own
    # FeedbackPolicy.CanApplyLaunch evaluation - i.e. the same single gate (MaxLaunchApplications + object identity)
    # decides both, and the probe cannot become a second impulse path.
    $decideApply = @(Find-Indices $decideIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.CorpseFeedback' 'ApplyLaunch' })
    $decideGate = @(Find-Indices $decideIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.FeedbackPolicy' 'CanApplyLaunch' })
    if ($decideApply.Count -lt 1 -or $decideApply.Count -gt 2 -or $decideApply.Count -ne $decideGate.Count) {
        throw "TryLaunchOrAbandon must have exactly one FeedbackPolicy.CanApplyLaunch evaluation per ApplyLaunch site (1 regular + at most 1 terminal probe); found apply=$($decideApply.Count) gate=$($decideGate.Count) (review R1/B)."
    }
    for ($applyIndex = 0; $applyIndex -lt $decideApply.Count; $applyIndex++) {
        if ($decideGate[$applyIndex] -ge $decideApply[$applyIndex]) {
            throw "ApplyLaunch #$($applyIndex + 1) must be guarded by a FeedbackPolicy.CanApplyLaunch evaluation that comes BEFORE it (review R1/B). IL: $(Show $decideIl)"
        }
    }
    # ... and the gate itself has to own both halves of the decision. A CanApplyLaunch that only forwarded to
    # ShouldApplyLaunch would satisfy the call-site check above and still push forever.
    $gateIl = Get-Instructions (Get-Method $feedbackPolicy 'CanApplyLaunch')
    if ((Find-Indices $gateIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.FeedbackPolicy' 'ShouldApplyLaunch' }).Count -ne 1 -or
        (Find-Indices $gateIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.FeedbackPolicy' 'ShouldReapplyLaunch' }).Count -ne 1) {
        throw "FeedbackPolicy.CanApplyLaunch must combine ShouldApplyLaunch with ShouldReapplyLaunch: that pair is what bounds a corpse to MaxLaunchApplications impulses (review R1). IL: $(Show $gateIl)"
    }
    # ---- B (1.2.19 real-machine): only an ACTIVATED ragdoll may be pushed ----------------------------------
    # `launch=applied` moved nothing: the leased body had not been activated by the game yet, so the velocity was
    # discarded and the later Hide() read as "the corpse vanished where it stood". The compiled contract is:
    #   * FeedbackPolicy.CanApplyLaunch takes the activation fact as its second parameter and asks IsRagdollLive;
    #   * every call site in TryLaunchOrAbandon builds that argument from LeaseLive (the lease's own
    #     activeInHierarchy), so the fact cannot be a constant;
    #   * the impulse site is still unique, and the applied/unavailable receipts name live=/waited= and
    #     reason=ragdoll-inactive;
    #   * the EndAt stage evaluates ShouldHideCorpse BEFORE it hides the leases.
    $gateParams = @((Get-Method $feedbackPolicy 'CanApplyLaunch').Parameters)
    if ($gateParams.Count -ne 6 -or $gateParams[1].ParameterType.FullName -ne 'System.Boolean') {
        throw "FeedbackPolicy.CanApplyLaunch must take (RagdollLease lease, bool live, int applications, bool sameLease, float age, float burstAt); found $((@($gateParams | ForEach-Object { $_.ParameterType.FullName }) -join ','))."
    }
    if ((Find-Indices $gateIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.FeedbackPolicy' 'IsRagdollLive' }).Count -ne 1) {
        throw "FeedbackPolicy.CanApplyLaunch must gate every impulse on FeedbackPolicy.IsRagdollLive exactly once (fix B). IL: $(Show $gateIl)"
    }
    $liveIl = Get-Instructions (Get-Method $feedbackPolicy 'IsRagdollLive')
    if (@((Get-Method $feedbackPolicy 'IsRagdollLive').Parameters).Count -ne 1 -or
        (Get-Method $feedbackPolicy 'IsRagdollLive').Parameters[0].ParameterType.FullName -ne 'System.Boolean' -or
        @(Find-Indices $liveIl { param($ins) $ins.Operand -is [Mono.Cecil.MethodReference] }).Count -ne 0 -or
        @(Find-Indices $liveIl { param($ins) $ins.OpCode.Name -eq 'ret' }).Count -lt 1) {
        throw "FeedbackPolicy.IsRagdollLive must stay the one pure activation predicate (bool activeInHierarchy -> bool). IL: $(Show $liveIl)"
    }
    if (@((Get-Method $feedbackPolicy 'ShouldHideCorpse').Parameters).Count -ne 2) {
        throw 'FeedbackPolicy.ShouldHideCorpse must stay the (applications, ragdollInactive) pure decision the EndAt stage asks.'
    }
    foreach ($gateIndex in (Find-Indices $decideIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.FeedbackPolicy' 'CanApplyLaunch' })) {
        Assert-OperandBefore $decideIl $gateIndex { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.CorpseFeedback' 'LeaseLive' } 14 'Every CanApplyLaunch call site must pass LeaseLive(item) as the live argument - a literal would keep the inactive-ragdoll bug (fix B).'
    }
    if ((Find-Indices (Get-Instructions (Get-Method $feedback 'LeaseLive')) { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.FeedbackPolicy' 'IsRagdollLive' }).Count -ne 1 -or
        (Find-Indices (Get-Instructions (Get-Method $feedback 'LeaseLive')) { param($ins) $ins.Operand -is [Mono.Cecil.MethodReference] -and $ins.Operand.Name -eq 'get_Live' }).Count -ne 1) {
        throw 'CorpseFeedback.LeaseLive must read CorpseVisualLease.Live (activeInHierarchy) and route it through FeedbackPolicy.IsRagdollLive exactly once.'
    }
    $appliedReceiptStrings = (@((Get-Instructions (Get-Method $feedback 'ApplyLaunch')) | Where-Object { $_.Operand -is [string] } | ForEach-Object { $_.Operand }) -join '')
    $unavailableReceiptStrings = (@((Get-Instructions (Get-Method $feedback 'LogUnavailableLaunch')) | Where-Object { $_.Operand -is [string] } | ForEach-Object { $_.Operand }) -join '')
    foreach ($appliedReceipt in @('live=', 'waited=')) {
        if (-not $appliedReceiptStrings.Contains($appliedReceipt)) {
            throw "The launch=applied receipt must report $appliedReceipt, so the next real-machine log proves the body was live before it was pushed (fix B)."
        }
    }
    foreach ($unavailableReceipt in @('wait=', 'active=')) {
        if (-not $unavailableReceiptStrings.Contains($unavailableReceipt)) {
            throw "The launch=unavailable receipt must report $unavailableReceipt (a never-activated ragdoll says wait=/active=False; fix B)."
        }
    }
    $reasonIl = Get-Instructions (Get-Method $feedback 'UnavailableReason')
    $reasonStrings = (@($reasonIl | Where-Object { $_.Operand -is [string] } | ForEach-Object { $_.Operand }) -join '')
    if (-not $reasonStrings.Contains('ragdoll-inactive') -or
        (Find-Indices $reasonIl { param($ins) $ins.OpCode.Name -eq 'ldfld' -and $ins.Operand -is [Mono.Cecil.FieldReference] -and $ins.Operand.Name -eq 'RagdollInactive' }).Count -lt 1) {
        throw "CorpseFeedback.UnavailableReason must name reason=ragdoll-inactive from item.RagdollInactive (never the last search result) when the game never activated the body. IL: $(Show $reasonIl)"
    }
    # The Hide/Recycle decision has to be evaluated before the leases are touched, and the queue entry still leaves.
    $hideGateAt = @(Find-Indices $tickIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.FeedbackPolicy' 'ShouldHideCorpse' })
    $hideLeaseAt = @(Find-Indices $tickIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.CorpseVisualLease' 'Hide' })
    if ($hideGateAt.Count -ne 2 -or $hideLeaseAt.Count -lt 1 -or $hideGateAt[1] -gt $hideLeaseAt[0]) {
        throw "Preparation and EndAt must each decide ShouldHideCorpse before the leases are touched. IL: $(Show $tickIl)"
    }
    # One impulse site in the whole assembly: the velocity is only ever written from CorpseFeedback.ApplyLaunch.
    $launchSites = @()
    foreach ($scanMethod in $allMethods) {
        if ((Find-Indices (Get-Instructions $scanMethod) { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.CorpseVisualLease' 'Launch' }).Count -gt 0) {
            $launchSites += ($scanMethod.DeclaringType.FullName + '::' + $scanMethod.Name)
        }
    }
    if ($launchSites.Count -ne 1 -or $launchSites[0] -ne 'HawChow.GTFOKillFeed.CorpseFeedback::ApplyLaunch') {
        throw "CorpseVisualLease.Launch may be called from exactly one place (the live-gated impulse site CorpseFeedback::ApplyLaunch); found: $($launchSites -join ', ')"
    }
    # ---- R2/R3 (2nd review): the release branch owns fade/hide/recycle, the kept branch owns its receipts ---------
    $attemptAt = @(Find-Indices $tickIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.CorpseFadePrep' 'Attempt' })
    $fadeKeptAt = @(Find-Indices $tickIl { param($ins) $ins.OpCode.Name -eq 'ldstr' -and "$($ins.Operand)".Contains('fade=skipped;reason=corpse-kept-inactive') })
    $cleanupRecycledAt = @(Find-Indices $tickIl { param($ins) $ins.OpCode.Name -eq 'ldstr' -and "$($ins.Operand)".Contains('cleanup=physics-recycled') })
    $cleanupKeptAt = @(Find-Indices $tickIl { param($ins) $ins.OpCode.Name -eq 'ldstr' -and "$($ins.Operand)".Contains('cleanup=physics-kept') })
    $recycleAt = @(Find-Indices $tickIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.CorpseVisualLease' 'RecyclePhysics' })
    if ($attemptAt.Count -ne 2 -or $fadeKeptAt.Count -ne 1 -or $cleanupRecycledAt.Count -ne 1 -or $cleanupKeptAt.Count -ne 1) {
        throw "CorpseFeedback.Tick must keep two fade barriers and one of each terminal cleanup receipt. IL: $(Show $tickIl)"
    }
    foreach ($gateIndex in $hideGateAt) {
        Assert-OperandBefore $tickIl $gateIndex { param($ins) $ins.Operand -is [Mono.Cecil.FieldReference] -and $ins.Operand.Name -eq 'RagdollInactive' } 8 'Every fade/hide gate must consume the real RagdollInactive state.'
        Assert-OperandBefore $tickIl $gateIndex { param($ins) $ins.Operand -is [Mono.Cecil.FieldReference] -and $ins.Operand.Name -eq 'LaunchApplications' } 8 'Every fade/hide gate must consume the real launch count.'
    }
    $recycleInRelease = @($recycleAt | Where-Object { $_ -gt $hideGateAt[1] -and $_ -lt $cleanupRecycledAt[0] })
    if ($hideGateAt[0] -gt $attemptAt[0] -or $attemptAt[0] -gt $hideGateAt[1] -or
        $hideGateAt[1] -gt $attemptAt[1] -or $attemptAt[1] -gt $hideLeaseAt[0] -or
        $attemptAt[1] -gt $fadeKeptAt[0] -or $cleanupRecycledAt[0] -gt $cleanupKeptAt[0] -or $recycleInRelease.Count -ne 2) {
        throw "Both barriers must follow their own release gate; EndAt Hide/Recycle stays after its barrier, outside the kept branch. IL: $(Show $tickIl)"
    }
    # ---- R4 (2nd review): an inactive usable lease is a ladder retry reason -------------------------------------
    $inactiveRetryAt = @(Find-Indices $decideIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.FeedbackPolicy' 'ShouldRetryInactiveLease' })
    $lateLookupAt = @(Find-Indices $decideIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.CorpseFeedback' 'TryLateRagdoll' })
    if ($inactiveRetryAt.Count -ne 1 -or $lateLookupAt.Count -ne 1 -or $inactiveRetryAt[0] -gt $lateLookupAt[0]) {
        throw "CorpseFeedback.TryLaunchOrAbandon must ask FeedbackPolicy.ShouldRetryInactiveLease exactly once, BEFORE the single TryLateRagdoll call (so an inactive lease can still hand over a live replacement) (R4). IL: $(Show $decideIl)"
    }
    $inactiveReasonAt = @(Find-Indices $reasonIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.FeedbackPolicy' 'IsInactiveRagdollLaunch' })
    if ($inactiveReasonAt.Count -ne 1) {
        throw "CorpseFeedback.UnavailableReason must decide reason=ragdoll-inactive through FeedbackPolicy.IsInactiveRagdollLaunch (a lease that is usable but not live) (R5). IL: $(Show $reasonIl)"
    }
    # ---- R5 (2nd review): the inactive/wait state resets with every lease change --------------------------------
    $acceptIl = Get-Instructions (Get-Method $feedback 'AcceptRagdoll')
    $resetInactive = @(Find-Indices $acceptIl { param($ins) $ins.OpCode.Name -eq 'stfld' -and $ins.Operand -is [Mono.Cecil.FieldReference] -and $ins.Operand.Name -eq 'RagdollInactive' })
    $resetWait = @(Find-Indices $acceptIl { param($ins) $ins.OpCode.Name -eq 'stfld' -and $ins.Operand -is [Mono.Cecil.FieldReference] -and $ins.Operand.Name -eq 'RagdollWaitStart' })
    if ($resetInactive.Count -ne 1 -or $resetWait.Count -ne 1) {
        throw "CorpseFeedback.AcceptRagdoll must clear item.RagdollInactive and item.RagdollWaitStart exactly once when a NEW lease is taken, so an old wait never colours a new object's receipt (R5). IL: $(Show $acceptIl)"
    }

    $staleReset = @(Find-Indices $tickIl { param($ins) $ins.OpCode.Name -eq 'stfld' -and $ins.Operand -is [Mono.Cecil.FieldReference] -and $ins.Operand.Name -eq 'RagdollInactive' })
    if ($staleReset.Count -lt 1) {
        throw 'CorpseFeedback.Tick must clear item.RagdollInactive when the lease goes stale, so a discarded lease can never produce reason=ragdoll-inactive (R5).'
    }
    # F1 (1.2.18 review): Restore must clean up FIRST and unlock LAST - the cleanup re-closes the identity through
    # DeathLedger.Close, so an earlier Unlock would be undone and the next real death of a reused wrapper refused.
    $restoreIl = Get-Instructions (Get-Method $feedback 'Restore')
    $cleanupIdx = @(Find-Indices $restoreIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.CorpseFeedback' 'CleanupRestored' })
    $unlockIdx = @(Find-Indices $restoreIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.CorpseDeathLedger' 'Unlock' })
    if ($cleanupIdx.Count -ne 1 -or $unlockIdx.Count -ne 1 -or $cleanupIdx[0] -ge $unlockIdx[0]) {
        throw 'CorpseFeedback.Restore must call CleanupRestored (release hidden + Close) BEFORE CorpseDeathLedger.Unlock, and Unlock must live in the finally (review F1).'
    }
    $cleanupIl = Get-Instructions (Get-Method $feedback 'CleanupRestored')
    if (@(Find-Indices $cleanupIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.CorpseFeedback' 'Release' }).Count -lt 1 -or
        @(Find-Indices $cleanupIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.CorpseFeedback' 'RemoveQueued' }).Count -lt 1) {
        throw 'CorpseFeedback.CleanupRestored must still release the hidden corpse and remove the queued entries (both go through Release -> DeathLedger.Close) (review F1).'
    }
    $releaseIl = Get-Instructions (Get-Method $feedback 'Release')
    if (@(Find-Indices $releaseIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.CorpseDeathLedger' 'Close' }).Count -lt 1) {
        throw 'CorpseFeedback.Release must close the ledger entry (that is what makes the Unlock order matter, review F1).'
    }
    # F2 (rungs 2-4): both hierarchy directions are rejected. 1.2.19: the game's own RagdollInstance is NO LONGER
    # routed through this gate (see the rung-1 block further down) - the ancestor/descendant heuristic stays with
    # the arbitrary-hierarchy rungs, where a limb of the living enemy could otherwise be leased.
    $livingHostIl = Get-Instructions (Get-Method $feedback 'IsLivingModelHost')
    if (@(Find-Indices $livingHostIl { param($ins) Test-MethodOperand $ins 'UnityEngine.Transform' 'IsChildOf' }).Count -lt 2) {
        throw 'CorpseFeedback.IsLivingModelHost must reject BOTH the ancestors and the descendants of the living model (review F2): with only the ancestor test a limb of the living enemy could still be leased.'
    }
    $resolveIl = Get-Instructions (Get-Method $feedback 'ResolveRagdoll')
    if (@(Find-Indices $resolveIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.CorpseFeedback' 'IsLivingModelHost' }).Count -ne 0) {
        throw 'CorpseFeedback.ResolveRagdoll must not run the living-host heuristic over EnemyAgent.RagdollInstance (1.2.19 real-machine fix: that veto refused 3 of 9 usable ragdolls).'
    }
    if (@(Find-Indices $resolveIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.CorpseFeedback' 'IsExactLivingRoot' }).Count -ne 1) {
        throw 'CorpseFeedback.ResolveRagdoll must still refuse the living model/enemy root ITSELF through IsExactLivingRoot (rung 1, 1.2.19).'
    }
    # R1b: the post-deadline observation must sit before the close-out and be bounded by the policy predicate.
    $decideIl2 = Get-Instructions (Get-Method $feedback 'TryLaunchOrAbandon')
    $probeIdx = @(Find-Indices $decideIl2 { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.FeedbackPolicy' 'ShouldRunFinalRagdollProbe' })
    $closeIdx = @(Find-Indices $decideIl2 { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.CorpseFeedback' 'CloseLaunch' })
    if ($probeIdx.Count -ne 1 -or $closeIdx.Count -lt 1 -or $probeIdx[0] -ge $closeIdx[$closeIdx.Count - 1]) {
        throw 'CorpseFeedback.TryLaunchOrAbandon must run the bounded post-deadline probe before it closes the launch (review R1b).'
    }
    # R4 (1.2.18): the impulse addressee is the OBJECT pointer. The game's Capture() bumps the generation of the
    # SAME object on every re-capture, which made 1.2.16 treat our own re-capture as a new corpse and spend the
    # second impulse on the same physical ragdoll. The signature still carries both generations (callers cannot
    # drift), but the identity comparison itself must be the single pointer equality.
    $sameLeaseIl = Get-Instructions (Get-Method $feedback 'SameLease')
    if ((Find-Indices $sameLeaseIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.FeedbackPolicy' 'IsSameLaunchLease' }).Count -ne 1 -or
        (Find-Indices $sameLeaseIl { param($ins) $ins.OpCode.Name -eq 'ldfld' -and $ins.Operand -is [Mono.Cecil.FieldReference] -and $ins.Operand.Name -eq 'LaunchLeaseGeneration' }).Count -lt 1) {
        throw 'CorpseFeedback.SameLease must ask FeedbackPolicy.IsSameLaunchLease and still read the recorded generation (review R2/R4).'
    }
    $isSameMethod = Get-Method $feedbackPolicy 'IsSameLaunchLease'
    if ($isSameMethod.Parameters.Count -ne 4) {
        throw 'FeedbackPolicy.IsSameLaunchLease must keep the (pointer, generation, pointer, generation) contract so call sites cannot silently drift (R4).'
    }
    $isSameIl = Get-Instructions $isSameMethod
    $pointerCompare = (Find-Indices $isSameIl { param($ins) ($ins.OpCode.Name -eq 'ceq') -or ("$($ins.Operand)" -match 'IntPtr::op_(Equality|Inequality)') }).Count
    if ($pointerCompare -lt 1 -or
        (Find-Indices $isSameIl { param($ins) $ins.OpCode.Name -eq 'ldfld' }).Count -ne 0) {
        throw 'FeedbackPolicy.IsSameLaunchLease must compare the two OBJECT pointers and touch no field: generations are no longer part of the impulse identity (R4 1.2.18, covered by the R4 unit cases).'
    }
    if ((Find-Indices $decideIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.CorpseVisualLease' 'get_BodyCount' }).Count -ne 0) {
        throw 'TryLaunchOrAbandon must not compare BodyCount itself: ClassifyRagdoll/LeaseState owns that decision, so an empty lease can never skip both branches.'
    }
    # ShouldReportUnavailableLaunch lives in CloseLaunch (asserted below), which both removal paths call.
    foreach ($policyCall in @('ShouldReplaceRagdollLease', 'ShouldApplyLaunch', 'ShouldCloseAppliedLaunch', 'ShouldAbandonRagdollLookup')) {
        if ((Find-Indices $decideIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.FeedbackPolicy' $policyCall }).Count -lt 1) {
            throw "TryLaunchOrAbandon must decide through FeedbackPolicy.$policyCall so the smoke tests drive the production comparison."
        }
    }
    # Review C1: the retry and the give-up decision must be separate; a missing ragdoll is retried on every tick
    # inside the window and only then reported.
    if ((Find-Indices $decideIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.FeedbackPolicy' 'ShouldRetryRagdollLookup' }).Count -ne 1) {
        throw 'TryLaunchOrAbandon must gate the late ragdoll lookup through FeedbackPolicy.ShouldRetryRagdollLookup (review C1).'
    }
    if ((Find-Indices $decideIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.CorpseFeedback' 'TryLateRagdoll' }).Count -ne 1) {
        throw 'TryLaunchOrAbandon must keep exactly one late ragdoll lookup call (review C1).'
    }
    $retryCalls = Find-Indices $decideIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.FeedbackPolicy' 'ShouldRetryRagdollLookup' }
    $lookupCalls = Find-Indices $decideIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.CorpseFeedback' 'TryLateRagdoll' }
    if ($retryCalls[0] -gt $lookupCalls[0]) {
        throw 'The retry decision must precede the late lookup (otherwise the lookup can never run inside the window, review C1).'
    }
    # 1.2.16 real-machine fix: the give-up decision must NOT be the burst window any more (that is what closed
    # the retries after three frames / one stutter and produced `attempts=0`), and it must be its own predicate.
    if ((Find-Indices $decideIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.FeedbackPolicy' 'ShouldAbandonRagdollLookup' }).Count -ne 1) {
        throw 'TryLaunchOrAbandon must decide to give up through FeedbackPolicy.ShouldAbandonRagdollLookup (burst + grace), not through the burst window.'
    }
    if ((Find-Indices $decideIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.FeedbackPolicy' 'ShouldWaitForRagdoll' }).Count -ne 0) {
        throw 'TryLaunchOrAbandon must no longer end the ragdoll lookup at the burst point: a coroutine-spawned ragdoll needs the grace window (1.2.16 real-machine fix).'
    }
    $abandonCalls = Find-Indices $decideIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.FeedbackPolicy' 'ShouldAbandonRagdollLookup' }
    if ($abandonCalls[0] -lt $lookupCalls[0]) {
        throw 'The give-up receipt must be evaluated after the late lookup (otherwise a corpse is reported unavailable before its last attempt).'
    }
    # Closure: every removal path runs through the ONE closure helper - RemoveQueued here, and the EndAt stage
    # (asserted above). Both print the receipt before the entry leaves the queue.
    $removeIl = Get-Instructions (Get-Method $feedback 'RemoveQueued')
    $removeClosers = Find-Indices $removeIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.CorpseFeedback' 'CloseLaunch' }
    if ($removeClosers.Count -ne 1) {
        throw 'CorpseFeedback.RemoveQueued must close the launch state through CloseLaunch before the entry leaves the queue: no corpse may disappear without an applied/unavailable receipt.'
    }
    $decideStrings = (@($decideIl | Where-Object { $_.Operand -is [string] } | ForEach-Object { $_.Operand }) -join '')
    $unavailStrings = (@($unavailIl | Where-Object { $_.Operand -is [string] } | ForEach-Object { $_.Operand }) -join '')
    foreach ($receipt in @('ragdoll=late@attempts=', 'firstLookupAge=', 'ladder=', 'ladderHit=', 'lease-empty', 'no-bodies')) {
        if (-not $decideStrings.Contains($receipt)) {
            throw "The corpse launch decision must log $receipt so a real-machine log can tell a late ragdoll, a discarded lease and a give-up apart (1.2.16)."
        }
    }
    foreach ($receipt in @('launch=unavailable', 'attempts=', 'window=', 'haveRagdoll=', 'bodies=', 'ladderHit=')) {
        if (-not $unavailStrings.Contains($receipt)) {
            throw "The launch=unavailable receipt must report $receipt (1.2.16: no silent skip, and the rung facts are named)."
        }
    }
    $applyStrings = (@($applyIl | Where-Object { $_.Operand -is [string] } | ForEach-Object { $_.Operand }) -join '')
    foreach ($receipt in @('application=#', 'ladderHit=', 'ragdollBodies=')) {
        if (-not $applyStrings.Contains($receipt)) {
            throw "The launch=applied receipt must report $receipt so a second impulse on a replacement ragdoll can be told apart."
        }
    }
    # The attempt counter has to be a real one: incremented when - and only when - a lookup runs.
    if ((Find-Indices $decideIl { param($ins) $ins.OpCode.Name -eq 'stfld' -and $ins.Operand -is [Mono.Cecil.FieldReference] -and $ins.Operand.Name -eq 'RagdollLookupAttempts' }).Count -ne 1) {
        throw 'TryLaunchOrAbandon must increment item.RagdollLookupAttempts exactly once per executed lookup.'
    }
    if ((Find-Indices $decideIl { param($ins) $ins.OpCode.Name -eq 'stfld' -and $ins.Operand -is [Mono.Cecil.FieldReference] -and $ins.Operand.Name -eq 'FirstLookupAge' }).Count -ne 1) {
        throw 'TryLaunchOrAbandon must record the age of the FIRST late lookup once (firstLookupAge= in the receipt).'
    }
    # The empty-lease discard is what turns the old silent case into a retry, and the hit rung must be recorded.
    if ((Find-Indices $resolveIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.CorpseVisualLease' 'get_BodyCount' }).Count -ne 1) {
        throw 'CorpseFeedback.ResolveRagdoll must re-resolve when the cached lease has no bodies (1.2.16 returned early on Ragdoll != null alone).'
    }
    if ((Find-Indices $acceptIl { param($ins) $ins.OpCode.Name -eq 'stfld' -and $ins.Operand -is [Mono.Cecil.FieldReference] -and $ins.Operand.Name -eq 'LadderHit' }).Count -ne 1) {
        throw 'CorpseFeedback.AcceptRagdoll must record the rung that actually produced the lease (item.LadderHit), so ladder= and ladderHit= cannot be confused.'
    }
    # F1 moved the release/keep logic into CleanupRestored (so Restore can unlock the identity only afterwards);
    # the behaviour itself must still live in exactly one place.
    $restoreIl = Get-Instructions (Get-Method $feedback 'Restore')
    $cleanupIl2 = Get-Instructions (Get-Method $feedback 'CleanupRestored')
    if ((Find-Indices $cleanupIl2 { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.FeedbackPolicy' 'ShouldKeepCorpseForPendingLaunch' }).Count -ne 1 -or
        (Find-Indices $restoreIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.FeedbackPolicy' 'ShouldKeepCorpseForPendingLaunch' }).Count -ne 0) {
        throw 'CorpseFeedback.CleanupRestored must keep a corpse whose launch is still pending (the ragdoll lease is separate from the model lease) instead of cancelling it on an enemy despawn, and Restore must not duplicate that decision (review F1).'
    }
    # R3: the keep branch may only carry pure data + the ragdoll lease. The pool hands the same EnemyAgent wrapper
    # to the next spawn, so keeping Enemy/EnemyRoot/ModelRoot alive would let BreakLimbs/TryLeaseModel/NativeDecay/
    # FadeRenderer write to a LIVING enemy (limbs, renderers, kinematic bodies, Decay.Play).
    foreach ($handle in @('Enemy', 'EnemyRoot', 'ModelRoot', 'Model')) {
        if ((Find-Indices $cleanupIl2 { param($ins) $ins.OpCode.Name -eq 'stfld' -and $ins.Operand -is [Mono.Cecil.FieldReference] -and $ins.Operand.DeclaringType.FullName -eq 'HawChow.GTFOKillFeed.CorpseFeedback/Corpse' -and $ins.Operand.Name -eq $handle }).Count -lt 1) {
            throw "CorpseFeedback.CleanupRestored must drop item.$handle when it keeps a corpse for a pending launch: a kept handle can reach a living enemy (review R3/F1)."
        }
    }
    # R4: the EndAt stage removes its entry without RemoveQueued, so it has to close the launch itself through the
    # same helper - otherwise the only guarantee was EndAt(0.75 s) > lookup deadline (0.30 s), which nothing asserted.
    if ((Find-Indices $tickIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.CorpseFeedback' 'CloseLaunch' }).Count -ne 1) {
        throw 'CorpseFeedback.Tick must close the launch state in the EndAt stage (CloseLaunch) before Active.RemoveAt (review R4).'
    }
    $removeCalls = Find-Indices $tickIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.CorpseFeedback' 'CloseLaunch' }
    $activeRemoveCalls = Find-Indices $tickIl { param($ins) $ins.Operand -is [Mono.Cecil.MethodReference] -and $ins.Operand.Name -eq 'RemoveAt' -and $ins.Operand.DeclaringType.FullName -match 'System\.Collections\.Generic\.List' }
    if ($activeRemoveCalls.Count -lt 1 -or $removeCalls[0] -gt $activeRemoveCalls[$activeRemoveCalls.Count - 1]) {
        throw 'The EndAt closure must run before the entry leaves the active queue.'
    }
    $closeIl = Get-Instructions (Get-Method $feedback 'CloseLaunch')
    if ((Find-Indices $closeIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.FeedbackPolicy' 'ShouldReportUnavailableLaunch' }).Count -ne 1 -or
        (Find-Indices $closeIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.CorpseFeedback' 'LogUnavailableLaunch' }).Count -ne 1) {
        throw 'CorpseFeedback.CloseLaunch must be the one closure (ShouldReportUnavailableLaunch + LogUnavailableLaunch) shared by RemoveQueued and EndAt.'
    }
    # R5: closing the state and printing the receipt are one operation - otherwise `launch=unavailable` was followed
    # by `launch=pending` in the same removal line.
    $unavailIl = Get-Instructions (Get-Method $feedback 'LogUnavailableLaunch')
    if ((Find-Indices $unavailIl { param($ins) $ins.OpCode.Name -eq 'stfld' -and $ins.Operand -is [Mono.Cecil.FieldReference] -and $ins.Operand.Name -eq 'LaunchClosed' }).Count -ne 1) {
        throw 'CorpseFeedback.LogUnavailableLaunch must set LaunchClosed, so an unavailable launch can never be reported as pending afterwards (review R5).'
    }
    # R8: `ladder=`/`lookupWindow=` must be values, not literals. The death-call receipt has to take the ladder from
    # LadderTried (never empty) and the window from the policy deadline.
    foreach ($valueSource in @(@{ Type = 'HawChow.GTFOKillFeed.CorpseFeedback'; Name = 'LadderTried' }, @{ Type = 'HawChow.GTFOKillFeed.FeedbackPolicy'; Name = 'RagdollLookupDeadline' })) {
        if ((Find-Indices $scheduleIl { param($ins) Test-MethodOperand $ins $valueSource.Type $valueSource.Name }).Count -lt 1) {
            throw "The death-call receipt must build ladder=/lookupWindow= from $($valueSource.Name), not from a literal (review R8)."
        }
    }
    if ((Find-Indices (Get-Instructions (Get-Method $feedback 'LadderTried')) { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.FeedbackPolicy' 'LadderText' }).Count -ne 1) {
        throw 'CorpseFeedback.LadderTried must answer through FeedbackPolicy.LadderText, so ladder= can never be an empty field (review R8).'
    }
    $limitCalls = Find-Indices $tickIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.CorpseVisualLease' 'Limit' }
    if ($limitCalls.Count -ne 1) {
        throw 'CorpseFeedback.Tick must keep the position/velocity safety net (CorpseVisualLease.Limit) exactly once.'
    }
    # Review C7: both Limit arguments must come from the policy - the radius from AppliedTravel and the cap from
    # LaunchVelocityCap - not from a literal.
    Assert-OperandBefore $tickIl $limitCalls[0] { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.FeedbackPolicy' 'AppliedTravel' } 24 'Limit(maxDistance) must come from FeedbackPolicy.AppliedTravel (review C7).'
    Assert-OperandBefore $tickIl $limitCalls[0] { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.FeedbackPolicy' 'LaunchVelocityCap' } 24 'Limit(maxSpeed) must come from FeedbackPolicy.LaunchVelocityCap (review C7).'
    $leaseType = $module.GetType('HawChow.GTFOKillFeed.CorpseVisualLease')
    $launchSignature = ((@((Get-Method $leaseType 'Launch').Parameters) | ForEach-Object { $_.ParameterType.FullName }) -join '|')
    if ($launchSignature -ne 'UnityEngine.Vector3|System.Single|System.Single') {
        throw "CorpseVisualLease.Launch must take (direction, strength, speed) so the budget decides the speed; found $launchSignature."
    }
    $limitSignature = ((@((Get-Method $leaseType 'Limit').Parameters) | ForEach-Object { $_.ParameterType.FullName }) -join '|')
    if ($limitSignature -ne 'System.Single|System.Single') {
        throw "CorpseVisualLease.Limit must take (maxDistance, maxSpeed) so the old flat 9 m/s cap cannot flatten the budget; found $limitSignature."
    }
    $launchIl = Get-Instructions (Get-Method $leaseType 'Launch')
    if ((Find-Indices $launchIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.FeedbackPolicy' 'LaunchSpeed' }).Count -gt 0) {
        throw 'CorpseVisualLease.Launch must no longer use the fixed FeedbackPolicy.LaunchSpeed(strength): the speed comes from the budget.'
    }
    # Review C3: a high-speed body must not move 0.73 m per Discrete step. The mode is decided by the policy,
    # applied in Launch and restored on release.
    if ((Find-Indices (Get-Instructions (Get-Method $leaseType 'Capture')) { param($ins) Test-MethodOperand $ins 'UnityEngine.Rigidbody' 'get_collisionDetectionMode' }).Count -ne 1) {
        throw 'CorpseVisualLease.Capture must record the original collisionDetectionMode (review C3).'
    }
    if ((Find-Indices $launchIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.FeedbackPolicy' 'NeedsContinuousCollision' }).Count -ne 1) {
        throw 'CorpseVisualLease.Launch must ask FeedbackPolicy.NeedsContinuousCollision before switching the mode (review C3).'
    }
    if ((Find-Indices $launchIl { param($ins) Test-MethodOperand $ins 'UnityEngine.Rigidbody' 'set_collisionDetectionMode' }).Count -ne 1) {
        throw 'CorpseVisualLease.Launch must set the collision detection mode once per body (review C3).'
    }
    $restoreIl = Get-Instructions (Get-Method $leaseType 'RestoreState')
    if ((Find-Indices $restoreIl { param($ins) Test-MethodOperand $ins 'UnityEngine.Rigidbody' 'set_collisionDetectionMode' }).Count -ne 1) {
        throw 'CorpseVisualLease.RestoreState must put the captured collision detection mode back (review C3).'
    }
    # Hardening: the non-continuous branch must restore the mode the lease CAPTURED, not a hard-coded Discrete
    # (a pooled ragdoll may ship with ContinuousSpeculative). One accessor is the only source for both paths.
    $capturedMethod = @($leaseType.Methods | Where-Object { $_.Name -eq 'CapturedDetection' })
    if ($capturedMethod.Count -ne 1) {
        throw 'CorpseVisualLease must expose exactly one CapturedDetection(int) accessor for the captured mode.'
    }
    $capturedSignatureBad = $capturedMethod[0].ReturnType.FullName -ne 'UnityEngine.CollisionDetectionMode' -or
        $capturedMethod[0].Parameters.Count -ne 1 -or
        $capturedMethod[0].Parameters[0].ParameterType.FullName -ne 'System.Int32'
    if ($capturedSignatureBad) {
        throw "CorpseVisualLease.CapturedDetection must stay (int) -> CollisionDetectionMode; found $($capturedMethod[0].ReturnType.FullName)."
    }
    if ((Find-Indices $launchIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.CorpseVisualLease' 'CapturedDetection' }).Count -ne 1) {
        throw 'CorpseVisualLease.Launch must use the captured collision detection mode on the non-continuous path (review hardening: no hard-coded Discrete).'
    }
    if ((Find-Indices $restoreIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.CorpseVisualLease' 'CapturedDetection' }).Count -ne 1) {
        throw 'CorpseVisualLease.RestoreState must restore through the same CapturedDetection accessor.'
    }
    # Only the continuous branch may be a literal: ContinuousDynamic == 2 (Discrete == 0 must never be pushed
    # straight into the setter on that path).
    $discreteAssignments = Find-Indices $launchIl {
        param($ins)
        $ins.OpCode.Name -eq 'callvirt' -and (Test-MethodOperand $ins 'UnityEngine.Rigidbody' 'set_collisionDetectionMode')
    }
    if ($discreteAssignments.Count -ne 1) { throw 'CorpseVisualLease.Launch must assign the collision detection mode exactly once.' }
    $beforeSetter = $launchIl[[Math]::Max(0, $discreteAssignments[0] - 1)]
    if ($beforeSetter.OpCode.Name -eq 'ldc.i4.0') {
        throw 'CorpseVisualLease.Launch pushes a hard-coded Discrete (ldc.i4.0) straight into collisionDetectionMode; the captured mode belongs there.'
    }
    if ((Find-Indices $launchIl { param($ins) $ins.OpCode.Name -eq 'ldc.i4.2' }).Count -ne 1) {
        throw 'CorpseVisualLease.Launch must use exactly one literal mode: ContinuousDynamic (ldc.i4.2) for the fast flight.'
    }
    # Review C2: the snap-back radius check is one policy call, so a natural fall-over is never pinned mid-air.
    $limitIl = Get-Instructions (Get-Method $leaseType 'Limit')
    if ((Find-Indices $limitIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.FeedbackPolicy' 'ShouldClampTravel' }).Count -ne 1) {
        throw 'CorpseVisualLease.Limit must decide the snap-back through FeedbackPolicy.ShouldClampTravel (review C2).'
    }
    foreach ($budgetConstant in @(@{ Name = 'MinLaunchDistanceMetres'; Value = 0.5 }, @{ Name = 'MaxLaunchDistanceMetres'; Value = 30 }, @{ Name = 'AbsoluteDistanceCeiling'; Value = 40 }, @{ Name = 'MaxLaunchSpeed'; Value = 42 }, @{ Name = 'MinTravelClampMetres'; Value = 2.5 }, @{ Name = 'MaxDiscreteTravelPerStep'; Value = 0.25 }, @{ Name = 'RagdollLookupGrace'; Value = 0.3 })) {
        $field = $module.GetType('HawChow.GTFOKillFeed.FeedbackPolicy').Fields | Where-Object { $_.Name -eq $budgetConstant.Name }
        if ($null -eq $field -or -not $field.HasConstant -or [Math]::Abs([single]$field.Constant - [single]$budgetConstant.Value) -gt 0.0001) {
            throw "FeedbackPolicy.$($budgetConstant.Name) must stay a compile-time constant $($budgetConstant.Value)."
        }
    }
    # ---------------------------------------------------------------- 1.2.16 #2: native corpse-own Decay
    # The dissolve must be reachable WITHOUT a pooled borrow (that is the reference mod's shape, and the only
    # one that also works on a corpse the game never gave a ragdoll). TryNative is that path: named policy
    # decision, then the readiness fact + Decay.Play + the m_playing proof. It never borrows, returns, destroys -
    # and 1.2.19 it never initialises either (readiness is m_targetRenderer; the game arms the component itself).
    $tryNative = @($fade.Methods | Where-Object { $_.Name -eq 'TryNative' })
    if ($tryNative.Count -ne 1) { throw 'CorpseFade must keep exactly one TryNative method (the corpse-own Decay path).' }
    $nativeIl = Get-Instructions $tryNative[0]
    if ((Find-Indices $nativeIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.CorpseFadePolicy' 'NativePath' }).Count -ne 1) {
        throw 'CorpseFade.TryNative must ask CorpseFadePolicy.NativePath before touching the component (every refusal has to be named).'
    }
    # 1.2.19: readiness replaces initialisation. TryNative must consult FadeRendererCast.HasTarget exactly once and
    # still call Decay.Play(float) exactly once; the native Initialize call is gone from the whole plugin.
    $nativeReady = Find-Indices $nativeIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.FadeRendererCast' 'HasTarget' }
    $nativePlay = Find-Indices $nativeIl { param($ins) Test-MethodOperand $ins 'Decay' 'Play' }
    if (@($nativeReady).Count -ne 1 -or @($nativePlay).Count -ne 1) {
        throw "CorpseFade.TryNative must check FadeRendererCast.HasTarget (the game armed it) and call Decay.Play(float) exactly once; found HasTarget=$(@($nativeReady).Count), Play=$(@($nativePlay).Count)."
    }
    if ((Find-Indices $nativeIl { param($ins) $ins.Operand -is [Mono.Cecil.MethodReference] -and $ins.Operand.Name -in @('get_m_playing', 'm_playing') -and $ins.Operand.DeclaringType.FullName -eq 'Decay' }).Count -lt 2) {
        throw 'CorpseFade.TryNative must read Decay.m_playing before AND after Play: that flag is the only proof the dissolve really started.'
    }
    if ((Find-Indices $nativeIl { param($ins) Test-MethodOperand $ins 'CorpseFadeCoordinator' 'TryStart' }).Count -ne 0) {
        throw 'CorpseFade.TryNative must never borrow from the pool: the corpse-own component is not a pooled instance.'
    }
    # R6: the refusal receipt must not depend on having read a component. 1.2.16 wrapped the log in
    # `native != null`, so `reason=no-decay-component` was unreachable even though the policy called it reportable.
    $refusalLog = @($fade.Methods | Where-Object { $_.Name -eq 'LogNativeUnavailable' })
    if ($refusalLog.Count -ne 1) { throw 'CorpseFade must keep exactly one LogNativeUnavailable helper (the named native refusal).' }
    if ((Find-Indices $nativeIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.CorpseFade' 'LogNativeUnavailable' }).Count -ne 1) {
        throw 'CorpseFade.TryNative must report a refused native path through LogNativeUnavailable exactly once (review R6).'
    }
    $refusalIl = Get-Instructions $refusalLog[0]
    if ((Find-Indices $refusalIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.CorpseFadePolicy' 'ShouldLogNativeRefusal' }).Count -ne 1) {
        throw 'CorpseFade.LogNativeUnavailable must gate on CorpseFadePolicy.ShouldLogNativeRefusal, which is independent of the component (review R6).'
    }
    if ((Find-Indices $refusalIl { param($ins) $ins.OpCode.Name -eq 'ldnull' }).Count -ne 0 -or
        (Find-Indices $refusalIl { param($ins) $ins.OpCode.Name -eq 'ceq' }).Count -ne 0) {
        throw 'CorpseFade.LogNativeUnavailable must not compare anything to null: that is exactly what hid reason=no-decay-component (review R6).'
    }
    # The end-of-life target must not depend on the ragdoll lease any more: FadeRenderer falls back to the
    # snapshotted hierarchy, which is what a ragdoll-less corpse needs.
    $fadeRendererIl = Get-Instructions (Get-Method $feedback 'FadeRenderer')
    if ((Find-Indices $fadeRendererIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.CorpseFeedback' 'HierarchyRenderer' }).Count -lt 1) {
        throw 'CorpseFeedback.FadeRenderer must fall back to the snapshotted hierarchy (a corpse without a ragdoll had no renderer at all in 1.2.16).'
    }
    # The component is resolved by a helper and handed to CorpseFade.TryBegin. Both calls live in the EndAt
    # lambda (a compiler-generated nested type), so they are located across CorpseFeedback and its nested types.
    $nativeDecayCalls = 0
    $tryBeginSeesDecay = $false
    $feedbackMethods = @($feedback.Methods)
    foreach ($nested in $feedback.NestedTypes) { $feedbackMethods += @($nested.Methods) }
    foreach ($candidate in $feedbackMethods) {
        if (-not $candidate.HasBody) { continue }
        $candidateIl = Get-Instructions $candidate
        $decayCalls = Find-Indices $candidateIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.CorpseFeedback' 'NativeDecay' }
        $nativeDecayCalls += $decayCalls.Count
        if ($decayCalls.Count -gt 0 -and (Find-Indices $candidateIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.CorpseFade' 'TryBegin' }).Count -gt 0) { $tryBeginSeesDecay = $true }
    }
    if ($nativeDecayCalls -ne 2 -or -not $tryBeginSeesDecay) {
        throw "Both pre-cleanup and EndAt lambdas must resolve NativeDecay and pass it to TryBegin; found $nativeDecayCalls call(s), sameLambda=$tryBeginSeesDecay."
    }
    # Read-only lookup only: the component is fetched, never created.
    $nativeDecayIl = Get-Instructions (Get-Method $feedback 'NativeDecay')
    if ((Find-Indices $nativeDecayIl { param($ins) Test-MethodOperand $ins 'UnityEngine.Component' 'GetComponentInChildren' }).Count -lt 1 -and
        (Find-Indices $nativeDecayIl { param($ins) $ins.Operand -is [Mono.Cecil.MethodReference] -and $ins.Operand.Name -eq 'GetComponentInChildren' }).Count -lt 1) {
        throw 'CorpseFeedback.NativeDecay must look the component up on the snapshotted hierarchy (GetComponentInChildren).'
    }
    foreach ($nativeField in @('m_stopAction', 'm_autoCleanup')) {
        if ((Find-Indices $nativeIl { param($ins) $ins.Operand -is [Mono.Cecil.MethodReference] -and $ins.Operand.Name -eq "get_$nativeField" -and $ins.Operand.DeclaringType.FullName -eq 'Decay' }).Count -ne 1) {
            throw "CorpseFade.TryNative must read Decay.${nativeField}: a stop action that would take the corpse with it must be refused."
        }
    }

    # ---- 1.2.18 (fade root cause): renderer type must be resolved with TryCast, never with `is` ---------------
    # GetComponentsInChildren<Renderer>(true) hands back elements wrapped as UnityEngine.Renderer (Il2CppArrayBase<T>
    # ::op_Implicit -> WrapElement -> typeof(T).ctor(IntPtr)), so `is SkinnedMeshRenderer` is ALWAYS false and the
    # native Decay.Initialize (only a SkinnedMeshRenderer and a MeshRenderer overload exist) was never reached:
    # that is the real log's 3/3 `fade=failed;reason=initialize-failed`.
    $isinstDerived = 0
    $isinstWhere = ''
    foreach ($scanType in $module.Types) {
        foreach ($scanMethod in $scanType.Methods) {
            if (-not $scanMethod.HasBody) { continue }
            foreach ($scanInstruction in $scanMethod.Body.Instructions) {
                if ($scanInstruction.OpCode.Name -ne 'isinst') { continue }
                $scanned = "$($scanInstruction.Operand)"
                if ($scanned -match '(^|\.)SkinnedMeshRenderer$' -or $scanned -match '(^|\.)MeshRenderer$') {
                    $isinstDerived++
                    $isinstWhere = "$($scanType.FullName)::$($scanMethod.Name)"
                }
            }
        }
    }
    if ($isinstDerived -ne 0) {
        throw "no code may decide a renderer's native type with isinst (found $isinstDerived at $isinstWhere): the wrapper is always UnityEngine.Renderer, so only TryCast<T>() can answer (1.2.18 fade root cause)."
    }
    $resolverType = $module.GetType('HawChow.GTFOKillFeed.FadeRendererCast')
    if ($null -eq $resolverType) { throw 'FadeRendererCast is missing: the renderer type decision must live in exactly one place.' }
    $resolverIl = Get-Instructions (Get-Method $resolverType 'TryResolve')
    foreach ($nativeName in @('SkinnedMeshRenderer', 'MeshRenderer')) {
        $castHits = Find-Indices $resolverIl { param($ins) ($ins.OpCode.Name -eq 'call' -or $ins.OpCode.Name -eq 'callvirt') -and "$($ins.Operand)" -match ("TryCast<UnityEngine\." + $nativeName + ">\(\)$") }
        if (@($castHits).Count -lt 1) {
            throw "FadeRendererCast.TryResolve must resolve $nativeName through Il2CppObjectBase.TryCast<UnityEngine.$nativeName>() (il2cpp_class_is_assignable_from), not through a managed cast."
        }
    }
    if (@(Find-Indices $resolverIl { param($ins) $ins.OpCode.Name -eq 'ldfld' -and "$($ins.Operand)" -match 'CorpseVisualLease' }).Count -ne 0 -or
        @(Find-Indices $resolverIl { param($ins) $ins.OpCode.Name -eq 'call' -and "$($ins.Operand)" -match 'op_Equality' }).Count -lt 1) {
        throw 'FadeRendererCast.TryResolve must test the Unity fake-null (Renderer op_Equality) before casting and must not reach into lease internals.'
    }
    # 1.2.20: the arm is allowed again, but only inside DecayFadeTarget::Ready - the native call that threw in 1.2.18
    # is now guarded by a read-back identity proof and an exactly-once hand-back.
    $initializeSites = @()
    foreach ($scanMethod in $allMethods) {
        foreach ($scanInstruction in $scanMethod.Body.Instructions) {
            $scanOperand = $scanInstruction.Operand
            if ($scanOperand -is [Mono.Cecil.MethodReference] -and $scanOperand.Name -eq 'Initialize' -and
                $null -ne $scanOperand.DeclaringType -and $scanOperand.DeclaringType.FullName -eq 'Decay') {
                $initializeSites += ($scanMethod.DeclaringType.FullName + '::' + $scanMethod.Name)
            }
        }
    }
    $initializeCalls = $initializeSites.Count
    $strayInitialize = @($initializeSites | Where-Object { $_ -ne 'HawChow.GTFOKillFeed.CorpseFade/DecayFadeTarget::Ready' })
    if ($initializeCalls -ne 2 -or $strayInitialize.Count -ne 0) {
        throw "Decay::Initialize may only be called from DecayFadeTarget::Ready, once per renderer overload (found $initializeCalls site(s): $($initializeSites -join ', '))."
    }
    # F2 (1.2.19): the corpse-own path is the ONLY one that may ask the readiness fact and play a Decay.
    $nativePathIl = Get-Instructions (Get-Method $fade 'TryNative')
    if (@(Find-Indices $nativePathIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.FadeRendererCast' 'HasTarget' }).Count -ne 1) {
        throw "CorpseFade.TryNative must ask the one readiness fact (FadeRendererCast.HasTarget) before playing the corpse-own Decay."
    }
    if (@($targetType.Methods | Where-Object { $_.Name -eq 'Initialize' }).Count -ne 0) {
        throw 'DecayFadeTarget must not expose an Initialize method any more: nothing may arm a pooled Decay (1.2.19 fade NRE root cause).'
    }
    # F2: the ONLY Decay.Play call sites are TryNative (corpse-own, readiness-gated) and the pooled adapter's Play,
    # which the constant-false Ready gate makes unreachable. Nothing else in the assembly may play a Decay.
    $playSites = @()
    foreach ($scanMethod in $allMethods) {
        foreach ($scanInstruction in $scanMethod.Body.Instructions) {
            $scanOperand = $scanInstruction.Operand
            if ($scanOperand -is [Mono.Cecil.MethodReference] -and $scanOperand.Name -eq 'Play' -and
                $null -ne $scanOperand.DeclaringType -and $scanOperand.DeclaringType.FullName -eq 'Decay') {
                $playSites += ($scanMethod.DeclaringType.FullName + '::' + $scanMethod.Name)
            }
        }
    }
    $unexpectedPlay = @($playSites | Where-Object { $_ -ne 'HawChow.GTFOKillFeed.CorpseFade::TryNative' -and $_ -ne 'HawChow.GTFOKillFeed.CorpseFade/DecayFadeTarget::Play' })
    if ($playSites.Count -ne 2 -or $unexpectedPlay.Count -ne 0) {
        throw "Decay.Play may only appear in TryNative and in the pooled adapter (reached only after ArmVerdict.Armed); found: $($playSites -join ', ')"
    }
    # The renderer preference used by the fade target must use the same resolver (it had never preferred a skinned renderer).
    $leaseType = $module.GetType('HawChow.GTFOKillFeed.CorpseVisualLease')
    $feedbackType = $module.GetType('HawChow.GTFOKillFeed.CorpseFeedback')
    if ($null -eq $leaseType -or $null -eq $feedbackType) { throw 'CorpseVisualLease / CorpseFeedback types are missing.' }
    foreach ($picker in @(@{ Type = $leaseType; Name = 'PrimaryRenderer' }, @{ Type = $feedbackType; Name = 'HierarchyRenderer' })) {
        if (@(Find-Indices (Get-Instructions (Get-Method $picker.Type $picker.Name)) { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.FadeRendererCast' 'IsSkinned' }).Count -lt 1) {
            throw "$($picker.Type.Name).$($picker.Name) must use FadeRendererCast.IsSkinned: `is SkinnedMeshRenderer` never matched on a Renderer wrapper."
        }
    }
    $resolveIl2 = Get-Instructions (Get-Method $feedback 'ResolveRagdoll')
    if (@(Find-Indices $resolveIl2 { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.CorpseFeedback' 'IsLivingModelHost' }).Count -ne 0) {
        throw 'CorpseFeedback.ResolveRagdoll (rung 1) must not call IsLivingModelHost: the game parents its ragdoll under the model after a death (1.2.19 real-machine fix).'
    }
    if (@(Find-Indices $resolveIl2 { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.FeedbackPolicy' 'ClassifyRagdollInstance' }).Count -ne 1 -or
        @(Find-Indices $resolveIl2 { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.FeedbackPolicy' 'AcceptRagdollInstance' }).Count -lt 2) {
        throw 'CorpseFeedback.ResolveRagdoll must classify rung 1 through FeedbackPolicy.ClassifyRagdollInstance and honour AcceptRagdollInstance.'
    }
    if (@(Find-Indices $searchIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.CorpseFeedback' 'IsLivingModelHost' }).Count -ne 3) {
        throw 'CorpseFeedback.SearchRagdollRoot must keep the living-host gate on exactly rungs 2-4 (RagdollScript / EnemyRagdollRef / raw Rigidbody).'
    }
    # ---- the single terminal receipt: one fade= line per corpse, the pooled skip is named (F2), and the terminal
    # allowance is its own budget claimed once per scheduleId (F5) ---------------------------------------------
    $failureIl = Get-Instructions (Get-Method $fade 'LogFailure')
    $failureStrings = (@($failureIl | Where-Object { $_.Operand -is [string] } | ForEach-Object { $_.Operand }) -join '')
    $terminalPrints = @(Find-Indices $failureIl { param($ins) $ins.Operand -is [Mono.Cecil.MethodReference] -and $ins.Operand.Name -eq 'LogInfo' })
    if (-not $failureStrings.Contains('fade=skipped;reason=') -or
        -not $failureStrings.Contains('note=pooled-instance-unverifiable') -or
        -not $failureStrings.Contains('fade=failed;reason=') -or
        $terminalPrints.Count -ne 2) {
        throw "CorpseFade.LogFailure must print exactly two terminal fade= lines (fade=skipped;reason=pool-unverifiable;note=pooled-instance-unverifiable for the fail-closed pool, fade=failed for a real failure); found $($terminalPrints.Count) LogInfo call(s)."
    }
    # F5: every terminal line is claimed exactly once per scheduleId, and a receipt (fallback/native-unavailable)
    # never claims one - so `fade=fallback` can no longer eat a corpse's terminal budget.
    $claimSites = @()
    foreach ($terminalSite in @('TryNative', 'LogStarted', 'LogStartedUnknown', 'LogSkip', 'LogFailure')) {
        $siteIl = Get-Instructions (Get-Method $fade $terminalSite)
        $claimSites += @(Find-Indices $siteIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.CorpseFadeDiagnostics' 'ClaimTerminal' }).Count
    }
    $receiptClaims = 0
    foreach ($receiptSite in @('LogFallback', 'LogNativeUnavailable')) {
        $receiptIl = Get-Instructions (Get-Method $fade $receiptSite)
        $receiptClaims += @(Find-Indices $receiptIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.CorpseFadeDiagnostics' 'ClaimTerminal' }).Count
        if (@(Find-Indices $receiptIl { param($ins) Test-MethodOperand $ins 'HawChow.GTFOKillFeed.CorpseFadeDiagnostics' 'AllowLine' }).Count -ne 1) {
            throw "CorpseFade.$receiptSite must spend the bounded reason budget (AllowLine) exactly once and never the terminal allowance (F5)."
        }
    }
    if (($claimSites -join ',') -ne '1,1,1,1,2' -or $receiptClaims -ne 0) {
        throw "The terminal allowance must be claimed exactly once by TryNative/LogStarted/LogStartedUnknown/LogSkip and twice by LogFailure's two branches, and never by a receipt; found claims=$($claimSites -join ','), receiptClaims=$receiptClaims (F5)."
    }
    $diagnosticsType = $module.GetType('HawChow.GTFOKillFeed.CorpseFadeDiagnostics')
    if ($null -eq $diagnosticsType) { throw 'CorpseFadeDiagnostics is missing: the terminal allowance lives there (F5).' }
    $maxTerminal = $diagnosticsType.Fields | Where-Object { $_.Name -eq 'MaxTerminalPerLevel' }
    if ($null -eq $maxTerminal -or -not $maxTerminal.HasConstant -or [int]$maxTerminal.Constant -lt 8) {
        throw 'CorpseFadeDiagnostics.MaxTerminalPerLevel must stay a compile-time constant >= 8 (F5: the first corpses of a level always print their terminal fade= line).'
    }
    Write-Host "CORPSE_FADE_IL=PASS;DESTROY_CALLS=0;POOL_CREATION=0;MATERIAL_BLOCKS=0;TIMESCALE_WRITES=0;PATCHED_TYPES=$($patchTargets.Count);ES_PATCHES=$($esPatches.Count);DECAY_INITIALIZE_CALLS=$initializeCalls(only-DecayFadeTarget::Ready);NATIVE_PATH=corpse-own-Decay(HasTarget/Play/m_playing);POOL=arm-then-verify(borrow+Initialize+m_targetRenderer-proof+ArmVerdict,POOL_REFUSALS=pool-unverifiable|pool-arm-failed,PLAY_SITES=$($playSites.Count));RUNG1=trusted-RagdollInstance(no-living-host-gate);RUNG2_4=living-host-gated;NATIVE_REFUSALS=Reportable(no-decay-component|unsafe-decay-component|decay-already-playing);TERMINAL=own-budget(>=8/level,1-per-scheduleId);SWEEP=Probe-only;ENDAT=ShouldHideCorpse->{CorpseFadePrep.Attempt+Hide+Recycle}|kept{fade=skipped;reason=corpse-kept-inactive+cleanup=physics-kept}+CloseLaunch+RemoveAt;LAUNCH=TryLaunchOrAbandon(ClassifyRagdoll)+apply@0.02s+singleGate(CanApplyLaunch:live=activeInHierarchy,pointer,max2)+inactiveRetry(ShouldRetryInactiveLease->TryLateRagdoll)+stateReset(new/stale/discard)+closureInCloseLaunch(RemoveQueued|EndAt)+grace0.30s+speedFromBudget(0.5-30m)+cap42+inactiveStaysPending(reason=ragdoll-inactive,corpseKept);DECAY_DRIVERS=CorpseFade-only"
}
finally { $assembly.Dispose() }
