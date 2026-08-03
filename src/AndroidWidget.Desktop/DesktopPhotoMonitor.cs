namespace AndroidWidget.Desktop;

internal sealed record DesktopPhotoEvent(string Serial, string Message, string? LocalPath);

internal sealed class DesktopPhotoMonitor(PortableAdbService adb)
{
    private readonly Dictionary<string, HashSet<string>> _known = new(StringComparer.Ordinal);
    private readonly HashSet<string> _running = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public event EventHandler<DesktopPhotoEvent>? PhotoDetected;

    public async Task CheckAsync(PortableAdbDevice device, DesktopSettings settings, CancellationToken token)
    {
        if (!device.Authorized || (!settings.NotifyNewPhotos && !settings.AutoImportPhotos))
            return;
        lock (_gate)
            if (!_running.Add(device.Serial))
                return;
        try
        {
            var files = await adb.ListDirectoryAsync(device.Serial, "/sdcard/DCIM/Camera", token);
            var current = files.Where(entry => !entry.IsDirectory && IsPhoto(entry.Name))
                .Select(entry => entry.Path).ToHashSet(StringComparer.Ordinal);
            if (!_known.TryGetValue(device.Serial, out var known))
            {
                _known[device.Serial] = current;
                return;
            }
            var added = current.Except(known, StringComparer.Ordinal).Take(10).ToList();
            known.UnionWith(current);
            foreach (var remotePath in added)
            {
                var name = Path.GetFileName(remotePath);
                string? localPath = null;
                var message = $"Новое фото: {name}";
                if (settings.AutoImportPhotos)
                {
                    var deviceFolder = string.Concat(device.Name.Select(character =>
                        Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
                    var folder = Path.Combine(settings.PhotoImportFolder, deviceFolder);
                    Directory.CreateDirectory(folder);
                    localPath = Path.Combine(folder, name);
                    var result = await adb.PullAsync(device.Serial, remotePath, localPath, token);
                    if (!result.IsSuccess)
                    {
                        localPath = null;
                        message = $"Не удалось импортировать {name}: {result.Message}";
                    }
                    else
                        message = $"Импортировано фото: {name}";
                }
                if (settings.NotifyNewPhotos || settings.AutoImportPhotos)
                    PhotoDetected?.Invoke(this, new DesktopPhotoEvent(device.Serial, message, localPath));
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch
        {
            // Some devices hide DCIM from adb; the next refresh retries silently.
        }
        finally
        {
            lock (_gate)
                _running.Remove(device.Serial);
        }
    }

    private static bool IsPhoto(string name) => Path.GetExtension(name).ToLowerInvariant() is
        ".jpg" or ".jpeg" or ".png" or ".heic" or ".webp" or ".dng";
}
