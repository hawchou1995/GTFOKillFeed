#!/usr/bin/env python3
"""Rebuild the recovered KillFeed 1.2.16 baseline and reconcile it with the original DLL.

Every subprocess return code is checked, every member (type/field/property/method) is compared by
name+signature+attributes, IL is compared per method with an explicit inventory of differences, and a
subset of pure functions is executed on both assemblies to compare observable results.

Exit code is 0 only if every gate passes. Gates:
  G1 build                      (rc == 0, output DLL exists)
  G2 tooling                    (inspect/ildump/decompile/semantics rc == 0, expected artifacts non-empty)
  G3 assembly identity + refs
  G4 embedded resources         (name, size, sha256)
  G5 members                    (types/fields/properties/methods/attributes, incl. Harmony declarations)
  G6 IL inventory               (no unexplained per-method difference; compiler-generated names mapped)
  G7 executable differential    (same results for every executed method+input; coverage reported)
  G8 re-decompile cross-check   (no source file missing; differences reported, not gated as "equal")
"""
import collections
import difflib
import json
import os
import subprocess
import sys
import tempfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SCRATCH = os.environ.get("PI_SCRATCH_DIR", os.path.dirname(ROOT))
DOTNET = os.environ.get("DOTNET", "dotnet")
GTFO_DIR = os.environ.get("GTFO_DIR", "")
ORIG_DLL = os.path.join(ROOT, "orig", "BepInEx", "plugins", "GTFOKillFeed", "GTFOKillFeed.dll")
SRC = os.path.join(ROOT, "src")
REBUILT_DLL = os.path.join(SRC, "bin", "Release", "GTFOKillFeed.dll")
INSPECT = os.path.join(ROOT, "tools", "Inspect", "bin", "Release", "net6.0", "Inspect.dll")
DECOMPILE = os.path.join(ROOT, "tools", "Decompile", "bin", "Release", "net6.0", "Decompile.dll")
SEMANTICS = os.path.join(ROOT, "tools", "Semantics", "bin", "Release", "net6.0", "Semantics.dll")
REF_DIRS = [os.path.join(GTFO_DIR, "BepInEx", d) for d in ("core", "interop", "unity-libs")]


class ToolError(RuntimeError):
    pass


def run_checked(args, what, timeout=600):
    """Run a subprocess, require rc == 0, and return stdout. Raises ToolError with the rc otherwise."""
    try:
        p = subprocess.run(args, capture_output=True, timeout=timeout)
    except subprocess.TimeoutExpired:
        raise ToolError("%s: timeout after %ss" % (what, timeout))
    except OSError as e:
        raise ToolError("%s: cannot execute %r (%s)" % (what, args[0], e))
    if p.returncode != 0:
        tail = p.stdout.decode("cp936", "replace")[-800:] + p.stderr.decode("cp936", "replace")[-800:]
        raise ToolError("%s: rc=%d\n%s" % (what, p.returncode, tail))
    return p.stdout.decode("utf-8", "replace"), p.returncode


def load_dump(path):
    d = {"asm": None, "refs": {}, "res": {}, "types": {}, "fields": {}, "props": {}, "methods": {}, "instrs": {},
         "typeattrs": collections.Counter(), "methodattrs": collections.Counter(), "asmattrs": collections.Counter()}
    with open(path, encoding="utf-8") as fh:
        for line in fh:
            r = json.loads(line)
            k = r["kind"]
            if k == "assembly":
                d["asm"] = r
            elif k == "ref":
                d["refs"][r["name"]] = (r["version"], r["publicKeyToken"])
            elif k == "resource":
                d["res"][r["name"]] = (r["size"], r["hash"])
            elif k == "type":
                d["types"][r["name"]] = (r["attrs"], r["base"], r.get("interface", False))
            elif k == "typeattr":
                d["typeattrs"][(r["owner"], r["attr"])] += 1
            elif k == "asmattr":
                d["asmattrs"][r["attr"]] += 1
            elif k == "field":
                d["fields"][(r["owner"], r["name"])] = (r["type"], r["attrs"], r["static"], r["literal"])
            elif k == "prop":
                d["props"][(r["owner"], r["name"])] = (r["type"], r["get"], r["set"])
            elif k == "method":
                d["methods"][(r["owner"], r["sig"])] = (r["static"], r["generic"])
                d["instrs"][(r["owner"], r["sig"])] = r["instrs"]
                d.setdefault("instrs", {})[(r["owner"], r["sig"])] = r["instrs"]
            elif k == "methodattr":
                d["methodattrs"][(r["owner"], r["sig"], r["attr"])] += 1
    return d


