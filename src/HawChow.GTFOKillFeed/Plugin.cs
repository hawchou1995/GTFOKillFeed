using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using System.IO;

namespace HawChow.GTFOKillFeed;

[BepInPlugin(PluginInfo.Guid, PluginInfo.Name, PluginInfo.Version)]
public sealed class Plugin : BasePlugin
{
    internal static Plugin Instance;
    internal static SoundConfigPlan SoundConfig;
    internal static SoundConfigBinding SoundBinding;
    internal static ConfigEntry<bool> Enabled;
    internal static ConfigEntry<bool> ShowEnemyHealth;
    internal static ConfigEntry<bool> ShowDistance;
    internal static ConfigEntry<float> IconSize;
    internal static ConfigEntry<float> CardLifetime;
    internal static ConfigEntry<float> FadeSeconds;
    internal static ConfigEntry<float> HorizontalOffset;
    internal static ConfigEntry<float> VerticalOffset;
    internal static ConfigEntry<float> MaxDistance;
    internal static ConfigEntry<bool> ShowDamageNumbers, CorpseEffects, ShowShotgunDamageNumbers, MergeShotgunPellets, CorpseExplosionFx, DamageNumberApproachEffect;
    internal static ConfigEntry<float> DamageNumberSize, DamageNumberLifetime, DamageNumberOpacity, CorpseExplosionDelay, CorpseTravelScale, BackgroundOpacity, MinLaunchDistance, MaxLaunchDistance;
    internal static ConfigEntry<bool> CorpseFade;
    internal static ConfigEntry<bool> KillCounterEnabled;
    internal static ConfigEntry<bool> CorpseLaunch, CorpseDismember, CorpseChunks, CorpseBurstVisual, CorpseBurstSound;
    internal static ConfigEntry<string> DamageNumberWeakspotColor;
    internal static ConfigEntry<bool> TeammateMarkEnabled, TeammateMarkHideDuringEmp, TeammateMarkLogMarks;
    internal static ConfigEntry<string> TeammateMarkIcon, TeammateMarkCustomIcon;
    internal static ConfigEntry<float> TeammateMarkRefDistance, TeammateMarkMinPixels, TeammateMarkMaxPixels, TeammateMarkMaxDistance, TeammateMarkHeadClearance;
    internal static ConfigEntry<int> MaxCards;
    internal static ConfigEntry<bool> HitSoundEnabled, HitSoundMuteVanillaIndicator, DamageFxTiersEnabled, EnemyBarFocusOutlineEnabled, DamageFxPrismaticEnabled;
    internal static ConfigEntry<string> HitSoundStyle;
    internal static ConfigEntry<float> HitSoundVolume, HitSoundMinIntervalSeconds, DamageFxPrismaticThreshold;
    internal static ConfigEntry<bool> HitSoundHitEnabled, HitSoundKillEnabled;
    internal static ConfigEntry<float> HitSoundHitVolume, HitSoundKillVolume;
    internal static ConfigEntry<float> DamageNumberFontWeight;
    internal static ConfigEntry<long> DamageFxWhiteThreshold, DamageFxSilverThreshold, DamageFxGoldThreshold, DamageFxDiamondThreshold;
    internal static ConfigEntry<float> KillHeavyHealthThreshold, HitMarkerSize;
    internal static ConfigEntry<float> KillMinorHealthThreshold;
    internal static ConfigEntry<bool> HitMarkerEnabled;
    internal static ConfigEntry<bool> HitMarkerHitEnabled, HitMarkerKillEnabled;
    internal static ConfigEntry<string> HitMarkerNormalColor, HitMarkerWeakspotColor, HitMarkerArmorColor, HitMarkerKillColor;
    internal static ConfigEntry<string> KillIconDisplay, KillIconFlyerColor, KillIconArmorFlyerColor;
    internal static ConfigEntry<bool> KillCardEnabled, KillIconHeadEnabled;
    internal static ConfigEntry<float> KillIconHeadOpacity;
    internal static ConfigEntry<string> KillIconViewMode;
    internal static ConfigEntry<float> KillIconLifetime, KillIconGap;
    internal static ConfigEntry<int> KillIconMax;
    internal static ConfigEntry<string> Language;
    internal static ConfigEntry<KeyCode> ToggleKey;
    internal static ConfigEntry<KeyCode> PreviewKey;
    internal static ConfigEntry<bool> EnemyBarEnabled, EnemyBarShowName, EnemyBarShowNumbers, EnemyBarShowWeakspotPart, EnemyBarLogFocus, EnemyBarShowStaggerLine;
    internal static ConfigEntry<float> EnemyBarMaxDistance, EnemyBarHeadOffset, EnemyBarScreenAnchor, EnemyBarVerticalOffset,
        EnemyBarMinScreenFraction, EnemyBarMaxScreenFraction, EnemyBarWidth, EnemyBarHeight, EnemyBarOpacity, EnemyBarNumberSize,
        EnemyBarYellowThresholdPercent, EnemyBarRedThresholdPercent;
    internal static ConfigEntry<string> EnemyBarAnchor, EnemyBarNameColor, EnemyBarWeakspotColor, EnemyBarHealthTextColor,
        EnemyBarFillColor, EnemyBarFillYellowColor, EnemyBarFillRedColor, EnemyBarGhostColor, EnemyBarTrackColor, EnemyBarBorderColor;

    private readonly ConfigHotReloadGate _configReloadGate = new();
    private float _configCheckAt;
    private int _configReloadWarnings;


