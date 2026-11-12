#!/usr/bin/env python3
"""Compare the final plugin against the saved reconstructed 1.2.16 baseline.

This is deliberately separate from tools/verify_baseline.py: the original IL
inventory is never edited. Every method difference must be listed with a reason in
docs/INTEGRATION-DIFF.txt; any unlisted difference, stale entry, resource change or
unexpected identity change fails this gate.
"""
import argparse
import json
import os
import subprocess
import sys
import tempfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, "tools"))
import verify_baseline as vb

DOTNET = os.environ.get("DOTNET", "dotnet")
INSPECT = os.path.join(ROOT, "tools", "Inspect", "bin", "Release", "net6.0", "Inspect.dll")
DEFAULT_ALLOWLIST = os.path.join(ROOT, "docs", "INTEGRATION-DIFF.txt")


def run(args, what):
    p = subprocess.run(args, capture_output=True)
    if p.returncode != 0:
        raise RuntimeError(what + ": rc=%d\n%s" % (p.returncode,
            (p.stdout + p.stderr).decode("cp936", "replace")[-1200:]))
    return p.stdout.decode("utf-8", "replace")


def ensure_inspect():
    if os.path.exists(INSPECT):
        return
    gtfo = os.environ.get("GTFO_DIR", "")
    if not gtfo:
        raise RuntimeError("Inspect is missing and GTFO_DIR is not set; set GTFO_DIR to a BepInEx reference tree")
    run([DOTNET, "build", os.path.join(ROOT, "tools", "Inspect", "Inspect.csproj"),
         "-c", "Release", "-v", "q", "--nologo", "-p:GTFO_DIR=" + gtfo,
         "--configfile", os.path.join(ROOT, "nuget.config")], "build Inspect")
    if not os.path.exists(INSPECT):
        raise RuntimeError("Inspect build produced no " + INSPECT)


def dump(dll, tag, tmp):
    j = os.path.join(tmp, tag + ".jsonl")
    i = os.path.join(tmp, tag + ".il")
    out = run([DOTNET, INSPECT, dll, j], "inspect " + tag)
    if not out.startswith("dumped "):
        raise RuntimeError("inspect " + tag + " unexpected stdout: " + out[:200])
    out = run([DOTNET, INSPECT, "ildump", dll, i], "ildump " + tag)
    if "ildump methods:" not in out:
        raise RuntimeError("ildump " + tag + " unexpected stdout: " + out[:200])
    return vb.load_dump(j), vb.load_il(i)


def parse_allowlist(path):
    rows = {}
    with open(path, encoding="utf-8") as fh:
        for line in fh:
            line = line.rstrip("\n")
            if not line or line.startswith("#"):
                continue
            parts = line.split("	")
            if len(parts) != 3:
                raise RuntimeError("bad allowlist line: " + line)
            status, key, reason = parts
            if status not in ("A", "R", "C") or not reason.strip():
                raise RuntimeError("bad allowlist status/reason: " + line)
            if key in rows:
                raise RuntimeError("duplicate allowlist key: " + key)
            rows[key] = (status, reason)
    return rows


