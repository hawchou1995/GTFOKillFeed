# 公开复现流程

本仓库保留以下历史与当前快照：

- tag `baseline-1.2.16`：从 1.2.16 原包恢复的**基线源码**（反编译基线，一个文件一个类型），
  带有 `docs/verify/il-differences.txt` 和已修复的 `tools/Inspect/Inspect.csproj`。基线门禁只应
  在这个 tag/commit 上运行。
- tag `v1.2.17`：1.2.17 发布源码（反编译基线 + 1.2.17 新增文件）。不要在这个源码树直接运行
  `tools/verify_baseline.py`；它期望的是 1.2.16 重建产物与 1.2.16 原 DLL 的对照。
- tag `v1.2.19`：1.2.19 发布源码 = 作者源码树的一次快照（`src/HawChow.GTFOKillFeed/` 47 个 `.cs`）。
- tag `v1.2.21`（同步至 `main`）= **1.2.21 作者源码树**：同样是「一个文件可声明多个类型」的作者布局，
  `src/HawChow.GTFOKillFeed/` 51 个 `.cs`（1.2.20/1.2.21 新增 `CorpseFadeRetry.cs`、`KillCounterPolicy.cs`、
  `KillCounterAnchorPolicy.cs`、`KillCounterHud.cs`）；反编译基线的 82 个「一个文件一个类型」单元已并入
  这些文件，不再存在。构建产物与作者 1.2.21 发布 DLL **逐字节相同**，见 §0b；本文件 §1–§5 的预期
  数字属于 `baseline-1.2.16` / `v1.2.17` 两个 tag。

## 0b. 1.2.21：构建即发布产物（`v1.2.21` / `main`）

```bash
export DOTNET=/path/to/dotnet            # 6.0.428（见 §0），或改装 6.0.428
export GTFO_DIR=/path/to/Survival        # 只读，含 BepInEx/core 与 BepInEx/interop
"$DOTNET" build src/GTFOKillFeed.csproj -c Release \
  -p:GTFO_DIR="$GTFO_DIR" --configfile nuget.config
```

预期：构建成功、0 个错误。`src/Properties/AssemblyInfo.cs` 已随 1.2.19 移除；程序集版本属性的唯一来源是
`src/HawChow.GTFOKillFeed/PluginInfo.cs` 顶部的 AssemblyVersion/AssemblyFileVersion，二者引用同文件的版本常量；
`src/GTFOKillFeed.csproj` 的 `<Version>1.2.21</Version>` 与其保持一致。产物 `src/bin/Release/GTFOKillFeed.dll`：

| 项 | 值 |
|---|---|
| 字节数 | `1311744` |
| SHA256 | `E337E8AAA0CB7B191DBB8CAF74E4FF35E98FAA423E9C9F84ABD6B07533BDD1BD` |
| FileVersion / ProductVersion | `1.2.21.0` / `1.2.21.0` |

该 SHA256 与作者 1.2.21 发布 DLL 逐字节相同：源码树 → 构建产物同源，中间没有手工替换。本仓 csproj 与
作者 csproj 的差异只涉及路径与资源清单（`RootNamespace` 为空、`GTFO_DIR` 默认空 + `RequireGtfoDir`
早失败、Release 下 `DebugType=none`/`DebugSymbols=false` 显式写死、资源在 `src/` 平铺而不是
`Assets/`+`Assets/audio/`），`Compile` 的字母序与嵌入资源的逻辑名/顺序都与作者树一致，所以编译结果
逐字节相同。

SDK 版本门禁：`global.json` 仍锁 `6.0.428` + `rollForward=latestPatch`，只装了更高特性带 SDK 的机器会
直接报 `Requested SDK version: 6.0.428`。这挡的是「用别的编译器生成不同 IL」，属有意设计；要么装
6.0.428，要么**在仓库外的目录**用绝对路径执行（SDK 解析从当前目录向上查找，读不到仓库内
`global.json`）：

```bash
cd "$SCRATCH" && "$DOTNET" build /abs/path/to/repo/src/GTFOKillFeed.csproj -c Release -p:GTFO_DIR="$GTFO_DIR"
```

仓库内的 `global.json` 不要为此改动。上面表格的 SHA256 就是用「仓库外目录 + SDK 8.0.428」复现的，
与作者 DLL 仍然逐字节相同。

测试（同一份源码树，5 个测试工程 + 1 个 IL 守卫都要跑）：

