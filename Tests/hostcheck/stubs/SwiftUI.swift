// hostcheck: swift-api
// Stub of SwiftUI (macOS 14+): the views, modifiers, property wrappers and AppKit hosting Sill.app's
// panes use, with SwiftUI's own signatures (generic views, @ViewBuilder content, ShapeStyle's static
// members). SwiftUI re-exports Foundation, AppKit's types it needs, and Observation's.
@_exported import Foundation
@_exported import Observation
import AppKit
import Combine

// MARK: View and the builder

@preconcurrency @MainActor public protocol View {
    associatedtype Body: View
    @ViewBuilder @MainActor var body: Self.Body { get }
}
extension Never: View { public typealias Body = Never; public var body: Never { fatalError() } }
public struct EmptyView: View { public init() {}; public typealias Body = Never; public var body: Never { fatalError() } }
public struct TupleView<T>: View { public init(_ value: T) {}; public typealias Body = Never; public var body: Never { fatalError() } }
public struct _ConditionalContent<TrueContent: View, FalseContent: View>: View { public typealias Body = Never; public var body: Never { fatalError() } }
public struct AnyView: View { public init<V: View>(_ view: V) {}; public init<V: View>(erasing view: V) {}; public typealias Body = Never; public var body: Never { fatalError() } }
extension Optional: View where Wrapped: View { public typealias Body = Never; public var body: Never { fatalError() } }
public struct ModifiedContent<Content, Modifier>: View { public typealias Body = Never; public var body: Never { fatalError() } }

@resultBuilder public struct ViewBuilder {
    public static func buildExpression<Content: View>(_ content: Content) -> Content { content }
    public static func buildBlock() -> EmptyView { EmptyView() }
    public static func buildBlock<Content: View>(_ content: Content) -> Content { content }
    public static func buildBlock<each Content: View>(_ content: repeat each Content) -> TupleView<(repeat each Content)> { fatalError() }
    public static func buildIf<Content: View>(_ content: Content?) -> Content? { content }
    public static func buildEither<T: View, F: View>(first: T) -> _ConditionalContent<T, F> { fatalError() }
    public static func buildEither<T: View, F: View>(second: F) -> _ConditionalContent<T, F> { fatalError() }
    public static func buildLimitedAvailability<Content: View>(_ content: Content) -> AnyView { AnyView(content) }
}

// MARK: Text, images, colors, fonts, styles

public struct LocalizedStringKey: Equatable, ExpressibleByStringInterpolation {
    public init(_ value: String) {}
    public init(stringLiteral value: String) {}
    public init(stringInterpolation: DefaultStringInterpolation) {}
}
public struct Text: View, Equatable {
    public init(_ key: LocalizedStringKey, tableName: String? = nil, bundle: Bundle? = nil, comment: StaticString? = nil) {}
    @_disfavoredOverload public init<S: StringProtocol>(_ content: S) {}
    public init(verbatim content: String) {}
    public init(_ attributedContent: AttributedString) {}
    public init(_ date: Date, style: DateStyle) {}
    public init<F: FormatStyle>(_ input: F.FormatInput, format: F) where F.FormatInput: Equatable, F.FormatOutput == String {}
    public init(_ image: Image) {}
    public struct DateStyle { public static let date = DateStyle(), time = DateStyle(), relative = DateStyle(), offset = DateStyle(), timer = DateStyle() }
    public enum TruncationMode { case head, tail, middle }
    public enum Case { case uppercase, lowercase }
    public typealias Body = Never; public var body: Never { fatalError() }
    public static func + (a: Text, b: Text) -> Text { a }
    public func font(_ font: Font?) -> Text { self }
    public func fontWeight(_ weight: Font.Weight?) -> Text { self }
    public func bold() -> Text { self }
    public func bold(_ isActive: Bool) -> Text { self }
    public func italic() -> Text { self }
    public func monospaced() -> Text { self }
    public func monospacedDigit() -> Text { self }
    public func foregroundColor(_ color: Color?) -> Text { self }
    public func foregroundStyle<S: ShapeStyle>(_ style: S) -> Text { self }
    public func strikethrough(_ isActive: Bool = true, color: Color? = nil) -> Text { self }
    public func underline(_ isActive: Bool = true, color: Color? = nil) -> Text { self }
    public func kerning(_ kerning: CGFloat) -> Text { self }
    public func baselineOffset(_ baselineOffset: CGFloat) -> Text { self }
    public static func == (a: Text, b: Text) -> Bool { true }
}
public struct Image: View, Equatable {
    public init(systemName: String) {}
    public init(_ name: String, bundle: Bundle? = nil) {}
    public init(nsImage: NSImage) {}
    public init(decorative cgImage: CGImage, scale: CGFloat, orientation: Orientation = .up) {}
    public init(_ cgImage: CGImage, scale: CGFloat, orientation: Orientation = .up, label: Text) {}
    public enum Orientation { case up, upMirrored, down, downMirrored, left, leftMirrored, right, rightMirrored }
    public enum Interpolation { case none, low, medium, high }
    public enum ResizingMode { case tile, stretch }
    public enum Scale { case small, medium, large }
    public typealias Body = Never; public var body: Never { fatalError() }
    public func resizable(capInsets: EdgeInsets = EdgeInsets(), resizingMode: ResizingMode = .stretch) -> Image { self }
    public func interpolation(_ interpolation: Interpolation) -> Image { self }
    public func antialiased(_ isAntialiased: Bool) -> Image { self }
    public func renderingMode(_ renderingMode: TemplateRenderingMode?) -> Image { self }
    public enum TemplateRenderingMode { case template, original }
    public func symbolRenderingMode(_ mode: SymbolRenderingMode?) -> Image { self }
    public static func == (a: Image, b: Image) -> Bool { true }
}
public struct SymbolRenderingMode { public static let monochrome = SymbolRenderingMode(), multicolor = SymbolRenderingMode(), hierarchical = SymbolRenderingMode(), palette = SymbolRenderingMode() }

