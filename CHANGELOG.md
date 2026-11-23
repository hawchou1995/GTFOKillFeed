# 更新日志

## 1.2.34

- 池已满且没有正在播放的同尸速溶时，启动游戏自己的 `DecayDelayed` 协程接管这具尸体：不新增
  Harmony 补丁，不接管死亡状态机，不建池。日志为 `fade=begun;path=game-coroutine`，关末
  `coroutines=` 单列，只表示启动次数。
- 击杀计数改用原生血条投影落点，取消屏幕角落备用锚点；无法验证落点时暂不绘制，不重置计数。
- 优先接续游戏已启动的同尸速溶，剩余时长最多 0.5 秒；实测该分支在无同尸组件时不命中。

## 1.2.27

- 区分游戏淡出池“实例全忙”和“总数为零”。后者不再按 8 秒周期恢复重试；清理截止时若已有空位或尸体已有可用组件，仍可尝试。持续无实例时仍按原时间隐藏，不创建或扩容游戏池。
- 关末分别统计零实例跳过、等到截止的次数和本模组两种 `Initialize` 重载的调用次数，便于区分实测中的原生异常与本模组行为。

## 1.2.26

- 修复尸爆音效写入 Unity 时误算采样数：只写入 WAV 实际包含的帧数。保留原音效、空间位置、合并播放和失败时的游戏音效回退。
- 修复尸体回收、复用时短暂进入不兼容的刚体碰撞模式；高速击飞仍使用连续检测。
- 没有本插件占用的淡出槽、连续两次观察到游戏池空时，暂缓池化尝试 8 秒；一旦读到空闲实例，下具尸体即可重新尝试。保留尸体自身已有的安全淡出和原清理时间，不建池、不扩池、不抢实例。
- 池化淡出须读回本次目标与新建的有效网格副本。准备失败时，清理和归还前分两次核验所有权，只把已在空闲表中确认的手动归还计为成功。日志把播放、状态未知和停止播放分开记录；历史 `Decay.InitializeInternal` 空引用的调用来源及本版实机效果仍待确认。

## 1.2.25

- **硬直行只显示理论最大阈值，每个分组只取最大值。** `EnemyBar/ShowStaggerLine` 现在每项一个数：`[四肢：40][躯体：50][普通(阈值)：80]`。四肢/躯体取该组**可确认的未毁可破部位**的固定上限 `Dam_EnemyDamageLimb.m_healthMax` 的**最大值**（不再读当前耐久 `m_health`，也不再写成 `当前/上限`；同组上限 40 与 60 **只显示 `60`**，不显示区间、不保留最小值）；普通硬直独立取 `GameData.HealthData.DamageUntilHitreact` 本身，不再读累计值 `m_damBuildToHitreact`、不再做减法。
- **数值是游戏真实值。** 用最短往返表示（`value.ToString("G")` 并回读校验）：`0.0001` 不会被缩成 0，`39.9999` 也不会被抬成 40，大数值保留真实量级而不再截成固定上限；数值宽度仍由现有布局缩放/夹取。
- **没有有效项的整项省略，全部为空则整行不画、不占位。** 不再输出 `—`，也不伪造 `0` 或“免疫”：没有可断部位、部位已毁、部位解剖值未知/模组值、阈值为 0/负数/NaN/无穷，都直接不显示该项；三项都没有时血条下方不绘制任何行。换行方案按实际项数计算（1/2/3 行），不会再产生空行；左端仍对齐怪物名称第一个字（名称隐藏时对齐血条左边），窄屏先缩字号再按组换行并夹取。
- **部位项必须有正向证据。** 只有 `GameData.HealthData.HitreactOnLimbDestruction`（`ES_HitreactType`，本机 interop 已核对：Unspecified=0/None=1/Micro=2/Light=3/Heavy=4/ToDeath=5/InstantRagdollDeath=6）为 **Micro/Light/Heavy** 时才显示四肢/躯体两项；`None`、`Unspecified`、`ToDeath`/`InstantRagdollDeath`、未知/模组值与**开关读取失败**都省略这两项，并打一条有界 `[BAR] stagger=part;shown=no;reason=no-confirmed-limb-hitreact|switch-unreadable;...;note=parts-omitted-not-immune` 回执——**省略不等于免疫**，也不声称该敌人一定没有硬直。`TryForceHitreactOnLimbDestruction` 只核对类型存在，不参与判断。
- **切怪不串用旧数字。** 缓存行记录它属于哪个敌人（`_staggerTargetId`）：切怪时先清空旧行再按新目标重建，目标丢失后该行立即不绘制，不会在淡出或 0.35 s 刷新间隔里沿用上一个敌人的数值。
- **未改动**：真正触发硬直的伤害数字绿字判定（仍需要累计值 `m_damBuildToHitreact` 与 `OnTakeDamage` 决策）、弱点/结沫标签、血条本体、尸体反馈、击杀计数与网络。
- **门禁**：`scripts/Test-EnemyBarStaggerIl.ps1` 断言 `ReadStagger` **不读** `m_damBuildToHitreact` 与 `m_health`、只经 `BarPolicy.IsUsableThreshold`/`LimbBreakIsHitreact`，分组只保留单个 `Max`（`MaxMin/MaxMax/_min` 必须不存在），`ThresholdValue` 不截断不取整，`DrawStaggerRow` 只在 `_haveTarget` 时绘制，`Tick` 记录 `_staggerTargetId` 并在切怪时清空旧行，并逐项核对 interop 的 `ES_HitreactType` 与 `GameData.HealthData` 成员；Smoke 覆盖正常/仅普通/仅部位/混合组/未知部位/非有限值/无有效项/开关 None·Unspecified·死亡类·读失败/极小极大值/切怪/无空行。本版程序集版本为 `1.2.25.0`。

## 1.2.24

- **修复尸体淡出可能导致整个游戏画面变黑的问题。** 开启 `Corpse/FadeEffect` 后，KillFeed 会避开可能破坏阴影绘制的尸体部件和已有的淡出副本；同一具尸体有安全部件就继续淡出，没有则按原清理时间隐藏。不需要关闭淡出，也不改动全局阴影。
- **准星血条分别显示四肢、躯体和普通硬直。** 四肢和躯体显示可破坏部位的当前耐久/上限；普通硬直显示还差多少伤害触发。提示与怪物名称左端对齐，可用 `EnemyBar/ShowStaggerLine` 关闭。
- **真正触发硬直的伤害用浅绿标出。** 合并霰弹只把触发硬直的那部分标绿，例如 `12 + 6` 总伤害仍为 18。保留弱点、护甲、棱彩与档位描边。
- **实测与已知问题：** Survival 的 FX.3 Overload 在开启淡出时未再黑屏，日志记录四次淡出开始，未出现旧阴影异常栈。同局另有一次原生 `Decay.InitializeInternal` 空引用，战斗继续；调用来源及画面影响尚未确定，将继续复测。本局未验证棱彩与档位叠加效果。

## 1.2.21
- 修复计数未显示：1.2.20 只接受 GetWorldCamera，而本机局内 HUD 使用手动 CellUI 画布，此入口为 null。现在核对 m_cellUICanvas 的根层级、相机裁剪层、目标像素、画布布局与完整 viewport 后投影，不误拒绝按需渲染的 disabled 相机，也不使用主相机猜测。
- 细分 health renderer/sprite/parent、画布相机/root/尺寸/viewport 失败原因；placed 记录实际相机来源、三档计数和图标资源状态。探测与绘制均不写入原生血条位置或缩放。
- 消融目标增加 active/enabled、sharedMesh/vertexCount 检查，过滤换模残留或无有效网格的 renderer；这属于输入防护，不能据此声称日志中 Decay.InitializeInternal 的五次原生 NRE 根因已被完全证明或消除。
- 增加直接链接生产 KillCounterHud 的 API 边界重放：旧版相机条件下确认失败，修复版实际执行 Tick/Draw，覆盖三组图标数字、零值、无图标回退、全长血条、门控与各个画布拒绝条件。不是 Unity 实机渲染。
- 发布前实测（2026-11-17，TOA Pure）：1.2.21 成功使用 cellui-manual-verified 放置血条下计数，三张图标资源齐全，远征成功后返回大厅；作者反馈计数基本正常。本模组日志 426 行无 Warning/Error/Fatal，未见 Decay.InitializeInternal 异常栈；DBO 接管消融时按设计让位（skipped=1636、failed=0），未执行本插件原生消融，因此不声称无 DBO 环境的 NRE 已根治，也不把初始 0/0/0 当作全部累计值逐项验证。

## 1.2.20
- 新增 `KillCounter/Enabled=true`：本机玩家血条下方右端对齐显示 Minor / Elite / Boss 三档原有图标与完整击杀数；沿用本机致死结算和 Ledger 去重，F9 预览不计入，离开远征清零。独立开关仅隐藏显示，不阻止累计。
- 批量消融：并发仍限2具，在原EndAt前0.5秒内最多两次预约，加截止点一次终态尝试；只重试暂时满员/空池，不重播已隐藏模型，不延长隐藏/回收截止，不强行归还 playing/unknown 实例。
- 增加不受逐尸体采样限制的 `fade=totals`，分别汇总 started/deferredAttempts/skipped/failed；碎块探针不再把无root/读取失败误记为0。
- 包含此前未升版的逐肢日志限流、跨关音效缓存恢复和原生消融武装修复。发现第三方接管 `Decay.Play` 时仍让位，不改第三方模组配置。
- Code Review 修复：已启动消融的释放状态单调保持，避免运行中热切换击飞设置后在截止点误报 `visual=kept`；IL 门禁验证准备/截止两处 `deferTransient=true/false` 实参，防止容量重试被静默关闭。
- 当时的实机边界：图标尺寸/位置与批量消融观感待作者进图确认；离线预览不是游戏截图。后续计数 HUD 的实测结论见 1.2.21，批量消融在第三方让位场景外仍需实机确认。

