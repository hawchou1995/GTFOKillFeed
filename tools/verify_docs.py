#!/usr/bin/env python3
"""Documentation gate for KillFeed.

Pinned here:
1. Every user-facing default quoted by README.md is derived from the shipped
   production constants and the Config.Bind wiring that uses them.
2. Every repository path cited by README.md or by a cfg description string in
   src/ must exist in the checkout (game-side BepInEx/config paths excepted).
3. global.json, manifest.json, AssemblyInfo and README agree on SDK and version.

Usage:
    python tools/verify_docs.py
    python tools/verify_docs.py --negative   # prove every gate can fail
"""

import argparse
import glob
import json
import os
import re
import shutil
import sys
import tempfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

EXPECTED_SDK = "6.0.428"
EXPECTED_ROLL_FORWARD = "latestPatch"

KILLVIEW = "src/HawChow.GTFOKillFeed/KillViewPolicy.cs"
HITSOUND = "src/HawChow.GTFOKillFeed/HitSoundPolicy.cs"
SOUNDPLAN = "src/HawChow.GTFOKillFeed/SoundConfigPlan.cs"
PLUGIN = "src/HawChow.GTFOKillFeed/Plugin.cs"
SOUNDBINDING = "src/HawChow.GTFOKillFeed/SoundConfigBinding.cs"
ASSEMBLY_INFO = "src/Properties/AssemblyInfo.cs"
README = "README.md"
REPRODUCE = "docs/REPRODUCE.md"

# README claims that used to contradict 1.2.17 production behaviour.
STALE_README = (
    "击杀反馈默认画在敌人头顶",
    "`HitSound/Enabled` 现在默认 `false`",
    "`MuteVanillaIndicator` 默认仍为 `true`",
    "想要 BF 手感就把两者一起打开",
    "默认静音原版命中提示层",
    "`MuteVanillaIndicator`（`true`",
    "把 `KillIcon/Display` 改成 `Card` 即可",
)

PATH_ROOTS = ("reports", "scripts", "docs", "tools", "tests", "src", "BepInEx")
PATH_SKIP_PREFIXES = ("BepInEx/config",)  # game-side user path
PATH_SCAN = (README, PLUGIN)


class GateError(Exception):
    pass


def read(root, rel):
    with open(os.path.join(root, rel), "rb") as handle:
        return handle.read().decode("utf-8-sig")


def const(root, rel, name):
    text = read(root, rel)
    match = re.search(r"\b" + re.escape(name) + r"\s*=\s*([0-9.]+|true|false)f?\s*;", text)
    if not match:
        raise GateError("constant %s not found in %s" % (name, rel))
    return match.group(1)


def fmt(value):
    return value