    public override void Load()
    {
        Instance = this;
        // The sound-config scan and the legacy view-config read MUST run before the first Bind:
        // BepInEx writes a missing key's default straight to disk, which would make a legacy cfg
        // look like one that already chose the new keys.
        KillViewConfigFile viewConfig = KillViewConfigFile.Read(Config.ConfigFilePath);
        SoundConfig = ScanSoundConfig();
        Enabled = Config.Bind("General", "Enabled", KillDefaults.Enabled, "总开关：控制本地玩家的击杀反馈（击杀卡片或头顶击杀图标）。默认 true；关闭后不绘制任何击杀反馈，也不播放击杀音效。保存即热重载。");
        Language = Config.Bind("General", "Language", "zh", "卡片文案语言，默认 zh（中文）。敌人名称仍使用 Hikaria 默认表加自定义覆盖，不受此处影响。保存即热重载。");
        KillCounterEnabled = Config.Bind("KillCounter", "Enabled", true, "在本机玩家血条下方右对齐显示三档击杀图标与本次远征击杀数。默认 true；关闭只隐藏计数，仍记录已确认的本机击杀，离开远征清零。保存即热重载。");
        ShowEnemyHealth = Config.Bind("Card", "ShowEnemyMaxHealth", true, "在击杀卡片上显示敌人最大生命值。默认 true；关闭后卡片只显示距离与名称，不显示血量。保存即热重载。");
        ShowDistance = Config.Bind("Card", "ShowDistance", true, "显示本地玩家在击杀瞬间与敌人的距离。默认 true。保存即热重载。");
        ShowDamageNumbers = Config.Bind("DamageNumbers", "Enabled", true, "显示本地造成的护甲、普通与弱点伤害数字。默认 true；关闭后不绘制任何伤害数字，棱彩与档位效果也一并停止。保存即热重载。");
        ShowShotgunDamageNumbers = Config.Bind("DamageNumbers", "ShowShotgunDamageNumbers", true, "在 DamageNumbers/Enabled 开启时单独控制霰弹枪伤害数字。默认 true；关闭只影响霰弹枪，其余武器照常显示。保存即热重载。");
        MergeShotgunPellets = Config.Bind("DamageNumbers", "MergeShotgunPellets", true, "把同一枪打在同一敌人身上的多颗弹丸合并成一个数字。默认 true；关闭后每颗弹丸单独显示。合并按一次开火计算：弹丸在后续帧陆续结算仍算同一枪（窗口上限 0.5 秒，距最后一颗 0.05 秒静默即提前结算），两次开火绝不合并。保存即热重载。");
        DamageNumberSize = Config.Bind("DamageNumbers", "FontSize", 27f, "伤害数字的基础字号（1080p 像素）。默认 27，夹取 16–42；实际大小还会按命中类型（弱点 ×1.18、护甲 ×0.82）与弹出动画缩放。保存即热重载。");
        DamageNumberLifetime = Config.Bind("DamageNumbers", "Lifetime", FeedbackPolicy.NumberLifetime, "伤害数字的存活时间（秒）。默认 0.72，夹取 0.35–1.2。保存即热重载。");
        DamageNumberApproachEffect = Config.Bind("DamageNumbers", "ApproachEffect", FeedbackPolicy.DefaultApproachEffect, "伤害数字是否朝玩家视点飞行。默认 false：数字在命中点浮现，按原轨迹上飘并淡出（不朝屏幕中心汇聚、不做穿身两段、不随飞行放大，大小只用 FontSize 与弹出缩放），与初版表现逐值一致。改为 true 时普通数字沿随机弹道穿身飞过、棱彩数字朝视点俯冲并放大。该键只影响轨迹：透明度、字号、描边、弱点与护甲装饰、队列上限、淡出曲线都不变。与 DamageFx/PrismaticEnabled、DamageFx/TiersEnabled 互相独立。保存即热重载，屏幕上已有的数字下一帧即按新值重绘。");
        DamageNumberOpacity = Config.Bind("DamageNumbers", "Opacity", FeedbackPolicy.DefaultNumberOpacity, "伤害数字的整体不透明度，范围 0–1。默认 1：与原有淡出相乘恰好一次，画面与旧版逐值一致；0 完全不绘制（正文与全部 8 道描边都不提交）；0.5 为半透明。越界夹取，NaN/Infinity 回落 1。该乘数同时作用于正文与描边；弱点颜色自带的 alpha 仍只参与正文一次。保存即热重载，已在屏幕上的数字立即按新值绘制。");
        CorpseEffects = Config.Bind("Corpse", "Enabled", true, "本地纯视觉尸体反馈：击飞、断肢与延迟血液爆裂，不产生任何额外伤害、不改变伤害数值。默认 true。保存即热重载。");
        CorpseExplosionDelay = Config.Bind("Corpse", "ExplosionDelay", FeedbackPolicy.CorpseBurstDelay, "尸体爆裂特效的延迟（秒）。默认 0.1，夹取 0.1–1.5。只决定特效与音效的时机，不影响击飞、断肢与死亡统计。保存即热重载。");
        CorpseTravelScale = Config.Bind("Corpse", "TravelScale", 1f, "在 Min/MaxLaunchDistance 之上再乘的击飞距离倍数，范围 0.5–1.5，默认 1。自 1.2.16 起该预算会真正反解成水平初速，不再叠在 12 m 的固定地板上，绝对上限仍是 40 m。保存即热重载。");
        MinLaunchDistance = Config.Bind("Corpse", "MinLaunchDistance", FeedbackPolicy.DefaultMinDistance, "标准体型敌人、爆裂伤害为零时的最短击飞预算（米）。默认 12，夹取 0.5–30，并按写入的值直接生效——1.2.15 会把 3.8 这类小值悄悄抬成 12，本版已移除该地板。预算会按阻力与滞空时间反解成水平初速，因此尸体实际落点接近这个距离；只有 40 m 绝对上限与由预算推导的速度上限会缩短它。日志 [CORPSE] 的 travel=/speed=/distance= 可用于对账。保存即热重载。");
        MaxLaunchDistance = Config.Bind("Corpse", "MaxLaunchDistance", FeedbackPolicy.DefaultMaxDistance, "爆裂伤害满值时的最远击飞预算（米）。默认 18，夹取 0.5–30，且永不低于 MinLaunchDistance；NaN 回落 18。爆裂伤害在这两个值之间插值，所以伤害会真正改变击飞距离；施加的初速由该预算推导，不再是 1.2.15 固定的 4.2–8.6 m/s。保存即热重载。");
        IconSize = Config.Bind("Layout", "IconSize", KillDefaults.IconSize, "击杀卡片图标的边长（屏幕像素）。默认 40，夹取 26–76，并随分辨率缩放。保存即热重载。");
        CardLifetime = Config.Bind("Layout", "CardLifetime", KillDefaults.CardLifetime, "每张击杀卡片可见的秒数。默认 3.5。若头顶图标要到更晚才消失，卡片寿命会自动延长到图标到期加淡出。保存即热重载。");
        FadeSeconds = Config.Bind("Layout", "FadeSeconds", KillDefaults.FadeSeconds, "卡片淡出所用的秒数。默认 0.35。保存即热重载。");
        HorizontalOffset = Config.Bind("Layout", "HorizontalOffset", 0f, "相对屏幕中心的水平像素偏移，正值向右。默认 0。保存即热重载。");
        VerticalOffset = Config.Bind("Layout", "VerticalOffset", 0f, "相对默认下方居中锚点的垂直像素偏移，正值向下。默认 0。保存即热重载。");
        MaxDistance = Config.Bind("Layout", "MaxDistance", KillDefaults.MaxDistance, "距离显示上限（米）。超过该值的距离显示为 <数值>m+。默认 999。保存即热重载，已在屏幕上的卡片会立即按新上限重算。");
        MaxCards = Config.Bind("Layout", "MaxCards", KillDefaults.MaxCards, "同时显示的击杀卡片上限，1–6。默认 3；满时移除最旧的一张。保存即热重载。");
        BackgroundOpacity = Config.Bind("Layout", "BackgroundOpacity", FeedbackPolicy.DefaultPanelOpacity, "击杀卡片底板的不透明度：0（默认）= 完全透明，只画图标与文字；0.9 = 接近实心。夹取 0–0.9，文字自身对比度不受影响。保存即热重载。");
        ToggleKey = Config.Bind("Input", "ToggleKey", KeyCode.F7, "显示/隐藏击杀反馈（头顶图标或卡片）、焦点血条与倒地队友标记的按键。默认 F7；写 None 关闭该按键。");
        PreviewKey = Config.Bind("Input", "PreviewKey", KeyCode.F9, "生成预览的按键：三类伤害数字、两类击杀卡片，以及九宫格「档位 × 飞行」头顶图标。默认 F9；写 None 关闭预览。");
        EnemyBarEnabled = Config.Bind("EnemyBar", "Enabled", true, "在准星聚焦的敌人上方显示一条细血条。默认 true。与击杀卡片共用 F7 显隐。保存即热重载。");
        EnemyBarMaxDistance = Config.Bind("EnemyBar", "MaxDistance", BarPolicy.DefaultMaxDistance, "超过该距离（米）的聚焦敌人不再显示血条。默认 60，夹取 5–300。保存即热重载。");
        EnemyBarAnchor = Config.Bind("EnemyBar", "Anchor", "Head", "血条位置。Head（默认）把血条锚在敌人头顶并随目标移动；Screen 把它固定在配置好的屏幕上方区域。非法值回落 Head。保存即热重载。");
        EnemyBarHeadOffset = Config.Bind("EnemyBar", "HeadOffsetMetres", BarPolicy.DefaultHeadOffsetMetres, "Head 模式下在模型顶端之上、内置余量之外再加的米数。默认 0.3，夹取 0–2。头顶击杀图标会再叠加这一值以保持间隙。保存即热重载。");
        EnemyBarScreenAnchor = Config.Bind("EnemyBar", "ScreenAnchorFraction", BarPolicy.DefaultScreenAnchorFraction, "Screen 模式下血条的垂直位置，按屏幕高度从顶部起算的比例。默认 0.18，夹取 0.05–0.95。保存即热重载。");
        EnemyBarVerticalOffset = Config.Bind("EnemyBar", "VerticalOffset", BarPolicy.DefaultVerticalOffset, "血条锚点的像素微调，负值向上。默认 0，夹取 -400–400。保存即热重载。");
        EnemyBarMinScreenFraction = Config.Bind("EnemyBar", "MinScreenFraction", BarPolicy.DefaultMinScreenFraction, "允许区域的顶边，按屏幕高度比例表示。默认 0.02。保存即热重载。");
        EnemyBarMaxScreenFraction = Config.Bind("EnemyBar", "MaxScreenFraction", BarPolicy.DefaultMaxScreenFraction, "允许区域的底边，按屏幕高度比例表示；用于让血条避开准星与画面中心。默认 0.45。保存即热重载。");
        EnemyBarWidth = Config.Bind("EnemyBar", "BarWidth", BarPolicy.DefaultWidth, "血条长度（1080p 像素），随分辨率缩放。默认 220，夹取 90–420。保存即热重载。");
        EnemyBarHeight = Config.Bind("EnemyBar", "BarHeight", BarPolicy.DefaultHeight, "血条厚度（1080p 像素），随分辨率缩放。默认 5，夹取 2–16。保存即热重载。");
        EnemyBarOpacity = Config.Bind("EnemyBar", "Opacity", BarPolicy.DefaultOpacity, "血条整体不透明度，范围 0.15–1。默认 0.85：清晰可读但不挡视野。颜色自带的 alpha 会再乘一次。保存即热重载。");
        EnemyBarShowName = Config.Bind("EnemyBar", "ShowName", true, "在血条旁显示敌人名称，取自与击杀卡片相同的名称表。默认 true。保存即热重载。");
        EnemyBarShowNumbers = Config.Bind("EnemyBar", "ShowNumbers", true, "在名称旁显示剩余/最大血量数值。默认 true。保存即热重载。");
        EnemyBarNumberSize = Config.Bind("EnemyBar", "NumberSize", BarPolicy.DefaultNumberSize, "标签字号（1080p 像素）。默认 12，夹取 8–22。保存即热重载。");
        EnemyBarYellowThresholdPercent = Config.Bind("EnemyBar", "YellowThresholdPercent", BarPolicy.DefaultYellowThresholdPercent, "生命百分比等于或低于该值时使用黄色填充，夹取 0–100。默认 66。保存即热重载。");
        EnemyBarRedThresholdPercent = Config.Bind("EnemyBar", "RedThresholdPercent", BarPolicy.DefaultRedThresholdPercent, "生命百分比等于或低于该值时使用红色填充，夹取 0 到黄色阈值之间。默认 33。保存即热重载。");
        EnemyBarShowWeakspotPart = Config.Bind("EnemyBar", "ShowWeakspotPart", true, "在血条上方显示每个已确认的弱点部位及其当前伤害倍率。默认 true；关闭 ShowName 时这些标签一并隐藏。保存即热重载。");
        EnemyBarShowStaggerLine = Config.Bind("EnemyBar", "ShowStaggerLine", true, "在血条下方用固定浅绿 #78DCA0 显示硬直行，每项只给一个理论最大阈值：[四肢：最大值][躯体：最大值][普通(阈值)：阈值]，并与怪物名称实际第一个字左端对齐（隐藏名称时对齐血条左边）。同组只取最大值：上限 40 与 60 的部位只显示 60，不显示区间、不显示最小值。部位来自伤害肢体自己的解剖枚举 CD_BodypartType（m_destruction.m_bodyPart）：上/下臂、上/下腿 → 四肢，胸/腹/胯 → 躯体；头、Unspecified 与未知/模组值不并入两组，绝不按数值大小、数组下标、怪物名或 ID 猜。四肢/躯体只读该组可确认的未毁可破部位的固定上限 m_healthMax（不读当前耐久 m_health，不写当前/上限）。普通硬直独立取 DamageUntilHitreact 本身（GameAssembly 0x137E570 已核实为同一单位下的绝对伤害预算；不乘 HealthMax、不减累计值）。数值按游戏真实值显示（往返一致，不四舍五入、不截断为固定上限），宽度由现有布局缩放/夹取。没有有效项就整项省略（不写“—”、不伪造 0、不写免疫），三项都没有时整行不绘制、不占位，换行按实际项数计算也不会留下空行。部位项必须有正向证据：只有游戏自带 HitreactOnLimbDestruction 为 Micro/Light/Heavy 时才显示；None、Unspecified、死亡类反应、未知/模组值与读取失败都省略这两项（省略不等于免疫）。切怪立刻按新目标重建、旧目标数字不会串用，目标丢失后该行随目标消失。名称隐藏、Head/Screen、边缘与 F7 均保持。独立开关，默认 true，热重载，不影响名称/弱点/结沫颜色与不透明度。");
        EnemyBarNameColor = Config.Bind("EnemyBar", "NameColor", "#E8ECEB", "敌人名称文字颜色，格式 #RRGGBB 或 #RRGGBBAA。默认 #E8ECEB，非法值回落默认。保存即热重载。");
        EnemyBarWeakspotColor = Config.Bind("EnemyBar", "WeakspotColor", "#EF4C4C", "弱点部位文字颜色，格式 #RRGGBB 或 #RRGGBBAA。默认红色 #EF4C4C，非法值回落默认。保存即热重载。");
        EnemyBarHealthTextColor = Config.Bind("EnemyBar", "HealthTextColor", "#E8ECEB", "血量数值文字颜色，格式 #RRGGBB 或 #RRGGBBAA。默认 #E8ECEB，非法值回落默认。保存即热重载。");
        EnemyBarFillColor = Config.Bind("EnemyBar", "FillColor", "#E8ECEB", "生命填充颜色（高于黄色阈值时），格式 #RRGGBB 或 #RRGGBBAA。默认 #E8ECEB，非法值回落默认。保存即热重载。");
        EnemyBarFillYellowColor = Config.Bind("EnemyBar", "FillYellowColor", "#E6B84C", "黄色生命填充颜色，格式 #RRGGBB 或 #RRGGBBAA。默认 #E6B84C，非法值回落默认。保存即热重载。");
        EnemyBarFillRedColor = Config.Bind("EnemyBar", "FillRedColor", "#EF4C4C", "红色生命填充颜色，格式 #RRGGBB 或 #RRGGBBAA。默认 #EF4C4C，非法值回落默认。保存即热重载。");
        EnemyBarGhostColor = Config.Bind("EnemyBar", "GhostColor", "#A8B0B2", "掉血后残影（上一次血量）的颜色，格式 #RRGGBB 或 #RRGGBBAA。默认 #A8B0B2，非法值回落默认。保存即热重载。");
        EnemyBarTrackColor = Config.Bind("EnemyBar", "TrackColor", "#161A1C", "空血槽（底色）颜色，格式 #RRGGBB 或 #RRGGBBAA。默认 #161A1C，非法值回落默认。保存即热重载。");
        EnemyBarBorderColor = Config.Bind("EnemyBar", "BorderColor", "#0B0E10", "血条边框与文字阴影颜色，格式 #RRGGBB 或 #RRGGBBAA。默认 #0B0E10，非法值回落默认。保存即热重载。");
        EnemyBarLogFocus = Config.Bind("EnemyBar", "LogFocus", false, "记录焦点被接受或拒绝的原因，用于排查改过的敌人为何一直不显示血条。默认 false（不输出）；开启后每个原因有独立输出上限。保存即热重载。");
        CorpseExplosionFx = Config.Bind("Corpse", "ExplosionFx", false, "播放游戏原生的 FXC_FlyerDeath 尸体爆裂特效及其爆裂音效。默认 false（关闭）：击飞、断肢、碎块、隐藏租约回收与死亡统计都不受影响。热关闭时会同时清空待播的合并音效。保存即热重载。");
        CorpseFade = Config.Bind("Corpse", "FadeEffect", true, "尸体被隐藏时用游戏原生 DecayEffect 在约 0.5 秒内溶解消失，而不是瞬间消失。默认 true：只驱动尸体自己身上、而且游戏已经武装好的那份原生 Decay 组件——只调用 Play(0.5f)，绝不调用 Initialize，要求它的 m_targetRenderer 仍然有效，并用 m_playing 确认真的开始。该组件缺失或不安全时，从游戏原生 Decay 池借用一具实例，并按游戏自己的方式武装它（Decay.Initialize 后回读 m_targetRenderer，确认它确实指向这具尸体的 renderer）才 Play；武装抛异常或绑定到别的 renderer 时立刻 Cleanup+Return 恰好一次并记 reason=pool-arm-failed（未武装则记 pool-unverifiable），这些情况最终仍是瞬间隐藏。绝不销毁任何对象。若其它模组 patch 过 Decay.Play、prefab 或池不可用、或游戏暂停，同样回落为瞬间隐藏。每次溶解都可能播放游戏自己的 MEDIUM_CORPSE_DECOMP 尸体分解音——该音属于原生 decay，不受 Corpse/ExplosionFx 控制，这里也绝不使用被关掉的 ENEMYEXPLODE 爆裂音。关闭后从下一具尸体起完全不做溶解。保存即热重载。");
        CorpseLaunch = Config.Bind("Corpse", "Launch", true, "本地尸体被击飞：按 MinLaunchDistance/MaxLaunchDistance 与 TravelScale 施加初速。默认 true，与 1.2.16 行为一致。父开关 Corpse/Enabled 关闭时无效；关闭本项只去掉击飞与速度上限，断肢、碎块、爆裂与隐藏/回收租约照旧。保存即热重载。");
        CorpseDismember = Config.Bind("Corpse", "Dismember", true, "尸体断肢：破坏肢体网格并触发游戏原生 Destruction 事件。默认 true，与 1.2.16 行为一致。关闭后不再调用 BreakLimbs（既不破坏肢体也不触发原生 GibData）；击飞、爆裂、隐藏与回收不受影响。父开关 Corpse/Enabled 关闭时无效。保存即热重载。");
        CorpseChunks = Config.Bind("Corpse", "Chunks", true, "断肢产生的碎块（原生 CD_Gib）是否由本插件捕获并加初速、缩放与限距。默认 true，与 1.2.16 行为一致。关闭后碎块仍由游戏原生逻辑生成与回收，本插件不登记、不施加初速、不缩放、不限距；断肢、击飞、爆裂与队列回收不受影响。父开关 Corpse/Enabled 关闭时无效。保存即热重载。");
        CorpseBurstVisual = Config.Bind("Corpse", "BurstVisual", true, "爆裂视觉：原生 FXC_FlyerDeath 特效，按体型与力度可能有多段。默认 true，与 1.2.16 行为一致。父开关 Corpse/Enabled 与 Corpse/ExplosionFx 任一关闭时无效（ExplosionFx 仍是爆裂的总开关）；关闭本项只停视觉，爆裂音效仍按 BurstSound 播放。保存即热重载。");
        CorpseBurstSound = Config.Bind("Corpse", "BurstSound", true, "爆裂音效：内置爆裂 WAV，读取失败时回落原生 ENEMYEXPLODE。默认 true，与 1.2.16 行为一致。父开关同上；关闭本项只停音效并清空待播的合并音效，视觉不受影响。保存即热重载。");
        DamageNumberFontWeight = Config.Bind("DamageNumbers", "FontWeight", FeedbackPolicy.DefaultNumberWeight, "伤害数字笔画粗细，范围 0–2。默认 1：与已发布画面一致的 Bold 字面。0.5 以下切换为 Normal（细）字面并把描边收到 0.9–1.35 px；1 以上额外把正文在半径 0–0.55 px 的小圆上叠印 4 次，真正加粗笔画（描边到 2.3 px）。越界夹取，NaN/Infinity 回落 1。保存即热重载，屏幕上的数字立即变粗细。");
        DamageNumberWeakspotColor = Config.Bind("DamageNumbers", "WeakspotColor", "#E5484D", "弱点伤害数字的颜色，格式 #RRGGBB 或 #RRGGBBAA。默认克制的深红 #E5484D，非法值回落琥珀色。普通数字保持中性白、护甲数字保持固定灰蓝、击杀卡片标题保持琥珀，均不受此键影响。保存即热重载。");
        TeammateMarkEnabled = Config.Bind("TeammateMark", "Enabled", true, "在每名倒地队友（真人与机器人，绝不含本地玩家）头顶绘制深红色标记。默认 true。保存即热重载。");
        TeammateMarkIcon = Config.Bind("TeammateMark", "Icon", "Cai", "随包标记：Cai = 菜（默认）、Ruo = 鶸、Si = 死、Sha = 殺。也接受汉字本身 cai/ruo/si/sha 的写法（菜/鶸/死/殺）与拼音别名 cai/ruo/si/sha（以及 weak/1/2/3/4）；无法识别时回落 Cai。保存即热重载。");
        TeammateMarkCustomIcon = Config.Bind("TeammateMark", "CustomIconPath", "", "自己的透明 PNG。以盘符或根目录开头的值（例如 C:\\Pictures\\mymark.png）按本机绝对路径使用；其它任何值按相对插件目录的 Assets\\icons 解析（例如 teammate-downed-sha.png 或 mymark.png）。留空或读取失败时保留所选随包图；文件路径或内容变化都会热重载。网络/UNC 路径会被拒绝。");
        TeammateMarkRefDistance = Config.Bind("TeammateMark", "RefDistanceMetres", TeammateMarkPolicy.DefaultRefDistance, "标记达到基准尺寸时对应的距离（米）。默认 8，夹取 1–30。保存即热重载。");
        TeammateMarkMinPixels = Config.Bind("TeammateMark", "MinPixels", TeammateMarkPolicy.DefaultMinPixels, "标记的最小边长（1080p 像素），保证远处的「鶸」也可辨认。默认 26，夹取 13–96。保存即热重载。");
        TeammateMarkMaxPixels = Config.Bind("TeammateMark", "MaxPixels", TeammateMarkPolicy.DefaultMaxPixels, "标记的最大边长（1080p 像素），同时受屏幕高度 5% 的上限约束。默认 42，夹取 13–96。保存即热重载。");
        TeammateMarkMaxDistance = Config.Bind("TeammateMark", "MaxDistanceMetres", TeammateMarkPolicy.DefaultMaxDistance, "超过该距离（米）的队友不再显示标记。默认 60，夹取 1–500。保存即热重载。");
        TeammateMarkHeadClearance = Config.Bind("TeammateMark", "HeadClearanceMetres", TeammateMarkPolicy.DefaultHeadClearance, "沿头部锚点自身朝上方向额外抬起的米数。默认 0.05，夹取 0–2。保存即热重载。");
        TeammateMarkHideDuringEmp = Config.Bind("TeammateMark", "HideDuringEMP", false, "false（默认）表示 EOSExt_EMP 脉冲期间标记照常显示。true 时在 EMPPlayerHudHandler.Instance.IsEMPed() 为真期间隐藏；关闭时完全跳过该反射探测，且探测失败绝不误隐藏。保存即热重载。");
        TeammateMarkLogMarks = Config.Bind("TeammateMark", "LogMarks", false, "记录解析到的头部锚点与名单变化，用于排查改过的玩家模型为何一直不显示标记。默认 false（不输出）；开启后每个原因有独立输出上限。保存即热重载。");
        SoundBinding = SoundConfigBinding.Bind(Config, SoundConfig);
        HitSoundEnabled = SoundBinding.Master;
        HitSoundHitEnabled = SoundBinding.HitEnabled;
        HitSoundKillEnabled = SoundBinding.KillEnabled;
        HitSoundHitVolume = SoundBinding.HitVolume;
        HitSoundKillVolume = SoundBinding.KillVolume;
        HitSoundStyle = Config.Bind("HitSound", "Style", HitSoundPolicy.DefaultStyle, "音效风格：BF1、BF5（默认）或 BF6；无法识别时回落 BF5。素材只对击杀音（light/medium）与 BF1 的命中音提供风格变体，因此 BF1/BF6 复用 BF5 的命中与弱点文件，详见 reports/feature-1.2.12-analysis.md。保存即热重载。");
        HitSoundVolume = SoundBinding.LegacyVolume;
        HitSoundMuteVanillaIndicator = Config.Bind("HitSound", "MuteVanillaIndicator", HitSoundPolicy.DefaultMuteVanillaIndicator, "静音原版命中确认提示层（按当前游戏版本能暴露的范围）：白名单固定三项（BULLETHITENEMYINDICATOR、BULLETHITENEMYLOCALINDICATOR、BULLETHITENEMYWEAKSPOTLOCALINDICATOR），而现售 GTFO 构建里只有两个 *LOCALINDICATOR* 能在 AK.EVENTS 上解析到；BULLETHITENEMYINDICATOR 只存在于 AK.BUSSES（总线表而非事件 id），因此不会被解析，那一层仍会响——本项并不承诺三项全部静音，日志会报未解析数量。子弹打中敌人身体的事件（BULLETHITENEMYLOCAL/SYNC、BULLETHITENEMYWEAKSPOTLOCAL/SYNC）与其它所有 Wwise 事件完全不动。默认 false：原版提示音保持可听，需要时自行打开。与 HitSound/Enabled、Volume、Style 互相独立，在下一个 CellSound.Post 生效。保存即热重载。");
        HitSoundMinIntervalSeconds = Config.Bind("HitSound", "MinIntervalSeconds", (float)HitSoundPolicy.DefaultMinIntervalSeconds, "两个命中音之间（以及独立的两个击杀音之间）的最小间隔秒数。默认 0.05，夹取 0.02–3.0，NaN 回落 0.05。保存即热重载。");
        DamageFxPrismaticEnabled = Config.Bind("DamageFx", "PrismaticEnabled", DamageFxPolicy.DefaultPrismaticEnabled, "是否启用棱彩外观。默认 true；关闭后不再绘制全息箔（8 向粉彩外圈、光晕环、浮雕高光与粉彩主体），也不做棱彩轨迹与飞行放大，该数字按普通数字渲染（仍受 DamageFx/TiersEnabled 与 DamageNumbers/ApproachEffect 控制）。与 PrismaticThreshold 是「与」的关系：阈值为 0 或本项为 false 都会关闭棱彩，任一开启都仍可能因另一项而关闭。三个开关彼此独立。保存即热重载，屏幕上的数字立即按新值重绘。");
        DamageFxPrismaticThreshold = Config.Bind("DamageFx", "PrismaticThreshold", DamageFxPolicy.DefaultPrismaticThreshold, "单次伤害严格大于该值时，这一个伤害数字绘制为棱彩（色相循环）。默认 60：60.0 不触发、60.01 触发。夹取 0–1000000，NaN 回落 60；写 0 等同于关闭棱彩（与 DamageFx/PrismaticEnabled 任一关闭即关闭）。只有 DamageFx/PrismaticEnabled 为 true 时才会真正画出箔材质。保存即热重载。");
        DamageFxTiersEnabled = Config.Bind("DamageFx", "TiersEnabled", DamageFxPolicy.DefaultTiersEnabled, "按本关累计伤害给伤害数字上档位色：白、银、金、钻（取已达成的最高档；阈值是下面四个键）。默认 true；关闭后移除累计伤害档位的描边与主体色（含棱彩开启时内圈的档位色），主体颜色回到普通/弱点/护甲各自的颜色、描边回到深色环。若棱彩开启（DamageFx/PrismaticEnabled，默认 true），该数字的箔外观（双环与光晕、浮雕高光、主体箔色）不受本项影响，本项只决定档位是否上墨。累计值仍照常记录、不会清零，每次换关时重置（暂停、EMP 与改配置都不会重置）。与棱彩、轨迹开关互相独立。保存即热重载。");
        DamageFxWhiteThreshold = Config.Bind("DamageFx", "WhiteThreshold", DamageFxPolicy.WhiteThreshold, "本关累计伤害达到该值时进入白档。默认 3000。写 0 或负数回落默认，夹取 1–1000000000；高于下一档的值会被压回下一档（四档永不倒置，每一档都可达）。保存即热重载。");
        DamageFxSilverThreshold = Config.Bind("DamageFx", "SilverThreshold", DamageFxPolicy.SilverThreshold, "本关累计伤害达到该值时进入银档。默认 5000。夹取与阶梯规则同 WhiteThreshold：不会低于白档，高于金档时被压回金档。保存即热重载。");
        DamageFxGoldThreshold = Config.Bind("DamageFx", "GoldThreshold", DamageFxPolicy.GoldThreshold, "本关累计伤害达到该值时进入金档。默认 10000。夹取与阶梯规则同 WhiteThreshold：不会低于银档，高于钻档时被压回钻档。保存即热重载。");
        DamageFxDiamondThreshold = Config.Bind("DamageFx", "DiamondThreshold", DamageFxPolicy.DiamondThreshold, "本关累计伤害达到该值时进入钻档。默认 20000。夹取规则同 WhiteThreshold，且永不低于金档。保存即热重载。");
        KillHeavyHealthThreshold = Config.Bind("KillIcon", "HeavyHealthThreshold", KillPolicy.DefaultHeavyHealthThreshold, "敌人血量池大于等于该值时使用 BFV 重型（齿轮）击杀图标，同时它就是击杀音效的 BOSS 档线——一个键、一条线，图标与 BOSS 档永远一致。默认 1000，夹取 1–1000000000，NaN 回落 1000。旧 cfg 把该值设在 120 以下时保留原值（ELITE 区间随之空掉）。保存即热重载，在下一次计入击杀时生效。");
        KillMinorHealthThreshold = Config.Bind("KillIcon", "MinorHealthThreshold", KillDefaults.MinorHealthThreshold, "MINOR 与 ELITE 之间的分界：HealthMax 低于该值为 MINOR。默认 120，夹取 1–1000000000；NaN/Infinity 或非正值回落 120。BOSS 线不在这里，而是既有的 KillIcon/HeavyHealthThreshold，所以图标与 BOSS 档不可能互相矛盾。若重型线不高于本行，ELITE 区间为空（低于重型线是 MINOR，达到即 BOSS）——这是保留既有值的确定性结果，不会有任何代码改写它。保存即热重载，在下一次计入击杀时生效。");
        HitMarkerEnabled = Config.Bind("HitMarker", "Enabled", HitMarkerPolicy.DefaultEnabled, "用 BFV 命中/击杀线条替换原版标记，画在游戏上报的世界位置上：普通命中为白色 X、弱点命中为橙色 X、护甲命中为青色四角括号、击杀为更重的红色线条。默认 true；改成 false 完全恢复原版视觉。若替换无法绘制（开关关闭、图标缺失、相机或投影失败、友伤、零伤害命中），该次事件保留原版标记，反馈不会丢失。保存即热重载。");
        HitMarkerHitEnabled = Config.Bind("HitMarker", "HitEnabled", true, "命中标记（普通 X、弱点 X、护甲四角括号）独立开关，默认 true，与 1.2.16 行为一致。父开关 HitMarker/Enabled 关闭时无效；关闭本项仍会显示击杀标记。保存即热重载。");
        HitMarkerKillEnabled = Config.Bind("HitMarker", "KillEnabled", true, "击杀标记（更重的红色线条）独立开关，默认 true，与 1.2.16 行为一致。父开关 HitMarker/Enabled 关闭时无效；关闭本项仍会显示命中标记。两者互不影响。保存即热重载。");
        HitMarkerSize = Config.Bind("HitMarker", "Size", HitMarkerPolicy.DefaultSize, "线条基础尺寸（1080p 像素）：弱点线条放大 18%，护甲括号缩小 8%，击杀线条放大 35%。默认 64，夹取 16–200，NaN 回落 64。保存即热重载。");
        HitMarkerNormalColor = Config.Bind("HitMarker", "ColorNormal", HitMarkerPolicy.DefaultNormalColor, "普通命中 X 的颜色，格式 #RRGGBB 或 #RRGGBBAA。默认 #FFFFFF，非法值回落白色。保存即热重载。");
        HitMarkerWeakspotColor = Config.Bind("HitMarker", "ColorWeakspot", HitMarkerPolicy.DefaultWeakspotColor, "弱点命中 X 的颜色，格式 #RRGGBB 或 #RRGGBBAA。默认 #FF8C00（BFV 中橙），非法值回落默认。保存即热重载。");
        HitMarkerArmorColor = Config.Bind("HitMarker", "ColorArmor", HitMarkerPolicy.DefaultArmorColor, "护甲命中括号的颜色，格式 #RRGGBB 或 #RRGGBBAA。默认 #49ADC9（BFV 辅助青），非法值回落默认。保存即热重载。");
        HitMarkerKillColor = Config.Bind("HitMarker", "ColorKill", HitMarkerPolicy.DefaultKillColor, "击杀线条的颜色，格式 #RRGGBB 或 #RRGGBBAA。默认 #B92828（BFV 重红），使击杀不会被看成又一次命中；非法值回落默认。保存即热重载。");
        KillIconDisplay = Config.Bind("KillIcon", "Display", "", "旧版兼容键。从旧版升级且尚未明确使用 KillIcon/HeadEnabled、Card/Enabled 的用户，本键仍按旧语义生效：Head 或无法识别的值 = 头顶图标开、卡片关；Card/Cards = 头顶图标关、卡片开。修改后保存即热重载，下一次 Reload 生效；ViewMode 会记录 Legacy:Head/Legacy:Card。显式设置两个新开关后本键不再生效（ViewMode=Explicit），程序不改写它的既有值。");
        KillCardEnabled = Config.Bind("Card", "Enabled", KillViewPolicy.DefaultCardEnabled, "击杀卡片开关，默认 true。与头顶图标完全独立：两者可同时开启并同时显示，也可只开其一。F7 与 General/Enabled 仍然同时管住两种呈现。保存即热重载。");
        KillIconHeadEnabled = Config.Bind("KillIcon", "HeadEnabled", KillViewPolicy.DefaultHeadEnabled, "头顶击杀图标开关，默认 false。与击杀卡片完全独立：两者可同时开启并同时显示。图标在击杀瞬间冻结世界锚点，画在敌人头顶；与当前屏幕投影重叠的可见怪物 hitbox 会把图标整体上移，屏幕顶端放不下时该项安全隐藏（不会强行开启卡片）。F7 与 General/Enabled 仍然同时管住两种呈现。保存即热重载。");
        KillIconHeadOpacity = Config.Bind("KillIcon", "Opacity", KillViewPolicy.DefaultOpacity, "头顶击杀图标与飞行徽记的不透明度，范围 0–1。默认 0.5；越界夹取，NaN/Infinity 回退 0.5。该值只与生命周期淡入淡出相乘一次。保存即热重载。");
        KillIconViewMode = Config.Bind("KillIcon", "ViewMode", "", "内部迁移状态，请勿手动修改：Legacy:Head/Legacy:Card 表示仍由旧 Display 驱动；Explicit 表示使用两个独立新开关。默认空；新装、旧版升级和热重载由插件维护。");
        KillIconLifetime = Config.Bind("KillIcon", "HeadLifetime", KillIconPolicy.DefaultLifetimeSeconds, "头顶击杀图标可见的秒数，夹取 0.4–6。默认 2.2。图标锚点在击杀瞬间冻结，寿命只控制淡入淡出。保存即热重载。");
        KillIconMax = Config.Bind("KillIcon", "HeadMaxIcons", KillIconPolicy.DefaultMaxIcons, "同时存在的头顶击杀图标上限，夹取 1–12。默认 6；满时先丢弃最旧的。保存即热重载。");
        KillIconGap = Config.Bind("KillIcon", "HeadGapMetres", KillIconPolicy.DefaultGapMetres, "敌人头部点与击杀图标之间的额外世界余量（米），夹取 0–2。默认 0.30。EnemyBar/Anchor 为 Head 时血条自身的头部偏移会再叠加，因此图标始终与血条保持间隙。保存即热重载。");
        KillIconFlyerColor = Config.Bind("KillIcon", "FlyerColor", KillIconPolicy.DefaultWingsColor, "飞行敌人击杀图标下方翼形 chevron 的颜色，格式 #RRGGBB 或 #RRGGBBAA。默认 #8FD3E8；形状本身已能区分飞行，此颜色只是重复编码，非法值回落默认。保存即热重载。");
        KillIconArmorFlyerColor = Config.Bind("KillIcon", "ArmorFlyerColor", KillIconPolicy.DefaultArmorPlateColor, "装甲飞行击杀图标下方装甲板徽记的颜色，格式 #RRGGBB 或 #RRGGBBAA。默认 #E6AB57，非法值回落默认。保存即热重载。");
        ApplyKillViewSwitches(viewConfig);
        EnemyBarFocusOutlineEnabled = Config.Bind("EnemyBar", "FocusOutlineEnabled", FocusOutlinePolicy.DefaultEnabled, "为聚焦敌人血条绘制描边：准星落在敌人背部朝外的半球时为橙色，落在弱点部位时为红色，两者同时成立时为棱彩。默认 true；关闭后不新增任何绘制。保存即热重载。");
        InitializeConfigHotReload();
        // Explicit registration receipts (1.2.16). Il2CppInterop logs "Assembly GTFOKillFeed.dll is not
        // registered in il2cpp" from Il2CppClassPointerStore<T>..cctor for ANY type in our own assembly
        // before injection; that error path returns IntPtr.Zero and is benign, but a real-machine log could
        // not tell it apart from a missing registration. Each injection now proves itself, one line per type.
        RegisterComponent<KillHud>(Log);
        RegisterComponent<CorpseVisualLease>(Log);
        RegisterComponent<HitMarkerHud>(Log);
        // One summary receipt for the whole plugin: three booleans a real-machine log can be checked against,
        // including the benign "Assembly GTFOKillFeed.dll is not registered in il2cpp" Il2CppInterop error that
        // the class-pointer store logs before the injection happens.
        Log.LogInfo($"[INJECT] class-injection summary: KillHud={ClassInjector.IsTypeRegisteredInIl2Cpp(typeof(KillHud))}"
            + $";CorpseVisualLease={ClassInjector.IsTypeRegisteredInIl2Cpp(typeof(CorpseVisualLease))}"
            + $";HitMarkerHud={ClassInjector.IsTypeRegisteredInIl2Cpp(typeof(HitMarkerHud))}"
            + $";assembly={typeof(Plugin).Assembly.GetName().Name};note=il2cpp-assembly-error-is-benign");
        EnemyNames.Initialize();
        KillHud.Instance = AddComponent<KillHud>();
        HitMarkerHud.Instance = AddComponent<HitMarkerHud>();
        var harmony = new Harmony(PluginInfo.Guid);
        harmony.PatchAll(typeof(Plugin).Assembly);
        HitMarkerHud.ApplyPatches(harmony, Log);

        KillTracker.Reset("plugin-load");
        EnemyBar.Reset();
        Log.LogInfo($"{PluginInfo.Name} {PluginInfo.Version} loaded; local kills only; lethal weakspot is read from eLimbDamageType.Weakspot; damage-number audio=off, opacity={DamageNumberOpacity.Value:0.##}; shotgun damage numbers={ShowShotgunDamageNumbers.Value}, pellet merge={MergeShotgunPellets.Value}; weakspot number colour={DamageNumberWeakspotColor.Value}; focused-enemy health bar={EnemyBarEnabled.Value} (name={EnemyBarShowName.Value}, numbers={EnemyBarShowNumbers.Value}, anchor={EnemyBarAnchor.Value}, opacity={EnemyBarOpacity.Value:0.##}); downed teammate mark={TeammateMarkEnabled.Value} (icon={TeammateMarkIcon.Value}, custom={(string.IsNullOrWhiteSpace(TeammateMarkCustomIcon.Value) ? "<bundled>" : TeammateMarkCustomIcon.Value)}, hideDuringEMP={TeammateMarkHideDuringEmp.Value}); language={Language.Value}; preview={PreviewKey.Value}.");
        KillTracker.LogPatchReceipts(harmony, Log);
        Log.LogInfo($"[HITSOUND] enabled={HitSoundEnabled.Value}; style={HitSoundStyle.Value} (resolved {HitSoundPolicy.ParseStyle(HitSoundStyle.Value)}); volume={HitSoundVolume.Value:0.##}; muteVanillaIndicator={HitSoundMuteVanillaIndicator.Value}; minInterval={HitSoundMinIntervalSeconds.Value:0.###}s; embedded cues={HitSoundPolicy.ShippedCueNames}; prismaticThreshold={DamageFxPrismaticThreshold.Value:0.##}; tiers={DamageFxTiersEnabled.Value}; focusOutline={EnemyBarFocusOutlineEnabled.Value}");
        LogDamageSwitchReceipts(true);
        LogSwitchReceipts();
        // Open the six waveOut voices now: doing it lazily would put six device opens inside the first
        // damage callback. Warmup is a no-op when the cues are switched off at load - and that is the one
        // case where the first cue AFTER a hot enable does pay for the device open (allocations plus six
        // waveOutOpen calls) inside the damage callback. This is stated rather than glossed over: the
        // steady state is allocation-free, the first hot-enabled cue is not.
        if (SoundNow().AnyPlays) CueWavePlayer.Warmup();
    }

