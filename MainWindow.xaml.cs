using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AndroidWidget.Models;
using AndroidWidget.Services;
using Microsoft.Win32;

namespace AndroidWidget;

public partial class MainWindow : Window
{
    public event EventHandler<IReadOnlyList<AndroidDevice>>? DevicesUpdated;
    private const double CompactWidth = 258;
    private const double CompactHeight = 392;
    private const double ExpandedWidth = 588;
    private const double MiniWidth = 88;
    private const double MiniHeight = 110;
    private readonly AdbService _adb = new();
    private readonly DispatcherTimer _refreshTimer;
    private readonly CancellationTokenSource _lifetime = new();
    private IReadOnlyList<AndroidDevice> _devices = Array.Empty<AndroidDevice>();
    private AndroidDevice? _activeDevice;
    private bool _refreshing;
    private bool _menuOpen;
    private bool _operationInProgress;
    private bool _ignoreComboChange;
    private bool _isMini;
    private Point _miniMouseDownPoint;
    private bool _miniDragStarted;

    public MainWindow()
    {
        AppLog.Write("MainWindow constructor begin");
        InitializeComponent();
        AppLog.Write("MainWindow XAML initialized");
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _refreshTimer.Tick += async (_, _) => await RefreshDevicesAsync();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        AppLog.Write("MainWindow loaded");
        RestoreSettings();
        await RefreshDevicesAsync();
        _refreshTimer.Start();
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (System.Windows.Application.Current is App app && !app.IsExiting)
        {
            e.Cancel = true;
            app.HideToTray();
            return;
        }
        _refreshTimer.Stop();
        _lifetime.Cancel();
        SaveSettings();
    }

    private async Task RefreshDevicesAsync(bool force = false)
    {
        if (_refreshing || (_operationInProgress && !force))
            return;

        _refreshing = true;
        var previousSerial = _activeDevice?.Serial;
        try
        {
            var devices = await _adb.GetDevicesAsync(_lifetime.Token);
            _devices = devices;
            var selected = devices.FirstOrDefault(device => device.Serial == previousSerial)
                           ?? devices.FirstOrDefault(device => device.State == DeviceConnectionState.Online)
                           ?? devices.FirstOrDefault();
            UpdateDevicePicker(selected);
            SetActiveDevice(selected);
            DevicesUpdated?.Invoke(this, devices);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _devices = Array.Empty<AndroidDevice>();
            UpdateDevicePicker(null);
            SetActiveDevice(null, ex.Message);
            DevicesUpdated?.Invoke(this, _devices);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void UpdateDevicePicker(AndroidDevice? selected)
    {
        _ignoreComboChange = true;
        DevicesCombo.ItemsSource = _devices;
        DevicesCombo.SelectedItem = selected;
        DevicesCombo.IsEnabled = false;
        DevicesCombo.ToolTip = _devices.Count > 1
            ? "Для переключения откройте мини-карточку другого устройства"
            : null;
        _ignoreComboChange = false;
    }

    private void SetActiveDevice(AndroidDevice? device, string? error = null)
    {
        _activeDevice = device;
        MiniStatusDot.Fill = new SolidColorBrush(device?.State switch
        {
            DeviceConnectionState.Online => Color.FromRgb(114, 216, 162),
            DeviceConnectionState.Unauthorized => Color.FromRgb(255, 190, 92),
            DeviceConnectionState.Offline => Color.FromRgb(255, 105, 105),
            _ => Color.FromRgb(105, 115, 142)
        });
        if (device is null)
        {
            PowerStateOverlay.Visibility = Visibility.Collapsed;
            AuthorizationOverlay.Visibility = Visibility.Collapsed;
            DeviceNameText.Text = "Устройство не найдено";
            ConnectionText.Text = "Подключите USB и разрешите отладку";
            BatteryText.Text = "—";
            BatteryBar.Value = 0;
            DropHintText.Text = "Ожидаю Android по ADB";
            StatusText.Text = error ?? "Проверьте USB debugging или Wi-Fi ADB";
            PanelStatusText.Text = error ?? "Нет подключённых устройств";
            return;
        }

        ApplyDeviceSkin(device);
        var authorizationRequired = device.State == DeviceConnectionState.Unauthorized;
        AuthorizationOverlay.Visibility = authorizationRequired ? Visibility.Visible : Visibility.Collapsed;
        if (authorizationRequired)
            PhoneShell.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 184, 77));

        DeviceNameText.Text = device.DisplayName;
        ConnectionText.Text = $"{device.ConnectionLabel}  ·  Android {device.AndroidVersion}";
        BatteryText.Text = device.BatteryPercent is int battery ? $"{battery}%" : "—";
        BatteryBar.Value = device.BatteryPercent ?? 0;
        BatteryBar.Foreground = new SolidColorBrush(device.BatteryPercent switch
        {
            < 20 => Color.FromRgb(255, 105, 105),
            < 45 => Color.FromRgb(255, 190, 92),
            _ => Color.FromRgb(114, 216, 162)
        });
        var sleepingOrLocked = device.State == DeviceConnectionState.Online && (!device.IsScreenOn || device.IsLocked);
        PowerStateOverlay.Visibility = sleepingOrLocked ? Visibility.Visible : Visibility.Collapsed;
        MainLockIcon.Visibility = sleepingOrLocked && device.IsLocked ? Visibility.Visible : Visibility.Collapsed;
        MainPowerIcon.Visibility = sleepingOrLocked && !device.IsLocked ? Visibility.Visible : Visibility.Collapsed;
        PowerStateText.Text = device.IsLocked ? "Телефон заблокирован" : "Экран выключен";

        switch (device.State)
        {
            case DeviceConnectionState.Online:
                DropHintText.Text = "Перетащите файл или APK";
                StatusText.Text = _operationInProgress ? StatusText.Text : "Нажмите, чтобы открыть действия";
                PanelStatusText.Text = _operationInProgress ? PanelStatusText.Text : $"Подключено: {device.Serial}";
                break;
            case DeviceConnectionState.Unauthorized:
                DropHintText.Text = "Подтвердите RSA-ключ на телефоне";
                StatusText.Text = "ADB ожидает разрешение отладки";
                PanelStatusText.Text = "Устройство не авторизовано";
                break;
            default:
                DropHintText.Text = "Устройство недоступно";
                StatusText.Text = "Переподключите кабель или Wi-Fi ADB";
                PanelStatusText.Text = "ADB: устройство offline";
                break;
        }
    }

