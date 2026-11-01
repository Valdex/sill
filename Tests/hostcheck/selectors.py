#!/usr/bin/env python3
"""What the compiler checks of #selector on a Mac and not here (stage.py takes @objc apart): a
#selector naming a method this target declares names one declared @objc (or `override`, which
overrides an Objective-C method in these targets, or @IBAction). Methods of the frameworks
(NSApplication.terminate(_:)) are @objc already.

    selectors.py SRC_DIR    prints `file:line:col: error: …` for each one that is not; exits 1 then
"""
import os
import re
import sys

src = sys.argv[1]
files = {n: open(os.path.join(src, n), encoding="utf-8").read().splitlines()
         for n in sorted(os.listdir(src)) if n.endswith(".swift")}

declared = {}   # name -> [(file, line, objc)]
for name, lines in files.items():
    for i, line in enumerate(lines):
        m = re.search(r"\bfunc\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(", line)
        if not m:
            continue
        before = line[:m.start()] + " " + (lines[i - 1] if i > 0 else "")
        objc = bool(re.search(r"@objc\b|@IBAction\b|\boverride\b", before))
        declared.setdefault(m.group(1), []).append((name, i + 1, objc))

types = set()
for lines in files.values():
    for line in lines:
        for m in re.finditer(r"\b(?:class|struct|enum|actor|extension)\s+([A-Za-z_][A-Za-z0-9_]*)", line):
            types.add(m.group(1))

problems = 0
for name, lines in files.items():
    for i, line in enumerate(lines):
        for m in re.finditer(r"#selector\(\s*(?:(?:getter|setter):\s*)?((?:[A-Za-z_][A-Za-z0-9_]*\.)*)([A-Za-z_][A-Za-z0-9_]*)", line):
            method, qualifier = m.group(2), m.group(1).rstrip(".")
            if qualifier and qualifier.split(".")[-1] not in types:
                continue   # a framework's method (NSText.copy(_:)): @objc there
            decls = declared.get(method)
            if decls and not any(objc for _, _, objc in decls):
                where = ", ".join(f"{f}:{l}" for f, l, _ in decls)
                print(f"{os.path.join(src, name)}:{i + 1}:{m.start() + 1}: error: argument of '#selector' refers to "
                      f"'{method}', declared without @objc ({where})")
                problems += 1
sys.exit(1 if problems else 0)
