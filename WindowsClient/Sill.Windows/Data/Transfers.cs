using Sill.Windows.Protocol;

namespace Sill.Windows.Data;

// Files over the wire (kinds 34–37, docs/coherence-protocol.md; #66's FileDrops on the host): transfer
// IDs this PC picks, a file cut into parts, and the parts of a file coming back put together again by
// ID whatever order and connection they arrive on. Pure bookkeeping (no file is touched here):
// Data/FileService.cs does the reading and writing, and WindowsClient/Sill.Checks checks this file.

/// <summary>The transfer IDs this PC picks: kind 34's ID on what it uploads, kind 37's `transfer` on
/// what it asks the Mac for. They start at a random number, not 1: the host keys a file in parts by
/// its ID across every device it serves and leaves a second device's file of an ID it already has
/// alone (#66's FileDrops.chunk), so two PCs counting from 1 would lose each other's. Never 0, never
/// one still in use. Thread-safe.</summary>
public sealed class TransferIds
{
    uint next;
    readonly HashSet<uint> live = new();
    readonly object gate = new();

    public TransferIds(uint start) { next = start == 0 ? 1 : start; }

    public static TransferIds Random() => new((uint)System.Security.Cryptography.RandomNumberGenerator.GetInt32(1, int.MaxValue));

    public uint Take()
    {
        lock (gate)
        {
            while (next == 0 || live.Contains(next)) next++;
            uint id = next++;
            live.Add(id);
            return id;
        }
    }

    public void Release(uint id) { lock (gate) live.Remove(id); }
    public bool InUse(uint id) { lock (gate) return live.Contains(id); }
}

public static class FileParts
{
    /// <summary>The most a drop or a clipboard copy from this PC sends in all: #66's FileChunk.maxTotal,
    /// which the host refuses beyond.</summary>
    public const ulong MaxDrop = 512UL << 20;
    /// <summary>The most one file from the Mac may be here (the Mac has no cap of its own on what it
    /// sends): a sanity bound on what a staging folder takes.</summary>
    public const ulong MaxDownload = 4UL << 30;
    /// <summary>A part on the bulk connection: the contract's 512 KB.</summary>
    public const int DataPartBytes = FileChunk.MaxBytes;
    /// <summary>A part on the session, when there is no bulk connection: 64 KB, so the input and pings
    /// queued behind it wait a few milliseconds, not the 40 a 512 KB part takes at 100 Mbit/s.</summary>
    public const int SessionPartBytes = 64 * 1024;

    /// <summary>The offsets and lengths of a file's parts, in order. An empty file is one empty part
    /// (the host makes the file at offset 0 and finishes it there, as #66's sender does).</summary>
    public static IEnumerable<(ulong offset, int length)> Split(ulong total, int partBytes)
    {
        if (partBytes <= 0 || partBytes > FileChunk.MaxBytes) throw new ArgumentOutOfRangeException(nameof(partBytes));
        if (total == 0) { yield return (0, 0); yield break; }
        for (ulong o = 0; o < total; o += (ulong)partBytes)
            yield return (o, (int)Math.Min((ulong)partBytes, total - o));
    }

    public static int PartCount(ulong total, int partBytes) => total == 0 ? 1 : (int)((total + (ulong)partBytes - 1) / (ulong)partBytes);
}

/// <summary>One file coming back in parts: which bytes have come (as ranges, so a part that arrives
/// early, or twice, or after the transfer moved to the other connection, is placed where it belongs),
/// how far from the start the bytes run without a gap (what a reader may read), and whether it is
/// whole. The wire has no way to ask for a file from an offset, so a transfer that breaks is asked
/// for again under a new ID; within one, nothing is lost or written twice for an early part.</summary>
public sealed class Reassembly
{
    readonly List<(ulong start, ulong end)> ranges = new();

    public uint Id { get; }
    public string Name { get; }
    public ulong Total { get; }
    /// <summary>Bytes from 0 with no gap.</summary>
    public ulong Contiguous => ranges.Count > 0 && ranges[0].start == 0 ? ranges[0].end : 0;
    public ulong Received { get; private set; }
    public bool Complete => Total == 0 ? sawEmpty : Contiguous == Total;
    bool sawEmpty;

    public Reassembly(uint id, string name, ulong total) { Id = id; Name = name; Total = total; }

