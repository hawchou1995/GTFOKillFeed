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

## 5. 敌人血条与焦点描边

| 功能 | 键 | 默认 | 父功能 | 关闭后 |
|---|---|---|---|---|
| 血条 | `EnemyBar/Enabled` | true | `General/Enabled` + F7 | 不画血条与标签 |
| 焦点描边 | `EnemyBar/FocusOutlineEnabled` | true | `EnemyBar/Enabled` | 只停描边，条体与文字照旧 |
| 名称 / 数值 / 弱点标签 | `EnemyBar/ShowName`、`EnemyBar/ShowNumbers`、`EnemyBar/ShowWeakspotPart` | true | `EnemyBar/Enabled` | 只停对应文字 |

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

资源回收不变量（`tests/KillFeed.Tests` 的 `gate.corpse.*` 用例逐条检查）：
- `num3 >= item.EndAt` 的生命周期收尾块**不受任何子开关控制**：`ReleaseGibs`、`CorpseFade.TryBegin`、`Hide`、`RecyclePhysics` 一律照旧执行。
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
| `Corpse/ExplosionDelay` | 0.1f | `Plugin.cs` |
| `Corpse/ExplosionFx` | false | `Plugin.cs` |
| `Corpse/FadeEffect` | true | `Plugin.cs` |
| `Corpse/Launch` | true | `Plugin.cs` |
| `Corpse/MaxLaunchDistance` | 18f | `Plugin.cs` |
| `Corpse/MinLaunchDistance` | 12f | `Plugin.cs` |
| `Corpse/TravelScale` | 1f | `Plugin.cs` |
| `DamageFx/DiamondThreshold` | 20000L | `Plugin.cs` |
| `DamageFx/GoldThreshold` | 10000L | `Plugin.cs` |
| `DamageFx/PrismaticEnabled` | true | `Plugin.cs` |
| `DamageFx/PrismaticThreshold` | 60f | `Plugin.cs` |
| `DamageFx/SilverThreshold` | 5000L | `Plugin.cs` |
| `DamageFx/TiersEnabled` | true | `Plugin.cs` |
| `DamageFx/WhiteThreshold` | 3000L | `Plugin.cs` |
| `DamageNumbers/ApproachEffect` | false | `Plugin.cs` |
| `DamageNumbers/Enabled` | true | `Plugin.cs` |
| `DamageNumbers/FontSize` | 27f | `Plugin.cs` |
| `DamageNumbers/FontWeight` | 1f | `Plugin.cs` |
| `DamageNumbers/Lifetime` | 0.72f | `Plugin.cs` |
| `DamageNumbers/MergeShotgunPellets` | true | `Plugin.cs` |
| `DamageNumbers/Opacity` | 1f | `Plugin.cs` |
| `DamageNumbers/ShowShotgunDamageNumbers` | true | `Plugin.cs` |
| `DamageNumbers/WeakspotColor` | "#E5484D" | `Plugin.cs` |
| `EnemyBar/Anchor` | "Head" | `Plugin.cs` |
| `EnemyBar/BarHeight` | 5f | `Plugin.cs` |
| `EnemyBar/BarWidth` | 200f | `Plugin.cs` |
| `EnemyBar/BorderColor` | "#0B0E10" | `Plugin.cs` |
| `EnemyBar/Enabled` | true | `Plugin.cs` |
| `EnemyBar/FillColor` | "#E8ECEB" | `Plugin.cs` |
| `EnemyBar/FillRedColor` | "#EF4C4C" | `Plugin.cs` |
| `EnemyBar/FillYellowColor` | "#E6B84C" | `Plugin.cs` |
| `EnemyBar/FocusOutlineEnabled` | true | `Plugin.cs` |
| `EnemyBar/GhostColor` | "#A8B0B2" | `Plugin.cs` |
| `EnemyBar/HeadOffsetMetres` | 0.12f | `Plugin.cs` |
| `EnemyBar/HealthTextColor` | "#E8ECEB" | `Plugin.cs` |
| `EnemyBar/LogFocus` | false | `Plugin.cs` |
| `EnemyBar/MaxDistance` | 60f | `Plugin.cs` |
| `EnemyBar/MaxScreenFraction` | 0.46f | `Plugin.cs` |
| `EnemyBar/MinScreenFraction` | 0.1f | `Plugin.cs` |
| `EnemyBar/NameColor` | "#E8ECEB" | `Plugin.cs` |
| `EnemyBar/NumberSize` | 12f | `Plugin.cs` |
| `EnemyBar/Opacity` | 0.55f | `Plugin.cs` |
| `EnemyBar/RedThresholdPercent` | 33f | `Plugin.cs` |
| `EnemyBar/ScreenAnchorFraction` | 0.3f | `Plugin.cs` |
| `EnemyBar/ShowName` | true | `Plugin.cs` |
| `EnemyBar/ShowNumbers` | true | `Plugin.cs` |
| `EnemyBar/ShowWeakspotPart` | true | `Plugin.cs` |
| `EnemyBar/TrackColor` | "#161A1C" | `Plugin.cs` |
| `EnemyBar/VerticalOffset` | -8f | `Plugin.cs` |
| `EnemyBar/WeakspotColor` | "#EF4C4C" | `Plugin.cs` |
| `EnemyBar/YellowThresholdPercent` | 66f | `Plugin.cs` |
| `General/Enabled` | true | `Plugin.cs` |
| `General/Language` | "zh" | `Plugin.cs` |
| `HitMarker/ColorArmor` | "#49ADC9" | `Plugin.cs` |
| `HitMarker/ColorKill` | "#B92828" | `Plugin.cs` |
| `HitMarker/ColorNormal` | "#FFFFFF" | `Plugin.cs` |
| `HitMarker/ColorWeakspot` | "#FF8C00" | `Plugin.cs` |
| `HitMarker/Enabled` | true | `Plugin.cs` |
| `HitMarker/HitEnabled` | true | `Plugin.cs` |
| `HitMarker/KillEnabled` | true | `Plugin.cs` |
| `HitMarker/Size` | 64f | `Plugin.cs` |
| `HitSound/Enabled` | true（1.2.16 旧键，升级后仍作父开关） | `SoundConfigBinding.cs` |
| `HitSound/HitEnabled` | 迁移：新安装 false / 旧配置 true（跟随旧 Enabled 父开关） | `SoundConfigBinding.cs` |
| `HitSound/HitVolume` | 迁移：新安装 0.4 / 旧配置 0.8（实际 = 值 × 主音量 ÷ 0.8） | `SoundConfigBinding.cs` |
| `HitSound/KillEnabled` | 迁移：新安装 false / 旧配置 true（跟随旧 Enabled 父开关） | `SoundConfigBinding.cs` |
| `HitSound/KillVolume` | 迁移：新安装 0.4 / 旧配置 0.8（实际 = 值 × 主音量 ÷ 0.8） | `SoundConfigBinding.cs` |
| `HitSound/MinIntervalSeconds` | 0.05f | `Plugin.cs` |
| `HitSound/MuteVanillaIndicator` | false | `Plugin.cs` |
| `HitSound/Style` | "BF5" | `Plugin.cs` |
| `HitSound/Volume` | 0.8 = 100% 主音量（1.2.16 旧键，升级后仍逐次生效） | `SoundConfigBinding.cs` |
| `Input/PreviewKey` | KeyCode.F9 | `Plugin.cs` |
| `Input/ToggleKey` | KeyCode.F7 | `Plugin.cs` |
| `KillIcon/ArmorFlyerColor` | "#E6AB57" | `Plugin.cs` |
| `KillIcon/Display` | "" | `Plugin.cs` |
| `KillIcon/FlyerColor` | "#8FD3E8" | `Plugin.cs` |
| `KillIcon/HeadEnabled` | false | `Plugin.cs` |
| `KillIcon/HeadGapMetres` | 0.3f | `Plugin.cs` |
| `KillIcon/HeadLifetime` | 2.2f | `Plugin.cs` |
| `KillIcon/HeadMaxIcons` | 6 | `Plugin.cs` |
| `KillIcon/HeavyHealthThreshold` | 1000f | `Plugin.cs` |
| `KillIcon/MinorHealthThreshold` | 120f | `Plugin.cs` |
| `KillIcon/Opacity` | 0.5f | `Plugin.cs` |
| `KillIcon/ViewMode` | "" | `Plugin.cs` |
| `Layout/BackgroundOpacity` | 0f | `Plugin.cs` |
| `Layout/CardLifetime` | 2.6f | `Plugin.cs` |
| `Layout/FadeSeconds` | 0.45f | `Plugin.cs` |
| `Layout/HorizontalOffset` | 0f | `Plugin.cs` |
| `Layout/IconSize` | 46f | `Plugin.cs` |
| `Layout/MaxCards` | 3 | `Plugin.cs` |
| `Layout/MaxDistance` | 999f | `Plugin.cs` |
| `Layout/VerticalOffset` | 0f | `Plugin.cs` |
| `TeammateMark/CustomIconPath` | "" | `Plugin.cs` |
| `TeammateMark/Enabled` | true | `Plugin.cs` |
| `TeammateMark/HeadClearanceMetres` | 0.05f | `Plugin.cs` |
| `TeammateMark/HideDuringEMP` | false | `Plugin.cs` |
| `TeammateMark/Icon` | "Cai" | `Plugin.cs` |
| `TeammateMark/LogMarks` | false | `Plugin.cs` |
| `TeammateMark/MaxDistanceMetres` | 60f | `Plugin.cs` |
| `TeammateMark/MaxPixels` | 42f | `Plugin.cs` |
| `TeammateMark/MinPixels` | 26f | `Plugin.cs` |
| `TeammateMark/RefDistanceMetres` | 8f | `Plugin.cs` |
<!-- END:generated-keys -->

## 10. 复现

```bash
python tools/feature_switch_inventory.py                     # 重新生成 §9
dotnet build tests/KillFeed.Tests/KillFeed.Tests.csproj -c Release
tests/KillFeed.Tests/bin/Release/net6.0/KillFeed.Tests.exe   # 含清单完整性门禁
```