    private AndroidDevice? RequireOnlineDevice()
    {
        if (_activeDevice?.State == DeviceConnectionState.Online)
            return _activeDevice;

        SetOperationStatus("Сначала подключите и авторизуйте Android-устройство", true);
        return null;
    }

    private void ApplyDeviceSkin(AndroidDevice device)
    {
        var skin = PhoneSkinResolver.Resolve(device);
        var accent = new SolidColorBrush(skin.Accent);
        PhoneShell.CornerRadius = new CornerRadius(skin.ShellRadius);
        PhoneShell.Background = new SolidColorBrush(skin.Body);
        PhoneShell.BorderBrush = accent;
        PhoneBezelGrid.Margin = skin.Bezel;
        PhoneScreen.CornerRadius = new CornerRadius(skin.ScreenRadius);
        SkinDeviceBadge.BorderBrush = accent;
        SkinAndroidFace.Fill = accent;
        SkinAntennaLeft.Fill = accent;
        SkinAntennaRight.Fill = accent;
        SkinPowerButton.Background = accent;
        SkinVolumeButton.Background = accent;
        SkinPowerButton.Visibility = skin.HasSideButtons ? Visibility.Visible : Visibility.Collapsed;
        SkinVolumeButton.Visibility = skin.HasSideButtons ? Visibility.Visible : Visibility.Collapsed;
        ApplyCameraCutout(skin.Camera);
        PhoneShell.ToolTip = $"Скин: {skin.Family}\n{device.Manufacturer} {device.Model}";
    }

    private void ApplyCameraCutout(CameraCutout camera)
    {
        SkinCameraCutout.Visibility = camera == CameraCutout.None ? Visibility.Collapsed : Visibility.Visible;
        SkinCameraCutout.Width = camera == CameraCutout.Pill ? 22 : 7;
        SkinCameraCutout.Height = 7;
        SkinCameraCutout.CornerRadius = new CornerRadius(4);
        SkinCameraCutout.HorizontalAlignment = camera == CameraCutout.LeftPunch
            ? HorizontalAlignment.Left
            : HorizontalAlignment.Center;
        SkinCameraCutout.Margin = camera == CameraCutout.LeftPunch
            ? new Thickness(28, 6, 0, 0)
            : new Thickness(0, 6, 0, 0);
    }

