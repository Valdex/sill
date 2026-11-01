using System.Buffers.Binary;
using System.Threading.Channels;
using Sill.Windows.Protocol;

namespace Sill.Windows.Net;

/// <summary>Messages over one dialed connection: a reader that hands each message on as it arrives
/// (with the caps a device puts on what a Mac may announce) and a writer that sends in order from any
/// thread. Ends once, with the reason.</summary>
public sealed class Connection : IDisposable
{
    readonly Dialed dialed;
    readonly Channel<byte[]> outgoing = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });
    readonly CancellationTokenSource stop = new();
    int ended;
    long lastReceivedTicks;

    /// <summary>Each message, on the reader's thread.</summary>
    public event Action<Header, byte[]>? Received;
    /// <summary>Once: null after Close(), else what ended it.</summary>
    public event Action<Exception?>? Ended;

    public Dialed Dialed => dialed;
    public DateTime LastReceived => new(Interlocked.Read(ref lastReceivedTicks), DateTimeKind.Utc);
    public bool IsOpen => Volatile.Read(ref ended) == 0;

    public Connection(Dialed dialed)
    {
        this.dialed = dialed;
        lastReceivedTicks = DateTime.UtcNow.Ticks;
    }

    public void Start()
    {
        _ = Task.Run(ReadLoop);
        _ = Task.Run(WriteLoop);
    }

    public void Send(Kind kind, ReadOnlySpan<byte> payload, bool keyframe = false)
    {
        if (payload.Length > Wire.MaxClientPayload || !IsOpen) return;
        outgoing.Writer.TryWrite(Wire.Message(kind, payload, keyframe));
    }

    async Task WriteLoop()
    {
        try
        {
            await foreach (var m in outgoing.Reader.ReadAllAsync(stop.Token))
            {
                // Small messages (input) go out at once, each its own TLS record; frames come the other way.
                await dialed.Stream.WriteAsync(m, stop.Token);
                await dialed.Stream.FlushAsync(stop.Token);
            }
        }
        catch (OperationCanceledException) { End(null); }
        catch (Exception e) { End(e); }
    }

    async Task ReadLoop()
    {
        var header = new byte[Header.Length];
        try
        {
            while (!stop.IsCancellationRequested)
            {
                await dialed.Stream.ReadExactlyAsync(header, stop.Token);
                Interlocked.Exchange(ref lastReceivedTicks, DateTime.UtcNow.Ticks);
                var h = Header.Parse(header);
                int cap = h.Kind == (byte)Kind.Frame ? Wire.MaxFramePayload : Wire.MaxOtherHostPayload;
                if (h.PayloadLength > cap) throw new InvalidDataException($"A message of kind {h.Kind} announced {h.PayloadLength} bytes.");
                var payload = new byte[h.PayloadLength];
                int got = 0;
                // In pieces, each a sign of life (MessageReader on the device): a keyframe on a slow link
                // takes seconds to arrive.
                while (got < payload.Length)
                {
                    int n = await dialed.Stream.ReadAsync(payload.AsMemory(got, Math.Min(payload.Length - got, 256 * 1024)), stop.Token);
                    if (n == 0) throw new EndOfStreamException();
                    got += n;
                    Interlocked.Exchange(ref lastReceivedTicks, DateTime.UtcNow.Ticks);
                }
                Received?.Invoke(h, payload);
            }
        }
        catch (EndOfStreamException) { End(new EndOfStreamException("The Mac closed the connection.")); }
        catch (OperationCanceledException) { End(null); }
        catch (Exception e) { End(e); }
    }

    void End(Exception? why)
    {
        if (Interlocked.Exchange(ref ended, 1) != 0) return;
        stop.Cancel();
        outgoing.Writer.TryComplete();
        dialed.Dispose();
        Ended?.Invoke(why);
    }

    public void Close() => End(null);

    public void Dispose() => Close();

    public static byte[] PingPayload(double seconds)
    {
        var b = new byte[8];
        BinaryPrimitives.WriteInt64BigEndian(b, BitConverter.DoubleToInt64Bits(seconds));
        return b;
    }

    public static double PongSeconds(ReadOnlySpan<byte> payload) =>
        payload.Length >= 8 ? BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64BigEndian(payload)) : double.NaN;
}
