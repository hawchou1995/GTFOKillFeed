#!/usr/bin/env python3
"""Reject local absolute paths, secrets and leaked debug metadata in release artifacts.

A text-only grep is not sufficient: a managed PE image carries a CodeView debug record
whose NUL-terminated payload is the PDB path recorded at compile time. When the build
runs on a contributor machine that payload is an absolute local path, so the DLL itself
leaks the path even though every text file is clean. This module parses the PE debug
directory (IMAGE_DEBUG_TYPE_CODEVIEW, RSDS/NB10) and also scans the raw bytes, so the
same leak is caught whether it lives in a binary or in a document.

Surfaces: every decompressed ZIP member, single files, the tracked tree of a repository,
and every blob plus author/committer identity in a git history.

Exit code is 0 only when no finding is reported.

Usage:
  python tools/privacy_scan.py --zip dist/KillFeed.zip
  python tools/privacy_scan.py --dir .
  python tools/privacy_scan.py --history .
  python tools/privacy_scan.py --file path/to/GTFOKillFeed.dll
"""
import argparse
import json
import os
import re
import subprocess
import sys
import zipfile

# Path patterns are assembled from fragments so that this file never matches itself
# when the repository tree or its history is scanned. Backslashes are written as the
# regex escape x5c so the pattern survives any shell/heredoc transport unchanged.
DRIVE_ASCII = re.compile(rb"(?<![A-Za-z0-9])[A-Za-z]:[\x5c/][A-Za-z0-9_.\-]{3,}")
DRIVE_UTF16 = re.compile(rb"(?<![A-Za-z0-9]\x00)[A-Za-z]\x00:\x00[\x5c/]\x00(?:[A-Za-z0-9_.\-]\x00){3,}")
UNIX_USER = re.compile(re.escape(b"/Use" + b"rs/") + b"|" + re.escape(b"/ho" + b"me/"))
LOCAL_AUTHOR = re.compile(b"|".join([b"ollama-" + b"subagent", b"baseline" + b"@local", b"@" + b"localhost"]))
PRIVATE_EMAIL = re.compile(rb"@(?:qq|163|126|gmail|outlook|hotmail)\.")
GITHUB_TOKEN = re.compile(rb"gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}")
AWS_KEY = re.compile(rb"AKIA[0-9A-Z]{16}")
PRIVATE_KEY = re.compile(rb"BEGIN (?:RSA |OPENSSH |EC )?PRIVATE KEY")
SECRET_ASSIGN = re.compile(rb"(?i)(?:password|passwd|secret|api[_-]?key|access[_-]?token)\s*[=:]\s*[^\s<]{6,}")
BYTE_PATTERNS = [
    ("drive-path", DRIVE_ASCII),
    ("drive-path-utf16", DRIVE_UTF16),
    ("users-path", UNIX_USER),
    ("local-author", LOCAL_AUTHOR),
    ("private-email", PRIVATE_EMAIL),
    ("github-token", GITHUB_TOKEN),
    ("aws-key", AWS_KEY),
    ("private-key", PRIVATE_KEY),
    ("secret-assignment", SECRET_ASSIGN),
]
PRINTABLE = set(range(0x09, 0x0E)) | set(range(0x20, 0x7F))

# Not every drive-letter literal is a leak. The original 1.2.16 DLL already contains
# exactly one such literal: a configuration description that shows an example value.
# It names no contributor machine and predates this repository, so it is exempt; any
# other drive path - including a longer path that merely starts with these bytes - is
# still reported. See docs/PRIVACY.md for the evidence.
BS = bytes([0x5C])
KNOWN_EXAMPLE_PATHS = [
    b"C:" + BS + b"Pictures" + BS + b"mymark.png",
]
KNOWN_EXAMPLE_PATHS += [b"".join(bytes([c, 0]) for c in ex) for ex in KNOWN_EXAMPLE_PATHS[:1]]
PATH_CHARS = set(b"ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_.-/") | {0x3A, 0x5C}


def _u16(data, off):
    if off < 0 or off + 2 > len(data):
        return None
    return int.from_bytes(data[off:off + 2], "little")


def _u32(data, off):
    if off < 0 or off + 4 > len(data):
        return None
    return int.from_bytes(data[off:off + 4], "little")


def _cstr(data, off, limit):
    end = data.find(b"\x00", off, off + max(limit, 0))
    if end < 0:
        end = min(len(data), off + 260)
    return data[off:end].decode("utf-8", "replace")


def _pe_offset(data, lfanew, optsz, nsec, rva):
    """Map a PE RVA to a file offset using the section table."""
    if not rva:
        return None
    sec = lfanew + 24 + optsz
    for i in range(nsec):
        b = sec + 40 * i
        vsize = _u32(data, b + 8) or 0
        addr = _u32(data, b + 12) or 0
        rawsize = _u32(data, b + 16) or 0
        rawptr = _u32(data, b + 20) or 0
        if addr <= rva < addr + max(vsize, rawsize):
            off = rawptr + (rva - addr)
            return off if off < len(data) else None
    return rva if rva < len(data) else None


