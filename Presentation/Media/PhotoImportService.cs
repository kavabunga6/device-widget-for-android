using AndroidWidget.Core.Abstractions;
using AndroidWidget.Core.Devices;

namespace AndroidWidget.Presentation.Media;

public sealed record PhotoImportEvent(string DeviceSerial, string FileName, bool Imported,
    string Message, string? LocalPath = null);

public sealed class PhotoImportService
{
    private static readonly string[] PhotoExtensions =
        [".jpg", ".jpeg", ".png", ".webp", ".heic", ".dng"];
    private readonly IAndroidDeviceService _devices;
    private readonly ISettingsService _settings;
    private readonly Dictionary<string, HashSet<string>> _known = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private DateTimeOffset _nextScan = DateTimeOffset.MinValue;

    public PhotoImportService(IAndroidDeviceService devices, ISettingsService settings)
    {
        _devices = devices;
        _settings = settings;
    }

    public event EventHandler<PhotoImportEvent>? PhotoDetected;

    public string Folder => RecordingStorage.ResolveFolder(_settings.Current.PhotoImportFolder,
        Environment.SpecialFolder.MyPictures, "Device Widget Imports");

    public void SetFolder(string folder)
    {
        var fullPath = Path.GetFullPath(folder);
        Directory.CreateDirectory(fullPath);
        _settings.Update(settings => settings with { PhotoImportFolder = fullPath });
    }

    public async Task ScanAsync(IReadOnlyList<AndroidDevice> devices,
        CancellationToken cancellationToken = default)
    {
        var settings = _settings.Current;
        if ((!settings.NotifyNewPhotos && !settings.AutoImportPhotos) || DateTimeOffset.Now < _nextScan ||
            !await _scanGate.WaitAsync(0, cancellationToken))
            return;

        try
        {
            _nextScan = DateTimeOffset.Now.AddSeconds(12);
            foreach (var device in devices.Where(device => device.State == DeviceConnectionState.Online))
                await ScanDeviceAsync(device, settings, cancellationToken);
        }
        finally
        {
            _scanGate.Release();
        }
    }

    private async Task ScanDeviceAsync(AndroidDevice device, Core.Settings.AppSettings settings,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Core.Files.RemoteEntry> entries;
        try
        {
            entries = await _devices.ListDirectoryAsync(device.Serial, "/sdcard/DCIM/Camera", cancellationToken);
        }
        catch
        {
            return;
        }

        var current = entries.Where(entry => !entry.IsDirectory && IsPhoto(entry.Name))
            .Select(entry => entry.FullPath).ToHashSet(StringComparer.Ordinal);
        if (!_known.TryGetValue(device.Serial, out var known))
        {
            _known[device.Serial] = current;
            return;
        }

        var added = current.Except(known).OrderBy(path => path, StringComparer.Ordinal).Take(10).ToList();
        _known[device.Serial] = current;
        foreach (var remotePath in added)
        {
            var name = Path.GetFileName(remotePath);
            if (!settings.AutoImportPhotos)
            {
                PhotoDetected?.Invoke(this, new PhotoImportEvent(device.Serial, name, false,
                    $"Новое фото: {name}"));
                continue;
            }

            var deviceFolder = Path.Combine(Folder, RecordingStorage.Sanitize(device.DisplayName));
            Directory.CreateDirectory(deviceFolder);
            var localPath = UniquePath(deviceFolder, name);
            var result = await _devices.PullFileAsync(device.Serial, remotePath, localPath,
                cancellationToken: cancellationToken);
            if (settings.NotifyNewPhotos)
            {
                PhotoDetected?.Invoke(this, result.IsSuccess
                    ? new PhotoImportEvent(device.Serial, name, true, $"Фото импортировано: {name}", localPath)
                    : new PhotoImportEvent(device.Serial, name, false,
                        $"Не удалось импортировать {name}: {result.BestMessage}"));
            }
        }
    }

    private static bool IsPhoto(string name) =>
        PhotoExtensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase);

    private static string UniquePath(string folder, string name)
    {
        var candidate = Path.Combine(folder, name);
        if (!File.Exists(candidate))
            return candidate;
        var stem = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);
        for (var index = 2; ; index++)
        {
            candidate = Path.Combine(folder, $"{stem} ({index}){extension}");
            if (!File.Exists(candidate))
                return candidate;
        }
    }
}
