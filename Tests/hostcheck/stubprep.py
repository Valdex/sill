#!/usr/bin/env python3
"""A stub as compiled: every function with a result gets @discardableResult, as Swift imports every C
function and Objective-C method (a stub's functions stand for those), unless the stub says
`// hostcheck: swift-api` (a Swift framework: CryptoKit, Network, …), where only its top-level
functions (C functions, such as Network's sec_*) are marked.

    stubprep.py IN OUT
"""
import re
import sys

text = open(sys.argv[1], encoding="utf-8").read()
swift_api = "// hostcheck: swift-api" in text
decl = re.compile(r"(?m)^(?P<indent>[ \t]*)(?P<head>(?:@[A-Za-z]+(?:\([^)]*\))?\s+)*(?:(?:public|open|static|class|override|final|mutating|nonisolated)\s+)+func\b[^{]*)")

def returns(head):
    """Whether the declaration has a result: an arrow after its parameter list, not to Void."""
    start = head.find("(")
    if start < 0:
        return False
    depth = 0
    for i in range(start, len(head)):
        depth += {"(": 1, ")": -1}.get(head[i], 0)
        if depth == 0:
            rest = head[i + 1:]
            return re.search(r"->\s*(?!Void\b)(?!\(\))", rest) is not None
    return False

def fix(m):
    indent, head = m.group("indent"), m.group("head")
    if swift_api and indent:
        return m.group(0)
    if "@discardableResult" in head or not returns(head):
        return m.group(0)
    return indent + "@discardableResult " + head

open(sys.argv[2], "w", encoding="utf-8").write(decl.sub(fix, text))
