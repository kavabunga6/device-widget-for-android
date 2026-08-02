using System.Collections.Concurrent;
using AndroidWidget.Core.Abstractions;
using AndroidWidget.Core.Operations;

namespace AndroidWidget.Presentation.Transfers;

public enum TransferJobState
{
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled
}

public enum TransferJobKind
{
    Upload,
    Download,
    InstallApk
}

public sealed record TransferJobSnapshot(
    Guid Id,
    string DeviceSerial,
    string Name,
    TransferJobKind Kind,
    TransferJobState State,
    double? Progress,
    string Message,
    DateTimeOffset CreatedAt)
{
    public bool CanCancel => State is TransferJobState.Queued or TransferJobState.Running;
}

public sealed class TransferQueueService : IDisposable
{
    private readonly IAndroidDeviceService _devices;
    private readonly ConcurrentQueue<TransferJob> _pending = new();
    private readonly Dictionary<Guid, TransferJob> _jobs = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _sync = new();
    private readonly Task _worker;

    public TransferQueueService(IAndroidDeviceService devices)
    {
        _devices = devices;
        _worker = Task.Run(ProcessQueueAsync);
    }

    public event EventHandler? Changed;

    public IReadOnlyList<TransferJobSnapshot> Snapshot
    {
        get
        {
            lock (_sync)
                return _jobs.Values.OrderByDescending(job => job.CreatedAt).Select(job => job.Snapshot()).ToList();
        }
    }