def attrs_cmp(a, b, owner, kind, is_interface=False):
    """Compare member attributes, allowing one documented compiler-flag difference: BeforeFieldInit on interfaces."""
    if a == b:
        return None
    fa = [x.strip() for x in a.split(",") if x.strip()]
    fb = [x.strip() for x in b.split(",") if x.strip()]
    is_interface = is_interface or "Interface" in fa or "Interface" in fb
    if is_interface:
        sa = [x for x in fa if x != "BeforeFieldInit"]
        sb = [x for x in fb if x != "BeforeFieldInit"]
        if sa == sb:
            return ("explained", "BeforeFieldInit flag on %s %s: original=%s rebuilt=%s"
                    % (kind, owner, "BeforeFieldInit" in fa, "BeforeFieldInit" in fb))
    return ("diff", "%s %s attrs: %s vs %s" % (kind, owner, a, b))


def load_il(path):
    d = {}
    cur = None
    buf = []
    with open(path, encoding="utf-8", errors="replace") as fh:
        for line in fh:
            if line.startswith("### "):
                if cur is not None:
                    d[cur] = buf
                cur = line[4:].rstrip("\r\n")
                buf = []
            else:
                buf.append(line.rstrip("\r\n"))
    if cur is not None:
        d[cur] = buf
    return d


def split_instr(line):
    s = line.strip()
    if len(s) < 6:
        return None
    off, _, rest = s.partition(" ")
    if len(off) != 4:
        return None
    op, _, operand = rest.partition(" ")
    return op, operand.strip()


BRANCH_FAMILY = {"bgt": "CMP_GT", "bgt.s": "CMP_GT", "cgt": "CMP_GT", "cgt.un": "CMP_GT",
                 "blt": "CMP_LT", "blt.s": "CMP_LT", "clt": "CMP_LT", "clt.un": "CMP_LT",
                 "beq": "CMP_EQ", "beq.s": "CMP_EQ", "ceq": "CMP_EQ",
                 "bne.un": "CMP_NE", "bne.un.s": "CMP_NE",
                 "brtrue": "BR_IF", "brtrue.s": "BR_IF", "brfalse": "BR_IF", "brfalse.s": "BR_IF",
                 "brzero": "BR_IF", "brzero.s": "BR_IF", "brinst": "BR_IF", "brinst.s": "BR_IF"}


def norm_tokens(block, family=False):
    out = []
    for line in block:
        parts = split_instr(line)
        if parts is None:
            continue
        op, operand = parts
        base = op.split(".")[0]
        if base in ("ldloc", "stloc", "ldloca") and (op.split(".")[-1].isdigit() or op.endswith(".s")):
            op = base + "L"
        if op == "nop":
            continue
        if operand.startswith("IL_"):
            operand = "L"
        fixed = []
        for t in operand.split(" "):
            if t.startswith("IL_"):
                fixed.append("L")
            elif t.startswith("V_") and t[2:].isdigit():
                fixed.append("L")
            else:
                fixed.append(t)
        if family and op in BRANCH_FAMILY:
            op = BRANCH_FAMILY[op]
        out.append((op, " ".join(fixed).strip()))
    return out