public protocol ShapeStyle {}
public struct Color: View, ShapeStyle, Hashable {
    public init(nsColor: NSColor) {}
    public init(_ nsColor: NSColor) {}
    public init(red: Double, green: Double, blue: Double, opacity: Double = 1) {}
    public init(white: Double, opacity: Double = 1) {}
    public init(_ name: String, bundle: Bundle? = nil) {}
    public init(cgColor: CGColor) {}
    public typealias Body = Never; public var body: Never { fatalError() }
    public static let clear = Color(white: 0), black = Color(white: 0), white = Color(white: 1), gray = Color(white: 0.5)
    public static let red = Color(white: 0), orange = Color(white: 0), yellow = Color(white: 0), green = Color(white: 0)
    public static let mint = Color(white: 0), teal = Color(white: 0), cyan = Color(white: 0), blue = Color(white: 0)
    public static let indigo = Color(white: 0), purple = Color(white: 0), pink = Color(white: 0), brown = Color(white: 0)
    public static let primary = Color(white: 0), secondary = Color(white: 0), accentColor = Color(white: 0)
    public func opacity(_ opacity: Double) -> Color { self }
}
public struct HierarchicalShapeStyle: ShapeStyle {
    public static let primary = HierarchicalShapeStyle(), secondary = HierarchicalShapeStyle(), tertiary = HierarchicalShapeStyle(), quaternary = HierarchicalShapeStyle()
}
public struct TintShapeStyle: ShapeStyle {}
public struct ForegroundStyle: ShapeStyle {}
public struct BackgroundStyle: ShapeStyle {}
public struct AnyShapeStyle: ShapeStyle { public init<S: ShapeStyle>(_ style: S) {} }
extension ShapeStyle where Self == HierarchicalShapeStyle {
    public static var primary: HierarchicalShapeStyle { .primary }
    public static var secondary: HierarchicalShapeStyle { .secondary }
    public static var tertiary: HierarchicalShapeStyle { .tertiary }
    public static var quaternary: HierarchicalShapeStyle { .quaternary }
}
extension ShapeStyle where Self == Color {
    public static var red: Color { .red }; public static var orange: Color { .orange }; public static var yellow: Color { .yellow }
    public static var green: Color { .green }; public static var blue: Color { .blue }; public static var gray: Color { .gray }
    public static var black: Color { .black }; public static var white: Color { .white }; public static var clear: Color { .clear }
    public static var purple: Color { .purple }; public static var pink: Color { .pink }; public static var mint: Color { .mint }
    public static var teal: Color { .teal }; public static var cyan: Color { .cyan }; public static var indigo: Color { .indigo }
    public static var brown: Color { .brown }
}
extension ShapeStyle where Self == TintShapeStyle { public static var tint: TintShapeStyle { TintShapeStyle() } }
extension ShapeStyle where Self == ForegroundStyle { public static var foreground: ForegroundStyle { ForegroundStyle() } }
extension ShapeStyle where Self == BackgroundStyle { public static var background: BackgroundStyle { BackgroundStyle() } }

public struct Font: Hashable {
    public enum Weight: Hashable { case ultraLight, thin, light, regular, medium, semibold, bold, heavy, black }
    public enum Design: Hashable { case `default`, serif, rounded, monospaced }
    public enum TextStyle: Hashable { case largeTitle, title, title2, title3, headline, subheadline, body, callout, footnote, caption, caption2 }
    public static let largeTitle = Font(), title = Font(), title2 = Font(), title3 = Font(), headline = Font(), subheadline = Font()
    public static let body = Font(), callout = Font(), footnote = Font(), caption = Font(), caption2 = Font()
    public static func system(size: CGFloat, weight: Weight? = nil, design: Design? = nil) -> Font { Font() }
    public static func system(_ style: TextStyle, design: Design? = nil, weight: Weight? = nil) -> Font { Font() }
    public static func custom(_ name: String, size: CGFloat) -> Font { Font() }
    public init(_ font: NSFont) {}
    init() {}
    public func monospacedDigit() -> Font { self }
    public func monospaced() -> Font { self }
    public func weight(_ weight: Weight) -> Font { self }
    public func bold() -> Font { self }
    public func italic() -> Font { self }
}

// MARK: Layout and controls

public struct EdgeInsets: Equatable { public var top, leading, bottom, trailing: CGFloat
    public init() { top = 0; leading = 0; bottom = 0; trailing = 0 }
    public init(top: CGFloat, leading: CGFloat, bottom: CGFloat, trailing: CGFloat) { self.top = top; self.leading = leading; self.bottom = bottom; self.trailing = trailing } }
