using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Sill.Windows.Coherence;
using Sill.Windows.Net;

namespace Sill.Windows.Shell;

/// <summary>One Sill per user and data folder: a named mutex says whether one runs, and a named pipe
/// (this user only) carries a second Sill's arguments to it, one line (<see cref="LaunchRequest"/>):
/// a pinned Mac app's `--launch`, or "show" when Sill is opened again. A test run with its own
/// SILL_TEST_DATA has its own names, so it never hands off to the user's Sill, nor the other way.</summary>
public sealed class SingleInstance : IDisposable
{
    readonly Mutex mutex;
    readonly CancellationTokenSource stop = new();

    SingleInstance(Mutex mutex) { this.mutex = mutex; }

    /// <summary>This user's and data folder's name for both: the mutex is per session (Local\), a pipe's
    /// name is machine-wide, so it carries the user's SID in its hash.</summary>
    static string Name
    {
        get
        {
            var sid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sid + "|" + AppData.Folder.ToUpperInvariant()));
            return "Sill-" + Convert.ToHexString(hash, 0, 8);
        }
    }

    /// <summary>This process is the first: the instance (keep it while Sill runs). Null: another runs.</summary>
    public static SingleInstance? Claim()
    {
        var m = new Mutex(true, @"Local\" + Name, out bool first);
        if (first) return new SingleInstance(m);
        m.Dispose();
        return null;
    }

    [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int processId);

    /// <summary>Hands a line to the Sill that runs. It may bring its window forward: this process,
    /// started by the user, has the right to give it that.</summary>
    public static bool HandOff(string line)
    {
        try
        {
            AllowSetForegroundWindow(-1);   // ASFW_ANY
            using var pipe = new NamedPipeClientStream(".", Name, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            pipe.Connect(3000);
            var bytes = Encoding.UTF8.GetBytes(line + "\n");
            pipe.Write(bytes);
            pipe.Flush();
            return true;
        }
        catch (Exception e) when (e is IOException or TimeoutException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>Listens for lines from later Sills, one connection at a time, each handed to
    /// <paramref name="received"/> on the pipe's thread (the caller marshals it).</summary>
    public void Listen(Action<string> received)
    {
        var name = Name;
        _ = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(name, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                                                                 PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(stop.Token);
                    var buffer = new byte[LaunchRequest.MaxLine + 2];
                    int got = 0;
                    using var cut = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                    cut.CancelAfter(2000);
                    while (got < buffer.Length)
                    {
                        int n = await server.ReadAsync(buffer.AsMemory(got), cut.Token);
                        if (n == 0) break;
                        got += n;
                        if (Array.IndexOf(buffer, (byte)'\n', 0, got) >= 0) break;
                    }
                    var text = Encoding.UTF8.GetString(buffer, 0, got);
                    int end = text.IndexOf('\n');
                    if (end >= 0) received(text[..end].TrimEnd('\r'));
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
                catch (Exception e) when (e is IOException or OperationCanceledException or UnauthorizedAccessException)
                {
                    TestArguments.Log($"single instance: {e.Message}");
                    try { await Task.Delay(500, stop.Token); } catch (OperationCanceledException) { return; }
                }
            }
        });
    }

    public void Dispose()
    {
        stop.Cancel();
        try { mutex.ReleaseMutex(); } catch (ApplicationException) { }
        mutex.Dispose();
    }
}