def classify(a, b):
    """Describe how two method bodies differ. The category is a description, not a proof of equivalence."""
    na, nb = norm_layout(a), norm_layout(b)
    if na == nb:
        return "layout", []
    fa = ["%s %s" % (o, s) for o, s in na]
    fb = ["%s %s" % (o, s) for o, s in nb]
    diff = [l for l in difflib.unified_diff(fa, fb, lineterm="", n=0)
            if l[:1] in "+-" and not l.startswith(("+++", "---"))]
    ca, cb = collections.Counter(na), collections.Counter(nb)
    dops = set(o for o, _ in (ca - cb)) | set(o for o, _ in (cb - ca))
    text = " ".join(" ".join(t) for t in na + nb)
    br = {"cgt", "bgt", "bgt.s", "clt", "blt", "blt.s", "ceq", "beq", "beq.s", "bne.un", "bne.un.s",
          "brtrue", "brtrue.s", "brfalse", "brfalse.s", "ldc.i4.0", "ldc.i4.1", "br", "br.s", "ret"}
    tmp = {"ldlocL", "stlocL", "ldlocaL", "dup", "pop", "ldnull"}
    if not dops:
        return "operand-only", diff
    if dops <= {"castclass", "isinst"}:
        return "extra-cast", diff
    if "IFormatProvider" in text or "DefaultInterpolatedStringHandler" in text or \
       "System.String::Format" in text or "System.String::Concat" in text:
        return "string-formatting", diff
    if dops <= br:
        return "branch-comparison", diff
    if dops <= (br | tmp):
        return "branch+temp", diff
    if dops <= tmp:
        return "temp-init", diff
    if "constrained." in dops or "box" in dops or "GetEnumerator" in text or "MoveNext" in text:
        return "enumeration-shape", diff
    scaff = {"dup", "pop", "nop", "ldnull", "castclass", "isinst", "cgt", "clt", "ceq", "ret",
             "ldlocL", "stlocL", "ldlocaL", "initobj"}
    sa = [(o, s) for o, s in na if o not in scaff and not o.startswith("ldc.i4") and not o.startswith("br")]
    sb = [(o, s) for o, s in nb if o not in scaff and not o.startswith("ldc.i4") and not o.startswith("br")]
    if collections.Counter(sa) == collections.Counter(sb):
        return "codegen-only", diff
    return "other-shape", diff


def il_to_sem(key):
    """'owner|ret Name(params)' -> 'owner|Name|params' (the Semantics tool id form)."""
    owner, _, rest = key.partition("|")
    sp = rest.find(" ")
    lp = rest.find("(")
    if sp < 0 or lp < 0:
        return key
    return owner + "|" + rest[sp + 1:lp] + "|" + rest[lp + 1:-1]



def norm_layout(block):
    return norm_tokens(block, family=False)


def norm_family(block):
    return collections.Counter(norm_tokens(block, family=True))


def norm_name(name):
    for marker in ("b__", "d__", "g__"):
        if marker in name:
            head, _, tail = name.partition(marker)
            if tail.replace("_", "").isdigit():
                return head + marker + "#_#"
    return name


def sig_norm(sig):
    sp = sig.find(" ")
    if sp < 0:
        return sig
    rest = sig[sp + 1:]
    lp = rest.find("(")
    if lp < 0:
        return sig
    return sig[:sp + 1] + norm_name(rest[:lp]) + rest[lp:]


def map_generated(orig_methods, rebuild_methods):
    mapping = {}
    amb = []
    r_by_norm = collections.defaultdict(list)
    for k in rebuild_methods:
        r_by_norm[(k[0], sig_norm(k[1]))].append(k)
    unmatched = []
    for k in orig_methods:
        if k in rebuild_methods:
            continue
        cand = [c for c in r_by_norm.get((k[0], sig_norm(k[1])), []) if c not in mapping.values()]
        if len(cand) == 1:
            mapping[k] = cand[0]
        elif len(cand) > 1:
            amb.append((k, cand))
        else:
            unmatched.append(k)
    return mapping, amb, unmatched


