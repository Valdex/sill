using System.Diagnostics;
using System.Threading.Channels;
using Sill.Windows.Net;
using Sill.Windows.Protocol;

namespace Sill.Windows.Data;

/// <summary>One session's files (kinds 34–37 and the bulk connection of kind 48): this PC's uploads
/// as kind 34 parts, paced so the picture comes first, on the bulk connection when it is up, else on
/// the session; the Mac's files asked for with kind 37, their parts routed by transfer ID from either
/// connection; the Mac's drag offers (kind 36) handed on. One per session, whichever windows show it
/// (the classic stream window, the Coherence windows): <see cref="Attach"/> makes it or finds it, and
/// it ends with the session. Thread-safe; its events fire on the connections' reader threads.</summary>
public sealed class FileService : IDisposable
{
    static readonly object registry = new();
    static readonly List<FileService> live = new();

    /// <summary>The service of every session alive (Open on Mac picks one).</summary>
    public static IReadOnlyList<FileService> Live { get { lock (registry) return live.ToList(); } }

    /// <summary>The session's service, made at the first call.</summary>
    public static FileService Attach(Session session)
    {
        lock (registry)
        {
            var found = live.FirstOrDefault(f => f.Session == session);
            if (found != null) return found;
            var made = new FileService(session);
            live.Add(made);
            return made;
        }
    }

    public static FileService? For(Session? session)
    {
        if (session == null) return null;
        lock (registry) return live.FirstOrDefault(f => f.Session == session);
    }

    public Session Session { get; }
    readonly TransferIds ids = TransferIds.Random();
    readonly IncomingParts incoming = new();
    readonly Dictionary<uint, Download> downloads = new();
    readonly Dictionary<(uint offer, int index), Download> byOffer = new();
    readonly object pacerGate = new();
    readonly UploadPacer sessionPacer = new(FileParts.SessionPartBytes);
    readonly UploadPacer dataPacer = new(FileParts.DataPartBytes, partsInFlight: 4);
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly Channel<UploadBatch> uploads = Channel.CreateUnbounded<UploadBatch>(new UnboundedChannelOptions { SingleReader = true });
    readonly CancellationTokenSource stop = new();
    volatile DataLink? link;
    string? ticket;
    int linkFailures, disposed;

    /// <summary>Kind 36, on the reader's thread: an app on the Mac is dragging files under this PC's
    /// held button (an offer of id 0: the drag ended there).</summary>
    public event Action<DragOffer>? DragOffered;
    /// <summary>A part of an upload went: its batch (a live drag stays alive on the host by them).</summary>
    public event Action<UploadBatch>? Progress;

    /// <summary>The bulk connection is up.</summary>
    public bool DataUp => link?.IsOpen == true;
    /// <summary>The Mac takes a drop of files (kinds 34 and 35).</summary>
    public bool HostTakesFiles => (Session.Windows?.FileDrop ?? 0) >= Capabilities.FileDrop;
    /// <summary>The Mac takes kind 34 parts at all: for a drop, or for the clipboard's files (generation 3).</summary>
    public bool HostTakesParts => HostTakesFiles || (Session.Windows?.Clipboard ?? 0) >= Capabilities.ClipboardFiles;
    public bool HostLiveDrag => (Session.Windows?.LiveDrag ?? 0) >= Capabilities.LiveDrag;
    public string MacName => Session.MacName;

    FileService(Session session)
    {
        Session = session;
        Staging.CleanOnce();
        session.OtherMessage += OnMessage;
        session.RoundTrip += OnRoundTrip;
        session.Ended += _ => Dispose();
        _ = Task.Run(Pump);
    }

    static void Log(string line) => TestArguments.Log($"files: {line}");

    // MARK: the session's messages

    void OnMessage(Header h, byte[] payload)
    {
        switch ((Kind)h.Kind)
        {
            case Kind.FileChunk:
                OnPart(payload);
                break;
            case Kind.DragOffer:
                if (Json.Decode<DragOffer>(payload) is { } offer) DragOffered?.Invoke(offer);
                break;
            case Kind.DataTicket:
                var t = Json.Decode<DataTicket>(payload);
                if (!DataLinkPolicy.Opens(Session.Windows?.Data, t)) { Log("a data ticket from a host whose list does not say data 1, or not a ticket: ignored"); break; }
                if (ticket != null) break;   // one per session
                ticket = t!.Token;
                _ = OpenLink();
                break;
        }
    }