## 1.2.19
- **霰弹枪弹丸改为「按次开火的弹丸窗口」合并（跨帧也只有一个数字）**：1.2.17 实机日志（`pellet merge=True`）里一次开火（`Fire captured: fired=1, rays=1`）之后，弹丸伤害在**后续 12 帧**陆续结算（2.85–3.31 各出一个数字），而同帧判据 `fireFrame == Time.frameCount` 在 `Fire` 返回的那一帧就把作用域关掉了，所以每颗弹丸各自成数。现在一次开火开一个 **volley**：`Gear.Shotgun.Fire`/`Gear.ShotgunSynced.Fire` 前缀记录 `startFrame`、`startTime=Time.unscaledTime`、期望弹丸数（能安全读到 `ArchetypeDataBlock.ShotgunBulletCount` 就用它，读不到记 `unknown`）、合并/显示开关与装备类别，volley 在后续帧继续收弹丸。时间窗判据 `FeedbackPolicy.IsShotgunFireScopeCurrent(startTime, now, drained)`：同一 volley 内、`now - startTime ≤ 0.5 s`、未被 drain，**不再要求同帧**。
- **弹丸归属必须同时满足「窗口开着」和「装备类别一致」（review F1）**：只有 `FeedbackPolicy.IsVolleyHit(windowOpen, volleyCategoryId, hitCategoryId)` 为真才算弹丸——已知 volley 类别时异类别命中一律拒绝，**命中自身类别未知（0）也一律拒绝**（这类命中保持立即显示，绝不会被霰弹隐藏开关吞掉）；volley 自身类别未知（无 `GearCategoryData` 的多弹丸武器）时接受，因为开这个 volley 的多弹丸 `Fire` 就是它唯一能给出的证明。修复前：霰弹打空时窗口会撑满 0.5 s，这期间步枪/近战/其它来源的命中被当成弹丸并入 batch（延迟、均值位置、数值相加），且 `ShowShotgunDamageNumbers=false` 时这些数字直接消失。
- **弹丸合并改由「真正的子弹路径」判定（`DamageOrigin`，彻底区分弹丸与近战/爆炸/DOT/推击/EWC 自定义伤害）**：1.2.19 之前的归属仍是「`gearCategoryId` 通配 + 0.30 s 窗口」的近似——开火后 0.3 s 内落地的近战/爆炸/DOT 仍可能被并进霰弹数字，且 `ShowShotgunDamageNumbers=false` 时连数字都不显示。现在伤害根 `DamageRoot` 多一个纯数据字段 `DamageOrigin Origin`（`Other=0` 是字段默认值，所以「没人标记」的根永远不会被当成子弹），在入口处一次性赋值：`Dam_EnemyDamageLimb.BulletDamage` 与 `ReceiveBulletDamage` → `Bullet`，`Dam_EnemyDamageLimb.MeleeDamage` → `Melee`，`ReceiveFireDamage`/`ReceivePushDamage` 用**只读调用作用域**（前缀 push、终结器 pop，不读载荷、不写回、不改任何游戏行为）标成 `Fire`/`Push`，匿名入口 `ProcessReceivedDamage` 新建根时是 `Other`。同一个根被复用（`ReceiveBulletDamage → ProcessReceivedDamage` 的嵌套、肢体路径）时**只能升格不能降级**（`FeedbackPolicy.ResolveOrigin(true, existing, entry, enclosing)`：已知来源永远胜出），所以内层匿名入口绝不会把 `Bullet` 覆盖成 `Other`。归属判据级联为 `FeedbackPolicy.IsShotgunDamageNumber(preview, origin, …)` → `DamageNumbers.Add(..., DamageOrigin origin = Other)` → `ShotgunDamageNumberCapture.TryCapture(..., origin)` → `FeedbackPolicy.IsVolleyHit(windowOpen, volleyCategoryId, hitCategoryId, hitOrigin)`（必须 `hitOrigin == Bullet`）→ `ShotgunVolleyState.TryCapture/IsWindowOpen(..., origin, …)`；**非 Bullet 一律不并入、立刻按普通数字显示，且不受霰弹隐藏开关影响**。0.30 s `MaxPelletCaptureLag`、异枪开火即结算的身份门与类别门全部保留（它们是附加条件，不是替代）。走 `ReceiveBulletDamage` 的 EWC/模组自定义投射物仍按子弹处理（它确实是弹丸）。诊断行 `[FEEDBACK] number=…;origin=…` 带上来源。
- **结束与绘制**：满足任一即 drain 一次并画一个数字（位置取弹丸平均）：距最后一颗 ≥ 0.05 s 静默、超过 0.5 s 上限、同一武器再次开火（先 flush 旧 volley 再开新）。**期望弹丸数不再参与结束判定（review F3）**：`ShotgunBulletCount` 只是原型声明值，实机一次开火的命中事件可能多于它（一颗弹丸同时命中两个肢体），按「够数即结算」会把一枪劈成「一个合并数字 + 一串逐颗数字」——正是本版要修的症状，它现在只作为 `[DN] merged;expected=` 诊断字段。静默与上限的检查放在既有的主线程 `DamageNumbers.Tick`，不新增协程、不加高频扫描；`KillTracker.Reset` 清空 volley，跨局/离开关卡不残留。**如实说明（review F4）**：两次开火重叠时（第二枪在上一枪静默前就开火），旧 volley 会在第二枪开火的那一瞬间被结算，之后落地的弹丸计入新枪——总数正确，划分按开火时刻，不做任何射序猜测。
- **新增两个多弹丸入口**：`Gear.BulletWeapon.Fire(bool)` 与 `Gear.BulletWeaponSynced.Fire(bool)` 前缀（两者在 Test profile interop 里已核实存在且参数为 `bool`），**仅当**该武器自己的 `ShotgunBulletCount > 1` 时开同样的 volley，否则一帧都不介入（狙击/步枪/EWC 单发行为与 1.2.18 完全一致）；同一实例同一帧的第二次 Fire 前缀视为同一次开火，不重复 flush。`ShowShotgunDamageNumbers=false` 仍完全隐藏真实弹丸且不画（异类别命中不受影响），`MergeShotgunPellets=false` 仍逐颗立即显示，一只敌人一枪一个数字、多敌人各自一个；两枪之间以「开火瞬间」为界，重叠时后一枪接住剩余弹丸（见上）。
- **有界诊断（review F6）**：每个**关卡**最多 8 行 `[DN]`（`Reset` 恢复额度，不再是一整个会话）。开火一行 `[DN] volley;frame=..;weapon=..;bullets=..|unknown;category=..`；drain **聚合成一行** `[DN] merged;enemies=..;pellets=..;expected=..|unknown;frames=..;total=..;each=<敌人:伤害,...>`（`each` 最多列 4 个敌人后写 `+N`），因此下一次真机日志里 `volley;` 与 `merged;frames=` 总能成对出现。`frames` 记录该 volley 实际跨了多少帧，可直接证明跨帧假设。
- **门禁与测试**：`scripts/Verify.ps1` 源码门禁（两个新 Fire 目标、`ShotgunBulletCount > 1` 门、volley 不再以同帧为唯一判据、`IsVolleyHit` 类别门进入捕获与状态机、期望弹丸数不再是结束判定参数、drain 只有一个出口、诊断行聚合且随关卡重置）与 IL 门禁 `scripts/Test-ShotgunVolleyIl.ps1`（两个新 patch 存在、`IsShotgunFireScopeCurrent` 的 IL 不读 `Time.frameCount`、`IsVolleyHit` 存在且 `ShotgunVolleyState.TryCapture/IsWindowOpen` 各调一次、`ShouldDrainVolley` 五参、`Reset` 调 `DiagnosticBudget.Reset`、`Drain` 全会话仅一处、无 `StartCoroutine`/`Destroy`/`timeScale`）。Smoke 的 `ShotgunVolleyTests`：同帧多弹丸=1 个且为和、跨 1/3 帧仍 1 个、两枪间隔 0.6 s=2 个、静默 0.05 s 提前 flush、超上限强制 flush、merge=false 逐颗、show=false 全隐藏、单发武器立即显示且不进 volley、多敌人分开、新开火先 flush、同帧双入口算一枪、跨局清空，以及本次 review 的：窗口内异类别/未知类别命中仍立即显示且不并入 batch、不因霰弹隐藏开关消失、同类别正常并入、volley 类别未知时接受、弹丸数 =／<／> 期望值都只产生一个合并数字。
- **修复实机「布娃娃被自己的 R3 守卫误杀」（rung 1 改为信任游戏自己的引用）**：1.2.18 的活体层级启发式（宿主是模型祖先/子孙即拒）也作用在 `EnemyAgent.RagdollInstance` 上。实机 9 次 `schedule=accepted` 里 3 次（sid 1/4/7）被它判成 `rung=ragdoll-instance;reason=instance-under-living-model`，随后 `ragdoll=final-probe` → `launch=unavailable;attempts=17;haveRagdoll=no`。死后游戏正是把布娃娃挂在模型之下，所以 rung 1 现在只按引用本身判定：有引用 + 租约 `BodyCount>0` 即接受；只拒绝可证的假候选，并各自具名——`instance-is-model-root`（引用就是活体模型/敌人根）、`instance-no-bodies`（租约无刚体）、`instance-unreadable`（读引用抛异常/原生对象已销毁），引用为空是死亡当帧的正常协程状态、不占日志额度。rung 2–4（`RagdollScript`/`EnemyRagdollRef`/裸 `Rigidbody`）的活体与 `ragdolled` 门完全保留。
- **修复消融的两个实机根因（不再 `Decay.Initialize`，池化路径 fail-closed）**：实机 `fade=native-unavailable;reason=no-decay-component` → 池化借用 → `fade=failed;reason=initialize-threw;message=System.NullReferenceException`（sid 2/3）。①尸体自带 `Decay` 的查找范围扩到 **布娃娃租约根** → `ModelRoot` → `EnemyRoot`（死后 Decay 常挂在布娃娃上），找到就用它播放；②**池化借用一律不播放（review F2）**：借来的实例 `m_targetRenderer` 指向上一次溶解的克隆渲染器，无法证明属于当前尸体，而唯一能绑定的 `Decay.Initialize` 已被禁用且实机抛 NRE——所以 `DecayFadeTarget.Ready` 恒为 `false`（IL 门禁钉死：不调用任何方法、不读任何 Decay 状态），借用实例只会被探测并**原样归还一次**，并打印终态 `fade=skipped;reason=pool-unverifiable;note=pooled-instance-unverifiable`；③全插件 `Decay.Initialize` 调用数为 **0**；④终态 `fade=` 行改用**独立额度**（review F5）：`fade=fallback`／`fade=native-unavailable` 这类回执只花有界原因预算，每关前 8 具尸体的终态行无条件打印，且**一个 `scheduleId` 只能领取一次终态行**，结构上保证「每具尸体恰好一条终态」。异常仍 fail-safe 归还、不 `Destroy`、不改 `timeScale`、不阻断 `Hide`/`RecyclePhysics`/队列收尾。`no-free-instance` 语义不变（池容量问题）。
- 本版程序集版本为 `1.2.19.0`。**未做实机验证**：自定义/模组霰弹是否真的跨帧结算、0.05 s 静默阈值的手感、`ShotgunBulletCount` 与实机命中弹丸数的偏差（现在只影响 `expected=` 诊断，不再劈开数字）；异类别命中是否真的立刻出数（`[DN] merged;pellets=` 应只等于真实弹丸数）；尸体两项修复——被误杀的 3 具（sid 1/4/7）是否恢复击飞、布娃娃根上的 `Decay` 是否真被找到并播放、`fade=skipped;reason=pool-unverifiable` 是否取代了 NRE（即消融是否只剩原生这一条路）——均需实机确认；本版尚未打包发布。
- **此前 1.2.19 本地热修（未单独升版）已恢复池化消融**：`Ready` 使用 TryCast、空 IRF 表与目标指针回读，拒绝身份不符并将武装异常独立命名为 `pool-arm-failed`（failed，不是 skipped）。2026-11-17 复核 Survival 日志已有两次池化 `fade=begun`；原生 NRE 内部根因、长期回池与本版提前预约观感仍未由这段日志证明。此记录仅说明历史本地构建，不代表所有同号 DLL 字节相同。

