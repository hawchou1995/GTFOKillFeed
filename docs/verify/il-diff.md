# IL difference inventory

- original: `orig/BepInEx/plugins/GTFOKillFeed/GTFOKillFeed.dll`
- rebuilt:  `src/bin/Release/GTFOKillFeed.dll`
- methods: exact=818, different=169, unmatched=0
- differing methods backed by an executable comparison: 34
- differing methods NOT backed by execution (unverified): 135

Categories describe *what differs*. They are not a proof of behavioural equivalence;
a category only says how the instruction stream differs. Only G7 execution results count as
evidence of equal behaviour, and G7 covers a subset of methods.

| category | methods |
|---|---|
| branch+temp | 39 |
| branch-comparison | 27 |
| codegen-only | 2 |
| enumeration-shape | 9 |
| extra-cast | 14 |
| layout | 15 |
| operand-only | 4 |
| other-shape | 34 |
| string-formatting | 25 |

## Differing methods

- `HawChow.GTFOKillFeed.BurstHistory|System.Void Add(System.Int64,System.Single,System.Single)` - branch+temp - UNVERIFIED
    `+ldnull `
    `+stlocL `
    `+ldlocL `
    `+stlocL `
- `HawChow.GTFOKillFeed.CorpseFadeDiagnostics|System.Int32 Count(System.String)` - branch+temp - UNVERIFIED
    `+ldc.i4.0 `
    `+stlocL `
- `HawChow.GTFOKillFeed.CorpseFade|System.Void LogFailure(HawChow.GTFOKillFeed.CorpseFadeOutcome,System.Int32)` - branch+temp - UNVERIFIED
    `-stlocL `
    `+stlocL L`
    `+ldlocL `
    `+brfalse L`
- `HawChow.GTFOKillFeed.CorpseFeedback|System.Void ReleaseGibs(HawChow.GTFOKillFeed.CorpseFeedback/Corpse)` - branch+temp - UNVERIFIED
    `+ldnull `
    `+stlocL `
    `-ceq `
    `+beq.s L`
- `HawChow.GTFOKillFeed.CorpseFeedback|System.Void SearchRagdollRoot(HawChow.GTFOKillFeed.CorpseFeedback/Corpse,UnityEngine.Transform,System.Boolean)` - branch+temp - UNVERIFIED
    `+ldlocL `
    `+stlocL `
    `-stlocL `
    `+stlocL L`
- `HawChow.GTFOKillFeed.CorpseVisualLease|System.Int32 get_BodyCount()` - branch+temp - UNVERIFIED
    `-dup `
    `+stlocL `
    `+ldlocL `
    `-pop `
- `HawChow.GTFOKillFeed.CorpseVisualLease|System.Int32 get_ColliderCount()` - branch+temp - UNVERIFIED
    `-dup `
    `+stlocL `
    `+ldlocL `
    `-pop `
- `HawChow.GTFOKillFeed.CorpseVisualLease|System.Int32 get_RendererCount()` - branch+temp - UNVERIFIED
    `-dup `
    `+stlocL `
    `+ldlocL `
    `-pop `
- `HawChow.GTFOKillFeed.DamageFx|HawChow.GTFOKillFeed.DamageTier Tier()` - branch+temp - covered-by-exec
    `+stlocL `
    `+ldlocL `
- `HawChow.GTFOKillFeed.EmpBridge|System.Boolean TryBindFromAppDomain(System.Reflection.PropertyInfo&,System.Reflection.MethodInfo&)` - branch+temp - UNVERIFIED
    `+ldlocL `
    `+stlocL `
    `-stlocL `
    `-ldlocL `
- `HawChow.GTFOKillFeed.EnemyBar|System.Void Draw()` - branch+temp - UNVERIFIED
    `+ldc.i4.1 `
    `+beq.s L`
    `+ldlocL L`
    `-ldlocL L`
- `HawChow.GTFOKillFeed.EnemyFocus|System.String ResolveWeakspotParts(Enemies.EnemyAgent,System.String)` - branch+temp - UNVERIFIED
    `-bne.un.s L`
    `+bne.un L`
    `+brtrue.s L`
    `+ldc.i4.0 `
- `HawChow.GTFOKillFeed.FeedbackPolicy|System.Single FlyJitter(System.Int32)` - branch+temp - covered-by-exec
    `-dup `
    `+stlocL `
    `+ldlocL `
    `+ldlocL `
- `HawChow.GTFOKillFeed.FeedbackPolicy|System.Single NumberScale(System.Single,HawChow.GTFOKillFeed.HitKind)` - branch+temp - covered-by-exec
    `+ldc.i4.1 `
    `+beq.s L`
    `+ldarg.1 `
    `-ldarg.1 `
- `HawChow.GTFOKillFeed.FeedbackPolicy|System.Void NumberPose(System.Boolean,System.Boolean,System.Int32,System.Single,System.Single,System.Single,System.Single,System.Single,System.Single,System.Single,System.Single,System.Single,HawChow.GTFOKillFeed.HitKind,System.Single,System.Single&,System.Single&,System.Single&,System.Single&,System.Single&,System.Single&)` - branch+temp - UNVERIFIED
    `-stlocL `
    `+stlocL L`
    `-ldlocL `
    `+ldlocL L`
- `HawChow.GTFOKillFeed.FileIconIo|System.Byte[] TryRead(System.String,System.Int64,System.String&)` - branch+temp - UNVERIFIED
    `-brfalse.s L`
    `-ldlocL `
    `+dup `
    `+brtrue.s L`
- `HawChow.GTFOKillFeed.HitMarkerHud|System.Boolean ShowHitIndicatorPrefix(UnityEngine.Vector3,System.Boolean,System.Boolean,System.Boolean)` - branch+temp - UNVERIFIED
    `-stlocL `
    `+stlocL L`
    `-ldlocL `
    `+ldlocL L`
- `HawChow.GTFOKillFeed.HitSoundPolicy|HawChow.GTFOKillFeed.HitCueKind KindOf(HawChow.GTFOKillFeed.HitKind,System.Boolean)` - branch+temp - covered-by-exec
    `+ldc.i4.1 `
    `+beq.s L`
    `+ldarg.0 `
    `-ldarg.0 `
