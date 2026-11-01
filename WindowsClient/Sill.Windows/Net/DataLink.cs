using System.Net;
using Sill.Windows.Data;
using Sill.Windows.Protocol;

namespace Sill.Windows.Net;

/// <summary>The bulk connection (`sill-data/1`, docs/coherence-protocol.md): a second connection to the
/// session's door, so a file's parts never queue in front of the Mac's picture, input and pings on the
/// session. Dialed as the session was (Transport.Dial: the same address and port, TLS 1.3 with this
/// PC's certificate and the Mac's key pinned to the one the session saw, or plain for a plain door),
/// then kind 49 with the session's ticket as its first message. Its reader and writer are a
/// Connection's with the bulk connection's cap on what this side sends (1 MiB + 64 bytes); the host
/// closes it with the session, and Data/FileService.cs closes it when the session ends.</summary>
public sealed class DataLink : IDisposable
{
    readonly Connection connection;

    /// <summary>Each message (kind 34 parts), on the reader's thread.</summary>
    public event Action<Header, byte[]>? Received;
    /// <summary>Once: null after Close(), else what ended it.</summary>
    public event Action<Exception?>? Ended;

    public bool IsOpen => connection.IsOpen;
    /// <summary>Bytes queued and not yet written (the upload's pacer waits on it).</summary>
    public long QueuedBytes => connection.QueuedBytes;

    DataLink(Connection c)
    {
        connection = c;
        c.Received += (h, p) => Received?.Invoke(h, p);
        c.Ended += e => Ended?.Invoke(e);
    }

    /// <summary>Dials and says the ticket. <paramref name="pin"/> null: the session's door was plain.</summary>
    public static async Task<DataLink> Open(IPEndPoint endpoint, byte[]? pin, string token, CancellationToken ct)
    {
        var dialed = await Transport.Dial(new[] { endpoint }, pin == null ? null : DataHello.Alpn, pin, ct);
        var link = new DataLink(new Connection(dialed, Wire.MaxDataPayload));
        link.connection.Start();
        // The first message, within DataHello.Within seconds of the connection: nothing else is queued yet.
        if (!link.connection.Send(Kind.DataHello, DataLinkPolicy.Hello(token)))
        {
            link.Dispose();
            throw new IOException("The bulk connection closed before its hello went.");
        }
        return link;
    }

    public bool Send(Kind kind, ReadOnlySpan<byte> payload) => connection.Send(kind, payload);

    public void Close() => connection.Close();
    public void Dispose() => connection.Close();
}