def evaluate(root, verbose=True):
    checks = 0
    failures = []

    def gate(gid, ok, detail=""):
        nonlocal checks
        checks += 1
        if verbose:
            print(("DOCGATE %-32s " % gid) + ("OK" if ok else "FAIL  " + detail))
        if not ok:
            failures.append(gid)

    values = {}
    try:
        for key, rel, name in (
            ("card", KILLVIEW, "DefaultCardEnabled"),
            ("head", KILLVIEW, "DefaultHeadEnabled"),
            ("opacity", KILLVIEW, "DefaultOpacity"),
            ("hit_channel", HITSOUND, "DefaultHitEnabled"),
            ("kill_channel", HITSOUND, "DefaultKillEnabled"),
            ("hit_volume", HITSOUND, "DefaultHitVolume"),
            ("kill_volume", HITSOUND, "DefaultKillVolume"),
            ("legacy_volume", HITSOUND, "DefaultVolume"),
            ("mute", HITSOUND, "DefaultMuteVanillaIndicator"),
            ("master", SOUNDPLAN, "LegacyMasterDefault"),
            ("channel_volume", SOUNDPLAN, "DefaultChannelVolume"),
        ):
            values[key] = const(root, rel, name)
        plugin = read(root, PLUGIN)
        plan = read(root, SOUNDPLAN)
        binding = read(root, SOUNDBINDING)
        wiring = [
            '"Card", "Enabled", defaultValue: KillViewPolicy.DefaultCardEnabled',
            '"KillIcon", "HeadEnabled", defaultValue: KillViewPolicy.DefaultHeadEnabled',
            '"KillIcon", "Opacity", KillViewPolicy.DefaultOpacity',
        ]
        missing = [item for item in wiring if item not in plugin]
        gate("bind.view-defaults", not missing, "missing binding(s): %r" % (missing,))
        sound_wiring = [
            ('"HitSound", "MuteVanillaIndicator", defaultValue: false', plugin),
            ('config.Bind("HitSound", SoundConfigPlan.KeyMasterEnabled, plan.BindMasterEnabled', binding),
            ("BindMasterEnabled = LegacyMasterDefault;", plan),
            ("NewInstallChannelEnabled = false;", plan),
            ("DefaultChannelVolume = 0.4f;", plan),
            ("LegacyVolumeDefault = 0.8f;", plan),
        ]
        missing = [item for item, text in sound_wiring if item not in text]
        gate("bind.sound-defaults", not missing, "missing wiring: %r" % (missing,))
        gate("bind.sound-channel-volume-agrees",
             fmt(values["channel_volume"]) == fmt(values["hit_volume"]) == fmt(values["kill_volume"]),
             "DefaultChannelVolume=%s vs %s/%s" % (values["channel_volume"], values["hit_volume"], values["kill_volume"]))
    except (GateError, OSError) as error:
        gate("bind.view-defaults", False, str(error))
        gate("bind.sound-defaults", False, str(error))
        gate("bind.sound-channel-volume-agrees", False, str(error))

    try:
        readme = read(root, README)
    except OSError as error:
        gate("doc.readme-present", False, str(error))
        readme = ""

    if "card" in values:
        gate("doc.card-default",
             ("`Card/Enabled` 默认 `%s`" % fmt(values["card"])) in readme,
             "README must state Card/Enabled default %s" % fmt(values["card"]))
        gate("doc.head-default",
             ("`KillIcon/HeadEnabled` 默认 `%s`" % fmt(values["head"])) in readme,
             "README must state KillIcon/HeadEnabled default %s" % fmt(values["head"]))
        gate("doc.opacity-default",
             ("`KillIcon/Opacity` 默认 `%s`" % fmt(values["opacity"])) in readme,
             "README must state KillIcon/Opacity default %s" % fmt(values["opacity"]))
        gate("doc.sound-master",
             ("`HitSound/Enabled` 父开关默认 `%s`" % fmt(values["master"])) in readme,
             "README must state the HitSound parent switch default %s" % fmt(values["master"]))
        gate("doc.sound-channels",
             ("`HitSound/HitEnabled`、`KillEnabled` 默认 `%s`" % fmt(values["hit_channel"])) in readme
             and fmt(values["hit_channel"]) == fmt(values["kill_channel"]),
             "README must state both channel defaults %s/%s" % (fmt(values["hit_channel"]), fmt(values["kill_channel"])))
        gate("doc.sound-volumes",
             ("`HitVolume`、`KillVolume` 默认 `%s`" % fmt(values["hit_volume"])) in readme
             and fmt(values["hit_volume"]) == fmt(values["kill_volume"]),
             "README must state both channel volumes %s/%s" % (fmt(values["hit_volume"]), fmt(values["kill_volume"])))
        gate("doc.sound-legacy-volume",
             ("`HitSound/Volume` 默认 `%s`" % fmt(values["legacy_volume"])) in readme,
             "README must state legacy master volume default %s" % fmt(values["legacy_volume"]))
        gate("doc.sound-mute",
             ("`HitSound/MuteVanillaIndicator` 默认 `%s`" % fmt(values["mute"])) in readme,
             "README must state MuteVanillaIndicator default %s" % fmt(values["mute"]))
    gate("doc.head-card-independent", "两者可同时开启" in readme,
         "README must state Card/Enabled and KillIcon/HeadEnabled can be on together")
    stale = [phrase for phrase in STALE_README if phrase in readme]
    gate("doc.no-stale-defaults", not stale, "stale README claims: %r" % (stale,))

    missing_paths = []
    pattern = re.compile(r"(?<![\w/.-])((?:%s)/[A-Za-z0-9_.\-/*{}+]+)" % "|".join(PATH_ROOTS))
    for rel in PATH_SCAN:
        try:
            text = read(root, rel)
        except OSError:
            missing_paths.append("%s (unreadable)" % rel)
            continue
        for line in text.split("\n"):
            for match in pattern.finditer(line):
                token = match.group(1).rstrip(".,;:，。；：）)\"'")
                if token.startswith(PATH_SKIP_PREFIXES):
                    continue
                if not glob.glob(os.path.join(root, token)):
                    missing_paths.append("%s -> %s" % (rel, token))
    gate("paths.repo-evidence", not missing_paths, "missing repo paths: %r" % (missing_paths[:8],))

    try:
        sdk = json.loads(read(root, "global.json"))["sdk"]
        sdk_ok = (sdk.get("version") == EXPECTED_SDK
                  and sdk.get("rollForward") == EXPECTED_ROLL_FORWARD
                  and EXPECTED_SDK in readme and "global.json" in readme
                  and EXPECTED_SDK in read(root, REPRODUCE) and "global.json" in read(root, REPRODUCE))
        gate("sdk.pin", sdk_ok,
             "global.json must pin %s (%s), README/REPRODUCE must state it; got %r"
             % (EXPECTED_SDK, EXPECTED_ROLL_FORWARD, sdk))
    except (OSError, ValueError, KeyError) as error:
        gate("sdk.pin", False, "global.json unusable: %s" % error)

    try:
        manifest = json.loads(read(root, "manifest.json"))
        version = manifest["version_number"]
        assembly = re.search(r'AssemblyVersion\("([0-9.]+)"\)', read(root, ASSEMBLY_INFO)).group(1)
        changelog = read(root, "CHANGELOG.md")
        gate("version.pin",
             assembly == version + ".0"
             and ("版本 `%s` 的程序集版本为 `%s`" % (version, assembly)) in readme
             and ("## " + version) in changelog,
             "manifest=%s assembly=%s README/CHANGELOG must agree" % (version, assembly))
    except (OSError, ValueError, KeyError, AttributeError) as error:
        gate("version.pin", False, "version inputs unusable: %s" % error)

    return checks, failures