- `HawChow.GTFOKillFeed.HitSoundPolicy|System.String FlightToken(HawChow.GTFOKillFeed.KillFlight)` - branch+temp - covered-by-exec
    `+ldarg.0 `
    `+ldc.i4.1 `
    `+beq.s L`
    `-ldarg.0 `
- `HawChow.GTFOKillFeed.HitSoundPolicy|System.String StyleToken(HawChow.GTFOKillFeed.HitSoundStyle)` - branch+temp - covered-by-exec
    `-beq.s L`
    `+bne.un.s L`
    `+ldstr bf6`
    `+stlocL `
- `HawChow.GTFOKillFeed.HitSoundPolicy|System.String TierToken(HawChow.GTFOKillFeed.KillTier)` - branch+temp - covered-by-exec
    `-beq.s L`
    `+bne.un.s L`
    `+ldstr elite`
    `+stlocL `
- `HawChow.GTFOKillFeed.IconPipeline|HawChow.GTFOKillFeed.IconPipeline/Slot SlotFor(System.String)` - branch+temp - UNVERIFIED
    `+ldnull `
    `+stlocL `
- `HawChow.GTFOKillFeed.IconPipeline|System.Int32 FailureCount(System.String)` - branch+temp - UNVERIFIED
    `+ldnull `
    `+stlocL `
- `HawChow.GTFOKillFeed.IconPipeline|System.Object Texture(System.String)` - branch+temp - UNVERIFIED
    `+ldnull `
    `+stlocL `
- `HawChow.GTFOKillFeed.IconPipeline|System.String LoadedPath(System.String)` - branch+temp - UNVERIFIED
    `+ldnull `
    `+stlocL `
- `HawChow.GTFOKillFeed.KillHud|UnityEngine.Texture2D TierIcon(HawChow.GTFOKillFeed.KillTier)` - branch+temp - UNVERIFIED
    `-beq.s L`
    `+bne.un.s L`
    `+ldarg.0 `
    `+ldfld UnityEngine.Texture2D HawChow.GTFOKillFeed.KillHud::_heavyIcon`
- `HawChow.GTFOKillFeed.KillIconHud|UnityEngine.Texture2D TextureFor(HawChow.GTFOKillFeed.KillTier)` - branch+temp - UNVERIFIED
    `-beq.s L`
    `+bne.un.s L`
    `+ldsfld UnityEngine.Texture2D HawChow.GTFOKillFeed.KillIconHud::_heavy`
    `+stlocL `
- `HawChow.GTFOKillFeed.KillIconPolicy|HawChow.GTFOKillFeed.FlightMark MarkFor(HawChow.GTFOKillFeed.KillFlight)` - branch+temp - covered-by-exec
    `+ldarg.0 `
    `+ldc.i4.1 `
    `+beq.s L`
    `-ldarg.0 `
- `HawChow.GTFOKillFeed.KillIconPolicy|System.String TierIconKey(HawChow.GTFOKillFeed.KillTier)` - branch+temp - covered-by-exec
    `-beq.s L`
    `+bne.un.s L`
    `+ldstr kill-heavy`
    `+stlocL `
- `HawChow.GTFOKillFeed.KillPolicy|System.String TierLabel(HawChow.GTFOKillFeed.KillTier,System.String)` - branch+temp - covered-by-exec
    `-beq.s L`
    `+bne.un.s L`
    `+ldstr ELITE`
    `+stlocL `
- `HawChow.GTFOKillFeed.KillTracker|System.Void LogPatchReceipts(HarmonyLib.Harmony,BepInEx.Logging.ManualLogSource)` - branch+temp - UNVERIFIED
    `-ldarg.1 `
    `-stlocL `
    `+stlocL L`
    `+ldlocL `
- `HawChow.GTFOKillFeed.LogTally|System.Int32 Add(System.String)` - branch+temp - UNVERIFIED
    `+stlocL `
    `+ldc.i4.0 `
    `+ldlocL `
    `+stlocL `
- `HawChow.GTFOKillFeed.LogTally|System.Int32 Count(System.String)` - branch+temp - UNVERIFIED
    `+ldc.i4.0 `
    `+stlocL `
- `HawChow.GTFOKillFeed.ShotgunDamageNumberBatch|System.Boolean Add(System.Int64,System.Single,System.Single,System.Single,System.Single,HawChow.GTFOKillFeed.HitKind)` - branch+temp - UNVERIFIED
    `+ldnull `
    `+stlocL `
- `HawChow.GTFOKillFeed.ShotgunDamageNumberScopeStack|HawChow.GTFOKillFeed.ShotgunDamageNumberScope Begin(System.Boolean,System.Boolean,System.Int32,System.UInt32)` - branch+temp - UNVERIFIED
    `+ldarg.0 `
    `+dup `
    `-ldarg.0 `
    `-ldlocL `
- `HawChow.GTFOKillFeed.TeammateMarkLedger|HawChow.GTFOKillFeed.TeammateMarkLedger/Entry GetOrAdd(System.Int64,System.Single)` - branch+temp - UNVERIFIED
    `+ldnull `
    `+stlocL `
- `HawChow.GTFOKillFeed.TeammateMarkPolicy|System.String IconFileName(HawChow.GTFOKillFeed.TeammateMarkIcon)` - branch+temp - covered-by-exec
    `+stlocL `
    `+br.s L`
    `+ldstr teammate-downed-si.png`
    `+stlocL `
- `HawChow.GTFOKillFeed.TeammateMarkPolicy|System.String IconGlyph(HawChow.GTFOKillFeed.TeammateMarkIcon)` - branch+temp - covered-by-exec
    `+stlocL `
    `+br.s L`
    `+ldstr 死`
    `+stlocL `
- `HawChow.GTFOKillFeed.TeammateMarks|HawChow.GTFOKillFeed.TeammateMarks/Runtime RuntimeFor(System.Int64)` - branch+temp - UNVERIFIED
    `+ldnull `
    `+stlocL `
