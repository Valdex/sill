namespace Sill.Windows.Data;

/// <summary>How fast this PC's file parts go out, so the Mac's picture keeps coming first (the plan's
/// "video first"). Two limits, both pure and on a clock the caller gives (seconds):
/// <list type="bullet">
/// <item>a rate, by the session's ping round trips: the lowest of the last 30 s is the baseline, and
/// one 15 ms or more above it means a queue is building somewhere between the Mac and this PC (Wi-Fi's
/// airtime is shared both ways, a router's buffer fills): the rate halves, at most every half second;
/// with round trips at the baseline it grows by a quarter every half second, up to <see cref="MaxRate"/>.
/// A token bucket spends it: a part waits until the bytes before it have had their time.</item>
/// <item>bytes in flight: what the connection has queued and not yet written to its socket. A part
/// goes only while that is under <see cref="InFlightCap"/>, so parts never pile up in this process
/// ahead of the input and pings that share the session.</item>
/// </list>
/// Not thread-safe: the upload's pump owns it, and round trips are handed to it under its lock.</summary>
public sealed class UploadPacer
{
    /// <summary>A round trip this far above the baseline backs the rate off (the plan's 15 ms).</summary>
    public const double BackoffMs = 15;
    public const double StartRate = 8e6, MinRate = 256e3, MaxRate = 250e6;
    /// <summary>How long a round trip counts towards the baseline.</summary>
    public const double BaselineWindow = 30;
    /// <summary>The least time between two changes of the rate.</summary>
    public const double Step = 0.5;

    readonly Queue<(double at, double ms)> samples = new();
    double lastChange = double.NegativeInfinity;
    double nextFree;

    public double Rate { get; private set; } = StartRate;
    /// <summary>The lowest round trip of the last <see cref="BaselineWindow"/> seconds; NaN before any.</summary>
    public double Baseline { get; private set; } = double.NaN;
    /// <summary>The last round trip was over the baseline by <see cref="BackoffMs"/> or more.</summary>
    public bool Congested { get; private set; }
    /// <summary>How many times the rate was halved, for the log.</summary>
    public int Backoffs { get; private set; }
    public long InFlightCap { get; }

    /// <param name="partBytes">The connection's part size.</param>
    /// <param name="partsInFlight">How many may be queued at once: two on the session (input and pings
    /// share it); four on the bulk connection, where only the timer's granularity (a wait is 15 ms on
    /// Windows) would otherwise hold a gigabit link back.</param>
    public UploadPacer(int partBytes, int partsInFlight = 2) { InFlightCap = (long)partsInFlight * partBytes; }

    public void OnRoundTrip(double ms, double now)
    {
        if (!double.IsFinite(ms) || ms < 0) return;
        samples.Enqueue((now, ms));
        while (samples.Count > 0 && now - samples.Peek().at > BaselineWindow) samples.Dequeue();
        Baseline = samples.Min(s => s.ms);
        Congested = ms >= Baseline + BackoffMs;
        if (now - lastChange < Step) return;
        if (Congested)
        {
            Rate = Math.Max(MinRate, Rate / 2);
            Backoffs++;
            lastChange = now;
        }
        else if (Rate < MaxRate)
        {
            Rate = Math.Min(MaxRate, Rate * 1.25);
            lastChange = now;
        }
    }

    /// <summary>Seconds to wait before a part of <paramref name="bytes"/> may go, with
    /// <paramref name="inFlight"/> bytes queued on its connection: 0 to send now.</summary>
    public double Wait(int bytes, long inFlight, double now)
    {
        // Whatever the rate, a part never goes while the connection holds more than one before it.
        if (inFlight > 0 && inFlight + bytes > InFlightCap) return 0.005;
        return Math.Max(0, nextFree - now);
    }

    /// <summary>A part of <paramref name="bytes"/> went at <paramref name="now"/>.</summary>
    public void Sent(int bytes, double now)
    {
        // A pause (nothing to send) earns no credit: the bucket holds at most a part's worth.
        nextFree = Math.Max(nextFree, now) + bytes / Rate;
    }
}
