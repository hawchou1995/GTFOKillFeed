# KillFeed 1.2.21

GTFO 客户端反馈插件：击杀卡片、头顶图标、伤害数字、命中标记与音效、焦点敌人血条、尸体视觉反馈及倒地队友标记。联机敌人血条依赖 DamageSync；本插件不实现自定义网络同步，也不依赖或改动 CombatPanel。

## 本版变化

- 玩家原生血条下新增右端对齐的 Minor / Elite / Boss 图标和完整累计击杀数，复用本地致死 credit、Ledger 去重与 `KillPresentation.Tier`。F9 不增加真实计数，离开远征清零。`KillCounter/Enabled` 默认 `true`，独立隐藏不阻止累计。
- 修复 1.2.20 计数不显示：核对 CellUI 手动画布的根层级、裁剪层、完整 viewport 和窗口像素后投影，不移动原生血条，不使用主相机猜位置。目标/画布/窗口像素不一致时明确 held 并输出尺寸。
- 消融在原清理截止前提供最多两次有限预约，仍保留并发 2、第三方接管时让位、不强还 playing/unknown；增加全量汇总、重复日志限流与有效网格检查。
- 2026-11-17 TOA Pure 实测：计数 HUD 放置成功、三张图标资源齐全，正常通关返回大厅，作者反馈计数基本正常。本模组 426 行日志无 Warning/Error/Fatal。

该场景由 DBO 接管消融，KillFeed 正常让位（skipped=1636、failed=0）；没有执行本插件原生消融，因此不据此声称无 DBO 环境的原生 NRE 已根治，也不把初始零值回执当作所有累计数的逐项验证。API 边界绘制重放不是 Unity 实机渲染。

完整功能、截图、配置与历史记录见 [包内介绍](Thunderstore/README.md) 和 [更新日志](CHANGELOG.md)。已有截图不展示新增计数 HUD。

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
- 按 [复现说明](docs/REPRODUCE.md) 提供自己的 `GTFO_DIR`。发布 DLL 采用真实 interop 构建，不以反编译桩替代运行依赖。
- `tests/GTFOKillFeed.Smoke`：1,409 条策略断言；`tests/GTFOKillFeed.HudReplay`：20 条生产 Tick/Draw API 边界重放；`tests/GTFOKillFeed.ConfigSmoke`：97 条真实 BepInEx 配置断言。
- 保留 `tests/KillFeed.Tests` 与 `tests/KillFeed.Policy.Tests` 两组回归。构建、测试与运行边界分别记录，离线通过不等于覆盖所有模组图和多人情境。
- `Thunderstore/` 保存实际发布 ZIP 的文档与图标原字节；根 README 是源码仓说明，不是 ZIP 中 README 的替代副本。

## 反馈

[qingju.me](https://qingju.me) · [小黑盒开黑频道](https://chat.xiaoheihe.cn/in1wpsjd)
