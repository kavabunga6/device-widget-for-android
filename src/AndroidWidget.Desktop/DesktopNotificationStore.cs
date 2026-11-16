using System.Text.Json;

namespace AndroidWidget.Desktop;

internal sealed record DesktopNotificationItem(
    Guid Id,
    string Message,
    long PostedAtUnixMilliseconds);

internal sealed class DesktopNotificationState
{
    public int Version { get; set; }
    public List<DesktopNotificationItem> Items { get; set; } = [];
}

internal enum DesktopNotificationChangeKind
{
    Added,
    Read,
    Removed,
    Cleared
}

internal sealed record DesktopNotificationChange(DesktopNotificationChangeKind Kind, Guid? ItemId = null);

internal sealed class DesktopNotificationStore
{
    internal const int CurrentVersion = 2;
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
        (_state, var migrated) = Load(_path);
        if (migrated)
            Persist();
    }

    public event EventHandler<DesktopNotificationChange>? Changed;

    public Guid? Add(string message)
    {
        var normalized = NormalizeMessage(message);
        if (normalized is null)
            return null;
        var id = Guid.NewGuid();
        lock (_gate)
        {
            var previousItems = _state.Items.ToList();
            _state.Items.Insert(0, new DesktopNotificationItem(id, normalized,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
            if (_state.Items.Count > MaximumItems)
                _state.Items.RemoveRange(MaximumItems, _state.Items.Count - MaximumItems);
            if (!Persist())
            {
                _state.Items = previousItems;
                return null;
            }
        }
        Changed?.Invoke(this, new DesktopNotificationChange(DesktopNotificationChangeKind.Added, id));
        return id;
    }

    public IReadOnlyList<DesktopNotificationItem> Snapshot()
    {
        lock (_gate)
            return _state.Items.ToArray();
    }

    public bool MarkRead(Guid id) => RemoveCore(id, DesktopNotificationChangeKind.Read);

    public bool Remove(Guid id) => RemoveCore(id, DesktopNotificationChangeKind.Removed);

    private bool RemoveCore(Guid id, DesktopNotificationChangeKind changeKind)
    {
        bool changed;
        lock (_gate)
        {
            var index = _state.Items.FindIndex(item => item.Id == id);
            changed = index >= 0;
            if (!changed)
                return false;
            var previous = _state.Items[index];
            _state.Items.RemoveAt(index);
            if (!Persist())
            {
                _state.Items.Insert(index, previous);
                return false;
            }
        }
        Changed?.Invoke(this, new DesktopNotificationChange(changeKind, id));
        return true;
    }

    public int Clear()
    {
        int removed;
        lock (_gate)
        {
            removed = _state.Items.Count;
            if (_state.Items.Count == 0)
                return 0;
            var previousItems = _state.Items;
            _state.Items = [];
            if (!Persist())
            {
                _state.Items = previousItems;
                return 0;
            }
        }
        Changed?.Invoke(this, new DesktopNotificationChange(DesktopNotificationChangeKind.Cleared));
        return removed;
    }

    private bool Persist()
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
            return true;
        }
        catch
        {
            // Mutations are committed only after the replacement file is safely written.
            return false;
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

    private static (DesktopNotificationState State, bool Migrated) Load(string path)
    {
        try
        {
            if (!File.Exists(path))
                return (CreateEmptyState(), false);
            var state = JsonSerializer.Deserialize<DesktopNotificationState>(File.ReadAllText(path)) ??
                        CreateEmptyState();
            var migrated = state.Version != CurrentVersion;
            var items = new List<DesktopNotificationItem>(MaximumItems);
            var ids = new HashSet<Guid>();
            foreach (var item in state.Items ?? [])
            {
                if (item is null ||
                    item.PostedAtUnixMilliseconds is < MinimumUnixMilliseconds or > MaximumUnixMilliseconds ||
                    NormalizeMessage(item.Message) is not { } message)
                {
                    migrated = true;
                    continue;
                }
                var id = item.Id;
                if (id == Guid.Empty || !ids.Add(id))
                {
                    do id = Guid.NewGuid();
                    while (!ids.Add(id));
                    migrated = true;
                }
                if (!string.Equals(message, item.Message, StringComparison.Ordinal))
                    migrated = true;
                items.Add(new DesktopNotificationItem(id, message, item.PostedAtUnixMilliseconds));
                if (items.Count == MaximumItems)
                {
                    if ((state.Items?.Count ?? 0) > MaximumItems)
                        migrated = true;
                    break;
                }
            }
            state.Version = CurrentVersion;
            state.Items = items;
            return (state, migrated);
        }
        catch
        {
            return (CreateEmptyState(), false);
        }
    }

    private static DesktopNotificationState CreateEmptyState() =>
        new() { Version = CurrentVersion };

    private static string? NormalizeMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return null;
        var normalized = message.Trim();
        return normalized.Length <= MaximumMessageLength ? normalized : normalized[..MaximumMessageLength];
    }
}
