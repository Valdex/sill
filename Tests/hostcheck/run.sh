#!/bin/bash
# The Mac host type-checked on Linux against stub frameworks (README.md says what that proves and
# what it does not). No macOS SDK is needed: every Apple framework the host imports is a Swift stub
# module in stubs/, compiled here first, and the host's own modules are then type-checked as SwiftPM
# would build them: StreamProtocol, SillHostCore (Sources/SillHost, `package` access, -package-name
# sill), SillHostCLI and SillMenuBar.
#
#   Tests/hostcheck/run.sh              every target; exits with the number of targets that failed
#   Tests/hostcheck/run.sh core cli     only those (protocol, core, cli, menubar; each needs the ones
#                                       before it, which are built quietly when asked for later ones)
#   SWIFTC=/path/to/swiftc Tests/hostcheck/run.sh
#
# Errors are printed with the paths under Sources/. Output goes to .build/hostcheck.
set -uo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
out="$root/.build/hostcheck"
mods="$out/mods"
stage="$out/src"
SWIFTC="${SWIFTC:-swiftc}"
command -v "$SWIFTC" >/dev/null || { [ -x /opt/swift/usr/bin/swiftc ] && SWIFTC=/opt/swift/usr/bin/swiftc; }
mkdir -p "$mods" "$stage"
cd "$root"

# Swift 5 mode, as Package.swift's tools version. No -enable-objc-interop: with it, Linux's standard
# library cannot pass `&x` as a pointer, so stage.py takes @objc and #selector apart instead.
common=(-swift-version 5 -I "$mods" -I "$here/stubs/c" -module-cache-path "$out/cache"
        -Xfrontend -import-module -Xfrontend FoundationDarwin)

# The stubs in dependency order. FoundationDarwin (what Darwin's Foundation has and Linux's lacks:
# CoreFoundation's types and functions, ObjectiveC's) is imported into every file, as Darwin's
# Foundation re-exports CoreFoundation, so every stub after it and every host file sees it.
stubs=(ObjectiveC Darwin FoundationDarwin CoreGraphics IOSurface CoreVideo CoreAudio CoreMedia
       AudioToolbox VideoToolbox ImageIO CoreImage UniformTypeIdentifiers QuartzCore Security
       CryptoKit CommonCrypto Network dnssd SystemConfiguration IOKit ApplicationServices
       ScreenCaptureKit AppKit ServiceManagement Combine SwiftUI)

build_stubs() {
    local m prev_fail=0
    # A changed harness rebuilds every stub.
    if [ ! -e "$mods/.stamp" ] || [ "$here/run.sh" -nt "$mods/.stamp" ] || [ "$here/stubprep.py" -nt "$mods/.stamp" ] \
            || [ -n "$(find "$here/stubs/c" -newer "$mods/.stamp" 2>/dev/null)" ]; then
        prev_fail=1
    fi
    for m in "${stubs[@]}"; do
        local src="$here/stubs/$m.swift"
        [ -f "$src" ] || continue
        local flags=(-swift-version 5 -I "$mods" -I "$here/stubs/c" -module-cache-path "$out/cache")
        # ObjectiveC, Darwin and FoundationDarwin come before the implicit import exists.
        case "$m" in ObjectiveC|Darwin|FoundationDarwin) ;; *) flags=("${common[@]}");; esac
        # A stub's functions stand for C functions and Objective-C methods, whose results Swift lets
        # go unused (stubprep.py marks them so).
        local src_compiled="$out/stub-$m.swift"
        python3 "$here/stubprep.py" "$src" "$src_compiled"
        if [ "$src" -nt "$mods/$m.swiftmodule" ] || [ "$prev_fail" = 1 ] || [ "${HOSTCHECK_REBUILD:-}" = 1 ]; then
            if ! "$SWIFTC" -emit-module -parse-as-library -module-name "$m" "${flags[@]}" \
                    -emit-module-path "$mods/$m.swiftmodule" "$src_compiled" 2>"$out/stub-$m.log"; then
                rm -f "$mods/$m.swiftmodule"   # the driver may have written it before its bodies failed
                echo "stub $m does not compile:" >&2; sed "s#$out/stub-$m.swift#stubs/$m.swift#g; s#$here/##g" "$out/stub-$m.log" >&2
                return 1
            fi
            prev_fail=1   # a changed stub rebuilds every stub after it
        fi
    done
    touch "$mods/.stamp"
}

