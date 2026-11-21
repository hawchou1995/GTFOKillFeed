# KillFeed 用户可见功能与开关清单

基线：Thunderstore `HawChow/KillFeed` 1.2.16。本清单列出用户能看到/听到的功能、控制它的配置键、默认值、父功能关系，以及关闭某一项后**明确不受影响**的东西。

约定：
- 「父功能」是该开关生效的前提。父功能关闭时子开关写什么都不生效（代码里是逻辑与，不会互相改写）。
- 「1.2.16 行为」指：只要父功能开着，该效果就开着。本版新增的开关默认值一律取这个行为，因此老用户升级后画面不变。
- 音效是唯一例外：验收要求新安装默认静音，因此它的默认值按「新安装 / 旧配置」分别决定（见 §2）。
- 头顶图标、击杀卡片、碰撞盒避让的键在本文件 §8 统一登记；视觉模块和音效模块各自在首个 Bind 前扫描原始 cfg，迁移互不覆盖。

## 1. 总开关与输入

| 功能 | 键 | 默认 | 父功能 | 关闭后 |
|---|---|---|---|---|
| 本地击杀反馈总开关 | `General/Enabled` | true | — | 不画卡片/头顶图标、不播击杀音效；血条与队友标记仍按 F7 |
| F7 显隐 | `Input/ToggleKey` | F7 | — | 语义不变：切换卡片/头顶图标、焦点血条、倒地队友标记 |
| F9 预览 | `Input/PreviewKey` | F9 | — | 语义不变 |
| 语言 | `General/Language` | zh | — | 只影响卡片文案 |

## 2. 命中 / 击杀音效（本版重点）

三层互相独立，任意一层都不会改写另外两层：
1. 父开关 `HitSound/Enabled`（1.2.16 旧键，默认 true）——原值保留并继续生效。
2. 命中通道 `HitSound/HitEnabled` + 音量 `HitSound/HitVolume`；击杀通道 `HitSound/KillEnabled` + 音量 `HitSound/KillVolume`。
3. 原版提示音静音 `HitSound/MuteVanillaIndicator`（默认 false，与上面全部互不联动）。

**音量合成规则**（`SoundSettings`；播放路径实际传给 `CueWavePlayer.Play` 的就是这个值，不是绑定原值）：

```
实际音量 = clamp01( 通道音量 × 主音量 ÷ 0.8 )
```

- 主音量就是 1.2.16 的旧单键 `HitSound/Volume`，本版**每次播放都读取**：热改它，两路一起变。0.8 是它的 1.2.16 默认值，对应 100%。
- 通道音量是 0–1 的增益：主音量保持默认 0.8 时，通道音量就是实际音量。
- 越界夹取 0–1；主音量 NaN/±Infinity 回落 0.8（=100%），通道音量 NaN/±Infinity 回落该配置的通道默认值；任一路音量 0 等于该路不播放，但不改变开关状态。

**默认与迁移**（`SoundConfigPlan`；`tests/KillFeed.Tests` 用真实 BepInEx `ConfigFile` 启动 + `Reload` 逐条执行）：

| 配置文件状态 | 通道开关默认 | 通道音量默认 | 实际输出 |
|---|---|---|---|
| 文件不存在（新安装） | false / false | 0.4 / 0.4 | 静音；开启某一路后实际 0.4（主音量仍为默认 0.8） |
| 有旧键、无通道键（1.2.16 升级） | true / true（跟随旧 `Enabled`） | 0.8 / 0.8 | 实际音量 = 旧 `Volume`，升级前后逐值相同 |
| 已包含通道键 | 文件里的值 | 文件里的值 | 缺哪个键就按下面两行回落 |
| 旧键 + 只补了部分通道键 | 缺的开关按旧配置语义（开） | 缺的音量 = 0.8 | 例如只写 `KillVolume=0.2`：击杀 0.2、命中 0.8 |
| 配置文件存在但读不出 | true / true | 0.8 | 保持原行为，不静默变哑 |