    private void Phone_DragEnter(object sender, DragEventArgs e)
    {
        var valid = e.Data.GetDataPresent(DataFormats.FileDrop) && _activeDevice?.State == DeviceConnectionState.Online;
        e.Effects = valid ? DragDropEffects.Copy : DragDropEffects.None;
        DropOverlay.Visibility = valid ? Visibility.Visible : Visibility.Collapsed;
        e.Handled = true;
    }

    private void Phone_DragLeave(object sender, DragEventArgs e) => DropOverlay.Visibility = Visibility.Collapsed;

    private void MiniPhone_DragEnter(object sender, DragEventArgs e)
    {
        var valid = e.Data.GetDataPresent(DataFormats.FileDrop) && _activeDevice?.State == DeviceConnectionState.Online;
        e.Effects = valid ? DragDropEffects.Copy : DragDropEffects.None;
        MiniDropArrow.Visibility = valid ? Visibility.Visible : Visibility.Collapsed;
        MiniScreen.Opacity = valid ? 0.35 : 1;
        e.Handled = true;
    }

    private void MiniPhone_DragLeave(object sender, DragEventArgs e) => ResetMiniDropVisual();

    private void MiniPhone_Drop(object sender, DragEventArgs e)
    {
        ResetMiniDropVisual();
        Phone_Drop(sender, e);
    }

    private void ResetMiniDropVisual()
    {
        MiniDropArrow.Visibility = Visibility.Collapsed;
        MiniScreen.Opacity = 1;
    }

    private async void Phone_Drop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        var device = RequireOnlineDevice();
        if (device is null || !e.Data.GetDataPresent(DataFormats.FileDrop))
            return;

        var paths = (string[]?)e.Data.GetData(DataFormats.FileDrop) ?? Array.Empty<string>();
        paths = paths.Where(path => File.Exists(path) || Directory.Exists(path)).ToArray();
        if (paths.Length == 0)
            return;

