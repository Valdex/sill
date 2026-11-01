using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Sill.Windows.Protocol;

namespace Sill.Windows.Net;

/// <summary>This PC's identity for the Mac's TLS doors (RemoteIdentity.swift on the device): one P-256
/// key, made once and kept non-exportable in Windows' key storage for this user (CNG, protected by
/// DPAPI), and a self-signed certificate rebuilt from it at every launch, which names and dates
/// nothing that matters: the Mac pins the key, as SHA-256 of its SubjectPublicKeyInfo.</summary>
public sealed class Identity
{
    const string KeyName = "Sill for Windows device key";

    public X509Certificate2 Certificate { get; }
    public byte[] Fingerprint { get; }

    Identity(X509Certificate2 cert)
    {
        Certificate = cert;
        Fingerprint = Spki.Fingerprint(cert.RawData) ?? throw new CryptographicException("The device key is not P-256.");
    }

    static Identity? shared;

    public static Identity Load()
    {
        if (shared != null) return shared;
        CngKey key;
        if (CngKey.Exists(KeyName))
        {
            key = CngKey.Open(KeyName);
        }
        else
        {
            key = CngKey.Create(CngAlgorithm.ECDsaP256, KeyName, new CngKeyCreationParameters
            {
                ExportPolicy = CngExportPolicies.None,
                KeyCreationOptions = CngKeyCreationOptions.None,
                KeyUsage = CngKeyUsages.Signing,
            });
        }
        using var ecdsa = new ECDsaCng(key);
        var cn = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
        var request = new CertificateRequest($"CN={cn}", ecdsa, HashAlgorithmName.SHA256);
        var cert = request.CreateSelfSigned(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                                            new DateTimeOffset(2099, 12, 31, 23, 59, 59, TimeSpan.Zero));
        shared = new Identity(cert);
        return shared;
    }

    /// <summary>Forgets the key: every Mac then sees a new device (pair again).</summary>
    public static void Reset()
    {
        shared = null;
        if (CngKey.Exists(KeyName)) using (var k = CngKey.Open(KeyName)) k.Delete();
    }

    /// <summary>What the Mac shows for this PC: the computer's name as its owner set it.</summary>
    public static string DeviceName
    {
        get
        {
            try
            {
                var n = System.Net.Dns.GetHostName();
                if (!string.IsNullOrWhiteSpace(n)) return n;
            }
            catch (System.Net.Sockets.SocketException) { }
            return Environment.MachineName;
        }
    }
}