- **不覆盖旧值**：`HitSound/Enabled`、`HitSound/Volume` 的值从不改写、不删除。
- **旧总开关继续工作**：旧 `Enabled=false` 升级后仍静音；热改回 true 后两路立刻按旧音量恢复（通道开关默认是「开」，不是照抄旧值）。
- **旧音量继续工作**：旧 `Volume` 改 0.3→0.6 并热重载后，两路实际音量都变 0.6；单独改 `HitVolume` 只影响命中，单独改 `KillVolume` 只影响击杀。

## 3. 命中 / 击杀标记

| 功能 | 键 | 默认 | 父功能 | 关闭后 |
|---|---|---|---|---|
| 标记总开关 | `HitMarker/Enabled` | true | — | 完全恢复原版标记 |
| 命中标记（普通/弱点/护甲） | `HitMarker/HitEnabled` | true | `HitMarker/Enabled` | 只停命中标记，击杀标记照旧 |
| 击杀标记 | `HitMarker/KillEnabled` | true | `HitMarker/Enabled` | 只停击杀标记，命中标记照旧 |

## 4. 伤害数字与可选效果

| 功能 | 键 | 默认 | 父功能 | 关闭后 |
|---|---|---|---|---|
| 伤害数字总开关 | `DamageNumbers/Enabled` | true | `General/Enabled` | 数字、棱彩、档位、轨迹全部停止 |
| 霰弹枪数字 | `DamageNumbers/ShowShotgunDamageNumbers` | true | `DamageNumbers/Enabled` | 只影响霰弹枪 |
| 弹丸合并 | `DamageNumbers/MergeShotgunPellets` | true | `DamageNumbers/ShowShotgunDamageNumbers` | 每颗弹丸单独显示 |
| 棱彩外观 | `DamageFx/PrismaticEnabled` | true | `DamageNumbers/Enabled` | 只停箔外观与棱彩轨迹 |
| 累计伤害档位 | `DamageFx/TiersEnabled` | true | `DamageNumbers/Enabled` | 只停白/银/金/钻上色，累计值照记 |
| 飞行轨迹 | `DamageNumbers/ApproachEffect` | false | `DamageNumbers/Enabled` | 回到原始上飘轨迹 |

硬直绿字没有单独的配置键。只对本机已确认触发非致死 Light/Heavy 硬直的正伤害正文换色；证据不足保留原色。合并霰弹内有部分弹丸触发时，同一数字容器分列普通与浅绿两段，二者之和仍是该枪伤害。关闭合并则按弹丸显示；关闭霰弹数字则两段都不画。硬直行开关不改变上述判定。

## 5. 敌人血条与焦点描边

| 功能 | 键 | 默认 | 父功能 | 关闭后 |
|---|---|---|---|---|
| 血条 | `EnemyBar/Enabled` | true | `General/Enabled` + F7 | 不画血条与标签 |
| 焦点描边 | `EnemyBar/FocusOutlineEnabled` | true | `EnemyBar/Enabled` | 只停描边，条体与文字照旧 |
| 名称 / 数值 / 弱点标签 | `EnemyBar/ShowName`、`EnemyBar/ShowNumbers`、`EnemyBar/ShowWeakspotPart` | true | `EnemyBar/Enabled` | 只停对应文字 |
| 硬直行 | `EnemyBar/ShowStaggerLine` | true | `EnemyBar/Enabled` | 只隐藏硬直行，不影响名称、弱点、结沫标签或硬直绿字 |

浅绿行按四肢、躯体分别显示可断部位的当前耐久与上限；普通硬直显示还差多少伤害才触发。无对应部位或数据不可读时显示 `—`，不填 0，也不把普通硬直值冒充部位耐久。隐藏名称时改为与血条左边对齐。

## 6. 倒地队友标记

| 功能 | 键 | 默认 | 父功能 | 关闭后 |
|---|---|---|---|---|
| 倒地标记 | `TeammateMark/Enabled` | true | `General/Enabled` + F7 | 不画标记；名单与图标缓存照旧清理 |
| EMP 期间隐藏 | `TeammateMark/HideDuringEMP` | false | `TeammateMark/Enabled` | EMP 期间照常显示 |

