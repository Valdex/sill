import AppKit
import StreamProtocol

/// Opening documents on this Mac for a device (kind 7's `documents`, Coherence M5;
/// docs/coherence-protocol.md, "Opening documents on the Mac"): the paths DocumentPolicy takes, opened
/// with the app the device named (`NSWorkspace.open(_:withApplicationAt:configuration:)`), or each with its
/// own default app when the bundle ID is empty. Every path refused gets a line naming its last part and
/// why; the paths themselves are never logged whole. The app comes forward, as a person opening a file
/// would see it. Main actor.
@MainActor
enum Documents {
    static func open(_ request: LaunchApp, device: String) {
        guard DocumentPolicy.bundleIDValid(request.bundleID) else {
            print("Documents from \(device) refused: \"\(SafeText.label(request.bundleID))\" is not an app's bundle ID.")
            return
        }
        let plan = DocumentPolicy.plan(bundleID: request.bundleID, documents: request.documents ?? [], look: look)
        for r in plan.refused { print("Documents from \(device): \(DocumentPolicy.logName(r.path)) refused: \(r.why.rawValue).") }
        guard !plan.open.isEmpty else { return }
        let urls = plan.open.map { URL(fileURLWithPath: $0) }
        let names = plan.open.map(DocumentPolicy.logName).joined(separator: ", ")
        let config = NSWorkspace.OpenConfiguration()
        config.activates = true
        if let app = plan.app {
            guard let appURL = NSWorkspace.shared.urlForApplication(withBundleIdentifier: app) else {
                print("Documents from \(device) refused: no app \(app) on this Mac.")
                return
            }
            print("Documents from \(device): opening \(names) with \(app).")
            NSWorkspace.shared.open(urls, withApplicationAt: appURL, configuration: config) { _, error in
                if let error { print("Documents from \(device): \(app) could not open them: \(error.localizedDescription)") }
            }
        } else {
            print("Documents from \(device): opening \(names), each with its own app.")
            for url in urls {
                NSWorkspace.shared.open(url, configuration: config) { _, error in
                    if let error { print("Documents from \(device): \(DocumentPolicy.logName(url.path)) could not be opened: \(error.localizedDescription)") }
                }
            }
        }
    }

    /// What the file system says of `path` (a symbolic link followed, as opening it does): a regular
    /// file, a folder (a package too), something else, or nothing; and whether this Mac's user may read it.
    nonisolated static func look(_ path: String) -> (DocumentPolicy.Kind, Bool) {
        let url = URL(fileURLWithPath: path).resolvingSymlinksInPath()
        guard FileManager.default.fileExists(atPath: url.path),
              let v = try? url.resourceValues(forKeys: [.isRegularFileKey, .isDirectoryKey]) else { return (.missing, false) }
        let kind: DocumentPolicy.Kind = v.isRegularFile == true ? .file : v.isDirectory == true ? .folder : .other
        return (kind, FileManager.default.isReadableFile(atPath: url.path))
    }
}