    void OnRoundTrip(double ms)
    {
        double now = clock.Elapsed.TotalSeconds;
        lock (pacerGate)
        {
            sessionPacer.OnRoundTrip(ms, now);
            dataPacer.OnRoundTrip(ms, now);
        }
    }

    // MARK: the bulk connection

    async Task OpenLink()
    {
        while (!stop.IsCancellationRequested && Session.IsOpen && ticket is { } token)
        {
            try
            {
                var l = await DataLink.Open(Session.Endpoint, Session.MacFingerprint, token, stop.Token);
                l.Received += (h, p) => { if (h.Kind == (byte)Kind.FileChunk) OnPart(p); };
                l.Ended += why => OnLinkEnded(l, why);
                link = l;
                Log($"the bulk connection is up ({(Session.MacFingerprint != null ? "TLS, " + DataHello.Alpn : "plain")})");
                if (Volatile.Read(ref disposed) != 0) l.Close();
                return;
            }
            catch (Exception e) when (e is DialException or IOException or System.Net.Sockets.SocketException or OperationCanceledException)
            {
                if (stop.IsCancellationRequested) return;
                if (DataLinkPolicy.NextTry(++linkFailures) is not { } wait)
                {
                    Log($"the bulk connection did not come up ({e.Message}); files go on the session");
                    return;
                }
                Log($"the bulk connection did not come up ({e.Message}); again in {wait:0} s");
                try { await Task.Delay(TimeSpan.FromSeconds(wait), stop.Token); } catch (OperationCanceledException) { return; }
            }
        }
    }

    void OnLinkEnded(DataLink l, Exception? why)
    {
        if (link == l) link = null;
        if (Volatile.Read(ref disposed) != 0 || !Session.IsOpen) return;
        Log($"the bulk connection ended ({why?.Message ?? "closed"})");
        if (DataLinkPolicy.NextTry(++linkFailures) is { } wait)
            _ = Task.Delay(TimeSpan.FromSeconds(wait), stop.Token).ContinueWith(_ => OpenLink(), TaskContinuationOptions.OnlyOnRanToCompletion);
    }

    // MARK: the Mac's files

