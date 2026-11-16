using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AndroidWidget.Desktop;

internal sealed record NotificationHistoryRow(Guid Id, string Message, string PostedAtText)
{
    public string AutomationName => $"Отметить прочитанным: {PostedAtText}. {Message}";
    public string DeleteAutomationName => $"Удалить запись: {Message}";
}

internal sealed partial class NotificationHistoryWindow : Window
{
    private readonly DesktopNotificationStore _store;
    private readonly ObservableCollection<NotificationHistoryRow> _rows = [];
    private bool _applyingLocalChange;

    public NotificationHistoryWindow(DesktopNotificationStore store)
    {
        _store = store;
        InitializeComponent();
        HistoryItems.ItemsSource = _rows;
        _store.Changed += Store_Changed;
        Closed += (_, _) => _store.Changed -= Store_Changed;
        Reload();
    }

    private void Store_Changed(object? sender, DesktopNotificationChange e)
    {
        if (_applyingLocalChange)
            return;
        if (Dispatcher.UIThread.CheckAccess())
            ApplyStoreChange(e);
        else
            Dispatcher.UIThread.Post(() => ApplyStoreChange(e));
    }

    private void ClearButton_Click(object? sender, RoutedEventArgs e)
    {
        _applyingLocalChange = true;
        int removed;
        try
        {
            removed = _store.Clear();
        }
        finally
        {
            _applyingLocalChange = false;
        }
        if (removed == 0 && _rows.Count > 0)
        {
            ShowSaveError();
            return;
        }
        _rows.Clear();
        StatusText.Text = removed == 0 ? "Уже пусто" : $"Очищено: {removed}";
        StatusText.IsVisible = true;
        UpdateState(preserveStatus: true);
        Dispatcher.UIThread.Post(() => EmptyState.Focus());
    }

    private void NotificationCard_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: NotificationHistoryRow row })
            return;
        var index = _rows.IndexOf(row);
        _applyingLocalChange = true;
        try
        {
            if (!_store.MarkRead(row.Id))
            {
                ShowSaveError();
                return;
            }
        }
        finally
        {
            _applyingLocalChange = false;
        }
        RemoveRow(row, index);
    }

    private void DeleteNotification_Click(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button { DataContext: NotificationHistoryRow row })
            return;
        var index = _rows.IndexOf(row);
        _applyingLocalChange = true;
        try
        {
            if (!_store.Remove(row.Id))
            {
                ShowSaveError();
                return;
            }
        }
        finally
        {
            _applyingLocalChange = false;
        }
        RemoveRow(row, index);
    }

    private void RemoveRow(NotificationHistoryRow row, int previousIndex)
    {
        _rows.Remove(row);
        UpdateState();
        Dispatcher.UIThread.Post(() => FocusAfterRemoval(previousIndex));
    }

    private void FocusAfterRemoval(int previousIndex)
    {
        if (_rows.Count == 0)
        {
            EmptyState.Focus();
            return;
        }
        var buttons = HistoryItems.GetVisualDescendants().OfType<Button>()
            .Where(button => button.Classes.Contains("rowSurface"))
            .ToArray();
        if (buttons.Length == 0)
        {
            ClearButton.Focus();
            return;
        }
        buttons[Math.Clamp(previousIndex, 0, buttons.Length - 1)].Focus();
    }

    private void Reload()
    {
        _rows.Clear();
        foreach (var item in _store.Snapshot())
            _rows.Add(CreateRow(item));
        UpdateState();
    }

    private void ApplyStoreChange(DesktopNotificationChange change)
    {
        switch (change.Kind)
        {
            case DesktopNotificationChangeKind.Added when change.ItemId is { } addedId:
                var snapshot = _store.Snapshot();
                var added = snapshot.FirstOrDefault(item => item.Id == addedId);
                if (added is not null && _rows.All(row => row.Id != addedId))
                    _rows.Insert(0, CreateRow(added));
                var retainedIds = snapshot.Select(item => item.Id).ToHashSet();
                for (var index = _rows.Count - 1; index >= 0; index--)
                    if (!retainedIds.Contains(_rows[index].Id))
                        _rows.RemoveAt(index);
                break;
            case DesktopNotificationChangeKind.Read or DesktopNotificationChangeKind.Removed
                when change.ItemId is { } removedId:
                if (_rows.FirstOrDefault(row => row.Id == removedId) is { } removed)
                    _rows.Remove(removed);
                break;
            case DesktopNotificationChangeKind.Cleared:
                _rows.Clear();
                break;
            default:
                Reload();
                return;
        }
        UpdateState();
    }

    private static NotificationHistoryRow CreateRow(DesktopNotificationItem item) =>
        new(item.Id, item.Message,
            DateTimeOffset.FromUnixTimeMilliseconds(item.PostedAtUnixMilliseconds).ToLocalTime()
                .ToString("dd.MM · HH:mm"));

    private void ShowSaveError()
    {
        StatusText.Text = "Не удалось сохранить изменение";
        StatusText.IsVisible = true;
    }

    private void UpdateState(bool preserveStatus = false)
    {
        SubtitleText.Text = $"{_rows.Count} записей";
        ClearButton.IsVisible = _rows.Count > 0;
        EmptyState.IsVisible = _rows.Count == 0;
        if (!preserveStatus)
            StatusText.IsVisible = false;
    }
}
