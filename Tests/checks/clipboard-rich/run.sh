#!/bin/bash
# The clipboard's later generations, the host's rules (Sources/SillHost/ClipboardRichPolicy.swift;
# docs/coherence-protocol.md, "Clipboard generations"): what a subscription takes (`accepts`), which of the
# Mac's pasteboard types a look reads, each generation's kind 32 within the 4 MiB cap (an image, HTML and
# RTF in 2, the files' offer in 3, `concealed`), generation 1's payload unchanged for `{}`, what of a
# device's kind 32 the pasteboard takes, and how the device's copied files are kept and pruned.
# Foundation-only files (module.txt): runs on Linux too.
#   Tests/checks/clipboard-rich/run.sh             compile and run the check
#   Tests/checks/clipboard-rich/run.sh --mutants   one-line mutants of ClipboardRichPolicy.swift; each must fail it
here="$(cd "$(dirname "$0")" && pwd)"
source "$here/../common.sh"
if [ "${1:-}" = "--mutants" ]; then run_mutants python3 "$here/mutants.py" "$root"; exit; fi
python3 "$here/../module.py" "$name" "$root" "$out/check"
