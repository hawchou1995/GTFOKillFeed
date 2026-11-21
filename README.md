# KillFeed 1.2.24

GTFO 客户端反馈插件：击杀卡片、头顶图标、伤害数字、命中标记与音效、焦点敌人血条、尸体视觉反馈及倒地队友标记。联机敌人血条依赖 DamageSync；本插件不实现自定义网络同步，也不依赖或改动 CombatPanel。

## 从 1.2.21 升级

- **尸体淡出黑屏防护。** 原生淡出不再使用带阴影渲染注册、属于已有淡出副本或无法安全判定的输入。找不到安全的渲染器就按原清理时机处理，不为播放特效强行复制对象。`Corpse/FadeEffect` 仍默认开启，可单独关闭。
- **硬直行。** 焦点敌人血条下用浅绿 `#78DCA0` 分列四肢、躯体耐久与普通硬直剩余伤害；无法读取显示 `—`。不需要这行时关闭 `EnemyBar/ShowStaggerLine`，不影响血条或伤害数字。
- **硬直绿字与霰弹。** 只把本机本次确实触发非致死 Light/Heavy 硬直的伤害正文标绿，证据不足保留原色。合并霰弹只标绿触发的部分：例如 `12 + 6` 是同一枪对同一目标的 18 点伤害，不是再加 6 点。弱点、护甲标记与棱彩、档位描边保留。

**实测边界：** Survival 的 FX.3 Overload 开启淡出后未复现黑屏，有 4 条淡出开始回执；同局仍有 1 条 `Decay.InitializeInternal` 原生空引用。调用方和画面影响尚未确定，需复测；这不是“所有黑屏或原生异常已修复”的结论。本局未验证棱彩与档位的叠加画面。

完整功能与历史记录见 [包内介绍](Thunderstore/README.md) 和 [更新日志](CHANGELOG.md)。

## 安装

从 [Thunderstore GTFO 页面](https://thunderstore.io/c/gtfo/p/HawChow/KillFeed/) 或 [GitHub Releases](https://github.com/hawchou1995/GTFOKillFeed/releases) 下载。r2modman 自动安装以下两个前置依赖：

1. `BepInEx-BepInExPack_GTFO-3.2.3`
2. `randomuserhi-DamageSync-0.0.7`

手动安装时将 ZIP 的 `BepInEx` 内容合入对应 profile，并自行安装依赖。已有 `BepInEx/config` 配置不需要删除或覆盖。

## 默认值与控制

- `Card/Enabled` 默认 `true`；`KillIcon/HeadEnabled` 默认 `false`；两者可同时开启。
- `KillIcon/Opacity` 默认 `0.5`。
- `HitSound/Enabled` 父开关默认 `true`；全新配置的 `HitSound/HitEnabled`、`KillEnabled` 默认 `false`，`HitVolume`、`KillVolume` 默认 `0.4`。
- `HitSound/Volume` 默认 `0.8`；`HitSound/MuteVanillaIndicator` 默认 `false`。旧配置迁移保留原有开关和音量，不主动覆盖既有值。
- F7 切换显示，F9 仅预览；保存配置可热重载。全部配置键见 [配置清单](docs/FEATURE-SWITCHES.md)，迁移语义见 [显示开关](docs/KILLVIEW.md)。

## 源码与复现

- 生产源码位于 `src/HawChow.GTFOKillFeed/`，构建入口为 `src/GTFOKillFeed.csproj`。
- `global.json` 保持 SDK **6.0.428**、`rollForward=latestPatch`；不分发 GTFO、BepInEx 或 interop DLL。
- 按 [复现说明](docs/REPRODUCE.md) 提供自己的 `GTFO_DIR`。公开源码用 Survival 真实 interop 重建的 DLL 与实测 DLL 的 SHA256 相同。
- 五组测试：策略 Smoke 1,542 条、HUD API 边界重放 20 条、真实 BepInEx 配置 97 条、策略回归 424 条、组合回归 407 条；另有 `KillFeed.IlGuard` 和 9 个 PowerShell IL 检查。它们验证构建和接线，不代替 Unity 实机画面。
- `Thunderstore/` 保存实际发布 ZIP 的文档与图标原字节；根 README 是源码仓说明，不是 ZIP 中 README 的替代副本。

## 反馈

[qingju.me](https://qingju.me) · [小黑盒开黑频道](https://chat.xiaoheihe.cn/in1wpsjd)
