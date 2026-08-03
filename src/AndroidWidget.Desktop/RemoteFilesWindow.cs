using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;

namespace AndroidWidget.Desktop;

internal sealed class RemoteFilesWindow : Window
{
    private readonly PortableAdbService _adb;
    private readonly string _serial;
    private readonly ListBox _entries = new();
    private readonly TextBlock _pathText = new();
    private readonly TextBlock _status = new();
    private string _path = "/sdcard";

    public RemoteFilesWindow(PortableAdbService adb, string serial)
    {
        _adb = adb;
        _serial = serial;
        Title = "Файлы телефона";
        using var iconStream = AssetLoader.Open(new Uri("avares://DeviceWidget/Assets/AppIcon.png"));
        Icon = new WindowIcon(iconStream);
        Width = 620;
        Height = 560;
        MinWidth = 440;
        MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid { Margin = new Thickness(18), RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto") };
        var title = new TextBlock { Text = "Файлы и фотографии", FontSize = 23, FontWeight = FontWeight.SemiBold };
        root.Children.Add(title);

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 14, 0, 10) };
        toolbar[Grid.RowProperty] = 1;
        toolbar.Children.Add(Button("Назад", async () => await GoUpAsync()));
        toolbar.Children.Add(Button("Память", async () => await NavigateAsync("/sdcard")));
        toolbar.Children.Add(Button("Camera", async () => await NavigateAsync("/sdcard/DCIM/Camera")));
        toolbar.Children.Add(Button("Download", async () => await NavigateAsync("/sdcard/Download")));
        toolbar.Children.Add(_pathText);
        _pathText.VerticalAlignment = VerticalAlignment.Center;
        _pathText.TextTrimming = TextTrimming.CharacterEllipsis;
        root.Children.Add(toolbar);

        _entries[Grid.RowProperty] = 2;
        _entries.DoubleTapped += async (_, _) => await OpenSelectedAsync();
        root.Children.Add(_entries);

        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(0, 10, 0, 0) };
        footer[Grid.RowProperty] = 3;
        _status.VerticalAlignment = VerticalAlignment.Center;
        footer.Children.Add(_status);
        var open = Button("Открыть", OpenSelectedAsync);
        open[Grid.ColumnProperty] = 1;
        footer.Children.Add(open);
        var download = Button("Скачать…", DownloadSelectedAsync);
        download[Grid.ColumnProperty] = 2;
        download.Margin = new Thickness(8, 0, 0, 0);
        footer.Children.Add(download);
        root.Children.Add(footer);
        Content = root;
        Opened += async (_, _) => await RefreshAsync();
    }

    private static Button Button(string text, Func<Task> action)
    {
        var button = new Button { Content = text, Padding = new Thickness(12, 7) };
        button.Click += async (_, _) => await action();
        return button;
    }

    private async Task RefreshAsync()
    {
        try
        {
            _status.Text = "Загрузка…";
            _pathText.Text = _path;
            _entries.ItemsSource = await _adb.ListDirectoryAsync(_serial, _path, CancellationToken.None);
            _status.Text = "Двойной щелчок открывает папку";
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
        }
    }

    private async Task NavigateAsync(string path)
    {
        _path = path;
        await RefreshAsync();
    }

    private Task GoUpAsync()
    {
        if (_path == "/sdcard")
            return Task.CompletedTask;
        var index = _path.LastIndexOf('/');
        return NavigateAsync(index <= 0 ? "/sdcard" : _path[..index]);
    }

    private async Task OpenSelectedAsync()
    {
        if (_entries.SelectedItem is PortableRemoteEntry { IsDirectory: true } entry)
            await NavigateAsync(entry.Path);
    }

    private async Task DownloadSelectedAsync()
    {
        if (_entries.SelectedItem is not PortableRemoteEntry entry)
            return;
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Скачать с телефона",
            AllowMultiple = false
        });
        if (folders.FirstOrDefault() is not { } folder)
            return;
        _status.Text = "Скачивание…";
        var result = await _adb.PullAsync(_serial, entry.Path, folder.Path.LocalPath, CancellationToken.None);
        _status.Text = result.IsSuccess ? $"Скачано: {entry.Name}" : result.Message;
    }
}