## 7. 尸体反馈（本版拆分为独立开关）

| 子功能 | 键 | 默认 | 父功能 | 关闭后明确不受影响 |
|---|---|---|---|---|
| 尸体反馈总开关 | `Corpse/Enabled` | true | `General/Enabled` | 关闭即整条队列按既有 `effects-disabled` 路径清账（`RemoveQueued` + `Release`），与 1.2.16 相同 |
| 击飞 | `Corpse/Launch` | true | `Corpse/Enabled` | 断肢、碎块、爆裂、隐藏与回收；只去掉初速与速度/距离上限 |
| 断肢 | `Corpse/Dismember` | true | `Corpse/Enabled` | 击飞、爆裂视觉/音效、隐藏与回收；不调用 `BreakLimbs`，因此也不触发原生 GibData |
| 碎块（原生 `CD_Gib` 捕获） | `Corpse/Chunks` | true | `Corpse/Enabled` | 断肢照旧（碎块仍由游戏原生生成/回收），击飞、爆裂、队列回收；本插件不登记、不加初速、不缩放、不限距 |
| 爆裂视觉 | `Corpse/BurstVisual` | true | `Corpse/Enabled` + `Corpse/ExplosionFx` | 爆裂音效、击飞、断肢、隐藏与回收 |
| 爆裂音效 | `Corpse/BurstSound` | true | `Corpse/Enabled` + `Corpse/ExplosionFx` | 爆裂视觉、击飞、断肢、隐藏与回收；关闭时把待播的合并音效清空 |
| 爆裂总开关（1.2.16 旧键） | `Corpse/ExplosionFx` | false | `Corpse/Enabled` | 关掉即视觉+音效一起停，与 1.2.16 相同 |
| 原生消融 | `Corpse/FadeEffect` | true | `Corpse/Enabled` | 只影响隐藏时的 Decay 溶解；不销毁任何对象 |
| 爆裂延迟 | `Corpse/ExplosionDelay` | 0.1 | `Corpse/Enabled` | 只改时机 |
| 击飞距离预算/倍数 | `Corpse/MinLaunchDistance`、`Corpse/MaxLaunchDistance`、`Corpse/TravelScale` | 12 / 18 / 1 | `Corpse/Launch` | 关闭击飞时这些值不生效 |

从 1.2.21 升级后，`Corpse/FadeEffect` 仍默认开启，但不保证每具尸体都播放淡出：筛选器拒绝阴影注册对象、已有淡出副本及无法判断的层级。使用尸体自带的 `Decay` 前还要检查其副本是否有效；没有安全路径就保留原清理行为，不改变全局渲染状态。

资源回收不变量（`tests/KillFeed.Tests` 的 `gate.corpse.*` 用例逐条检查）：
- 到 `age >= item.EndAt` 时仍释放已登记的碎块；仅在应隐藏尸体或已成功开始淡出时走视觉收尾。未解决的淡出经 `CorpseFadePrep.Attempt` 的异常保护尝试，不以淡出成功作为队列清理条件。
- `ReleaseGibs` 函数体内没有任何 `Chunks`/`switches` 判断；`Chunks` 只拦 `CaptureGib`（登记/初速/缩放）与 `LimitGibs`（限距）。
- 关闭 `Corpse/Enabled` 仍走原有 `effects-disabled` 队列清理路径，不会留下悬挂队列项。

## 8. 头顶图标、击杀卡片与碰撞盒避让

视觉和音效都从**同一个**原始 cfg 文本读取迁移输入：`Plugin.Load` 在第一个
`Config.Bind` 之前分别建立 `KillViewConfigFile` 与 `SoundConfigPlan`，然后在各自
Bind 之后应用。两条迁移只写自己的新键，不改写对方的旧键。

