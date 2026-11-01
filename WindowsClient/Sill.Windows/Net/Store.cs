using System.Text.Json;
using Sill.Windows.Protocol;

namespace Sill.Windows.Net;

/// <summary>Where Sill keeps what it remembers: %LOCALAPPDATA%\Sill.</summary>
public static class AppData
{
    public static string Folder
    {
        get
        {
            // TEST: SILL_TEST_DATA keeps a test run's saved Macs and settings out of the user's.
            var d = Environment.GetEnvironmentVariable("SILL_TEST_DATA") is { Length: > 0 } test ? test
                  : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sill");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    public static T Read<T>(string file) where T : new()
    {
        try
        {
            var p = Path.Combine(Folder, file);
            if (!File.Exists(p)) return new T();
            // An editor that saved it with a byte order mark loses nothing for it.
            var bytes = File.ReadAllBytes(p).AsSpan();
            if (bytes.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) bytes = bytes[3..];
            return JsonSerializer.Deserialize<T>(bytes, Json.Options) ?? new T();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
        return new T();
    }

    public static void Write<T>(string file, T value)
    {
        try
        {
            var p = Path.Combine(Folder, file);
            var tmp = p + ".tmp";
            File.WriteAllBytes(tmp, JsonSerializer.SerializeToUtf8Bytes(value, new JsonSerializerOptions(Json.Options) { WriteIndented = true }));
            File.Move(tmp, p, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}

/// <summary>A Mac this PC paired with (SavedMacs on the device): its key's fingerprint is the pin;
/// the recognition key reads the tag in its Bonjour record, whatever its Bonjour name.</summary>
public sealed class SavedMac
{
    public string MacID { get; set; } = "";
    public string Fingerprint { get; set; } = "";       // base64url SHA-256 of the Mac's SPKI
    public string Name { get; set; } = "";
    public string? RecognitionKey { get; set; }         // base64url, 32 bytes
    public string? BonjourName { get; set; }
    public string? LastAddress { get; set; }
    public int LastPort { get; set; }
    public DateTimeOffset PairedAt { get; set; }
    public DateTimeOffset? LastConnectedAt { get; set; }

    public byte[]? Pin => Base64Url.Decode(Fingerprint);
    public byte[]? RecognitionKeyBytes => Base64Url.Decode(RecognitionKey);
}

public sealed class SavedMacs
{
    public List<SavedMac> Macs { get; set; } = new();

    const string File = "macs.json";
    static readonly object gate = new();

    public static SavedMacs Load() { lock (gate) return AppData.Read<SavedMacs>(File); }

    public static void Update(Action<SavedMacs> change)
    {
        lock (gate)
        {
            var s = AppData.Read<SavedMacs>(File);
            change(s);
            AppData.Write(File, s);
        }
    }

    /// <summary>The saved Mac a listed service is: by its recognition tag, else (no tag at all) by name.</summary>
    public SavedMac? Match(string? tag, string bonjourName)
    {
        if (!string.IsNullOrEmpty(tag)) return Macs.FirstOrDefault(m => RecognitionTag.Matches(tag, m.RecognitionKeyBytes));
        return Macs.FirstOrDefault(m => m.BonjourName == bonjourName);
    }
}

/// <summary>This PC's preferences.</summary>
public sealed class Settings
{
    /// <summary>Ctrl acts as ⌘ and the Windows key as ⌃ (on), or each as itself (off).</summary>
    public bool CtrlIsCommand { get; set; } = true;
    /// <summary>The Windows key goes to the Mac while the stream has the keyboard (off: it stays Windows').</summary>
    public bool WindowsKeyToMac { get; set; } = true;
    public bool ShareClipboard { get; set; } = true;
    /// <summary>Points a wheel notch scrolls on the Mac.</summary>
    public double ScrollPoints { get; set; } = 40;
    public bool ShowStats { get; set; }
    /// <summary>The address typed last into Connect by Address.</summary>
    public string? LastMac { get; set; }
    /// <summary>The saved Mac streamed last, which Sill connects to when it opens (AutoConnect).</summary>
    public string? LastMacID { get; set; }
    public bool AutoConnect { get; set; } = true;
    /// <summary>A mouse's back and forward buttons go to the Mac as ⌘[ and ⌘].</summary>
    public bool MouseBackForward { get; set; } = true;
    public bool AlwaysOnTop { get; set; }
    /// <summary>The stream window as it was closed: its normal bounds, maximized, full screen.</summary>
    public int WindowWidth { get; set; }
    public int WindowHeight { get; set; }
    public int? WindowX { get; set; }
    public int? WindowY { get; set; }
    public bool WindowMaximized { get; set; }
    public bool WindowFullScreen { get; set; }

    const string File = "settings.json";
    static Settings? current;
    public static Settings Current => current ??= AppData.Read<Settings>(File);
    public static void Save() { if (current != null) AppData.Write(File, current); }
}

/// <summary>Sill opening when the user signs in to Windows (Sill.app's Launch at Login): this user's Run
/// key, naming this Sill.exe wherever it is now.</summary>
public static class LoginItem
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run", Name = "Sill";

    public static bool Enabled
    {
        get
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(Name) is string;
        }
        set
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey);
            if (value && Environment.ProcessPath is { } exe) key.SetValue(Name, $"\"{exe}\"");
            else key.DeleteValue(Name, throwOnMissingValue: false);
        }
    }
}
