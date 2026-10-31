using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace AndroidWidget.Desktop;

internal sealed record NotificationHistoryRow(string Message, string PostedAtText);

internal sealed partial class NotificationHistoryWindow : Window
{
    private readonly DesktopNotificationStore _store;
    private readonly Action _clearVisibleBubbles;

    public NotificationHistoryWindow(DesktopNotificationStore store, Action clearVisibleBubbles)
    {
        _store = store;
        _clearVisibleBubbles = clearVisibleBubbles;
        InitializeComponent();
        _store.Changed += Store_Changed;
        Closed += (_, _) => _store.Changed -= Store_Changed;
        Render();
    }

    private void Store_Changed(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
            Render();
        else
            Dispatcher.UIThread.Post(() => Render());
    }

    private void ClearButton_Click(object? sender, RoutedEventArgs e)
    {
        var removed = _store.Clear();
        _clearVisibleBubbles();
        StatusText.Text = removed == 0 ? "Уже пусто" : $"Очищено: {removed}";
        StatusText.IsVisible = true;
        Render(preserveStatus: true);
    }

    private void Render(bool preserveStatus = false)
    {
        var items = _store.Snapshot();
        SubtitleText.Text = $"{items.Count} записей";
        ClearButton.IsVisible = items.Count > 0;
        HistoryItems.ItemsSource = items.Select(item => new NotificationHistoryRow(item.Message,
            DateTimeOffset.FromUnixTimeMilliseconds(item.PostedAtUnixMilliseconds).ToLocalTime()
                .ToString("dd.MM · HH:mm"))).ToArray();
        EmptyState.IsVisible = items.Count == 0;
        if (!preserveStatus)
            StatusText.IsVisible = false;
    }
}
