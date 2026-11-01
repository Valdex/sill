using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Sill.Windows.Protocol;

namespace Sill.Windows.Net;

public enum DialFailure
{
    /// <summary>Nothing answered at any address.</summary>
    NoAnswer,
    /// <summary>The Mac's key is not the one this PC paired with (a Mac set up again, or another Mac).</summary>
    PinRefused,
    /// <summary>TLS did not complete: not a Sill door, a door that refused this PC's key, or TLS 1.3 missing.</summary>
    Handshake,
}

public sealed class DialException : Exception
{
    public DialFailure Failure { get; }
    public DialException(DialFailure failure, string message, Exception? inner = null) : base(message, inner) { Failure = failure; }
}

/// <summary>A connection to a Mac's door: plain TCP, or TLS 1.3 with this PC's certificate and the Mac's
/// key checked against a pin (RemoteTLS.swift's client: one ALPN, SNI "sill", no name or chain check).</summary>
public sealed class Dialed : IDisposable
{
    public required TcpClient Tcp { get; init; }
    public required Stream Stream { get; init; }
    public required IPEndPoint Endpoint { get; init; }
    /// <summary>The Mac's key as this connection saw it; null on a plain door.</summary>
    public byte[]? MacFingerprint { get; init; }
    public bool Tls => MacFingerprint != null;

    public void Dispose()
    {
        try { Stream.Dispose(); } catch (IOException) { }
        Tcp.Dispose();
    }
}

public static class Transport
{
    public const string SessionALPN = "sill/1";
    public const string PairingALPN = "sill-pair/1";

    /// <summary>Dials each endpoint in turn until one connects. <paramref name="alpn"/> null: plain TCP.
    /// <paramref name="pin"/> null: any P-256 key (an open door, an ask, the typed pairing path).</summary>
    public static async Task<Dialed> Dial(IReadOnlyList<IPEndPoint> endpoints, string? alpn, byte[]? pin, CancellationToken ct)
    {
        Exception? last = null;
        bool pinRefused = false, handshake = false;
        foreach (var ep in endpoints)
        {
            ct.ThrowIfCancellationRequested();
            var tcp = new TcpClient(ep.AddressFamily) { NoDelay = true };
            try
            {
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(5));
                    await tcp.ConnectAsync(ep, timeout.Token);
                }
                KeepAlive(tcp.Client);
                if (alpn == null)
                    return new Dialed { Tcp = tcp, Stream = tcp.GetStream(), Endpoint = ep };
                byte[]? seen = null;
                bool refused = false;
                var ssl = new SslStream(tcp.GetStream(), false, (_, cert, _, _) =>
                {
                    // The pin is the whole of trust: no chain, name or date is looked at.
                    var fp = cert == null ? null : Spki.Fingerprint(cert.GetRawCertData());
                    seen = fp;
                    if (fp == null) return false;
                    if (pin != null && !fp.AsSpan().SequenceEqual(pin)) { refused = true; return false; }
                    return true;
                });
                var identity = Identity.Load();
                var options = new SslClientAuthenticationOptions
                {
                    TargetHost = "sill",
                    ApplicationProtocols = new List<SslApplicationProtocol> { new(alpn) },
                    EnabledSslProtocols = SslProtocols.Tls13,
                    ClientCertificates = new X509CertificateCollection { identity.Certificate },
                    LocalCertificateSelectionCallback = (_, _, _, _, _) => identity.Certificate,
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                };
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeout.CancelAfter(TimeSpan.FromSeconds(10));
                    await ssl.AuthenticateAsClientAsync(options, timeout.Token);
                }
                catch (Exception e) when (e is AuthenticationException or IOException or System.ComponentModel.Win32Exception)
                {
                    ssl.Dispose();
                    tcp.Dispose();
                    if (refused) { pinRefused = true; last = e; continue; }
                    handshake = true; last = e;
                    continue;
                }
                if (ssl.NegotiatedApplicationProtocol.ToString() != alpn)
                {
                    ssl.Dispose(); tcp.Dispose();
                    handshake = true; last = new AuthenticationException($"The Mac did not agree to {alpn}.");
                    continue;
                }
                return new Dialed { Tcp = tcp, Stream = ssl, Endpoint = ep, MacFingerprint = seen };
            }
            catch (Exception e) when (e is SocketException or OperationCanceledException or IOException)
            {
                tcp.Dispose();
                if (ct.IsCancellationRequested) throw;
                last = e;
            }
        }
        if (pinRefused) throw new DialException(DialFailure.PinRefused, "The Mac answered with another key.", last);
        if (handshake) throw new DialException(DialFailure.Handshake, last?.Message ?? "TLS did not complete.", last);
        throw new DialException(DialFailure.NoAnswer, last?.Message ?? "No address answered.", last);
    }

    static void KeepAlive(Socket s)
    {
        try
        {
            s.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            s.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 5);
            s.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 2);
            s.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 3);
        }
        catch (SocketException) { }
    }

    /// <summary>The endpoints of a listed Mac, or of a typed address ("host:port", "[v6]:port").</summary>
    public static List<IPEndPoint> Endpoints(MacService svc) => svc.Addresses.Select(a => new IPEndPoint(a, svc.Port)).ToList();

    public static async Task<List<IPEndPoint>> Resolve(string text, CancellationToken ct)
    {
        text = text.Trim();
        string host; int port;
        if (IPEndPoint.TryParse(text, out var ep) && ep.Port != 0) return new() { ep };
        int colon = text.LastIndexOf(':');
        if (colon <= 0 || !int.TryParse(text[(colon + 1)..], out port) || port is < 1 or > 65535)
            throw new FormatException("Type the Mac's address and Sill's port, like 192.168.1.20:52345.");
        host = text[..colon].Trim('[', ']');
        var addrs = await Dns.GetHostAddressesAsync(host, ct);
        return addrs.OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1).Select(a => new IPEndPoint(a, port)).ToList();
    }
}
