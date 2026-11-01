// hostcheck: swift-api
// Stub of Combine: publishers as far as Sill.app uses them (a notification's publisher for
// SwiftUI's onReceive), ObservableObject and AnyCancellable.
import Foundation

public protocol Publisher<Output, Failure> {
    associatedtype Output
    associatedtype Failure: Error
}
public protocol Cancellable { func cancel() }
public final class AnyCancellable: Cancellable, Hashable {
    public init(_ cancel: @escaping () -> Void) {}
    public func cancel() {}
    public func store(in set: inout Set<AnyCancellable>) {}
    public static func == (a: AnyCancellable, b: AnyCancellable) -> Bool { a === b }
    public func hash(into h: inout Hasher) {}
}
extension Publisher {
    public func sink(receiveValue: @escaping (Output) -> Void) -> AnyCancellable where Failure == Never { AnyCancellable {} }
}
public final class ObservableObjectPublisher: Publisher {
    public typealias Output = Void; public typealias Failure = Never
    public init() {}
    public func send() {}
}
public protocol ObservableObject: AnyObject {
    associatedtype ObjectWillChangePublisher: Publisher = ObservableObjectPublisher where ObjectWillChangePublisher.Failure == Never
    var objectWillChange: ObjectWillChangePublisher { get }
}
extension ObservableObject where ObjectWillChangePublisher == ObservableObjectPublisher {
    public var objectWillChange: ObservableObjectPublisher { ObservableObjectPublisher() }
}
extension NotificationCenter {
    public struct Publisher: Combine.Publisher {
        public typealias Output = Notification; public typealias Failure = Never
        public init(center: NotificationCenter, name: Notification.Name, object: AnyObject? = nil) {}
    }
    public func publisher(for name: Notification.Name, object: AnyObject? = nil) -> NotificationCenter.Publisher {
        Publisher(center: self, name: name, object: object)
    }
}
