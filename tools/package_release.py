#!/usr/bin/env python3
"""Build the Thunderstore/GitHub release ZIP from repository files only."""
import argparse
import hashlib
import io
import json
import os
import re
import sys
import zipfile
import privacy_scan

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DLL = os.path.join(ROOT, "src", "bin", "Release", "GTFOKillFeed.dll")
ASSETS = os.path.join(ROOT, "BepInEx", "plugins", "GTFOKillFeed", "Assets", "icons")
EXPECTED_ASSETS = {
    "kill-normal.png": "11a32d4bdb7d69ee626c2591f9130f8bd792f2df386198e5fd001ac13e541a72",
    "kill-weakspot.png": "3bca1e68b6c2830ee2085ae7888ff5fb9b2f1c3f02f2226adbb63beac6531e68",
    "kill-heavy.png": "078c5d3f5d3e93931672404ac55d7b88bc3b715c4953e10a0acd9fd3f011183f",
    "hitmark-normal.png": "c499654d44c46b2d0f660fa829147d0ea3e1a94764489f57996e55d773db4722",
    "hitmark-weakspot.png": "615cdaba0c288ce8750aa494589a5821120a2491f9e579fa366118e69adc1ea0",
    "hitmark-armor.png": "afa715f3c16fad726086a73422f624df5ec708d40768ac4f8c0efe4bf6f67486",
    "hitmark-kill.png": "853cf4929dc7161759d6bdbb850dad5911db7ef033905febb5aa8e3bef190df2",
    "teammate-downed-cai.png": "918e6fbf6f41b793d8d78f49e85f12c72184d4a24d458205d6f83fe9c800170f",
    "teammate-downed-ruo.png": "5510a6e6e3ac90789270463e21887949a2df8c092968a92db00d3aac2bc7e9d4",
    "teammate-downed-si.png": "518f72e9efc6316d18135503dcdf0d5167f3806aa724e07a0e6ca1c20e4eb746",
    "teammate-downed-sha.png": "84ee7a7d867062fed9c50b5cc2b6f7da6aa97fac00862624036979e2aa63a786",
    "OFL-YujiSyuku.txt": "ef7c85c72ae94381c8bc4832ae4e6fbabdeafa2bb8a31313cd75dce95a690256",
}
PACKAGE_ORDER = [
    "manifest.json", "README.md", "CHANGELOG.md", "icon.png",
    "BepInEx/plugins/GTFOKillFeed/GTFOKillFeed.dll",
] + ["BepInEx/plugins/GTFOKillFeed/Assets/icons/" + n for n in EXPECTED_ASSETS]


def sha(data):
    return hashlib.sha256(data).hexdigest()


def read_text(rel):
    with open(os.path.join(ROOT, rel), encoding="utf-8") as fh:
        return fh.read()


def check_no_local_paths(text, name):
    for i in range(0, max(0, len(text) - 2)):
        if text[i].isalpha() and text[i + 1] == ":" and text[i + 2] in "/" + chr(92):
            if i == 0 or not text[i - 1].isalnum():
                raise SystemExit("local absolute path in " + name + ": " + text[i:i + 3])
    for marker in ("/" + "Users/", "/" + "home/"):
        if marker in text:
            raise SystemExit("local absolute path in " + name + ": " + marker)


def main(argv):
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--dll", default=DLL)
    args = ap.parse_args(argv)
    manifest = json.loads(read_text("manifest.json"))
    version = manifest["version_number"]
    if version != "1.2.17":
        raise SystemExit("unexpected manifest version: " + version)
    if manifest.get("name") != "KillFeed":
        raise SystemExit("unexpected package name")
    plugin_info = read_text("src/HawChow.GTFOKillFeed/PluginInfo.cs")
    m = re.search(r'Version\s*=\s*"([^"]+)"', plugin_info)
    if not m or m.group(1) != version:
        raise SystemExit("PluginInfo version mismatch")
    asm = read_text("src/Properties/AssemblyInfo.cs")
    if ('AssemblyVersion("' + version + '.0")') not in asm or ('AssemblyFileVersion("' + version + '.0")') not in asm:
        raise SystemExit("AssemblyInfo version mismatch")

    if not os.path.isfile(args.dll):
        raise SystemExit("missing built DLL: " + args.dll)
    dll = open(args.dll, "rb").read()
    entries = {}
    for rel in PACKAGE_ORDER:
        path = os.path.join(ROOT, rel) if rel != "BepInEx/plugins/GTFOKillFeed/GTFOKillFeed.dll" else args.dll
        if not os.path.isfile(path):
            raise SystemExit("missing package file: " + rel)
        entries[rel] = open(path, "rb").read()

    for name, expected in EXPECTED_ASSETS.items():
        actual = sha(entries["BepInEx/plugins/GTFOKillFeed/Assets/icons/" + name])
        if actual != expected:
            raise SystemExit("asset changed: " + name + " expected=" + expected + " actual=" + actual)
    if sha(entries["icon.png"]) != "7294c9c1e0f4f917adbc6f5d8d7f8de057bf2a35c5fb5669a43b86a20cf03315":
        raise SystemExit("icon.png changed")
    for rel in ("README.md", "CHANGELOG.md", "manifest.json"):
        check_no_local_paths(entries[rel].decode("utf-8"), rel)
    if version not in entries["README.md"].decode("utf-8") or version not in entries["CHANGELOG.md"].decode("utf-8"):
        raise SystemExit("version missing from README/CHANGELOG")
    # The ZIP may not carry a contributor-machine path or a CodeView PDB path, in any
    # entry, text or binary. This is what makes a leaking build impossible to package.
    for rel in PACKAGE_ORDER:
        for f in privacy_scan.scan_blob(entries[rel], rel):
            raise SystemExit("release artifact leak in %s: %s@%d %s"
                             % (f["label"], f["kind"], f["offset"], f["snippet"][:80]))

    os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
    with zipfile.ZipFile(args.out, "w", zipfile.ZIP_DEFLATED) as zf:
        for rel in PACKAGE_ORDER:
            info = zipfile.ZipInfo(rel, date_time=(2026, 11, 12, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            zf.writestr(info, entries[rel])
    data = open(args.out, "rb").read()
    print("zip=" + os.path.abspath(args.out))
    print("entries=" + str(len(entries)))
    print("sha256=" + sha(data))
    print("size=" + str(len(data)))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