    public Guid EnqueueUpload(string serial, string path)
    {
        var fullPath = Path.GetFullPath(path);
        var isApk = File.Exists(fullPath) &&
                    Path.GetExtension(fullPath).Equals(".apk", StringComparison.OrdinalIgnoreCase);
        var kind = isApk ? TransferJobKind.InstallApk : TransferJobKind.Upload;
        return Enqueue(new TransferJob(serial, Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar)), kind,
            fullPath, null));
    }

    public Guid EnqueueDownload(string serial, string remotePath, string localPath) =>
        Enqueue(new TransferJob(serial, Path.GetFileName(remotePath.TrimEnd('/')), TransferJobKind.Download,
            remotePath, Path.GetFullPath(localPath)));

    public Task<OperationResult> WaitAsync(Guid id)
    {
        lock (_sync)
            return _jobs.TryGetValue(id, out var job)
                ? job.Completion.Task
                : Task.FromResult(OperationResult.Failure("Передача не найдена."));
    }

    public void Cancel(Guid id)
    {
        lock (_sync)
        {
            if (!_jobs.TryGetValue(id, out var job) || job.State is not (TransferJobState.Queued or TransferJobState.Running))
                return;

            job.Cancellation.Cancel();
            if (job.State == TransferJobState.Queued)
            {
                job.State = TransferJobState.Cancelled;
                job.Message = "Отменено";
                job.Completion.TrySetResult(OperationResult.Failure("Операция отменена.", -3));
            }
        }
        RaiseChanged();
    }

    private Guid Enqueue(TransferJob job)
    {
        lock (_sync)
            _jobs.Add(job.Id, job);
        _pending.Enqueue(job);
        _signal.Release();
        RaiseChanged();
        return job.Id;
    }

    private async Task ProcessQueueAsync()
    {
        while (!_lifetime.IsCancellationRequested)
        {
            try
            {
                await _signal.WaitAsync(_lifetime.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            if (!_pending.TryDequeue(out var job))
                continue;
            if (job.Cancellation.IsCancellationRequested)
            {
                Finish(job, TransferJobState.Cancelled, OperationResult.Failure("Операция отменена.", -3));
                continue;
            }

            Update(job, TransferJobState.Running, job.Kind == TransferJobKind.InstallApk ? null : 0,
                job.Kind switch
                {
                    TransferJobKind.Download => "Скачивание на компьютер…",
                    TransferJobKind.InstallApk => "Установка APK…",
                    _ => "Передача на телефон…"
                });
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token,
                job.Cancellation.Token);
            try
            {
                var progress = job.Kind == TransferJobKind.InstallApk
                    ? null
                    : new InlineProgress<double>(value => Update(job, TransferJobState.Running, value, job.Message));
                var result = job.Kind switch
                {
                    TransferJobKind.Download => await _devices.PullFileAsync(job.DeviceSerial, job.Source,
                        job.Destination!, progress, linked.Token),
                    TransferJobKind.InstallApk => await _devices.InstallApkAsync(job.DeviceSerial, job.Source,
                        linked.Token),
                    _ => await _devices.PushFileAsync(job.DeviceSerial, job.Source, progress, linked.Token)
                };
                Finish(job, result.IsSuccess ? TransferJobState.Completed : TransferJobState.Failed, result);
            }
            catch (OperationCanceledException)
            {
                Finish(job, TransferJobState.Cancelled, OperationResult.Failure("Операция отменена.", -3));
            }
            catch (Exception ex)
            {
                Finish(job, TransferJobState.Failed, OperationResult.Failure(ex.Message));
            }
        }
    }

    private void Finish(TransferJob job, TransferJobState state, OperationResult result)
    {
        Update(job, state, state == TransferJobState.Completed ? 1 : job.Progress,
            state switch
            {
                TransferJobState.Completed => "Готово",
                TransferJobState.Cancelled => "Отменено",
                _ => result.BestMessage
            });
        job.Completion.TrySetResult(result);
        TrimHistory();
    }

    private void Update(TransferJob job, TransferJobState state, double? progress, string message)
    {
        lock (_sync)
        {
            job.State = state;
            job.Progress = progress;
            job.Message = message;
        }
        RaiseChanged();
    }

    private void TrimHistory()
    {
        lock (_sync)
        {
            foreach (var stale in _jobs.Values
                         .Where(job => job.State is not TransferJobState.Queued and not TransferJobState.Running)
                         .OrderByDescending(job => job.CreatedAt).Skip(50).ToList())
            {
                _jobs.Remove(stale.Id);
                stale.Cancellation.Dispose();
            }
        }
    }

    private void RaiseChanged()
    {
        foreach (var handler in Changed?.GetInvocationList().Cast<EventHandler>() ?? [])
        {
            try { handler(this, EventArgs.Empty); } catch { /* A closed view must not stop the queue. */ }
        }
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        lock (_sync)
        {
            foreach (var job in _jobs.Values)
            {
                job.Cancellation.Cancel();
                if (job.State is TransferJobState.Queued or TransferJobState.Running)
                {
                    job.State = TransferJobState.Cancelled;
                    job.Message = "Отменено";
                    job.Completion.TrySetResult(OperationResult.Failure("Операция отменена.", -3));
                }
            }
        }
        _signal.Release();
        var stopped = false;
        try { stopped = _worker.Wait(TimeSpan.FromSeconds(3)); } catch { }
        if (stopped)
        {
            _signal.Dispose();
            _lifetime.Dispose();
        }
    }

    private sealed class TransferJob
    {
        public TransferJob(string deviceSerial, string name, TransferJobKind kind, string source,
            string? destination)
        {
            DeviceSerial = deviceSerial;
            Name = name;
            Kind = kind;
            Source = source;
            Destination = destination;
        }

        public Guid Id { get; } = Guid.NewGuid();
        public string DeviceSerial { get; }
        public string Name { get; }
        public TransferJobKind Kind { get; }
        public string Source { get; }
        public string? Destination { get; }
        public DateTimeOffset CreatedAt { get; } = DateTimeOffset.Now;
        public TransferJobState State { get; set; } = TransferJobState.Queued;
        public double? Progress { get; set; }
        public string Message { get; set; } = "В очереди";
        public CancellationTokenSource Cancellation { get; } = new();
        public TaskCompletionSource<OperationResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TransferJobSnapshot Snapshot() => new(Id, DeviceSerial, Name, Kind, State, Progress, Message,
            CreatedAt);
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