- `HawChow.GTFOKillFeed.BarPolicy|System.Single HeaderWidth(System.Single,System.Single,System.Single,System.Single,System.Single,System.Single,System.Single)` - branch-comparison - UNVERIFIED
    `-cgt `
    `+bgt.s L`
    `+ldc.i4.0 `
    `+br.s L`
- `HawChow.GTFOKillFeed.CorpseFade/DecayFadeTarget|System.Void Return(System.Object)` - branch-comparison - UNVERIFIED
    `-brtrue.s L`
    `-ret `
    `+brfalse.s L`
- `HawChow.GTFOKillFeed.CorpseFadeCoordinator|System.Int32 Prune(HawChow.GTFOKillFeed.ICorpseFadeTarget)` - branch-comparison - UNVERIFIED
    `+ldc.i4.1 `
    `+beq.s L`
    `+ldlocL `
    `-ldlocL `
- `HawChow.GTFOKillFeed.CorpseFadePrep|System.Boolean Attempt(System.Func`1<System.Object>,System.Func`2<System.Object,System.Boolean>,System.Action`1<System.Exception>)` - branch-comparison - UNVERIFIED
    `-brfalse.s L`
    `+brtrue.s L`
    `+ldnull `
    `+br.s L`
- `HawChow.GTFOKillFeed.CorpseFade|System.Boolean TryNative(HawChow.GTFOKillFeed.CorpseFadePlan&,Decay,UnityEngine.Renderer,System.Int32)` - branch-comparison - UNVERIFIED
    `-brfalse.s L`
    `-ldarg.1 `
    `-ldlocL L`
    `-ldnull `
- `HawChow.GTFOKillFeed.CorpseFade|System.Void LogNativeUnavailable(HawChow.GTFOKillFeed.CorpseFadeNativePath,System.Int32,System.Boolean,System.Boolean,System.Int32)` - branch-comparison - UNVERIFIED
    `-brtrue.s L`
    `-ret `
    `+brfalse.s L`
- `HawChow.GTFOKillFeed.CorpseFeedback|System.Void CloseLaunch(HawChow.GTFOKillFeed.CorpseFeedback/Corpse,System.Single)` - branch-comparison - UNVERIFIED
    `-brtrue.s L`
    `-ret `
    `+brfalse.s L`
- `HawChow.GTFOKillFeed.DamageFxPolicy|HawChow.GTFOKillFeed.DamageFxPolicy/NumberPaintPlan Paint(System.Boolean,System.Boolean,HawChow.GTFOKillFeed.DamageTier)` - branch-comparison - UNVERIFIED
    `-brtrue.s L`
    `+brfalse.s L`
    `+ldc.i4.2 `
    `+br.s L`
- `HawChow.GTFOKillFeed.DamageNumbers|System.Void Add(UnityEngine.Vector3,System.Single,HawChow.GTFOKillFeed.HitKind,System.Boolean,System.UInt32)` - branch-comparison - UNVERIFIED
    `-brtrue.s L`
    `-ret `
    `+brfalse.s L`
    `-brfalse.s L`
- `HawChow.GTFOKillFeed.DamageNumbers|System.Void AddImmediate(UnityEngine.Vector3,System.Single,HawChow.GTFOKillFeed.HitKind,System.Boolean)` - branch-comparison - UNVERIFIED
    `-brfalse.s L`
    `+brfalse L`
    `-brfalse.s L`
    `+brfalse L`
- `HawChow.GTFOKillFeed.EnemyBar|System.Void DrawLabel(UnityEngine.Rect,System.Int32,System.Int32,System.Single,UnityEngine.Color)` - branch-comparison - UNVERIFIED
    `-cgt `
    `-ldlocL L`
    `-ldc.r4 0`
    `-cgt `
- `HawChow.GTFOKillFeed.EnemyBar|System.Void EnsureLabel(System.Boolean,System.Boolean,System.Boolean,System.String)` - branch-comparison - UNVERIFIED
    `-brfalse.s L`
    `-ret `
    `+brtrue.s L`
- `HawChow.GTFOKillFeed.EnemyBar|System.Void EnsureStyles()` - branch-comparison - covered-by-exec
    `-brfalse.s L`
    `-ret `
    `+brtrue.s L`
- `HawChow.GTFOKillFeed.EnemyBar|System.Void LogFocus(System.Boolean)` - branch-comparison - UNVERIFIED
    `-brtrue.s L`
    `-ret `
    `+brfalse.s L`
    `-brfalse L`
- `HawChow.GTFOKillFeed.FeedbackPolicy|System.Void FlightTarget(System.Single,System.Single,System.Single,System.Int32,System.Single&,System.Single&,System.Single&,System.Single&)` - branch-comparison - UNVERIFIED
    `-brtrue.s L`
    `-ret `
    `+brfalse.s L`
- `HawChow.GTFOKillFeed.FeedbackPolicy|System.Void RotateAroundTarget(System.Single,System.Single,System.Single,System.Single,System.Single,System.Single&,System.Single&)` - branch-comparison - UNVERIFIED
    `-brtrue.s L`
    `-ret `
    `+brfalse.s L`
- `HawChow.GTFOKillFeed.HexColorCache|System.Void Resolve(System.String,System.Single,System.Single,System.Single,System.Single)` - branch-comparison - UNVERIFIED
    `-brfalse.s L`
    `-ret `
    `+brtrue.s L`
- `HawChow.GTFOKillFeed.HitMarkerHud|System.Void OnGUI()` - branch-comparison - UNVERIFIED
    `-beq.s L`
    `-ret `
    `+bne.un.s L`
- `HawChow.GTFOKillFeed.HitSound|System.Void OnLocalHit(HawChow.GTFOKillFeed.HitKind)` - branch-comparison - covered-by-exec
    `-brtrue.s L`
    `-ret `
    `+brfalse.s L`
- `HawChow.GTFOKillFeed.HitSound|System.Void OnLocalKill(HawChow.GTFOKillFeed.KillTier,HawChow.GTFOKillFeed.KillFlight)` - branch-comparison - covered-by-exec
    `-brtrue.s L`
    `-ret `
    `+brfalse.s L`
- `HawChow.GTFOKillFeed.IconPipeline|System.Void ApplyProbe(HawChow.GTFOKillFeed.IconPipeline/Job,System.Single)` - branch-comparison - UNVERIFIED
    `-beq.s L`
    `-ret `
    `+bne.un.s L`
- `HawChow.GTFOKillFeed.KillHud|System.Void EnsureStyles()` - branch-comparison - UNVERIFIED
    `-brfalse.s L`
    `-ret `
    `+brtrue.s L`
- `HawChow.GTFOKillFeed.KillIconHud|System.Void DrawSlot(HawChow.GTFOKillFeed.KillIconSlot&,UnityEngine.Camera,System.Single,System.Single,System.Single,System.Single,System.Single)` - branch-comparison - UNVERIFIED
    `-brfalse.s L`
    `-ret `
    `+brtrue L`
    `-brtrue.s L`
- `HawChow.GTFOKillFeed.KillTracker|System.Void Complete(Dam_EnemyDamageBase,HawChow.GTFOKillFeed.DamageScope)` - branch-comparison - UNVERIFIED
    `-brfalse.s L`
    `-ret `
    `+brtrue.s L`
- `HawChow.GTFOKillFeed.KillTracker|System.Void CompleteFromActive(Dam_EnemyDamageBase)` - branch-comparison - UNVERIFIED
    `-brtrue.s L`
    `-ldc.i4.0 `
    `-br.s L`
    `+brfalse.s L`
- `HawChow.GTFOKillFeed.KillTracker|System.Void CompleteFromAgent(Enemies.EnemyAgent)` - branch-comparison - UNVERIFIED
    `-brtrue.s L`
    `-ldc.i4.0 `
    `-br.s L`
    `+brfalse.s L`
- `HawChow.GTFOKillFeed.KillTracker|System.Void Spawned(Dam_EnemyDamageBase)` - branch-comparison - UNVERIFIED
    `-brfalse.s L`
    `-ret `
    `+brtrue.s L`
- `HawChow.GTFOKillFeed.CorpseFeedback|System.Void GibReused(CharacterDestruction.CD_Gib)` - codegen-only - UNVERIFIED
    `+ldnull `
    `+stlocL `
    `+ldlocaL L`
    `+initobj UnityEngine.Vector3`
- `HawChow.GTFOKillFeed.CorpseFeedback|UnityEngine.AudioClip LoadExplosionSound()` - codegen-only - UNVERIFIED
    `+castclass System.IO.Stream`
    `-brfalse.s L`
    `-ldlocL `
    `+dup `
- `HawChow.GTFOKillFeed.BurstHistory|System.Single Sum(System.Int64,System.Single)` - enumeration-shape - UNVERIFIED
    `+ldnull `
    `+stlocL `
    `+ldlocL `
    `-stlocL `
- `HawChow.GTFOKillFeed.BurstHistory|System.Void Prune(System.Single)` - enumeration-shape - UNVERIFIED
    `-stlocL `
    `+stlocL L`
    `-ldlocL `
    `+ldlocL L`
- `HawChow.GTFOKillFeed.CorpseFeedback|System.Void CaptureGib(CharacterDestruction.CD_Gib)` - enumeration-shape - UNVERIFIED
    `-stlocL L`
    `+stlocL `
    `-ldlocaL L`
    `-constrained. System.Collections.Generic.List`1/Enumerator<CharacterDestruction.CD_Gib>`
