using System.Diagnostics;
using AndroidWidget.Core.Operations;

namespace AndroidWidget.Infrastructure.Scrcpy;

public sealed class ScreenMirroringService
{
    private readonly ScrcpyBundleManager _bundleManager;

    public ScreenMirroringService(ScrcpyBundleManager bundleManager) => _bundleManager = bundleManager;

    public OperationResult Start(string serial)
    {
        var bundled = _bundleManager.Prepare(out var error);
        if (string.IsNullOrWhiteSpace(bundled))
            return OperationResult.Failure($"Не удалось подготовить встроенный scrcpy: {error}");

        try
        {
            var info = new ProcessStartInfo(bundled)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(bundled)!
            };
            info.ArgumentList.Add("--serial");
            info.ArgumentList.Add(serial);
            info.ArgumentList.Add("--window-title");
            info.ArgumentList.Add("Android Widget · Screen");
            Process.Start(info);
            return OperationResult.Success();
        }
        catch (Exception ex)
        {
            return OperationResult.Failure(ex.Message);
        }
    }
}