## 1.2.18
- **卡片与头顶图标成为两个独立开关（旧键自动迁移）**：新增 `Card/Enabled`（默认 true）与 `KillIcon/HeadEnabled`（默认 false），两者可同时开、同时显示。旧 `KillIcon/Display` 继续按旧语义生效（`Head`/无法识别 = 只开头顶图标，`Card`/`Cards` = 只开卡片），迁移状态写入 `KillIcon/ViewMode`（`Legacy:Head`/`Legacy:Card`/`Explicit`）；显式设置两个新开关后旧键即失效，程序不改写用户既有值。启动与每次热重载打印一条 `[KILLVIEW] head=…;card=…;mode=…;headSource=…;cardSource=…` 回执。
- **音效拆成命中/击杀两路（旧开关与音量继续有效）**：`HitSound/Enabled` 与 `HitSound/Volume` 仍是总开关与主音量（主音量 0.8 = 100%，通道输出 = 通道音量 × 主音量 ÷ 0.8），新增 `HitSound/HitEnabled`、`HitSound/KillEnabled`、`HitSound/HitVolume`、`HitSound/KillVolume`。关键约束：配置**在第一次 `Config.Bind` 之前**整文件预扫（`SoundConfigPlan`），否则 BepInEx 会把新键默认值立刻写盘、旧 cfg 被误判成“已选过新键”。旧 cfg 迁移后两路默认开启、音量 0.8（输出与 1.2.16 逐值相同）；全新 cfg 两路默认关闭、0.4；文件不可读时按旧行为处理而不是静音。启动打印 `[HITSOUND] config migration:` 与 `[HITSOUND] effective:`。
- **命中标记的命中/击杀分路**：新增 `HitMarker/HitEnabled`（默认 true）与 `HitMarker/KillEnabled`（默认 true），`HitMarker/Enabled` 仍是总开关；`Submit` 只走一个门（总开关 + 分路 + 已解析的 kind）。关掉某一路时该事件保留原版标记，不会静默丢失。
- **尸体五个子开关**：`Corpse/Launch`、`Corpse/Dismember`、`Corpse/Chunks`、`Corpse/BurstVisual`、`Corpse/BurstSound` 全部默认 true（与 1.2.16 行为一致）；`Corpse/Enabled` 仍是父开关，`Corpse/ExplosionFx` 仍是两路爆裂的总开关。关掉击飞只去掉初速与逐帧限速，不影响模型租约、hidden 回收、断肢与碎块；每 tick 只解析一次开关，热改不会在一具尸体的爆裂中途切换通道。
- **头顶图标不透明度与可见 hitbox 避让**：新增 `KillIcon/Opacity`（默认 0.5，只与生命周期淡入淡出相乘一次）。每帧只收集一次可见怪物 hitbox：与图标框重叠时整体上推，屏幕顶端放不下时本帧安全隐藏（不强行开卡片、不画在身体上）；候选集合不完整的那一帧同样只隐藏本帧。注册表按敌人注册/注销，96 敌 / 每敌 48 collider 上限、seed 一次与有界指数退避重建。
- **门禁与测试**：`scripts/Verify.ps1` 全套 `VERIFY=PASS`（Smoke 1191 条断言、ConfigSmoke 97 条真实 BepInEx 断言、6 个 IL 门禁）。三条门禁按新模型改写：不再断言共享 `Display` 派生的 `CanDrawNow`/`ShouldDrawCard`，改为断言 `KillViewPolicy.CardVisible/HeadVisible` 两路门、每帧一次候选收集、F9 两路预览。
- 本版程序集版本为 `1.2.18.0`。**未做实机验证**：避让上推幅度、0.5 不透明度的观感、两路音量实际响度、真实旧 cfg 的迁移表现均需实机确认；尚未打包发布。

