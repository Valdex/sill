// Stub of IOSurface: the surface type CoreVideo's buffers sit on.
import Foundation
public final class IOSurfaceRef {}
public typealias IOSurfaceID = UInt32
public func IOSurfaceGetID(_ buffer: IOSurfaceRef) -> IOSurfaceID { 0 }
public func IOSurfaceGetWidth(_ buffer: IOSurfaceRef) -> Int { 0 }
public func IOSurfaceGetHeight(_ buffer: IOSurfaceRef) -> Int { 0 }
