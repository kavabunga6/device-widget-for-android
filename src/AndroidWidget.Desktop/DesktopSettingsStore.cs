using System.Text.Json;

namespace AndroidWidget.Desktop;

internal sealed record DesktopSettings(
    string Theme,
    bool AutoStart,
    bool ShowNotifications,
    int NotificationDurationSeconds,
    string ScreenshotFolder,
    string RecordingFolder,
    string ScrcpyPreset,
    bool NotifyNewPhotos,
    bool AutoImportPhotos,
    string PhotoImportFolder,
    bool ShowScreenRecordingGuide,
    bool Topmost,
    Dictionary<string, DesktopDeviceWindowState>? DeviceWindows = null)
{
    public static DesktopSettings Default => new(
        "Dark",
        false,
        true,
        10,
        DefaultFolder(Environment.SpecialFolder.MyPictures, "Device Widget"),
        DefaultFolder(Environment.SpecialFolder.MyVideos, "Device Widget"),
        "Balanced",
        true,
        false,
        DefaultFolder(Environment.SpecialFolder.MyPictures, "Device Widget Imports"),
        true,
        true);

    private static string DefaultFolder(Environment.SpecialFolder folder, string child)
    {
        var root = Environment.GetFolderPath(folder);
        if (string.IsNullOrWhiteSpace(root))
            root = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(root, child);
    }
}

internal sealed record DesktopDeviceWindowState(
    int Left,
    int Top,
    double Width,
    double Height,
    bool IsMini,
    int? MiniLeft = null,
    int? MiniTop = null);

internal sealed class DesktopSettingsStore
{
    private readonly string _path;

    public DesktopSettingsStore()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AndroidWidget");
        _path = Path.Combine(directory, "desktop-settings.json");
        Current = Load(_path);
    }

    public DesktopSettings Current { get; private set; }
    public event EventHandler? Changed;

    public void Update(Func<DesktopSettings, DesktopSettings> update)
    {
        Current = update(Current);
        Persist();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public DesktopDeviceWindowState? GetDeviceWindowState(string serial)
    {
        if (Current.DeviceWindows is null)
            return null;
        return Current.DeviceWindows.GetValueOrDefault(serial);
    }

    public void SaveDeviceWindowState(string serial, DesktopDeviceWindowState state)
    {
        var states = Current.DeviceWindows is null
            ? new Dictionary<string, DesktopDeviceWindowState>(StringComparer.Ordinal)
            : new Dictionary<string, DesktopDeviceWindowState>(Current.DeviceWindows, StringComparer.Ordinal);
        if (states.TryGetValue(serial, out var existing) && existing == state)
            return;
        states[serial] = state;
        Current = Current with { DeviceWindows = states };
        Persist();
    }

    private void Persist()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(Current, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
        }
        catch
        {
            // Settings still apply for the current session if persistence is unavailable.
        }
    }

    public bool SetAutoStart(bool enabled, out string? error)
    {
        if (!DesktopAutoStart.TrySet(enabled, out error))
            return false;
        Update(current => current with { AutoStart = enabled });
        return true;
    }

    private static DesktopSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path))
                return DesktopSettings.Default;
            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<DesktopSettings>(json) ?? DesktopSettings.Default;
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty(nameof(DesktopSettings.ShowScreenRecordingGuide), out _)
                ? settings
                : settings with { ShowScreenRecordingGuide = true };
        }
        catch
        {
            return DesktopSettings.Default;
        }
    }
}
