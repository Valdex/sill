import Foundation

// Opening documents on the Mac from a device (Sources/SillHost/DocumentPolicy.swift): which paths kind 7's
// `documents` opens and with which app, against a stand-in file system; then random paths against an
// oracle of the rule.
var failures = 0
var checks = 0
func check(_ ok: Bool, _ what: @autoclosure () -> String, line: Int = #line) {
    checks += 1
    if !ok { failures += 1; print("FAIL (line \(line)): \(what())") }
}
var rng = SystemRandomNumberGenerator()
typealias D = DocumentPolicy

// MARK: - Shapes

check(D.generation == 1 && D.maxDocuments == 64 && D.maxPathBytes == 1024, "documents 1, 64 at once, 1024 bytes a path")
check(D.shape("/Users/v/Documents/a.pdf") == nil && D.shape("/Volumes/Share/Report 2026.docx") == nil, "absolute and plain")
check(D.shape("/Users/v/Documents/") == nil && D.shape("/") == nil, "a folder with its trailing slash; the root")
check(D.shape("/Users/v/Docs/Ünïcödé — 日本.txt") == nil, "any letters")
check(D.shape("Users/v/a.pdf") == .notAbsolute && D.shape("") == .notAbsolute && D.shape("~/a.pdf") == .notAbsolute, "not absolute")
check(D.shape("/Users/v/../w/a.pdf") == .notPlain && D.shape("/Users/./v") == .notPlain && D.shape("/Users//v") == .notPlain
      && D.shape("/..") == .notPlain && D.shape("/Users/v/..") == .notPlain && D.shape("//") == .notPlain, "`.`, `..` or an empty part")
check(D.shape("/Users/v/..a") == nil && D.shape("/Users/v/a..") == nil && D.shape("/Users/.config") == nil, "dots inside a name are a name")
check(D.shape("/Users/v/a\nb") == .control && D.shape("/Users/v/a\u{7F}") == .control && D.shape("/Users/v/\u{0}") == .control, "control characters")
check(D.shape("/" + String(repeating: "a", count: 1023)) == nil && D.shape("/" + String(repeating: "a", count: 1024)) == .tooLong, "1024 bytes at most")
check(D.shape("/" + String(repeating: "é", count: 512)) == .tooLong, "counted in UTF-8 bytes")
check(D.bundleIDValid("") && D.bundleIDValid("com.apple.Preview") && D.bundleIDValid("com.microsoft.Word") && D.bundleIDValid("org.my-app_2"),
      "bundle IDs: none, or letters, digits, dots, dashes and underscores")
check(!D.bundleIDValid(".com.apple") && !D.bundleIDValid("com apple") && !D.bundleIDValid("com/apple") && !D.bundleIDValid("cöm.apple")
      && !D.bundleIDValid(String(repeating: "a", count: 256)) && D.bundleIDValid(String(repeating: "a", count: 255)), "anything else not")

// MARK: - Judged against the file system

check(D.judge("/a/b.pdf", kind: .file, readable: true) == nil && D.judge("/a/Folder", kind: .folder, readable: true) == nil, "a readable file or folder")
check(D.judge("/a/b.pdf", kind: .missing, readable: false) == .missing, "not there")
check(D.judge("/a/b.pdf", kind: .file, readable: false) == .unreadable && D.judge("/a/F", kind: .folder, readable: false) == .unreadable, "not readable")
check(D.judge("/dev/tty", kind: .other, readable: true) == .notFileOrFolder, "a device or a socket")
check(D.judge("a/b.pdf", kind: .file, readable: true) == .notAbsolute, "the shape first")

let fs: [String: (D.Kind, Bool)] = ["/Users/v/a.pdf": (.file, true), "/Users/v/Folder": (.folder, true), "/Users/v/secret": (.file, false),
                                     "/dev/null": (.other, true)]
var asked: [String] = []
func look(_ p: String) -> (D.Kind, Bool) { asked.append(p); return fs[p] ?? (.missing, false) }
let plan = D.plan(bundleID: "com.apple.Preview",
                  documents: ["/Users/v/a.pdf", "/Users/v/Folder", "/Users/v/secret", "/dev/null", "/Users/v/gone.txt", "relative.pdf", "/Users/v/a.pdf"],
                  look: look)
check(plan.app == "com.apple.Preview", "with that app")
check(plan.open == ["/Users/v/a.pdf", "/Users/v/Folder"], "the file and the folder, in order, each once: \(plan.open)")
check(plan.refused == [D.Refused(path: "/Users/v/secret", why: .unreadable), D.Refused(path: "/dev/null", why: .notFileOrFolder),
                       D.Refused(path: "/Users/v/gone.txt", why: .missing), D.Refused(path: "relative.pdf", why: .notAbsolute)],
      "the rest refused, each with why: \(plan.refused)")
check(asked == ["/Users/v/a.pdf", "/Users/v/Folder", "/Users/v/secret", "/dev/null", "/Users/v/gone.txt"], "the file system asked once a path, never for a bad shape: \(asked)")
check(D.plan(bundleID: "", documents: ["/Users/v/a.pdf"], look: look).app == nil, "an empty bundle ID: each with its default app")
let many = (0..<70).map { "/Users/v/f\($0).txt" }
let manyPlan = D.plan(bundleID: "", documents: many) { _ in (.file, true) }
check(manyPlan.open.count == 64 && manyPlan.refused.count == 6 && manyPlan.refused.allSatisfy { $0.why == .tooMany }, "64 at most; the rest refused")
check(D.plan(bundleID: "x", documents: []) { _ in (.file, true) } == D.Plan(app: "x"), "none: nothing")

// MARK: - The log's name

check(D.logName("/Users/v/Documents/Report.pdf") == "Report.pdf" && D.logName("/Users/v/Folder/") == "Folder", "only the last part")
check(D.logName("/Users/v/a\nforged line") == "aforged line" && D.logName("/") == "/", "nothing that forges a line")
check(D.logName("/" + String(repeating: "x", count: 100)).count == 81, "at most 80 characters and an ellipsis")

// MARK: - Random paths against an oracle

func oracleShape(_ p: String) -> D.Refusal? {
    if p.utf8.count > 1024 { return .tooLong }
    if p.unicodeScalars.contains(where: { $0.value < 32 || $0.value == 127 }) { return .control }
    if !p.hasPrefix("/") { return .notAbsolute }
    if p == "/" { return nil }
    var parts = p.components(separatedBy: "/").dropFirst()
    if parts.last == "" { parts = parts.dropLast() }
    if parts.isEmpty || parts.contains(where: { $0.isEmpty || $0 == "." || $0 == ".." }) { return .notPlain }
    return nil
}
let pieces = ["Users", "v", ".", "..", "", "a.pdf", "Ä", "..x", "x\n", "日本"]
var agree = 0
for _ in 0..<20_000 {
    var p = Bool.random(using: &rng) ? "/" : ""
    p += (0..<Int.random(in: 0...5, using: &rng)).map { _ in pieces.randomElement(using: &rng)! }.joined(separator: "/")
    if Int.random(in: 0..<5, using: &rng) == 0 { p += "/" }
    if D.shape(p) == oracleShape(p) { agree += 1 } else { check(false, "random shape \(p.debugDescription): \(String(describing: D.shape(p))) ≠ \(String(describing: oracleShape(p)))"); break }
}
check(agree == 20_000, "20,000 random paths shaped as the rule says")

print(failures == 0 ? "\(checks) checks, 0 failed" : "\(failures) FAILED of \(checks)")
exit(failures == 0 ? 0 : 1)