- `HawChow.GTFOKillFeed.CorpseFeedback|System.Void LimitGibs(HawChow.GTFOKillFeed.CorpseFeedback/Corpse)` - enumeration-shape - UNVERIFIED
    `+ldnull `
    `+stlocL `
    `+ldlocaL L`
    `+initobj UnityEngine.Vector3`
- `HawChow.GTFOKillFeed.DamageNumbers|System.Void Draw()` - enumeration-shape - UNVERIFIED
    `-brfalse.s L`
    `-ldlocL L`
    `-ldlocaL L`
    `-ldlocaL L`
- `HawChow.GTFOKillFeed.EnemyNamePolicy|System.String Resolve(System.UInt32,System.String,System.Collections.Generic.IReadOnlyList`1<HawChow.GTFOKillFeed.EnemyNameRule>,System.Collections.Generic.IReadOnlyDictionary`2<System.UInt32,System.String>)` - enumeration-shape - UNVERIFIED
    `-leave.s L`
    `+leave L`
    `-brfalse.s L`
    `-ldlocL `
- `HawChow.GTFOKillFeed.EnemyNames|System.Collections.Generic.Dictionary`2<System.UInt32,System.String> BuildDefaults(System.Collections.Generic.List`1<HawChow.GTFOKillFeed.EnemyNames/NameGroup>)` - enumeration-shape - UNVERIFIED
    `-ldlocaL L`
    `-constrained. System.Collections.Generic.List`1/Enumerator<System.UInt32>`
    `+ldlocL `
    `+box System.Collections.Generic.List`1/Enumerator<System.UInt32>`
- `HawChow.GTFOKillFeed.TeammateMarkLedger|System.Int32 ForgetUnseen(System.Single,System.Collections.Generic.List`1<System.Int64>)` - enumeration-shape - UNVERIFIED
    `-stlocL `
    `+stlocL L`
    `-ldlocaL L`
    `-constrained. System.Collections.Generic.Dictionary`2/Enumerator<System.Int64,HawChow.GTFOKillFeed.TeammateMarkLedger/Entry>`
- `HawChow.GTFOKillFeed.TeammateMarks|System.Void Draw()` - enumeration-shape - UNVERIFIED
    `-brtrue.s L`
    `-ret `
    `+brfalse.s L`
    `-brtrue.s L`
- `HawChow.GTFOKillFeed.BarPolicy|System.Boolean TryHexByte(System.String,System.Int32,System.Byte&)` - extra-cast - UNVERIFIED
    `+castclass System.IFormatProvider`
- `HawChow.GTFOKillFeed.BarPolicy|System.String CfoamTag(System.Single)` - extra-cast - covered-by-exec
    `+ldstr [结沫*`
    `-ldstr [结沫*`
    `+castclass System.IFormatProvider`
