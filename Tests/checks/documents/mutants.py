"""Mutants of opening documents (Sources/SillHost/DocumentPolicy.swift): each must compile with the check and
make it fail. usage: mutants.py ROOT"""
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
import module

P = "Sources/SillHost/DocumentPolicy.swift"
MUTANTS = [
    ("no length limit", P, "guard path.utf8.count <= maxPathBytes else { return .tooLong }", ""),
    ("the length in characters", P, "guard path.utf8.count <= maxPathBytes else", "guard path.count <= maxPathBytes else"),
    ("control characters taken", P, "guard !path.unicodeScalars.contains(where: { $0.value < 0x20 || $0.value == 0x7F }) else { return .control }", ""),
    ("DEL taken", P, "{ $0.value < 0x20 || $0.value == 0x7F }) else { return .control }", "{ $0.value < 0x20 }) else { return .control }"),
    ("relative paths taken", P, "guard path.hasPrefix(\"/\") else { return .notAbsolute }", ""),
    ("`..` taken", P, "$0.isEmpty || $0 == \".\" || $0 == \"..\" })", "$0.isEmpty || $0 == \".\" })"),
    ("`.` taken", P, "$0.isEmpty || $0 == \".\" || $0 == \"..\" })", "$0.isEmpty || $0 == \"..\" })"),
    ("empty parts taken", P, "$0.isEmpty || $0 == \".\" || $0 == \"..\" })", "$0 == \".\" || $0 == \"..\" })"),
    ("no trailing slash", P, "let inner = parts.last == \"\" ? parts.dropLast() : parts[...]", "let inner = parts[...]"),
    ("a missing file opened", P, "case .missing: return .missing", "case .missing: return nil"),
    ("a device opened", P, "case .other: return .notFileOrFolder", "case .other: return nil"),
    ("unreadable opened", P, "case .file, .folder: return readable ? nil : .unreadable", "case .file, .folder: return nil"),
    ("no count limit", P, "guard i < maxDocuments else", "guard i < Int.max else"),
    ("opened twice", P, "guard seen.insert(path).inserted else { continue }", "seen.insert(path)"),
    ("a bad shape asked of the disk", P, "if let bad = shape(path) { plan.refused.append(Refused(path: path, why: bad)); continue }", ""),
    ("an empty bundle ID as an app", P, "var plan = Plan(app: bundleID.isEmpty ? nil : bundleID)", "var plan = Plan(app: bundleID)"),
    ("bundle IDs with spaces", P, "|| $0 == \".\" || $0 == \"-\" || $0 == \"_\") }", "|| $0 == \".\" || $0 == \"-\" || $0 == \"_\" || $0 == \" \") }"),
    ("a bundle ID from a dot", P, "guard id.utf8.count <= 255, !id.hasPrefix(\".\") else { return false }", "guard id.utf8.count <= 255 else { return false }"),
    ("the whole path in the log", P, "let last = path.split(separator: \"/\").last.map(String.init) ?? path", "let last = path"),
    ("a newline in the log", P, "let clean = String(last.unicodeScalars.filter { $0.value >= 0x20 && $0.value != 0x7F }.map(Character.init))", "let clean = last"),
]
module.mutate("documents", MUTANTS, sys.argv[1])
