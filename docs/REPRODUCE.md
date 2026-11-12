# 公开复现流程

本仓库有两个快照：

- tag `baseline-1.2.16`：从 1.2.16 原包恢复的**基线源码**，带有 `docs/verify/il-differences.txt`
  和已修复的 `tools/Inspect/Inspect.csproj`。基线门禁只应在这个 tag/commit 上运行。
- tag `v1.2.17`：最终发布源码。不要在最终源码树直接运行 `tools/verify_baseline.py`；
  它期望的是 1.2.16 重建产物与 1.2.16 原 DLL 的对照。

原始 1.2.16 ZIP 不含在本仓库，请使用自己合法持有的原包。下面所有临时输出都放在仓库外的
`$SCRATCH`，不要把 `orig/`、`bin/`、`obj/` 或生成的证据提交回来。

## 0. 前置

```bash
export DOTNET=/path/to/dotnet-6.0.x
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

预期：视觉 364 项通过、ILGuard 0 failures、控制/联合测试 399 项通过（原控制 304 项门禁不变）。

## 3. 最终 DLL 对基线逐方法核对

`verify_integration.py` 需要一个从 `baseline-1.2.16` checkout 构建出的基线 DLL。可在另一个
干净目录执行第 1 节的 `src` 构建，得到 `$BASELINE/src/bin/Release/GTFOKillFeed.dll`，然后：

```bash
python tools/verify_integration.py \
  --baseline "$BASELINE/src/bin/Release/GTFOKillFeed.dll" \
  --final src/bin/Release/GTFOKillFeed.dll \
  --out "$SCRATCH/integration.json"
```

预期：987/1082 方法，947 个共同方法的 Mono.Cecil `ildump` 文本相同（这不是原始 IL 字节
比较，metadata token/offset 未单独证明），156 条预期差异逐条列在
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