```bash
"$DOTNET" build tests/KillFeed.Policy.Tests/KillFeed.Policy.Tests.csproj -c Release -p:GTFO_DIR="$GTFO_DIR"
"$DOTNET" build tests/KillFeed.Tests/KillFeed.Tests.csproj -c Release -p:GTFO_DIR="$GTFO_DIR"
"$DOTNET" build tests/KillFeed.IlGuard/KillFeed.IlGuard.csproj -c Release -p:GTFO_DIR="$GTFO_DIR"
"$DOTNET" build tests/GTFOKillFeed.Smoke/GTFOKillFeed.Smoke.csproj -c Release
"$DOTNET" build tests/GTFOKillFeed.ConfigSmoke/GTFOKillFeed.ConfigSmoke.csproj -c Release -p:GTFO_DIR="$GTFO_DIR"
"$DOTNET" build tests/GTFOKillFeed.HudReplay/GTFOKillFeed.HudReplay.csproj -c Release

"$DOTNET" tests/KillFeed.Policy.Tests/bin/Release/net6.0/KillFeed.Policy.Tests.dll       # RESULT pass=424 fail=0
"$DOTNET" tests/KillFeed.Tests/bin/Release/net6.0/KillFeed.Tests.dll                     # SUMMARY pass=407;fail=0
"$DOTNET" tests/KillFeed.IlGuard/bin/Release/net6.0/KillFeed.IlGuard.dll src/bin/Release/GTFOKillFeed.dll   # ILGUARD failures=0
"$DOTNET" tests/GTFOKillFeed.Smoke/bin/Release/net6.0/GTFOKillFeed.Smoke.dll             # PASS: 1409 kill-feed policy assertions
"$DOTNET" tests/GTFOKillFeed.ConfigSmoke/bin/Release/net6.0/GTFOKillFeed.ConfigSmoke.dll # CONFIG_SMOKE=PASS;ASSERTIONS=97
"$DOTNET" tests/GTFOKillFeed.HudReplay/bin/Release/net6.0/GTFOKillFeed.HudReplay.dll     # HUD_REPLAY=PASS;ASSERTIONS=20;PRODUCTION=Tick+Draw
```

`tests/GTFOKillFeed.Smoke`、`tests/GTFOKillFeed.ConfigSmoke`、`tests/GTFOKillFeed.HudReplay` 是 1.2.21
新增的作者验收工程（链接 `src/HawChow.GTFOKillFeed` 的生产源码，不是复制策略）：

- **Smoke（1409 断言）**：纯策略源文件按链接编译，并嵌入与插件完全相同的 `KillFeed.Audio.*.wav`、
  `KillFeed.Icons.hitmark-*.png`、`KillFeed.EnemyNames.defaults.json`，逐字节断言出货音效/标记/名单；
  另有 `KillCounterTests`（档位计数、饱和、布局、`CorpseFadeRetry` 窗口）、`HotReloadTests`、
  `ShotgunVolley*` 等，以及「源码里只 `Counters.Add` 一次」「F9 预览不计入总数」这类文本门禁。
- **ConfigSmoke（97 断言）**：用真实 `BepInEx.Core` 的 `ConfigFile` 跑迁移/热重载（新安装、旧配置、
  只补部分新键、存在但读不出），不是重写一份配置实现；需要 `GTFO_DIR`。
- **HudReplay（20 断言）**：把生产 `KillCounterHud.Tick/Draw` 直接跑在
  `tests/GTFOKillFeed.HudReplay/EngineStubs.cs` 的引擎 API 边界替身上，重放三档击杀、
  四条「合成器不可验证」拒绝分支、目标/窗口分辨率不一致、缺 sprite 等路径，并断言全程没有移动或
  缩放原生血条。

源码门禁说明：`tests/KillFeed.Tests/SourceGateTests.cs` 恢复了作者树 1.2.21 的门禁
（scan-before-first-bind、注册键、生产接线、尸体资源回收不变量、总开关字面量），只做两处布局适配：
`Root()` 锚 `src/GTFOKillFeed.csproj`，`Read("src/HawChow.GTFOKillFeed/X.cs")` 在该目录下取文件。
`gate.doc.*` 仍然要求「每个 `Config.Bind` 键都要在 `docs/FEATURE-SWITCHES.md` 里有反引号词条」，
当前 110 键全覆盖（§9 由 `python tools/feature_switch_inventory.py` 生成）。作者树的
`scripts/Verify.ps1` 不在本仓库内。

**验证边界（不得夸大）**：以上都是构建期/进程内的源码与策略证据，纯 .NET 与文本断言，不加载 Unity、
不启动游戏。`HudReplay` 是「生产 Tick/Draw + 引擎 API 边界替身」，不等于游戏内画面证据；碰撞盒、
相机投影、`Il2Cpp` 互操作、真实 `Decay`/GUI 合成行为都没有在游戏里验证。

