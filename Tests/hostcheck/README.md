# The Mac host, type-checked on Linux

`Tests/hostcheck/run.sh` compiles the Mac host's four targets as SwiftPM builds them on a Mac
(StreamProtocol; SillHostCore, the folder `Sources/SillHost`, with `package` access and
`-package-name sill`; SillHostCLI; SillMenuBar), with Linux's Swift (6.4 here) in Swift 5 mode, against
stub modules of every Apple framework they import. No macOS SDK is involved: it is a way to find the
mistakes in code written without a Mac before a Mac sees it.

```
Tests/hostcheck/run.sh              # every target (about 40 s); exits with the number that failed
Tests/hostcheck/run.sh core         # one target and, quietly, those it needs (protocol, core, cli, menubar)
SWIFTC=/opt/swift/usr/bin/swiftc Tests/hostcheck/run.sh
```

Each target is one whole-module `swiftc -emit-sil` job, so it gets the type checker and SIL's mandatory
diagnostics: definite initialization, a missing return, a C function pointer formed from a closure that
captures, exclusivity (`-typecheck` alone never runs those). Errors are printed with their paths under
`Sources/`; the logs are `.build/hostcheck/<target>.log`.

## What it proves

That the host's own code is consistent with itself and with the framework API it declares it uses:
every name exists, every call has the labels, types, optionality, `throws` and `async` the stubs give it,
every `await` and `try` is there, actor isolation within the host's code (its own `@MainActor`s) holds,
`package` and `internal` access across the targets holds, every `#selector` names an existing method,
which is `@objc` when the target declares it, and every constant is initialized before use.

The stubs are written from the frameworks' real signatures (as Swift imports the macOS 14+ SDK), never
from what the host happens to call, and they are held to compiled code: upstream main at `e4d3034`, which
CI built on macOS, type-checks against them with no error, and so does upstream PR #66's host (branch
`pr66`), but for its AVFoundation file (not stubbed: the port left it out) and the limitations below. A
stub is never loosened to silence an error (no `Any` parameters, no catch-all overloads): when code that
compiled on a Mac fails here, the stub is wrong and is fixed; when new code fails, the code is.

## What it does not prove

- **The real SDK.** The stubs are hand-written. A signature written wrongly the same way the new code
  uses it would pass here and fail on a Mac. Only `swift build -c release` on a Mac settles it.
- **Availability.** Linux's compiler ignores `@available(macOS …)`, so an API newer than macOS 14 used
  without `#available` is not caught.
- **Objective-C.** Linux's Swift has no Objective-C interoperability (with `-enable-objc-interop` its
  standard library cannot pass `&x` as a pointer). `stage.py` blanks `@objc` and turns `#selector(X)` into
  a call that still requires X to exist; `selectors.py` checks that a selector naming a method of the target
  names an `@objc` one. Not checked: that an `@objc` signature is representable in Objective-C, `@objc`
  in a generic class. Objective-C optional protocol requirements are requirements with default
  implementations here, so a near-miss method name compiles silently (a warning on a Mac).
- **C callbacks with class parameters.** On a Mac CoreFoundation and Accessibility types are CF classes,
  representable in a `@convention(c)` function; here they are Swift classes, so the stubs declare those
  callback types (`AXObserverCallback`, `SCDynamicStoreCallBack`, `VTCompressionOutputCallback`) as Swift
  function types, and "a C function pointer cannot capture context" is not checked for them. C structs
  handed to C callbacks (the audio converter's) are a C module (`stubs/c`), as on a Mac.
- **Isolation of the frameworks.** AppKit's and SwiftUI's classes and protocols are
  `@preconcurrency @MainActor`, as Swift takes Objective-C's `NS_SWIFT_UI_ACTOR`: in Swift 5 mode a
  nonisolated use is no error, there as here. Sendable checking is warnings in Swift 5 mode either way.
- **Behaviour.** Nothing runs: no capture, encode, window, socket or Accessibility call.

## How it is built

- `stubs/*.swift`: one module per framework, in dependency order (`run.sh`'s `stubs` list), each
  re-exporting what the real one does (AppKit re-exports Foundation, CoreGraphics, ApplicationServices…).
  CoreFoundation's types are Foundation's classes (`CFString` is `NSString`, `CFDictionary` is
  `NSDictionary`…), which keeps toll-free bridging (`"x" as CFString`, `cf as? [String: Any]`, an
  `NSMutableData` passed where `CFMutableData` is wanted) as on a Mac. A stub that says
  `// hostcheck: swift-api` is a Swift framework (CryptoKit, Network, SwiftUI…); in the others
  `stubprep.py` marks every function with a result `@discardableResult`, as Swift imports C functions
  and Objective-C methods.
- `stubs/FoundationDarwin.swift`: what Darwin's Foundation has and Linux's lacks (CoreFoundation's
  types and functions, the Objective-C runtime's names, `OSStatus`, URLSession through
  FoundationNetworking, `DistributedNotificationCenter`, `ProcessInfo`'s activities…), imported into
  every file (`-import-module`), as `import Foundation` would on a Mac. `stubs/Darwin.swift` is Glibc
  with Darwin's extras (mach time, libproc, sysctl, the BSD `sin_len`), shadowing Glibc where their types
  differ (`SOCK_STREAM` is an `Int32` on a Mac).
- `stage.py` copies each target into `.build/hostcheck/src`, changed only where Linux cannot follow a
  Mac build: `#if os(macOS)` true, `import IOKit.pwr_mgt` and `import CoreImage.CIFilterBuiltins` as the
  whole stub, `@objc` and `#selector` as above, and `patches.txt`. Lines and columns stay where they were.
- `patches.txt`: the lines the stubs cannot follow, each patched in the stage copy only, each patch an
  exact text that must match once (a change to such a line stops the run and is looked at again):
  the five `@convention(c)` typealiases whose parameters are Linux's Swift structs (`CGSize`, `CGPoint`,
  `ObjCBool`) or the stubs' CF classes; `NSObject.init()` through a metatype (`required` only on a Mac);
  `URLSessionConfiguration.waitsForConnectivity` (read-only in Linux's FoundationNetworking);
  `NSMutableAttributedString()` (no `init()` on Linux). Each would otherwise be an error.

## Results (2026-11-01, branch `coherence`)

StreamProtocol 22 files, SillHostCore 86, SillHostCLI 1, SillMenuBar 26: no errors. The run found one
issue in the branch's new code, a warning a Mac shows too (HostClipboard's `filesArrived` held the
clipboard strongly: fixed). The remaining warnings are Linux's own (`FileManager.createFile`'s unused
result, discardable on a Mac) or older than the branch.
