# KillFeed 1.2.27

GTFO 客户端反馈插件：击杀卡片、头顶图标、伤害数字、命中标记与音效、焦点敌人血条、尸体视觉反馈及倒地队友标记。联机敌人血条依赖 DamageSync；本插件不实现自定义网络同步，也不依赖或改动 CombatPanel。

## 从 1.2.24 升级

- **硬直提示**只显示可确认的四肢、躯体及普通硬直最大阈值。没有有效值时不显示，不标为免疫。真实硬直绿字与霰弹分段规则不变。
- **尸爆音效与刚体**按 WAV 实际帧数写入，回收与复用时先切到兼容的碰撞模式，保留高速击飞。
- **尸体淡出**区分全忙与零实例池；零实例时不重复准备，在原清理截止时再检查一次。没有安全实例仍按时隐藏，不补池、不抢用对象。

**验证边界：** Survival 已在 1.2.27 中记录两次配置热重载及头顶图标、卡片开关切换。此前出现的 `Decay.InitializeInternal` 原生空引用仍未归因；未确认本版能使持续零实例的池恢复。

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
- 六组测试：刚体 48 种切换和 7 项音频回放、策略 Smoke 1,609 条、HUD API 边界重放 20 条、真实 BepInEx 配置 97 条、策略回归 424 条、组合回归 407 条；另有 `KillFeed.IlGuard` 和 9 个 PowerShell IL 检查。它们验证构建和接线，不代替 Unity 实机画面。
- `Thunderstore/` 保存实际发布 ZIP 的文档与图标原字节；根 README 是源码仓说明，不是 ZIP 中 README 的替代副本。

## 反馈

[qingju.me](https://qingju.me) · [小黑盒开黑频道](https://chat.xiaoheihe.cn/in1wpsjd)
