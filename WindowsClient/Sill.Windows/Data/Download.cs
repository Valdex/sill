using System.Runtime.InteropServices;
using Sill.Windows.Protocol;

namespace Sill.Windows.Data;

/// <summary>One file of the Mac's (a drag's or a clipboard's offer) coming back in parts, asked for with
/// kind 37 (FileService.Fetch). Parts are written where they belong as they come, on the connection's
/// reader thread; readers (a virtual file's IStream, a CF_HDROP that waits for the whole file) read
/// what has come from another handle and wait for more. A folder arrives zipped and is unpacked once
/// whole. The staging folder is %LOCALAPPDATA%\Sill\Incoming\…, cleaned after a day; a file handed
/// out by path (CF_HDROP) is moved first to Downloads\From ‹Mac›, where it stays.</summary>
public sealed class Download
{
    readonly object gate = new();
    readonly ManualResetEvent changed = new(false);
    readonly TaskCompletionSource<string> finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    FileStream? writer;
    long available;
    ulong? total;
    string? delivered;

    public FileService Service { get; }
    public uint Transfer { get; }
    public uint Offer { get; }
    public int Index { get; }
    public OfferedFile File { get; }
    public string Folder { get; }
    /// <summary>Where the bytes land: the file's Windows name (a folder's with ".zip").</summary>
    public string PartPath { get; }
    /// <summary>The whole file (or the unpacked folder) once done.</summary>
    public Task<string> Finished => finished.Task;
    public bool Done => finished.Task.IsCompletedSuccessfully;
    public Exception? Failure => finished.Task.Exception?.InnerException;
    public bool Failed => finished.Task.IsFaulted || finished.Task.IsCanceled;
    /// <summary>Bytes readable from the start.</summary>
    public long Available => Interlocked.Read(ref available);
    /// <summary>The size: the first part's word for it (a folder's zip is only known then), else the offer's.</summary>
    public ulong Size { get { lock (gate) return total ?? File.Size; } }
    public bool SizeKnown { get { lock (gate) return total != null; } }

    internal Download(FileService service, uint transfer, uint offer, int index, OfferedFile file, string folder)
    {
        Service = service; Transfer = transfer; Offer = offer; Index = index; File = file; Folder = folder;
        PartPath = Path.Combine(folder, WindowsNames.FromMac(file.Name) + (file.Folder == true ? ".zip" : ""));
    }

    /// <summary>A part's new bytes, at its offset (the reader's thread).</summary>
    internal void Write(ulong offset, byte[] bytes, Reassembly file)
    {
        lock (gate)
        {
            if (finished.Task.IsCompleted) return;
            total ??= file.Total;
            // Every handle shares delete, so the finished file can be moved while a reader holds it.
            writer ??= new FileStream(PartPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            writer.Position = (long)offset;
            writer.Write(bytes);
            writer.Flush();
            Interlocked.Exchange(ref available, (long)file.Contiguous);
        }
        changed.Set();
    }

    /// <summary>The last part came: the file closed, a folder unpacked (off the reader's thread).</summary>
    internal void Complete()
    {
        lock (gate)
        {
            if (finished.Task.IsCompleted) return;
            writer ??= new FileStream(PartPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);   // an empty file
            writer.Dispose();
            writer = null;
        }
        if (File.Folder != true) { finished.TrySetResult(PartPath); changed.Set(); return; }
        _ = Task.Run(() =>
        {
            try { finished.TrySetResult(FolderZip.Unpack(PartPath, Folder, File.Name)); }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or FolderZip.UnpackException) { finished.TrySetException(e); }
            changed.Set();
        });
    }

    internal void Fail(string why)
    {
        lock (gate)
        {
            writer?.Dispose();
            writer = null;
        }
        finished.TrySetException(new IOException(why));
        changed.Set();
    }

    /// <summary>Gives the file up: the Mac's later parts are dropped.</summary>
    public void Cancel()
    {
        Service.CancelDownload(this);
        Fail("Cancelled.");
    }

    /// <summary>Blocks until <paramref name="bytes"/> are readable from the start, the file is whole, or
    /// it failed; false when nothing came for <paramref name="quiet"/>. The wait does not pump messages:
    /// it is called from a COM call (IStream::Read, IDataObject::GetData) on an STA thread, where a
    /// pumping wait would let a second call in on top of the first.</summary>
    public bool WaitFor(long bytes, TimeSpan quiet)
    {
        long seen = Available;
        var since = Environment.TickCount64;
        while (true)
        {
            changed.Reset();
            long now = Available;
            if (now >= bytes || finished.Task.IsCompleted) return true;
            if (now != seen) { seen = now; since = Environment.TickCount64; }
            if (Environment.TickCount64 - since > quiet.TotalMilliseconds) return false;
            Waits.NonPumping(changed, 200);
        }
    }

    /// <summary>Blocks until the whole file (or folder) is here; its path, or null.</summary>
    public string? WaitDone(TimeSpan quiet)
    {
        while (!finished.Task.IsCompleted)
        {
            if (!WaitFor(long.MaxValue, quiet)) return null;
            if (!finished.Task.IsCompleted) Waits.NonPumping(changed, 100);
        }
        return Done ? finished.Task.Result : null;
    }

    /// <summary>Reads what has come at <paramref name="position"/> (never past <see cref="Available"/>).</summary>
    public int ReadAt(long position, Span<byte> buffer)
    {
        long avail = Available;
        if (position >= avail) return 0;
        int n = (int)Math.Min(buffer.Length, avail - position);
        using var r = new FileStream(delivered != null && File.Folder != true ? delivered : PartPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        r.Position = position;
        int got = 0;
        while (got < n)
        {
            int k = r.Read(buffer.Slice(got, n - got));
            if (k <= 0) break;
            got += k;
        }
        return got;
    }

    /// <summary>The finished file (or folder) moved to <paramref name="directory"/> under a name not
    /// taken there (CF_HDROP: an app that keeps the path keeps a file that stays); the same path each
    /// time it is asked again.</summary>
    public string? Deliver(string directory)
    {
        if (!Done) return null;
        lock (gate)
        {
            if (delivered != null) return delivered;
            var from = finished.Task.Result;
            Directory.CreateDirectory(directory);
            var to = Staging.Unique(Path.Combine(directory, Path.GetFileName(from)));
            try
            {
                if (Directory.Exists(from)) Directory.Move(from, to);
                else System.IO.File.Move(from, to);
                delivered = to;
            }
            catch (IOException)
            {
                // Another volume, or a reader without delete sharing: handed out from staging instead.
                delivered = from;
            }
            return delivered;
        }
    }
}

/// <summary>Waits that never pump an STA thread's messages.</summary>
public static class Waits
{
    [DllImport("kernel32.dll", SetLastError = true)] static extern uint WaitForSingleObject(IntPtr handle, uint ms);

    public static void NonPumping(WaitHandle h, int ms)
    {
        bool added = false;
        var safe = h.SafeWaitHandle;
        try
        {
            safe.DangerousAddRef(ref added);
            WaitForSingleObject(safe.DangerousGetHandle(), (uint)ms);
        }
        finally { if (added) safe.DangerousRelease(); }
    }
}
