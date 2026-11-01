// Stub of ServiceManagement: SMAppService (macOS 13+), the login item.
import Foundation
public final class SMAppService: NSObject {
    public enum Status: Int { case notRegistered = 0, enabled = 1, requiresApproval = 2, notFound = 3 }
    public class var mainApp: SMAppService { fatalError() }
    public class func loginItem(identifier: String) -> SMAppService { fatalError() }
    public class func agent(plistName: String) -> SMAppService { fatalError() }
    public var status: Status { .notRegistered }
    public func register() throws {}
    public func unregister() throws {}
    public func unregister() async throws {}
    public func unregister(completionHandler handler: @escaping (Error?) -> Void) {}
    public class func openSystemSettingsLoginItems() {}
    public class func statusForLegacyURL(_ url: URL) -> Status { .notRegistered }
}