- `HawChow.GTFOKillFeed.BarPolicy|System.String Readout(System.Single,System.Single)` - extra-cast - UNVERIFIED
    `+castclass System.IFormatProvider`
    `+castclass System.IFormatProvider`
- `HawChow.GTFOKillFeed.BarPolicy|System.String WeakspotMultiplierTag(System.String,System.Single,System.String)` - extra-cast - covered-by-exec
    `+castclass System.IFormatProvider`
- `HawChow.GTFOKillFeed.CorpseFade|System.Boolean PlayIsPatched()` - extra-cast - UNVERIFIED
    `+castclass System.Reflection.MethodBase`
    `+castclass System.Reflection.MethodBase`
- `HawChow.GTFOKillFeed.EnemyNames|System.String Resolve(System.UInt32,System.String)` - extra-cast - covered-by-exec
    `+castclass System.Collections.Generic.IReadOnlyDictionary`2<System.UInt32,System.String>`
- `HawChow.GTFOKillFeed.FeedbackPolicy|System.String LaunchState(System.Int32,System.Boolean)` - extra-cast - covered-by-exec
    `+castclass System.IFormatProvider`
- `HawChow.GTFOKillFeed.GlueGunWield|System.Boolean IsHeld(FirstPersonItemHolder)` - extra-cast - UNVERIFIED
    `+castclass UnityEngine.Object`
    `+castclass UnityEngine.Object`
    `+castclass Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase`
    `+castclass UnityEngine.Object`
- `HawChow.GTFOKillFeed.HitMarkerHud|System.Void ApplyPatches(HarmonyLib.Harmony,BepInEx.Logging.ManualLogSource)` - extra-cast - UNVERIFIED
    `+castclass System.Reflection.MethodBase`
    `+castclass System.Reflection.MethodBase`
- `HawChow.GTFOKillFeed.HitSound|System.Void .cctor()` - extra-cast - UNVERIFIED
    `+castclass System.Collections.Generic.IEqualityComparer`1<System.String>`
    `+castclass System.Collections.Generic.IEqualityComparer`1<System.String>`
- `HawChow.GTFOKillFeed.HitSound|System.Void ResolveMutedIds()` - extra-cast - covered-by-exec
    `+castclass System.Collections.Generic.IEqualityComparer`1<System.String>`
- `HawChow.GTFOKillFeed.KillPolicy|System.String FormatDistance(System.Single,System.Single)` - extra-cast - covered-by-exec
    `+castclass System.IFormatProvider`
    `+castclass System.IFormatProvider`
- `HawChow.GTFOKillFeed.KillPolicy|System.String FormatHealth(System.Single)` - extra-cast - covered-by-exec
    `+castclass System.IFormatProvider`