public enum Edge: Int8, CaseIterable { case top, leading, bottom, trailing
    public struct Set: OptionSet { public var rawValue: Int8; public init(rawValue: Int8) { self.rawValue = rawValue }
        public init(_ e: Edge) { rawValue = 1 << e.rawValue }
        public static let top = Set(.top), leading = Set(.leading), bottom = Set(.bottom), trailing = Set(.trailing)
        public static let all: Set = [.top, .leading, .bottom, .trailing], horizontal: Set = [.leading, .trailing], vertical: Set = [.top, .bottom] } }
public struct HorizontalAlignment: Equatable { public static let leading = HorizontalAlignment(), center = HorizontalAlignment(), trailing = HorizontalAlignment()
    public static let listRowSeparatorLeading = HorizontalAlignment() }
public struct VerticalAlignment: Equatable { public static let top = VerticalAlignment(), center = VerticalAlignment(), bottom = VerticalAlignment()
    public static let firstTextBaseline = VerticalAlignment(), lastTextBaseline = VerticalAlignment() }
public struct Alignment: Equatable {
    public init(horizontal: HorizontalAlignment, vertical: VerticalAlignment) {}
    public static let center = Alignment(horizontal: .center, vertical: .center), leading = Alignment(horizontal: .leading, vertical: .center)
    public static let trailing = Alignment(horizontal: .trailing, vertical: .center), top = Alignment(horizontal: .center, vertical: .top)
    public static let bottom = Alignment(horizontal: .center, vertical: .bottom), topLeading = Alignment(horizontal: .leading, vertical: .top)
    public static let topTrailing = Alignment(horizontal: .trailing, vertical: .top), bottomLeading = Alignment(horizontal: .leading, vertical: .bottom)
    public static let bottomTrailing = Alignment(horizontal: .trailing, vertical: .bottom)
    public static let leadingFirstTextBaseline = Alignment(horizontal: .leading, vertical: .firstTextBaseline)
    public static let centerFirstTextBaseline = Alignment(horizontal: .center, vertical: .firstTextBaseline)
    public static let trailingFirstTextBaseline = Alignment(horizontal: .trailing, vertical: .firstTextBaseline)
}
public enum TextAlignment: Hashable { case leading, center, trailing }

public struct VStack<Content: View>: View { public init(alignment: HorizontalAlignment = .center, spacing: CGFloat? = nil, @ViewBuilder content: () -> Content) {}
    public typealias Body = Never; public var body: Never { fatalError() } }
public struct HStack<Content: View>: View { public init(alignment: VerticalAlignment = .center, spacing: CGFloat? = nil, @ViewBuilder content: () -> Content) {}
    public typealias Body = Never; public var body: Never { fatalError() } }
public struct ZStack<Content: View>: View { public init(alignment: Alignment = .center, @ViewBuilder content: () -> Content) {}
    public typealias Body = Never; public var body: Never { fatalError() } }
public struct Group<Content> { public init(@ViewBuilder content: () -> Content) where Content: View {} }
extension Group: View where Content: View { public typealias Body = Never; public var body: Never { fatalError() } }
public struct Spacer: View { public init(minLength: CGFloat? = nil) {}; public typealias Body = Never; public var body: Never { fatalError() } }
public struct Divider: View { public init() {}; public typealias Body = Never; public var body: Never { fatalError() } }
public struct ScrollView<Content: View>: View { public init(_ axes: Axis.Set = .vertical, showsIndicators: Bool = true, @ViewBuilder content: () -> Content) {}
    public typealias Body = Never; public var body: Never { fatalError() } }
public enum Axis: Int8 { case horizontal, vertical
    public struct Set: OptionSet { public var rawValue: Int8; public init(rawValue: Int8) { self.rawValue = rawValue }
        public static let horizontal = Set(rawValue: 1), vertical = Set(rawValue: 2) } }
public struct Grid<Content: View>: View {
    public init(alignment: Alignment = .center, horizontalSpacing: CGFloat? = nil, verticalSpacing: CGFloat? = nil, @ViewBuilder content: () -> Content) {}
    public typealias Body = Never; public var body: Never { fatalError() } }
public struct GridRow<Content: View>: View { public init(alignment: VerticalAlignment? = nil, @ViewBuilder content: () -> Content) {}
    public typealias Body = Never; public var body: Never { fatalError() } }
public struct Form<Content: View>: View { public init(@ViewBuilder content: () -> Content) {}
    public typealias Body = Never; public var body: Never { fatalError() } }
