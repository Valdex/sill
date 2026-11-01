using Sill.Windows.Coherence;
using Sill.Windows.Protocol;

namespace Sill.Windows.Net;

// The session's part in Coherence (docs/coherence-protocol.md) and the Mac's menus (kinds 24, 25, 27):
// what it hands on and what it sends. The decisions are CoherenceController's and MacMenuState's;
// here only the wire. Kept apart from Session.cs, which calls in at four places (the hello, the
// message switch, the first window list, the Desktop it would pick by itself).
public sealed partial class Session : ICoherenceLink
{
    /// <summary>Kind 4's icon size this PC asks for (Hello.iconSize): the taskbar's and Alt-Tab's
    /// largest, so a Mac app's windows show its own icon there sharp.</summary>
    public const int CoherenceIconSize = 256;

    int menuToken;
    volatile bool coherent;

    /// <summary>In Coherence: no window is picked on purpose (the Desktop is never asked for by
    /// itself), and the windows stream as side streams.</summary>
    public bool Coherent { get => coherent; set => coherent = value; }

    /// <summary>The host can show its windows each as a window of this PC (WindowList's `coherence`
    /// 1 and `sideStreams` ≥ 1).</summary>
    public bool CanCoherence => Windows is { Coherence: >= Capabilities.Coherence, SideStreams: >= 1 };

    /// <summary>The Mac's ID, from the key its TLS door showed; null at a plain door.</summary>
    public string? MacID => connection.Dialed.MacFingerprint is { } fp ? Protocol.MacID.Make(fp) : null;
    /// <summary>The Mac's key in the shell's names (AppUserModelIDs, `--launch`): its ID, or for a
    /// plain door one made from its address.</summary>
    public string MacKey => Aumid.MacKey(MacID, Address);

    /// <summary>The worst round trip of the last second measured, in ms.</summary>
    public double WorstRttMs { get { lock (gate) return worstRtt; } }

    /// <summary>Kinds 30 and 31 (the payload with its window ID, the keyframe flag, the timestamp), on
    /// the reader's thread: straight to the window's video, as kind 1 goes to the stream window's.</summary>
    public event Action<Kind, byte[], bool, double>? SideStreamReceived;
    public event Action<WindowState>? WindowStateReceived;
    public event Action<TakenBack>? TakenBackReceived;
    public event Action<MacMenu>? MacMenuReceived;

    /// <summary>A message on this session (CoherenceController's link; the menus' fetches and choices).</summary>
    public bool Send(Kind kind, byte[] payload) => connection.Send(kind, payload);

    /// <summary>The next token for a kind 27 or 25: strictly increasing on this connection.</summary>
    public int NextMenuToken() => Interlocked.Increment(ref menuToken);

    /// <summary>Once per connection, at its first window list: the Mac's top level now and whenever it
    /// changes (a Mac from before the menus skips the kind and sends nothing).</summary>
    void SubscribeToMenus() => connection.Send(Kind.FetchMenu, Json.Encode(new FetchMenu { Token = NextMenuToken() }));

    void OnCoherenceMessage(Header h, byte[] payload)
    {
        switch ((Kind)h.Kind)
        {
            case Kind.SideParameterSets:
                SideStreamReceived?.Invoke(Kind.SideParameterSets, payload, h.IsKeyframe, h.Timestamp);
                break;
            case Kind.SideFrame:
                {
                    // Counted in the second's figures as the picked stream's frames are, so the stats
                    // the Mac logs and the status line still say what arrives.
                    double age = (Wire.Now - h.Timestamp) * 1000;
                    lock (gate)
                    {
                        frames++;
                        if (double.IsFinite(age)) ages.Add(Math.Clamp(age, 0, 10_000));
                    }
                    SideStreamReceived?.Invoke(Kind.SideFrame, payload, h.IsKeyframe, h.Timestamp);
                }
                break;
            case Kind.WindowState:
                if (Json.Decode<WindowState>(payload) is { } state) WindowStateReceived?.Invoke(state);
                break;
            case Kind.TakenBack:
                if (Json.Decode<TakenBack>(payload) is { } back) TakenBackReceived?.Invoke(back);
                break;
            case Kind.MacMenu:
                if (Json.Decode<MacMenu>(payload) is { } menu) MacMenuReceived?.Invoke(menu);
                break;
        }
    }
}
