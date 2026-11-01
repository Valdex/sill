using System.Security.Cryptography;
using System.Text;

namespace Sill.Windows.Protocol;

// Pairing and identities as Sources/StreamProtocol/Pairing.swift and RemoteIdentity.swift define
// them: the key is the identity, pinned as SHA-256 of its SubjectPublicKeyInfo; the 12-digit code
// ends in a Damm check digit; the proofs bind both keys as the TLS session saw them.

public static class Base64Url
{
    public static string Encode(ReadOnlySpan<byte> data) =>
        Convert.ToBase64String(data).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    /// <summary>Null for anything that is not canonical unpadded base64url.</summary>
    public static byte[]? Decode(string? text)
    {
        if (text == null || text.Length % 4 == 1) return null;
        foreach (var c in text)
            if (!(c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_')) return null;
        var s = text.Replace('-', '+').Replace('_', '/');
        s += new string('=', (4 - s.Length % 4) % 4);
        try
        {
            var d = Convert.FromBase64String(s);
            return Encode(d) == text ? d : null;
        }
        catch (FormatException) { return null; }
    }
}

public static class MacID
{
    const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>Crockford base32 of the fingerprint's first 10 bytes: 16 characters.</summary>
    public static string Make(ReadOnlySpan<byte> fingerprint)
    {
        if (fingerprint.Length < 10) return "";
        var sb = new StringBuilder(16);
        int buffer = 0, bits = 0;
        for (int i = 0; i < 10; i++)
        {
            buffer = (buffer << 8) | fingerprint[i];
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                sb.Append(Alphabet[(buffer >> bits) & 31]);
            }
            buffer &= (1 << bits) - 1;
        }
        return sb.ToString();
    }
}

public static class Spki
{
    /// <summary>SubjectPublicKeyInfo for a P-256 point 04‖X‖Y: the DER every pin hashes.</summary>
    public static readonly byte[] P256Prefix = Convert.FromHexString("3059301306072a8648ce3d020106082a8648ce3d030107034200");

    /// <summary>A certificate's SubjectPublicKeyInfo, exactly as it sits in the DER (the TBSCertificate's
    /// sixth field after the optional [0] version), so the fingerprint never depends on a re-encoding.</summary>
    public static byte[]? FromCertificate(ReadOnlySpan<byte> der)
    {
        try
        {
            var (_, certStart, _) = Tlv(der, 0);
            var (_, tbsStart, tbsEnd) = Tlv(der, certStart);
            var fields = new List<(byte tag, int start, int end)>();
            int i = tbsStart;
            while (i < tbsEnd)
            {
                var (tag, _, end) = Tlv(der, i);
                fields.Add((tag, i, end));
                i = end;
            }
            if (fields.Count > 0 && fields[0].tag == 0xA0) fields.RemoveAt(0);
            if (fields.Count < 6) return null;
            return der[fields[5].start..fields[5].end].ToArray();
        }
        catch (ArgumentOutOfRangeException) { return null; }
        catch (IndexOutOfRangeException) { return null; }
    }

    static (byte tag, int contentStart, int end) Tlv(ReadOnlySpan<byte> b, int i)
    {
        byte tag = b[i];
        int n = b[i + 1];
        i += 2;
        if ((n & 0x80) != 0)
        {
            int k = n & 0x7F;
            n = 0;
            for (int j = 0; j < k; j++) n = (n << 8) | b[i + j];
            i += k;
        }
        if (i + n > b.Length) throw new ArgumentOutOfRangeException(nameof(b));
        return (tag, i, i + n);
    }

    /// <summary>The fingerprint of a certificate's key when it is P-256, else null (which no pin matches).</summary>
    public static byte[]? Fingerprint(ReadOnlySpan<byte> certificateDer)
    {
        var spki = FromCertificate(certificateDer);
        if (spki == null || spki.Length != P256Prefix.Length + 65 || !spki.AsSpan(0, P256Prefix.Length).SequenceEqual(P256Prefix)) return null;
        return SHA256.HashData(spki);
    }

