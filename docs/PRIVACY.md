# 发布物隐私门禁

本仓库的发布物（GitHub Release / Thunderstore 使用同一个 ZIP）不得包含贡献者机器的绝对路径、
调试元数据里的 PDB 路径、私人邮箱或凭据。该约束同时覆盖**源码树**与**全部 git 历史**。

## 为什么单纯 grep 文本不够

1.2.17 的早期候选包曾通过旧的包校验：`manifest.json`、README、CHANGELOG 都没有本机路径，
但 `BepInEx/plugins/GTFOKillFeed/GTFOKillFeed.dll` 里带有一条 `RSDS` CodeView 调试记录，
其 NUL 结尾的载荷就是编译时的 PDB 绝对路径（指向构建机的临时目录）。二进制里的这条记录
文本扫描器看不见，却会随 DLL 一起公开。

因此 `tools/privacy_scan.py` 除逐字节扫描外，还解析 PE 调试目录
（`IMAGE_DEBUG_DIRECTORY`，`IMAGE_DEBUG_TYPE_CODEVIEW`），读出 `RSDS` / `NB10` 记录里的
PDB 路径并按“是否绝对路径”判定。`tools/verify_package.py` 与 `tools/package_release.py`
都调用它：前者校验已有包，后者在写 ZIP 之前就拒绝有泄漏的构建产物。

## 扫描范围

- ZIP：**每个条目的解压内容**（文本与 PNG/DLL 等二进制一视同仁），不只扫文件名或文档。
- 目录：仓库 `git ls-files` 列出的全部文件。
- 历史：所有 ref 可达的 blob（按内容去重后逐个扫描）＋ 每个提交的作者/提交者姓名与邮箱、
  提交信息。
- 单文件：用于单独核对某个 DLL。

## 检测项

| 类别 | 说明 |
| --- | --- |
| `drive-path` / `drive-path-utf16` | 盘符绝对路径，ASCII/UTF-8 与 UTF-16LE 两种编码 |
| `users-path` | Unix 用户目录前缀（`/Users`、`/home` 后接分隔符） |
| `codeview-pdb-absolute` | PE CodeView 记录里的绝对 PDB 路径（盘符或 POSIX 根路径） |
| `codeview-unparsed` | 有 CodeView 记录但签名不是 `RSDS`/`NB10`，无法解析，按可疑处理 |
| `local-author` / `private-email` | 本机化作者标识、常见私人邮箱域名 |
| `github-token` / `aws-key` / `private-key` / `secret-assignment` | 凭据与口令赋值 |

二进制里的盘符匹配要求分隔符后有至少 3 个路径字符，且匹配两侧是可打印字节（或 NUL 边界），
以避免压缩数据里的随机三字节组合造成误报。

## Release 构建不产生调试元数据

`src/GTFOKillFeed.csproj` 在 `Release` 下显式关闭调试符号：

```xml
<PropertyGroup Condition="'$(Configuration)' == 'Release'">
  <DebugType>none</DebugType>
  <DebugSymbols>false</DebugSymbols>
</PropertyGroup>
```

因此默认 `dotnet build -c Release` 既不生成 `.pdb`，也不写 CodeView 记录，任何读者的本机
路径都不会进入 DLL。`Debug` 配置不受影响，需要调试时照常生成符号。不要给 Release 加回
`portable`/`embedded`/`pdbonly`，也不要只靠 `PathMap`——`PathMap` 仍需逐构建配置，且一旦
遗漏就会重新泄漏。

## 唯一豁免：配置说明里的示例值

插件配置说明里有一个**示例**路径，用来解释“以盘符或根目录开头的值按本机绝对路径使用”：

```
C:\Pictures\mymark.png
```

它不是任何机器的真实路径，并且**逐字节存在于原始 1.2.16 DLL**（原 DLL 偏移 201674 起的
UTF-16LE 字符串，sha256 `76e33c47…` 的原包），不是本次构建引入的。基线门禁要求 1.2.16
与原 DLL 的字符串/IL 一致，删除它会破坏可复现性，因此按以下**极窄**规则豁免：

- 只豁免“从匹配起点开始的完整路径**恰好等于**该字面量”的情形，ASCII 与 UTF-16LE 各一份；
- 更长路径（例如在该示例后继续拼接目录）仍然报 `drive-path`；
- 其它任何盘符路径一律报错，包括本次泄漏的构建机路径。

该字面量在 `tools/privacy_scan.py` 中以字节片段拼接，源码本身不会被自己的规则命中。

## 命令

```bash
# 候选 ZIP：每个条目（含 DLL 二进制）
python tools/privacy_scan.py --zip dist/KillFeed-1.2.17.zip
# 源码树（git ls-files）
python tools/privacy_scan.py --dir .
# 全历史：所有 blob + 作者/提交信息
python tools/privacy_scan.py --history .
# 单个 DLL：直接读 CodeView
python tools/privacy_scan.py --file src/bin/Release/GTFOKillFeed.dll
```

预期均为 `findings=0`、`SUMMARY: PASS`、退出码 0。负向对照：用修复前的候选 ZIP 运行时必须
`SUMMARY: FAIL`，并指出 `BepInEx/plugins/GTFOKillFeed/GTFOKillFeed.dll` 与
`codeview-pdb-absolute`。

`tools/verify_package.py` 额外输出两条门禁：`privacy.entries`（整包逐条目扫描）与
`privacy.dll-codeview`（直接读 PE 调试目录；无记录时显示 `no CodeView (RSDS/NB10) record`）。

## 边界

- 扫描是模式匹配 + PE 调试目录解析，只能证明**没有命中这些模式**；它不证明包内没有其它
  可识别信息（例如编译时间戳、嵌入的第三方字符串）。
- 历史扫描覆盖 ref 可达的对象；已被删除且不可达的对象不在扫描范围内（本仓库不存在这种情况）。
- 候选 ZIP 的哈希与检测结果记录在发布记录中；本仓库不存放候选包本身。
