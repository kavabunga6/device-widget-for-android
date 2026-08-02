using AndroidWidget.Infrastructure.Scrcpy;

namespace AndroidWidget.Infrastructure.Adb;

public sealed class AdbExecutableProvider
{
    private readonly ScrcpyBundleManager _bundleManager;
    private string? _cachedPath;

    public AdbExecutableProvider(ScrcpyBundleManager bundleManager) => _bundleManager = bundleManager;

    public string GetPath()
    {
        if (!string.IsNullOrWhiteSpace(_cachedPath) &&
            (!Path.IsPathRooted(_cachedPath) || File.Exists(_cachedPath)))
            return _cachedPath;

        var scrcpy = _bundleManager.Prepare(out _);
        if (!string.IsNullOrWhiteSpace(scrcpy))
        {
            var bundledAdb = Path.Combine(Path.GetDirectoryName(scrcpy)!, "adb.exe");
            if (File.Exists(bundledAdb))
                return _cachedPath = bundledAdb;
        }

        return _cachedPath = "adb";
    }
}
