using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace AndroidWidget.Desktop;

internal sealed class TransferQueueWindow : Window
{
    private readonly DesktopTransferQueue _queue;
    private readonly string _serial;
    private readonly StackPanel _items = new() { Spacing = 8 };

    public TransferQueueWindow(DesktopTransferQueue queue, string serial)
    {
        _queue = queue;
        _serial = serial;
        Title = "Передачи · Device Widget";
        Width = 470;
        Height = 420;
        MinWidth = 380;
        MinHeight = 280;
        Content = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Margin = new Thickness(18),
            Children =
            {
                new TextBlock { Text = "Очередь передач", FontSize = 20, FontWeight = FontWeight.SemiBold },
                new ScrollViewer { Content = _items, Margin = new Thickness(0, 18, 0, 0),
                    [Grid.RowProperty] = 1 }
            }
        };
        _queue.Changed += Queue_Changed;
        Closed += (_, _) => _queue.Changed -= Queue_Changed;
        Refresh();
    }

    private void Queue_Changed(object? sender, EventArgs e) => Dispatcher.UIThread.Post(Refresh);

    private void Refresh()
    {
        _items.Children.Clear();
        foreach (var item in _queue.Snapshot.Where(item => item.Serial == _serial))
        {
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
                Margin = new Thickness(0, 0, 0, 6)
            };
            row.Children.Add(new TextBlock
            {
                Text = item.Name,
                Margin = new Thickness(12, 10, 12, 2),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            row.Children.Add(new TextBlock
            {
                Text = item.Message,
                Margin = new Thickness(12, 0, 12, 8),
                FontSize = 11,
                [Grid.RowProperty] = 1
            });
            var progress = new ProgressBar
            {
                Minimum = 0,
                Maximum = 1,
                Value = item.Progress ?? 0,
                IsIndeterminate = item.State == DesktopTransferState.Running && item.Progress is null,
                Height = 4,
                Margin = new Thickness(12, 0, 12, 10),
                [Grid.RowProperty] = 2
            };
            row.Children.Add(progress);
            if (item.CanCancel)
            {
                var cancel = new Button
                {
                    Content = "Отмена",
                    Margin = new Thickness(8),
                    Tag = item.Id,
                    VerticalAlignment = VerticalAlignment.Center,
                    [Grid.ColumnProperty] = 1,
                    [Grid.RowSpanProperty] = 3
                };
                cancel.Click += (_, _) => _queue.Cancel((Guid)cancel.Tag!);
                row.Children.Add(cancel);
            }
            _items.Children.Add(row);
        }
        if (_items.Children.Count == 0)
            _items.Children.Add(new TextBlock { Text = "Передач пока нет" });
    }
}
