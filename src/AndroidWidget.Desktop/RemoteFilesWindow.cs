using System.Security.Cryptography;
using System.Text;
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
    private static readonly HashSet<string> UnsafePreviewExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".app", ".bat", ".cmd", ".com", ".desktop", ".exe", ".js", ".jse", ".lnk", ".msi", ".ps1",
        ".run", ".scr", ".sh", ".url", ".vbe", ".vbs", ".wsf", ".wsh"
    };
    private readonly PortableAdbService _adb;
    private readonly string _serial;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ListBox _entries = new();
    private readonly TextBlock _pathText = new();
    private readonly TextBlock _status = new();
    private string _path = "/sdcard";
    private bool _busy;

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
        _entries.Classes.Add("remote-files");
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
        Closed += (_, _) =>
        {
            _lifetime.Cancel();
            _lifetime.Dispose();
        };
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
            _entries.ItemsSource = await _adb.ListDirectoryAsync(_serial, _path, _lifetime.Token);
            _status.Text = "Двойной щелчок открывает папку или файл";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
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
        if (_entries.SelectedItem is not PortableRemoteEntry entry || _busy)
            return;
        if (entry.IsDirectory)
        {
            await NavigateAsync(entry.Path);
            return;
        }

        var extension = Path.GetExtension(entry.Name);
        if (UnsafePreviewExtensions.Contains(extension))
        {
            _status.Text = "Этот тип файла нельзя запускать из предпросмотра · используйте «Скачать…»";
            return;
        }

        _busy = true;
        _status.Text = $"Открываю {entry.Name}…";
        try
        {
            var localPath = CreatePreviewPath(entry);
            var result = await _adb.PullAsync(_serial, entry.Path, localPath, _lifetime.Token);
            if (!result.IsSuccess)
                throw new InvalidOperationException(result.Message);
            DesktopFileLauncher.Open(localPath);
            _status.Text = "Файл открыт из временной копии ✓";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task DownloadSelectedAsync()
    {
        if (_entries.SelectedItem is not PortableRemoteEntry entry || _busy)
            return;
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Скачать с телефона",
            AllowMultiple = false
        });
        if (folders.FirstOrDefault() is not { } folder)
            return;
        _busy = true;
        _status.Text = "Скачивание…";
        try
        {
            var result = await _adb.PullAsync(_serial, entry.Path, folder.Path.LocalPath, _lifetime.Token);
            _status.Text = result.IsSuccess ? $"Скачано: {entry.Name}" : result.Message;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        finally
        {
            _busy = false;
        }
    }

    private string CreatePreviewPath(PortableRemoteEntry entry)
    {
        var serialKey = Hash(_serial)[..12];
        var fileKey = Hash(entry.Path)[..16];
        var extension = Path.GetExtension(entry.Name);
        if (extension.Length > 16 || extension.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            extension = string.Empty;
        var directory = Path.Combine(Path.GetTempPath(), "DeviceWidget", "Previews", serialKey);
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"preview-{fileKey}{extension}");
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

}