public struct Section<Parent, Content, Footer> {}
extension Section: View where Parent: View, Content: View, Footer: View { public typealias Body = Never; public var body: Never { fatalError() } }
extension Section where Parent: View, Content: View, Footer: View {
    public init(@ViewBuilder content: () -> Content, @ViewBuilder header: () -> Parent, @ViewBuilder footer: () -> Footer) {}
}
extension Section where Parent == EmptyView, Content: View, Footer == EmptyView {
    public init(@ViewBuilder content: () -> Content) {}
}
extension Section where Parent: View, Content: View, Footer == EmptyView {
    public init(@ViewBuilder content: () -> Content, @ViewBuilder header: () -> Parent) {}
}
extension Section where Parent == EmptyView, Content: View, Footer: View {
    public init(@ViewBuilder content: () -> Content, @ViewBuilder footer: () -> Footer) {}
}
extension Section where Parent == Text, Content: View, Footer == EmptyView {
    public init(_ titleKey: LocalizedStringKey, @ViewBuilder content: () -> Content) {}
    @_disfavoredOverload public init<S: StringProtocol>(_ title: S, @ViewBuilder content: () -> Content) {}
}
public struct ForEach<Data: RandomAccessCollection, ID: Hashable, Content> {}
extension ForEach: View where Content: View { public typealias Body = Never; public var body: Never { fatalError() } }
extension ForEach where Content: View {
    public init(_ data: Data, id: KeyPath<Data.Element, ID>, @ViewBuilder content: @escaping (Data.Element) -> Content) {}
}
extension ForEach where ID == Data.Element.ID, Content: View, Data.Element: Identifiable {
    public init(_ data: Data, @ViewBuilder content: @escaping (Data.Element) -> Content) {}
}
extension ForEach where Data == Range<Int>, ID == Int, Content: View {
    public init(_ data: Range<Int>, @ViewBuilder content: @escaping (Int) -> Content) {}
}
public struct Label<Title: View, Icon: View>: View {
    public init(@ViewBuilder title: () -> Title, @ViewBuilder icon: () -> Icon) {}
    public typealias Body = Never; public var body: Never { fatalError() } }
extension Label where Title == Text, Icon == Image {
    public init(_ titleKey: LocalizedStringKey, systemImage name: String) {}
    @_disfavoredOverload public init<S: StringProtocol>(_ title: S, systemImage name: String) {}
}
public struct LabeledContent<Label, Content> {}
extension LabeledContent: View where Label: View, Content: View { public typealias Body = Never; public var body: Never { fatalError() } }
extension LabeledContent where Label: View, Content: View {
    public init(@ViewBuilder content: () -> Content, @ViewBuilder label: () -> Label) {}
}
extension LabeledContent where Label == Text, Content: View {
    public init(_ titleKey: LocalizedStringKey, @ViewBuilder content: () -> Content) {}
    @_disfavoredOverload public init<S: StringProtocol>(_ title: S, @ViewBuilder content: () -> Content) {}
}
extension LabeledContent where Label == Text, Content == Text {
    public init<S: StringProtocol>(_ titleKey: LocalizedStringKey, value: S) {}
    @_disfavoredOverload public init<S1: StringProtocol, S2: StringProtocol>(_ title: S1, value: S2) {}
}
public struct ButtonRole: Equatable { public static let destructive = ButtonRole(), cancel = ButtonRole() }
public struct Button<Label: View>: View {
    public init(action: @escaping () -> Void, @ViewBuilder label: () -> Label) {}
    public init(role: ButtonRole?, action: @escaping () -> Void, @ViewBuilder label: () -> Label) {}
    public typealias Body = Never; public var body: Never { fatalError() } }
extension Button where Label == Text {
    public init(_ titleKey: LocalizedStringKey, action: @escaping () -> Void) {}
    @_disfavoredOverload public init<S: StringProtocol>(_ title: S, action: @escaping () -> Void) {}
    public init(_ titleKey: LocalizedStringKey, role: ButtonRole?, action: @escaping () -> Void) {}
    @_disfavoredOverload public init<S: StringProtocol>(_ title: S, role: ButtonRole?, action: @escaping () -> Void) {}
}
public struct Toggle<Label: View>: View {
    public init(isOn: Binding<Bool>, @ViewBuilder label: () -> Label) {}
    public typealias Body = Never; public var body: Never { fatalError() } }
extension Toggle where Label == Text {
    public init(_ titleKey: LocalizedStringKey, isOn: Binding<Bool>) {}
    @_disfavoredOverload public init<S: StringProtocol>(_ title: S, isOn: Binding<Bool>) {}
}
public struct Picker<Label: View, SelectionValue: Hashable, Content: View>: View {
    public init(selection: Binding<SelectionValue>, @ViewBuilder content: () -> Content, @ViewBuilder label: () -> Label) {}
    public typealias Body = Never; public var body: Never { fatalError() } }
extension Picker where Label == Text {
    public init(_ titleKey: LocalizedStringKey, selection: Binding<SelectionValue>, @ViewBuilder content: () -> Content) {}
    @_disfavoredOverload public init<S: StringProtocol>(_ title: S, selection: Binding<SelectionValue>, @ViewBuilder content: () -> Content) {}
}
public struct TextField<Label: View>: View { public typealias Body = Never; public var body: Never { fatalError() } }
extension TextField where Label == Text {
    public init(_ titleKey: LocalizedStringKey, text: Binding<String>) {}
    @_disfavoredOverload public init<S: StringProtocol>(_ title: S, text: Binding<String>) {}
    public init(_ titleKey: LocalizedStringKey, text: Binding<String>, prompt: Text?) {}
}
public struct Link<Label: View>: View {
    public init(destination: URL, @ViewBuilder label: () -> Label) {}
    public typealias Body = Never; public var body: Never { fatalError() } }
