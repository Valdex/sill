namespace Sill.Windows;

/// <summary>TEST: launch arguments for checking the client without clicking it, as the iOS app's
/// DEBUG harness has (-SillConnect, -SillPairCode, -SillInputScript). Each does nothing unless given.
/// <list type="bullet">
/// <item>--connect host:port: connect by address as soon as the window shows.</item>
/// <item>--pair-code DIGITS: type these into the pairing dialog when it opens.</item>
/// <item>--input-script: once the first picture is on screen, send a fixed sequence of pointer moves,
///   clicks, a scroll and keys, only to a Mac on this PC's loopback (SillTestHost, never a real Mac).</item>
/// <item>--actions "S:NAME,…": at S seconds after the first picture, a View menu action or shortcut
///   (a hotkey's letter, fit, actual, bar, nobar, back, forward, bounds, mute), only with a loopback test host.</item>
/// <item>--type-text TEXT: what the clipboard's text is for Type the Clipboard’s Text (the user's is never read).</item>
/// <item>--quit-after SECONDS: close the app then.</item>
/// <item>--log FILE: write what the session does there.</item>
/// </list>
/// SILL_TEST_DATA (AppData) keeps a run's saved Macs, settings and screenshots in that folder, and its
/// screenshots off the clipboard.</summary>
public static class TestArguments
{
    static string[] args = Array.Empty<string>();

    public static void Read(string[] a) => args = a;

    static string? Value(string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    public static string? Connect => Value("--connect");
    public static string? PairCode => Value("--pair-code");
    public static bool InputScript => args.Contains("--input-script");
    public static double? QuitAfter => double.TryParse(Value("--quit-after"), System.Globalization.CultureInfo.InvariantCulture, out var s) ? s : null;
    public static string? LogFile => Value("--log");
    public static string? TypeText => Value("--type-text");
    public static bool IsolatedData => Environment.GetEnvironmentVariable("SILL_TEST_DATA") is { Length: > 0 };

    public static List<(double at, string name)>? Actions => Value("--actions") is { } list
        ? list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
              .Select(a => a.Split(':', 2))
              .Where(p => p.Length == 2 && double.TryParse(p[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _))
              .Select(p => (double.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture), p[1]))
              .OrderBy(a => a.Item1).ToList()
        : null;
    /// <summary>--offscreen: the windows open off every display and never take the focus, so a test run
    /// never lands under the user's mouse.</summary>
    public static bool Offscreen => args.Contains("--offscreen");

    public static void Place(Form form)
    {
        if (!Offscreen) return;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-6000, 80);
        form.ShowInTaskbar = false;
    }

    static readonly object gate = new();

    public static void Log(string line)
    {
        if (LogFile is not { } f) return;
        lock (gate) File.AppendAllText(f, $"{DateTime.Now:HH:mm:ss.fff} {line}{Environment.NewLine}");
    }
}
