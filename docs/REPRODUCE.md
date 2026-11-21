# 公开复现流程

本仓库保留以下历史与当前快照：

- tag `baseline-1.2.16`：从 1.2.16 原包恢复的**基线源码**（反编译基线，一个文件一个类型），
  带有 `docs/verify/il-differences.txt` 和已修复的 `tools/Inspect/Inspect.csproj`。基线门禁只应
  在这个 tag/commit 上运行。
- tag `v1.2.17`：1.2.17 发布源码（反编译基线 + 1.2.17 新增文件）。不要在这个源码树直接运行
  `tools/verify_baseline.py`；它期望的是 1.2.16 重建产物与 1.2.16 原 DLL 的对照。
- tag `v1.2.19`：1.2.19 发布源码 = 作者源码树的一次快照（`src/HawChow.GTFOKillFeed/` 47 个 `.cs`）。
- `v1.2.21`：51 个生产 `.cs`。该版复现命令和 SHA256 保留在该 tag 的本文件。
- 1.2.24 从 `6f134510dd4739632ae450b72db38b883586a85e`（1.2.21）升级。54 个生产 `.cs` 保留作者代码，按公开布局置于 `src/HawChow.GTFOKillFeed/`；新增 `DamageNumberPaint.cs`、`StaggerHitCapture.cs`、`StaggerHitPolicy.cs`。根目录的 SDK pin、逐文件换行规则和 `src/` 内的嵌入资源布局不变。

## 1.2.24：从公开源码复现实测 DLL

以下是 Windows PowerShell 示例。从仓库根开始，设置 `$env:GTFO_DIR` 指向自己合法持有的 Survival 环境（只读，需有 `BepInEx/core` 和 `BepInEx/interop`）；另准备一个**仓库外**的临时目录。不要安装到游戏、复制 profile 或提交 `bin/`、`obj/`、日志和依赖 DLL。

仓库内仍锁定 SDK `6.0.428` / `rollForward=latestPatch`。本次字节对照使用 SDK **8.0.428**、.NET 6 运行时，在仓库外运行 `dotnet build`，用绝对路径指向公开工程；没有修改 `global.json`。不同 SDK 或其它 interop 集不保证同一哈希。使用以下命令前，设置 `$env:PI_SCRATCH_DIR` 为上述临时目录（测试将临时配置写到这里）。

```powershell
$repo = (Get-Location).Path
Set-Location -LiteralPath $env:PI_SCRATCH_DIR
dotnet --version                    # 本次为 8.0.428
dotnet build "$repo/src/GTFOKillFeed.csproj" -c Release --configfile "$repo/nuget.config"
if ($LASTEXITCODE) { throw 'plugin build failed' }
$dll = "$repo/src/bin/Release/GTFOKillFeed.dll"
$expected = '65D4F79D004BCD61F01CBC65242808DA6B96BFD66CD05E710EECB7DFF28A7C6C'
if ((Get-FileHash -Algorithm SHA256 $dll).Hash -ne $expected) { throw 'DLL hash mismatch' }
```

实测重建结果：**1,333,248 字节**，FileVersion `1.2.24.0`，0 警告、0 错误；SHA256 与 Survival 实测 DLL 一致。公开工程使用作者的编译单元和嵌入资源，不使用反编译桩；`GTFO_DIR` 不提供本机默认路径，引用不会作为插件依赖复制到发布目录。

### 五组测试与编译后 IL 检查

在同一 PowerShell 会话、同一仓库外工作目录继续：

```powershell
foreach ($name in 'GTFOKillFeed.Smoke','GTFOKillFeed.ConfigSmoke','GTFOKillFeed.HudReplay','KillFeed.Policy.Tests','KillFeed.Tests','KillFeed.IlGuard') {
    $project = "$repo/tests/$name"
    dotnet build "$project/$name.csproj" -c Release --configfile "$repo/nuget.config"
    if ($LASTEXITCODE) { throw "build failed: $name" }
    $extra = @(); if ($name -eq 'KillFeed.IlGuard') { $extra = @($dll) }
    dotnet "$project/bin/Release/net6.0/$name.dll" @extra
    if ($LASTEXITCODE) { throw "test failed: $name" }
}
```

| 工程 | 本次结果 | 检查对象 |
|---|---:|---|
| `GTFOKillFeed.Smoke` | 1542 断言通过 | 含淡出输入拒绝、部位分组、硬直归属、合并弹丸分色和绘制顺序 |
| `GTFOKillFeed.ConfigSmoke` | 97 断言通过 | 真实 BepInEx `ConfigFile` 的绑定、迁移与热重载 |
| `GTFOKillFeed.HudReplay` | 20 断言通过 | 生产计数 HUD 的 Tick/Draw；引擎 API 替身，不是 Unity 截图 |
| `KillFeed.Policy.Tests` | 424 通过 / 0 失败 | 视觉策略、投影与避让决策 |
| `KillFeed.Tests` | 407 通过 / 0 失败 | 配置、生产接线、回收与文档门禁 |
| `KillFeed.IlGuard` | `ILGUARD failures=0` | 从实测哈希对应的 DLL 读取编译后接线 |

另外运行 `tools/` 下 9 个只读 PowerShell 检查。5 个还核对真实 interop 的签名或字段；全部使用同一个重建 DLL 和本机提供的 Mono.Cecil，不复制第三方 DLL。

```powershell
$withInterop = 'EnemyBarStaggerIl','StaggerInkIl','HitMarkerIl','HitSoundIl','KillIconIl'
foreach ($name in (@('CorpseFadeIl','DamageNumberOpacityIl','GlueWieldWiring','ShotgunVolleyIl') + $withInterop)) {
    $extra = @(); if ($name -in $withInterop) { $extra = @('-InteropDir', "$env:GTFO_DIR/BepInEx/interop") }
    powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$repo/tools/Test-$name.ps1" -PluginDll $dll -CecilPath "$env:GTFO_DIR/BepInEx/core/Mono.Cecil.dll" @extra
    if ($LASTEXITCODE) { throw "IL check failed: $name" }
}
```

预期：9/9 退出 0。`Test-CorpseFadeIl` 和 `Test-EnemyBarStaggerIl` 检查淡出目标筛选与尸体自有副本校验；`Test-StaggerInkIl` 检查命中回执和实际绘制顺序；`Test-ShotgunVolleyIl` 检查弹丸归属与唯一合并出口。配置清单在仓库根运行 `python tools/feature_switch_inventory.py` 可再生成，当前为 111 个键。

**证据边界：** 这里的离线检查不启动游戏。另有 Survival FX.3 Overload 的实测：淡出开启，黑屏未复现，有 4 条淡出开始回执；另有 1 条 `Decay.InitializeInternal` 原生空引用，调用归属及画面影响待复测。不得据此断言原生异常已消除，或棱彩与档位叠加已在该局逐项验证。

下方 §0–§5 保留旧版复现说明。旧版基线、文档与打包脚本的布局和期望值属于相应历史 tag，不是 1.2.24 的发布门禁。

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