        await RunOperationAsync(async token =>
        {
            for (var index = 0; index < paths.Length; index++)
            {
                var path = paths[index];
                var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
                var isApk = File.Exists(path) && Path.GetExtension(path).Equals(".apk", StringComparison.OrdinalIgnoreCase);
                SetOperationStatus(isApk
                    ? $"Устанавливаю {name} ({index + 1}/{paths.Length})…"
                    : $"Копирую {name} ({index + 1}/{paths.Length})…");

                var result = isApk
                    ? await _adb.InstallApkAsync(device.Serial, path, token)
                    : await _adb.PushFileAsync(device.Serial, path, token);
                if (!result.IsSuccess)
                    throw new InvalidOperationException($"{name}: {result.BestMessage}");
            }

            SetOperationStatus(paths.Length == 1 ? "Готово ✓" : $"Готово: {paths.Length} объектов ✓");
        });
    }

    private void DragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not Button)
            DragMove();
    }

    private void PhoneScreen_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is Button)
            return;
        ToggleActionPanel();
    }

    private void MiniModeButton_Click(object sender, RoutedEventArgs e)
    {
        SaveSettings();
        ((App)System.Windows.Application.Current).EnterMiniMode();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e) =>
        ((App)System.Windows.Application.Current).ShowSettings();

    private void MiniPhone_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _miniMouseDownPoint = e.GetPosition(this);
        _miniDragStarted = false;
        MiniPhone.CaptureMouse();
        e.Handled = true;
    }

    private void MiniPhone_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || !MiniPhone.IsMouseCaptured || _miniDragStarted)
            return;

        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _miniMouseDownPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _miniMouseDownPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        _miniDragStarted = true;
        MiniPhone.ReleaseMouseCapture();
        DragMove();
        SaveSettings();
        e.Handled = true;
    }

    private void MiniPhone_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (MiniPhone.IsMouseCaptured)
            MiniPhone.ReleaseMouseCapture();
        if (!_miniDragStarted)
            SetMiniMode(false);
        e.Handled = true;
    }

    private void SetMiniMode(bool mini, bool save = true)
    {
        _isMini = mini;
        _menuOpen = false;
        ActionPanel.Visibility = Visibility.Collapsed;
        PhoneShell.Visibility = mini ? Visibility.Collapsed : Visibility.Visible;
        MiniPhone.Visibility = mini ? Visibility.Visible : Visibility.Collapsed;
        Width = mini ? MiniWidth : CompactWidth;
        Height = mini ? MiniHeight : CompactHeight;
        KeepWindowOnScreen();
        if (save)
            SaveSettings();
    }

    private void ToggleActionPanel(bool? open = null)
    {
        if (_isMini)
            SetMiniMode(false, false);
        _menuOpen = open ?? !_menuOpen;
        ActionPanel.Visibility = _menuOpen ? Visibility.Visible : Visibility.Collapsed;
        Width = _menuOpen ? ExpandedWidth : CompactWidth;
        KeepWindowOnScreen();
    }

    private void CollapsePanel_Click(object sender, RoutedEventArgs e) => ToggleActionPanel(false);
    private void CloseButton_Click(object sender, RoutedEventArgs e) =>
        ((App)System.Windows.Application.Current).HideToTray();

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        SettingsService.Update(settings => settings with { Topmost = Topmost });
        PinButton.Foreground = new SolidColorBrush(Topmost ? Color.FromRgb(138, 115, 255) : Color.FromRgb(120, 132, 163));
        SetOperationStatus(Topmost ? "Виджет закреплён поверх окон" : "Режим «поверх окон» выключен");
    }

    private void DevicesCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ignoreComboChange && DevicesCombo.SelectedItem is AndroidDevice selected)
            SetActiveDevice(selected);
    }

    public void SelectDevice(string serial)
    {
        var selected = _devices.FirstOrDefault(device => device.Serial == serial);
        if (selected is null)
            return;
        UpdateDevicePicker(selected);
        SetActiveDevice(selected);
    }

    private void ScreenButton_Click(object sender, RoutedEventArgs e)
    {
        var device = RequireOnlineDevice();
        if (device is null)
            return;
        if (_adb.TryStartScrcpy(device.Serial, out var error))
            SetOperationStatus("scrcpy запущен ✓");
        else
            SetOperationStatus(error ?? "Не удалось запустить scrcpy", true);
    }

    private void FilesButton_Click(object sender, RoutedEventArgs e)
    {
        var device = RequireOnlineDevice();
        if (device is null)
            return;
        new RemoteFilesWindow(_adb, device) { Owner = this }.Show();
        SetOperationStatus("Открыт ADB-браузер файлов");
    }

    private async void ScreenshotButton_Click(object sender, RoutedEventArgs e)
    {
        var device = RequireOnlineDevice();
        if (device is null)
            return;

        var defaultFolder = SettingsService.Current.ScreenshotFolder;
        if (string.IsNullOrWhiteSpace(defaultFolder) || !Directory.Exists(defaultFolder))
            defaultFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

        var dialog = new SaveFileDialog
        {
            Title = "Сохранить снимок экрана Android",
            Filter = "PNG-изображение (*.png)|*.png",
            DefaultExt = ".png",
            AddExtension = true,
            OverwritePrompt = true,
            InitialDirectory = defaultFolder,
            FileName = $"Screenshot_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png"
        };
        if (dialog.ShowDialog(this) != true)
        {
            SetOperationStatus("Сохранение скриншота отменено");
            return;
        }

        var file = dialog.FileName;
        SaveSettings(Path.GetDirectoryName(file));
        await RunOperationAsync(async token =>
        {
            SetOperationStatus("Делаю снимок экрана…");
            var result = await _adb.TakeScreenshotAsync(device.Serial, file, token);
            if (!result.IsSuccess)
                throw new InvalidOperationException(result.BestMessage);
            SetOperationStatus($"Сохранено: {Path.GetFileName(file)} ✓");
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = true });
        });
    }

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        var device = RequireOnlineDevice();
        if (device is null)
            return;

        var dialog = new OpenFileDialog { Filter = "Android package (*.apk)|*.apk", Multiselect = true };
        if (dialog.ShowDialog(this) != true)
            return;
        await InstallApksAsync(device, dialog.FileNames);
    }

    private async Task InstallApksAsync(AndroidDevice device, IReadOnlyList<string> paths)
    {
        await RunOperationAsync(async token =>
        {
            for (var i = 0; i < paths.Count; i++)
            {
                SetOperationStatus($"Устанавливаю {Path.GetFileName(paths[i])} ({i + 1}/{paths.Count})…");
                var result = await _adb.InstallApkAsync(device.Serial, paths[i], token);
                if (!result.IsSuccess)
                    throw new InvalidOperationException(result.BestMessage);
            }
            SetOperationStatus("Приложение установлено ✓");
        });
    }

    private void ShellButton_Click(object sender, RoutedEventArgs e)
    {
        var device = RequireOnlineDevice();
        if (device is null)
            return;
        try
        {
            _adb.StartShell(device.Serial);
            SetOperationStatus("ADB shell открыт");
        }
        catch (Exception ex) { SetOperationStatus(ex.Message, true); }
    }

    private async void ClipboardButton_Click(object sender, RoutedEventArgs e)
    {
        var device = RequireOnlineDevice();
        if (device is null)
            return;
        if (!Clipboard.ContainsText())
        {
            SetOperationStatus("В буфере обмена нет текста", true);
            return;
        }

        var text = Clipboard.GetText();
        if (text.Length > 1000)
            text = text[..1000];
        await RunOperationAsync(async token =>
        {
            SetOperationStatus("Отправляю текст в активное поле телефона…");
            var result = await _adb.SendTextAsync(device.Serial, text, token);
            if (!result.IsSuccess)
                throw new InvalidOperationException(result.BestMessage);
            SetOperationStatus("Текст отправлен ✓");
        });
    }

    private async void PowerButton_Click(object sender, RoutedEventArgs e)
    {
        var device = RequireOnlineDevice();
        if (device is null)
            return;
        await RunOperationAsync(async token =>
        {
            var result = await _adb.TogglePowerAsync(device.Serial, token);
            if (!result.IsSuccess)
                throw new InvalidOperationException(result.BestMessage);
            SetOperationStatus("Команда экрана отправлена ✓");
        });
    }

    private void MtpButton_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("explorer.exe", "shell:MyComputerFolder") { UseShellExecute = true });
        SetOperationStatus("Открыт «Этот компьютер» — выберите Android-устройство");
    }

    private async Task RunOperationAsync(Func<CancellationToken, Task> operation)
    {
        if (_operationInProgress)
        {
            SetOperationStatus("Дождитесь завершения текущей операции", true);
            return;
        }

        _operationInProgress = true;
        try
        {
            await operation(_lifetime.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            SetOperationStatus(ex.Message, true);
        }
        finally
        {
            _operationInProgress = false;
        }
    }

    private void SetOperationStatus(string message, bool isError = false)
    {
        StatusText.Text = message;
        PanelStatusText.Text = message;
        var color = isError ? Color.FromRgb(255, 126, 126) : Color.FromRgb(132, 145, 178);
        StatusText.Foreground = new SolidColorBrush(color);
        PanelStatusText.Foreground = new SolidColorBrush(color);
    }

    private void KeepWindowOnScreen()
    {
        var workArea = SystemParameters.WorkArea;
        if (Left + Width > workArea.Right)
            Left = Math.Max(workArea.Left, workArea.Right - Width);
        if (Top + Height > workArea.Bottom)
            Top = Math.Max(workArea.Top, workArea.Bottom - Height);
    }

    private void RestoreSettings()
    {
        var settings = SettingsService.Current;
        Topmost = settings.Topmost;
        PinButton.Foreground = new SolidColorBrush(Topmost ? Color.FromRgb(138, 115, 255) : Color.FromRgb(120, 132, 163));
        var workArea = SystemParameters.WorkArea;
        Left = settings.Left is double left && left >= workArea.Left && left < workArea.Right - 80
            ? left : workArea.Right - CompactWidth - 30;
        Top = settings.Top is double top && top >= workArea.Top && top < workArea.Bottom - 80
            ? top : workArea.Bottom - Height - 40;
    }

    private void SaveSettings(string? screenshotFolder = null)
    {
        SettingsService.Update(settings => settings with
        {
            Left = Left,
            Top = Top,
            Topmost = Topmost,
            ScreenshotFolder = screenshotFolder ?? settings.ScreenshotFolder
        });
    }
}
