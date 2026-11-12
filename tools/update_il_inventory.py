#!/usr/bin/env python3
"""Regenerate docs/verify/il-differences.txt from the current baseline.

The inventory is the reviewed list of methods whose IL differs from 1.2.16. verify_baseline.py fails
when the set or category changes, so regenerating this file is an explicit, reviewable act:
    python tools/update_il_inventory.py --reason "why the difference set changed"
"""
import os
import sys
import tempfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import verify_baseline as vb


def main(argv):
    reason = ""
    for i, a in enumerate(argv):
        if a == "--reason":
            reason = argv[i + 1]
    if not reason:
        print("refusing to rewrite the reviewed inventory without --reason")
        return 2
    vb.ensure_tools()
    tmp = tempfile.mkdtemp(prefix="kf-inv-", dir=vb.SCRATCH)
    oj = os.path.join(tmp, "o.jsonl")
    rj = os.path.join(tmp, "r.jsonl")
    oi = os.path.join(tmp, "o.il")
    ri = os.path.join(tmp, "r.il")
    vb.run_checked([vb.DOTNET, vb.INSPECT, vb.ORIG_DLL, oj], "inspect original")
    vb.run_checked([vb.DOTNET, vb.INSPECT, vb.REBUILT_DLL, rj], "inspect rebuilt")
    vb.run_checked([vb.DOTNET, vb.INSPECT, "ildump", vb.ORIG_DLL, oi], "ildump original")
    vb.run_checked([vb.DOTNET, vb.INSPECT, "ildump", vb.REBUILT_DLL, ri], "ildump rebuilt")
    res = vb.compare(vb.load_dump(oj), vb.load_dump(rj), vb.load_il(oi), vb.load_il(ri))
    rows = [r for r in res["stats"]["il_rows"] if r["category"] != "unmatched"]
    path = os.path.join(vb.ROOT, "docs", "verify", "il-differences.txt")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as fh:
        fh.write("# Reviewed IL difference inventory for the 1.2.16 baseline recovery.\n")
        fh.write("# Format: <owner>|<method signature>\t<category>\n")
        fh.write("# Reason for this revision: %s\n" % reason)
        fh.write("# Categories describe how the IL differs; they are not a proof of equivalence.\n")
        for r in sorted(rows, key=lambda x: x["method"]):
            fh.write("%s\t%s\n" % (r["method"], r["category"]))
    print("wrote %d entries to %s" % (len(rows), path))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
