#!/bin/bash
# The side streams' and Sill's desk's rules (Sources/SillHost/SideStreamPolicy.swift; pull request #66's,
# ported for coherence, docs/coherence-plan.md): SidePlan (which windows stream beside the main one, at
# what rate, to whom), SideFrameGate (which captured frames reach a window's encoder: the cap, a hidden
# window), SideSend (which side frames go to a client, the keyframe asked), DeskGeometry (the desk's
# displays: envelope, scale, memory, identities) and DropFileName (a dropped file's name on the Mac).
# Foundation-only files (module.txt), so it runs on Linux too.
#   Tests/checks/side-streams/run.sh             compile and run the check
#   Tests/checks/side-streams/run.sh --mutants   one-line mutants of SideStreamPolicy.swift; each must fail it
here="$(cd "$(dirname "$0")" && pwd)"
source "$here/../common.sh"
if [ "${1:-}" = "--mutants" ]; then run_mutants python3 "$here/mutants.py" "$root"; exit; fi
python3 "$here/../module.py" "$name" "$root" "$out/check"