- `HawChow.GTFOKillFeed.LogTally|System.Void .ctor()` - extra-cast - UNVERIFIED
    `+castclass System.Collections.Generic.IEqualityComparer`1<System.String>`
- `HawChow.GTFOKillFeed.CorpseFade|System.Void LogFallback(System.String,System.Int32)` - layout - UNVERIFIED
- `HawChow.GTFOKillFeed.CorpseFade|System.Void LogSkip(HawChow.GTFOKillFeed.CorpseFadeSkip,System.Int32)` - layout - UNVERIFIED
- `HawChow.GTFOKillFeed.CorpseFade|System.Void LogStarted(System.Int32,UnityEngine.Renderer)` - layout - UNVERIFIED
- `HawChow.GTFOKillFeed.CorpseFade|System.Void LogStartedUnknown(System.Int32,UnityEngine.Renderer)` - layout - UNVERIFIED
- `HawChow.GTFOKillFeed.CorpseFade|System.Void Reset()` - layout - covered-by-exec
- `HawChow.GTFOKillFeed.CorpseFade|System.Void Warn(System.Exception)` - layout - UNVERIFIED
- `HawChow.GTFOKillFeed.CorpseFeedback|System.Void Schedule(Enemies.EnemyAgent,UnityEngine.Vector3,System.Single,System.String)` - layout - UNVERIFIED
- `HawChow.GTFOKillFeed.EnemyBar|System.Void Tick(System.Single,System.Boolean)` - layout - UNVERIFIED
- `HawChow.GTFOKillFeed.EnemyFocus|System.Boolean TryGetHeadWorld(Enemies.EnemyAgent,System.Single,UnityEngine.Vector3&)` - layout - UNVERIFIED
- `HawChow.GTFOKillFeed.HitMarkerHud|System.Boolean TryShowKill(UnityEngine.Vector3)` - layout - UNVERIFIED
- `HawChow.GTFOKillFeed.KillHud|System.Void DrawCards(HawChow.GTFOKillFeed.KillIconDisplay)` - layout - UNVERIFIED
- `HawChow.GTFOKillFeed.KillHud|System.Void TickCardIcons(System.Single)` - layout - UNVERIFIED
- `HawChow.GTFOKillFeed.KillIconHud|System.Int32 Push(System.IntPtr,UnityEngine.Vector3,System.Single,HawChow.GTFOKillFeed.KillPresentation,System.Single,System.Boolean)` - layout - UNVERIFIED
- `HawChow.GTFOKillFeed.KillIconHud|System.Void Draw()` - layout - UNVERIFIED
- `HawChow.GTFOKillFeed.Plugin|System.Void LogDamageSwitchReceipts(System.Boolean)` - layout - UNVERIFIED
- `HawChow.GTFOKillFeed.CorpseFeedback|UnityEngine.Renderer HierarchyRenderer(UnityEngine.Transform)` - operand-only - UNVERIFIED
    `+ldnull `
    `-ldnull `
- `HawChow.GTFOKillFeed.KillPolicy|HawChow.GTFOKillFeed.KillTier ClassifyTier(System.Single,System.Single,System.Single)` - operand-only - covered-by-exec
    `-ldarg.0 `
    `+ldarg.0 `
- `HawChow.GTFOKillFeed.Plugin|System.Void RegisterComponent(BepInEx.Logging.ManualLogSource)` - operand-only - UNVERIFIED
    `-ldarg.0 `
    `+ldarg.0 `
    `-ldarg.0 `
    `+ldarg.0 `
- `HawChow.GTFOKillFeed.TeammateMarks|System.Int64 KeyOf(Player.PlayerAgent)` - operand-only - UNVERIFIED
    `+neg `
    `-neg `
- `HawChow.GTFOKillFeed.CorpseFadeCoordinator|System.Boolean TryOnce(HawChow.GTFOKillFeed.ICorpseFadeTarget,System.Object,HawChow.GTFOKillFeed.CorpseFadeOutcome&)` - other-shape - UNVERIFIED
    `-stlocL L`
    `-ldarg.0 `
    `-ldlocL L`
    `+stlocL `
- `HawChow.GTFOKillFeed.CorpseFadeCoordinator|System.Boolean TryStart(HawChow.GTFOKillFeed.ICorpseFadeTarget,System.Object,HawChow.GTFOKillFeed.CorpseFadeOutcome&)` - other-shape - UNVERIFIED
    `-blt.s L`
    `-ldc.i4.0 `
    `-ret `
    `+bge.s L`
- `HawChow.GTFOKillFeed.CorpseFade|HawChow.GTFOKillFeed.CorpseFadePoolState PoolState()` - other-shape - covered-by-exec
    `-bgt.s L`
    `+ble.s L`
    `+ldc.i4.2 `
    `+br.s L`
- `HawChow.GTFOKillFeed.CorpseFade|System.Void Sweep()` - other-shape - covered-by-exec
    `-brfalse.s L`
    `+brtrue.s L`
    `+leave.s L`
- `HawChow.GTFOKillFeed.CorpseFeedback|System.Int32 ActivateGibData(CharacterDestruction.CD_CharacterDestructionCollider,System.Byte,UnityEngine.Vector3,UnityEngine.Vector3,System.Int32,System.Boolean&,System.Int32&,System.Int32&)` - other-shape - UNVERIFIED
    `+ldlocL `
    `+ldarg.s maxCalls`
    `+bge L`
    `+ldlocL `
- `HawChow.GTFOKillFeed.CorpseFeedback|System.Void TryLeaseModel(HawChow.GTFOKillFeed.CorpseFeedback/Corpse)` - other-shape - UNVERIFIED
    `-brfalse.s L`
    `-leave.s L`
    `+brtrue.s L`
- `HawChow.GTFOKillFeed.CorpseVisualLease|System.Void RecyclePhysics()` - other-shape - UNVERIFIED
    `-brfalse.s L`
    `-leave.s L`
    `+brtrue.s L`
    `-brfalse.s L`
- `HawChow.GTFOKillFeed.CorpseVisualLease|System.Void RestoreState()` - other-shape - UNVERIFIED
    `-brfalse.s L`
    `-leave.s L`
    `+brtrue.s L`
- `HawChow.GTFOKillFeed.CorpseVisualLease|UnityEngine.Renderer PrimaryRenderer()` - other-shape - UNVERIFIED
    `-brtrue.s L`
    `-leave.s L`
    `+brfalse.s L`
- `HawChow.GTFOKillFeed.CueWavePlayer|System.Void EnsureOpen()` - other-shape - covered-by-exec
    `-leave L`
    `+br L`
    `-leave.s L`
    `+br.s L`
- `HawChow.GTFOKillFeed.DamageFxPolicy|System.Void FoilEmbossOffset(System.Single,System.Single&,System.Single&)` - other-shape - UNVERIFIED
    `+ldc.r8 0`
    `-neg `
    `+sub `
- `HawChow.GTFOKillFeed.EnemyBar|System.Void DrawLabelText(UnityEngine.GUIContent,UnityEngine.Rect,UnityEngine.GUIStyle,UnityEngine.Color,UnityEngine.Color,System.Single,System.Single)` - other-shape - UNVERIFIED
    `-bgt.un.s L`
    `-ret `
    `+ble.s L`
- `HawChow.GTFOKillFeed.EnemyBar|System.Void Fill(UnityEngine.Rect,UnityEngine.Color)` - other-shape - UNVERIFIED
    `-bgt.un.s L`
    `-ret `
    `+ble.s L`
- `HawChow.GTFOKillFeed.EnemyBar|System.Void ReadHealth(Enemies.EnemyAgent,System.Single&,System.Single&)` - other-shape - UNVERIFIED
    `-brfalse.s L`
    `-leave.s L`
    `+brtrue.s L`
- `HawChow.GTFOKillFeed.EnemyBar|UnityEngine.Color ConfigColor(BepInEx.Configuration.ConfigEntry`1<System.String>,System.String&,UnityEngine.Color&,UnityEngine.Color)` - other-shape - UNVERIFIED
    `-callvirt !0 BepInEx.Configuration.ConfigEntry`1<System.String>::get_Value()`
    `+call !0 BepInEx.Configuration.ConfigEntry`1<System.String>::get_Value()`
- `HawChow.GTFOKillFeed.EnemyFocus|System.Single GlueToleranceOf(Enemies.EnemyAgent)` - other-shape - UNVERIFIED
    `-stlocL `
    `-ldlocL `
    `+dup `
    `+pop `
- `HawChow.GTFOKillFeed.FeedbackPolicy|HawChow.GTFOKillFeed.FeedbackPolicy/RagdollLease ClassifyRagdoll(System.Boolean,System.Int32,System.Boolean)` - other-shape - covered-by-exec
    `-brfalse.s L`
    `+and `
- `HawChow.GTFOKillFeed.FeedbackPolicy|System.Void PrismaticFlightPoint(System.Int32,System.Single,System.Single,System.Single,System.Single,System.Single,System.Single,System.Single&,System.Single&,System.Single&,System.Single&)` - other-shape - UNVERIFIED
    `-bne.un.s L`
    `-ret `
    `+beq.s L`
    `-bgt.un.s L`
- `HawChow.GTFOKillFeed.HitMarkerHud|System.Boolean IconReady(HawChow.GTFOKillFeed.HitMarkerKind)` - other-shape - UNVERIFIED
    `-beq.s L`
    `-ldarg.1 `
    `-ldc.i4.3 `
    `-beq.s L`
- `HawChow.GTFOKillFeed.HitMarkerHud|UnityEngine.Color TintFor(HawChow.GTFOKillFeed.HitMarkerKind)` - other-shape - UNVERIFIED
    `-bne.un.s L`
    `+sub `
    `+switch Mono.Cecil.Cil.Instruction[]`
    `+br L`
- `HawChow.GTFOKillFeed.HitMarkerHud|UnityEngine.Texture2D IconFor(HawChow.GTFOKillFeed.HitMarkerKind)` - other-shape - UNVERIFIED
    `-beq.s L`
    `-ldarg.1 `
    `-ldc.i4.3 `
    `-beq.s L`
- `HawChow.GTFOKillFeed.HitMarkerPolicy|System.Boolean IsProjectable(System.Single,System.Single,System.Single,System.Single,System.Single,System.Single)` - other-shape - UNVERIFIED
    `+ldc.r4 0`
    `-neg `
    `+sub `
    `+ldc.r4 0`
- `HawChow.GTFOKillFeed.HitMarkerPolicy|System.Single SizeScale(HawChow.GTFOKillFeed.HitMarkerKind)` - other-shape - covered-by-exec
    `-beq.s L`
    `-ldarg.0 `
    `-ldc.i4.3 `
    `-beq.s L`
- `HawChow.GTFOKillFeed.HitMarkerPolicy|System.String IconKey(HawChow.GTFOKillFeed.HitMarkerKind)` - other-shape - covered-by-exec
    `-ldarg.0 `
    `-ldc.i4.2 `
    `-beq.s L`
    `-ldarg.0 `
- `HawChow.GTFOKillFeed.HitSoundState|System.Void PublishResolved(System.UInt32[],System.Int32)` - other-shape - UNVERIFIED
    `-blt.s L`
    `+bge.s L`
    `+ldc.i4.0 `
    `+br.s L`
- `HawChow.GTFOKillFeed.IconTextureCache|System.Void .ctor(HawChow.GTFOKillFeed.IIconIo,HawChow.GTFOKillFeed.IIconSink,HawChow.GTFOKillFeed.IIconJobRunner)` - other-shape - UNVERIFIED
    `-ldftn System.Void HawChow.GTFOKillFeed.IconTextureCache::<.ctor>b__3_0(System.String)`
    `+ldftn System.Void HawChow.GTFOKillFeed.IconTextureCache::<.ctor>b__5_0(System.String)`
- `HawChow.GTFOKillFeed.KillHud|System.Void Update()` - other-shape - UNVERIFIED
    `-brtrue.s L`
    `-leave.s L`
    `+brfalse.s L`
- `HawChow.GTFOKillFeed.KillIconHud|System.Void DrawFlightMark(HawChow.GTFOKillFeed.KillFlight,System.Single,System.Single,System.Single,System.Single)` - other-shape - UNVERIFIED
    `-bne.un L`
    `-ldarg.3 `
    `-stlocL L`
    `+bne.un.s L`
- `HawChow.GTFOKillFeed.KillIconPolicy|System.Void PreviewCombo(System.Int32,HawChow.GTFOKillFeed.KillTier&,HawChow.GTFOKillFeed.KillFlight&)` - other-shape - UNVERIFIED
    `-ble.s L`
    `+bgt.s L`
    `+ldc.i4.0 `
    `+br.s L`
- `HawChow.GTFOKillFeed.KillIconQueue|System.Void PushReal(HawChow.GTFOKillFeed.KillIconSlot&,System.Int32)` - other-shape - UNVERIFIED
    `-blt.s L`
    `-ret `
    `+bge.s L`
- `HawChow.GTFOKillFeed.KillIconQueue|System.Void RemovePreviewAt(System.Int32)` - other-shape - UNVERIFIED
    `-blt.s L`
    `-ret `
    `+bge.s L`
- `HawChow.GTFOKillFeed.KillIconQueue|System.Void RemoveRealAt(System.Int32)` - other-shape - UNVERIFIED
    `-blt.s L`
    `-ret `
    `+bge.s L`
- `HawChow.GTFOKillFeed.ShotgunDamageNumberCapture|System.UInt32 ReadCategoryId(Gear.BulletWeapon)` - other-shape - UNVERIFIED
    `-ldnull `
    `-call System.Boolean UnityEngine.Object::op_Inequality(UnityEngine.Object,UnityEngine.Object)`
    `-stlocL `
    `-ldlocL `
- `HawChow.GTFOKillFeed.TeammateMarks|System.Void ReadState(Player.PlayerAgent,System.Boolean&,System.Boolean&)` - other-shape - UNVERIFIED
    `-ldnull `
    `-call System.Boolean UnityEngine.Object::op_Equality(UnityEngine.Object,UnityEngine.Object)`
    `-leave.s L`
    `-beq.s L`
- `HawChow.GTFOKillFeed.BarPolicy|System.String WeakspotTags(System.Collections.Generic.IEnumerable`1<HawChow.GTFOKillFeed.WeakspotPartMultiplier>,System.String)` - string-formatting - UNVERIFIED
    `+castclass System.Collections.Generic.IEqualityComparer`1<System.String>`
    `-brfalse.s L`
    `-ldlocL `
    `+dup `
