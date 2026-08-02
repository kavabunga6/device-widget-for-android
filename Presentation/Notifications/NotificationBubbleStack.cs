using System.Collections.ObjectModel;
using System.Windows.Threading;

namespace AndroidWidget.Presentation.Notifications;

public sealed class NotificationBubbleStack : IDisposable
{
    public const int MaximumVisible = 5;

    private readonly Dictionary<Guid, DispatcherTimer> _timers = new();
    private readonly HashSet<string> _activeIdentities = new(StringComparer.Ordinal);

    public ObservableCollection<NotificationBubbleItem> Items { get; } = new();

    public event EventHandler? Changed;

    public void Add(PhoneMessage message)
    {
        var identity = $"{message.Fingerprint}\0{message.Sender}\0{message.Preview}";
        if (!_activeIdentities.Add(identity))
            return;

        if (Items.Count >= MaximumVisible)
            Remove(Items[0]);

        Items.Add(new NotificationBubbleItem(Guid.NewGuid(), identity, message));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Remove(NotificationBubbleItem item)
    {
        if (!Items.Remove(item))
            return;

        StopTimer(item.Id);
        _activeIdentities.Remove(item.Identity);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Start(TimeSpan duration)
    {
        foreach (var item in Items.Where(item => !_timers.ContainsKey(item.Id)).ToList())
        {
            var timer = new DispatcherTimer { Interval = duration };
            timer.Tick += (_, _) => Remove(item);
            _timers[item.Id] = timer;
            timer.Start();
        }
    }

    public void Restart(TimeSpan duration)
    {
        Pause();
        Start(duration);
    }

    public void Pause()
    {
        foreach (var timer in _timers.Values)
            timer.Stop();
        _timers.Clear();
    }

    public void Clear()
    {
        Pause();
        Items.Clear();
        _activeIdentities.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => Clear();

    private void StopTimer(Guid id)
    {
        if (!_timers.Remove(id, out var timer))
            return;
        timer.Stop();
    }
}

public sealed record NotificationBubbleItem(Guid Id, string Identity, PhoneMessage Message);