def pe_codeview_records(data):
    """Return CodeView debug records as [{"signature", "path", "offset"}]."""
    out = []
    if len(data) < 0x40 or data[:2] != b"MZ":
        return out
    lfanew = _u32(data, 0x3C)
    if lfanew is None or data[lfanew:lfanew + 4] != b"PE\x00\x00":
        return out
    coff = lfanew + 4
    nsec = _u16(data, coff + 2) or 0
    optsz = _u16(data, coff + 16) or 0
    opt = coff + 20
    magic = _u16(data, opt)
    if magic == 0x10B:
        dirs = opt + 96
    elif magic == 0x20B:
        dirs = opt + 112
    else:
        return out
    nrva = _u32(data, dirs - 4)
    if nrva is None or nrva < 7 or optsz < (dirs - opt) + 8:
        return out
    rva = _u32(data, dirs + 6 * 8)
    size = _u32(data, dirs + 6 * 8 + 4)
    if not rva or not size:
        return out
    base = _pe_offset(data, lfanew, optsz, nsec, rva)
    if base is None:
        return out
    for i in range(min(size // 28, 64)):
        ent = base + i * 28
        typ = _u32(data, ent + 12)
        dsize = _u32(data, ent + 16)
        praw = _u32(data, ent + 24)
        if typ != 2 or not dsize:
            continue
        off = praw if praw and praw + dsize <= len(data) else _pe_offset(
            data, lfanew, optsz, nsec, _u32(data, ent + 20) or 0)
        if off is None or off + 4 > len(data):
            continue
        sig = data[off:off + 4]
        if sig == b"RSDS":
            path = _cstr(data, off + 24, dsize)
        elif sig == b"NB10":
            path = _cstr(data, off + 16, dsize)
        else:
            out.append({"signature": sig.decode("latin-1", "replace"), "path": None, "offset": off})
            continue
        out.append({"signature": sig.decode("ascii"), "path": path, "offset": off})
    return out


def is_absolute_local(text):
    """True for a Windows drive-absolute path or a POSIX-absolute build path."""
    if not text:
        return False
    if len(text) >= 3 and text[0].isalpha() and text[1] == ":" and ord(text[2]) in (0x5C, 0x2F):
        return True
    return text.startswith("/")


def _snip(raw):
    if isinstance(raw, bytes):
        raw = raw.decode("utf-8", "replace")
    return "".join(c if c in "\t\r\n" or (ord(c) >= 0x20 and ord(c) != 0x7F) else "." for c in raw)[:96]


def _extract_path(data, start, utf16=False):
    """Return the complete candidate path starting at start, for allowlist comparison."""
    if utf16:
        out, i = bytearray(), start
        while i + 1 < len(data) and data[i] in PATH_CHARS and data[i + 1] == 0:
            out += data[i:i + 2]
            i += 2
        return bytes(out)
    i = start
    while i < len(data) and data[i] in PATH_CHARS:
        i += 1
    return data[start:i]


def _known_example_path(data, start, utf16):
    candidate = _extract_path(data, start, utf16)
    return any(candidate == ex for ex in KNOWN_EXAMPLE_PATHS)


def _string_context(data, start, end):
    """Reject a binary match that is not surrounded by printable bytes."""
    before = data[start - 1] if start else 0x20
    after = data[end] if end < len(data) else 0x20
    # 0x00 is a string boundary: a CodeView path follows the four-byte age field,
    # so the byte before it is NUL even though the path itself is a normal string.
    return (before in PRINTABLE or before == 0) and after in PRINTABLE


def scan_blob(data, label, limit=32):
    """Scan one blob of bytes; returns a list of findings."""
    findings = []

    def add(kind, offset, snippet):
        if len(findings) < limit:
            findings.append({"label": label, "kind": kind, "offset": offset, "snippet": _snip(snippet)})

    for kind, pat in BYTE_PATTERNS:
        utf16 = kind.endswith("utf16")
        for m in pat.finditer(data):
            if kind.startswith("drive-path"):
                if not _string_context(data, m.start(), m.end()):
                    continue
                if _known_example_path(data, m.start(), utf16):
                    continue
            add(kind, m.start(), data[max(0, m.start() - 4):m.end() + 40])
    for rec in pe_codeview_records(data):
        if rec["path"] is None:
            add("codeview-unparsed", rec["offset"], rec["signature"])
        elif is_absolute_local(rec["path"]):
            add("codeview-pdb-absolute", rec["offset"], rec["path"])
    return findings


def scan_zip(path):
    findings = []
    with zipfile.ZipFile(path) as zf:
        names = zf.namelist()
        for name in names:
            findings.extend(scan_blob(zf.read(name), name))
    return len(names), findings


def scan_file(path):
    with open(path, "rb") as fh:
        return scan_blob(fh.read(), os.path.basename(path))


def scan_tree(root):
    try:
        raw = subprocess.check_output(["git", "-C", root, "ls-files", "-z"])
        names = [n for n in raw.decode("utf-8", "replace").split("\x00") if n]
    except (OSError, subprocess.CalledProcessError):
        names = []
        for base, dirs, files in os.walk(root):
            dirs[:] = [d for d in dirs if d not in (".git", "bin", "obj", "__pycache__")]
            for f in files:
                names.append(os.path.relpath(os.path.join(base, f), root).replace(os.sep, "/"))
    findings = []
    for rel in names:
        with open(os.path.join(root, rel), "rb") as fh:
            findings.extend(scan_blob(fh.read(), rel))
    return len(names), findings


def _git(root, *args):
    return subprocess.check_output(["git", "-C", root] + list(args))


def git_blobs(root):
    """Return (sha, size, path) for every blob reachable from any ref."""
    lines = _git(root, "rev-list", "--objects", "--all").decode("utf-8", "replace").splitlines()
    names, order = {}, []
    for line in lines:
        parts = line.split(" ", 1)
        sha = parts[0]
        if sha not in names:
            names[sha] = parts[1] if len(parts) > 1 else ""
            order.append(sha)
    p = subprocess.run(["git", "-C", root, "cat-file", "--batch-check"],
                       input="\n".join(order).encode(), capture_output=True)
    blobs = []
    for line in p.stdout.decode("utf-8", "replace").splitlines():
        parts = line.split()
        if len(parts) == 3 and parts[1] == "blob":
            blobs.append((parts[0], int(parts[2]), names.get(parts[0], "")))
    return blobs


def read_git_blobs(root, blobs):
    shas = [b[0] for b in blobs]
    p = subprocess.run(["git", "-C", root, "cat-file", "--batch"],
                       input="\n".join(shas).encode(), capture_output=True)
    out, pos, data = {}, 0, p.stdout
    while pos < len(data):
        nl = data.find(b"\n", pos)
        if nl < 0:
            break
        header = data[pos:nl].decode("utf-8", "replace").split()
        pos = nl + 1
        if len(header) < 3 or header[1] != "blob":
            continue
        size = int(header[2])
        out[header[0]] = data[pos:pos + size]
        pos += size + 1
    return out


def scan_history(root):
    blobs = git_blobs(root)
    contents = read_git_blobs(root, blobs)
    commits = _git(root, "rev-list", "--all").decode().split()
    findings = []
    for sha, _size, path in blobs:
        findings.extend(scan_blob(contents.get(sha, b""), "%s:%s" % (sha[:12], path)))
    meta = _git(root, "log", "--all", "--format=%an%x00%ae%x00%cn%x00%ce%x00%B%x00%x1e")
    for chunk in meta.split(b"\x1e"):
        if chunk.strip():
            findings.extend(scan_blob(chunk, "commit-metadata"))
    return len(commits), len(blobs), findings


def main(argv):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    src = ap.add_mutually_exclusive_group(required=True)
    src.add_argument("--zip", help="release ZIP to scan")
    src.add_argument("--file", help="single file to scan")
    src.add_argument("--dir", help="repository checkout; scans git ls-files")
    src.add_argument("--history", help="git repository; scans every blob and identity")
    ap.add_argument("--json", help="also write findings as JSON to this path")
    args = ap.parse_args(argv)

    findings = []
    if args.zip:
        count, findings = scan_zip(args.zip)
        scope = "zip=%s entries=%d" % (os.path.abspath(args.zip), count)
    elif args.file:
        findings = scan_file(args.file)
        scope = "file=%s" % os.path.abspath(args.file)
    elif args.dir:
        count, findings = scan_tree(args.dir)
        scope = "tree=%s files=%d" % (os.path.abspath(args.dir), count)
    else:
        commits, blobs, findings = scan_history(args.history)
        scope = "history=%s commits=%d blobs=%d" % (os.path.abspath(args.history), commits, blobs)

    print(scope)
    print("findings=%d" % len(findings))
    for f in findings:
        print("FINDING %s kind=%s offset=%d snippet=%s" % (f["label"], f["kind"], f["offset"], f["snippet"]))
    if args.json:
        os.makedirs(os.path.dirname(os.path.abspath(args.json)) or ".", exist_ok=True)
        with open(args.json, "w", encoding="utf-8", newline="\n") as fh:
            json.dump({"scope": scope, "findings": findings}, fh, ensure_ascii=False, indent=2)
            fh.write("\n")
    print("SUMMARY: %s" % ("FAIL" if findings else "PASS"))
    return 1 if findings else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