| 功能 | 键 | 默认 | 父功能 / 关系 | 关闭后 |
|---|---|---|---|---|
| 击杀卡片 | `Card/Enabled` | true | `General/Enabled` + F7 | 不画卡片；头顶图标不受影响 |
| 头顶击杀图标 | `KillIcon/HeadEnabled` | false | `General/Enabled` + F7 | 不画头顶图标；卡片不受影响 |
| 头顶图标/飞行徽章不透明度 | `KillIcon/Opacity` | 0.5 | `KillIcon/HeadEnabled` | 只影响头顶图标与飞行徽章，不影响卡片 |
| 旧呈现位置（兼容键） | `KillIcon/Display` | 空（旧值 Head/Card 原样保留） | 仅 `ViewMode=Legacy:*` 时 | Head/Card 按旧语义联动；显式使用新开关后忽略 |
| 迁移状态（内部） | `KillIcon/ViewMode` | 空 | — | `Legacy:Head`/`Legacy:Card` 表示仍跟随旧 Display；`Explicit` 表示两新键独立 |

- 新安装：卡片开、头顶图标关、透明度 0.5；两个呈现开关独立，可同时开。
- 旧版升级：只改旧 `KillIcon/Display` 时，在 `ConfigFile.Reload` 后按旧语义即时切换
  Head/Card；显式改任一新的呈现键后才进入 `Explicit`，两个新键从此独立并跨重启保持。
- 旧 `Display` 的既有值不被改写；`ViewMode` 只为迁移记录，不是用户功能开关。
- 碰撞盒避让只影响头顶图标：候选包括所有已注册的活体敌人（不是按冻结 anchor 的
  固定半径球查询），只对与图标+飞行徽章当前投影重叠的 hitbox 生效；候选集不完整、
  缓冲/注册表饱和或投影无法证明时隐藏头顶图标，绝不把它当成无遮挡。卡片是否回退
  只看 `Card/Enabled`，不会强行开启已关闭的卡片。

## 8b. 血条下击杀计数（1.2.21 新增）

| 功能 | 键 | 默认 | 父功能 / 关系 | 关闭后 |
|---|---|---|---|---|
| 三档击杀图标 + 本次远征击杀数 | `KillCounter/Enabled` | true | `General/Enabled` + F7 | 不画计数；卡片、头顶图标、音效、血条本身都不受影响 |

- 计数只来自既有 `KillTracker` 的击杀账本：每个敌人死亡记一次，按呈现档位（普通/精英/重型）分列，
  累加到 `int.MaxValue` 饱和；F9 预览不计数，关卡重置时与总数一起清零。
- 位置是本机血条的真实屏幕坐标（`KillCounterPolicy.TryLayout` + `KillCounterAnchorPolicy`）：右边缘对齐
  血条右边缘、贴着血条下沿，像素不够时整组等比缩小。读不到可验证的 CellUI 合成器（缺 sprite、血条缩放
  为 0、cullingMask / 视口 / 画布尺寸与目标分辨率不符）就拒画并打一行 `held;` 原因——不做场景扫描、
  不猜主相机、不移动也不缩放原生血条。
- 已知边界：验证是源码与进程内策略证据（`tests/GTFOKillFeed.Smoke` 的 `KillCounterTests`、
  `tests/GTFOKillFeed.HudReplay` 的生产 Tick/Draw 重放），游戏内画面未验证。

## 9. 完整键表（由 `tools/feature_switch_inventory.py` 从源码生成）