    void OnPart(byte[] payload)
    {
        if (FileChunk.Unpack(payload) is not { } part) return;
        var routed = incoming.Route(part.Id, part.Name, part.Total, part.Offset, part.Bytes.Length);
        Download? d;
        lock (downloads) downloads.TryGetValue(part.Id, out d);
        switch (routed.Outcome)
        {
            case IncomingParts.Outcome.Write:
            case IncomingParts.Outcome.WriteAndComplete:
                if (d == null) break;
                try
                {
                    d.Write(part.Offset, part.Bytes, routed.File!);
                    if (routed.Outcome == IncomingParts.Outcome.WriteAndComplete)
                    {
                        incoming.Forget(part.Id);
                        ids.Release(part.Id);
                        d.Complete();
                        Log($"{part.Total:N0} bytes of transfer {part.Id} arrived");
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    incoming.Cancel(part.Id);
                    d.Fail($"Could not write {d.PartPath}: {e.Message}");
                }
                break;
            case IncomingParts.Outcome.Refused:
                incoming.Cancel(part.Id);
                d?.Fail($"The Mac's transfer {part.Id} broke: {routed.Why}.");
                Log($"transfer {part.Id} refused: {routed.Why}");
                break;
        }
    }

    /// <summary>One file of an offer (a drag's, kind 36, or the clipboard's, kind 32), asked for with
    /// kind 37 under a transfer ID of this PC's. Asked once: a second ask for the same file (another
    /// paste, a second read of a virtual file) shares the first unless it failed.</summary>
    public Download Fetch(uint offer, int index, OfferedFile file)
    {
        lock (downloads)
        {
            if (byOffer.TryGetValue((offer, index), out var known) && !known.Failed) return known;
            uint t = ids.Take();
            var d = new Download(this, t, offer, index, file, Staging.NewFolder(t));
            downloads[t] = d;
            byOffer[(offer, index)] = d;
            incoming.Expect(t);
            if (!Session.Send(Kind.FileRequest, Json.Encode(new FileRequest { Offer = offer, Index = index, Transfer = t })))
                d.Fail($"{MacName} is not connected.");
            else Log($"asked for {file.Name} ({file.Size:N0} bytes) of offer {offer} as transfer {t}");
            return d;
        }
    }

    internal void CancelDownload(Download d)
    {
        incoming.Cancel(d.Transfer);
        ids.Release(d.Transfer);
    }

    // MARK: this PC's files

    /// <summary>Files and folders to send as kind 34 parts, one transfer each (a folder zipped). Null,
    /// with why, when there is nothing to send, the Mac takes no files, or the whole is over the 512 MB
    /// a drop may be; else the batch, queued behind any other.</summary>
    public UploadBatch? Upload(IReadOnlyList<string> paths, out string? refusal, IReadOnlyCollection<string>? deleteAfter = null)
    {
        refusal = null;
        if (!HostTakesParts) { refusal = $"{MacName} doesn’t take files (update Sill on the Mac)."; return null; }
        var files = new List<Outgoing>();
        long total = 0;
        foreach (var p in paths)
        {
            try
            {
                if (Directory.Exists(p))
                {
                    var (bytes, _) = FolderZip.Measure(p);
                    files.Add(new Outgoing(p, Path.GetFileName(Path.TrimEndingDirectorySeparator(p)), true, bytes, deleteAfter?.Contains(p) == true));
                    total += bytes;
                }
                else if (File.Exists(p))
                {
                    long len = new FileInfo(p).Length;
                    files.Add(new Outgoing(p, Path.GetFileName(p), false, len, deleteAfter?.Contains(p) == true));
                    total += len;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { refusal = $"Couldn’t read {Path.GetFileName(p)}: {e.Message}"; return null; }
        }
        if (files.Count == 0) { refusal = "Nothing to send."; return null; }
        if ((ulong)total > FileParts.MaxDrop) { refusal = $"Too large: {MacName} takes up to {FileParts.MaxDrop >> 20} MB at a time ({total / (1 << 20):N0} MB)."; return null; }
        var batch = new UploadBatch(files, total);
        uploads.Writer.TryWrite(batch);
        return batch;
    }

    async Task Pump()
    {
        try
        {
            await foreach (var batch in uploads.Reader.ReadAllAsync(stop.Token))
            {
                try
                {
                    await SendBatch(batch);
                    batch.Succeed();
                }
                catch (OperationCanceledException) { batch.Fail(new OperationCanceledException("Cancelled.")); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    Log($"upload failed: {e.Message}");
                    batch.Fail(e);
                }
                finally
                {
                    foreach (var f in batch.Files.Where(f => f.DeleteAfter)) Staging.RemoveTemp(f.Path);
                }
            }
        }
        catch (OperationCanceledException) { }
        // Whatever is still queued when the session ends fails.
        while (uploads.Reader.TryRead(out var left)) left.Fail(new IOException($"{MacName} is not connected."));
    }

    async Task SendBatch(UploadBatch batch)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(stop.Token, batch.Cancellation);
        var ct = cts.Token;
        ulong sentTotal = 0;
        foreach (var file in batch.Files)
        {
            ct.ThrowIfCancellationRequested();
            string source = file.Path, name = file.Name;
            string? zip = null;
            try
            {
                if (file.Folder)
                {
                    // ditto's --keepParent shape, named as the Mac's unpack expects: ‹folder›.zip holding ‹folder›.
                    zip = Staging.Temp(file.Name + ".zip");
                    await Task.Run(() => FolderZip.Zip(file.Path, zip, ct), ct);
                    source = zip;
                    name = WindowsNames.ToMac(file.Name) + ".zip";
                }
                else name = WindowsNames.ToMac(file.Name);
                ulong size = (ulong)new FileInfo(source).Length;
                if (sentTotal + size > FileParts.MaxDrop) throw new IOException($"Over the {FileParts.MaxDrop >> 20} MB {MacName} takes at a time.");
                while (true)
                {
                    var (onData, partBytes) = DataLinkPolicy.Route(DataUp);
                    var via = onData ? link : null;
                    uint id = ids.Take();
                    bool ok = await SendFile(batch, source, name, size, id, via, partBytes, ct);
                    ids.Release(id);
                    if (ok) { file.Id = id; break; }
                    ct.ThrowIfCancellationRequested();
                    if (!DataLinkPolicy.Restart(onData, DataUp, Session.IsOpen)) throw new IOException($"{MacName} is not connected.");
                    Log($"{name}: the bulk connection went mid-way; again from the start on the session");
                }
                sentTotal += size;
            }
            finally
            {
                if (zip != null) Staging.RemoveTemp(zip);
            }
        }
    }

    /// <summary>One file in parts on one connection: false when that connection went (the caller may
    /// begin it again on the other), true when every part was queued.</summary>
    async Task<bool> SendFile(UploadBatch batch, string path, string name, ulong size, uint id, DataLink? via, int partBytes, CancellationToken ct)
    {
        var pacer = via != null ? dataPacer : sessionPacer;
        var buffer = new byte[partBytes];
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, useAsync: true);
        foreach (var (offset, length) in FileParts.Split(size, partBytes))
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                long queued = via?.QueuedBytes ?? Session.QueuedBytes;
                double wait;
                lock (pacerGate) wait = pacer.Wait(length, queued, clock.Elapsed.TotalSeconds);
                if (wait <= 0) break;
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(wait, 0.001)), ct);
            }
            int got = 0;
            while (got < length)
            {
                int n = await fs.ReadAsync(buffer.AsMemory(got, length - got), ct);
                if (n == 0) throw new IOException($"{Path.GetFileName(path)} got shorter while it was sent.");
                got += n;
            }
            var payload = FileChunk.Pack(id, name, size, offset, buffer.AsSpan(0, length));
            bool sent = via != null ? via.Send(Kind.FileChunk, payload) : Session.Send(Kind.FileChunk, payload);
            if (!sent) return false;
            lock (pacerGate) pacer.Sent(length, clock.Elapsed.TotalSeconds);
            batch.Add(length);
            Progress?.Invoke(batch);
        }
        Log($"sent {name} ({size:N0} bytes) as transfer {id} on the {(via != null ? "bulk connection" : "session")}");
        return true;
    }

    /// <summary>Sends a JSON message on the session.</summary>
    public bool Send<T>(Kind kind, T value) => Session.Send(kind, Json.Encode(value));

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        lock (registry) live.Remove(this);
        stop.Cancel();
        uploads.Writer.TryComplete();
        link?.Close();
        link = null;
        List<Download> pending;
        lock (downloads) pending = downloads.Values.Where(d => !d.Done).ToList();
        foreach (var d in pending) d.Fail($"{MacName} disconnected.");
        Session.OtherMessage -= OnMessage;
        Session.RoundTrip -= OnRoundTrip;
    }
}

