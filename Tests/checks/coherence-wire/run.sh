#!/bin/bash
# The coherence wire (docs/coherence-protocol.md): kinds 29–31, 34–42 (pull request #66's) and 44's
# window, 45, 46, 48 and 49 (this fork's), the capabilities in WindowList and Hello, the clipboard's
# later generations, kind 15's activate and restore, kind 7's documents and kind 43's pcm-s16le, against
# the contract's own example payloads, with the old payloads' bytes unchanged. Compiles only
# Sources/StreamProtocol's Foundation-only files (those importing neither Network, Security, CryptoKit
# nor CommonCrypto), so it runs on Linux too.
#   Tests/checks/coherence-wire/run.sh             compile and run the check
#   Tests/checks/coherence-wire/run.sh --mutants   one-line mutants of the protocol's files; each must fail it
here="$(cd "$(dirname "$0")" && pwd)"
source "$here/../common.sh"
if [ "${1:-}" = "--mutants" ]; then run_mutants python3 "$here/mutants.py" "$root"; exit; fi
files=$(grep -L -E '^import (Network|Security|CryptoKit|CommonCrypto)' Sources/StreamProtocol/*.swift)
rm -f "$out/check"
swiftc -O $files "$here/main.swift" -o "$out/check"
"$out/check"
