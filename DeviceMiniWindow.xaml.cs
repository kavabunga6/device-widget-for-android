using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AndroidWidget.Models;
using AndroidWidget.Services;
using Microsoft.Win32;

namespace AndroidWidget;

public partial class DeviceMiniWindow : Window
{
    private readonly AdbService _adb = new();
    private AndroidDevice _device;
    private Point _mouseDownPoint;
    private bool _dragStarted;
    private bool _transferring;
    private bool _actionRunning;

    public string Serial => _device.Serial;

    public DeviceMiniWindow(AndroidDevice device)
    {
        InitializeComponent();
        _device = device;
        Topmost = SettingsService.Current.Topmost;
        UpdateDevice(device);
    }

    public void UpdateDevice(AndroidDevice device)
    {
        _device = device;
        ApplySkin(device);
        DeviceNameText.Text = device.DisplayName;
        ConnectionText.Text = $"{device.ConnectionLabel} · {ShortSerial(device.Serial)}";
        StatusDot.Fill = new SolidColorBrush(device.State switch
        {
            DeviceConnectionState.Online => Color.FromRgb(114, 216, 162),
            DeviceConnectionState.Unauthorized => Color.FromRgb(255, 190, 92),
            DeviceConnectionState.Offline => Color.FromRgb(255, 105, 105),
            _ => Color.FromRgb(105, 115, 142)
        });

        var sleepingOrLocked = device.State == DeviceConnectionState.Online && (!device.IsScreenOn || device.IsLocked);
        var authorizationRequired = device.State == DeviceConnectionState.Unauthorized;
        PowerIcon.Visibility = sleepingOrLocked && !device.IsLocked ? Visibility.Visible : Visibility.Collapsed;
        LockIcon.Visibility = sleepingOrLocked && device.IsLocked ? Visibility.Visible : Visibility.Collapsed;
        AuthorizationIcon.Visibility = authorizationRequired ? Visibility.Visible : Visibility.Collapsed;
        if (authorizationRequired)
        {
            var warning = new SolidColorBrush(Color.FromRgb(255, 184, 77));
            Card.BorderBrush = warning;
            MiniPhoneBody.BorderBrush = warning;
        }
        DetailText.Text = authorizationRequired
            ? "Подтвердите RSA-ключ"
            : sleepingOrLocked
            ? device.IsLocked ? "Телефон заблокирован" : "Экран выключен"
            : device.BatteryPercent is int battery ? $"Заряд {battery}%" : "Нажмите, чтобы открыть";
        DetailText.Foreground = authorizationRequired
            ? new SolidColorBrush(Color.FromRgb(255, 184, 77))
            : sleepingOrLocked
                ? new SolidColorBrush(Color.FromRgb(240, 68, 68))
                : (Brush)FindResource("TextSecondary");
        ToolTip = $"{device.DisplayName}\n{device.Serial}\n{DetailText.Text}";
    }

    private void ApplySkin(AndroidDevice device)
    {
        var skin = PhoneSkinResolver.Resolve(device);
        var accent = new SolidColorBrush(skin.Accent);
        Card.BorderBrush = accent;
        MiniPhoneBody.Background = new SolidColorBrush(skin.Body);
        MiniPhoneBody.BorderBrush = accent;
        MiniPhoneBody.CornerRadius = new CornerRadius(Math.Max(7, skin.ShellRadius * 0.34));
        MiniPhoneScreen.CornerRadius = new CornerRadius(Math.Max(5, skin.ScreenRadius * 0.28));
        MiniSkinFrame.Stroke = accent;
        MiniSkinHomeBar.Background = accent;
        MiniCameraCutout.Visibility = skin.Camera == CameraCutout.None ? Visibility.Collapsed : Visibility.Visible;
        MiniCameraCutout.Width = skin.Camera == CameraCutout.Pill ? 11 : 4;
        MiniCameraCutout.HorizontalAlignment = skin.Camera == CameraCutout.LeftPunch
            ? HorizontalAlignment.Left
            : HorizontalAlignment.Center;
        MiniCameraCutout.Margin = skin.Camera == CameraCutout.LeftPunch
            ? new Thickness(7, 3, 0, 0)
            : new Thickness(0, 3, 0, 0);
    }

