#!/bin/bash
# Coherence's bulk connection, the host's rules (Sources/SillHost/DataLinkPolicy.swift; docs/coherence-protocol.md,
# "The bulk connection"): the ticket (kind 48), who a `sill-data/1` connection presenting it is admitted for
# (kind 49: the same door, way, key and address as its session, one per session, never a session the Mac
# cut off), what the plain door and the connection read, how the host paces the parts it sends so the
# session's picture comes first (DataPacing, on a simulated clock), and how long a message naming
# transfers waits for their parts (TransferWait). Foundation-only files (module.txt): runs on Linux too.
#   Tests/checks/data-link/run.sh             compile and run the check
#   Tests/checks/data-link/run.sh --mutants   one-line mutants of DataLinkPolicy.swift; each must fail it
here="$(cd "$(dirname "$0")" && pwd)"
source "$here/../common.sh"
if [ "${1:-}" = "--mutants" ]; then run_mutants python3 "$here/mutants.py" "$root"; exit; fi
python3 "$here/../module.py" "$name" "$root" "$out/check"
