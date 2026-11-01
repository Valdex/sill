#!/usr/bin/env python3
"""Copies one target's Swift files into the stage, changed only where Linux's compiler cannot follow a
Mac build (README.md lists each change). Every change keeps lines and columns where they were, so an
error's position in the stage is its position in Sources/.

    stage.py SRC_DIR STAGE_DIR

  - `#if os(macOS)` is true and `#if os(iOS)` false: the target is macOS, the compiler Linux's.
  - `import IOKit.pwr_mgt`, `import CoreImage.CIFilterBuiltins`: the stub module whole (a Swift stub
    has no submodules).
  - `@objc`, `@objc(name)` and `@objcMembers` become blanks: Linux's Swift has no Objective-C
    interoperability (with -enable-objc-interop its standard library cannot convert `&x` to a
    pointer, which the host does everywhere).
  - `#selector(X)` becomes `_selector(X)`, a function in the FoundationDarwin stub taking X as an
    autoclosure, so X must still name a method (or, after `getter:`/`setter:`, a property) that
    exists; that it is @objc is checked by run.sh's own look (selectors.py), not by the compiler.
"""
import os
import re
import sys

src, dst = sys.argv[1], sys.argv[2]
os.makedirs(dst, exist_ok=True)

def blank(m):
    return " " * len(m.group(0))

for name in sorted(os.listdir(src)):
    if not name.endswith(".swift"):
        continue
    out = []
    with open(os.path.join(src, name), encoding="utf-8") as f:
        for line in f:
            if re.match(r"\s*#(if|elseif)\b", line):
                line = line.replace("os(macOS)", "true     ").replace("os(iOS)", "false  ")
            line = re.sub(r"^import (IOKit|CoreImage)\.([A-Za-z_]+)", lambda m: "import " + m.group(1) + " " * (len(m.group(2)) + 1), line)
            # Outside string literals and comments only: a crude split on the first // or quote is
            # enough for the host's style (attributes start declarations).
            code, sep, rest = line.partition("//")
            code = re.sub(r"@objcMembers\b|@objc\([A-Za-z0-9_:]*\)|@objc\b", blank, code)
            code = re.sub(r"#selector\((\s*)(getter|setter):", lambda m: "_selector(" + m.group(1) + " " * (len(m.group(2)) + 1), code)
            code = code.replace("#selector(", "_selector(")
            out.append(code + sep + rest)
    with open(os.path.join(dst, name), "w", encoding="utf-8") as f:
        f.writelines(out)
