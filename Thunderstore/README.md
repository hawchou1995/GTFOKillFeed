# KillFeed 1.2.35
独立的 GTFO 客户端反馈插件，需要 BepInEx，并以前置依赖 DamageSync 实现联机敌人血条。
击杀计数在池化复用的敌人身上不再停住：离场时会把该尸位的“已记过”记录撤掉，重置后仍按原规则去重。若某次击杀被判定为重复，日志会给一条有界回执而不是静默丢弃。

切换分辨率后，如果原生 HUD 纹理仍是旧尺寸，只有画布与纹理自洽、视口完整且与窗口同宽高比时，才换算真实血条位置。三档击杀计数保持在血条下方，不再挪到水印旁；无法确认锚点时暂不绘制，不重置本局计数，也不修改原生 HUD。

## 尸体淡出与硬直提示

优先接续游戏已启动且能确认属于同一具尸体的原生速溶，把剩余动画压到最多 0.5 秒。不重新播放、不额外借用实例。没有可接续的实例且共享池已满时仍按原定时限隐藏。

池已满时，KillFeed 会把游戏自己的速溶协程交给这具尸体播放，因此不再受“借不到实例”限制。它没有新增补丁，也不接管游戏的状态机；原清理时限不变。

`Corpse/FadeEffect` 开启时，KillFeed 只对能够安全绘制的尸体部件播放淡出。带阴影注册组件的部件和已有的衰减副本不会被复制；优先改用同一具尸体上的安全部件，找不到就按原清理时间隐藏。日志中 `hazardous-input` 表示主动跳过，不需要关闭全局阴影或改动相机设置。其他模组接管 `Decay.Play` 时，KillFeed 让位。

池有实例但全忙时，KillFeed 会短暂退避。如果空闲、占用和总数都为零，不按忙池反复重试；尸体原定清理时再检查一次，仍没有可用实例则按时隐藏。尸体自身已准备好的安全淡出不依赖该池。关末 `started` 是观察到播放，`idleObserved` 不是归还证明；`manualReleases` 才是已读回空闲表的手动归还。

检测到 MemoryLeakFix 的蒙皮延迟回调或第三方接管 `Decay.Play` 时，KillFeed 在查找淡出对象前让位，尸体按原时限隐藏。不移除第三方补丁、不干预游戏自己的播放。`compatBeforeScan` 是 `compatSkipped` 中在查找前就让位的次数。关末 `pool=not-sampled` 表示本关没有采样，不能当作缺池。缺少同尸组件时标记 `playing=n/a`；已结束的淡出不会在清理时重复查找渲染器。

准星对准敌人时，`EnemyBar/ShowStaggerLine` 在敌人名称下方显示与首字左端对齐的浅绿提示，每项只给一个**理论最大阈值**：`[四肢：40][躯体：50][普通(阈值)：80]`。四肢和躯体取该组可确认的未毁可破部位的固定上限，并且**同组只取最大值**（上限 40 和 60 只显示 `60`，不显示区间）；部位已毁、部位不可确认、上限不可读、或游戏自己的断肢硬直开关不是 Micro/Light/Heavy（含 None、Unspecified、死亡类反应与读取失败）时整项省略——省略不代表免疫。普通硬直独立取 `DamageUntilHitreact` 本身，不减累计值、不是躯体耐久也不是敌人血量。数值按游戏真实值显示（不四舍五入、不截断）。没有有效项就整项不显示，三项都没有时整行不画、不占位，也不会留下空行；切怪或目标丢失后不会串用上一个敌人的数字。

伤害数字只有在 `OnTakeDamage` 等游戏读数能确认本次本机命中触发轻/重硬直时才变浅绿（`#78DCA0`）；证据不足保留原色。合并霰弹中若仅部分弹丸触发，显示如 `12 + 6`，只有后面的 6 为绿色，总伤害仍是 18。弱点、护甲、棱彩、档位描边及透明度保留各自设置。可查 `[DN] stagger=` 日志判断本次是否满足着色条件。

## 血条击杀计数

本机玩家血条下方按 Minor / Elite / Boss 分别累计确认归属本机的击杀，远征结束后清零。`KillCounter/Enabled` 可独立关闭显示；F7 隐藏界面不影响累计，F9 预览不会增加真实击杀。

## 截图

以下均为作者本人会话中的游戏内实拍，不是效果图。

默认的透明击杀卡片位于画面左下（弱点击杀卡片，随后是距离 / 最大生命值 / 敌人名称），琥珀色 `60!` 是弱点伤害数字，飞散的碎块来自游戏原生尸体爆裂：

