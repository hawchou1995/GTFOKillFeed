#!/usr/bin/env python3
"""Negative controls for tools/verify_baseline.py.

Each control mutates one input and asserts that the verifier reports a failure with the expected gate.
A control that does NOT fail means the gate is blind. The unmutated baseline must stay clean first.
"""
import copy
import json
import os
import subprocess
import sys
import tempfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import verify_baseline as vb


def artifacts():
    tmp = tempfile.mkdtemp(prefix="kf-neg-", dir=vb.SCRATCH)
    p = {k: os.path.join(tmp, n) for k, n in
         (("oj", "o.jsonl"), ("rj", "r.jsonl"), ("oi", "o.il"), ("ri", "r.il"))}
    vb.run_checked([vb.DOTNET, vb.INSPECT, vb.ORIG_DLL, p["oj"]], "inspect original")
    vb.run_checked([vb.DOTNET, vb.INSPECT, vb.REBUILT_DLL, p["rj"]], "inspect rebuilt")
    vb.run_checked([vb.DOTNET, vb.INSPECT, "ildump", vb.ORIG_DLL, p["oi"]], "ildump original")
    vb.run_checked([vb.DOTNET, vb.INSPECT, "ildump", vb.REBUILT_DLL, p["ri"]], "ildump rebuilt")
    return {"orig": vb.load_dump(p["oj"]), "rebuilt": vb.load_dump(p["rj"]),
            "il_orig": vb.load_il(p["oi"]), "il_rebuilt": vb.load_il(p["ri"])}


def gates(res):
    return {f["gate"] for f in res["failures"]}


def control(name, expect_gate, mutate):
    base = artifacts()
    clean = vb.compare(base["orig"], base["rebuilt"], base["il_orig"], base["il_rebuilt"])
    if clean["failures"]:
        return False, "baseline is not clean: %d finding(s), first=%s" % (len(clean["failures"]), clean["failures"][0])
    mutated = copy.deepcopy(base)
    note = mutate(mutated)
    res = vb.compare(mutated["orig"], mutated["rebuilt"], mutated["il_orig"], mutated["il_rebuilt"])
    hit = gates(res)
    if expect_gate not in hit:
        return False, "expected gate %s, got %s (%s)" % (expect_gate, sorted(hit), note)
    return True, "gate %s raised (%s); %d finding(s)" % (expect_gate, note, len(res["failures"]))


def main():
    results = []

    def field_type(m):
        k = sorted(m["rebuilt"]["fields"])[0]
        t, a, s, l = m["rebuilt"]["fields"][k]
        m["rebuilt"]["fields"][k] = ("System.Int32" if t != "System.Int32" else "System.Int64", a, s, l)
        return "field %r type %s -> %s (member counts unchanged)" % (k, t, m["rebuilt"]["fields"][k][0])

    def resource_bytes(m):
        k = sorted(m["rebuilt"]["res"])[0]
        size, h = m["rebuilt"]["res"][k]
        m["rebuilt"]["res"][k] = (size, "0" * 64)
        return "resource %r sha256 altered, size kept" % k

    def method_dropped(m):
        k = sorted(m["rebuilt"]["methods"])[0]
        del m["rebuilt"]["methods"][k]
        for key in [x for x in m["rebuilt"]["instrs"] if x == k]:
            del m["rebuilt"]["instrs"][key]
        return "method %r removed from rebuilt metadata" % (k,)

    def il_changed(m):
        k = sorted(m["il_rebuilt"])[0]
        body = list(m["il_rebuilt"][k])
        body.append("   FFFF throw ")
        m["il_rebuilt"][k] = body
        return "extra 'throw' instruction injected into %r" % k

    for name, gate, fn in (("member signature change", "G5-fields", field_type),
                           ("resource byte change", "G4-resources", resource_bytes),
                           ("method dropped", "G5-methods", method_dropped),
                           ("IL instruction injected", "G6-inventory", il_changed)):
        ok, note = control(name, gate, fn)
        results.append((name, ok, note))
        print(("PASS " if ok else "FAIL ") + name + ": " + note)

    # tool failure: a missing dotnet executable must fail the run, not pass silently
    env = dict(os.environ)
    env["DOTNET"] = os.path.join(vb.SCRATCH, "no-such-dotnet")
    isolated = tempfile.mkdtemp(prefix="kf-neg-out-", dir=vb.SCRATCH)
    p = subprocess.run([sys.executable, os.path.join(vb.ROOT, "tools", "verify_baseline.py"),
                        "--no-build", "--out", isolated],
                       capture_output=True, env=env, timeout=600)
    out = p.stdout.decode("utf-8", "replace") + p.stderr.decode("utf-8", "replace")
    ok = p.returncode != 0 and "TOOL-FAIL" in out
    results.append(("subprocess failure (missing dotnet)", ok,
                    "rc=%d, TOOL-FAIL in output=%s" % (p.returncode, "TOOL-FAIL" in out)))
    print(("PASS " if ok else "FAIL ") + "subprocess failure (missing dotnet): rc=%d" % p.returncode)

    bad = [r for r in results if not r[1]]
    print("SUMMARY: " + ("all %d negative controls raised the expected failure" % len(results)
                         if not bad else "%d control(s) did not fail as expected" % len(bad)))
    return 0 if not bad else 1


if __name__ == "__main__":
    sys.exit(main())
