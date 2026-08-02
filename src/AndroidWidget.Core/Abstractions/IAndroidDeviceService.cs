using AndroidWidget.Core.Devices;
using AndroidWidget.Core.Files;
using AndroidWidget.Core.Operations;

namespace AndroidWidget.Core.Abstractions;

public interface IAndroidDeviceService
{
    Task<IReadOnlyList<AndroidDevice>> GetDevicesAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> InstallApkAsync(string serial, string filePath, CancellationToken cancellationToken = default);
    Task<OperationResult> PushFileAsync(string serial, string filePath, CancellationToken cancellationToken = default);
    Task<OperationResult> PullFileAsync(string serial, string remotePath, string localPath,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RemoteEntry>> ListDirectoryAsync(string serial, string remotePath,
        CancellationToken cancellationToken = default);
    Task<OperationResult> TakeScreenshotAsync(string serial, string localPath,
        CancellationToken cancellationToken = default);
    Task<OperationResult> SendTextAsync(string serial, string text, CancellationToken cancellationToken = default);
    Task<OperationResult> TogglePowerAsync(string serial, CancellationToken cancellationToken = default);
    OperationResult StartScreenMirroring(string serial);
    OperationResult StartShell(string serial);
}