- **头顶击杀图标改为「击杀瞬间冻结」**：`KillIcon/Display = Head` 的图标现在在**本地致死 credit 的那一刻**只取一次世界点（优先敌人自己的 `m_headLimb` 头部点；取不到时用致死伤害事件已经携带的世界坐标并抬同样的 clearance），之后**不再逐帧重读活体/尸体头部**，只按相机重投影。1.2.15 会跟着死亡模型走前 60% 生命周期（默认 2.2 s ⇒ 跟随 1.32 s），尸体倒地时图标被拖着一起下滑；这与源 BFV 模组「完全不做世界坐标跟随」的语义不符，现已对齐。对象池指针仅作身份审计、不再参与寻位，因此复用对象不会把图标拽到新敌人身上；相机后方/屏幕外、对象被销毁、贴图缺失仍安全跳过并回退卡片，`HeadGapMetres`/`HeadLifetime`/`HeadMaxIcons`/F7/F9 语义与 `Display = Card` 均不变。取不到头顶锚点时仍走已有的卡片回退，**绝不**用屏幕中心伪造。新增一次性 `[KILLICON] anchor=frozen;source=head-world|lethal-position` 回执（每次会话最多两条，便于实机对账）。
- **击飞距离按配置真实生效**：`Corpse/MinLaunchDistance`/`MaxLaunchDistance` 不再被 12 m 地板覆盖（1.2.15 把 `3.8` 抬成 `12`、把 `8.5` 压成 `12`，导致两档相同、伤害不再影响预算）。现在接受 **0.5–30 m**（`Max` 不低于 `Min`），默认仍是 `12`/`18`；预算按 drag 与滞空时间**反解成水平初速**施加到布娃娃上，标准体型、典型强度下实际落点≈预算（例：`3.8 m` ⇒ 约 `4.8 m/s`、`8.5 m` ⇒ 约 `10.8 m/s`、`12 m` ⇒ 约 `15.2 m/s`），保留体型 `TravelBias`、`TravelScale`、`OffSceneOrigin` 100 m、`ExplosionFx` 关闭语义与 hidden/lease/recycle 行为。原来的 **9 m/s 全向量硬上限**改为由预算推导（`max(9, 速度×1.15)`），绝对上限仍是 **40 m**；只在极端配置（如 40 m）触发的 42 m/s 安全速度上限会在日志里报 `capped=speed`，不再静默压平。`[CORPSE]` 日志新增 `travel=`/`speed=`/`distance=`。
- **修复实机「0 次真正击飞」**：1.2.15 在死亡后 40 ms 才去读 `EnemyAgent.RagdollInstance`，实机 97 次调度里 `launch=applied` 为 0（53 次 `ragdoll-instance-unavailable`、44 次 `enemy-null-fallback`）。根因是**布娃娃由 `EnemyRagdollBodyController.LinkToSpawnedRagdoll` 协程异步生成**，死亡回调当帧必然为 `null`；而 1.2.16 首次修复只把重试窗口绑在 burst 点之前（实机配置 `ExplosionDelay = 0.1` ⇒ 窗口仅 `[0.04, 0.1)` s，60 fps 下 3 帧、一次 90 ms 卡顿就没有了），实机因此仍有 28 次 `launch=unavailable;reason=no-ragdoll-instance;attempts=0`。现在：①死亡调用内先快照敌人指针/transform/模型 transform/方向/强度/预算，重试期间即使 `EnemyAgent` 包装体被销毁也继续用快照搜；②重试窗口与 burst 解耦，截止到 **`BurstAt + 0.30 s`**（`RagdollLookupGrace`，具名常量），仍受 `MaxRagdollLookupAttempts = 8` 上限；③取物按**阶梯**逐级尝试并缓存成功的那一份（同一次击杀只解析一次）：`EnemyAgent.RagdollInstance` → `RagdollScript`（优先 `ragdolled == true`）→ `EnemyRagdollRef`（要求 `m_rigidBodyLookup` 非空）→ 子层级 `Rigidbody`（`IsLivingModelHost` 只把敌人/模型这两个根排除在外；这是范围限定，不是「绝不碰活体」的完整保证）；④日志三态可对账：`ragdoll=null@death;reason=…;ladder=…` / `ragdoll=late@attempts=N;firstLookupAge=…;ladder=…` / `launch=unavailable;reason=<enemy-destroyed|no-ragdoll-instance|no-rigidbody|capture-failed|enemy-offscene-cleanup>;attempts=N;firstLookupAge=…;ladder=…`，成功的 `launch=applied` 追加 `attempts=`/`firstLookupAge=`。死亡统计、Ledger 去重与 `InstantDead`/`OnDead` 双钩顺序未改动，不新增网络包、不改变伤害。**`launch=applied` 的真实比例仍需实机确认**。
- **图标口径统一 + 注册回执**：卡片图标与头顶图标现在**只**由同一个 `KillPresentation.Tier` 经同一张 `tier → BFV raster` 映射决定（1.2.15 卡片按 classification 选图、头顶按 tier 选图，同一次击杀会出现两套图标）；血量三档（MINOR/ELITE/BOSS）是唯一主口径，**弱点不参与图标选择**，只在卡片标题体现（`WEAKPOINT KILL`）。击杀类型、飞行徽标与音效仍读同一个 `KillPresentation`。另为每个注入的 MonoBehaviour（`KillHud`、`CorpseVisualLease`、`HitMarkerHud`）补了 `[INJECT]` 注册回执（含 `ClassInjector.IsTypeRegisteredInIl2Cpp` 的真实结果），并说明实机日志里的 `[Error :Il2CppInterop] Assembly GTFOKillFeed.dll is not registered in il2cpp` 来自 `Il2CppClassPointerStore<T>` 对本程序集的类名查询（返回 `IntPtr.Zero` 后仍由 `ClassInjector` 完成注入），属于良性噪音，现可被回执直接核对。
- **头顶图标不再画弱点小横条（弱点仅在卡片标题体现）**：本版评审期间曾在图标上方加一枚琥珀色小短杠（`#E6AB57`）作为弱点次级标记，实机评审认为它与卡片标题 `WEAKPOINT KILL` 重复、只是多一个噪点，故**整条删除**——连同只服务于它的琥珀色常量、`WeakspotMark*` 像素方法，以及 `KillIconSlot` 里只为它存在的 `Kind` 字段（不留死状态）。头顶图标回到两件事：tier 族图标 + 飞行徽记。弱点判定、卡片标题、`[KILLICON]` 回执里的 `kind=` 与音效口径均未改动。IL 门禁新增**反向断言**：`KillIconHud`/`KillIconPolicy` 中不得再出现 `WeakspotMark`/`DrawWeakspotMark`。
- **预期噪音降级、GibData 说明**：`schedule=skipped;reason=duplicate-death-callback`（实机 163 次）与 `hidden=pruned;reason=enemy-null`（43 次）分别改为按原因计数、只记前几条与每 N 条一次（`seen=` 带总数），去重与回收逻辑一字未改；`dismemberment` 在「匹配到 destruction setup 但没有 GibData 条目」时不再逐尸体打印 `gibDataCalls=0;gibsCaptured=0`，而是每关一次 `gibdata=unavailable;reason=matched-setup-has-no-gibdata`（本地网格破坏、FX 与布娃娃路径均正常）。
- **代码审查第二轮修复（击飞）**：①迟查布娃娃的重试不再被放弃条件挡住——`age ≤ BurstAt + 0.30 s` 期间**每帧重试**（上限 8 次），放弃由独立谓词 `ShouldAbandonRagdollLookup` 在同一个截止点决定（本节后续又因实机 `attempts=0` 把窗口与 burst 彻底解耦）；②位置夹回半径改为**不小于尸体自身体型**（`max(2.5, 2.5×sizeFactor)` m），自然倒地/滑行不再被吸回球面并清零速度，`Limit` 仍是夹回而非推力；③高速飞行改用**连续碰撞**（单步位移 > 0.25 m 时把该刚体设为 `ContinuousDynamic`，释放时还原 `collisionDetectionMode`），30 m 预算 37.9 m/s ≈ 0.76 m/步不再穿墙；④`launch=applied` 的 `path=` 改为真实来源（不再可能打成 `enemy-destroyed`）；⑤亚 0.5 m 预算不再被初速下限抬起（速度下限降到 0.1 m/s，仅作正数保护）；⑥日志拆成 `budget=`（配置/体型偏置）与 `appliedTravel=`（安全夹取，≤40 m），并新增 `collision=`。
- **代码审查第二轮修复（图标/卡片）**：①图标被提前淘汰时卡片寿命延长到 `max(卡自身寿命, 图标到期+fade)`，一次击杀不会既无图标也无卡片；②飞行徽标按**最终像素**取比例，不再被 26–76 px 的图标夹取二次压缩（4K 下不再缩水约 37%）；③无相机（或 Card 模式）时图标本帧不可能绘制，卡片据此视为「未呈现」并顶上；④Head 模式下 F9 **不再排队预览卡片**（预览卡在此模式永不绘制，只会占用槽位、挤掉真实回退卡），九宫格图标预览保留；⑤卡片飞行徽标统一画在图标框**下方**，卡片高度预留该行；⑥卡片测宽改用**实际绘制标题**（含 `HEAVY KILL`），不再因长标题压到 8 px 或裁切；⑦删除 `KillIconQueue.SetReal`（1.2.15 逐帧跟随的入口），门禁加「除 Push 外任何方法不得写 X/Y/Z」的类型级断言。
- **阶段号说明**：代码注释/门禁里曾出现的内部阶段名 `1.2.17`（受击部位线条）与 `1.2.18`（头顶击杀图标）**全部并入 1.2.16 发布**；两个 IL 门禁的表头已统一为 1.2.16，与 `version_number` 一致。
- 本版程序集版本为 `1.2.16.0`。**未做实机验证**：画面（图标是否稳定/飞行徽记观感）、手感（击飞距离与速度曲线）、听感（音效未改但未复听）均需实机确认；本版尚未打包发布。
- **投掷真正发生且必然可观测（第二轮实机修复）**：实机 1.2.16 日志 `schedule=accepted 47`、`burst=complete 47` 但 `ragdollBodies=0` 且**全日志 0 条 `launch=`**。三个根因全部修掉：①`Ragdoll != null && BodyCount == 0` 的租约既进不了「施加」也进不了「放弃」分支（19 次静默），现在租约状态被归一为 `Missing/Empty/Usable`（`FeedbackPolicy.ClassifyRagdoll`），空租约**丢弃并重新走阶梯**；②条目在 `age=0.19 s` 因 `model-lease-stale` 被整条移除——那是**游戏把活体模型切到布娃娃时禁用了我们持有租约的对象**，现在「模型可视租约」与「布娃娃租约」分离：模型租约失效只丢句柄，布娃娃租约失效转为 `lease-stale` 重新解析，敌人 despawn/setup 的 `Restore` 也不再取消**尚未投掷**的尸体（`queue=kept;note=pending-launch-survives-despawn`）；③投掷时机从 `burst` 前 0.04 s 提前到 **`age ≥ 0.02 s`（第二帧）**并**与 burst 特效解耦**，一旦拿到可用布娃娃立刻施加；若随后该对象被游戏收走、阶梯又解析到**新的**布娃娃，会且只会在窗口内再施加一次（`MaxLaunchApplications = 2`，日志 `application=#2`）。所有移除路径统一经 `CloseLaunch` 收口（`RemoveQueued` 与 EndAt 分支共用同一个收口助手，EndAt 不再只靠 0.75 s > 0.30 s 的算术巧合）：`launch=applied` 或 `launch=unavailable;reason=…;attempts=…;haveRagdoll=…;bodies=…` **二者必居其一**，不再有静默消失（含 `reason=no-bodies`）。`ladder=` 现在列出**尝试过**的所有级别（`ragdoll-instance|ragdoll-script|…`），命中级别单独打成 `ladderHit=`。
- **消融改为「游戏原生 Decay 溶解」**：不再只依赖 `Ragdoll.PrimaryRenderer()`（无布娃娃的尸体因此连 renderer 都没有）。现在优先驱动**尸体自己身上**的 `Decay` 组件：反射确认的真实成员为 `Initialize(SkinnedMeshRenderer|MeshRenderer, List<IRF.InstancedRenderFeature>)`、`Play(float)`、`m_playing`、`m_stopAction`、`m_autoCleanup`（Test profile interop 只读核对），**不借池、不校验 target renderer**，随后用 `m_playing` 确认溶解真的开始；拿不到组件/不安全/已在播放各打一条 `fade=native-unavailable;reason=…`（`no-decay-component` / `unsafe-decay-component` / `decay-already-playing`）后回落到原来的池化路径；只有原生路径调用失败（抛异常、`m_playing` 未置位、渲染器类型不支持）才打 `fade=fallback;reason=…`，池化路径也不成立时才走旧的瞬间隐藏。安全闸：该组件挂在尸体自己的 GameObject 上，`StopAction.Deactivate/Destroy` 会连带清掉尸体，因此**只接受 `StopAction.None` 且 `autoCleanup = true`**，否则明确拒绝（`fade=native-unavailable;reason=unsafe-decay-component`）。`Corpse/FadeEffect = false` 时**零调用**。**不 Harmony patch 任何 `ES_*` 方法**（仓库既有约束：`ES_*` detour 会在加载期触发原生 AccessViolation），门禁新增反向断言 `ES_PATCHES=0`。
- 本版程序集版本仍为 `1.2.16.0`。**未做实机验证**：投掷的真实落点/手感、消融观感（是否与参考模组视觉一致）、`launch=applied` 的比例、原生 Decay 路径在实机上是否被 `unsafe-decay-component` 挡下（取决于敌人物件序列化的 `m_stopAction`/`m_autoCleanup`，离线不可读）均需实机确认；本版尚未打包发布。
- **第三轮代码审查修复（同一 1.2.16，全部离线可复现）**：①**冲量只有一个入口**——`TryLaunchOrAbandon` 里两处 `ApplyLaunch` 合并为一处，统一由 `FeedbackPolicy.CanApplyLaunch` 判定（`applications <= 0` 或 `ShouldReapplyLaunch`），迟查分支不再能绕过 `MaxLaunchApplications`（旧形态下同一具尸体最多可被推 8 次）；②**租约身份改为「指针 + generation」**（`FeedbackPolicy.IsSameLaunchLease`）——游戏 `Capture()` 重取同一对象会让 generation 自增，旧的指针比较把第二次冲量永久吞掉而日志仍报 applied；③`Restore` 只为真实 `enemy-despawn` 保留待投尸体，且保留时同时丢弃 `Enemy/EnemyRoot/ModelRoot/Model` 句柄（对象池把同一个 `EnemyAgent` 交给下一个敌人生成，旧形态下断肢/关 renderer/kinematic/`Decay.Play` 可能作用到**活体敌人**），`enemy-setup` 一律收口；④**EndAt 分支也走收口**：`RemoveQueued` 与 EndAt 共用 `CloseLaunch`，不再只靠 `EndAt(0.75 s) > 查询截止(0.30 s)` 的算术巧合，并断言 `CorpsePostBurstHold > RagdollLookupGrace`；⑤`LogUnavailableLaunch` 置 `LaunchClosed`，`LaunchState` 收进 policy，同一实体不会先打 `launch=unavailable` 再打 `launch=pending`；⑥`fade=native-unavailable;reason=no-decay-component` 现在**真的会打印**（1.2.16 的回执被 `native != null` 挡住，策略里却写着可上报）；⑦`Plugin.Unload` 复位 `KillTracker`（含 `CorpseFeedback.Reset`），隐藏/kinematic 的池化布娃娃在插件卸载时有回收路径；⑧门禁补齐：单一冲量入口与其组成、EndAt 收口、全会话禁止 `CorpseFeedback*` 调用 `Decay::Play/Initialize`（只允许 `CorpseFade`）、`ladder=` 的值语义（`LadderText` 不返回空串）、整 tick 收口测试改为驱动生产谓词（旧测试重写了 `applications == 0 ||` 这一行，正是它盖住了第①项）。上述修复合计**未做实机验证**。
- **伤害数字的「飞向视点」轨迹改为可选、默认关闭（`DamageNumbers/ApproachEffect`，默认 `false`）**：关闭时回到初版行为——数字在命中点浮现，按原公式（`Side*(0.35+age*1.7)` 与 `-95*age+105*age²`）上飘并淡出，不朝屏幕中心汇聚、不做穿身两段、不随飞行放大（尺寸只用 `FontSize` 与弹出缩放）；棱彩数字在关闭态下同样只上飘，箔材质仍按 `DamageFx/PrismaticEnabled` 决定。改成 `true` 才是本版此前那套表现（普通数字随机穿身、棱彩朝视点俯冲并放大到 2.45×）。默认值用 `FeedbackPolicy.DefaultApproachEffect` 钉住，`DamageNumberApproachTests` 用一份**冻结的初版公式**逐值锁定关闭态（6 种子 × 3 分辨率 × 3 命中点 × 3 命中类型 × 3 时刻 = 486 采样），并断言开启态确实改变位置、关闭态字号永不大于开启态；`PrismaticIncomingTests` 的 72 行 1.2.15 基线改为在**开启态**下运行，所以两态都有人说真话。透明度／字号／粗细／颜色／弱点与护甲装饰／队列上限／`Fade` 曲线均未改变。
- **棱彩与档位可分别关闭（`DamageFx/PrismaticEnabled` 默认 `true`、`DamageFx/TiersEnabled` 默认 `true`）**：`PrismaticEnabled=false` 时不再画箔（8 向粉彩外圈、光晕环、浮雕高光、粉彩主体），也不做棱彩轨迹与飞行放大，该数字按普通数字渲染；它与 `PrismaticThreshold` 是「与」的关系（阈值为 0 或开关为 false 都关闭）。`TiersEnabled=false` 复核为**同时**去掉档位主体色与档位描边（含棱彩开启时内圈那一圈档位色），累计值仍照常记录。三个开关（棱彩／档位／轨迹）两两独立，交汇处抽成纯策略 `DamageFxPolicy.Paint` 并由新测试穷举四档 × 开关组合；两个开关各打一条**有界** `[DAMAGEFX]` 回执（加载时一行、之后只在值真的变化时一行），保存即热重载且对屏幕上已有数字即时生效。
- **所有配置项描述改为中文**：`Plugin.cs` 里 94 个 `Config.Bind` 的 description 全部改为中文，写明取值范围、默认值、是否热重载与关闭后的效果；**节名与键名一字未改**（旧 `.cfg` 的既有值全部保留，两个新键只在缺失时取默认 `false`/`true`），BepInEx 的 `# Default value:` 机制不变。`VERIFY` 新增门禁：扫描每个 `Config.Bind` 的第 4 个参数必须含中文，并钉住两个新键的默认值来源（`FeedbackPolicy.DefaultApproachEffect=false`、`DamageFxPolicy.DefaultPrismaticEnabled=true`）与 `DamageFxPolicy.Paint`／三参 `IsPrismatic` 的存在。
- 本版程序集版本仍为 `1.2.16.0`。**未做实机验证**：默认关闭轨迹后的观感（上飘+淡出是否与初版一致）、开启轨迹的手感、关闭棱彩/档位后的画面、`.cfg` 中文注释在游戏内的显示，均需实机确认；本版尚未打包发布。
## 1.2.15
- **击杀图标换成 BFV 三档**：普通击杀 = BFV `light`（白骷髅）、弱点击杀 = BFV `medium`（橙骷髅）、**飞行物/BOSS 单位 = BFV `heavy`（红齿轮）**。重型线由 `KillIcon/HeavyHealthThreshold`（默认 `1000`，与 CombatPanel 的 elite 线一致）判定，用的是敌人自己的 `HealthMax`；夹取 1–1000000000，`NaN` 回落 1000，保存即热重载。几何取自 BFV 素材的 64×64 矩形块表（`Assets/icons/bfv-icon-runs.json`，构建期用 `scripts/Build-BfvIcons.py` 渲染成 128×128），不重绘、不引入第三方贴图。
- **命中标记换成 BFV 三档并替代原版**：护甲命中 = BFV `vehiclehit`（青色四角括号）、普通命中 = `hitmark`（白 X）、弱点命中 = `killhitmark`（橙 X，放大 18%）。位置在准星正中、尺寸按 1080p 的 `HitMarker/Size`（默认 64 px）；开启时通过 Harmony 抑制原版 `CrosshairHitIndicator.PlayHit/PlayDeath`，关掉 `HitMarker/Enabled` 就恢复原版，绝不会出现"两个标记叠着"或"没有任何反馈"。三档靠**形状 + 尺寸 + 颜色**三重区分：`ColorNormal` `#FFFFFF`、`ColorWeakspot` `#FF8C00`、`ColorArmor` `#49ADC9`。
- **命中/击杀音效改为默认关闭**：`HitSound/Enabled` 默认由 `true` 变 **`false`**，`MuteVanillaIndicator` 同时由 `true` 变 **`false`** —— 默认安装时原版命中叮声照常，没人会被"既没新音效、原版也静音"卡住；想要 BF 手感就把 `HitSound/Enabled` 和 `MuteVanillaIndicator` 一起打开。两个开关依旧互相独立（指令级门禁 `HIT_SOUND_MUTE_IL` 仍然断言静音路径不读音效开关）。
- **伤害数字弹道重做**：普通数字走一条**随机弹道穿过你的身体**——落点固定在屏幕中心下方 10% 高度（视角偏下），并且**继续飞出**到身体下方才淡出；棱彩数字仍是"固定砸向视点 + 随机来袭方向"。两段路径在穿身处严格连续，每个数字的随机角度按自己的种子固定，不会逐帧抖动。
## 1.2.14
- **四档阈值可配置（`DamageFx/WhiteThreshold`、`SilverThreshold`、`GoldThreshold`、`DiamondThreshold`，默认 `3000`/`5000`/`10000`/`20000`）**：本关累计伤害到哪一档由这四个键决定，不再是写死的数字。每个值夹取到 1–1000000000，写 0 或负数回落该档默认；写反序（例如把 `SilverThreshold` 填得比 `WhiteThreshold` 小）会自动抬成不减序列，避免某一档永远进不去。四个键都热重载，保存后屏幕上的数字下一帧就换档。\r\n- **倒地标记图标扩到四选**：`TeammateMark/Icon` 现在接受 `Cai`（菜，默认）、`Ruo`（鶸）、`Si`（死）、`Sha`（殺）。`Si`/`Sha` 对应随包新图 `Assets/icons/teammate-downed-si.png` 与 `teammate-downed-sha.png`（与菜/鶸同一套 128×128 规格）。除原名外还接受汉字本身（`死`/`殺`/`杀`——简体`杀`也映射到`殺`）与拼音/编号别名 `cai/ruo/si/sha/sha1`、`weak`、`death`、`kill`、`1/2/3/4`；未知值仍回落 `Cai`。填 `Icon = Si` 即可让倒地队友头顶显示「死」。
- **`TeammateMark/CustomIconPath` 支持相对路径（双轨解析）**：以盘符/根开头的值（`C:\...`）仍按本机绝对路径使用；**其他任何值按相对路径处理**，相对插件目录的 `Assets/icons` 解析——填 `teammate-downed-sha.png` 就是随包殺图，填 `mymark.png` 或 `subdir\own.png` 则指向 `Assets/icons` 下的同名文件。UNC/网络路径仍被拒绝（断链共享会卡住探测）；空值与不可读文件仍回落所选随包图，热重载与坏文件退避语义不变。
- **随包标记图更新**：四张倒地标记 PNG（`teammate-downed-{cai,ruo,si,sha}.png`）全部为 128×128 RGBA，作为已验收源资产随包；构建只校验尺寸与哈希、绝不重绘，包内字节与源文件逐字节一致。
- 击杀卡片图标（`kill-normal.png`/`kill-weakspot.png`）与倒地标记互不影响，本版零新增 Harmony、零发包。
- 上面四项（倒地标记四选）已随线上 `1.2.12` 发布，本版在它之上叠加下列改动。
- **命中/击杀音效替换（`HitSound/*`）**：内置 3 套风格 × 3 类反馈 = 9 个 PCM WAV（48000 Hz 单声道 16-bit；命中 120 ms、弱点击中 200 ms、击杀 450 ms），在本地玩家命中/弱点击中/击杀时播放。`Style` 取 `BF1`/`BF5`/`BF6`（默认 `BF5`，非法值回落 BF5），`Volume` 默认 `0.8`（0–1），`MinIntervalSeconds` 默认 `0.05`（0.02–3.0）。音源本身只对击杀音提供了完整风格变体、对 BF1 额外提供了专属命中音，所以 BF6 命中音与 BF5 相同、三套弱点击中音相同（详见详情页"命中/击杀音效"一节）。
- **默认静音原版命中提示层（行为定义，务必知悉）**：`HitSound/MuteVanillaIndicator` **默认 `true`**，只跳过 `BULLETHITENEMYINDICATOR`、`BULLETHITENEMYLOCALINDICATOR`、`BULLETHITENEMYWEAKSPOTLOCALINDICATOR` 这三个"命中确认叮声"。子弹打在敌人身上的冲击音（`BULLETHITENEMYLOCAL`/`SYNC`、`BULLETHITENEMYWEAKSPOTLOCAL`/`SYNC`）、敌人受伤/死亡语音与其它所有事件**完全不动**；想保留原版叮声把它改成 `false`。该开关与 `HitSound/Enabled`、`Volume`、`Style` 相互独立——关掉新音效不会让原版提示音回来。
- **伤害数字改为**朝玩家视角飞来**：从命中点滑向准星、途中放大到 1.45×，末段淡出；每个数字落在准星上方自己的槽位，连射时不会叠成一团（旧版是向上飘）。
- **棱彩是材质效果，不是换色**：触发棱彩的数字会画成**全息箔**——8 个方向各一种粉彩色组成外圈（1.6 px 核心环 + 3.1 px 半透明光晕环）、一个随相位旋转的白色浮雕高光、主体是接近白的粉彩核心并带呼吸微光；血条棱彩描边把整圈周长切成 20 段、色相沿边连续推进，厚度提升到 `clamp(BarHeight × 0.55, 2, 5)` px（默认 5 px 血条下从 1.5 px → 2.75 px），否则色阶在 HUD 上根本看不出来。
- **棱彩伤害数字（`DamageFx/PrismaticThreshold`，默认 `60`）**：单次伤害**严格大于**阈值时该数字按 1.8 秒一圈的色相循环绘制；`60.0` 不触发、`60.01` 触发，`NaN`/负值/无穷不触发，写 `0` 关闭该效果。
- **棱彩数字的来袭方向随机**：棱彩数字的落点固定为同一个点（准星上方 4 px，与槽位无关），飞过来的方向按每个数字自己的种子在 ±34° 内随机；角度随飞行进度增长，落点不受影响，同一次数字每一帧方向不变。
- **伤害数字粗细（`DamageNumbers/FontWeight`，默认 `1`，范围 0–2）**：`0–0.5` 换成 Normal 字面并把描边收到 0.9–1.35 px（细），`1` 与现在完全一致（Bold + 1.6 px 描边、无额外叠印），`>1` 额外把本体在半径 0–0.55 px 的小圆上叠印 4 次（真正加粗笔画，不只是加粗阴影），描边到 2.3 px。越界夹取、`NaN` 回落 `1`，保存即热重载，已在屏幕上的数字立刻变粗细。
- **累计伤害档位（`DamageFx/TiersEnabled`，默认 `true`，含行为定义）**：本关累计伤害达到 `3000`/`5000`/`10000`/`20000` 时伤害数字进入 **白/银/金/钻** 四档（取最高档）。**累计值按关卡重置**：换关/重开/插件重载清零，暂停、EMP 隐藏与配置热重载**不会**清零。叠加规则：**进档后所有伤害数字（普通／弱点／护甲）主体色统一换成档位色**并以同色描边标出档位，弱点与护甲改由 `!`、`[ ]` 与字号区分，棱彩优先主色；两个效果都未触发时绘制结果与 1.2.11 逐值一致。
- **血条专注描边（`EnemyBar/FocusOutlineEnabled`，默认 `true`）**：准星射线落在敌人背部（朝向反半球）→ 橙色 `#FF8A1E`；落在弱点 limb → 红色 `#E5484D`；两者同时成立 → 棱彩。厚度 `clamp(BarHeight × 0.30, 1, 3)` 像素，跟随血条 alpha，关闭后不新增任何绘制。
- **全部 8 个新键都支持热重载**：样式/音量/节流/静音开关/棱彩阈值/档位开关/描边开关均实时读取，保存 `.cfg` 后立即生效；累计伤害属于关卡状态，不因改配置而清零。
- **最小击飞距离提升到 12 m**：`Corpse/MinLaunchDistance` 默认 `12`（钳制 12–20），`Corpse/MaxLaunchDistance` 默认 `18`（有效区间 12–18 m）；体型与 `TravelScale` 缩放后仍强制 **12 m** 下限，绝对上限 40 m 不变。
- **修复（加载失败）**：1.2.12 的首个本地构建里，`CellSound.Post` 静音补丁的第二个参数被命名为 `position`，而游戏里该参数叫 `pos`。Harmony 按**参数名**绑定，名字对不上时 `PatchAll` 直接抛异常、**整个插件加载失败**——表现就是击杀卡片、伤害数字与尸体反馈同时消失（日志：`Parameter "position" not found in method static ulong CellSound::Post(...)`）。已改为 `pos`；并在指令级门禁里新增「补丁参数名/类型必须与游戏真实方法一致」的检查，该检查对旧构建会直接报错，今后不会再静默出货。
- 本版程序集版本为 `1.2.13.0`；倒地标记四选的实机截图见详情页，其余验证边界见 README。

