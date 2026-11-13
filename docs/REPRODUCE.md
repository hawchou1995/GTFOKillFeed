# 公开复现流程

本仓库有三个快照：

- tag `baseline-1.2.16`：从 1.2.16 原包恢复的**基线源码**（反编译基线，一个文件一个类型），
  带有 `docs/verify/il-differences.txt` 和已修复的 `tools/Inspect/Inspect.csproj`。基线门禁只应
  在这个 tag/commit 上运行。
- tag `v1.2.17`：1.2.17 发布源码（反编译基线 + 1.2.17 新增文件）。不要在这个源码树直接运行
  `tools/verify_baseline.py`；它期望的是 1.2.16 重建产物与 1.2.16 原 DLL 的对照。
- tag `v1.2.19`：当前发布源码 = **作者源码树**（`src/HawChow.GTFOKillFeed/` 47 个 `.cs`，
  一个文件可声明多个类型；反编译基线的 82 个「一个文件一个类型」单元已并入这些文件，不再存在），
  构建产物与商店包内 DLL **逐字节相同**。参见下面的 §0b；本文件 §1–§5 的预期数字属于
  `baseline-1.2.16` / `v1.2.17` 两个 tag。

## 0b. 1.2.19：构建即发布产物（tag `v1.2.19`）

```bash
export DOTNET=/path/to/dotnet            # 6.0.428（见 §0），或改装 6.0.428
export GTFO_DIR=/path/to/Survival        # 只读，含 BepInEx/core 与 BepInEx/interop
"$DOTNET" build src/GTFOKillFeed.csproj -c Release \
  -p:GTFO_DIR="$GTFO_DIR" --configfile nuget.config
```

预期：构建成功、0 个错误（`src/Properties/AssemblyInfo.cs` 已随 1.2.19 移除——版本属性唯一来源是
`src/HawChow.GTFOKillFeed/PluginInfo.cs`），产物 `src/bin/Release/GTFOKillFeed.dll`：

| 项 | 值 |
|---|---|
| 字节数 | `1294848` |
| SHA256 | `76608B71D072C728C4C21ABFA90BDEB3459F4D71A6C274F31C4A53174995E999` |
| FileVersion / ProductVersion | `1.2.19.0` / `1.2.19.0` |

该 SHA256 与 Thunderstore 包 `KillFeed-1.2.19` 内 `BepInEx/plugins/GTFOKillFeed/GTFOKillFeed.dll`
逐字节相同：源码树 → 构建产物 → 商店包三者同源，中间没有手工替换。

SDK 版本门禁：`global.json` 仍锁 `6.0.428` + `rollForward=latestPatch`，只装了更高特性带 SDK 的
机器会直接报 `Requested SDK version: 6.0.428`。这挡的是「用别的编译器生成不同 IL」，属有意设计；
要么装 6.0.428，要么在一个 `global.json` 允许 `latestMajor` 的目录里执行（SDK 解析从**当前目录**
向上查找），仓库内的 `global.json` 不要为此改动。

测试（同一份源码树）：

```bash
"$DOTNET" build tests/KillFeed.Policy.Tests/KillFeed.Policy.Tests.csproj -c Release -p:GTFO_DIR="$GTFO_DIR"
"$DOTNET" build tests/KillFeed.Tests/KillFeed.Tests.csproj -c Release -p:GTFO_DIR="$GTFO_DIR"
"$DOTNET" build tests/KillFeed.IlGuard/KillFeed.IlGuard.csproj -c Release -p:GTFO_DIR="$GTFO_DIR"
"$DOTNET" tests/KillFeed.Policy.Tests/bin/Release/net6.0/KillFeed.Policy.Tests.dll   # RESULT pass=424 fail=0
"$DOTNET" tests/KillFeed.Tests/bin/Release/net6.0/KillFeed.Tests.dll                 # SUMMARY pass=354;fail=0
```

说明：`tests/KillFeed.Tests/SourceGateTests.cs` 在 1.2.19 只保留「每个 `Config.Bind` 键必须在
`docs/FEATURE-SWITCHES.md` 里有反引号词条」这一条与文件布局无关的门禁（当前 103 键全覆盖）；其余
旧门禁匹配的是反编译文本（`base.Config.Bind(`、`num3 >= item.EndAt`、`GibActivatePatch.cs`），
对作者源码树只会给出假答案，已随合并删除。1.2.19 的源码级门禁在作者树的 `scripts/Verify.ps1`，
不在本仓库内。

原始 1.2.16 ZIP 不含在本仓库，请使用自己合法持有的原包。下面所有临时输出都放在仓库外的
`$SCRATCH`，不要把 `orig/`、`bin/`、`obj/` 或生成的证据提交回来。

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