    // BepInEx 6 calls BasePlugin.Unload when the plugin is unloaded/reloaded. Without this override the six
    // waveOut handles, their prepared WAVEHDRs and the six unmanaged sample buffers were never released
    // (review A2). Idempotent: everything is freed and the state reset, and base.Unload() keeps the
    // framework's own return value semantics.
    // Review R7: the corpse queue is reset here too. A hidden/kinematic pooled ragdoll (and its borrowed model
    // lease) otherwise had no recovery path at all after the plugin went away.
    public override bool Unload()
    {
        try { HitSound.Shutdown(); }
        catch (Exception error) { Log?.LogWarning("[HITSOUND] shutdown failed: " + error.GetType().Name); }
        try { KillTracker.Reset("plugin-unload"); }
        catch (Exception error) { Log?.LogWarning("[CORPSE] unload reset failed: " + error.GetType().Name); }
        return base.Unload();
    }
    // One receipt per injected MonoBehaviour: ClassInjector.IsTypeRegisteredInIl2Cpp answers the question
    // the real-machine log could not answer ("is the assembly registered?"), and a failure is loud instead
    // of surfacing later as a missing HUD.
    private static void RegisterComponent<T>(ManualLogSource log) where T : MonoBehaviour
    {
        ClassInjector.RegisterTypeInIl2Cpp<T>();
        bool registered = ClassInjector.IsTypeRegisteredInIl2Cpp(typeof(T));
        log.LogInfo($"[INJECT] type={typeof(T).Name};registered={registered};assembly={typeof(Plugin).Assembly.GetName().Name};anchor=il2cpp-class-injection");
        if (!registered) log.LogWarning($"[INJECT] type={typeof(T).Name} did not register: behaviour attached to it cannot work.");
    }
    // Bounded receipts for the three independent damage-number switches (1.2.16): one line per switch at
    // load and one line per ACTUAL change afterwards, so a real-machine log can prove that saving the cfg
    // repainted numbers already on screen without flooding the log with a line every reload.
    private bool _loggedTiers = DamageFxPolicy.DefaultTiersEnabled;
    private bool _loggedPrismatic = DamageFxPolicy.DefaultPrismaticEnabled;
    private bool _loggedApproach = FeedbackPolicy.DefaultApproachEffect;
    private void LogDamageSwitchReceipts(bool force)
    {
        string when = force ? "load" : "applied";
        bool tiers = DamageFxTiersEnabled == null || DamageFxTiersEnabled.Value;
        bool prismatic = DamageFxPrismaticEnabled == null || DamageFxPrismaticEnabled.Value;
        bool approach = DamageNumberApproachEffect != null && DamageNumberApproachEffect.Value;
        if (force || tiers != _loggedTiers)
        {
            Log.LogInfo($"[DAMAGEFX] tiers={tiers};note={(tiers ? "tier ink on body and outline" : "tier ink off: body and outline keep the plain colours")};source={when}");
        }
        if (force || prismatic != _loggedPrismatic)
        {
            Log.LogInfo($"[DAMAGEFX] prismatic={prismatic};threshold={DamageFxPrismaticThreshold.Value:0.##};note={(prismatic ? "foil look and prismatic flight allowed above the threshold" : "foil off: every number renders as a plain number")};source={when}");
        }
        if (force || approach != _loggedApproach)
        {
            Log.LogInfo($"[DAMAGEFX] approach={approach};note={(approach ? "numbers fly at the viewer" : "numbers drift up from the hit point (initial trajectory)")};source={when}");
        }
        _loggedTiers = tiers;
        _loggedPrismatic = prismatic;
        _loggedApproach = approach;
    }