## 1.2.11
- **新增 `DamageNumbers/Opacity`（默认 `1.0`，范围 0–1）**：伤害数字的整体不透明度。该乘数与原有的生命周期淡出相乘**恰好一次**，并**同时**作用于正文与全部 8 道描边；`Opacity = 0.5` 为半透明且保留原淡出曲线，`Opacity = 0` 完全不绘制（不提交任何文字或描边），`1.0` 与 1.2.10 的画面完全一致。示例：`Opacity = 0.6` 可让伤害数字更不挡视线。
- **弱点颜色的自身 alpha 仍然生效**：`DamageNumbers/WeakspotColor` 写成 `#RRGGBBAA` 时，该 alpha 依旧只参与正文一次，描边继续不跟随弱点颜色 alpha（历史行为未改动）。普通/护甲/弱点判定、伤害文本、寿命、字号、位置、弹丸合并与队列逻辑均未改动，击杀卡片、焦点血条、倒地队友标记与尸体特效不受影响。
- **边界**：越界值夹取到 0–1；`NaN`/`Infinity` 回落到安全默认值 `1.0`（保持原观感，不抛异常也不会让数字消失）。
- **热重载**：保存 `.cfg` 后经现有主线程热重载生效，**已经在飞的数字**下一帧即按新透明度绘制，无需重启。插件在热重载前后不会主动回写文件，**已有配置键的值不会被覆盖**；`BepInEx.ConfigFile` 保存时会保留文件里已有的键值，若把本版新增的 `Opacity` 键绑定进旧 `.cfg`，该键可能被追加写入（值仍是 `1.0` 或你手动写的数字）。旧配置没有该键时按默认 `1.0` 生效。
- 本版程序集版本为 `1.2.11.0`。游戏内画面与游戏内热重载**尚未实测**；验证边界见 README。