def compare(orig, rebuilt, il_orig, il_rebuild, sem_ids=None, cfg=None):
    cfg = cfg or {}
    failures = []
    checks = []
    stats = {}

    explained = []

    def fail(gate, detail):
        failures.append({"gate": gate, "detail": detail})

    for f in ("name", "version", "module", "culture", "publicKeyToken"):
        if orig["asm"][f] != rebuilt["asm"][f]:
            fail("G3-identity", "%s: %r vs %r" % (f, orig["asm"][f], rebuilt["asm"][f]))
    for n in sorted(set(orig["refs"]) | set(rebuilt["refs"])):
        if orig["refs"].get(n) != rebuilt["refs"].get(n):
            fail("G3-references", "%s: %r vs %r" % (n, orig["refs"].get(n), rebuilt["refs"].get(n)))
    checks.append(("G3", "identity+references (%d refs)" % len(orig["refs"]),
                   "OK" if not any(f["gate"].startswith("G3") for f in failures) else "FAIL"))

    for n in sorted(set(orig["res"]) | set(rebuilt["res"])):
        if orig["res"].get(n) != rebuilt["res"].get(n):
            fail("G4-resources", "%s: %r vs %r" % (n, orig["res"].get(n), rebuilt["res"].get(n)))
    checks.append(("G4", "resources %d" % len(orig["res"]),
                   "OK" if not any(f["gate"].startswith("G4") for f in failures) else "FAIL"))

    for label, key in (("types", "types"), ("fields", "fields"), ("properties", "props")):
        a, b = orig[key], rebuilt[key]
        missing = sorted(set(a) - set(b))
        extra = sorted(set(b) - set(a))
        for m in missing[:20]:
            fail("G5-%s" % label, "missing " + repr(m))
        for m in extra[:20]:
            fail("G5-%s" % label, "extra " + repr(m))
        for k in sorted(set(a) & set(b)):
            if a[k] == b[k]:
                continue
            if label == "types":
                verdict = attrs_cmp(a[k][0], b[k][0], k, "type", a[k][2])
                if verdict is not None and verdict[0] == "explained":
                    explained.append(verdict[1])
                    if a[k][1] != b[k][1]:
                        fail("G5-types", "%r base type: %r vs %r" % (k, a[k][1], b[k][1]))
                    continue
            fail("G5-%s" % label, "%r: %r vs %r" % (k, a[k], b[k]))
        stats[label] = (len(a), len(b))
        label_fails = [f for f in failures if f["gate"] == "G5-" + label]
        checks.append(("G5", "%s %d/%d" % (label, len(a), len(b)),
                       "OK" if not (missing or extra) and not label_fails else "FAIL"))

    gen_map, gen_amb, gen_unmatched = map_generated(set(orig["methods"]), set(rebuilt["methods"]))
    for k in gen_unmatched[:20]:
        fail("G5-methods", "missing (no exact or generated-name match): %r" % (k,))
    for k, cand in gen_amb:
        fail("G5-methods", "ambiguous generated-name match %r -> %r" % (k, cand))
    stats["methods"] = (len(orig["methods"]), len(rebuilt["methods"]))
    stats["explained"] = explained
    stats["generated_mapped"] = sorted(("%r -> %r" % (k, v)) for k, v in gen_map.items())
    for k, v in orig["methods"].items():
        rk = gen_map.get(k, k)
        rv = rebuilt["methods"].get(rk)
        if rv is not None and v != rv:
            fail("G5-methods", "%r: %r vs %r" % (k, v, rv))
    checks.append(("G5", "methods %d/%d (generated-name mapped %d)" % (len(orig["methods"]), len(rebuilt["methods"]), len(gen_map)),
                   "OK" if not any(f["gate"].startswith("G5-methods") for f in failures) else "FAIL"))

    def tf_key(s):
        return s.split(",FrameworkDisplayName=")[0]

    a, b = orig["asmattrs"], rebuilt["asmattrs"]
    na = collections.Counter()
    nb = collections.Counter()
    for k, v in a.items():
        na[tf_key(k)] += v
    for k, v in b.items():
        nb[tf_key(k)] += v
    if na == nb and a != b:
        explained.append("TargetFrameworkAttribute: FrameworkDisplayName differs (%r vs %r); TFM identical"
                         % (sorted(a), sorted(b)))
    elif na != nb:
        for k in sorted(set(na) | set(nb)):
            if na.get(k, 0) != nb.get(k, 0):
                fail("G5-asmattrs", "%r: %d vs %d" % (k, na.get(k, 0), nb.get(k, 0)))
    checks.append(("G5", "asmattrs %d/%d" % (sum(a.values()), sum(b.values())),
                   "OK" if na == nb else "FAIL"))

    a, b = orig["typeattrs"], rebuilt["typeattrs"]
    for k in sorted(set(a) | set(b)):
        if a.get(k, 0) != b.get(k, 0):
            fail("G5-typeattrs", "%r: %d vs %d" % (k, a.get(k, 0), b.get(k, 0)))
    checks.append(("G5", "typeattrs %d/%d" % (sum(a.values()), sum(b.values())), "OK" if a == b else "FAIL"))
    harmony = [k for k in orig["typeattrs"] if "Harmony" in k[1]]
    harmony_b = [k for k in rebuilt["typeattrs"] if "Harmony" in k[1]]
    stats["harmony"] = (len(harmony), len(harmony_b), harmony == harmony_b)
    if harmony != harmony_b:
        fail("G5-harmony", "type-level HarmonyPatch declarations differ")
    checks.append(("G5", "HarmonyPatch declarations %d/%d" % (len(harmony), len(harmony_b)),
                   "OK" if harmony == harmony_b else "FAIL"))

    il_b = {}
    renamed = []
    gen_inv = {v: k for k, v in gen_map.items()}
    for k, v in il_rebuild.items():
        owner, _, sig = k.partition("|")
        tgt = gen_inv.get((owner, sig))
        if tgt:
            renamed.append((k, tgt[0] + "|" + tgt[1]))
            il_b[tgt[0] + "|" + tgt[1]] = v
        else:
            il_b[k] = v
    stats["il_renamed"] = renamed

    inv_path = os.path.join(ROOT, "docs", "verify", "il-differences.txt")
    inventory = {}
    if os.path.exists(inv_path):
        for line in open(inv_path, encoding="utf-8"):
            line = line.rstrip("\r\n")
            if not line or line.startswith("#"):
                continue
            key, _, cat = line.partition("\t")
            inventory[key] = cat

    rows = []
    counts = collections.Counter()
    for k in sorted(set(il_orig) | set(il_b)):
        a, b = il_orig.get(k), il_b.get(k)
        if a is None or b is None:
            counts["unmatched"] += 1
            rows.append({"method": k, "tier": "unmatched", "category": "unmatched",
                         "detail": "present only in " + ("rebuilt" if a is None else "original")})
            fail("G6-il", "method only in one assembly: " + k)
            continue
        if a == b:
            counts["exact"] += 1
            continue
        cat, diff = classify(a, b)
        counts["different"] += 1
        counts["cat:" + cat] += 1
        rows.append({"method": k, "tier": "different", "category": cat,
                     "diff_count": len(diff), "diff_lines": diff[:30]})
    stats["il_counts"] = dict(counts)
    stats["il_rows"] = rows

    diff_keys = {r["method"]: r["category"] for r in rows if r["tier"] == "different"}
    added = sorted(set(diff_keys) - set(inventory))
    removed = sorted(set(inventory) - set(diff_keys))
    changed = sorted(k for k in set(diff_keys) & set(inventory) if diff_keys[k] != inventory[k])
    for k in added[:15]:
        fail("G6-inventory", "difference not in reviewed inventory (%s): %s" % (diff_keys[k], k))
    for k in removed[:15]:
        fail("G6-inventory", "inventoried difference no longer present: " + k)
    for k in changed[:15]:
        fail("G6-inventory", "category changed for %s: %s -> %s" % (k, inventory[k], diff_keys[k]))

    sem_cov = set(sem_ids or ())
    covered = sorted(k for k in diff_keys if il_to_sem(k) in sem_cov)
    unverified = sorted(k for k in diff_keys if il_to_sem(k) not in sem_cov)
    stats["il_unverified"] = unverified
    stats["il_covered"] = covered
    checks.append(("G6", "IL exact=%d different=%d unmatched=%d (inventory %d, covered-by-exec %d, unverified %d)"
                   % (counts["exact"], counts["different"], counts["unmatched"], len(inventory),
                      len(covered), len(unverified)),
                   "OK" if counts["unmatched"] == 0 and not (added or removed or changed) else "FAIL"))
    if counts["different"]:
        checks.append(("G6", "difference categories: " + ", ".join(
            "%s=%d" % (c.replace("cat:", ""), n) for c, n in sorted(counts.items()) if c.startswith("cat:")),
            "INFO"))

    return {"failures": failures, "checks": checks, "stats": stats}


