> **公开快照说明**：本文属于 `baseline-1.2.16` tag。不要在 1.2.17 源码树直接运行
> `tools/verify_baseline.py`；公开复现步骤见 `docs/REPRODUCE.md`，其中说明如何从原 ZIP
> 在 scratch 提取原 DLL。

# KillFeed 1.2.16 基线恢复记录（阶段 A）

记录：从 `KillFeed-1.2.16.zip` 的 IL2CPP 插件 DLL 恢复可编译项目的流程、逐项对账结果、
**尚未证明的部分**，以及校验脚本本身的自检（负向对照）。
本文所有"通过"均指具体门禁通过；不表示运行期行为等价。

```
$KILLFEED_ZIP = 原包 KillFeed-1.2.16.zip（只读）
$GTFO_DIR     = 解压出的 BepInEx 参考树（只读，含 core/interop/unity-libs）
$WORK         = 本工作树根
```

## 1. 基线与产物

| 工件 | 值 |
|---|---|
| 原包 | `$KILLFEED_ZIP`，1,382,874 B，sha256 `9e1c1871d7612606afa34913b51bd00b3cfdd84daeb7233a3006d0cefb113e38` |
| 原 DLL（包内） | `BepInEx/plugins/GTFOKillFeed/GTFOKillFeed.dll`，1,239,040 B，sha256 `76e33c4742eadf974307aaf46b782ab82605f938c0d0721120656974a4279617` |
| 原 DLL 身份 | `GTFOKillFeed, Version=1.2.16.0, Culture=neutral, PublicKeyToken=null`，MVID `2064d4d3-53a7-4d9a-9ef8-8857d225e733` |
| 调试信息 | 唯一 debug entry 为 `IMAGE_DEBUG_TYPE_REPRO`（size=0）：无 CodeView/PDB，构建路径不可恢复 |
| 内嵌资源 | 27 个（26 WAV + `KillFeed.EnemyNames.defaults.json`），`Implementation=0` 即内嵌 |
| 重建产物 | `src/bin/Release/GTFOKillFeed.dll`（每次构建字节不同：MVID/时间戳/PDB 必然变化） |

已安装副本 `BepInEx/plugins/HawChow-KillFeed/.../GTFOKillFeed.dll`（149,504 B，sha256 `72d57746a3c4c0021b93c2f2c803b11ee05bdfa84ce73b7d22edc374f8e6e720`）
**不是** 1.2.16，仅作参考，不得当基线。

## 2. 工具链

| 用途 | 工具 | 结果 |
|---|---|---|
| SDK | `dotnet` 6.0.428（`$DOTNET`） | OK；离线 ref pack `Microsoft.NETCore.App.Ref/6.0.36` |
| 反编译 | `ICSharpCode.Decompiler` 8.2.0.7535（库） | OK |
| 反编译 CLI | `dotnet tool install ilspycmd` | 失败：SDK 6 报"命令 ilspycmd 使用不受支持的运行程序"，改用库方式 |
| IL/元数据 | `Mono.Cecil` 0.11.4.0（只读引用 `$GTFO_DIR/BepInEx/core/Mono.Cecil.dll`） | OK |
| NuGet | 工作树内 `nuget.config`（仅 nuget.org）+ scratch 内 `DOTNET_CLI_HOME`/`NUGET_PACKAGES` | 全局配置未改 |

工具源码：`tools/Decompile`（项目级反编译）、`tools/Inspect`（元数据+IL 转储）、
`tools/Semantics`（纯函数差分执行器）、`tools/verify_baseline.py`（门禁）、`tools/negative_controls.py`（脚本自检）。

## 3. 恢复流程

```bash
export DOTNET=/path/to/dotnet            # 6.0.4xx
export GTFO_DIR=/path/to/Survival        # 含 BepInEx/core + interop + unity-libs
cd $WORK/orig && unzip -q -o "$KILLFEED_ZIP"
dotnet tools/Decompile/bin/Release/net6.0/Decompile.dll \
  orig/BepInEx/plugins/GTFOKillFeed/GTFOKillFeed.dll src \
  "$GTFO_DIR/BepInEx/core" "$GTFO_DIR/BepInEx/interop" "$GTFO_DIR/BepInEx/unity-libs"
$DOTNET build src/GTFOKillFeed.csproj -c Release -p:GTFO_DIR="$GTFO_DIR" --configfile nuget.config
python tools/verify_baseline.py          # 全部门禁
python tools/negative_controls.py        # 门禁自检（负向对照）
```