## 1.2.10
- **新增前置依赖 `randomuserhi-DamageSync-0.0.7`**（`manifest.json` 中的第二项依赖，第一项仍是 `BepInEx-BepInExPack_GTFO-3.2.3`）：这是修复「客机敌人血条恒满」的关键。客机的敌人血量由主机权威，原生链路不会把主机的扣血同步到客机的 `Damage.Health`；装上前置后，主机的伤害结算会让 DamageSync 从主机侧发出原生的 SetHealth 同步包，客机原生接收后就地写入 `Health`，因此本插件的血条**原读取路径无需任何改动**即显示真实剩余血量。
- **主机与客机都安装 DamageSync 才精确**：只有客机装、主机没装时，客机收不到任何同步，血条仍然显示游戏原版的恒满值——这是游戏原生的限制，不是本插件的缺陷。主机未安装时请勿期待该修复生效。
- **主机权威切换（房主迁移）后需要等下一次受击**：新主机只有在它自己完成一次伤害结算后才会再次发出同步，因此在切换瞬间到下一次受击之间，客机血条可能停留在旧值。
- **移除 1.2.9 本地候选里的自建网络通道**：该通道及配套的 `Network/ShareEnemyHealth` 配置项**从未随任何发布版本提供过**（只存在于未发布的本地候选），用户无需做任何操作。本插件现在不发送、不接收任何自定义网络包，也不 patch `SNet_Replication.RecieveBytes`。
- **尸体消融失败原因拆分**：1.2.9 把「借不到实例」「`Initialize` 失败」「`Play` 被第三方吞掉」「探针非 Idle」全部合并成一条 `reason=borrow-or-start`，真实主机日志里一局 4 次消融全部跳过却无法定位。现在拆成 `fade=skipped;reason=disabled|corpse-effects-off|off-level|paused|patched-by-third-party|prefab-unsafe|pool-missing|no-free-instance|no-renderer|off-scene|concurrency-cap` 与 `fade=failed;reason=pool-missing|no-free-instance|borrow-error|instance-busy|instance-tracked|instance-gone|probe-unknown|initialize-failed|initialize-threw|play-not-observed|play-threw|unexpected`，并附带 `scheduleId`、`polls`、`failures`、`manualReleases`、`active`；异常另外补一条 `fade=error;reason=…;type=…;message=…`（消息截断 140 字符）。含义见 README 的 reason 表。
- **池状态与借池结果分型**：`Decay.pool` 的观测由布尔改为 `Missing/Exhausted/Ready` 三态，借池结果改为 `Ok/PoolMissing/NoFreeInstance/Error`，因此「池根本没建」与「池被占满」不再共用同一个 null 句柄。
- **借池有界重试（最多 3 次）**：池返回「无空闲实例」时，在同一具尸体上最多轮询 `CorpseFadePolicy.MaxBorrowPolls = 3` 次（**只读池、不占额、不克隆**）；`Initialize`/`Play` 失败一律不重试，避免多烧一个模型克隆。上限是编译期常量，源码与 IL 两层断言都会校验它不能被改成无界循环。
- **Initialize 后回读校验**：原生 `Initialize` 在 renderer 为空/已销毁时只 `LogError` 不抛，随后 `Play` 静默无效。现在调用后回读 `Decay.m_targetRenderer`，未绑定即判 `initialize-failed`，`Cleanup()` + `pool.Return()` 恰好一次。
- **诊断预算每关重置**：每个 reason 每关最多 3 条、整关最多 24 条信息 + 4 条警告，`CorpseFade.Reset()`（离关/重进）同时清空预算与 `CorpseFadeCoordinator` 计数，长会话不会因为预算耗尽而彻底沉默；用户主动关闭、无 renderer、已移出场景等正常情况不写日志。
- **保持的守恒**：仍然只借用不建池、绝不 `Object.Destroy`、不 patch `Decay`/`ES_Dead`、不写 `MaterialPropertyBlock`、不改 `timeScale`；检测到第三方 patch 仍整体回退；并发占额仍为 2 且 `playing`/`unknown` 继续保留名额；任何异常都不影响 `Hide()`、刚体回收与队列清理。
- 配置项：本版**没有**新增或修改任何配置键（`Corpse/FadeEffect` 与 10 个 `TeammateMark/*` 键属于已发布的 1.2.9，见下一条目）；本版只是移除了从未发布过的 `Network/ShareEnemyHealth`，任何用户 `.cfg` 里都不存在该键，无需处理。
- 本版程序集版本为 `1.2.10.0`。本版**未安装、未发布、未实机联机验证**；`Smoke` 断言 529 条（回落至 1.2.9 基线，自建通道的 55 条用例已随代码移除）、`CONFIG_SMOKE=25`、`GLUE_WIELD_WIRING`、`CORPSE_FADE_IL`、`PACKAGE_VERIFY` 均 PASS。