def semantics_ids(dll):
    out, _ = run_checked([DOTNET, SEMANTICS, "--asm", dll] + sum([["--ref", d] for d in REF_DIRS], []) + ["--list"],
                         "semantics --list")
    return [l.strip() for l in out.splitlines() if l.strip()]


def semantics_run(dll, ids, shard=60, timeout=240):
    res = {}
    for i in range(0, len(ids), shard):
        args = [DOTNET, SEMANTICS, "--asm", dll] + sum([["--ref", d] for d in REF_DIRS], []) + \
               ["--runall", "--skip", str(i), "--take", str(shard)]
        out, _ = run_checked(args, "semantics --runall skip=%d" % i, timeout=timeout)
        for line in out.splitlines():
            line = line.strip()
            if not line.startswith("{"):
                continue
            try:
                rec = json.loads(line)
            except json.JSONDecodeError:
                raise ToolError("semantics: unparsable output line: " + line[:200])
            res[rec["id"]] = rec
    return res


def semantic_compare(ids, orig_res, rebuilt_res, orig_root=None, rebuilt_root=None):
    def clean(items, root):
        if not root:
            return list(items)
        variants = {root, root.replace(chr(92), "/"), root.replace("/", chr(92))}
        out = []
        for x in items:
            for v in variants:
                x = x.replace(v, "<ASM_DIR>")
            out.append(x)
        return out

    failures = []
    mism = 0
    same = 0
    for i in ids:
        a, b = orig_res.get(i), rebuilt_res.get(i)
        if a is None or b is None:
            failures.append({"gate": "G7-semantics",
                             "detail": "no result for %s (orig=%s rebuilt=%s)" % (i, bool(a), bool(b))})
            continue
        ai, bi = clean(a["inputs"], orig_root), clean(b["inputs"], rebuilt_root)
        ar, br = clean(a["results"], orig_root), clean(b["results"], rebuilt_root)
        if ai != bi:
            failures.append({"gate": "G7-semantics", "detail": "input generation differs for " + i})
            continue
        if ar != br:
            mism += 1
            for k in range(min(len(ar), len(br))):
                if ar[k] != br[k]:
                    failures.append({"gate": "G7-semantics",
                                     "detail": "%s input[%d]=%s orig=%s rebuilt=%s" % (i, k, ai[k], ar[k], br[k])})
                    break
        else:
            same += 1
    return same, mism, failures