    private SoundConfigPlan ScanSoundConfig()
    {
        string text = null;
        bool existed = false;
        try
        {
            var file = new FileInfo(Config.ConfigFilePath);
            existed = file.Exists;
            if (existed) text = File.ReadAllText(Config.ConfigFilePath);
        }
        catch (Exception error)
        {
            Log?.LogWarning("[CONFIG] sound config scan failed (" + error.GetType().Name + "); treating as legacy so the previous behaviour and volumes are kept");
            return SoundConfigPlan.FromUnreadableFile();
        }
        return SoundConfigPlan.FromText(text, existed);
    }

    // Single source of truth for "does this cue play, and how loud": the legacy HitSound/Enabled
    // and Volume keys keep working as the master switch and master gain, the new per-channel keys
    // sit under them, and a cfg that never saw the new keys keeps its exact 1.2.16 output.
    internal static SoundSettings SoundNow()
    {
        if (SoundBinding != null) return SoundBinding.Current();
        if (SoundConfig != null) return SoundConfig.Defaults();
        return SoundSettings.Resolve(true, HitSoundPolicy.DefaultHitEnabled, HitSoundPolicy.DefaultKillEnabled,
            HitSoundPolicy.DefaultHitVolume, HitSoundPolicy.DefaultKillVolume,
            SoundSettings.ReferenceMasterVolume, SoundSettings.ReferenceMasterVolume);
    }

