using System.Collections.Concurrent;

namespace AndroidWidget.Desktop;

internal enum DesktopTransferState { Queued, Running, Completed, Failed, Cancelled }

internal sealed record DesktopTransferSnapshot(Guid Id, string Serial, string Name, bool IsApk,
    DesktopTransferState State, double? Progress, string Message, DateTimeOffset CreatedAt)
{
    public bool CanCancel => State is DesktopTransferState.Queued or DesktopTransferState.Running;
}

internal sealed class DesktopTransferQueue : IDisposable
{
    private readonly PortableAdbService _adb;
    private readonly ConcurrentQueue<Job> _pending = new();
    private readonly Dictionary<Guid, Job> _jobs = [];
    private readonly SemaphoreSlim _signal = new(0);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _gate = new();
    private readonly Task _worker;

    public DesktopTransferQueue(PortableAdbService adb)
    {
        _adb = adb;
        _worker = Task.Run(ProcessAsync);
    }

    public event EventHandler? Changed;

    public IReadOnlyList<DesktopTransferSnapshot> Snapshot
    {
        get
        {
            lock (_gate)
                return _jobs.Values.OrderByDescending(job => job.CreatedAt).Select(job => job.Snapshot()).ToList();
        }
    }

    public Guid Enqueue(string serial, string path, bool? installApk = null)
    {
        var fullPath = Path.GetFullPath(path);
        var job = new Job(serial, fullPath, installApk ??
            Path.GetExtension(fullPath).Equals(".apk", StringComparison.OrdinalIgnoreCase));
        lock (_gate)
            _jobs.Add(job.Id, job);
        _pending.Enqueue(job);
        _signal.Release();
        RaiseChanged();
        return job.Id;
    }

    public void Cancel(Guid id)
    {
        lock (_gate)
        {
            if (!_jobs.TryGetValue(id, out var job) || !job.Snapshot().CanCancel)
                return;
            job.Cancellation.Cancel();
            if (job.State == DesktopTransferState.Queued)
            {
                job.State = DesktopTransferState.Cancelled;
                job.Message = "Отменено";
            }
        }
        RaiseChanged();
    }

    private async Task ProcessAsync()
    {
        while (!_lifetime.IsCancellationRequested)
        {
            try { await _signal.WaitAsync(_lifetime.Token); }
            catch (OperationCanceledException) { break; }
            if (!_pending.TryDequeue(out var job) || job.Cancellation.IsCancellationRequested)
                continue;
            Update(job, DesktopTransferState.Running, null, job.IsApk ? "Установка APK…" : "Передача…");
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, job.Cancellation.Token);
            try
            {
                var progress = new Progress<double>(value =>
                    Update(job, DesktopTransferState.Running, value, $"{Math.Round(value * 100)}%"));
                var result = job.IsApk
                    ? await _adb.InstallAsync(job.Serial, job.Path, linked.Token)
                    : await _adb.PushAsync(job.Serial, job.Path, progress, linked.Token);
                Update(job, result.IsSuccess ? DesktopTransferState.Completed : DesktopTransferState.Failed,
                    result.IsSuccess ? 1 : job.Progress,
                    result.IsSuccess ? (job.IsApk ? "Приложение установлено" : "Файл передан") : result.Message);
            }
            catch (OperationCanceledException)
            {
                Update(job, DesktopTransferState.Cancelled, job.Progress, "Отменено");
            }
            catch (Exception ex)
            {
                Update(job, DesktopTransferState.Failed, job.Progress, ex.Message);
            }
        }
    }

    private void Update(Job job, DesktopTransferState state, double? progress, string message)
    {
        lock (_gate)
        {
            job.State = state;
            job.Progress = progress;
            job.Message = message;
        }
        RaiseChanged();
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        _lifetime.Cancel();
        lock (_gate)
            foreach (var job in _jobs.Values)
                job.Cancellation.Cancel();
        _signal.Release();
    }

    private sealed class Job(string serial, string path, bool isApk)
    {
        public Guid Id { get; } = Guid.NewGuid();
        public string Serial { get; } = serial;
        public string Path { get; } = path;
        public bool IsApk { get; } = isApk;
        public DateTimeOffset CreatedAt { get; } = DateTimeOffset.Now;
        public DesktopTransferState State { get; set; } = DesktopTransferState.Queued;
        public double? Progress { get; set; }
        public string Message { get; set; } = "В очереди";
        public CancellationTokenSource Cancellation { get; } = new();
        public DesktopTransferSnapshot Snapshot() => new(Id, Serial,
            System.IO.Path.GetFileName(Path.TrimEnd(System.IO.Path.DirectorySeparatorChar)), IsApk,
            State, Progress, Message, CreatedAt);
    }
}