## 1.2.9
- **升级须知（默认行为变更）**：1.2.8 及更早恒定播放尸体爆裂特效与爆炸音效；1.2.9 新增 `Corpse/ExplosionFx` 且默认 **false**，想要保留旧观感请手动设为 `true`。该键只开关尸体爆裂的原生视觉特效与爆炸音效；击飞、断肢、碎块、爆裂后隐藏、租约回收、死亡统计与 InLevel/去重队列语义不变，热关闭时同时清空待播的合并音效。旧 `.cfg` 没有该键时按默认 false 生效。
- 新增原生快速消融 `Corpse/FadeEffect`（默认 **true**）：爆裂后 0.75 秒物理窗口结束的同一 tick、在 `Hide()` 与刚体回收**之前**，从游戏**自己的** `Decay.pool` 借一具实例对一具可见主 `Renderer` 执行 `Initialize(renderer, null)` + `Play(0.5f)`，由原生 Decay 播放约 0.5 秒分解消融；隐藏/恢复、击飞、断肢、碎块与去重清理的顺序和时机不变。守恒条件：每具尸体最多 1 个实例、全局并发上限 2、池必须已存在且有空闲实例（从不 `Resources.Load`/`InitializePool`）、prefab 原型必须 `stopAction=Deactivate`+`autoCleanup=true`、`Harmony.GetPatchInfo` 在 `Decay.Play` 上发现任何 patch（DBO 1.5.0 属于这种情况）即跳过、暂停/无 Renderer/已移出场景均跳过——任一不满足就静默回落为原来的瞬时隐藏。归还只按 `m_playing` 判所有权：`true` 时由游戏 `Update` 归还、本插件绝不 `Return`；`false` 时 `Cleanup()` 后 `pool.Return()` 恰好一次。不建池、不销毁任何 Decay/GameObject、不 detour `ES_Dead`、不写 `MaterialPropertyBlock`、不新增 Harmony 补丁。
- **消融会播放原生 `MEDIUM_CORPSE_DECOMP` 分解音**：该音由原生 decay 自己发出，`Corpse/ExplosionFx` 管不到它，被关掉的 `ENEMYEXPLODE` 爆裂音也不会由此触发；要完全安静请把 `Corpse/FadeEffect` 也设为 false。
- 弱点伤害数字默认改为克制的深红 `#E5484D`（`DamageNumbers/WeakspotColor`，接受 `#RRGGBB`/`#RRGGBBAA`，非法值回落琥珀）；普通数字、护甲数字、击杀卡片琥珀色与弱点血条颜色都不受影响。
- 倒地队友头顶新增「菜」/「鶸」标记：`TeammateMark/Icon` 默认 `Cai`（菜），可切 `Ruo`（鶸），也接受「菜/鶸」与 `cai/ruo`；按本机到该队友的真实距离缩放（1080p 下 8 m = 30 px，下限 26 px）；`TeammateMark/CustomIconPath` 可填**本机绝对路径**的透明 PNG 并热重载，坏路径/锁定/半写/坏 PNG 时保留上一张有效贴图并回退内置字形；复活、离队、槽位换人、未知状态与离关重进都会收敛或清空缓存；共用已有 F7 显隐，零新增 Harmony、零发包、不写原生 UI 或玩家状态。
- 新增 `TeammateMark/HideDuringEMP`（默认 **false** = EMP 期间仍显示）：只有设为 true 时才反射只读 `Inas07.EOSExt.EMP` 的 `EMPPlayerHudHandler.Instance.IsEMPed()`；缺程序集、类型改名、实例为空或调用抛异常一律按「无 EMP」降级，不 patch 第三方。
- 配置支持热重载：所有 `.cfg`（含本轮新增键）在文件写入稳定后于主线程重载；持久显示项（颜色、尺寸、位置、可见性、图标）下一帧生效，瞬时事件（尸体爆裂排程、正在飞的伤害数字）在**下一个事件**才生效。`Corpse/FadeEffect` 关掉不会打断已借出的原生消融实例（那是一具游戏刚借出的池对象），从下一具尸体起生效。
- 本轮新增配置键与默认值：`Corpse/ExplosionFx=false`、`Corpse/FadeEffect=true`、`DamageNumbers/WeakspotColor=#E5484D`、`TeammateMark/Enabled=true`、`TeammateMark/Icon=Cai`、`TeammateMark/CustomIconPath=""`、`TeammateMark/RefDistanceMetres=8`、`TeammateMark/MinPixels=26`、`TeammateMark/MaxPixels=42`、`TeammateMark/MaxDistanceMetres=60`、`TeammateMark/HeadClearanceMetres=0.05`、`TeammateMark/HideDuringEMP=false`、`TeammateMark/LogMarks=false`。旧 `.cfg` 没有这些键时按代码默认生效。
- 两张倒地标记 PNG（`teammate-downed-cai.png` / `teammate-downed-ruo.png`，Yuji Syuku Regular，SIL OFL 1.1）作为**已验收源资产**随包：构建只校验尺寸与哈希、绝不重绘，包内字节与源文件逐字节一致，许可全文 `Assets/icons/OFL-YujiSyuku.txt` 随包；既有击杀图标字节未变。
- 本条目相对作者已在 Survival profile 实机确认的候选只改了版本号与文档，插件逻辑未改；程序集版本为 `1.2.9.0`。仍未实机验证的项见 README「测试与验证边界」。

## 1.2.8
- 修复手持 C-Foam 胶枪（`GlueGun`）时的武器识别：`FirstPersonItemHolder.WieldedItem` 的 getter 每次都会新建托管包装，托管侧 `is GlueGun` 判断永远为假，导致手持胶枪瞄准敌人时仍显示 `[头5×]` 这类弱点标签；改用原生侧 `TryCast<GlueGun>()` 判断，读取或转换失败时按「未知武器」处理。
- 结沫团数标签改用固定暖琥珀橙 `#F2A65A`（242/166/90），与默认红色的弱点标签明确区分；该颜色不受 `NameColor`/`WeakspotColor` 覆盖，放下胶枪后的弱点标签与 `ShowWeakspotPart` 行为不变。
- 焦点日志与异常告警的预算彼此独立：原先 focus 状态翻转与 `[BAR]` 异常告警共用一个计数器，累计 5 次翻转后异常告警会永久静默；现拆分为 focus 切换 16 条、focus 获取 12 条、异常告警 5 条三个独立预算，日志文案与上限数值不变。
- 更新 Thunderstore 详情页：加入两张实机截图（胶枪下的橙色 `[结沫*2]`、普通武器下的红色 `[头3×]`），并把详情与更新日志改为简体中文。

## 1.2.7
- 手持 C-Foam 胶枪（`GlueGun`）时，把弱点部位与倍率标签替换为聚焦敌人 `EnemyBalancingDataBlock.GlueTolerance` 对应的完整结沫团数；每发弹丸按 3 个 C-Foam 单位向上取整；识别 1,000,000 的免疫哨兵值，读不出的数值留空而不编造团数。
- 放下胶枪后恢复原有的弱点标签与 `ShowWeakspotPart` 行为；不新增设置项，也不改变玩法交互。

## 1.2.6
- 为每个已配置弱点及其当前肢体倍率在血条上方加入紧凑标签（例如 `[孢子4×][头2×]`）；复用 `ShowWeakspotPart` 与既有红色弱点配色。
- 从当前运行时 limb 读取 `m_weakspotDamageMulti`，每 0.35 秒刷新聚焦标签，对相同标签去重，最多显示三个标签并附加 `+N` 溢出标记。
- 用提供的方形 DORO 游戏图替换 Thunderstore 包图标；保留其源资产与旧图标，便于复现构建与回滚。