原始 1.2.16 ZIP 不含在本仓库，请使用自己合法持有的原包。下面所有临时输出都放在仓库外的
`$SCRATCH`，不要把 `orig/`、`bin/`、`obj/` 或生成的证据提交回来。

历史工具边界：下方 §1–§5 的 `verify_baseline.py`、`verify_integration.py`、`verify_docs.py`、
`package_release.py`、`verify_package.py` 的期望值和布局属于对应历史 tag，不是 1.2.21 的通用发布命令。
其中旧文档/打包工具仍按旧 AssemblyInfo 布局取版本；不要在当前作者源码树运行后把失败当作 1.2.21 构建失败。
当前版使用本节的构建、五组测试与 IL 守卫；发布 ZIP 的原始文档与图标保存在 `Thunderstore/`，
它们与 Release 资产中同名成员逐字节一致，根 README 为源码仓说明。

## 0. 前置

SDK 由仓库根的 `global.json` 锁定为 **6.0.428**（`rollForward = latestPatch`，允许同一
6.0.4xx 特性带的更高补丁，不允许降级到更早的特性带）。用不匹配的 SDK 启动会立即报错，
不会静默换用别的 SDK 生成不同 IL：

```text
Requested SDK version: 6.0.428
A compatible .NET SDK was not found.
```

```bash
export DOTNET=/path/to/dotnet-sdk-6.0.428/dotnet   # 6.0.428 的 dotnet 可执行文件
export GTFO_DIR=/path/to/Survival    # 只读，含 BepInEx/core、BepInEx/interop、BepInEx/unity-libs
export KILLFEED_ZIP=/path/to/KillFeed-1.2.16.zip
export SCRATCH=/path/to/scratch
```

`GTFO_DIR` 也可以用 `-p:GTFO_DIR=...` 传给每个 dotnet build。`nuget.config` 只指向 nuget.org；
如使用离线包缓存，设置 `DOTNET_CLI_HOME`/`NUGET_PACKAGES` 到 `$SCRATCH`。

## 1. 运行 1.2.16 基线（tag `baseline-1.2.16`）

```bash
git checkout baseline-1.2.16
mkdir -p orig
unzip -q -o "$KILLFEED_ZIP" -d orig
# orig/BepInEx/plugins/GTFOKillFeed/GTFOKillFeed.dll 是 verify_baseline.py 的输入

"$DOTNET" build tools/Inspect/Inspect.csproj -c Release \
  -p:GTFO_DIR="$GTFO_DIR" --configfile nuget.config
"$DOTNET" build tools/Decompile/Decompile.csproj -c Release --configfile nuget.config
"$DOTNET" build tools/Semantics/Semantics.csproj -c Release --configfile nuget.config
"$DOTNET" build src/GTFOKillFeed.csproj -c Release \
  -p:GTFO_DIR="$GTFO_DIR" --configfile nuget.config

python tools/verify_baseline.py --out "$SCRATCH/baseline-evidence"
python tools/negative_controls.py
```

预期：`verify_baseline.py` 退出码 0、8 项门禁全过，`G6` 为 `exact=818 different=169
unmatched=0`，并与仓库内 `docs/verify/il-differences.txt` 的 169 条逐条一致；
`negative_controls.py` 退出码 0 且 5/5 通过。`docs/verify/verify-report.json` 不在仓库内，
请让 `--out` 生成到 `$SCRATCH`。

## 2. 构建并测试 1.2.17（tag `v1.2.17`）

在另一份干净 checkout 或同一仓库切换到 tag 后：

```bash
git checkout v1.2.17
"$DOTNET" build src/GTFOKillFeed.csproj -c Release \
  -p:GTFO_DIR="$GTFO_DIR" --configfile nuget.config
"$DOTNET" build tools/Inspect/Inspect.csproj -c Release \
  -p:GTFO_DIR="$GTFO_DIR" --configfile nuget.config
"$DOTNET" build tools/Decompile/Decompile.csproj -c Release --configfile nuget.config
"$DOTNET" build tools/Semantics/Semantics.csproj -c Release --configfile nuget.config
"$DOTNET" build tests/KillFeed.Policy.Tests/KillFeed.Policy.Tests.csproj -c Release \
  -p:GTFO_DIR="$GTFO_DIR"
"$DOTNET" build tests/KillFeed.IlGuard/KillFeed.IlGuard.csproj -c Release \
  -p:GTFO_DIR="$GTFO_DIR"
"$DOTNET" build tests/KillFeed.Tests/KillFeed.Tests.csproj -c Release \
  -p:GTFO_DIR="$GTFO_DIR"

"$DOTNET" run -c Release --no-build --project tests/KillFeed.Policy.Tests/KillFeed.Policy.Tests.csproj
"$DOTNET" tests/KillFeed.IlGuard/bin/Release/net6.0/KillFeed.IlGuard.dll src/bin/Release/GTFOKillFeed.dll
"$DOTNET" run -c Release --no-build --project tests/KillFeed.Tests/KillFeed.Tests.csproj
```