![KillFeed 实机画面中的弱点击杀卡片、弱点伤害数字与尸体碎块](https://raw.githubusercontent.com/hawchou1995/HawChow-Mod-Assets/main/KillFeed-weakspot-kill-gameplay.jpg)

手持 C-Foam 胶枪瞄准敌人时，血条把弱点标签替换为该敌人所需的完整结沫团数，标签固定为暖琥珀橙 `#F2A65A`：

![实机截图：手持 C-Foam 胶枪时，准星血条在敌人名后显示橙色结沫团数 [结沫*2]](https://raw.githubusercontent.com/hawchou1995/HawChow-Mod-Assets/main/KillFeed-cfoam-healthbar-1.2.8.png)

换回普通武器后，同一位置恢复弱点部位与实时倍率标签，默认红色：

![实机截图：使用普通武器时，准星血条在敌人名后显示红色弱点标签 [头3×]](https://raw.githubusercontent.com/hawchou1995/HawChow-Mod-Assets/main/KillFeed-weakspot-healthbar-1.2.8.png)

## 击杀卡片

两行紧凑信息：击杀确认，然后是距离 / 可选最大生命值 / 敌人名称。按血量划分的 MINOR / ELITE / BOSS 等级已移除。默认最多 **3 张卡片**（`MaxCards`：1–6，满时移除最旧的一张）。普通与弱点击杀使用初版原始骷髅美术，未做改动。卡片**默认完全不绘制面板**（`Layout/BackgroundOpacity` = 0；可提高到 0.9 加一层薄色），图标与两行文字都保留 1 px 深色阴影，因此在明亮场景中依然可读。卡片紧凑，位于屏幕中心下方。

## 伤害数字

护甲 `[6.75]`、普通 `24.35`、弱点 `81.2!`：同一套带描边的字形家族，但尺寸、标记与克制的语义色各不相同。短促弹出、向外弧线与淡出，灵感来自 Brotato。默认存活 0.72 秒；同时最多 64 个数字。可观测时使用最终伤害结算；否则调用游戏自身的抗性与肢体/背击修正，做法参考 DamageAnalyzer。数值保留小数并包含过量伤害，而不是把所有命中取整。开启全局 `DamageNumbers/Enabled` 后，`ShowShotgunDamageNumbers` 可独立开关霰弹枪弹字。`MergeShotgunPellets` 默认开启：一次开火（volley）内同一敌人的弹丸合并成一个数字，弹丸在后续帧陆续结算仍算同一枪（窗口上限 0.5 秒，距最后一颗 0.05 秒静默即提前结算），两次开火绝不合并；关闭时每颗弹丸各自立即显示，关闭 `ShowShotgunDamageNumbers` 则这些弹丸完全不显示。混合命中类型使用中性样式。

弱点数字的颜色现在由 `DamageNumbers/WeakspotColor` 控制，默认克制的深红 `#E5484D`（可写 `#RRGGBB` 或 `#RRGGBBAA`，非法值回落琥珀，保存后热重载）。普通数字仍是中性的 `Ink` 白，护甲数字仍是固定的灰蓝 `[x]`，击杀卡片标题仍是琥珀色——三者都不受影响；6 位色时数字的淡出 alpha 与之前完全一致。

伤害数字的整体不透明度由 `DamageNumbers/Opacity` 控制（默认 `1.0`，范围 0–1，可写 `0.5` 这类小数）。该乘数与原有的生命周期淡出相乘恰好一次，并同时作用于正文与全部 8 道描边：`0.5` 是半透明但仍保留原来的淡出曲线，`0` 完全不绘制（不提交任何文字或描边），`1.0` 与更新前的画面完全一致。`WeakspotColor` 写成 `#RRGGBBAA` 时，颜色自身的 alpha 仍只参与正文一次（描边继续不跟随弱点颜色 alpha，与旧版相同）。越界值夹取到 0–1，`NaN`/`Infinity` 回落到 `1.0`；保存 `.cfg` 后热重载，已经在飞的数字下一帧即按新值绘制。
## 命中/击杀音效

内置 Battlefield 风格的反馈音，在**本地玩家**命中与击杀时播放：

- 三套风格 `HitSound/Style`：`BF1`、`BF5`（默认）、`BF6`，非法值自动回落 `BF5`。
- 1.2.16 起共 25 个 PCM WAV（48000 Hz 单声道 16-bit，120–450 ms），全部内嵌在 DLL 里，不需要额外下载：
  - 命中：普通、弱点、护甲三种；
  - 击杀：血量档位 MINOR / ELITE / BOSS 各一套；
  - 飞行击杀与装甲飞行击杀：在各自档位上再叠加不同的飞行／护甲音色，**档位仍然可辨**。
- 血量档位按敌人整体 `HealthMax` 划分：默认 `<120` MINOR、`120–<1000` ELITE、`>=1000` BOSS。BOSS 线就是**既有**的 `KillIcon/HeavyHealthThreshold`（同一把尺子决定重型图标与 BOSS 音效），1.2.16 只新增 `KillIcon/MinorHealthThreshold`，两者均可热重载。旧配置把重型线设在 `<120` 时保持该值：低于它是 MINOR、达到它就是 BOSS，ELITE 区间为空。飞行身份来自游戏数据 `EnemyBehaviorDataBlock.IsFlyer`，装甲身份来自敌人自身的护甲部位，都与最后一次打中的部位无关。
- 素材事实：这套音源只对 `light`/`medium` 击杀音（和 BF1 命中音）提供风格变体；`heavy`、`flying`、`hit_heavy` 只有一份来源，因此这些类别统一使用 `bf5` 文件，不会出现"名字不同、字节相同"的假变体。弱点／护甲命中音与飞行／装甲飞行击杀音是**确定性派生**（截取或固定比例混合），来源与哈希写在 `reports/feature-1.2.16-analysis.md`。
- **默认静音原版命中提示层**（`HitSound/MuteVanillaIndicator = false`，即默认**不**静音）：把这一项改成 `true` 才会跳过 `BULLETHITENEMYINDICATOR`、`BULLETHITENEMYLOCALINDICATOR`、`BULLETHITENEMYWEAKSPOTLOCALINDICATOR` 三个"命中确认叮声"。子弹真正打在敌人身上的冲击音（`BULLETHITENEMYLOCAL`/`SYNC`、`BULLETHITENEMYWEAKSPOTLOCAL`/`SYNC`）、敌人受伤/死亡语音，以及其它所有音效**完全不受影响**。
- 这个静音开关与 `HitSound/Enabled`、`Volume`、`Style` 互相独立：关掉自建音效**不会**让原版提示音回来；静音开关也不会让新音效变哑。
- `HitSound/Volume` 默认 `0.8`（0–1，越界夹取，非有限值回落 0.8）；`HitSound/MinIntervalSeconds` 默认 `0.05`（0.02–3.0），命中与击杀各自独立节流，机枪连射不会糊成一片。

## BFV 击杀图标与命中标记

- **击杀反馈默认画在敌人头顶**：本地确认击杀时在该敌人**头顶**显示 BFV 击杀图标，档位与击杀音效共用同一个 `KillPresentation.Tier`：MINOR = `light`（白骷髅）、ELITE = `medium`（橙骷髅）、BOSS = `heavy`（红齿轮）。BOSS 线仍是 `KillIcon/HeavyHealthThreshold` 一条权威线（默认 `1000`，夹取 1–1000000000，`NaN` 回落 1000，保存即热重载），判定读敌人自己的 `HealthMax`，不写死任何敌人 ID。图标几何取自 BFV 素材的 64×64 矩形块表，构建期渲染成 128×128，硬边像素原样保留。想回到旧版卡片式击杀提示，把 `KillIcon/Display` 改成 `Card` 即可（大小写不敏感，保存即生效、不用重启），卡片的尺寸/位置/生命/背景透明度/距离与血量显示一律保持原样，`General/Enabled` 与 F7 对两种呈现一视同仁。
- **命中线条三档 + 击杀线条，替代原版标记（1.2.17）**：护甲命中 = BFV `vehiclehit`（青色四角括号），普通命中 = `hitmark`（白 X），弱点命中 = `killhitmark`（橙 X，比普通大 18%），**击杀** = 同一 `killhitmark` 几何加粗后的独立红线条（默认 `#B92828`，比弱点大 35%，停留更久）。击杀线条与头顶击杀图标是两种独立效果：致命一击先在**实际受击部位**显示击杀线条，头顶图标另行显示（本版默认就是它）。线条跟随游戏自己报告的世界命中点（`CrosshairGuiLayer.ShowHitIndicator` / `ShowDeathIndicator`）并随相机投影，因此**不再画在准星正中**；相机后方或屏幕外的命中不绘制。尺寸基准 `HitMarker/Size`（默认 `64` px @1080p，16–200 夹取）。`HitMarker/Enabled`（默认 `true`）逐事件替换原版；把它改成 `false` 回到原版标记。资源缺失、投影失败、友伤命中、无法与本地伤害事件对应的零伤害命中、补丁缺失时该事件仍显示原版，绝不会两个标记叠在一起或完全没有命中反馈。护甲判定来自原始伤害方法**运行前**建立的本地命中上下文（同一点 0.25 m 内、同一/上一帧、只用一次）：确认为本地装甲部位的零伤害命中会显示装甲线条并只抑制那一次原版，未确认的 `noDamage` 事件不会被猜成护甲。四档同时用**形状、尺寸、颜色**区分，颜色键 `HitMarker/ColorNormal`、`ColorWeakspot`、`ColorArmor`、`ColorKill`（默认 `#FFFFFF`、`#FF8C00`、`#49ADC9`、`#B92828`）全部热重载。
- **命中/击杀音效默认关闭**：`HitSound/Enabled` 现在默认 `false`，想要 BF 那三套 cue 的人自己打开；原版提示层的静音开关 `HitSound/MuteVanillaIndicator` 默认仍为 `true`，`MuteVanillaIndicator` 现在也默认 `false`：默认安装保留原版叮声，想要 BF 手感就把两者一起打开；两个开关互相独立。
- **头顶位置（1.2.16 起在击杀瞬间冻结）**：世界锚点 = 敌人真实头部 limb（与准星焦点血条同一个 helper，含 0.12 m 头部 padding）再向上 `KillIcon/HeadGapMetres`（默认 `0.30` m）；血条开着且为 Head 模式时额外叠加血条自己的 `EnemyBar/HeadOffsetMetres`，所以图标始终在血条上方留有清晰间隙；血条为 Screen 模式或关闭时仍在怪物头顶，不贴屏幕边缘。锚点在**本地致死 credit 的那一刻只取一次**（对象仍是刚死的那只；头部不可读时改用致死伤害事件已携带的世界坐标并抬同样的 clearance），之后**不再逐帧重读活体/尸体头部**，只随相机重投影——所以尸体倒地不会把图标拖着一起下滑（对齐源 BFV 模组「不做世界跟随」）。对象池指针只作身份审计，不参与寻位，复用对象也不会把图标拽到新敌人身上；无任何可用锚点时不绘制并回退卡片，绝不跳到屏幕中心。相机后方/屏幕外、无相机时不绘制。`KillIcon/HeadLifetime`（默认 `2.2` s）控制淡入淡出，`HeadMaxIcons`（默认 `6`，上限 12）限制同屏数量（满时移除最旧），切关/重生即清空。
- **同档区分普通飞行与装甲飞行（形状优先）**：普通飞行在图标下方加翼形 chevron（`KillIcon/FlyerColor`，默认 `#8FD3E8`），装甲飞行加空心装甲板（`KillIcon/ArmorFlyerColor`，默认 `#E6AB57`），地面击杀没有徽记——先靠形状、颜色只作冗余编码。`IsFlyer` 读游戏自己的 `EnemyBehaviorDataBlock.IsFlyer`，装甲判定读敌人自身 limb 布局里是否存在 `eLimbDamageType.Armor`（与打中哪条 limb 无关）。卡片模式下图标角上也带同一个徽记，所以音效、图标、卡片三者永远同档同飞行。
## 伤害数字的棱彩与档位

- **伤害数字的轨迹是可选的，默认关闭**（`DamageNumbers/ApproachEffect`，默认 `false`）：关闭时是**初版行为**——数字在命中点浮现，按原公式上飘并淡出，不朝屏幕中心汇聚、不做穿身两段、不随飞行放大（大小只用 `FontSize` 与弹出缩放）。把它改成 `true` 才启用**朝玩家视点飞来**：从命中点滑向准星/穿身而过、普通数字放大到 1.45×、棱彩放大到 2.45×，末段淡出，每个数字落在准星上方自己的槽位，连射时不会叠成一团。**棱彩与档位的开关见下方"伤害数字的棱彩与档位"一节，四个开关彼此独立。**
- **棱彩是材质效果，不是换色**：触发棱彩的数字会画成**全息箔**——8 个方向各一种粉彩色组成外圈（1.6 px 核心环 + 3.1 px 半透明光晕环）、一个随相位旋转的白色浮雕高光、主体是接近白的粉彩核心并带呼吸微光；血条棱彩描边把整圈周长切成 20 段、色相沿边连续推进，厚度提升到 `clamp(BarHeight × 0.55, 2, 5)` px（默认 5 px 血条下从 1.5 px → 2.75 px），否则色阶在 HUD 上根本看不出来。
- **棱彩数字的来袭方向是随机的**：普通数字沿"命中点 → 自己的槽位"直飞；棱彩数字的落点固定为同一个点（准星上方 4 px，与槽位无关），飞过来的方向按每个数字自己的种子在 ±34° 内随机，飞行途中角度随进度增长，落点不受影响。
- **棱彩**（`DamageFx/PrismaticThreshold`，默认 `60`；`DamageFx/PrismaticEnabled`，默认 `true`）：单次伤害**严格大于**阈值时，这一个数字会以 1.8 秒一圈的色相循环绘制。`60.0` 不触发、`60.01` 触发；`NaN`/负值/无穷不触发；写成 `0` 即关闭该效果。`PrismaticEnabled = false` 则完全关闭棱彩外观（不画箔、不做棱彩轨迹与放大，按普通数字渲染）；两者是「与」的关系，任一关闭即关闭。同屏多个棱彩数字带相位偏移，颜色各不相同。两个键都热重载，屏幕上的数字立即重绘。
- **伤害数字粗细（`DamageNumbers/FontWeight`，默认 `1`，范围 0–2）**：`0–0.5` 换成 Normal 字面并把描边收到 0.9–1.35 px（细），`1` 与现在完全一致（Bold + 1.6 px 描边、无额外叠印），`>1` 额外把本体在半径 0–0.55 px 的小圆上叠印 4 次（真正加粗笔画，不只是加粗阴影），描边到 2.3 px。越界夹取、`NaN` 回落 `1`，保存即热重载，已在屏幕上的数字立刻变粗细。
- **档位**（`DamageFx/TiersEnabled`，默认 `true`）：本关累计伤害达到 `3000` / `5000` / `10000` / `20000` 时，伤害数字依次进入 **白 / 银 / 金 / 钻** 四档（取已达成的最高档）。**累计值按关卡重置**——换关、重开、插件重载会清零；暂停、EMP、改配置**不会**清零。**四档阈值可改**：`DamageFx/WhiteThreshold`、`SilverThreshold`、`GoldThreshold`、`DiamondThreshold`（默认 3000/5000/10000/20000，保存即热重载）。
- 叠加规则：**进档后所有伤害数字（普通／弱点／护甲）主体色统一换成档位色**，外描边同时用该档位色（alpha×0.55）；弱点与护甲改由 `!`、`[ ]` 与字号区分。棱彩开启且已进档时，内圈核心环**仍保留档位色**，只有光晕、浮雕与主体是箔色。`TiersEnabled = false` 会同时去掉档位主体色与档位描边（含棱彩内圈那一圈），累计值仍照常记录但不再上色。棱彩、档位、轨迹三个开关两两独立：都关闭时绘制结果与初版**逐值一致**。

## 血条专注描边

准星焦点血条的描边（`EnemyBar/FocusOutlineEnabled`，默认 `true`）：准星射线落在敌人**背部**（敌人朝向的反半球）→ 橙色 `#FF8A1E`；落在**弱点** limb → 红色 `#E5484D`（与弱点数字同一个红）；两者同时成立 → 棱彩。厚度 `clamp(BarHeight × 0.30, 1, 3)` 像素，跟随血条自身的淡入淡出；关掉后不新增任何绘制。

## 尸体反馈

所有敌人死亡都通过 typed 的 `EnemyAgent.OnDead` 与 `Dam_EnemyDamageBase.InstantDead` 钩子调度，且仅在游戏处于 `InLevel` 时进行，因此炮台击杀、队友击杀等非本地来源也能获得本地的外观爆裂。队列在离开关卡时清空。击杀卡片仍只显示本地来源。致命本地命中在采样爆裂强度之前记录；其他来源使用中性强度 0.5。重复回调按池化敌人身份去重，没有敌人 ID 允许列表，也没有固定的活跃/隐藏数量上限。击飞预算默认 **最小 12 m / 最大 18 m**；配置把最小值钳制在 12–20 m、最大值钳制在最小值到 30 m 之间，缩放后的位移在 40 m 绝对上限下仍保持 12 m 下限。布娃娃查找会重试到爆裂截止时间；尸体沿径向击飞。每具尸体尝试 **3–7** 个分离的原生爆裂特效与 **2–6** 个可破坏身体部位。爆裂默认在 KillFeed 接受死亡排程后 0.1 秒触发，可配置 0.1–1.5 秒，与 DBO 的 0.1 秒消散启动时间对齐；升级时保留已保存的 `ExplosionDelay` 值。爆裂后尸体保持可见与物理状态 0.75 秒，随后冻结刚体、禁用碰撞体并隐藏渲染器。若其他清理模组先把敌人根节点移出场景，KillFeed 使用缓存的死亡位置播放爆裂、释放其碎块并提前回收租用的物理；它会跳过后续断肢，并在池复用或关卡清理时恢复池对象状态。自定义 WAV、本地音效回退、本地破坏视觉效果与逐敌诊断均未改变。

`Corpse/ExplosionFx` 默认 **false** ——**相对更早版本这是默认行为变更**：更早版本恒定播放尸体爆裂特效与爆炸音效，现在默认关闭，想要保留旧观感请在 `HawChow.GTFOKillFeed.cfg` 里手动写 `ExplosionFx = true`。该键只关闭原版 `FXC_FlyerDeath` 爆裂特效与爆炸音效（含热关闭时清空待播的合并音效），击飞、断肢、碎块、爆裂后的隐藏与租约回收、死亡统计、InLevel 与去重队列都不受影响。已有 `.cfg` 没有这个键时按默认 false 生效。

`原生快速消融（默认开启）`：优先播放尸体自身已武装的安全 Decay 组件；没有可用组件时借用游戏已有池，以 `TryCast` 解析 renderer、`Initialize(renderer, 空 IRF 表)` 并回读 `m_targetRenderer` 身份后再 Play。武装异常记 `fade=failed;reason=pool-arm-failed`，身份不符记 `fade=skipped;reason=pool-unverifiable`。不创建池，不销毁 Decay，不 patch ES_*，不触碰源材质。只有未开始播放且能证明空闲的实例才 Cleanup/Return；playing/unknown 交给原生生命周期，保留占额，不按时钟强归还。

1.2.20 在 EndAt 前 0.5 秒提供最多两次准备机会、间隔 0.25 秒，只有 concurrency-cap/no-free-instance 暂态容量不足才重试；原 EndAt 保留最后一次未解决尝试，仍按顺序 Hide/回收物理/移除队列。不在隐藏后排队重播，不延长截止；池化并发仍为 2，因此一次大量死亡仍可能部分即时隐藏。第三方 patch `Decay.Play`（如 DBO 1.5.0）、池不存在、prefab 不安全、暂停或无有效 renderer 时让位回退。每次渐变可能播放 `MEDIUM_CORPSE_DECOMP`，不受 `Corpse/ExplosionFx` 的 ENEMYEXPLODE 开关控制。

消融准备与 EndAt 尝试都通过 `CorpseFadePrep.Attempt` 异常屏障，getter 或原生调用失败不会跳过原截止清理。IL 门禁继续禁止销毁 Decay/建池/写材质/改时钟/ES_* detour，Initialize 两个重载只准出现在 Ready，且必须回读绑定身份。Survival 日志已记录两次池化 begun；本版提前预约的观感、长期回池仍需实机。

### 消融失败原因

消融失败原因逐项命名。每关终态采样前 8 具；fallback/waiting 同原因最多 3 条，总信息预算 24 条、警告 4 条。离关输出全量 `fade=totals`（started/deferredAttempts/skipped/failed/active/gameReleases/manualReleases）；deferredAttempts 是暂缓次数而非尸体数。重复逐肢诊断采用前 3 条加每 100 条并在离关汇总，不能把采样行数当作总执行数。

跳过（`fade=skipped`）：

| reason | 含义 |
|---|---|
| `disabled` | 用户把 `Corpse/FadeEffect` 设为 false |
| `corpse-effects-off` | 用户把 `Corpse/Enabled` 或 `Corpse/ExplosionFx` 主开关设为 false |
| `off-level` | 不在关卡内（大厅、加载中） |
| `paused` | 游戏暂停（`timeScale<=0` 或本帧 `deltaTime` 为 0），暂停会无限期占住池实例 |
| `patched-by-third-party` | `Harmony.GetPatchInfo` 在 `Decay.Play(float)`/`Decay.Play()` 上发现任何 Prefix/Postfix/Transpiler/Finalizer（如 DBO 1.5.0），直接回退 |
| `prefab-unsafe` | `Decay` prefab 原型不是 `stopAction=Deactivate` + `autoCleanup=true` |
| `pool-missing` | 原生池对象为 null（`ES_Dead.Setup` 尚未派发，或本关只有不 override `Setup` 的飞行/乌贼 Boss 敌人）。**本插件从不建池**，直接回退 |
| `no-free-instance` | 池存在但 `freeInstanceCount == 0`（原生或其它模组正占满） |
| `no-renderer` | 这具尸体没有任何可见 renderer |
| `off-scene` | 已按距离移出场景并回收物理 |
| `concurrency-cap` | 本插件并发消融已达硬上限 2 |
| `pool-unverifiable` | 借来的池实例没有被武装，无法证明属于这具尸体，已原样归还：不播放 | 
| `pool-arm-failed` | 武装借来的实例时抛出原生异常，或 `m_targetRenderer` 没有指向本具尸体的 renderer：已 `Cleanup`+`Return` 恰好一次，不播放 |

尝试失败（`fade=failed`，附带 `scheduleId`、`polls`、`failures`、`manualReleases`、`active`）：

| reason | 含义 |
|---|---|
| `pool-missing` / `no-free-instance` | 借池时池消失或第三次轮询仍满（**有界重试上限 3 次**，绝不无限占额） |
| `borrow-error` | `Decay.GetInstance()` 或池读取抛异常 |
| `instance-busy` / `instance-tracked` / `instance-gone` | 拿到的实例仍在播放、已被本插件跟踪、或已随关卡销毁：三种都不属于我们，**不释放** |
| `probe-unknown` | 借到手后读不到原生状态，fail-closed 保留名额，不猜不还 |
| `pool-unverifiable` / `pool-arm-failed` | 前者是武装身份不符而 skipped，后者是武装异常而 failed；旧 `initialize-failed`/`initialize-threw` 名称不再使用 |
| `play-not-observed` / `play-threw` | `Play(0.5f)` 调用后 `m_playing` 仍为 false（被吞）或抛出；同样 `Cleanup()` + `pool.Return()` 恰好一次 |
| `unexpected` | 未预期异常，只影响这一次消融 |

**无论以上哪一条，`Hide()`、刚体/碰撞体回收与队列清理都照旧执行**——消融只是视觉附加项，失败即回退为原有的「爆裂后快速隐藏」。池未就绪、第三方拦截、初始化失败都不会让尸体留在场景里，也不会改变击飞/断肢/碎骨的时序。

纯外观：不增加爆炸伤害、不与队友同步、不发送网络破坏包。原生爆炸 FX 参考 Localia 的 MoreCorpseExplosions 做法；血液/断肢表现受 More Gore for 7 Days to Die 启发。不打包其他游戏的美术资源。

## 倒地队友标记

倒地队友头顶会出现一枚深红单字标记，默认「菜」，可在配置里四选一：「菜」（Cai）、「鶸」（Ruo）、「死」（Si）、「殺」（Sha）。它只读游戏状态：队伍来自 `PlayerManager.PlayerAgentsInLevel`（人类与机器人同表），倒地判据是 `PlayerLocomotion.PLOC_State.Downed`，头部锚点优先用原版头顶名字牌 `PlayerSyncModelData.m_nameNavMarkerAlign`，退化到颈部骨与眼位；本机玩家由 `PlayerManager.IsLocalAgent` 排除。标记跟随 3D 世界位置投影，按**本地玩家到该队友的真实距离**缩放：1080p 下 8 m 为 30 px，最近封顶 41 px，最远收紧到 26 px（这条下限由「鶸」的字形封闭空隙实测得出，避免远距离糊成一团），并整体随分辨率缩放、再受屏幕高度 5% 上限约束。视角后方（`screen.z <= 0.1`）、出屏、超过 `MaxDistanceMetres`、目标销毁或状态读不出的目标一律跳过，不夹到屏幕边缘。复活、离队、槽位换人（`SNet_Player.Lookup` 变化）、空名单、离关重进都会收敛或清空缓存；不逐帧扫场景、不做每帧 raycast、不新增 Harmony 补丁，也不发送任何网络包。

图标就是插件目录里的四张透明 PNG：`Assets/icons/teammate-downed-{cai,ruo,si,sha}.png`（128×128，由 `TeammateMark/Icon` 四选一）。可以直接覆盖，或用 `TeammateMark/CustomIconPath` 指向你自己的图——**双轨路径**：以盘符/根开头的值（`C:\...`）按本机绝对路径使用；其他任何值按相对路径处理，相对插件的 `Assets/icons` 目录解析（填 `teammate-downed-sha.png` 就是随包殺图，填 `mymark.png` 即 `Assets/icons/mymark.png`）。UNC/网络路径仍会被拒绝，因为断链的共享目录会卡住探测。文件内容或路径变化会被自动发现并热重载，探测与读取在单个后台任务里完成，不占用 `Update`/`OnGUI`；遇到锁定、半写、坏 PNG 或错误路径时保留上一张有效贴图（从未成功过则回退到内置字形文字绘制），并按失败次数指数退避重试（0.5 s 起、5 s 封顶，每个失败周期只记一条日志），不会出现白块也不会抛异常。四选图标均由作者实机确认（菜/鶸实机截图见下）。

![倒地队友头顶的「鶸」标记（实机）](https://raw.githubusercontent.com/hawchou1995/HawChow-Mod-Assets/main/images/KillFeed-teammate-mark-ruo-1.2.12.png)

![倒地队友头顶的「菜」标记（实机）](https://raw.githubusercontent.com/hawchou1995/HawChow-Mod-Assets/main/images/KillFeed-teammate-mark-cai-1.2.12.png)

击杀卡片的显示名与距离上限也是**绘制时**按最新配置与名字表重解析的：改了 `.cfg` 或名称 JSON 后，已经在屏幕上的卡片会立刻显示新名字/新距离口径，而这次击杀的归属、等级与时间戳不变。

`TeammateMark/HideDuringEMP` 默认 false，即 EMP 期间继续显示。只有把它改成 true 时才会去反射读取可选模组 `Inas07.EOSExt.EMP` 的 `EOSExt.EMP.Impl.Handlers.EMPPlayerHudHandler.Instance.IsEMPed()`；没有该模组、类型被改名、实例暂时为空或调用抛异常都按「无 EMP」处理（照样显示），并对解析失败做有限退避重试，不会 patch 第三方，也不影响其他 HUD 的 EMP 行为。
## 准星焦点血条

准星所指敌人上方会出现一条细条，显示其名称、剩余/最大生命值与一个目标标签。使用非胶枪装备时，标签保持为已配置的弱点部位与实时 `m_weakspotDamageMulti`，例如 `Shooter[孢子4×][头2×]`。手持 C-Foam 胶枪（`GlueGun`）时，弱点标签会被替换为满足敌人当前 `GlueTolerance` 所需的完整结沫团数，例如 `Shooter[结沫*2]`；每发胶枪弹丸提供 3 个 C-Foam 单位，因此团数向上取整。1,000,000 的免疫哨兵值显示 `[结沫*免疫]`，读不出的容差则省略。放下胶枪后恢复此前的弱点标签。视线移开或有墙遮挡敌人时，血条消失。

紧凑的标题行把名称与当前目标标签放在左侧，血量数字放在右侧，细条紧贴其下。弱点标签保持默认红色的可配置颜色；完整的结沫团数标签使用固定的克制暖琥珀色（`#F2A65A`）而非中性的名称颜色，且没有配置键可以覆盖它。标签会测量自身文字宽度，在窄屏上截断过长的名称，保持在 10%–46% 的屏幕高度带内并避开准星。默认仍是 5 px 细条、12 px 标签与 55% 整体不透明度，且不带面板。填充在 **66%** 以上使用配置的基础 `FillColor`，在 **66%** 及以下切换为 `FillYellowColor`，在 **33%** 及以下切换为 `FillRedColor`。两个切点与各档颜色都可编辑。

结沫团数标签固定显示简体中文（`[结沫*N]` / `[结沫*免疫]`），不随 `General/Language` 设置切换。

在 `HawChow.GTFOKillFeed.cfg` 的 `EnemyBar` 节中配置：`Enabled`、`MaxDistance`（**60 m**）、`Anchor`（`Head` 跟随目标，`Screen` 固定在屏幕）、`HeadOffsetMetres`、`ScreenAnchorFraction`、`VerticalOffset`、`MinScreenFraction`、`MaxScreenFraction`、`BarWidth`、`BarHeight`、`Opacity`、`ShowName`、`ShowWeakspotPart`、`ShowNumbers`、`NumberSize`、`YellowThresholdPercent`、`RedThresholdPercent`、`NameColor`、`WeakspotColor`、`HealthTextColor`、`FillColor`、`FillYellowColor`、`FillRedColor`、`GhostColor`、`TrackColor`、`BorderColor`、`LogFocus`。数值会被钳制，红色阈值上限为黄色阈值。颜色接受 `#RRGGBB` 或 `#RRGGBBAA`；非法字符串回落到默认值，`Opacity` 与配置的 alpha 相乘。**F7** 会把血条与击杀卡片一起隐藏。

关闭 `ShowName` 时，名称、弱点标签与结沫团数标签会一起隐藏。`ShowWeakspotPart` 只控制非胶枪模式的弱点标签；手持胶枪时，若名称显示开启且结沫数据有效，结沫标签不受该设置影响。`ShowNumbers` 独立控制血量数字。

`EnemyBar` 默认值与范围：`Anchor=Head`、`HeadOffsetMetres=0.12`（0–2）、`ScreenAnchorFraction=0.30`（0.05–0.95）、`VerticalOffset=-8`（-400–400）、`MinScreenFraction=0.10`、`MaxScreenFraction=0.46`、`BarWidth=200`（90–420）、`BarHeight=5`（2–16）、`Opacity=0.55`（0.15–1）、`NumberSize=12`（8–22）、`MaxDistance` 5–300 m；`YellowThresholdPercent=66`、`RedThresholdPercent=33`；`NameColor`/`HealthTextColor`/`FillColor` 为 `#E8ECEB`，`WeakspotColor`/`FillRedColor` 为 `#EF4C4C`，`FillYellowColor` 为 `#E6B84C`，`GhostColor` 为 `#A8B0B2`，`TrackColor` 为 `#161A1C`，`BorderColor` 为 `#0B0E10`；`LogFocus=false`。

血条部分按设计只读：没有伤害调用，也从不写入敌人血量或任何游戏状态。渲染器边界只会在敌人头部肢体附近的小窗口内被信任，因为 GTFO 的服装与特效渲染器可能报告高出数米的边界。多人下客机能显示真实剩余血量完全依赖前置依赖 DamageSync（见下一节「联机敌人血条」）；本插件自身不含任何自定义网络通道，血量条的绘制、钳制与每帧刷新逻辑未改。

## 联机敌人血条

### 为什么客机血条恒满

GTFO 的敌人血量是主机权威的。唯一的扣血写入 `Dam_SyncedDamageBase.RegisterDamage` 只被 `Dam_EnemyDamageBase.ProcessReceivedDamage` 调用，而这条链在 `SendLocally()` 里被 `!m_onlyToMaster || SNet.IsMaster` 门控；敌人侧 `m_onlyToMaster` 为真，所以只有主机本机落地写血。客机把命中包转给主机后不写本地血，`Damage.Health` 自出生起就等于 `HealthMax`。原版敌人侧的 `SendSetHealth` 调用点为 0，因此**客机没有任何原版途径读到真实剩余血量**。

### 原理（依赖前置 DamageSync）

本插件自己**不实现任何网络通道**：不发包、不收包、不 patch 任何网络方法。多人下客机血条能显示真值，完全来自前置依赖 **DamageSync 0.0.7**：

- 主机侧 DamageSync 在 `Dam_EnemyDamageBase.ProcessReceivedDamage` 上挂 Postfix，每次伤害结算后调用 `m_setHealthPacket.Send`，把主机的权威血量通过游戏原生的 SetHealth 同步包发给同局客机。
- 客机原生接收路径 `ReceiveSetHealth` 直接 `movss [rbx+20h]` 写入该敌人的 `Health`，**不受 `m_onlyToMaster` 门控**。
- 因此只要主机也装着 DamageSync，客机的 `Damage.Health / HealthMax` 就是主机权威真值，本插件的焦点血条（直接读这两个字段）自动正确，读取路径无需任何改动。

### 主机与客机都装才精确

| 组合 | 客机焦点血条 |
|---|---|
| 主机装了 DamageSync、客机也装了 | 显示主机权威的剩余血量 |
| 主机装了、客机没装 | 客机没有同步接收方，仍读本地 `Health`，恒为满血 |
| **主机没装**、客机装了 | 仍读本地 `Health`，恒为满血。**这是游戏原生限制，不是 KillFeed 的缺陷**；要精确请让主机也安装 DamageSync |

### 主机权威切换后要等下一次受击

房主迁移（主机权威切换）后，新主机只有在**它自己完成一次伤害结算**时才会再发出同步，因此切换瞬间到下一次受击之间客机血条可能停留在旧值；受击一次后即回填。

### 不需要额外设置

KillFeed 没有任何与联机同步相关的配置项。早期**未发布的本地候选**曾有过的 `[Network]` 节从未随任何发布版本提供过，用户 `.cfg` 里不会存在它，无需处理。

## 敌人名称与自定义 MOD 图

已把 Hikaria DamageAnalyzer 现有的 29 个名称组作为默认值内置。自定义名称从 `BepInEx/config/HawChow.KillFeed.EnemyNames.json` 加载，保存后无需重启即可重载。插件 `.cfg` 同样被监视：文件保存稳定后，所有配置项在主线程重载，无需重启。用户覆盖优先；未被规则匹配的 MOD 敌人保留其自身的游戏数据块名称。

## 操作

- **F7**：切换 HUD 显隐（击杀卡片、焦点血条与倒地标记共用）。
- **F9**：预览两张卡片与全部三类命中数字，不触发尸体特效。
- 尸体与伤害数字的控制项仍在 `HawChow.GTFOKillFeed.cfg`；两个按键都能在 `Input` 节改成其他键或设为 `None` 关闭。

## 其余配置项

`BepInEx/config/HawChow.GTFOKillFeed.cfg`：

- `General`：`Enabled`（默认 `true`）总开关；`Language`（默认 `zh`）卡片文字语言。
- `Card`：`ShowEnemyMaxHealth`（`true`）、`ShowDistance`（`true`）。
- `KillCounter`：`Enabled`（true，热重载；只控制新计数显示）。
- `Layout`：`MaxCards`（`3`，1–6）、`IconSize`（`46` px，26–76）、`CardLifetime`（`2.6` 秒）、`FadeSeconds`（`0.45` 秒）、`HorizontalOffset`（`0`）、`VerticalOffset`（`0`）、`MaxDistance`（`999`，超过时显示为 `<数值>m+`）、`BackgroundOpacity`（`0`，0–0.9）。
- `DamageNumbers`：`Enabled`（`true`）、`ApproachEffect`（`false`：轨迹开关，关闭=初版上飘淡出、不放大不汇聚；`true`=朝视点飞行）、`ShowShotgunDamageNumbers`（`true`，仅在总开关开启时生效）、`MergeShotgunPellets`（`true`）、`FontSize`（`27`，16–42）、`FontWeight`（`1`，0–2：同上）、`Lifetime`（`0.72` 秒，0.35–1.2）、`WeakspotColor`（`#E5484D`，`#RRGGBB` 或 `#RRGGBBAA`；只影响弱点数字，普通/护甲数字与击杀卡片琥珀色不变）、`Opacity`（`1.0`，0–1：`1` 与旧版完全一致，`0.5` 半透明，`0` 完全不绘制；乘数与淡出相乘一次并同时作用于正文与 8 道描边，越界夹取、非有限值回落 `1.0`）。
- `DamageFx`：`PrismaticEnabled`（`true`：棱彩外观与棱彩轨迹/放大的总开关，关闭后按普通数字渲染）、`PrismaticThreshold`（`60`，0–1000000，`0` 关闭；与上一项任一关闭即关闭）、`TiersEnabled`（`true`：关闭后同时去掉档位主体色与档位描边，含棱彩开启时那一圈内圈档位色；累计值照常记录；棱彩开启时该数字的箔外观不受本项影响）、`WhiteThreshold`/`SilverThreshold`/`GoldThreshold`/`DiamondThreshold`（默认 `3000`/`5000`/`10000`/`20000`；1–1000000000 夹取，0 或以下回落默认，反序自动抬正）。
- `HitSound`：`Enabled`（`true`）、`Style`（`BF5`，可写 `BF1`/`BF6`）、`Volume`（`0.8`，0–1）、`MuteVanillaIndicator`（`true`，只静音三个 INDICATOR 事件）、`MinIntervalSeconds`（`0.05`，0.02–3.0）。
- `Corpse`：`Enabled`（`true`）、`ExplosionFx`（`false`，只控制爆裂视觉与爆炸音效）、`FadeEffect`（`true`，约 0.5 秒原生分解消融；不可用/被 patch/暂停时自动回落为瞬时隐藏）、`ExplosionDelay`（`0.1` 秒，0.1–1.5）、`TravelScale`（`1.0`，0.5–1.5）、`MinLaunchDistance`（`12` m，**0.5–30**）、`MaxLaunchDistance`（`18` m，0.5–30，不小于最小值；两个值自 1.2.16 起直接反解成初速，配置多少就飞多远，绝对上限 40 m）。
- `TeammateMark`：`Enabled`（`true`）、`Icon`（`Cai`=菜（默认），`Ruo`=鶸，`Si`=死，`Sha`=殺）、`CustomIconPath`（`""`，双轨：根路径=绝对路径，其余=相对 `Assets/icons`；空 = 用随包 PNG）、`RefDistanceMetres`（`8`，1–30）、`MinPixels`（`26`，13–96）、`MaxPixels`（`42`，13–96）、`MaxDistanceMetres`（`60`，1–500）、`HeadClearanceMetres`（`0.05`，0–2）、`HideDuringEMP`（`false`）、`LogMarks`（`false`）。
- `Input`：`ToggleKey`（`F7`）、`PreviewKey`（`F9`）。
- 所有配置项的描述文本（`.cfg` 里的注释）都是中文，写明取值范围、默认值、是否热重载与关闭后的效果；**节名与键名一字未改**，旧 `.cfg` 的既有值全部保留，两个新键（`DamageNumbers/ApproachEffect`、`DamageFx/PrismaticEnabled`）只在该键缺失时才取默认值（`false` / `true`），BepInEx 的 `# Default value:` 机制不变。

- 升级不会覆盖已有配置值。所有 `.cfg` 项（含 `DamageNumbers/Opacity`、`Corpse/ExplosionFx`、`Corpse/FadeEffect`、`DamageNumbers/WeakspotColor` 与 10 个 `TeammateMark` 键）保存后无需重启即可热重载；敌人名称 JSON 独立热重载。持久显示项（颜色、尺寸、位置、可见性与图标）下一帧生效；已经在飞的伤害数字会立刻按新的 `Opacity` 重绘，尸体爆裂排程这类瞬时事件在下一个事件生效；关掉 `Corpse/FadeEffect` 不会打断已借出的原生消融实例，从下一具尸体起生效。
- 版本 `1.2.16` 的程序集版本为 `1.2.16.0`；升级请通过 Thunderstore/r2modman 安装新版本，不要覆盖旧包。已有 `.cfg` 里没有 `DamageNumbers/Opacity` 时按默认 `1.0` 生效（画面与更新前完全一致）；没有 `Corpse/ExplosionFx` 时按默认 `false` 生效，想要爆裂视觉/音效需手动改为 `true`；1.2.16 起 `Corpse/MinLaunchDistance`/`MaxLaunchDistance` 按你写的值生效（0.5–30 m，旧的 12 m 地板已删），如果你之前把最小值设得很小是因为它不生效，现在它会真的生效，想要旧手感请改成 3.5–5 m 左右。没有 1.2.14 的 12 个新键时按上面列出的默认生效（音效开、BF5、音量 0.8、静音原版提示层、棱彩阈值 60、档位开、专注描边开）。热重载前后插件不主动回写文件，**已有键值不会被覆盖**；BepInEx 把旧 `.cfg` 里缺失的新键绑定后保存时，可能追加该键。

## 安装与依赖

- 依赖一：`BepInEx-BepInExPack_GTFO-3.2.3`。
- 依赖二（**必须安装的前置依赖**）：`randomuserhi-DamageSync-0.0.7`——多人游戏下客机敌人血条显示真实剩余血量就靠它。
- 用 r2modman 安装 Thunderstore 包（会自动带上这两项依赖），或把包内文件放入 `BepInEx/plugins`；若手动安装，请自行把 DamageSync 也装进 `BepInEx/plugins`。
- 首次加载时生成 `BepInEx/config/HawChow.GTFOKillFeed.cfg` 与 `BepInEx/config/HawChow.KillFeed.EnemyNames.json`。
- 不依赖、也不改动 CombatPanel；不需要 DamageAnalyzer、MoreCorpseExplosions 或 DamageNumbers。

本地玩家只看到自己的反馈，不增加伤害、不与队友同步、不发送网络破坏包；血条本身不写入敌人血量。插件通过 Harmony 订阅伤害与死亡事件做本地观测；唯一与联机血量有关的机制来自前置依赖 DamageSync，本插件自身不含任何自定义网络通道。

## 测试与验证边界

Survival 的 FX.3 Overload 已在开启尸体淡出的情况下实测：记录到四次淡出开始，玩家未再看到黑屏，也未出现之前的阴影渲染异常栈。同局仍记录到一次原生 `Decay.InitializeInternal` 空引用，之后战斗继续；调用来源和对单具尸体的影响尚未确认。

本局有硬直着色判定记录，但没有开启棱彩或伤害档位。混合霰弹的分段外观、棱彩与档位同时开启时的实机效果，以及长局的淡出池回收仍待复测。下方和上方的实机截图用于展示各自标注的功能，不是本次黑屏对照图。

## 致谢与反馈

- 卡片呈现：Deep Rock Galactic 中的 Battlefield V Kill Feed 风格。
- 原生爆炸参考：Localia / MoreCorpseExplosions。
- 伤害参考与默认名称数据：Hikaria / DamageAnalyzer（Tuesday1028/GTFO_DamageAnalyzer）。
- 浮字实现参考：dakkhuza / DamageNumbers。
- 排版/动效参考：Brotato；设计纪律：Leonxlnx / taste-skill。独立实现，运行时不依赖这些模组。
- 倒地标记「菜 / 鶸」字形：**Yuji Syuku Regular**（Kinuta Font Factory，Copyright 2021 The Yuji Project Authors，SIL Open Font License 1.1）。仅随包分发已栅格化的 128×128 PNG 与该字体的 OFL 许可全文（`BepInEx/plugins/GTFOKillFeed/Assets/icons/OFL-YujiSyuku.txt`），**不分发字体文件本身**；未使用任何第三方游戏纹理。
- 网站与反馈：[qingju.me](https://qingju.me)
- 小黑盒开黑语音频道：[小黑盒开黑频道](https://chat.xiaoheihe.cn/in1wpsjd)
