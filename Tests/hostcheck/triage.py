#!/usr/bin/env python3
"""Sorts one target's errors into known stub limitations (known.txt) and the rest.

    triage.py LOG KNOWN

known.txt: one limitation a line, `COUNT<TAB>FILE<TAB>TEXT` (# comments): up to COUNT errors in FILE
whose message contains TEXT are the stub's, not the code's. Prints the other errors, then one line
`<other> <known>`; exits 1 when any other error is left.
"""
import re
import sys

log, known_path = sys.argv[1], sys.argv[2]
known = []
for line in open(known_path, encoding="utf-8"):
    line = line.rstrip("\n")
    if not line.strip() or line.lstrip().startswith("#"):
        continue
    count, path, text = line.split("\t", 2)
    known.append([int(count), path, text])

other, matched = [], 0
for line in open(log, encoding="utf-8"):
    m = re.match(r"(\S+?):\d+:\d+: error: (.*)", line.rstrip("\n"))
    if not m:
        continue
    path, message = m.groups()
    for k in known:
        if k[0] > 0 and path == k[1] and k[2] in message:
            k[0] -= 1
            matched += 1
            break
    else:
        other.append(line.rstrip("\n"))
for o in other:
    print(o)
print(f"{len(other)} {matched}")
sys.exit(1 if other else 0)