- `HawChow.GTFOKillFeed.CorpseFade|System.Void TryBegin(UnityEngine.Renderer,System.Boolean,System.Boolean,System.Int32,Decay)` - string-formatting - UNVERIFIED
    `-brfalse.s L`
    `-leave.s L`
    `+brtrue.s L`
    `-leave.s L`
- `HawChow.GTFOKillFeed.CorpseFeedback|System.Int32 BreakLimbs(HawChow.GTFOKillFeed.CorpseFeedback/Corpse)` - string-formatting - UNVERIFIED
    `-stlocL L`
    `-ldlocL L`
    `-brtrue.s L`
    `-ldc.i4.0 `
- `HawChow.GTFOKillFeed.CorpseFeedback|System.Void NoteRung(HawChow.GTFOKillFeed.CorpseFeedback/Corpse,System.String)` - string-formatting - UNVERIFIED
    `-dup `
    `+ldarg.0 `
- `HawChow.GTFOKillFeed.CorpseFeedback|System.Void Reset(System.String)` - string-formatting - covered-by-exec
    `-ldlocaL L`
    `-constrained. System.Collections.Generic.List`1/Enumerator<HawChow.GTFOKillFeed.CorpseFeedback/Corpse>`
    `+ldlocL `
    `+box System.Collections.Generic.List`1/Enumerator<HawChow.GTFOKillFeed.CorpseFeedback/Corpse>`