<!-- BEGIN:generated-keys -->
| 键（`Section/Key`） | 默认值 / 迁移规则 | 注册位置 |
|---|---|---|
| `Card/Enabled` | true | `Plugin.cs` |
| `Card/ShowDistance` | true | `Plugin.cs` |
| `Card/ShowEnemyMaxHealth` | true | `Plugin.cs` |
| `Corpse/BurstSound` | true | `Plugin.cs` |
| `Corpse/BurstVisual` | true | `Plugin.cs` |
| `Corpse/Chunks` | true | `Plugin.cs` |
| `Corpse/Dismember` | true | `Plugin.cs` |
| `Corpse/Enabled` | true | `Plugin.cs` |
| `Corpse/ExplosionDelay` | FeedbackPolicy.CorpseBurstDelay | `Plugin.cs` |
| `Corpse/ExplosionFx` | false | `Plugin.cs` |
| `Corpse/FadeEffect` | true | `Plugin.cs` |
| `Corpse/Launch` | true | `Plugin.cs` |
| `Corpse/MaxLaunchDistance` | FeedbackPolicy.DefaultMaxDistance | `Plugin.cs` |
| `Corpse/MinLaunchDistance` | FeedbackPolicy.DefaultMinDistance | `Plugin.cs` |
| `Corpse/TravelScale` | 1f | `Plugin.cs` |
| `DamageFx/DiamondThreshold` | DamageFxPolicy.DiamondThreshold | `Plugin.cs` |
| `DamageFx/GoldThreshold` | DamageFxPolicy.GoldThreshold | `Plugin.cs` |
| `DamageFx/PrismaticEnabled` | DamageFxPolicy.DefaultPrismaticEnabled | `Plugin.cs` |
| `DamageFx/PrismaticThreshold` | DamageFxPolicy.DefaultPrismaticThreshold | `Plugin.cs` |
| `DamageFx/SilverThreshold` | DamageFxPolicy.SilverThreshold | `Plugin.cs` |
| `DamageFx/TiersEnabled` | DamageFxPolicy.DefaultTiersEnabled | `Plugin.cs` |
| `DamageFx/WhiteThreshold` | DamageFxPolicy.WhiteThreshold | `Plugin.cs` |
| `DamageNumbers/ApproachEffect` | FeedbackPolicy.DefaultApproachEffect | `Plugin.cs` |
| `DamageNumbers/Enabled` | true | `Plugin.cs` |
| `DamageNumbers/FontSize` | 27f | `Plugin.cs` |
| `DamageNumbers/FontWeight` | FeedbackPolicy.DefaultNumberWeight | `Plugin.cs` |
| `DamageNumbers/Lifetime` | FeedbackPolicy.NumberLifetime | `Plugin.cs` |
| `DamageNumbers/MergeShotgunPellets` | true | `Plugin.cs` |
| `DamageNumbers/Opacity` | FeedbackPolicy.DefaultNumberOpacity | `Plugin.cs` |
| `DamageNumbers/ShowShotgunDamageNumbers` | true | `Plugin.cs` |
| `DamageNumbers/WeakspotColor` | "#E5484D" | `Plugin.cs` |
| `EnemyBar/Anchor` | "Head" | `Plugin.cs` |
| `EnemyBar/BarHeight` | BarPolicy.DefaultHeight | `Plugin.cs` |
| `EnemyBar/BarWidth` | BarPolicy.DefaultWidth | `Plugin.cs` |
| `EnemyBar/BorderColor` | "#0B0E10" | `Plugin.cs` |
| `EnemyBar/Enabled` | true | `Plugin.cs` |
| `EnemyBar/FillColor` | "#E8ECEB" | `Plugin.cs` |
| `EnemyBar/FillRedColor` | "#EF4C4C" | `Plugin.cs` |
| `EnemyBar/FillYellowColor` | "#E6B84C" | `Plugin.cs` |
| `EnemyBar/FocusOutlineEnabled` | FocusOutlinePolicy.DefaultEnabled | `Plugin.cs` |
| `EnemyBar/GhostColor` | "#A8B0B2" | `Plugin.cs` |
| `EnemyBar/HeadOffsetMetres` | BarPolicy.DefaultHeadOffsetMetres | `Plugin.cs` |
| `EnemyBar/HealthTextColor` | "#E8ECEB" | `Plugin.cs` |
| `EnemyBar/LogFocus` | false | `Plugin.cs` |
| `EnemyBar/MaxDistance` | BarPolicy.DefaultMaxDistance | `Plugin.cs` |
| `EnemyBar/MaxScreenFraction` | BarPolicy.DefaultMaxScreenFraction | `Plugin.cs` |
| `EnemyBar/MinScreenFraction` | BarPolicy.DefaultMinScreenFraction | `Plugin.cs` |
| `EnemyBar/NameColor` | "#E8ECEB" | `Plugin.cs` |
| `EnemyBar/NumberSize` | BarPolicy.DefaultNumberSize | `Plugin.cs` |
| `EnemyBar/Opacity` | BarPolicy.DefaultOpacity | `Plugin.cs` |
| `EnemyBar/RedThresholdPercent` | BarPolicy.DefaultRedThresholdPercent | `Plugin.cs` |
| `EnemyBar/ScreenAnchorFraction` | BarPolicy.DefaultScreenAnchorFraction | `Plugin.cs` |
| `EnemyBar/ShowName` | true | `Plugin.cs` |
| `EnemyBar/ShowNumbers` | true | `Plugin.cs` |
| `EnemyBar/ShowStaggerLine` | true | `Plugin.cs` |
| `EnemyBar/ShowWeakspotPart` | true | `Plugin.cs` |
| `EnemyBar/TrackColor` | "#161A1C" | `Plugin.cs` |
| `EnemyBar/VerticalOffset` | BarPolicy.DefaultVerticalOffset | `Plugin.cs` |
| `EnemyBar/WeakspotColor` | "#EF4C4C" | `Plugin.cs` |
| `EnemyBar/YellowThresholdPercent` | BarPolicy.DefaultYellowThresholdPercent | `Plugin.cs` |
| `General/Enabled` | KillDefaults.Enabled | `Plugin.cs` |
| `General/Language` | "zh" | `Plugin.cs` |
| `HitMarker/ColorArmor` | HitMarkerPolicy.DefaultArmorColor | `Plugin.cs` |
| `HitMarker/ColorKill` | HitMarkerPolicy.DefaultKillColor | `Plugin.cs` |
| `HitMarker/ColorNormal` | HitMarkerPolicy.DefaultNormalColor | `Plugin.cs` |
| `HitMarker/ColorWeakspot` | HitMarkerPolicy.DefaultWeakspotColor | `Plugin.cs` |
| `HitMarker/Enabled` | HitMarkerPolicy.DefaultEnabled | `Plugin.cs` |
| `HitMarker/HitEnabled` | true | `Plugin.cs` |
| `HitMarker/KillEnabled` | true | `Plugin.cs` |
| `HitMarker/Size` | HitMarkerPolicy.DefaultSize | `Plugin.cs` |
| `HitSound/Enabled` | true（1.2.16 旧键，升级后仍作父开关） | `SoundConfigBinding.cs` |
| `HitSound/HitEnabled` | 迁移：新安装 false / 旧配置 true（跟随旧 Enabled 父开关） | `SoundConfigBinding.cs` |
| `HitSound/HitVolume` | 迁移：新安装 0.4 / 旧配置 0.8（实际 = 值 × 主音量 ÷ 0.8） | `SoundConfigBinding.cs` |
| `HitSound/KillEnabled` | 迁移：新安装 false / 旧配置 true（跟随旧 Enabled 父开关） | `SoundConfigBinding.cs` |
| `HitSound/KillVolume` | 迁移：新安装 0.4 / 旧配置 0.8（实际 = 值 × 主音量 ÷ 0.8） | `SoundConfigBinding.cs` |
| `HitSound/MinIntervalSeconds` | (float)HitSoundPolicy.DefaultMinIntervalSeconds | `Plugin.cs` |
| `HitSound/MuteVanillaIndicator` | HitSoundPolicy.DefaultMuteVanillaIndicator | `Plugin.cs` |
| `HitSound/Style` | HitSoundPolicy.DefaultStyle | `Plugin.cs` |
| `HitSound/Volume` | 0.8 = 100% 主音量（1.2.16 旧键，升级后仍逐次生效） | `SoundConfigBinding.cs` |
| `Input/PreviewKey` | KeyCode.F9 | `Plugin.cs` |
| `Input/ToggleKey` | KeyCode.F7 | `Plugin.cs` |
| `KillCounter/Enabled` | true | `Plugin.cs` |
| `KillIcon/ArmorFlyerColor` | KillIconPolicy.DefaultArmorPlateColor | `Plugin.cs` |
| `KillIcon/Display` | "" | `Plugin.cs` |
| `KillIcon/FlyerColor` | KillIconPolicy.DefaultWingsColor | `Plugin.cs` |
| `KillIcon/HeadEnabled` | false | `Plugin.cs` |
| `KillIcon/HeadGapMetres` | KillIconPolicy.DefaultGapMetres | `Plugin.cs` |
| `KillIcon/HeadLifetime` | KillIconPolicy.DefaultLifetimeSeconds | `Plugin.cs` |
| `KillIcon/HeadMaxIcons` | KillIconPolicy.DefaultMaxIcons | `Plugin.cs` |
| `KillIcon/HeavyHealthThreshold` | KillPolicy.DefaultHeavyHealthThreshold | `Plugin.cs` |
| `KillIcon/MinorHealthThreshold` | KillDefaults.MinorHealthThreshold | `Plugin.cs` |
| `KillIcon/Opacity` | 0.5f | `Plugin.cs` |
| `KillIcon/ViewMode` | "" | `Plugin.cs` |
| `Layout/BackgroundOpacity` | FeedbackPolicy.DefaultPanelOpacity | `Plugin.cs` |
| `Layout/CardLifetime` | KillDefaults.CardLifetime | `Plugin.cs` |
| `Layout/FadeSeconds` | KillDefaults.FadeSeconds | `Plugin.cs` |
| `Layout/HorizontalOffset` | 0f | `Plugin.cs` |
| `Layout/IconSize` | KillDefaults.IconSize | `Plugin.cs` |
| `Layout/MaxCards` | KillDefaults.MaxCards | `Plugin.cs` |
| `Layout/MaxDistance` | KillDefaults.MaxDistance | `Plugin.cs` |
| `Layout/VerticalOffset` | 0f | `Plugin.cs` |
| `TeammateMark/CustomIconPath` | "" | `Plugin.cs` |
| `TeammateMark/Enabled` | true | `Plugin.cs` |
| `TeammateMark/HeadClearanceMetres` | TeammateMarkPolicy.DefaultHeadClearance | `Plugin.cs` |
| `TeammateMark/HideDuringEMP` | false | `Plugin.cs` |
| `TeammateMark/Icon` | "Cai" | `Plugin.cs` |
| `TeammateMark/LogMarks` | false | `Plugin.cs` |
| `TeammateMark/MaxDistanceMetres` | TeammateMarkPolicy.DefaultMaxDistance | `Plugin.cs` |
| `TeammateMark/MaxPixels` | TeammateMarkPolicy.DefaultMaxPixels | `Plugin.cs` |
| `TeammateMark/MinPixels` | TeammateMarkPolicy.DefaultMinPixels | `Plugin.cs` |
| `TeammateMark/RefDistanceMetres` | TeammateMarkPolicy.DefaultRefDistance | `Plugin.cs` |
<!-- END:generated-keys -->

## 10. 复现

```bash
python tools/feature_switch_inventory.py                     # 重新生成 §9（当前 keys=110）
dotnet build tests/KillFeed.Tests/KillFeed.Tests.csproj -c Release -p:GTFO_DIR=/path/to/Survival
dotnet tests/KillFeed.Tests/bin/Release/net6.0/KillFeed.Tests.dll   # SUMMARY pass=407;fail=0（含 gate.doc.* 清单门禁）
dotnet build tests/GTFOKillFeed.Smoke/GTFOKillFeed.Smoke.csproj -c Release
dotnet tests/GTFOKillFeed.Smoke/bin/Release/net6.0/GTFOKillFeed.Smoke.dll   # PASS: 1409（KillCounterTests 覆盖 §8b）
```
