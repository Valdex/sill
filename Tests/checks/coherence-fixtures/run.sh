#!/bin/bash
# The Coherence messages as Sill for Mac's Swift types encode them, compared with the fixture the
# Windows client's checks read (WindowsClient/Sill.Checks/Fixtures/swift-wire.jsonl, group
# "swift-wire"). A change to a Swift payload that the fixture does not have yet fails here: run with
# --write to write the fixture again, then run the Windows client's checks, which must still pass.
# Foundation only: runs on macOS and on Linux with a Swift toolchain.
set -euo pipefail
cd "$(dirname "$0")/../../.."
out=.build/checks/coherence-fixtures
mkdir -p .build/checks
files=$(grep -L -E '^import (Network|CryptoKit|Security|CoreGraphics|AppKit|UIKit|ImageIO|UniformTypeIdentifiers)' Sources/StreamProtocol/*.swift)
# shellcheck disable=SC2086
swiftc -swift-version 5 -O -o "$out" $files Tests/checks/coherence-fixtures/main.swift
fixture=WindowsClient/Sill.Checks/Fixtures/swift-wire.jsonl
if [[ "${1:-}" == "--write" ]]; then "$out" > "$fixture"; echo "wrote $fixture"; exit 0; fi
if diff <("$out") "$fixture" > /dev/null; then echo "coherence-fixtures: the fixture matches the Swift types ($(wc -l < "$fixture") messages)"
else echo "coherence-fixtures: FAIL, the Swift types encode differently from $fixture (run with --write)"; diff <("$out") "$fixture" | head -20; exit 1; fi
