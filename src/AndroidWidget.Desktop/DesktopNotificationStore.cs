using System.Text.Json;

namespace AndroidWidget.Desktop;

internal sealed record DesktopNotificationItem(string Message, long PostedAtUnixMilliseconds);

internal sealed class DesktopNotificationState
{
    public List<DesktopNotificationItem> Items { get; set; } = [];
}

internal sealed class DesktopNotificationStore
{
    private const int MaximumItems = 200;
    private const int MaximumMessageLength = 4096;
    private const long MinimumUnixMilliseconds = -62135596800000;
    private const long MaximumUnixMilliseconds = 253402300799999;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object _gate = new();
    private readonly string _path;
    private DesktopNotificationState _state;

    public DesktopNotificationStore()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AndroidWidget");
        _path = Path.Combine(directory, "widget-notification-history.json");
        _state = Load(_path);
    }

    public event EventHandler? Changed;

    public void Add(string message)
    {
        var normalized = NormalizeMessage(message);
        if (normalized is null)
            return;
        lock (_gate)
        {
            _state.Items.Insert(0, new DesktopNotificationItem(normalized,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
            if (_state.Items.Count > MaximumItems)
                _state.Items.RemoveRange(MaximumItems, _state.Items.Count - MaximumItems);
            Persist();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<DesktopNotificationItem> Snapshot()
    {
        lock (_gate)
            return _state.Items.ToArray();
    }

    public int Clear()
    {
        int removed;
        lock (_gate)
        {
            removed = _state.Items.Count;
            if (removed == 0)
                return 0;
            _state.Items.Clear();
            Persist();
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return removed;
    }

    private void Persist()
    {
        string? temporaryPath = null;
        try
        {
            var directory = Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(directory);
            temporaryPath = Path.Combine(directory, $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(_state, JsonOptions));
            File.Move(temporaryPath, _path, overwrite: true);
            temporaryPath = null;
        }
        catch
        {
            // History remains available for the current session if persistence is unavailable.
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch
                {
                    // A stale temporary file is harmless and can be replaced on the next write.
                }
            }
        }
    }

    private static DesktopNotificationState Load(string path)
    {
        try
        {
            if (!File.Exists(path))
                return new DesktopNotificationState();
            var state = JsonSerializer.Deserialize<DesktopNotificationState>(File.ReadAllText(path)) ??
                        new DesktopNotificationState();
            state.Items = (state.Items ?? [])
                .Where(item => item is not null &&
                               item.PostedAtUnixMilliseconds is >= MinimumUnixMilliseconds and <= MaximumUnixMilliseconds)
                .Select(item => (Message: NormalizeMessage(item.Message), item.PostedAtUnixMilliseconds))
                .Where(item => item.Message is not null)
                .Select(item => new DesktopNotificationItem(item.Message!, item.PostedAtUnixMilliseconds))
                .Take(MaximumItems)
                .ToList();
            return state;
        }
        catch
        {
            return new DesktopNotificationState();
        }
    }

    private static string? NormalizeMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return null;
        var normalized = message.Trim();
        return normalized.Length <= MaximumMessageLength ? normalized : normalized[..MaximumMessageLength];
    }
}
