#!/bin/bash
# Coherence's rules on the host (Sources/SillHost/CoherencePolicy.swift; M1b, docs/coherence-protocol.md):
# CoherenceAsk (kind 29 held to the contract), CoherenceGates (each window's focus rate, the others' rate,
# hidden), CoherenceScale (the capture's pixels a point), JoinRules (windows joining by themselves),
# CoherenceEntries (kind 45's windows and children), WindowStateReporter and SendPacer (what each kind 45
# carries, 20 a second at most), SizeLimits (the sizes a window refused), PlaceSize and PlaceLimiter (kind 46),
# CoherenceInput and ActivationGate (which input needs its window key first), IconSizing (kind 4).
# Foundation-only files (module.txt), so it runs on Linux too.
#   Tests/checks/coherence-policy/run.sh             compile and run the check
#   Tests/checks/coherence-policy/run.sh --mutants   one-line mutants of CoherencePolicy.swift; each must fail it
here="$(cd "$(dirname "$0")" && pwd)"
source "$here/../common.sh"
if [ "${1:-}" = "--mutants" ]; then run_mutants python3 "$here/mutants.py" "$root"; exit; fi
python3 "$here/../module.py" "$name" "$root" "$out/check"
