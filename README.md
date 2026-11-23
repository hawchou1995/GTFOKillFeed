# KillFeed 1.2.35

GTFO 客户端反馈插件：击杀卡片、头顶图标、伤害数字、命中标记与音效、焦点敌人血条、尸体视觉反馈及倒地队友标记。联机敌人血条依赖 DamageSync；本插件不实现自定义网络同步，也不依赖或改动 CombatPanel。

## 从 1.2.34 升级

- **击杀计数**不再在若干次之后停住：去重用的“已记过”名单按伤害组件指针记录，池化敌人离场时没有撤销，
  复用后的同一尸位击杀被当成重复而丢弃。现在 `EnemyAgent.OnDeSpawn` 前缀同样撤销；离场时
  `Damage` 已清空也能用记账时留下的指针释放。
- 被拒绝的重复记账与不在本插件结算栈里的死亡各打一条有界回执（`credit skipped: identity …` /
  `kill=unsettled`），不再静默；回执走独立预算，不占用伤害路由的 12 行说明。
- 计数、卡片、伤害数字、尸体反馈、音效与网络行为均未改动；仍不新增 `ES_Dead` / `Decay` 的 Harmony 补丁。

**验证边界：** 1.2.35 在 Survival 连打两局，第一局 `seen=34`（记账 34 次）、`credit skipped` 为 0；
`kill=unsettled` 只出现在非本插件结算的死亡上。HUD 上的数值仍只能目视确认，日志只记录放置时的初值。

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
