#!/bin/bash
# Opening documents on the Mac from a device, the host's rules (Sources/SillHost/DocumentPolicy.swift;
# docs/coherence-protocol.md, "Opening documents on the Mac"): which paths kind 7's `documents` opens
# (absolute, plain, files or folders that are there and readable, at most 64), with which app, and how a
# path is named in the host's line. Foundation only (module.txt): runs on Linux too.
#   Tests/checks/documents/run.sh             compile and run the check
#   Tests/checks/documents/run.sh --mutants   one-line mutants of DocumentPolicy.swift; each must fail it
here="$(cd "$(dirname "$0")" && pwd)"
source "$here/../common.sh"
if [ "${1:-}" = "--mutants" ]; then run_mutants python3 "$here/mutants.py" "$root"; exit; fi
python3 "$here/../module.py" "$name" "$root" "$out/check"