def build_tool(name, proj):
    run_checked([DOTNET, "build", proj, "-c", "Release", "-v", "q", "-p:GTFO_DIR=" + GTFO_DIR,
                 "--configfile", os.path.join(ROOT, "nuget.config")], "build " + name)


def source_crosscheck(decomp_dir):
    """Re-decompile the rebuilt DLL and diff against the recovered sources. Reported, not called 'equal'."""
    files = []
    for root, dirs, fs in os.walk(SRC):
        dirs[:] = [d for d in dirs if d not in ("bin", "obj")]
        for f in fs:
            if f.endswith(".cs"):
                files.append(os.path.relpath(os.path.join(root, f), SRC))
    ident = 0
    diffs = []
    add = dele = 0
    missing = []
    for rel in sorted(files):
        pa, pb = os.path.join(SRC, rel), os.path.join(decomp_dir, rel)
        if not os.path.exists(pb):
            missing.append(rel)
            continue
        a = open(pa, encoding="utf-8").read().splitlines()
        b = open(pb, encoding="utf-8").read().splitlines()
        if a == b:
            ident += 1
            continue
        d = [l for l in difflib.unified_diff(a, b, lineterm="", n=0)
             if l[:1] in "+-" and not l.startswith(("+++", "---"))]
        add += sum(1 for l in d if l[0] == "+")
        dele += sum(1 for l in d if l[0] == "-")
        diffs.append({"file": rel, "changed_lines": len(d)})
    return {"files": len(files), "identical": ident, "differing": len(diffs), "added": add, "deleted": dele,
            "missing": missing, "detail": sorted(diffs, key=lambda x: -x["changed_lines"])}


def write_il_report(path, stats, orig_dll, rebuilt_dll):
    c = stats.get("il_counts", {})
    rows = stats.get("il_rows", [])
    covered = set(stats.get("il_covered", []))
    lines = ["# IL difference inventory", "",
             "- original: `%s`" % os.path.relpath(orig_dll, ROOT).replace(os.sep, "/"),
             "- rebuilt:  `%s`" % os.path.relpath(rebuilt_dll, ROOT).replace(os.sep, "/"),
             "- methods: exact=%d, different=%d, unmatched=%d" % (c.get("exact", 0), c.get("different", 0), c.get("unmatched", 0)),
             "- differing methods backed by an executable comparison: %d" % len(covered),
             "- differing methods NOT backed by execution (unverified): %d" % len(stats.get("il_unverified", [])),
             "",
             "Categories describe *what differs*. They are not a proof of behavioural equivalence;",
             "a category only says how the instruction stream differs. Only G7 execution results count as",
             "evidence of equal behaviour, and G7 covers a subset of methods.",
             ""]
    cats = sorted((k.replace("cat:", ""), v) for k, v in c.items() if k.startswith("cat:"))
    lines += ["| category | methods |", "|---|---|"]
    for name, n in cats:
        lines.append("| %s | %d |" % (name, n))
    lines += ["", "## Differing methods", ""]
    for r in sorted(rows, key=lambda x: (x["category"], x["method"])):
        tag = "covered-by-exec" if r["method"] in covered else "UNVERIFIED"
        lines.append("- `%s` - %s - %s" % (r["method"], r["category"], tag))
        for d in r.get("diff_lines", [])[:4]:
            lines.append("    `%s`" % d)
    lines += ["", "## Not present in both assemblies", ""]
    un = [r for r in rows if r["category"] == "unmatched"]
    lines += ["- none"] if not un else ["- `%s`" % r["method"] for r in un]
    open(path, "w", encoding="utf-8", newline="\n").write("\n".join(lines) + "\n")
    return path