extension Link where Label == Text {
    public init(_ titleKey: LocalizedStringKey, destination: URL) {}
    @_disfavoredOverload public init<S: StringProtocol>(_ title: S, destination: URL) {}
}
public struct ProgressView<Label: View, CurrentValueLabel: View>: View { public typealias Body = Never; public var body: Never { fatalError() } }
extension ProgressView where Label == EmptyView, CurrentValueLabel == EmptyView { public init() {} }

public protocol Shape: View {}
public struct RoundedRectangle: Shape {
    public init(cornerRadius: CGFloat, style: RoundedCornerStyle = .circular) {}
    public typealias Body = Never; public var body: Never { fatalError() } }
public struct Rectangle: Shape { public init() {}; public typealias Body = Never; public var body: Never { fatalError() } }
public struct Circle: Shape { public init() {}; public typealias Body = Never; public var body: Never { fatalError() } }
public struct Capsule: Shape { public init() {}; public typealias Body = Never; public var body: Never { fatalError() } }
public enum RoundedCornerStyle { case circular, continuous }
extension Shape {
    public func fill<S: ShapeStyle>(_ content: S) -> some View { self }
    public func stroke<S: ShapeStyle>(_ content: S, lineWidth: CGFloat = 1) -> some View { self }
}

public struct TimelineViewDefaultContext { public let date: Date; public let cadence: Cadence
    public enum Cadence: Comparable { case live, seconds, minutes } }
public struct TimelineView<Schedule: TimelineSchedule, Content: View>: View {
    public typealias Context = TimelineViewDefaultContext
    public init(_ schedule: Schedule, @ViewBuilder content: @escaping (TimelineViewDefaultContext) -> Content) {}
    public typealias Body = Never; public var body: Never { fatalError() } }
public protocol TimelineSchedule {}
public struct PeriodicTimelineSchedule: TimelineSchedule { public init(from startDate: Date, by interval: TimeInterval) {} }
public struct EveryMinuteTimelineSchedule: TimelineSchedule {}
extension TimelineSchedule where Self == PeriodicTimelineSchedule {
    public static func periodic(from startDate: Date, by interval: TimeInterval) -> PeriodicTimelineSchedule { .init(from: startDate, by: interval) }
}
extension TimelineSchedule where Self == EveryMinuteTimelineSchedule { public static var everyMinute: EveryMinuteTimelineSchedule { .init() } }

// MARK: State and the environment

@propertyWrapper @dynamicMemberLookup public struct Binding<Value> {
    public init(get: @escaping () -> Value, set: @escaping (Value) -> Void) {}
    public init(projectedValue: Binding<Value>) {}
    public static func constant(_ value: Value) -> Binding<Value> { fatalError() }
    public var wrappedValue: Value { get { fatalError() } nonmutating set {} }
    public var projectedValue: Binding<Value> { self }
    public subscript<Subject>(dynamicMember keyPath: WritableKeyPath<Value, Subject>) -> Binding<Subject> { fatalError() }
}
@propertyWrapper public struct State<Value>: DynamicProperty {
    public init(wrappedValue value: Value) {}
    public init(initialValue value: Value) {}
    public var wrappedValue: Value { get { fatalError() } nonmutating set {} }
    public var projectedValue: Binding<Value> { fatalError() }
}
extension State where Value: ExpressibleByNilLiteral { public init() {} }
@propertyWrapper public struct FocusState<Value: Hashable>: DynamicProperty {
    public init() where Value == Bool {}
    public init<T: Hashable>() where Value == T? {}
    public var wrappedValue: Value { get { fatalError() } nonmutating set {} }
    public var projectedValue: Binding { fatalError() }
    public struct Binding { public var wrappedValue: Value { get { fatalError() } nonmutating set {} } }
}
@propertyWrapper @dynamicMemberLookup public struct Bindable<Value> {
    public init(wrappedValue: Value) where Value: AnyObject & Observable { self.wrappedValue = wrappedValue }
    public init(_ wrappedValue: Value) where Value: AnyObject & Observable { self.wrappedValue = wrappedValue }
    public var wrappedValue: Value
    public var projectedValue: Bindable<Value> { self }
    public subscript<Subject>(dynamicMember keyPath: ReferenceWritableKeyPath<Value, Subject>) -> Binding<Subject> where Value: AnyObject { fatalError() }
}
public protocol DynamicProperty {}
@propertyWrapper public struct Environment<Value>: DynamicProperty {
    public init(_ keyPath: KeyPath<EnvironmentValues, Value>) {}
    public var wrappedValue: Value { fatalError() }
}
public struct EnvironmentValues {
    public var displayScale: CGFloat { get { 2 } set {} }
    public var colorScheme: ColorScheme { get { .light } set {} }
    public var isEnabled: Bool { get { true } set {} }
    public var openURL: OpenURLAction { get { fatalError() } set {} }
    public var dismiss: DismissAction { fatalError() }
    public var controlActiveState: ControlActiveState { get { .active } set {} }
}
public enum ColorScheme: Hashable { case light, dark }
public enum ControlActiveState: Equatable { case key, active, inactive }
public struct OpenURLAction { public func callAsFunction(_ url: URL) {} }
public struct DismissAction { public func callAsFunction() {} }

// MARK: Modifiers

