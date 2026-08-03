using AndroidWidget.Core.Devices;
using AndroidWidget.Core.Files;
using AndroidWidget.Core.Operations;

namespace AndroidWidget.Core.Abstractions;

public interface IAndroidDeviceService
{
    Task<IReadOnlyList<AndroidDevice>> GetDevicesAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> InstallApkAsync(string serial, string filePath, CancellationToken cancellationToken = default);
    Task<OperationResult> PushFileAsync(string serial, string filePath, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
    Task<OperationResult> PullFileAsync(string serial, string remotePath, string localPath,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RemoteEntry>> ListDirectoryAsync(string serial, string remotePath,
        CancellationToken cancellationToken = default);
    Task<OperationResult> TakeScreenshotAsync(string serial, string localPath,
        CancellationToken cancellationToken = default);
    Task<OperationResult> SendTextAsync(string serial, string text, CancellationToken cancellationToken = default);
    Task<OperationResult> TogglePowerAsync(string serial, CancellationToken cancellationToken = default);
    Task<OperationResult> PairWirelessAsync(string endpoint, string pairingCode,
        CancellationToken cancellationToken = default);
    Task<OperationResult> PairWirelessQrAsync(string serviceName, string password,
        CancellationToken cancellationToken = default);
    Task<OperationResult> ConnectWirelessAsync(string endpoint, CancellationToken cancellationToken = default);
    OperationResult StartScreenMirroring(string serial, ScrcpyPreset preset = ScrcpyPreset.Balanced);
    OperationResult StartScreenRecording(string serial, string localPath,
        ScrcpyPreset preset = ScrcpyPreset.Balanced);
    bool IsScreenRecording(string serial);
    OperationResult StopScreenRecording(string serial);
    OperationResult StartShell(string serial);
}