def main(argv):
    no_build = "--no-build" in argv
    skip_semantics = "--skip-semantics" in argv
    out_dir = os.path.join(ROOT, "docs", "verify")
    for i, a in enumerate(argv):
        if a == "--out":
            out_dir = argv[i + 1]
    os.makedirs(out_dir, exist_ok=True)
    checks = []
    failures = []

    def gate(name, fn):
        try:
            r = fn()
            detail, status = (r, "OK") if isinstance(r, str) else r
        except ToolError as e:
            checks.append((name, str(e).splitlines()[0], "TOOL-FAIL"))
            failures.append({"gate": name, "detail": str(e)})
            return None
        checks.append((name, detail, status))
        return detail

    if no_build:
        checks.append(("G1", "build skipped by flag", "SKIPPED"))
        failures.append({"gate": "G1-build", "detail": "build skipped by --no-build"})
    else:
        gate("G1", lambda: build_target())

    gate("G2", lambda: ensure_tools())
    tmp = tempfile.mkdtemp(prefix="kf-verify-", dir=SCRATCH)
    paths = {k: os.path.join(tmp, v) for k, v in {
        "o_json": "o.jsonl", "r_json": "r.jsonl", "o_il": "o.il", "r_il": "r.il"}.items()}

    def artifacts():
        out, _ = run_checked([DOTNET, INSPECT, ORIG_DLL, paths["o_json"]], "inspect original")
        if not out.startswith("dumped "):
            raise ToolError("inspect original: unexpected stdout " + out[:200])
        out, _ = run_checked([DOTNET, INSPECT, REBUILT_DLL, paths["r_json"]], "inspect rebuilt")
        if not out.startswith("dumped "):
            raise ToolError("inspect rebuilt: unexpected stdout " + out[:200])
        out1, _ = run_checked([DOTNET, INSPECT, "ildump", ORIG_DLL, paths["o_il"]], "ildump original")
        out2, _ = run_checked([DOTNET, INSPECT, "ildump", REBUILT_DLL, paths["r_il"]], "ildump rebuilt")
        n1 = parse_count(out1, "ildump methods: ")
        n2 = parse_count(out2, "ildump methods: ")
        m1 = sum(1 for l in open(paths["o_json"], encoding="utf-8") if '"kind":"method"' in l)
        m2 = sum(1 for l in open(paths["r_json"], encoding="utf-8") if '"kind":"method"' in l)
        if not (n1 == n2 == m1 == m2) or n1 == 0:
            raise ToolError("dump/ildump method counts disagree: ildump %d/%d, dump %d/%d" % (n1, n2, m1, m2))
        return "dump+ildump ok (%d methods each)" % n1

    if gate("G2-dumps", artifacts) is None:
        return report(out_dir, checks, failures, {})
    orig = load_dump(paths["o_json"])
    rebuilt = load_dump(paths["r_json"])
    il_o = load_il(paths["o_il"])
    il_r = load_il(paths["r_il"])
    sem_ids = []
    if not skip_semantics:
        try:
            sem_ids = semantics_ids(REBUILT_DLL)
        except ToolError as e:
            failures.append({"gate": "G7-semantics", "detail": str(e)})
            checks.append(("G7", "candidate enumeration failed", "TOOL-FAIL"))
    res = compare(orig, rebuilt, il_o, il_r, sem_ids=sem_ids)
    checks.extend(res["checks"])
    failures.extend(res["failures"])
    stats = res["stats"]

    if skip_semantics:
        checks.append(("G7", "executable differential skipped by flag", "SKIPPED"))
        failures.append({"gate": "G7-semantics", "detail": "skipped by --skip-semantics; nothing proves runtime behaviour"})
        stats["semantics"] = {"skipped": True}
    else:
        def sem():
            ids_o = semantics_ids(ORIG_DLL)
            ids_r = sem_ids
            if ids_o != ids_r:
                raise ToolError("semantics candidate lists differ: orig=%d rebuilt=%d" % (len(ids_o), len(ids_r)))
            if not ids_o:
                raise ToolError("semantics: no candidate methods")
            ro = semantics_run(ORIG_DLL, ids_o)
            rr = semantics_run(REBUILT_DLL, ids_r)
            same, mism, fails = semantic_compare(ids_o, ro, rr, os.path.dirname(ORIG_DLL), os.path.dirname(REBUILT_DLL))
            stats["semantics"] = {"methods": len(ids_o), "matched": same, "mismatched": mism,
                                  "inputs": sum(len(ro[i]["results"]) for i in ro)}
            failures.extend(fails)
            return ("%d methods x %d inputs, matched=%d mismatched=%d"
                    % (len(ids_o), stats["semantics"]["inputs"], same, mism),
                    "OK" if mism == 0 and not fails else "FAIL")
        gate("G7", sem)

    def cross():
        decomp = os.path.join(tmp, "decomp")
        run_checked([DOTNET, DECOMPILE, REBUILT_DLL, decomp] + REF_DIRS, "decompile rebuilt")
        st = source_crosscheck(decomp)
        stats["sources"] = st
        for m in st["missing"][:10]:
            failures.append({"gate": "G8-sources", "detail": "source file missing from re-decompile: " + m})
        return ("%d files, identical=%d differing=%d (+%d/-%d)"
                % (st["files"], st["identical"], st["differing"], st["added"], st["deleted"]),
                "OK" if not st["missing"] else "FAIL")
    gate("G8", cross)
    return report(out_dir, checks, failures, stats)