    internal static bool MarkerChannel(HitMarkerKind kind)
    {
        bool enabled = HitMarkerEnabled == null || HitMarkerEnabled.Value;
        bool channel = kind == HitMarkerKind.Kill
            ? HitMarkerKillEnabled == null || HitMarkerKillEnabled.Value
            : HitMarkerHitEnabled == null || HitMarkerHitEnabled.Value;
        return HitMarkerPolicy.ChannelEnabled(enabled, channel);
    }

    // One bounded receipt per independent channel at load, so a real-machine log can prove which
    // switches the running build actually resolved instead of guessing from the cfg text.
    private void LogSwitchReceipts()
    {
        if (SoundConfig != null) Log?.LogInfo("[HITSOUND] config migration: " + SoundConfig.Describe());
        Log?.LogInfo("[HITSOUND] effective: " + SoundNow().Describe());
        Log?.LogInfo("[HITMARKER] hit=" + MarkerChannel(HitMarkerKind.Normal) + ";kill=" + MarkerChannel(HitMarkerKind.Kill));
        Log?.LogInfo("[CORPSE] switches: " + CorpseFeedback.Current().Describe());
    }

    // Legacy KillIcon/Display compatibility: an untouched 1.2.16 cfg keeps driving the two
    // independent switches; once the user sets them explicitly the legacy key is ignored and the
    // migration token records which regime is active. Nothing here rewrites the user's Display value.
    private void ApplyKillViewSwitches(KillViewConfigFile viewConfig)
    {
        KillViewSwitches viewSwitches = KillViewPolicy.ResolveSwitches(viewConfig);
        KillViewConfigApply.Apply(viewSwitches,
            value => KillIconHeadEnabled.Value = value,
            value => KillCardEnabled.Value = value,
            value => KillIconViewMode.Value = value);
        Log.LogInfo($"[KILLVIEW] head={KillIconHeadEnabled.Value};card={KillCardEnabled.Value};mode={viewSwitches.Mode};modeToken={viewSwitches.ModeToken};headSource={viewSwitches.HeadOrigin};cardSource={viewSwitches.CardOrigin};legacyKey={viewSwitches.LegacyKeyPresent};legacyDisplay={KillViewPolicy.ParseLegacyDisplay(viewConfig.Raw("KillIcon", "Display"))};note=legacy-display-migration");
    }

