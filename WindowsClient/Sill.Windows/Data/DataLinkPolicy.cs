using Sill.Windows.Protocol;

namespace Sill.Windows.Data;

/// <summary>The bulk connection's rules (`sill-data/1`, kinds 48 and 49, docs/coherence-protocol.md):
/// when this PC opens it, what it sends first, when it tries again, and which connection a part goes
/// on. Pure; WindowsClient/Sill.Checks checks it, Net/DataLink.cs and Data/FileService.cs follow it.</summary>
public static class DataLinkPolicy
{
    /// <summary>The token's bytes: base64 of 32 random bytes.</summary>
    public const int TokenBytes = 32;
    /// <summary>The waits before each try after a failed one (the first try goes at the ticket).</summary>
    public static readonly double[] Retries = { 1, 3, 10 };

    /// <summary>Whether a kind 48 opens the connection: only from a host whose list says `data` 1 (this
    /// PC's hello said `coherence` 1, or no ticket would have come), and only for a token that reads as
    /// 32 bytes of base64 (anything else is not a ticket the host made, and dialing with it would only
    /// be refused).</summary>
    public static bool Opens(int? hostData, DataTicket? ticket) =>
        (hostData ?? 0) >= Capabilities.Data && ticket != null && TokenValid(ticket.Token);

    public static bool TokenValid(string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 64) return false;
        var buffer = new byte[48];
        return Convert.TryFromBase64String(token, buffer, out int n) && n == TokenBytes;
    }

    /// <summary>Kind 49, the first message on the connection, within DataHello.Within seconds.</summary>
    public static byte[] Hello(string token) => Json.Encode(new DataHello { Token = token });

    /// <summary>The wait before try <paramref name="failures"/> + 1, or null once the tries are spent:
    /// the host admits one bulk connection per session, so a host that keeps refusing is left alone and
    /// the parts go on the session, as with a host without it.</summary>
    public static double? NextTry(int failures) => failures >= 1 && failures <= Retries.Length ? Retries[failures - 1] : null;

    /// <summary>Which connection a new transfer's parts go on, and their size. A transfer stays on the
    /// connection it started on: the host keys a file in parts by the connection that began it (#66's
    /// FileDrops), so parts that moved would be dropped; one whose connection goes starts again under
    /// a new ID on the other (<see cref="Restart"/>).</summary>
    public static (bool onData, int partBytes) Route(bool dataUp) =>
        dataUp ? (true, FileParts.DataPartBytes) : (false, FileParts.SessionPartBytes);

    /// <summary>A transfer whose connection ended mid-way, while the session lives: begun again, from
    /// its first byte, under a new ID, on what is up now.</summary>
    public static bool Restart(bool startedOnData, bool dataUp, bool sessionUp) => sessionUp && startedOnData && !dataUp;
}