public struct TextSelectability { public static let enabled = TextSelectability(), disabled = TextSelectability() }
public struct KeyboardShortcut: Hashable { public static let defaultAction = KeyboardShortcut(), cancelAction = KeyboardShortcut()
    public init(_ key: KeyEquivalent, modifiers: EventModifiers = .command) {}
    init() {} }
public struct KeyEquivalent: Hashable, ExpressibleByExtendedGraphemeClusterLiteral { public init(_ character: Character) {}
    public init(extendedGraphemeClusterLiteral value: Character) {}
    public static let `return` = KeyEquivalent("\r"), escape = KeyEquivalent("\u{1B}"), delete = KeyEquivalent("\u{8}"), space = KeyEquivalent(" ") }
public struct EventModifiers: OptionSet, Hashable { public var rawValue: Int; public init(rawValue: Int) { self.rawValue = rawValue }
    public static let command = EventModifiers(rawValue: 1), shift = EventModifiers(rawValue: 2), option = EventModifiers(rawValue: 4), control = EventModifiers(rawValue: 8) }
public protocol FormStyle {}
public struct GroupedFormStyle: FormStyle {}
public struct ColumnsFormStyle: FormStyle {}
extension FormStyle where Self == GroupedFormStyle { public static var grouped: GroupedFormStyle { .init() } }
extension FormStyle where Self == ColumnsFormStyle { public static var columns: ColumnsFormStyle { .init() } }
public protocol PickerStyle {}
public struct SegmentedPickerStyle: PickerStyle {}
public struct MenuPickerStyle: PickerStyle {}
public struct RadioGroupPickerStyle: PickerStyle {}
extension PickerStyle where Self == SegmentedPickerStyle { public static var segmented: SegmentedPickerStyle { .init() } }
extension PickerStyle where Self == MenuPickerStyle { public static var menu: MenuPickerStyle { .init() } }
extension PickerStyle where Self == RadioGroupPickerStyle { public static var radioGroup: RadioGroupPickerStyle { .init() } }
public protocol PrimitiveButtonStyle {}
public struct LinkButtonStyle: PrimitiveButtonStyle {}
public struct BorderlessButtonStyle: PrimitiveButtonStyle {}
public struct BorderedButtonStyle: PrimitiveButtonStyle {}
public struct PlainButtonStyle: PrimitiveButtonStyle {}
public struct BorderedProminentButtonStyle: PrimitiveButtonStyle {}
extension PrimitiveButtonStyle where Self == LinkButtonStyle { public static var link: LinkButtonStyle { .init() } }
extension PrimitiveButtonStyle where Self == BorderlessButtonStyle { public static var borderless: BorderlessButtonStyle { .init() } }
extension PrimitiveButtonStyle where Self == BorderedButtonStyle { public static var bordered: BorderedButtonStyle { .init() } }
extension PrimitiveButtonStyle where Self == PlainButtonStyle { public static var plain: PlainButtonStyle { .init() } }
extension PrimitiveButtonStyle where Self == BorderedProminentButtonStyle { public static var borderedProminent: BorderedProminentButtonStyle { .init() } }
public enum ControlSize: CaseIterable { case mini, small, regular, large, extraLarge }
public struct AccessibilityChildBehavior: Hashable { public static let ignore = AccessibilityChildBehavior(), contain = AccessibilityChildBehavior(), combine = AccessibilityChildBehavior() }
public struct AccessibilityTraits: SetAlgebra, Hashable {
    public init() {}
    public static let isButton = AccessibilityTraits(), isHeader = AccessibilityTraits(), isImage = AccessibilityTraits(), isStaticText = AccessibilityTraits()
    public static let isSelected = AccessibilityTraits(), isLink = AccessibilityTraits(), updatesFrequently = AccessibilityTraits()
    public func union(_ other: Self) -> Self { self }; public func intersection(_ other: Self) -> Self { self }
    public func symmetricDifference(_ other: Self) -> Self { self }; public func contains(_ member: Self) -> Bool { false }
    public mutating func insert(_ newMember: Self) -> (inserted: Bool, memberAfterInsert: Self) { (true, self) }
    public mutating func remove(_ member: Self) -> Self? { nil }; public mutating func update(with newMember: Self) -> Self? { nil }
    public mutating func formUnion(_ other: Self) {}; public mutating func formIntersection(_ other: Self) {}; public mutating func formSymmetricDifference(_ other: Self) {}
}