    public void PlaceAt(int index)
    {
        var workArea = SystemParameters.WorkArea;
        const double gap = 10;
        var perColumn = Math.Max(1, (int)((workArea.Height - 30) / (Height + gap)));
        var column = index / perColumn;
        var row = index % perColumn;
        Left = workArea.Right - Width - 18 - column * (Width + gap);
        Top = workArea.Bottom - Height - 18 - row * (Height + gap);
    }

    private void Card_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _mouseDownPoint = e.GetPosition(this);
        _dragStarted = false;
        Card.CaptureMouse();
        e.Handled = true;
    }

    private void Card_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || !Card.IsMouseCaptured || _dragStarted)
            return;
        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _mouseDownPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _mouseDownPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;
        _dragStarted = true;
        Card.ReleaseMouseCapture();
        DragMove();
        e.Handled = true;
    }

    private void Card_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (Card.IsMouseCaptured)
            Card.ReleaseMouseCapture();
        if (!_dragStarted && !_transferring)
            ((App)System.Windows.Application.Current).ShowMainFor(_device.Serial);
        e.Handled = true;
    }

    private void ActionMenu_Opening(object sender, RoutedEventArgs e)
    {
        var enabled = _device.State == DeviceConnectionState.Online && !_actionRunning && !_transferring;
        ScreenMenuItem.IsEnabled = enabled;
        FilesMenuItem.IsEnabled = enabled;
        ScreenshotMenuItem.IsEnabled = enabled;
        InstallMenuItem.IsEnabled = enabled;
        PowerMenuItem.IsEnabled = enabled;
    }

    private void ExpandMenuItem_Click(object sender, RoutedEventArgs e) =>
        ((App)System.Windows.Application.Current).ShowMainFor(_device.Serial);

    private void ScreenMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_adb.TryStartScrcpy(_device.Serial, out var error))
            SetActionStatus("scrcpy запущен ✓");
        else
            SetActionStatus(error ?? "Не удалось запустить scrcpy", true);
    }

    private void FilesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        new RemoteFilesWindow(_adb, _device) { Owner = this }.Show();
        SetActionStatus("Открыт браузер файлов");
    }

    private async void ScreenshotMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var defaultFolder = SettingsService.Current.ScreenshotFolder;
        if (string.IsNullOrWhiteSpace(defaultFolder) || !Directory.Exists(defaultFolder))
            defaultFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        var dialog = new SaveFileDialog
        {
            Title = $"Снимок экрана {_device.DisplayName}",
            Filter = "PNG-изображение (*.png)|*.png",
            DefaultExt = ".png",
            AddExtension = true,
            OverwritePrompt = true,
            InitialDirectory = defaultFolder,
            FileName = $"{SafeFileName(_device.DisplayName)}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png"
        };
        if (dialog.ShowDialog(this) != true)
            return;

        var file = dialog.FileName;
        SettingsService.Update(settings => settings with { ScreenshotFolder = Path.GetDirectoryName(file) });
        await RunMenuActionAsync(async () =>
        {
            var result = await _adb.TakeScreenshotAsync(_device.Serial, file);
            if (!result.IsSuccess)
                throw new InvalidOperationException(result.BestMessage);
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = true });
        }, "Делаю снимок…", "Скриншот сохранён ✓");
    }

    private async void InstallMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = $"Установить APK на {_device.DisplayName}",
            Filter = "Android package (*.apk)|*.apk",
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != true)
            return;

        await RunMenuActionAsync(async () =>
        {
            for (var index = 0; index < dialog.FileNames.Length; index++)
            {
                SetActionStatus($"Установка {index + 1}/{dialog.FileNames.Length}…");
                var result = await _adb.InstallApkAsync(_device.Serial, dialog.FileNames[index]);
                if (!result.IsSuccess)
                    throw new InvalidOperationException(result.BestMessage);
            }
        }, "Устанавливаю APK…", "Приложение установлено ✓");
    }

    private void ShellMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _adb.StartShell(_device.Serial);
            SetActionStatus("ADB shell открыт");
        }
        catch (Exception ex) { SetActionStatus(ex.Message, true); }
    }

    private async void ClipboardMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!System.Windows.Clipboard.ContainsText())
        {
            SetActionStatus("В буфере нет текста", true);
            return;
        }
        var text = System.Windows.Clipboard.GetText();
        if (text.Length > 1000)
            text = text[..1000];
        await RunMenuActionAsync(async () =>
        {
            var result = await _adb.SendTextAsync(_device.Serial, text);
            if (!result.IsSuccess)
                throw new InvalidOperationException(result.BestMessage);
        }, "Отправляю текст…", "Текст отправлен ✓");
    }

    private async void PowerMenuItem_Click(object sender, RoutedEventArgs e)
    {
        await RunMenuActionAsync(async () =>
        {
            var result = await _adb.TogglePowerAsync(_device.Serial);
            if (!result.IsSuccess)
                throw new InvalidOperationException(result.BestMessage);
        }, "Отправляю команду…", "Команда экрана отправлена ✓");
    }

    private void MtpMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("explorer.exe", "shell:MyComputerFolder") { UseShellExecute = true });
        SetActionStatus("Открыт «Этот компьютер»");
    }

    private void SettingsMenuItem_Click(object sender, RoutedEventArgs e) =>
        ((App)System.Windows.Application.Current).ShowSettings();

    private async Task RunMenuActionAsync(Func<Task> action, string progress, string success)
    {
        if (_actionRunning)
            return;
        _actionRunning = true;
        SetActionStatus(progress);
        try
        {
            await action();
            SetActionStatus(success);
        }
        catch (Exception ex)
        {
            SetActionStatus(ex.Message, true);
        }
        finally
        {
            _actionRunning = false;
        }
    }

    private void SetActionStatus(string message, bool error = false)
    {
        DetailText.Text = message;
        DetailText.Foreground = error
            ? new SolidColorBrush(Color.FromRgb(240, 68, 68))
            : (Brush)FindResource("TextSecondary");
    }

    private void Card_DragEnter(object sender, DragEventArgs e)
    {
        var valid = e.Data.GetDataPresent(DataFormats.FileDrop) && _device.State == DeviceConnectionState.Online;
        e.Effects = valid ? DragDropEffects.Copy : DragDropEffects.None;
        DropOverlay.Visibility = valid ? Visibility.Visible : Visibility.Collapsed;
        e.Handled = true;
    }

    private void Card_DragLeave(object sender, DragEventArgs e) => DropOverlay.Visibility = Visibility.Collapsed;

    private async void Card_Drop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        if (_transferring || _device.State != DeviceConnectionState.Online ||
            !e.Data.GetDataPresent(DataFormats.FileDrop))
            return;

        var paths = ((string[]?)e.Data.GetData(DataFormats.FileDrop) ?? Array.Empty<string>())
            .Where(path => File.Exists(path) || Directory.Exists(path)).ToArray();
        if (paths.Length == 0)
            return;

        _transferring = true;
        try
        {
            for (var index = 0; index < paths.Length; index++)
            {
                var path = paths[index];
                var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
                DetailText.Text = $"Передаю {name}…";
                var isApk = File.Exists(path) && Path.GetExtension(path).Equals(".apk", StringComparison.OrdinalIgnoreCase);
                var result = isApk
                    ? await _adb.InstallApkAsync(_device.Serial, path)
                    : await _adb.PushFileAsync(_device.Serial, path);
                if (!result.IsSuccess)
                    throw new InvalidOperationException(result.BestMessage);
            }
            DetailText.Text = paths.Length == 1 ? "Готово ✓" : $"Передано: {paths.Length} ✓";
        }
        catch (Exception ex)
        {
            DetailText.Text = ex.Message;
            DetailText.Foreground = new SolidColorBrush(Color.FromRgb(240, 68, 68));
        }
        finally
        {
            _transferring = false;
        }
        e.Handled = true;
    }

    private static string ShortSerial(string serial) => serial.Length <= 8 ? serial : $"…{serial[^6..]}";

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var result = string.Concat(value.Select(character => invalid.Contains(character) ? '_' : character));
        return string.IsNullOrWhiteSpace(result) ? "Android" : result;
    }
}
