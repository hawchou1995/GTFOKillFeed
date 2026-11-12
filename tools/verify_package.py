#!/usr/bin/env python3
"""Verify the release ZIP layout, assets, manifest, version and embedded resources."""
import argparse
import json
import os
import subprocess
import sys
import tempfile
import zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, "tools"))
import verify_baseline as vb
from package_release import EXPECTED_ASSETS, PACKAGE_ORDER, check_no_local_paths, sha
import privacy_scan

DOTNET = os.environ.get("DOTNET", "dotnet")
INSPECT = os.path.join(ROOT, "tools", "Inspect", "bin", "Release", "net6.0", "Inspect.dll")
FORBIDDEN = ("src/", ".cs", ".pdb", "bin/", "obj/", ".log", ".cfg", ".zip", "Assembly-CSharp", "BepInEx/core")


def ensure_inspect():
    if os.path.exists(INSPECT):
        return
    gtfo = os.environ.get("GTFO_DIR", "")
    if not gtfo:
        raise SystemExit("Inspect is missing and GTFO_DIR is not set")
    subprocess.run([DOTNET, "build", os.path.join(ROOT, "tools", "Inspect", "Inspect.csproj"),
                    "-c", "Release", "-v", "q", "--nologo", "-p:GTFO_DIR=" + gtfo,
                    "--configfile", os.path.join(ROOT, "nuget.config")], check=True)
    if not os.path.exists(INSPECT):
        raise SystemExit("Inspect build produced no " + INSPECT)


def dump(dll, out):
    ensure_inspect()
    subprocess.run([DOTNET, INSPECT, dll, out], check=True, capture_output=True)
    return vb.load_dump(out)


def main(argv):
    ap = argparse.ArgumentParser()
    ap.add_argument("--zip", required=True)
    ap.add_argument("--baseline", required=True)
    ap.add_argument("--dll", required=True)
    args = ap.parse_args(argv)
    failures = []

    def check(name, ok, detail=""):
        print(("  OK   " if ok else "  FAIL ") + name + " " + str(detail))
        if not ok:
            failures.append(name + ": " + str(detail))

    with zipfile.ZipFile(args.zip) as zf:
        names = zf.namelist()
        check("layout.count", len(names) == 17, len(names))
        check("layout.exact", names == PACKAGE_ORDER, names)
        for name in names:
            check("layout.forbidden:" + name, not any(x in name for x in FORBIDDEN), name)
        manifest = json.loads(zf.read("manifest.json").decode("utf-8"))
        check("manifest.name", manifest.get("name") == "KillFeed", manifest.get("name"))
        check("manifest.version", manifest.get("version_number") == "1.2.17", manifest.get("version_number"))
        check("manifest.deps", manifest.get("dependencies") == ["BepInEx-BepInExPack_GTFO-3.2.3", "randomuserhi-DamageSync-0.0.7"], manifest.get("dependencies"))
        for rel in ("manifest.json", "README.md", "CHANGELOG.md"):
            try:
                check_no_local_paths(zf.read(rel).decode("utf-8"), rel)
                check("paths:" + rel, True)
            except SystemExit as e:
                check("paths:" + rel, False, str(e))
        check("docs.version.readme", "1.2.17" in zf.read("README.md").decode("utf-8"))
        check("docs.version.changelog", "1.2.17" in zf.read("CHANGELOG.md").decode("utf-8"))
        for name, expected in EXPECTED_ASSETS.items():
            rel = "BepInEx/plugins/GTFOKillFeed/Assets/icons/" + name
            check("asset:" + name, sha(zf.read(rel)) == expected, sha(zf.read(rel)))
        check("asset:icon.png", sha(zf.read("icon.png")) == "7294c9c1e0f4f917adbc6f5d8d7f8de057bf2a35c5fb5669a43b86a20cf03315")
        privacy = []
        for name in names:
            privacy.extend(privacy_scan.scan_blob(zf.read(name), name))
        check("privacy.entries", not privacy,
              "; ".join("%s %s@%d %s" % (f["label"], f["kind"], f["offset"], f["snippet"][:48]) for f in privacy[:6]))
        package_dll = zf.read("BepInEx/plugins/GTFOKillFeed/GTFOKillFeed.dll")
        records = privacy_scan.pe_codeview_records(package_dll)
        codeview_paths = [r for r in records if r["path"] and privacy_scan.is_absolute_local(r["path"])]
        check("privacy.dll-codeview", not codeview_paths,
              " | ".join("%s:%s" % (r["signature"], r["path"]) for r in records) if records else "no CodeView (RSDS/NB10) record")
        check("dll.matches.built", sha(package_dll) == sha(open(args.dll, "rb").read()))
        tmp = tempfile.mkdtemp(prefix="kf-pkg-", dir=os.environ.get("PI_SCRATCH_DIR", None))
        base = dump(args.baseline, os.path.join(tmp, "base.jsonl"))
        final = dump(args.dll, os.path.join(tmp, "final.jsonl"))
        check("assembly.version", final["asm"]["version"] == "1.2.17.0", final["asm"]["version"])
        check("assembly.name", final["asm"]["name"] == "GTFOKillFeed", final["asm"]["name"])
        check("resources.count", len(final["res"]) == 27, len(final["res"]))
        check("resources.identical", base["res"] == final["res"], "27 resources")
        check("refs.identical", base["refs"] == final["refs"], len(final["refs"]))

    if failures:
        print("SUMMARY: FAIL (%d)" % len(failures))
        return 1
    print("SUMMARY: PASS (17 package entries, 12 PNG + OFL byte-identical, 27 embedded resources, 1.2.17, "
          "no local path or CodeView PDB path in any text/binary entry)")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
