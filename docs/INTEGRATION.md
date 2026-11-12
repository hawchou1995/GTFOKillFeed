# 1.2.17 集成验证记录

本记录对应公开仓库 tag `v1.2.17`，基线是 tag `baseline-1.2.16`。所有验证都可以从本仓库
的两个 tag 和读者自己的 1.2.16 原 ZIP 复现；不依赖任何本机内部 checkout。

## 基线

- `baseline-1.2.16`：从 1.2.16 IL2CPP DLL 恢复的可构建源码，包含
  `docs/verify/il-differences.txt` 与 `docs/verify/il-diff.md`；只包含构建工具所需的
  `tools/Inspect/Inspect.csproj` 属性位置修复，不改变基线 gate 源码、断言或 169 条差异清单。
- 原 ZIP 和原 DLL 不随仓库发布。`docs/REPRODUCE.md` 说明如何把读者自己的原 ZIP 解到
  未跟踪的 `orig/`（`orig/BepInEx/plugins/GTFOKillFeed/GTFOKillFeed.dll`），再在
  `$SCRATCH` 生成证据。
- 在 tag `baseline-1.2.16` 上运行 `tools/verify_baseline.py --out $SCRATCH/baseline-evidence`
  应通过 8 项门禁：987 方法、G6 `exact=818 different=169 unmatched=0`，27 个资源一致；
  `tools/negative_controls.py` 应通过 5/5。**不要在 `v1.2.17` 源码树直接运行基线 gate。**

## 最终源码与测试

在 tag `v1.2.17` 从源码构建 `src`、`tools/Inspect`、`tools/Decompile`、`tools/Semantics`
和三套测试，按 `docs/REPRODUCE.md` 的命令运行：

- `tests/KillFeed.Policy.Tests`：424 项通过。
- `tests/KillFeed.IlGuard`：0 failures。
- `tests/KillFeed.Tests`：405 项通过（其中原控制门禁 304 项保持通过），覆盖新装、
  旧 Display + 旧 Enabled/Volume 升级、旧键热重载、新键独立调整、重启与冲突优先级，
  以及视觉/音效模块互不覆盖旧键。

## 最终 DLL 对基线

`tools/verify_integration.py` 使用 Mono.Cecil `Inspect` 转储；它比较的是 **ildump 文本**，
不是原始 IL 字节，metadata token/offset 未单独证明相同。基线 987 方法、最终 1123 方法；
共同 966 个方法中 947 个 ildump 文本相同，157 个新增、21 个删除、19 个改变，共 197 条
逐方法差异全部列在 `docs/INTEGRATION-DIFF.txt` 并带原因；未列差异、失效允许项或资源变化
会失败。27 个嵌入资源名称/大小/sha256 完全一致，24 条 AssemblyRef 一致；程序集版本
`1.2.16.0 -> 1.2.17.0`。

## 包

在 tag `v1.2.17` 执行 `tools/package_release.py` 生成 17 个条目的 ZIP；
`tools/verify_package.py` 检查布局、12 个 PNG + OFL 与原包逐字节一致、27 个嵌入资源、
manifest/程序集 1.2.17，并拒绝源码、PDB、游戏 DLL、`orig/`、缓存或日志。
候选 ZIP 的 sha256 和路径记录在发布记录中；GitHub Release 与 Thunderstore 使用同一个
ZIP 文件，不重新打包。

1.2.17 的 `Release` 构建关闭调试符号（`DebugType=none`、`DebugSymbols=false`），发布 DLL
不含 `.pdb` 引用，也没有 CodeView（`RSDS`/`NB10`）记录；`tools/verify_package.py` 直接解析
PE 调试目录并在无记录时显示 `no CodeView (RSDS/NB10) record`。`package_release.py` 与
`verify_package.py` 都会对 ZIP 的**每个条目**（文本与二进制）做本机绝对路径扫描：修复前的
候选包（DLL 内 `RSDS` 指向构建机临时目录）会被这两道门禁明确拒绝并指出
`BepInEx/plugins/GTFOKillFeed/GTFOKillFeed.dll`。扫描范围、检测项与唯一豁免（原 1.2.16 DLL
本就带有的配置示例值）见 `docs/PRIVACY.md`。

## 未实测

游戏内相机投影、`FindObjectsOfType`/collider 对象池生命周期、hitbox 避让实际画面、
GUI 观感、音效听感与尸体子开关效果未在 GTFO 客户端运行；本记录不把这些列为已验证。
