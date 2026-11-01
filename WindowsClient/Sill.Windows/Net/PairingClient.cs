using System.Net;
using System.Security.Cryptography;
using Sill.Windows.Protocol;

namespace Sill.Windows.Net;

/// <summary>Pairing at a Mac's home door (docs/home-pairing-plan.md §7.5): one connection with ALPN
/// sill-pair/1 per step, one kind 19 out and one kind 20 back. An ask ("pair me") makes the Mac put up
/// its pairing window; the 12 digits it shows then make the key both proofs bind, each side taking
/// the other's key from the TLS session, never from a message.</summary>
public static class PairingClient
{
    public sealed record Answer(PairResult Result, byte[] MacFingerprint);

    static async Task<Answer> Exchange(IReadOnlyList<IPEndPoint> endpoints, byte[]? pin, PairRequest request, CancellationToken ct)
    {
        using var d = await Transport.Dial(endpoints, Transport.PairingALPN, pin, ct);
        var message = Wire.Message(Kind.PairRequest, Json.Encode(request));
        await d.Stream.WriteAsync(message, ct);
        await d.Stream.FlushAsync(ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var header = new byte[Header.Length];
        while (true)
        {
            await d.Stream.ReadExactlyAsync(header, timeout.Token);
            var h = Header.Parse(header);
            if (h.PayloadLength > 64 * 1024) throw new InvalidDataException("The Mac's answer is too large.");
            var payload = new byte[h.PayloadLength];
            await d.Stream.ReadExactlyAsync(payload, timeout.Token);
            if (h.Kind != (byte)Kind.PairResult) continue;
            var r = Json.Decode<PairResult>(payload) ?? throw new InvalidDataException("The Mac's answer does not decode.");
            return new Answer(r, d.MacFingerprint!);
        }
    }

    static PairRequest Request(string method, string proof = "") => new()
    {
        V = 1, Method = method, Proof = proof, Name = Identity.DeviceName, Model = "Windows PC",
    };

    /// <summary>"Pair me": the Mac shows its code ("shown"), asks its user to open the window
    /// ("openOnMac"), is locked, or refuses. Any P-256 key: this PC has none to pin yet.</summary>
    public static Task<Answer> Ask(IReadOnlyList<IPEndPoint> endpoints, CancellationToken ct) =>
        Exchange(endpoints, null, Request("ask"), ct);

    /// <summary>The device's Cancel after "shown": the Mac's window for this ask closes.</summary>
    public static async Task Cancel(IReadOnlyList<IPEndPoint> endpoints, byte[] macFingerprint)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await Exchange(endpoints, macFingerprint, Request("cancel"), cts.Token);
        }
        catch (Exception) { }
    }

    /// <summary>The typed code, pinned to the key the ask saw. On ok, the Mac's proof is checked and
    /// the Mac saved; the result says why not otherwise.</summary>
    public static async Task<(PairResult result, SavedMac? saved)> PairWithCode(IReadOnlyList<IPEndPoint> endpoints, byte[] macFingerprint,
                                                                             string code, string bonjourName, CancellationToken ct)
    {
        var identity = Identity.Load();
        var key = await Task.Run(() => PairingProof.CodeKey(code, macFingerprint), ct);
        var proof = PairingProof.DeviceProof(key, identity.Fingerprint, macFingerprint);
        var answer = await Exchange(endpoints, macFingerprint, Request("code", Base64Url.Encode(proof)), ct);
        var r = answer.Result;
        if (!r.Ok) return (r, null);
        var expected = PairingProof.MacProof(key, answer.MacFingerprint, identity.Fingerprint);
        var got = Base64Url.Decode(r.Proof);
        if (got == null || !CryptographicOperations.FixedTimeEquals(got, expected))
            return (new PairResult { Ok = false, Reason = "proof", Message = "The Mac's answer did not prove it knew the code." }, null);
        if (r.MacID != MacID.Make(answer.MacFingerprint))
            return (new PairResult { Ok = false, Reason = "proof", Message = "The Mac's answer named another key." }, null);
        var saved = new SavedMac
        {
            MacID = r.MacID!,
            Fingerprint = Base64Url.Encode(answer.MacFingerprint),
            Name = string.IsNullOrWhiteSpace(r.Name) ? bonjourName : r.Name!,
            RecognitionKey = r.RecognitionKey,
            BonjourName = bonjourName,
            PairedAt = DateTimeOffset.Now,
        };
        SavedMacs.Update(s =>
        {
            s.Macs.RemoveAll(m => m.MacID == saved.MacID || m.BonjourName == bonjourName && m.Fingerprint != saved.Fingerprint);
            s.Macs.Add(saved);
        });
        return (r, saved);
    }

    /// <summary>A refusal in words.</summary>
    public static string Describe(PairResult r, string mac) => r.Reason switch
    {
        "code" => r.TriesLeft is int n && n > 0 ? $"That code didn’t match. {n} {(n == 1 ? "try" : "tries")} left." : "That code didn’t match.",
        "expired" => $"The code on {mac} expired. Try again for a new one.",
        "stopped" => $"{mac} closed its pairing window. Try again.",
        "closed" => $"{mac} isn’t showing a pairing code. Try again.",
        "busy" => r.RetryAfter is double s ? $"{mac} is busy. Try again in {Math.Ceiling(s)} s." : $"{mac} is busy. Try again in a moment.",
        "locked" => $"Unlock {mac}, then try again.",
        "openOnMac" => $"Choose Pair iPhone or iPad… from Sill’s menu on {mac}, then type the code it shows.",
        "shown" => $"{mac} shows a code now.",
        "proof" => r.Message ?? "Pairing failed.",
        _ => string.IsNullOrWhiteSpace(r.Message) ? $"{mac} refused ({r.Reason})." : r.Message!,
    };
}