extension View {
    public func font(_ font: Font?) -> some View { self }
    public func fontWeight(_ weight: Font.Weight?) -> some View { self }
    public func foregroundStyle<S: ShapeStyle>(_ style: S) -> some View { self }
    public func foregroundStyle<S1: ShapeStyle, S2: ShapeStyle>(_ primary: S1, _ secondary: S2) -> some View { self }
    public func foregroundColor(_ color: Color?) -> some View { self }
    public func tint(_ tint: Color?) -> some View { self }
    public func frame(width: CGFloat? = nil, height: CGFloat? = nil, alignment: Alignment = .center) -> some View { self }
    public func frame(minWidth: CGFloat? = nil, idealWidth: CGFloat? = nil, maxWidth: CGFloat? = nil, minHeight: CGFloat? = nil,
                      idealHeight: CGFloat? = nil, maxHeight: CGFloat? = nil, alignment: Alignment = .center) -> some View { self }
    public func fixedSize() -> some View { self }
    public func fixedSize(horizontal: Bool, vertical: Bool) -> some View { self }
    public func padding(_ insets: EdgeInsets) -> some View { self }
    public func padding(_ edges: Edge.Set = .all, _ length: CGFloat? = nil) -> some View { self }
    public func padding(_ length: CGFloat) -> some View { self }
    @available(macOS, deprecated: 100000.0, message: "Use `background(alignment:content:)` instead.")
    @_disfavoredOverload public func background<V: View>(_ background: V, alignment: Alignment = .center) -> some View { self }
    public func background<V: View>(alignment: Alignment = .center, @ViewBuilder content: () -> V) -> some View { self }
    public func background<S: ShapeStyle>(_ style: S, ignoresSafeAreaEdges edges: Edge.Set = .all) -> some View { self }
    @available(macOS, deprecated: 100000.0, message: "Use `overlay(alignment:content:)` instead.")
    @_disfavoredOverload public func overlay<V: View>(_ overlay: V, alignment: Alignment = .center) -> some View { self }
    public func overlay<S: ShapeStyle>(_ style: S, ignoresSafeAreaEdges edges: Edge.Set = .all) -> some View { self }
    public func overlay<V: View>(alignment: Alignment = .center, @ViewBuilder content: () -> V) -> some View { self }
    public func textSelection(_ selectability: TextSelectability) -> some View { self }
    public func tag<V: Hashable>(_ tag: V) -> some View { self }
    public func id<ID: Hashable>(_ id: ID) -> some View { self }
    public func disabled(_ disabled: Bool) -> some View { self }
    public func opacity(_ opacity: Double) -> some View { self }
    public func hidden() -> some View { self }
    public func keyboardShortcut(_ shortcut: KeyboardShortcut) -> some View { self }
    public func keyboardShortcut(_ key: KeyEquivalent, modifiers: EventModifiers = .command) -> some View { self }
    public func formStyle<S: FormStyle>(_ style: S) -> some View { self }
    public func pickerStyle<S: PickerStyle>(_ style: S) -> some View { self }
    public func buttonStyle<S: PrimitiveButtonStyle>(_ style: S) -> some View { self }
    public func controlSize(_ controlSize: ControlSize) -> some View { self }
    public func labelsHidden() -> some View { self }
    public func multilineTextAlignment(_ alignment: TextAlignment) -> some View { self }
    public func lineLimit(_ number: Int?) -> some View { self }
    public func truncationMode(_ mode: Text.TruncationMode) -> some View { self }
    public func monospacedDigit() -> some View { self }
    public func help(_ textKey: LocalizedStringKey) -> some View { self }
    @_disfavoredOverload public func help<S: StringProtocol>(_ text: S) -> some View { self }
    public func help(_ text: Text) -> some View { self }
    public func focused(_ condition: FocusState<Bool>.Binding) -> some View { self }
    public func focused<Value: Hashable>(_ binding: FocusState<Value>.Binding, equals value: Value) -> some View { self }
    public func environment<V>(_ keyPath: WritableKeyPath<EnvironmentValues, V>, _ value: V) -> some View { self }
    public func onAppear(perform action: (() -> Void)? = nil) -> some View { self }
    public func onSubmit(of triggers: SubmitTriggers = .text, _ action: @escaping () -> Void) -> some View { self }
    public func onExitCommand(perform action: (() -> Void)?) -> some View { self }
    public func onDisappear(perform action: (() -> Void)? = nil) -> some View { self }
    public func onTapGesture(count: Int = 1, perform action: @escaping () -> Void) -> some View { self }
    public func onChange<V: Equatable>(of value: V, perform action: @escaping (V) -> Void) -> some View { self }
    public func onChange<V: Equatable>(of value: V, initial: Bool = false, _ action: @escaping () -> Void) -> some View { self }
    public func onChange<V: Equatable>(of value: V, initial: Bool = false, _ action: @escaping (V, V) -> Void) -> some View { self }
    public func onReceive<P: Publisher>(_ publisher: P, perform action: @escaping (P.Output) -> Void) -> some View where P.Failure == Never { self }
    public func task(priority: TaskPriority = .userInitiated, _ action: @escaping @isolated(any) @Sendable () async -> Void) -> some View { self }
    public func task<T: Equatable>(id value: T, priority: TaskPriority = .userInitiated, _ action: @escaping @isolated(any) @Sendable () async -> Void) -> some View { self }
    public func accessibilityElement(children: AccessibilityChildBehavior = .ignore) -> some View { self }
    public func accessibilityLabel(_ label: Text) -> some View { self }
    public func accessibilityLabel(_ labelKey: LocalizedStringKey) -> some View { self }
    @_disfavoredOverload public func accessibilityLabel<S: StringProtocol>(_ label: S) -> some View { self }
    public func accessibilityValue(_ valueDescription: Text) -> some View { self }
    @_disfavoredOverload public func accessibilityValue<S: StringProtocol>(_ value: S) -> some View { self }
    public func accessibilityHint(_ hint: Text) -> some View { self }
    public func accessibilityHidden(_ hidden: Bool) -> some View { self }
    public func accessibilityAddTraits(_ traits: AccessibilityTraits) -> some View { self }
    public func accessibilityAction(named nameKey: LocalizedStringKey, _ handler: @escaping () -> Void) -> some View { self }
    @_disfavoredOverload public func accessibilityAction<S: StringProtocol>(named name: S, _ handler: @escaping () -> Void) -> some View { self }
    public func clipShape<S: Shape>(_ shape: S, style: FillStyle = FillStyle()) -> some View { self }
    public func cornerRadius(_ radius: CGFloat, antialiased: Bool = true) -> some View { self }
    public func shadow(color: Color = .black, radius: CGFloat, x: CGFloat = 0, y: CGFloat = 0) -> some View { self }
    public func scaleEffect(_ s: CGFloat, anchor: UnitPoint = .center) -> some View { self }
    public func animation<V: Equatable>(_ animation: Animation?, value: V) -> some View { self }
    public func layoutPriority(_ value: Double) -> some View { self }
    public func contentShape<S: Shape>(_ shape: S, eoFill: Bool = false) -> some View { self }
    public func symbolRenderingMode(_ mode: SymbolRenderingMode?) -> some View { self }
    public func imageScale(_ scale: Image.Scale) -> some View { self }
    public func gridColumnAlignment(_ guide: HorizontalAlignment) -> some View { self }
    public func gridCellColumns(_ count: Int) -> some View { self }
    public func gridCellAnchor(_ anchor: UnitPoint) -> some View { self }
    public func alert<A: View>(_ titleKey: LocalizedStringKey, isPresented: Binding<Bool>, @ViewBuilder actions: () -> A) -> some View { self }
    public func sheet<Content: View>(isPresented: Binding<Bool>, onDismiss: (() -> Void)? = nil, @ViewBuilder content: @escaping () -> Content) -> some View { self }
    public func scrollContentBackground(_ visibility: Visibility) -> some View { self }
    public func listRowSeparator(_ visibility: Visibility) -> some View { self }
}
public enum Visibility: Hashable, CaseIterable { case automatic, visible, hidden }
public struct FillStyle: Equatable { public init(eoFill: Bool = false, antialiased: Bool = true) {} }
public struct UnitPoint: Hashable { public static let center = UnitPoint(), leading = UnitPoint(), trailing = UnitPoint(), top = UnitPoint(), bottom = UnitPoint(), topLeading = UnitPoint() }
public struct Animation: Equatable { public static let `default` = Animation(), easeInOut = Animation(), linear = Animation() }
public func withAnimation<Result>(_ animation: Animation? = .default, _ body: () throws -> Result) rethrows -> Result { try body() }