    public enum Verdict
    {
        /// <summary>New bytes: write them at their offset.</summary>
        Write,
        /// <summary>Bytes already here: nothing to write.</summary>
        Duplicate,
        /// <summary>The part does not belong (another size, past the end): the transfer is broken.</summary>
        Refused,
    }

    public Verdict Add(ulong offset, int length, ulong total)
    {
        if (total != Total || length < 0 || offset > Total || (ulong)length > Total - offset) return Verdict.Refused;
        if (length == 0)
        {
            if (Total != 0 || offset != 0) return Verdict.Duplicate;
            bool first = !sawEmpty;
            sawEmpty = true;
            return first ? Verdict.Write : Verdict.Duplicate;
        }
        ulong s = offset, e = offset + (ulong)length;
        if (Covered(s, e)) return Verdict.Duplicate;
        // Merge [s, e) into the sorted, disjoint ranges.
        int i = 0;
        while (i < ranges.Count && ranges[i].end < s) i++;
        ulong ns = s, ne = e;
        int j = i;
        while (j < ranges.Count && ranges[j].start <= e)
        {
            ns = Math.Min(ns, ranges[j].start);
            ne = Math.Max(ne, ranges[j].end);
            j++;
        }
        ulong had = 0;
        for (int k = i; k < j; k++) had += ranges[k].end - ranges[k].start;
        ranges.RemoveRange(i, j - i);
        ranges.Insert(i, (ns, ne));
        Received += (ne - ns) - had;
        return Verdict.Write;
    }

    bool Covered(ulong s, ulong e) => ranges.Any(r => r.start <= s && e <= r.end);

    /// <summary>The ranges as they stand, for the checks.</summary>
    public IReadOnlyList<(ulong start, ulong end)> Ranges => ranges;
}

/// <summary>The files this PC asked for, by transfer ID: a part of an ID it never asked for, or of one
/// it cancelled, is dropped (a Mac sends a device only what it asked for). Thread-safe: parts come on
/// the session's reader and the bulk connection's.</summary>
public sealed class IncomingParts
{
    readonly Dictionary<uint, Reassembly?> expected = new();
    readonly Queue<uint> cancelled = new();
    readonly HashSet<uint> cancelledSet = new();
    readonly object gate = new();
    /// <summary>How many cancelled IDs are remembered, so their late parts are told apart from strays.</summary>
    public const int RememberCancelled = 256;

    public enum Outcome { Write, WriteAndComplete, Duplicate, Unknown, Cancelled, Refused }

    public readonly record struct Routed(Outcome Outcome, Reassembly? File, string? Why = null);

    /// <summary>A file asked for (kind 37 sent, or about to be): its parts are taken from now on.</summary>
    public void Expect(uint id) { lock (gate) expected[id] = null; }

    public void Cancel(uint id)
    {
        lock (gate)
        {
            if (!expected.Remove(id)) return;
            if (cancelledSet.Add(id)) cancelled.Enqueue(id);
            while (cancelled.Count > RememberCancelled) cancelledSet.Remove(cancelled.Dequeue());
        }
    }

    /// <summary>Forgets a file finished or given up (its later parts then count as unknown).</summary>
    public void Forget(uint id) { lock (gate) expected.Remove(id); }

    public bool Expects(uint id) { lock (gate) return expected.ContainsKey(id); }

    /// <summary>Where a part goes. The first part fixes the file's name and size; a size over
    /// <paramref name="cap"/> refuses the transfer.</summary>
    public Routed Route(uint id, string name, ulong total, ulong offset, int length, ulong cap = FileParts.MaxDownload)
    {
        lock (gate)
        {
            if (!expected.TryGetValue(id, out var file))
                return new(cancelledSet.Contains(id) ? Outcome.Cancelled : Outcome.Unknown, null);
            if (file == null)
            {
                if (total > cap) return new(Outcome.Refused, null, $"{total:N0} bytes, over the {cap:N0} a file may be");
                file = new Reassembly(id, name, total);
                expected[id] = file;
            }
            var v = file.Add(offset, length, total);
            return v switch
            {
                Reassembly.Verdict.Write => new(file.Complete ? Outcome.WriteAndComplete : Outcome.Write, file),
                Reassembly.Verdict.Duplicate => new(Outcome.Duplicate, file),
                _ => new(Outcome.Refused, file, total != file.Total ? $"its size changed from {file.Total:N0} to {total:N0}" : "a part past its end"),
            };
        }
    }
}