# Copies a target's sources into the stage, changed only where Linux cannot follow a Mac build
# (stage.py says how; lines and columns never move).
stage_target() {
    local name="$1" dir="$2"
    rm -rf "${stage:?}/$name"
    python3 "$here/stage.py" "$dir" "$stage/$name"
}

# Type-checks one target and, when later targets need it, emits its module. Errors name Sources/.
check_target() {
    local name="$1" dir="$2" kind="$3" quiet="$4"; shift 4
    stage_target "$name" "$dir"
    local files=("$stage/$name"/*.swift) log="$out/$name.log" status=0
    local lib=()
    [ "$kind" = lib ] && lib=(-parse-as-library)
    "$SWIFTC" -typecheck -continue-building-after-errors -j "$(nproc)" "${lib[@]}" -module-name "$name" "${common[@]}" "$@" "${files[@]}" >"$log" 2>&1 || status=$?
    python3 "$here/selectors.py" "$dir" >>"$log" 2>&1 || status=$?   # what @objc's removal hides
    sed -i "s#$stage/$name/#$dir/#g; s#$root/##g" "$log"
    local errors warnings report counts other known
    warnings=$(grep -c ': warning: ' "$log" || true)
    report=$(python3 "$here/triage.py" "$log" "$here/known.txt" || true)
    counts=$(tail -n 1 <<<"$report"); other=${counts% *}; known=${counts#* }
    errors=$other
    if [ "$quiet" != 1 ]; then
        sed '$d' <<<"$report"
        echo "$name: ${#files[@]} files, $other errors, $known known stub limitations (known.txt), $warnings warnings (full log: ${log#$root/})"
    fi
    if [ "$status" != 0 ] && [ "$other" = 0 ] && [ "$known" = 0 ]; then
        errors=1; [ "$quiet" != 1 ] && echo "$name: the compiler failed without an error line; see the log" >&2
    fi
    status=0
    if [ "$kind" = lib ]; then
        "$SWIFTC" -emit-module -parse-as-library -module-name "$name" "${common[@]}" "$@" \
            -Xfrontend -experimental-skip-all-function-bodies -Xfrontend -experimental-allow-module-with-compiler-errors \
            -emit-module-path "$mods/$name.swiftmodule" "${files[@]}" >/dev/null 2>&1 || true
    fi
    [ "$status" = 0 ] && [ "$errors" = 0 ]
}

wanted=("$@"); [ ${#wanted[@]} = 0 ] && wanted=(protocol core cli menubar)
want() { local w; for w in "${wanted[@]}"; do [ "$w" = "$1" ] && return 0; done; return 1; }
last=protocol
for t in protocol core cli menubar; do want "$t" && last="$t"; done

build_stubs || exit 99
failed=0
run() {   # run NAME: checked loudly when asked for, quietly when a later target needs it
    local t="$1"; shift
    local quiet=1; want "$t" && quiet=0
    check_target "$1" "$2" "$3" "$quiet" "${@:4}" || { [ "$quiet" = 0 ] && failed=$((failed + 1)); }
}
run protocol StreamProtocol Sources/StreamProtocol lib -package-name sill
case "$last" in protocol) exit $failed;; esac
run core SillHostCore Sources/SillHost lib -package-name sill
case "$last" in core) exit $failed;; esac
want cli && run cli SillHostCLI Sources/SillHostCLI exe -package-name sill
want menubar && run menubar SillMenuBar Sources/SillMenuBar exe -package-name sill
exit $failed
