// Stub of UniformTypeIdentifiers: UTType, and what its overlay adds to Foundation (a URL's content type).
// hostcheck: swift-api
@_exported import Foundation

public struct UTType: Hashable, Sendable, CustomStringConvertible, CustomDebugStringConvertible {
    public init?(_ identifier: String) { self.identifier = identifier }
    public init?(filenameExtension: String) { identifier = "" }
    public init?(filenameExtension: String, conformingTo supertype: UTType) { identifier = "" }
    public init?(mimeType: String) { identifier = "" }
    public init?(mimeType: String, conformingTo supertype: UTType) { identifier = "" }
    public init(exportedAs identifier: String) { self.identifier = identifier }
    public init(importedAs identifier: String) { self.identifier = identifier }
    public let identifier: String
    public var preferredFilenameExtension: String? { nil }
    public var preferredMIMEType: String? { nil }
    public var localizedDescription: String? { nil }
    public var isDeclared: Bool { false }
    public var isDynamic: Bool { false }
    public var isPublic: Bool { false }
    public var supertypes: Set<UTType> { [] }
    public func conforms(to type: UTType) -> Bool { false }
    public func isSupertype(of type: UTType) -> Bool { false }
    public func isSubtype(of type: UTType) -> Bool { false }
    public var description: String { identifier }
    public var debugDescription: String { identifier }
    public static let item = UTType(exportedAs: "public.item"), content = UTType(exportedAs: "public.content")
    public static let data = UTType(exportedAs: "public.data"), text = UTType(exportedAs: "public.text")
    public static let plainText = UTType(exportedAs: "public.plain-text"), utf8PlainText = UTType(exportedAs: "public.utf8-plain-text")
    public static let rtf = UTType(exportedAs: "public.rtf"), rtfd = UTType(exportedAs: "com.apple.rtfd"), flatRTFD = UTType(exportedAs: "com.apple.flat-rtfd")
    public static let html = UTType(exportedAs: "public.html"), xml = UTType(exportedAs: "public.xml"), json = UTType(exportedAs: "public.json")
    public static let image = UTType(exportedAs: "public.image"), png = UTType(exportedAs: "public.png"), jpeg = UTType(exportedAs: "public.jpeg")
    public static let tiff = UTType(exportedAs: "public.tiff"), gif = UTType(exportedAs: "com.compuserve.gif"), heic = UTType(exportedAs: "public.heic")
    public static let bmp = UTType(exportedAs: "com.microsoft.bmp"), svg = UTType(exportedAs: "public.svg-image"), pdf = UTType(exportedAs: "com.adobe.pdf")
    public static let movie = UTType(exportedAs: "public.movie"), audio = UTType(exportedAs: "public.audio")
    public static let folder = UTType(exportedAs: "public.folder"), directory = UTType(exportedAs: "public.directory")
    public static let fileURL = UTType(exportedAs: "public.file-url"), url = UTType(exportedAs: "public.url")
    public static let zip = UTType(exportedAs: "public.zip-archive"), archive = UTType(exportedAs: "public.archive")
    public static let application = UTType(exportedAs: "com.apple.application"), applicationBundle = UTType(exportedAs: "com.apple.application-bundle")
    public static let package = UTType(exportedAs: "com.apple.package"), bundle = UTType(exportedAs: "com.apple.bundle")
    public static let symbolicLink = UTType(exportedAs: "public.symlink"), aliasFile = UTType(exportedAs: "com.apple.alias-file")
    public static let webArchive = UTType(exportedAs: "com.apple.webarchive"), executable = UTType(exportedAs: "public.executable")
}
extension URLResourceValues {
    public var contentType: UTType? { nil }
}
extension URLResourceKey {
    public static let contentTypeKey = URLResourceKey(rawValue: "NSURLContentTypeKey")
}