// MARK: Hosting in AppKit

@preconcurrency @MainActor open class NSHostingView<Content: View>: NSView {
    public required init(rootView: Content) { super.init(frame: .zero) }
    public required init?(coder: NSCoder) { super.init(frame: .zero) }
    open var rootView: Content { get { fatalError() } set {} }
    open var sizingOptions: NSHostingSizingOptions = .standardBounds
}
@preconcurrency @MainActor open class NSHostingController<Content: View>: NSViewController {
    public init(rootView: Content) { super.init(nibName: nil, bundle: nil) }
    public required init?(coder: NSCoder) { super.init(nibName: nil, bundle: nil) }
    open var rootView: Content { get { fatalError() } set {} }
    open var sizingOptions: NSHostingSizingOptions = .standardBounds
}
public struct NSHostingSizingOptions: OptionSet { public var rawValue: Int; public init(rawValue: Int) { self.rawValue = rawValue }
    public static let minSize = NSHostingSizingOptions(rawValue: 1), intrinsicContentSize = NSHostingSizingOptions(rawValue: 2)
    public static let maxSize = NSHostingSizingOptions(rawValue: 4), preferredContentSize = NSHostingSizingOptions(rawValue: 8)
    public static let standardBounds: NSHostingSizingOptions = [.minSize, .intrinsicContentSize, .maxSize] }

@preconcurrency @MainActor public final class ImageRenderer<Content: View>: ObservableObject {
    public init(content view: Content) {}
    public var content: Content { get { fatalError() } set {} }
    public var scale: CGFloat = 1
    public var proposedSize: ProposedViewSize = .unspecified
    public var cgImage: CGImage? { nil }
    public var nsImage: NSImage? { nil }
}
public struct ProposedViewSize: Equatable { public var width: CGFloat?; public var height: CGFloat?
    public init(width: CGFloat?, height: CGFloat?) { self.width = width; self.height = height }
    public static let unspecified = ProposedViewSize(width: nil, height: nil), zero = ProposedViewSize(width: 0, height: 0) }

public struct SubmitTriggers: OptionSet { public var rawValue: Int; public init(rawValue: Int) { self.rawValue = rawValue }
    public static let text = SubmitTriggers(rawValue: 1), search = SubmitTriggers(rawValue: 2) }
public enum AccessibilityNotification {
    public struct Announcement { public init(_ announcement: String) {}; public init(_ announcement: AttributedString) {}; public func post() {} }
    public struct LayoutChanged { public init(_ argument: Any? = nil) {}; public func post() {} }
    public struct ScreenChanged { public init(_ argument: Any? = nil) {}; public func post() {} }
}