    public static byte[] FingerprintOfPoint(ReadOnlySpan<byte> point65)
    {
        var d = new byte[P256Prefix.Length + point65.Length];
        P256Prefix.CopyTo(d, 0);
        point65.CopyTo(d.AsSpan(P256Prefix.Length));
        return SHA256.HashData(d);
    }
}

public static class PairingCode
{
    static readonly int[,] Table =
    {
        { 0, 3, 1, 7, 5, 9, 8, 6, 4, 2 }, { 7, 0, 9, 2, 1, 5, 4, 8, 6, 3 }, { 4, 2, 0, 6, 8, 7, 1, 3, 5, 9 },
        { 1, 7, 5, 0, 9, 8, 3, 4, 2, 6 }, { 6, 1, 2, 3, 0, 4, 5, 9, 7, 8 }, { 3, 6, 7, 4, 2, 0, 9, 5, 8, 1 },
        { 5, 8, 6, 9, 7, 2, 0, 1, 3, 4 }, { 8, 9, 4, 5, 3, 6, 2, 0, 1, 7 }, { 9, 4, 3, 8, 6, 1, 7, 2, 0, 5 },
        { 2, 5, 8, 1, 4, 3, 6, 7, 9, 0 },
    };

    public enum Problem { None, Length, Typo }

    /// <summary>The input as a code: spaces and hyphens removed, 12 ASCII digits, the check digit valid.</summary>
    public static (string? code, Problem problem) Check(string input)
    {
        var kept = new string(input.Where(c => c != ' ' && c != '-' && c != ' ').ToArray());
        if (kept.Length != 12 || !kept.All(c => c is >= '0' and <= '9')) return (null, Problem.Length);
        int interim = 0;
        foreach (var c in kept) interim = Table[interim, c - '0'];
        return interim == 0 ? (kept, Problem.None) : (null, Problem.Typo);
    }
}

public static class PairingProof
{
    const string Label = "sill-pair-v1";

    /// <summary>The typed path's key: PBKDF2-HMAC-SHA256(the 12 digits, "sill-pair-v1" ‖ fp_mac, 600,000, 32).</summary>
    public static byte[] CodeKey(string code, byte[] macFingerprint)
    {
        var salt = Encoding.ASCII.GetBytes(Label).Concat(macFingerprint).ToArray();
        return Rfc2898DeriveBytes.Pbkdf2(Encoding.ASCII.GetBytes(code), salt, 600_000, HashAlgorithmName.SHA256, 32);
    }

    public static byte[] DeviceProof(byte[] key, byte[] deviceFingerprint, byte[] macFingerprint) =>
        HMACSHA256.HashData(key, Message("device", deviceFingerprint, macFingerprint));

    public static byte[] MacProof(byte[] key, byte[] macFingerprint, byte[] deviceFingerprint) =>
        HMACSHA256.HashData(key, Message("mac", macFingerprint, deviceFingerprint));

    static byte[] Message(string who, byte[] first, byte[] second)
    {
        var m = new List<byte>(Encoding.ASCII.GetBytes($"{Label} {who}")) { 0 };
        m.AddRange(first);
        m.AddRange(second);
        return m.ToArray();
    }
}

/// <summary>The Bonjour TXT record's `r`: p(6) ‖ HMAC-SHA256(recognitionKey, "sill-tag-v1" ‖ p)[0..6].</summary>
public static class RecognitionTag
{
    public static bool Matches(string? tag, byte[]? recognitionKey)
    {
        if (recognitionKey == null || recognitionKey.Length == 0) return false;
        var bytes = Base64Url.Decode(tag);
        if (bytes == null || bytes.Length != 12) return false;
        var message = Encoding.ASCII.GetBytes("sill-tag-v1").Concat(bytes.Take(6)).ToArray();
        var expected = HMACSHA256.HashData(recognitionKey, message);
        return CryptographicOperations.FixedTimeEquals(expected.AsSpan(0, 6), bytes.AsSpan(6, 6));
    }
}

public static class MacInfoCheck
{
    /// <summary>Kind 18 when the signature verifies over the exact info bytes and its macID is the
    /// signing key's: the MacInfo and that key's fingerprint.</summary>
    public static (MacInfo info, byte[] fingerprint)? Verify(SignedMacInfo s)
    {
        try
        {
            var info = Convert.FromBase64String(s.Info);
            var point = Convert.FromBase64String(s.Key);
            var sig = Convert.FromBase64String(s.Sig);
            if (point.Length != 65 || point[0] != 4) return null;
            using var key = ECDsa.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = point[1..33], Y = point[33..65] },
            });
            if (!key.VerifyData(info, sig, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence)) return null;
            var decoded = Json.Decode<MacInfo>(info);
            var fp = Spki.FingerprintOfPoint(point);
            if (decoded == null || decoded.MacID != MacID.Make(fp)) return null;
            return (decoded, fp);
        }
        catch (Exception e) when (e is FormatException or CryptographicException) { return null; }
    }
}