## 4. 相对原始反编译输出的改动（21 个 .cs，+158/−263 行）

| 类别 | 数量 | 内容与依据 |
|---|---|---|
| 删除 ILSpy 冗余 `[CompilerGenerated]` | 106 行（7 文件） | ILSpy 为自动属性访问器/后备字段显式写出该特性，编译器同时会自动补一份 → 重编译后出现重复属性行（实测 71 处重复）。删除后属性行与原 DLL 完全一致 |
| `ref` → `out` | 22 处 | 目标参数为 `out`（IL 用 `ldloca`），否则 CS1620 |
| `Enumerator<T>` → `var` | 17 处 | ILSpy 丢失外层类型（`List<T>.Enumerator` / `Dictionary<K,V>.Enumerator`） |
| 显式 `System.Collections.Generic.*` | 13 处 | 元数据字段/属性签名即 System 集合 |
| `Object` → `object` | 3 处 | `UnityEngine.Object` 与 `System.Object` 二义，元数据为 System.Object |
| `Il2CppSystem.Collections.Generic.List<T>` | 2 处 | 赋值右侧为 Il2Cpp 列表 |
| `(SecurityAction)8` | 1 处 | .NET 6 的 `SecurityAction` 无 `SkipVerification`，保留原 IL 数值 |
| csproj 重写 | 1 | `net6.0`；`$(GTFO_DIR)` 引用 core/interop（`Private=false`，排除 `dobby.dll`）；`RootNamespace` 置空以保证资源逻辑名 |

## 5. 门禁（`tools/verify_baseline.py`）

脚本对每个子进程检查返回码与预期输出，任一项不符即 `TOOL-FAIL` 并退出非零。
`--no-build` / `--skip-semantics` 也不是"绿"：会记为 SKIPPED 并使退出码非零。

| 门禁 | 内容 | 当前结果 |
|---|---|---|
| G1 build | `dotnet build` rc=0 且产物存在 | OK |
| G2 tooling | inspect/decompile/semantics 存在（缺失则构建）、3 个引用目录存在；dump 与 ildump 的方法数必须一致（987/987） | OK |
| G3 identity+refs | 名称/版本/模块/culture/token；24 条 AssemblyRef 的名称+版本+公钥令牌逐条相等 | OK（24/24） |
| G4 resources | 27 个资源的名称、字节数、sha256 逐项相等 | OK（27/27） |
| G5 members | 类型（名称+特性+基类+接口标志）、字段（owner+名称+类型+特性+static+literal）、属性（类型+get/set）、方法（owner+签名+static+generic）、程序集/类型/方法级特性逐项比对；`HarmonyPatch` 声明逐条比对 | OK（163/987/1098/98，特性 98/98、57/57、17 条 HarmonyPatch） |
| G6 IL | 逐方法比对；`unmatched` 必须为 0；差异必须与已评审清单 `docs/verify/il-differences.txt` 完全一致（新增/消失/类别变化都失败） | OK（exact 818，different 169，unmatched 0） |
| G7 semantics | 对可安全执行的纯函数做两版程序集差分执行（相同输入→逐位相同结果/异常类型） | OK（255 方法 × 2409 组输入，0 不一致） |
| G8 sources | 重建 DLL 再反编译，119 个源文件必须都存在（差异只报告，不称"相等"） | OK（92 相同 / 27 有差异，+518/−354） |

可证实的编译器生成名差异：仅 1 处，`IconTextureCache.<.ctor>b__3_0` → `b__5_0`。
脚本按去掉序号后的名字唯一配对，再要求 owner、签名、特性、IL 全部一致；配对不唯一或缺失即失败。

## 6. IL 差异清单（169 个方法）

