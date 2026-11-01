import Foundation

/// Kind 32, both ways: the clipboard (docs/clipboard-plan.md). Text in this generation
/// (`WindowList.clipboard` 1); the rules are ClipboardPolicy's.
///
/// - Device → host, only to a host whose window list says `clipboard` 1 or more: any kind 32
///   subscribes the connection to the Mac's copies (an empty one, `{}`, is what a device sends at
///   connect while its Share Clipboard is on); `text` also becomes the Mac's clipboard; `stop: true`
///   ends the subscription (the device's switch went off) and wins over anything else in it.
/// - Host → device, only to a connection that subscribed: `text`, copied on the Mac, or sent by
///   another device. A subscription says what the device takes, and `{}` is text alone: a host sends
///   `image` only to a connection whose subscription carries a later field saying it takes images
///   (for example `"accepts":2`, the generation), never to one that sent `{}`, which drops it
///   (found in review: #66's host sends an image to every subscriber; as it rebases it follows this).
///
/// HostSettings.swift's rules apply: JSON; every field optional and left out when nil, so `{}` is a
/// subscription; fields added later are optional. Pull request #66 (a Mac client) uses this number
/// with `text` and `image`; `stop` is this change's, and a peer that never sends it is unaffected.
public struct Clipboard: Codable, Hashable, Sendable {
    /// The text: at most `maxTextBytes` of UTF-8, never empty when sent.
    public var text: String?
    /// Generation 2 (`clipboard` 2): an image, PNG (or JPEG, #66's), base64 in the JSON. A host of
    /// generation 1 ignores it, and a device sends none to it; a later host sends it only to a device
    /// whose subscription said it takes images (`accepts` 2 or more), never to one that subscribed with `{}`.
    public var image: Data?
    /// Device → host: true, this device's Share Clipboard went off; the host sends it nothing more
    /// until its next kind 32. Never from a host.
    public var stop: Bool?
    // The later generations (this fork; docs/coherence-protocol.md, "Clipboard generations"). A host sends
    // each only to a subscription whose `accepts` takes its generation, and fills `text` too when the item
    // has text, so a generation 1 reader keeps working; a device sends them only to a host whose window
    // list says `clipboard` 2 or 3.
    /// Device → host: the generation this subscription takes (2: `image`, `html`, `rtf`; 3: `files` too);
    /// nil, text alone (`{}`). As pull request #66's.
    public var accepts: Int?
    /// Generation 2: a UTF-8 HTML fragment (no CF_HTML header).
    public var html: String?
    /// Generation 2: RTF bytes (base64 in the JSON).
    public var rtf: Data?
    /// Generation 3: files on the sender's clipboard, fetched only when pasted (`ClipboardFiles`).
    public var files: ClipboardFiles?

    public init(text: String? = nil, image: Data? = nil, stop: Bool? = nil, accepts: Int? = nil,
                html: String? = nil, rtf: Data? = nil, files: ClipboardFiles? = nil) {
        self.text = text; self.image = image; self.stop = stop; self.accepts = accepts
        self.html = html; self.rtf = rtf; self.files = files
    }

    /// The most text either way: 256 KB of UTF-8 (262,144 bytes). The whole message's JSON also
    /// keeps to `ClipboardPolicy.maxPayload` (1 MiB), the host's cap on a device's message
    /// (`StreamMessage.maxClientPayload`), which is smaller than a device's on a host's (4 MiB,
    /// `maxOtherHostPayload`): JSON writes a control character as six bytes, so 256 KB of text can
    /// be more than 1 MiB as JSON, and then it is not sent.
    public static let maxTextBytes = 256 * 1024
}
