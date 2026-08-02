using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AndroidWidget.Models;
using AndroidWidget.Services;

namespace AndroidWidget;

public partial class RemoteFilesWindow : Window
{
    private readonly AdbService _adb;
    private readonly AndroidDevice _device;
    private readonly CancellationTokenSource _lifetime = new();
    private string _currentPath = "/sdcard";
    private bool _busy;

    public RemoteFilesWindow(AdbService adb, AndroidDevice device)
    {
        InitializeComponent();
        _adb = adb;
        _device = device;
        DeviceText.Text = $"{device.DisplayName}  ·  {device.ConnectionLabel}";
        Closed += (_, _) => _lifetime.Cancel();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e) => await NavigateAsync(_currentPath);
    private async void CameraButton_Click(object sender, RoutedEventArgs e) => await NavigateAsync("/sdcard/DCIM/Camera");
    private async void DownloadsButton_Click(object sender, RoutedEventArgs e) => await NavigateAsync("/sdcard/Download");
    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await NavigateAsync(_currentPath);

    private async void UpButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPath == "/sdcard")
            return;
        var slash = _currentPath.TrimEnd('/').LastIndexOf('/');
        var parent = slash <= "/sdcard".Length ? "/sdcard" : _currentPath[..slash];
        await NavigateAsync(parent);
    }

    private async Task NavigateAsync(string path)
    {
        if (_busy)
            return;
        _busy = true;
        SetStatus("Читаю папку…");
        try
        {
            var entries = await _adb.ListDirectoryAsync(_device.Serial, path, _lifetime.Token);
            _currentPath = path;
            PathText.Text = path;
            FilesList.ItemsSource = entries;
            EmptyState.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            SetStatus($"Объектов: {entries.Count}");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            SetStatus(ex.Message, true);
        }
        finally
        {
            _busy = false;
        }
    }

    private async void FilesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FilesList.SelectedItem is RemoteEntry entry)
            await OpenEntryAsync(entry);
    }

    private void FilesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = FilesList.SelectedItem is RemoteEntry;
        OpenButton.IsEnabled = selected;
        DownloadButton.IsEnabled = selected;
    }

    private async void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        if (FilesList.SelectedItem is RemoteEntry entry)
            await OpenEntryAsync(entry);
    }

    private async Task OpenEntryAsync(RemoteEntry entry)
    {
        if (entry.IsDirectory)
        {
            await NavigateAsync(entry.FullPath);
            return;
        }

        if (_busy)
            return;
        _busy = true;
        SetStatus($"Открываю {entry.DisplayName}…");
        try
        {
            var safeSerial = string.Concat(_device.Serial.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_'));
            var cacheFolder = Path.Combine(Path.GetTempPath(), "AndroidWidget", safeSerial);
            Directory.CreateDirectory(cacheFolder);
            var localPath = Path.Combine(cacheFolder, entry.DisplayName);
            var result = await _adb.PullFileAsync(_device.Serial, entry.FullPath, localPath, _lifetime.Token);
            if (!result.IsSuccess)
                throw new InvalidOperationException(result.BestMessage);
            Process.Start(new ProcessStartInfo(localPath) { UseShellExecute = true });
            SetStatus("Файл открыт из временной копии ✓");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { SetStatus(ex.Message, true); }
        finally { _busy = false; }
    }

    private async void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (FilesList.SelectedItem is not RemoteEntry entry || _busy)
            return;

        _busy = true;
        SetStatus($"Скачиваю {entry.DisplayName}…");
        try
        {
            var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads", "Android Widget");
            Directory.CreateDirectory(downloads);
            var localPath = GetUniquePath(Path.Combine(downloads, entry.DisplayName));
            var result = await _adb.PullFileAsync(_device.Serial, entry.FullPath, localPath, _lifetime.Token);
            if (!result.IsSuccess)
                throw new InvalidOperationException(result.BestMessage);
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{localPath}\"") { UseShellExecute = true });
            SetStatus($"Сохранено: {localPath} ✓");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { SetStatus(ex.Message, true); }
        finally { _busy = false; }
    }

    private void SetStatus(string message, bool error = false)
    {
        StatusText.Text = message;
        StatusText.Foreground = new SolidColorBrush(error
            ? Color.FromRgb(255, 126, 126)
            : Color.FromRgb(126, 138, 168));
    }

    private static string GetUniquePath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
            return path;
        var directory = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var i = 2; i < 1000; i++)
        {
            var candidate = Path.Combine(directory, $"{name} ({i}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
                return candidate;
        }
        return Path.Combine(directory, $"{name}_{DateTime.Now:yyyyMMddHHmmss}{extension}");
    }
}
