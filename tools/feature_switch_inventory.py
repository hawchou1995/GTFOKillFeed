#!/usr/bin/env python3
"""Regenerate the complete config-key table inside docs/FEATURE-SWITCHES.md.

Every `Config.Bind("Section", "Key", ...)` call in src/HawChow.GTFOKillFeed is
collected, so a key that exists in code but is missing from the documentation
fails the build gate in tests/KillFeed.Tests (FeatureDocCoversEveryKey).
"""
import io
import os
import re
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(REPO, "src", "HawChow.GTFOKillFeed")
DOC = os.path.join(REPO, "docs", "FEATURE-SWITCHES.md")
BEGIN = "<!-- BEGIN:generated-keys -->"
END = "<!-- END:generated-keys -->"

LITERAL = re.compile(r'Bind\("([A-Za-z]+)", "([A-Za-z0-9_]+)",\s*(?:defaultValue:\s*)?([^,]+),', re.S)
CONSTANT = re.compile(r'Bind\("([A-Za-z]+)", SoundConfigPlan\.([A-Za-z0-9_]+),\s*([^,]+),', re.S)
DEFAULT_ALIASES = {
    "KillViewPolicy.DefaultCardEnabled": "true",
    "KillViewPolicy.DefaultHeadEnabled": "false",
    "KillViewPolicy.DefaultOpacity": "0.5f",
}

SOUND_KEYS = {
    "KeyMasterEnabled": ("Enabled", "true（1.2.16 旧键，升级后仍作父开关）"),
    "KeyLegacyVolume": ("Volume", "0.8 = 100% 主音量（1.2.16 旧键，升级后仍逐次生效）"),
    "KeyHitEnabled": ("HitEnabled", "迁移：新安装 false / 旧配置 true（跟随旧 Enabled 父开关）"),
    "KeyKillEnabled": ("KillEnabled", "迁移：新安装 false / 旧配置 true（跟随旧 Enabled 父开关）"),
    "KeyHitVolume": ("HitVolume", "迁移：新安装 0.4 / 旧配置 0.8（实际 = 值 × 主音量 ÷ 0.8）"),
    "KeyKillVolume": ("KillVolume", "迁移：新安装 0.4 / 旧配置 0.8（实际 = 值 × 主音量 ÷ 0.8）"),
}


def collect():
    rows = {}
    for name in sorted(os.listdir(SRC)):
        if not name.endswith(".cs"):
            continue
        text = io.open(os.path.join(SRC, name), encoding="utf-8").read()
        for section, key, default in LITERAL.findall(text):
            value = default.strip()
            rows.setdefault((section, key), (DEFAULT_ALIASES.get(value, value), name))
        for section, const, _ in CONSTANT.findall(text):
            key, default = SOUND_KEYS[const]
            rows.setdefault((section, key), (default, name))
    return rows


def main():
    if not os.path.isfile(DOC):
        sys.exit("missing " + DOC)
    rows = collect()
    doc = io.open(DOC, encoding="utf-8").read()
    if BEGIN not in doc or END not in doc:
        sys.exit("generated markers missing in " + DOC)
    lines = ["", "| 键（`Section/Key`） | 默认值 / 迁移规则 | 注册位置 |", "|---|---|---|"]
    for (section, key) in sorted(rows):
        default, name = rows[(section, key)]
        default = default.replace("|", "\\|")
        lines.append("| `%s/%s` | %s | `%s` |" % (section, key, default, name))
    lines.append("")
    out = doc.split(BEGIN)[0] + BEGIN + "\n".join(lines) + END + doc.split(END)[1]
    io.open(DOC, "w", encoding="utf-8", newline="\n").write(out)
    print("keys=" + str(len(rows)))


if __name__ == "__main__":
    main()
