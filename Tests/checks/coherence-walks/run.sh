#!/bin/bash
# Coherence's semantics across the two languages (docs/coherence-protocol.md; main.swift has the detail):
# 1. kind 45: seeded walks of a simulated Mac through the host's real CoherenceEntries and WindowStateReporter,
#    each message written with the set a receiver holds after it by the contract; the Windows client's real
#    WindowSet replays them (WindowsClient/Sill.Checks, group "swift-walks", reads the fixture below).
# 2. kind 29: the Windows client's real SideStreamPlan, driven by Sill.Checks, wrote each kind 29 with what it
#    means (kind29-walks.jsonl here); the host's CoherenceAsk, CoherenceGates and SidePlan read each.
# Foundation-only files (module.txt), so it runs on Linux too.
#   Tests/checks/coherence-walks/run.sh             compile, run, and compare the kind 45 walks with the fixture
#   Tests/checks/coherence-walks/run.sh --write     write the kind 45 fixture again (then run Sill.Checks)
#   Tests/checks/coherence-walks/run.sh --mutants   mutants of both sides; each must fail a check (needs dotnet)
# The kind 29 fixture is the Windows client's to write:
#   dotnet run --project WindowsClient/Sill.Checks -- --write-kind29 Tests/checks/coherence-walks/kind29-walks.jsonl
here="$(cd "$(dirname "$0")" && pwd)"
source "$here/../common.sh"
if [ "${1:-}" = "--mutants" ]; then run_mutants python3 "$here/mutants.py" "$root"; exit; fi
fixture=WindowsClient/Sill.Checks/Fixtures/swift-walks.jsonl
export WALKS_OUT="$out/swift-walks.jsonl" WALKS_KIND29="$here/kind29-walks.jsonl"
if [ "${1:-}" = "--write" ]; then
    unset WALKS_KIND29   # only the kind 45 walks: the kind 29 file is the Windows client's to write
    python3 "$here/../module.py" "$name" "$root" "$out/check"
    cp "$WALKS_OUT" "$fixture"; echo "wrote $fixture ($(wc -l < "$fixture") lines, $(wc -c < "$fixture") bytes)"; exit 0
fi
python3 "$here/../module.py" "$name" "$root" "$out/check"
if cmp -s "$WALKS_OUT" "$fixture"; then echo "coherence-walks: the kind 45 walks match $fixture ($(wc -l < "$fixture") lines)"
else echo "coherence-walks: FAIL, the host's kind 45 walks differ from $fixture (run with --write, then Sill.Checks)"; diff "$WALKS_OUT" "$fixture" | head -5; exit 1; fi