def copy_tree(root):
    target = tempfile.mkdtemp(prefix="kf-docneg-")
    ignore = shutil.ignore_patterns(".git", "bin", "obj", "__pycache__", "orig")
    shutil.copytree(root, os.path.join(target, "repo"), ignore=ignore)
    return os.path.join(target, "repo")


def rewrite(root, rel, transform):
    path = os.path.join(root, rel)
    original = open(path, "rb").read().decode("utf-8-sig")
    updated = transform(original)
    if updated == original:
        raise GateError("negative control made no change in %s" % rel)
    open(path, "wb").write(updated.encode("utf-8"))


def negative(root):
    work = copy_tree(root)
    results = []

    def control(name, gate_id, rel, transform):
        path = os.path.join(work, rel)
        original = open(path, "rb").read()
        try:
            rewrite(work, rel, transform)
            unused_checks, failures = evaluate(work, verbose=False)
            ok = gate_id in failures
            detail = "gate %s raised" % gate_id if ok else "expected %s, got %r" % (gate_id, failures)
        except GateError as error:
            ok, detail = False, str(error)
        finally:
            open(path, "wb").write(original)
        results.append((name, ok, detail))
        print(("PASS " if ok else "FAIL ") + name + ": " + detail)

    try:
        control("README Card/Enabled default flipped", "doc.card-default", README,
                lambda text: text.replace("`Card/Enabled` 默认 `true`", "`Card/Enabled` 默认 `false`"))
        control("README MuteVanillaIndicator default flipped", "doc.sound-mute", README,
                lambda text: text.replace("`HitSound/MuteVanillaIndicator` 默认 `false`",
                                          "`HitSound/MuteVanillaIndicator` 默认 `true`"))
        control("README cites a missing report", "paths.repo-evidence", README,
                lambda text: text + "\n- 详见 `reports/feature-1.2.16-analysis.md`。\n")
        control("cfg description cites a missing report", "paths.repo-evidence", PLUGIN,
                lambda text: text.replace("docs/FEATURE-SWITCHES.md", "reports/feature-1.2.12-analysis.md", 1))
        control("production head default flipped", "doc.head-default", KILLVIEW,
                lambda text: text.replace("DefaultHeadEnabled = false;", "DefaultHeadEnabled = true;", 1))
        control("global.json SDK changed", "sdk.pin", "global.json",
                lambda text: text.replace(EXPECTED_SDK, "7.0.410", 1))
        control("manifest version changed", "version.pin", "manifest.json",
                lambda text: text.replace('"version_number": "1.2.17"', '"version_number": "1.2.18"', 1))
    finally:
        shutil.rmtree(os.path.dirname(work), ignore_errors=True)

    bad = [item for item in results if not item[1]]
    print("SUMMARY: " + ("all %d negative controls raised the expected gate" % len(results)
                         if not bad else "%d control(s) did not fail as expected" % len(bad)))
    return 0 if not bad else 1


def main(argv):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", default=ROOT)
    parser.add_argument("--negative", action="store_true")
    args = parser.parse_args(argv)
    root = os.path.abspath(args.root)
    if args.negative:
        return negative(root)
    checks, failures = evaluate(root)
    print("DOCGATE RESULT gates=%d failures=%d" % (checks, len(failures)))
    return 0 if not failures else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