- `HawChow.GTFOKillFeed.CorpseFeedback|System.Void Restore(Enemies.EnemyAgent,System.String)` - string-formatting - UNVERIFIED
    `+ldnull `
    `+stlocL `
- `HawChow.GTFOKillFeed.CorpseFeedback|System.Void Tick(System.Single)` - string-formatting - UNVERIFIED
    `-ldlocaL L`
    `-constrained. System.Collections.Generic.List`1/Enumerator<HawChow.GTFOKillFeed.CorpseFeedback/Corpse>`
    `+ldlocL `
    `+box System.Collections.Generic.List`1/Enumerator<HawChow.GTFOKillFeed.CorpseFeedback/Corpse>`
- `HawChow.GTFOKillFeed.CorpseFeedback|System.Void TryLaunchOrAbandon(HawChow.GTFOKillFeed.CorpseFeedback/Corpse,System.Single)` - string-formatting - UNVERIFIED
    `-brtrue.s L`
    `-ret `
    `+brfalse.s L`
- `HawChow.GTFOKillFeed.EnemyFocus|System.Void Update(System.Single)` - string-formatting - UNVERIFIED
    `-ldnull `
    `-call System.Boolean UnityEngine.Object::op_Equality(UnityEngine.Object,UnityEngine.Object)`
    `-brfalse.s L`
    `+brtrue.s L`
- `HawChow.GTFOKillFeed.EnemyNames|System.Void Initialize()` - string-formatting - UNVERIFIED
    `-brfalse.s L`
    `-ldlocL `
    `+dup `
    `+brtrue.s L`
- `HawChow.GTFOKillFeed.EnemyNames|System.Void Reload()` - string-formatting - UNVERIFIED
    `-ldlocaL L`
    `-constrained. System.Collections.Generic.List`1/Enumerator<HawChow.GTFOKillFeed.EnemyNameRule>`
    `+ldlocL `
    `+box System.Collections.Generic.List`1/Enumerator<HawChow.GTFOKillFeed.EnemyNameRule>`
- `HawChow.GTFOKillFeed.FeedbackPolicy|System.String DamageText(System.Single,HawChow.GTFOKillFeed.HitKind)` - string-formatting - covered-by-exec
    `+castclass System.IFormatProvider`
    `+castclass System.IFormatProvider`
    `+ldc.i4.1 `
    `+beq.s L`
- `HawChow.GTFOKillFeed.HitMarkerHud|System.Void Report(System.String,System.String)` - string-formatting - UNVERIFIED
    `-blt.s L`
    `-ret `
    `+bge.s L`
- `HawChow.GTFOKillFeed.HitMarkerHud|System.Void Reset(System.String)` - string-formatting - covered-by-exec
    `-brfalse.s L`
    `-ret `
    `+brtrue.s L`
- `HawChow.GTFOKillFeed.HitMarkerHud|System.Void TickIcons(System.Single)` - string-formatting - UNVERIFIED
    `-brfalse.s L`
    `-leave L`
    `+brtrue L`
- `HawChow.GTFOKillFeed.HitSound|System.Int16[] LoadCue(System.String)` - string-formatting - UNVERIFIED
    `+ldnull `
    `+stlocL `
- `HawChow.GTFOKillFeed.HitSound|System.Int16[] ReadEmbeddedCue(System.String)` - string-formatting - UNVERIFIED
    `+castclass System.IO.Stream`
    `-brfalse.s L`
    `-ldlocL `
    `+dup `
- `HawChow.GTFOKillFeed.IconPipeline|System.Void Tick(System.String,System.String,System.Single)` - string-formatting - UNVERIFIED
    `-brfalse.s L`
    `-ret `
    `+brtrue.s L`
    `-brtrue.s L`
- `HawChow.GTFOKillFeed.KillIconHud|System.Void LogFrozenAnchor(System.String,HawChow.GTFOKillFeed.KillPresentation,System.Single)` - string-formatting - UNVERIFIED
    `-stlocL `
    `-ldlocaL L`
    `-constrained. HawChow.GTFOKillFeed.KillTier`
    `+box HawChow.GTFOKillFeed.KillTier`
- `HawChow.GTFOKillFeed.KillIconHud|System.Void TickIcons(System.Single)` - string-formatting - UNVERIFIED
    `-brfalse.s L`
    `-leave L`
    `+brtrue.s L`
- `HawChow.GTFOKillFeed.KillTracker|System.Boolean IsFlyer(Enemies.EnemyAgent)` - string-formatting - UNVERIFIED
    `-stlocL `
    `-ldlocL `
    `+dup `
    `+pop `
- `HawChow.GTFOKillFeed.KillTracker|System.Void ObserveState(eGameStateName)` - string-formatting - UNVERIFIED
    `-ldarga.s state`
    `-constrained. eGameStateName`
    `+ldarg.0 `
    `+box eGameStateName`
- `HawChow.GTFOKillFeed.Plugin|System.Void PollConfigHotReload(System.Single)` - string-formatting - UNVERIFIED
    `-beq.s L`
    `-leave.s L`
    `+bne.un.s L`
- `HawChow.GTFOKillFeed.TeammateMarks|System.Void Clear(System.String)` - string-formatting - covered-by-exec
    `-brtrue.s L`
    `-ret `
    `+brfalse.s L`
- `HawChow.GTFOKillFeed.TeammateMarks|System.Void ObserveState(eGameStateName)` - string-formatting - UNVERIFIED
    `-ldarga.s state`
    `-constrained. eGameStateName`
    `+ldarg.0 `
    `+box eGameStateName`

## Not present in both assemblies

- none