def main(argv):
    ap = argparse.ArgumentParser()
    ap.add_argument("--baseline", required=True)
    ap.add_argument("--final", required=True)
    ap.add_argument("--allowlist", default=DEFAULT_ALLOWLIST)
    ap.add_argument("--out", default=os.path.join(ROOT, "docs", "INTEGRATION-VERIFY.json"))
    args = ap.parse_args(argv)

    ensure_inspect()
    tmp = tempfile.mkdtemp(prefix="kf-integration-", dir=os.environ.get("PI_SCRATCH_DIR", None))
    base, ilb = dump(args.baseline, "baseline", tmp)
    final, ilf = dump(args.final, "final", tmp)
    allow = parse_allowlist(args.allowlist)

    failures = []
    checks = []

    def check(name, ok, detail=""):
        checks.append({"check": name, "ok": bool(ok), "detail": detail})
        if not ok:
            failures.append({"check": name, "detail": detail})

    ba, fa = base["asm"], final["asm"]
    check("assembly.name", ba["name"] == fa["name"] == "GTFOKillFeed", ba["name"] + " -> " + fa["name"])
    check("assembly.module", ba["module"] == fa["module"] == "GTFOKillFeed.dll", ba["module"] + " -> " + fa["module"])
    check("assembly.culture-token", (ba["culture"], ba["publicKeyToken"]) == (fa["culture"], fa["publicKeyToken"]), str((ba["culture"], ba["publicKeyToken"])))
    check("assembly.version", ba["version"] == "1.2.16.0" and fa["version"] == "1.2.17.0", ba["version"] + " -> " + fa["version"])
    check("assembly.refs", base["refs"] == final["refs"], "%d refs" % len(base["refs"]))

    check("resources.count", len(base["res"]) == len(final["res"]) == 27, "%d -> %d" % (len(base["res"]), len(final["res"])))
    res_diff = {k: (base["res"].get(k), final["res"].get(k)) for k in set(base["res"]) | set(final["res"]) if base["res"].get(k) != final["res"].get(k)}
    check("resources.bytes", not res_diff, json.dumps(res_diff, ensure_ascii=False) if res_diff else "27 identical (name,size,sha256)")

    bm = set(base["methods"])
    fm = set(final["methods"])
    added = fm - bm
    removed = bm - fm
    common_il = set(ilb) & set(ilf)
    changed = set(k for k in common_il if ilb[k] != ilf[k])
    exact = common_il - changed

    diffs = {}
    for k in added:
        diffs[k[0] + "|" + k[1]] = "A"
    for k in removed:
        diffs[k[0] + "|" + k[1]] = "R"
    for k in changed:
        diffs[k] = "C"

    missing = sorted(set(diffs) - set(allow))
    stale = sorted(set(allow) - set(diffs))
    wrong = sorted(k for k, st in diffs.items() if k in allow and allow[k][0] != st)
    check("methods.unlisted-differences", not missing, "\n".join(missing[:30]))
    check("methods.stale-allowlist", not stale, "\n".join(stale[:30]))
    check("methods.status", not wrong, "\n".join(wrong[:30]))
    unchanged_dump_text = all(ilb[k] == ilf[k] for k in exact)
    check("methods.unchanged-dump-text-identical", unchanged_dump_text and len(exact) == 947, "%d exact ildump text / %d common" % (len(exact), len(common_il)))
    check("methods.counts", (len(added), len(removed), len(changed)) == (157, 21, 19), "%d/%d/%d" % (len(added), len(removed), len(changed)))

    report = {
        "baseline": os.path.abspath(args.baseline),
        "final": os.path.abspath(args.final),
        "baseline_methods": len(bm),
        "final_methods": len(fm),
        "exact_unchanged": len(exact),
        "added": len(added),
        "removed": len(removed),
        "changed_il": len(changed),
        "resources": len(final["res"]),
        "allowlist_entries": len(allow),
        "checks": checks,
        "failures": failures,
        "il_comparison": "Mono.Cecil ildump text equality; metadata tokens and offsets are part of the text, not a raw byte comparison",
        "method_rows": [{"status": diffs[k], "method": k, "reason": allow.get(k, ("", ""))[1]} for k in sorted(diffs)],
    }
    os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
    with open(args.out, "w", encoding="utf-8", newline="\n") as fh:
        json.dump(report, fh, ensure_ascii=False, indent=2)
        fh.write("\n")

    print("== integration checks ==")
    for c in checks:
        print(("  OK   " if c["ok"] else "  FAIL ") + c["check"] + " " + str(c["detail"]))
    print("== counts ==")
    print("  baseline=%d final=%d exact-dump-text=%d added=%d removed=%d changed=%d resources=%d" %
          (len(bm), len(fm), len(exact), len(added), len(removed), len(changed), len(final["res"])))
    if failures:
        print("SUMMARY: FAIL (%d finding(s))" % len(failures))
        return 1
    print("SUMMARY: PASS (all method differences are listed and reasoned; 27 resources unchanged)")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