## 1.2.5
- 通过伤害回调中携带的持久 `gearCategoryId` 判定霰弹枪伤害，使可见性开关对延迟的客户端回调依然生效。
- 仅在帧与装备类别都匹配当前开火作用域时合并弹丸；跨帧延迟命中保持逐弹显示，避免并入后续射击。
- 把开火/目标作用域栈抽成纯代码，并为嵌套/乱序 finalizer、目标恢复、抑制与预览处理补充回归覆盖。

## 1.2.4
- 新增独立的霰弹枪伤害数字可见性开关，以及按每次射击、每个敌人合并弹丸（默认开启）；关闭合并则恢复逐弹数字。
- 把新安装的尸爆延迟默认值下调到 0.1 秒并钳制在 0.1–1.5 秒，使其时间点与 DBO 的消散启动窗口对齐；升级时保留已保存的 `ExplosionDelay` 值。
- 保持现有的致死伤害排程与 DBO 离场清理处理；不新增主机权威伤害或网络状态改动。

## 1.2.3
- 与离场清理模组协同：当敌人根节点或布娃娃已移出场景时，使用缓存的死亡位置播放延迟爆裂 FX。
- 检测到离场清理时，立即释放 KillFeed 的碎块并回收租用的尸体物理；跳过后续击飞、断肢与新碎片，长时间帧之后同样如此。
- 通过现有的 Setup、OnDeSpawn 与关卡重置生命周期保留池对象恢复；不新增清理用 Harmony 钩子，也不新增网络伤害。
- 通过 typed 的 `EnemyAgent.OnDead` 与 `Dam_EnemyDamageBase.InstantDead` 钩子并按池化身份去重来调度尸体反馈；仅在 `InLevel` 接受死亡，退出时清空排队工作。在不改变仅本地击杀卡片的前提下覆盖炮台、队友等非本地死亡；在采样爆裂强度前记录本地致死伤害。
- 把默认击飞距离提升为最小 6 m / 最大 12 m，并在体型与位移缩放后仍强制 6 m 有效下限。
- 爆裂与 0.75 秒物理窗口结束后，冻结尸体刚体、禁用碰撞体并隐藏渲染器；在池复用、禁用或关卡清理时恢复捕获的状态。

## 1.2.2
- 修复非有限红阈值的回退逻辑，使其仍受自定义黄阈值约束；默认 66%/33% 分档不变。

## 1.2.1
- 在敌人名称后追加其已配置的弱点部位（例如 `触手[头]`）；该标签基于敌人实际的 `eLimbDamageType.Weakspot` 肢体元数据，而不是准星当前的命中点。
- 新增 `ShowWeakspotPart` 开关，以及可编辑的名称、弱点、血量文字、基础/黄/红填充、残影、轨道与边框颜色（`#RRGGBB` 或 `#RRGGBBAA`）；弱点文字默认红色。血量高于 66% 保持基础色，66% 及以下转黄，33% 及以下转红；阈值与颜色均热重载。
- 把标题行重组为左对齐的名称/弱点标签与右对齐的血量读数，细条紧贴其下；按文字测量宽度、限制边距并对名称省略，保证留在屏幕内。
- 文件稳定保存后从磁盘重载所有插件 `.cfg` 设置，无需重启；自定义敌人名称 JSON 仍热重载并刷新当前血条标签。

## 1.2.0
- 新增：准星所指敌人上方的一条细而半透明的血条，显示名称与剩余/最大生命值。全部位于 `EnemyBar` 配置节，并与击杀卡片共用 F7 显隐开关。
- 焦点来自对游戏自身相机/子弹层的真实准星射线，因此你与敌人之间有墙时会隐藏血条；死亡敌人与队友永不显示，敌人层回退仍受最近遮挡物距离限制。
- 头部锚点取自敌人的头部肢体，且只在头部附近的小窗口内信任渲染器边界，因为 GTFO 的服装与特效渲染器可能报告高出数米的边界。结果被钳制在 10%–46% 的屏幕高度带内，永不遮挡准星或视野中心。
- 血条 0.10 秒淡入，保持 0.15 秒后 0.20 秒淡出。关闭开关、禁用或发生内部错误时立即结束读数并丢弃敌人引用，因此陈旧或已销毁的敌人不会被再次解引用。
- 名称来自与击杀卡片相同的表，卡片与血条始终一致；仅在显示值变化时重建标签字符串，且即使敌人没有可见渲染器也缓存头部锚点，因此聚焦敌人不会产生逐帧分配。
- 只读：无 Harmony 补丁、无网络包、无伤害调用、不写入血量。`Verify.ps1` 现已在血条源码上断言这些不变量。

## 1.1.4
- 新增一段原创嵌入式尸爆 WAV（单声道、48 kHz、16 位、420 ms）；自定义音频加载/播放失败时回退到游戏本地的 ENEMYEXPLODE 事件。
- 修复冷却边界处的尸爆音效合并：待播提示会被保留，并在主线程 tick 时冲刷，而不是被边界请求丢弃。
- 2026-10-18 的实机测试确认自定义音频路径完成九次爆裂，以及一次合并爆裂及其待播提示。

## 1.1.3
- 碎块激活现在会扫描共享同一 destruction ID 的每个允许 setup，遇到空 GibData 数组时跳过而不是在首个匹配处停止，并在每具尸体 24 次调用上限内汇总条目；同时记录匹配的 setup 数量。
- 每个被破坏的身体部位现在还会在本机调用游戏自身的 `CD_CharacterDestruction.ApplyDestruction` 视觉效果，因此尸体伤害不再依赖主机；每个部位记录 `destruction=local` 及肢体索引、destruction ID、网格结果与允许的 setup 数量。
- 击杀卡片现在按每个可见文字组测量，并以其实际卡片宽度居中，而不是把一个不可见的固定宽度区域居中。
- 布娃娃查找现在会重试到配置的尸爆截止时间；不再在 0.35 秒后放弃迟到/部分初始化的布娃娃。
- 布娃娃刚体获得向外的径向击飞向量；捕获的碎块沿向外方向以 5.5–12 m/s 发射。
- 每具尸体尝试 3–7 个分离的原生爆裂 FX，并播放本地的 `ENEMYEXPLODE` 音效；碎片尝试为 2–6 个可破坏部位。
- 新增逐敌的排程/跳过原因、体型、布娃娃刚体数、肢体/碎块数、实际 FX 数与音效结果的日志。
- 未添加敌人 ID 允许列表；只有本地记入的击杀符合条件，并有明确的活跃/隐藏队列上限。
- 候选的游戏内视觉效果等待作者确认；现有商店页截图代表已发布的 1.1.2 基线。

## 1.1.2
- 击飞距离现在由两个可配置的米制数值驱动：`Corpse/MinLaunchDistance`（默认 **3.8 m**）与 `Corpse/MaxLaunchDistance`（默认 **8.5 m**），按 0.65 秒爆裂伤害插值，再按体型与 `Corpse/TravelScale` 缩放 0.925–1.35 倍。
- 最近击飞提升到约 **3.5 m**（原约 2.0 m）；最远在大体型、满伤害时提升到约 **11.5 m**（原约 5.4 m）。
- 击飞速度从 3.4–7.2 m/s 提升到 **4.2–8.6 m/s**，使尸体真正达到配置预算而不是明显偏短。
- 旧的每尸体上限已移除：它不再覆盖你配置的距离。仅保留 40 m 绝对安全边界。
- 配置值被钳制在合理范围（最小值 0.5–20 m；最大值不小于最小值，至多 30 m）。
- 商店页现在展示作者会话中的真实游戏内截图（弱点击杀卡片、弱点伤害数字、尸体血肉）。

## 1.1.1
- 击杀卡片默认完全透明：`Layout/BackgroundOpacity` 默认 0（仅图标与文字，带深色阴影保证对比）。可提升到 0.9 获得带色面板。
- 按原字节恢复初版（1.0.0）的普通与弱点骷髅美术，取代重新推导的那一对。
- 卡片更小：图标默认 58 -> 46 px（钳制 26–76），卡片高宽减小，标题 15 -> 13 px、明细 13 -> 12 px，卡片间距更紧。
- 卡片位置更低：默认垂直锚点从屏幕高度的 0.69 移到 0.78，使其避开视野中心。
- 尸体反馈、伤害数字与敌人名称与 1.1.0 相同。

## 1.1.0
- 紧凑的两行击杀卡片：敌人名称取代血量等级；不再有额外的名称行。
- 半透明击杀卡片面板保留画面可见性：`Layout/BackgroundOpacity` 默认 0.42（0–0.9），强调线变暗，两行文字都带 1 px 阴影以保持对比。
- 普通/弱点图标由单一母版剪影推导并相互匹配。
- 护甲、普通与弱点浮动伤害数字，保留小数与短促的描边弹出/弧线/淡出动画。
- 有界短窗口内按伤害缩放的尸体击飞，并刻意提高最小距离（体型缩放前 2.2–4.0 m），配合原生断肢、血液喷溅与清理。
- 尸体击飞距离、碎块缩放、碎片散布与原生爆裂数量都按敌人实测体型（0.7–2.4 倍）等比缩放；每尸体击飞上限也随之缩放。
- 包含 Hikaria DamageAnalyzer 的名称默认值，以及为 MOD 图热重载的自定义名称覆盖。
- 保持特效本地化、队列有界、池化渲染器/刚体可恢复。
- 保持插件加载日志无警告；HUD 错误辅助不再暴露 Il2CppInterop 不支持的签名。
- 玩法仍未验证；此候选只做了隔离的无头加载检查，没有实机会话。

## 1.0.0
- 初版 GTFO 击杀卡片插件。
- 依据游戏肢体类型区分普通与弱点击杀图标。
- 新增敌人名称、距离、最大生命值、血量等级、可配置布局与预览键。
- 怪物名称显示 GTFO `EnemyDataBlock.name` 原文；不做别名或自定义翻译。
- 仅本地玩家；无音频。