`docs/verify/il-differences.txt` 是**已评审清单**（owner|签名 + 类别），G6 与它逐条比对，
任何新差异都会让门禁失败；`tools/update_il_inventory.py --reason "..."` 才能显式改写。

类别只描述"差在哪"，**不是**等价证明：

| 类别 | 方法数 | 含义 |
|---|---|---|
| layout | 15 | 仅指令偏移/分支标签/局部槽号不同 |
| operand-only | 4 | 操作码序列相同，操作数不同（如 `ldftn` 目标显示差异） |
| branch-comparison | 27 | 比较/分支族不同（`cgt`↔`bgt.s` 等） |
| branch+temp | 39 | 分支 + 临时变量/`dup`/`pop`/`ret` 摆放不同 |
| codegen-only | 2 | 去掉临时/分支/常量/强转脚手架后语义操作完全相同 |
| extra-cast | 14 | 多/少一次 `castclass`（如 `(IFormatProvider)`） |
| string-formatting | 25 | 字符串插值/拼接的代码生成形态不同 |
| enumeration-shape | 9 | `foreach`/枚举器实现形态不同（`constrained.`、`box`） |
| other-shape | 34 | 未归入以上模式的形状差异（逐条列在 `il-diff.md`） |

覆盖情况：169 个差异方法中，**34 个已被 G7 实际执行比对（结果逐位相同）**，
**135 个没有执行证据，标记 UNVERIFIED**（清单里逐条标注）。未验证 ≠ 等价。

## 7. 负向对照（`tools/negative_controls.py`）

每个对照先确认未变异基线无 finding，再变异一处并断言指定门禁报错：

| 对照 | 变异 | 期望/实际 |
|---|---|---|
| 同数量不同签名 | 改一个字段类型（成员数量不变） | G5-fields 报错 ✔ |
| 资源字节变化 | 改一个资源 sha256（大小不变） | G4-resources 报错 ✔ |
| 少一个方法 | 删一个方法（数量变化） | G5-methods 报错 ✔ |
| IL 关键差异 | 注入一条 `throw` 指令 | G6-inventory 报错 ✔ |
| 子进程失败 | `DOTNET` 指向不存在路径 | 退出码非零 + `TOOL-FAIL` ✔ |

## 8. 未恢复 / 不可恢复

1. 原始工程文件划分、命名空间组织、注释、提交历史（无 PDB/路径串）。当前结构是 ILSpy 的"每类型一文件"。
2. 原工程编译设置（`LangVersion`/优化开关/`DebugType`）无直接证据；现取 `net6.0`+C#10+Release（依据：`System.Runtime 6.0.0.0` 引用 + REPRO debug entry）。
3. 符号与行号：重建 PDB 与原包无关；编译器生成的 lambda/闭包编号与原 DLL 不完全一致（已知 1 处）。
4. 原工程 "IL 门禁"（CHANGELOG 提到）源码不在包内，未恢复；当前门禁是本次自建。
5. 游戏本体不在本机，**运行期/实机行为未验证**；编译期引用只用 `$GTFO_DIR` 快照（只读）。

## 9. 验证边界（不得外推）

- G3–G5 证明"元数据面一致"；G6 只证明"差异集合稳定且被归类"；G7 只覆盖能安全执行的纯函数子集。
- 135 个 IL 差异方法没有执行证据；涉及 Unity/Il2Cpp/Harmony 的部分（补丁、HUD、尸体物理、音效）**无法在本机执行验证**。
- G8 只检查再反编译可重现，不构成行为证明。
- 因此：**"结构与资源对等 + 部分纯函数行为一致"≠"插件整体行为等价"**。发布前仍需实机验证。

## 10. 复现与证据

```bash
python tools/verify_baseline.py            # 退出码 0 表示 8 个门禁全过；产物见 docs/verify/
python tools/negative_controls.py          # 退出码 0 表示 5 个负向对照都被正确捕获
python tools/update_il_inventory.py --reason "..."   # 仅在评审差异集变化后使用
```

产物：`docs/verify/verify-report.json`（门禁与统计）、`docs/verify/il-diff.md`（逐方法差异与覆盖标记）、
`docs/verify/il-differences.txt`（已评审差异清单，G6 的比对基准）。
