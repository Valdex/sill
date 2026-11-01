// Stub of the ObjectiveC module: what the host uses of the Objective-C runtime. With
// -enable-objc-interop (run.sh), the compiler takes Selector from here for #selector and checks
// every #selector's target is @objc, as on a Mac. Foundation on Linux has its own ObjCBool.

public struct Selector: Hashable, ExpressibleByStringLiteral, CustomStringConvertible {
    let name: String
    public init(_ str: String) { name = str }
    public init(stringLiteral value: String) { name = value }
    public var description: String { name }
}

public typealias Method = OpaquePointer
public typealias IMP = OpaquePointer
public typealias Ivar = OpaquePointer

public func class_getInstanceMethod(_ cls: AnyClass?, _ name: Selector) -> Method? { nil }
public func class_getClassMethod(_ cls: AnyClass?, _ name: Selector) -> Method? { nil }
public func method_getTypeEncoding(_ m: Method) -> UnsafePointer<CChar>? { nil }
public func method_getImplementation(_ m: Method) -> IMP { m }
public func object_getClass(_ obj: Any?) -> AnyClass? { nil }
public func sel_registerName(_ str: UnsafePointer<CChar>) -> Selector { Selector(String(cString: str)) }
public func sel_getName(_ sel: Selector) -> UnsafePointer<CChar> { fatalError() }
public func class_getName(_ cls: AnyClass?) -> UnsafePointer<CChar> { fatalError() }
public func NSSelectorFromString(_ aSelectorName: String) -> Selector { Selector(aSelectorName) }
public func NSStringFromSelector(_ aSelector: Selector) -> String { aSelector.name }
