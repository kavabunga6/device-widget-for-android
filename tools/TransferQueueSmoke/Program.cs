using AndroidWidget.Core.Abstractions;
using AndroidWidget.Core.Devices;
using AndroidWidget.Core.Files;
using AndroidWidget.Core.Operations;
using AndroidWidget.Presentation.Transfers;

var fake = new FakeDeviceService();
using var queue = new TransferQueueService(fake);
var first = queue.EnqueueUpload("device-a", Path.Combine(Path.GetTempPath(), "first.bin"));
var second = queue.EnqueueUpload("device-b", Path.Combine(Path.GetTempPath(), "second.bin"));
Ensure((await queue.WaitAsync(first)).IsSuccess, "first transfer failed");
Ensure((await queue.WaitAsync(second)).IsSuccess, "second transfer failed");
Ensure(fake.MaxConcurrent == 1, "queue executed transfers concurrently");
Ensure(queue.Snapshot.Where(job => job.Id == first || job.Id == second)
    .All(job => job.State == TransferJobState.Completed && job.Progress == 1), "completed state/progress missing");

var blocker = queue.EnqueueUpload("device-a", Path.Combine(Path.GetTempPath(), "blocker.bin"));
var cancelled = queue.EnqueueUpload("device-a", Path.Combine(Path.GetTempPath(), "cancelled.bin"));
queue.Cancel(cancelled);
Ensure((await queue.WaitAsync(blocker)).IsSuccess, "blocking transfer failed");
Ensure(!(await queue.WaitAsync(cancelled)).IsSuccess, "cancelled transfer unexpectedly succeeded");
Ensure(queue.Snapshot.Single(job => job.Id == cancelled).State == TransferJobState.Cancelled,
    "cancelled state missing");

Console.WriteLine("Transfer queue smoke: PASS");

static void Ensure(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

internal sealed class FakeDeviceService : IAndroidDeviceService
{
    private int _concurrent;
    public int MaxConcurrent { get; private set; }

    public Task<IReadOnlyList<AndroidDevice>> GetDevicesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AndroidDevice>>([]);

    public Task<OperationResult> InstallApkAsync(string serial, string filePath,
        CancellationToken cancellationToken = default) => ExecuteAsync(null, cancellationToken);

    public Task<OperationResult> PushFileAsync(string serial, string filePath, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default) => ExecuteAsync(progress, cancellationToken);

    public Task<OperationResult> PullFileAsync(string serial, string remotePath, string localPath,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default) =>
        ExecuteAsync(progress, cancellationToken);

    private async Task<OperationResult> ExecuteAsync(IProgress<double>? progress, CancellationToken token)
    {
        var current = Interlocked.Increment(ref _concurrent);
        MaxConcurrent = Math.Max(MaxConcurrent, current);
        try
        {
            progress?.Report(0.5);
            await Task.Delay(80, token);
            progress?.Report(1);
            return OperationResult.Success();
        }
        finally
        {
            Interlocked.Decrement(ref _concurrent);
        }
    }

    public Task<IReadOnlyList<RemoteEntry>> ListDirectoryAsync(string serial, string remotePath,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<OperationResult> TakeScreenshotAsync(string serial, string localPath,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<OperationResult> SendTextAsync(string serial, string text,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<OperationResult> TogglePowerAsync(string serial,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<OperationResult> PairWirelessAsync(string endpoint, string pairingCode,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<OperationResult> PairWirelessQrAsync(string serviceName, string password,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<OperationResult> ConnectWirelessAsync(string endpoint,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public OperationResult StartScreenMirroring(string serial, ScrcpyPreset preset = ScrcpyPreset.Balanced) =>
        throw new NotSupportedException();
    public OperationResult StartScreenRecording(string serial, string localPath,
        ScrcpyPreset preset = ScrcpyPreset.Balanced) => throw new NotSupportedException();
    public bool IsScreenRecording(string serial) => false;
    public string? GetScreenRecordingPath(string serial) => null;
    public OperationResult StopScreenRecording(string serial) => throw new NotSupportedException();
    public OperationResult StartShell(string serial) => throw new NotSupportedException();
}