预期：`RESULT pass=424 fail=0`（视觉/呈现策略）、`ILGUARD failures=0`、`SUMMARY pass=405;fail=0`
（控制/联合；原 304 项门禁不变，新增 6 条 `marker.submit.*` 提交门断言）。

## 2b. 文档、版本与 SDK 门禁

```bash
python tools/verify_docs.py
python tools/verify_docs.py --negative
```

`verify_docs.py` 把 README 的默认值直接绑到生产常量（`KillViewPolicy.DefaultCardEnabled/
DefaultHeadEnabled/DefaultOpacity`、`HitSoundPolicy.Default*`、`SoundConfigPlan.*`）与
`Config.Bind` 接线，并要求 README 和 `Plugin.cs` 的 cfg 描述里引用的每个仓库路径真的存在；
同时校验 `global.json` 的 SDK 版本、`manifest.json`、`AssemblyInfo` 与 README/CHANGELOG 的版本一致。

预期：`DOCGATE RESULT gates=16 failures=0`；`--negative` 输出
`SUMMARY: all 7 negative controls raised the expected gate`（改 README 默认值、改坏
README/cfg 里的路径、翻转生产默认值、改 `global.json`、改 manifest 版本，各自触发对应门禁）。

`Release` 构建在 `src/GTFOKillFeed.csproj` 里关闭调试符号（`DebugType=none`、
`DebugSymbols=false`），因此不生成 `.pdb`，DLL 内也没有 CodeView/PDB 路径。不要给 Release
改回 `portable`；`Debug` 配置不受影响。原因与验证方式见 `docs/PRIVACY.md`。

## 3. 最终 DLL 对基线逐方法核对

`verify_integration.py` 需要一个从 `baseline-1.2.16` checkout 构建出的基线 DLL。可在另一个
干净目录执行第 1 节的 `src` 构建，得到 `$BASELINE/src/bin/Release/GTFOKillFeed.dll`，然后：

```bash
python tools/verify_integration.py \
  --baseline "$BASELINE/src/bin/Release/GTFOKillFeed.dll" \
  --final src/bin/Release/GTFOKillFeed.dll \
  --out "$SCRATCH/integration.json"
```

预期：987/1123 方法，947 个共同方法的 Mono.Cecil `ildump` 文本相同（这不是原始 IL 字节
比较，metadata token/offset 未单独证明），197 条预期差异逐条列在
`docs/INTEGRATION-DIFF.txt`，27 个嵌入资源完全相同。任何未列差异、失效允许项或资源变化
都会失败。

## 4. 包检查

```bash
python tools/package_release.py --out "$SCRATCH/KillFeed-1.2.17.zip" \
  --dll src/bin/Release/GTFOKillFeed.dll
python tools/verify_package.py \
  --zip "$SCRATCH/KillFeed-1.2.17.zip" \
  --baseline "$BASELINE/src/bin/Release/GTFOKillFeed.dll" \
  --dll src/bin/Release/GTFOKillFeed.dll
```

预期：17 个条目、12 个 PNG + OFL 与原包逐字节一致、27 个嵌入资源、manifest/程序集 1.2.17。
ZIP 不含源码、PDB、游戏 DLL、`orig/`、缓存或用户日志。

`package_release.py` 在写 ZIP 之前逐条目扫描（文本与二进制，含 PE CodeView 记录），命中本机
绝对路径或绝对 PDB 路径时直接退出、不生成文件；`verify_package.py` 额外输出
`privacy.entries` 与 `privacy.dll-codeview` 两条门禁（无 CodeView 时显示
`no CodeView (RSDS/NB10) record`）。

## 5. 隐私扫描

```bash
python tools/privacy_scan.py --zip "$SCRATCH/KillFeed-1.2.17.zip"
python tools/privacy_scan.py --dir .
python tools/privacy_scan.py --history .
```

预期均 `findings=0`、`SUMMARY: PASS`。负向对照：修复前的 1.2.17 候选 ZIP（DLL 内含 `RSDS`
与构建机绝对路径）必须 `SUMMARY: FAIL`，并指出
`BepInEx/plugins/GTFOKillFeed/GTFOKillFeed.dll` 的 `codeview-pdb-absolute`。
扫描范围、检测项与唯一豁免（原 DLL 就带有的配置示例值）见 `docs/PRIVACY.md`。