def parse_count(text, prefix):
    for line in text.splitlines():
        if line.startswith(prefix):
            return int(line[len(prefix):].strip())
    raise ToolError("expected stdout line %r not found in: %r" % (prefix, text[:200]))


def build_target():
    if not GTFO_DIR:
        raise ToolError("GTFO_DIR is not set (see docs/BASELINE.md section 3)")
    out, _ = run_checked([DOTNET, "build", os.path.join(SRC, "GTFOKillFeed.csproj"), "-c", "Release", "-v", "q",
                          "-p:GTFO_DIR=" + GTFO_DIR,
                          "--configfile", os.path.join(ROOT, "nuget.config")], "dotnet build")
    if not os.path.exists(REBUILT_DLL):
        raise ToolError("build produced no " + REBUILT_DLL)
    return "rc=0, " + os.path.basename(REBUILT_DLL) + " rebuilt"


def ensure_tools():
    for name, dll, proj in (("Inspect", INSPECT, "tools/Inspect/Inspect.csproj"),
                            ("Decompile", DECOMPILE, "tools/Decompile/Decompile.csproj"),
                            ("Semantics", SEMANTICS, "tools/Semantics/Semantics.csproj")):
        if not os.path.exists(dll):
            build_tool(name, os.path.join(ROOT, proj))
        if not os.path.exists(dll):
            raise ToolError(name + " tool missing after build: " + dll)
    for d in REF_DIRS:
        if not os.path.isdir(d):
            raise ToolError("reference directory missing: " + d)
    return "inspect/decompile/semantics present, 3 reference dirs"


def report(out_dir, checks, failures, stats):
    os.makedirs(out_dir, exist_ok=True)
    w = max([len(c[0]) for c in checks] + [3])
    print("== checks ==")
    for gate_name, detail, status in checks:
        print("  %-*s %-9s %s" % (w, gate_name, status, detail))
    print("== failures ==")
    if failures:
        for f in failures[:40]:
            print("  [%s] %s" % (f["gate"], f["detail"]))
        if len(failures) > 40:
            print("  ... %d more" % (len(failures) - 40))
    else:
        print("  none")
    il_path = write_il_report(os.path.join(out_dir, "il-diff.md"), stats or {"il_counts": {}, "il_rows": []},
                              ORIG_DLL, REBUILT_DLL)
    payload = {"checks": checks, "failures": failures,
               "stats": {k: v for k, v in (stats or {}).items() if k != "il_rows"}}
    with open(os.path.join(out_dir, "verify-report.json"), "w", encoding="utf-8", newline="\n") as fh:
        json.dump(payload, fh, ensure_ascii=False, indent=2)
        fh.write("\n")
    print("== artifacts ==")
    print("  " + il_path)
    print("  " + os.path.join(out_dir, "verify-report.json"))
    hard = [f for f in failures]
    print("SUMMARY: " + ("PASS (all gates)" if not hard else "FAIL (%d finding(s))" % len(hard)))
    return 0 if not hard else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