/// <summary>A file or folder to send.</summary>
public sealed class Outgoing
{
    public string Path { get; }
    /// <summary>Its name on this PC (a folder's without ".zip").</summary>
    public string Name { get; }
    public bool Folder { get; }
    /// <summary>Its bytes (a folder's files' bytes before zipping).</summary>
    public long Size { get; }
    /// <summary>A temporary copy (a virtual file), removed once sent.</summary>
    public bool DeleteAfter { get; }
    /// <summary>The transfer ID it went under, once sent (a transfer begun again has a new one).</summary>
    public uint Id { get; internal set; }

    public Outgoing(string path, string name, bool folder, long size, bool deleteAfter = false)
    {
        Path = path; Name = name; Folder = folder; Size = size; DeleteAfter = deleteAfter;
    }
}

/// <summary>Files sent together (a drop, a clipboard copy): their transfer IDs once all are sent.</summary>
public sealed class UploadBatch
{
    readonly CancellationTokenSource cancel = new();
    readonly TaskCompletionSource done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    long sent;

    public IReadOnlyList<Outgoing> Files { get; }
    public long TotalBytes { get; }
    public long SentBytes => Interlocked.Read(ref sent);
    public Task Completion => done.Task;
    public CancellationToken Cancellation => cancel.Token;
    /// <summary>The transfer IDs, in order, once <see cref="Completion"/> succeeded.</summary>
    public List<uint> Ids => Files.Select(f => f.Id).ToList();
    /// <summary>The IDs of those that are folders sent zipped (kind 35's and 38's `folders`).</summary>
    public List<uint> FolderIds => Files.Where(f => f.Folder).Select(f => f.Id).ToList();

    public UploadBatch(IReadOnlyList<Outgoing> files, long total) { Files = files; TotalBytes = total; }

    public void Cancel() { try { cancel.Cancel(); } catch (ObjectDisposedException) { } done.TrySetCanceled(); }
    internal void Add(int bytes) => Interlocked.Add(ref sent, bytes);
    internal void Succeed() => done.TrySetResult();
    internal void Fail(Exception e) { if (e is OperationCanceledException) done.TrySetCanceled(); else done.TrySetException(e); }
}
