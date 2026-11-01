// Stub of ImageIO: image sources and destinations (CGImageSource.h, CGImageDestination.h).
@_exported import CoreGraphics
import Foundation

public final class CGImageSource {}
public final class CGImageDestination {}
public func CGImageSourceCreateWithData(_ data: CFData, _ options: CFDictionary?) -> CGImageSource? { nil }
public func CGImageSourceCreateWithURL(_ url: CFURL, _ options: CFDictionary?) -> CGImageSource? { nil }
public func CGImageSourceGetCount(_ isrc: CGImageSource) -> Int { 0 }
public func CGImageSourceGetType(_ isrc: CGImageSource) -> CFString? { nil }
public func CGImageSourceCreateImageAtIndex(_ isrc: CGImageSource, _ index: Int, _ options: CFDictionary?) -> CGImage? { nil }
public func CGImageSourceCreateThumbnailAtIndex(_ isrc: CGImageSource, _ index: Int, _ options: CFDictionary?) -> CGImage? { nil }
public func CGImageSourceCopyPropertiesAtIndex(_ isrc: CGImageSource, _ index: Int, _ options: CFDictionary?) -> CFDictionary? { nil }
public func CGImageDestinationCreateWithData(_ data: CFMutableData, _ type: CFString, _ count: Int, _ options: CFDictionary?) -> CGImageDestination? { nil }
public func CGImageDestinationCreateWithURL(_ url: CFURL, _ type: CFString, _ count: Int, _ options: CFDictionary?) -> CGImageDestination? { nil }
public func CGImageDestinationAddImage(_ idst: CGImageDestination, _ image: CGImage, _ properties: CFDictionary?) {}
public func CGImageDestinationFinalize(_ idst: CGImageDestination) -> Bool { false }
public let kCGImageDestinationLossyCompressionQuality: CFString = "kCGImageDestinationLossyCompressionQuality"
public let kCGImagePropertyPixelWidth: CFString = "PixelWidth"
public let kCGImagePropertyPixelHeight: CFString = "PixelHeight"
public let kCGImagePropertyOrientation: CFString = "Orientation"
public let kCGImageSourceCreateThumbnailFromImageAlways: CFString = "kCGImageSourceCreateThumbnailFromImageAlways"
public let kCGImageSourceThumbnailMaxPixelSize: CFString = "kCGImageSourceThumbnailMaxPixelSize"
public let kCGImageSourceCreateThumbnailWithTransform: CFString = "kCGImageSourceCreateThumbnailWithTransform"
public let kCGImageSourceShouldCache: CFString = "kCGImageSourceShouldCache"