    private void InitializeConfigHotReload()
    {
        try
        {
            var file = new FileInfo(Config.ConfigFilePath);
            if (file.Exists) _configReloadGate.Seed(file.LastWriteTimeUtc, file.Length);
        }
        catch (Exception error)
        {
            if (_configReloadWarnings++ < 4) Log.LogWarning("[CONFIG] hot-reload initialization failed: " + error.Message);
        }
    }

    internal static void TickConfigHotReload(float now)
        => Instance?.PollConfigHotReload(now);

    private void PollConfigHotReload(float now)
    {
        if (!KillPolicy.IsFinite(now) || now < _configCheckAt) return;
        _configCheckAt = now + 0.25f;
        string path = Config.ConfigFilePath;
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists) return;
            DateTime stamp = file.LastWriteTimeUtc;
            long length = file.Length;
            if (!_configReloadGate.ShouldReload(stamp, length, now)) return;

            bool saveOnConfigSet = Config.SaveOnConfigSet;
            try
            {
                Config.SaveOnConfigSet = false;
                Config.Reload();
                ApplyKillViewSwitches(KillViewConfigFile.Read(path));
                var after = new FileInfo(path);
                if (!after.Exists || after.LastWriteTimeUtc != stamp || after.Length != length) return;
                _configReloadGate.MarkApplied(stamp, length);
                Log.LogInfo("[CONFIG] reloaded HawChow.GTFOKillFeed.cfg from disk.");
                LogDamageSwitchReceipts(false);
            }
            finally { Config.SaveOnConfigSet = saveOnConfigSet; }
        }
        catch (Exception error)
        {
            _configReloadGate.MarkFailed(now);
            if (_configReloadWarnings++ < 4) Log.LogWarning("[CONFIG] hot reload failed: " + error.Message);
        }
    }

}
