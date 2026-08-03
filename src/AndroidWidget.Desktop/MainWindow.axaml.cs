using System.Diagnostics;
using AndroidWidget.CompanionHost;
using AndroidWidget.Core;
using AndroidWidget.Protocol;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;

namespace AndroidWidget.Desktop;

public sealed partial class MainWindow : Window
{
    private const double PhoneWindowWidth = 258;
    private const double PhoneWindowHeight = 392;
    private const double DrawerWindowWidth = 584;
    private const double DrawerWindowHeight = 508;
    private readonly CompanionHostService _host;
    private readonly PortableAdbService _adb = new();
    private readonly DesktopSettingsStore _settings = new();
    private readonly List<AdbDeviceChoice> _adbDevices = [];
    private readonly DispatcherTimer _adbRefreshTimer;
    private CancellationTokenSource? _adbOperation;
    private string? _recordingPath;
    private bool _drawerOpen;
    private bool _drawerOnLeft;
    private bool _miniMode;
    private PixelPoint? _miniReturnPosition;
    private int _reportedDeviceCount = -1;
    private AdbDeviceChoice? _activeDevice;

    internal event EventHandler<int>? DeviceCountChanged;
    internal event EventHandler? HideRequested;

    public MainWindow()
    {
        InitializeComponent();
        ApplySettings();
        _settings.Changed += (_, _) => ApplySettings();
        ProductVersionText.Text = ProductVersion.ProductLabel;
        var dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AndroidWidget", "companion-v1");
        _host = new CompanionHostService(new CompanionHostOptions(dataDirectory));
        _host.DeviceChanged += HandleCompanionDeviceChanged;
        _host.NotificationReceived += HandleNotification;
        _adbRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _adbRefreshTimer.Tick += async (_, _) => await RefreshAdbAsync();
        Opened += async (_, _) =>
        {
            await StartHostAsync();
            await RefreshAdbAsync();
            _adbRefreshTimer.Start();
        };
        Closed += async (_, _) =>
        {
            _adbRefreshTimer.Stop();
            _adbOperation?.Cancel();
            await _host.DisposeAsync();
        };
    }

    private async Task StartHostAsync()
    {
        try
        {
            await _host.StartAsync();
            HostStatusText.Text = $"Защищённый host · {ProtocolConstants.DefaultPort}";
        }
        catch (Exception ex)
        {
            HostStatusText.Text = $"Ошибка host: {ex.Message}";
            HostStatusText.Foreground = Brushes.IndianRed;
        }
    }

    private async Task RefreshAdbAsync()
    {
        var selectedSerial = _activeDevice?.Serial;
        try
        {
            var discovered = await _adb.GetDevicesAsync(CancellationToken.None);
            _adbDevices.Clear();
            foreach (var device in discovered)
                _adbDevices.Add(AdbDeviceChoice.From(device));
            _activeDevice = _adbDevices.FirstOrDefault(device => device.Serial == selectedSerial)
                            ?? _adbDevices.FirstOrDefault();
            ApplySelectedDevice();
            if (_adbDevices.Count == 0)
                SetStatus("Устройство не найдено · подключите USB или Wi-Fi ADB");
            ReportDeviceCount();
        }
        catch (Exception ex)
        {
            _adbDevices.Clear();
            _activeDevice = null;
            ApplyDevice(null);
            SetStatus($"ADB: {FriendlyToolError(ex.Message, "adb")}", true);
            ReportDeviceCount();
        }
    }

    private void ReportDeviceCount()
    {
        if (_reportedDeviceCount == _adbDevices.Count)
            return;
        _reportedDeviceCount = _adbDevices.Count;
        DeviceCountChanged?.Invoke(this, _reportedDeviceCount);
    }

    private void ApplySelectedDevice() => ApplyDevice(_activeDevice);

    private void ApplyDevice(AdbDeviceChoice? device)
    {
        if (device is null)
        {
            DeviceNameText.Text = "Устройство не найдено";
            ConnectionText.Text = "Подключите USB и разрешите отладку";
            BatteryText.Text = "—";
            BatteryBar.Value = 0;
            BatteryPanel.IsVisible = false;
            DropHintText.Text = "Ожидаю Android по ADB";
            MiniDeviceNameText.Text = "Android";
            MiniConnectionText.Text = "ADB не подключён";
            MiniBatteryText.Text = "—";
            MiniDetailText.Text = "Ожидаю устройство";
            MiniStatusDot.Fill = new SolidColorBrush(Color.FromRgb(105, 115, 142));
            return;
        }

        DeviceNameText.Text = device.Name;
        ConnectionText.Text = $"{(device.Wireless ? "Wi-Fi" : "USB")} / ADB" +
                              (string.IsNullOrWhiteSpace(device.AndroidVersion)
                                  ? string.Empty
                                  : $" · Android {device.AndroidVersion}");
        BatteryText.Text = device.BatteryPercent is int battery ? $"{battery}%" : "—";
        BatteryPanel.IsVisible = device.BatteryPercent is not null;
        BatteryBar.Value = device.BatteryPercent ?? 0;
        BatteryBar.Foreground = new SolidColorBrush(device.BatteryPercent switch
        {
            < 20 => Color.FromRgb(255, 105, 105),
            < 45 => Color.FromRgb(255, 190, 92),
            _ => Color.FromRgb(114, 216, 162)
        });
        DropHintText.Text = "Отправить файл или APK";
        MiniDeviceNameText.Text = device.Name;
        MiniConnectionText.Text = device.Wireless ? "Wi-Fi / ADB" : "USB / ADB";
        MiniBatteryText.Text = BatteryText.Text;
        MiniDetailText.Text = "Готов к работе";
        MiniStatusDot.Fill = new SolidColorBrush(Color.FromRgb(78, 205, 132));
        SetStatus($"Подключено: {device.Serial}");
    }

    private void ToggleDrawer(bool? open = null)
    {
        if (_miniMode)
            return;
        var shouldOpen = open ?? !_drawerOpen;
        if (shouldOpen == _drawerOpen)
            return;

        if (shouldOpen)
        {
            var placement = GetExpansionPlacement(DrawerWindowWidth, DrawerWindowHeight);
            _drawerOnLeft = placement.OpensLeft;
            Grid.SetColumn(PhoneShell, _drawerOnLeft ? 1 : 0);
            Grid.SetColumn(ActionDrawer, _drawerOnLeft ? 0 : 1);
            _drawerOpen = true;
            ActionDrawer.IsVisible = true;
            SetWindowSize(DrawerWindowWidth, DrawerWindowHeight);
            Position = ClampToWorkingArea(placement.Position, DrawerWindowWidth, DrawerWindowHeight);
            return;
        }

        var phonePosition = GetCurrentPhonePosition();
        _drawerOpen = false;
        ActionDrawer.IsVisible = false;
        Grid.SetColumn(PhoneShell, 0);
        Grid.SetColumn(ActionDrawer, 1);
        SetWindowSize(PhoneWindowWidth, PhoneWindowHeight);
        Position = ClampToWorkingArea(phonePosition, PhoneWindowWidth, PhoneWindowHeight);
    }

    private void DragHandle_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
            e.Handled = true;
        }
    }

    private void PhoneShell_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
            e.Handled = true;
        }
    }

    private void PhoneScreen_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            ToggleDrawer();
            e.Handled = true;
        }
    }

    private void MiniContent_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        if (e.ClickCount >= 2)
            SetMiniMode(false);
        else
            BeginMoveDrag(e);
        e.Handled = true;
    }

    private void MiniModeButton_Click(object? sender, RoutedEventArgs e) => SetMiniMode(true);

    private void RestoreFromMiniMenu_Click(object? sender, RoutedEventArgs e) => SetMiniMode(false);

    private void OpenActionsFromMiniMenu_Click(object? sender, RoutedEventArgs e)
    {
        SetMiniMode(false);
        ToggleDrawer(true);
    }

    private void SetMiniMode(bool mini)
    {
        if (mini == _miniMode)
            return;

        PixelPoint targetPosition;
        if (mini)
        {
            targetPosition = _miniReturnPosition ?? GetCurrentPhonePosition();
        }
        else
        {
            _miniReturnPosition = Position;
            targetPosition = GetExpansionPlacement(PhoneWindowWidth, PhoneWindowHeight).Position;
        }

        _miniMode = mini;
        _drawerOpen = false;
        ActionDrawer.IsVisible = false;
        Grid.SetColumn(PhoneShell, 0);
        Grid.SetColumn(ActionDrawer, 1);
        FullPhoneContent.IsVisible = !mini;
        MiniContent.IsVisible = mini;
        RootLayout.Margin = mini ? new Thickness(0) : new Thickness(4);
        PhoneShell.Margin = mini ? new Thickness(3) : new Thickness(4);
        PhoneShell.CornerRadius = mini ? new CornerRadius(24) : new CornerRadius(39);
        PhoneShell.Width = mini ? 114 : 242;
        PhoneShell.Height = mini ? 182 : 376;
        SetWindowSize(mini ? 120 : PhoneWindowWidth, mini ? 188 : PhoneWindowHeight);
        Position = ClampToWorkingArea(
            targetPosition,
            mini ? 120 : PhoneWindowWidth,
            mini ? 188 : PhoneWindowHeight);
    }

    private void SetWindowSize(double width, double height)
    {
        MinWidth = 0;
        MinHeight = 0;
        MaxWidth = double.PositiveInfinity;
        MaxHeight = double.PositiveInfinity;
        Width = width;
        Height = height;
        MinWidth = width;
        MinHeight = height;
    }

    private (PixelPoint Position, bool OpensLeft) GetExpansionPlacement(double targetWidth, double targetHeight)
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null)
            return (Position, false);

        var work = screen.WorkingArea;
        var scale = screen.Scaling;
        var targetWidthPixels = (int)Math.Ceiling(targetWidth * scale);
        var targetHeightPixels = (int)Math.Ceiling(targetHeight * scale);
        var currentWidthPixels = (int)Math.Ceiling(Width * scale);
        var currentHeightPixels = (int)Math.Ceiling(Height * scale);
        var current = Position;

        var rightFits = current.X + targetWidthPixels <= work.Right;
        var leftX = current.X + currentWidthPixels - targetWidthPixels;
        var leftFits = leftX >= work.X;
        var roomRight = work.Right - current.X;
        var roomLeft = current.X + currentWidthPixels - work.X;
        var opensLeft = !rightFits && (leftFits || roomLeft > roomRight);
        var x = opensLeft ? leftX : current.X;

        var y = current.Y;
        if (y + targetHeightPixels > work.Bottom)
            y = current.Y + currentHeightPixels - targetHeightPixels;

        return (ClampToWorkingArea(new PixelPoint(x, y), targetWidth, targetHeight, screen), opensLeft);
    }

    private PixelPoint GetCurrentPhonePosition()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null || !_drawerOpen || !_drawerOnLeft)
            return Position;

        var drawerPixels = (int)Math.Ceiling((DrawerWindowWidth - PhoneWindowWidth) * screen.Scaling);
        return new PixelPoint(Position.X + drawerPixels, Position.Y);
    }

    private PixelPoint ClampToWorkingArea(PixelPoint position, double width, double height) =>
        ClampToWorkingArea(position, width, height, Screens.ScreenFromPoint(position) ?? Screens.ScreenFromWindow(this) ?? Screens.Primary);

    private static PixelPoint ClampToWorkingArea(
        PixelPoint position, double width, double height, Avalonia.Platform.Screen? screen)
    {
        if (screen is null)
            return position;

        var work = screen.WorkingArea;
        var widthPixels = (int)Math.Ceiling(width * screen.Scaling);
        var heightPixels = (int)Math.Ceiling(height * screen.Scaling);
        var maxX = Math.Max(work.X, work.Right - widthPixels);
        var maxY = Math.Max(work.Y, work.Bottom - heightPixels);
        return new PixelPoint(
            Math.Clamp(position.X, work.X, maxX),
            Math.Clamp(position.Y, work.Y, maxY));
    }

    private void SettingsButton_Click(object? sender, RoutedEventArgs e) => OpenSettings();

    internal void OpenSettings(bool owned = true)
    {
        var window = new SettingsWindow(_settings);
        if (owned && IsVisible)
            window.ShowDialog(this);
        else
            window.Show();
        window.Activate();
    }

    internal Task RefreshDevicesAsync() => RefreshAdbAsync();

    private void ApplySettings()
    {
        Topmost = _settings.Current.Topmost;
        if (Application.Current is { } app)
            app.RequestedThemeVariant = _settings.Current.Theme == "Light"
                ? ThemeVariant.Light
                : ThemeVariant.Dark;
    }

    private void PinButton_Click(object? sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        _settings.Update(current => current with { Topmost = Topmost });
        PinButton.Foreground = new SolidColorBrush(Topmost
            ? Color.FromRgb(138, 115, 255)
            : Color.FromRgb(120, 132, 163));
        SetStatus(Topmost ? "Виджет закреплён поверх окон" : "Режим «поверх окон» выключен");
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        HideRequested?.Invoke(this, EventArgs.Empty);
        Hide();
    }
    private void CollapsePanel_Click(object? sender, RoutedEventArgs e) => ToggleDrawer(false);
    private async void RefreshButton_Click(object? sender, RoutedEventArgs e) => await RefreshAdbAsync();

    private void ScreenButton_Click(object? sender, RoutedEventArgs e)
    {
        if (SelectedAdbDevice() is not { } device)
            return;
        var result = _adb.StartScrcpy(device.Serial);
        SetStatus(result.IsSuccess ? "scrcpy запущен ✓" : FriendlyToolError(result.Message, "scrcpy"),
            !result.IsSuccess);
    }

    private void RecordButton_Click(object? sender, RoutedEventArgs e)
    {
        if (SelectedAdbDevice() is not { } device)
            return;
        if (_adb.IsRecording(device.Serial))
        {
            var stopped = _adb.StopRecording(device.Serial);
            RecordButtonText.Text = "Запись";
            SetStatus(stopped.IsSuccess && _recordingPath is not null
                ? $"Видео сохранено: {Path.GetFileName(_recordingPath)}"
                : stopped.Message, !stopped.IsSuccess);
            return;
        }
        var folder = _settings.Current.RecordingFolder;
        Directory.CreateDirectory(folder);
        _recordingPath = Path.Combine(folder, $"Android_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.mkv");
        var result = _adb.StartScrcpy(device.Serial, _recordingPath);
        if (result.IsSuccess)
            RecordButtonText.Text = "Остановить";
        SetStatus(result.IsSuccess ? "Идёт запись · нажмите ещё раз для остановки" : result.Message,
            !result.IsSuccess);
    }

    private async void FilesButton_Click(object? sender, RoutedEventArgs e)
    {
        if (SelectedAdbDevice() is not { } device)
            return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Отправить на Android",
            AllowMultiple = true
        });
        foreach (var file in files)
        {
            var path = file.Path.LocalPath;
            await RunAdbOperationAsync(token => _adb.PushAsync(device.Serial, path, token),
                result => result.IsSuccess ? $"Передано: {Path.GetFileName(path)} ✓" : result.Message);
        }
    }

    private void TransfersButton_Click(object? sender, RoutedEventArgs e) =>
        SetStatus("Передачи выполняются последовательно; прогресс отображается здесь");

    private async void ScreenshotButton_Click(object? sender, RoutedEventArgs e)
    {
        if (SelectedAdbDevice() is not { } device)
            return;
        var folder = _settings.Current.ScreenshotFolder;
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"Android_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png");
        await RunAdbOperationAsync(token => _adb.ScreenshotAsync(device.Serial, path, token), result =>
        {
            if (!result.IsSuccess)
                return result.Message;
            RevealPath(path);
            return $"Скриншот сохранён: {Path.GetFileName(path)} ✓";
        });
    }

    private async void InstallButton_Click(object? sender, RoutedEventArgs e)
    {
        if (SelectedAdbDevice() is not { } device)
            return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Установить APK",
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType("Android package") { Patterns = ["*.apk"] }]
        });
        foreach (var file in files)
        {
            var path = file.Path.LocalPath;
            await RunAdbOperationAsync(token => _adb.InstallAsync(device.Serial, path, token),
                result => result.IsSuccess ? $"Установлено: {Path.GetFileName(path)} ✓" : result.Message);
        }
    }

    private void ShellButton_Click(object? sender, RoutedEventArgs e)
    {
        if (SelectedAdbDevice() is not { } device)
            return;
        var result = _adb.StartShell(device.Serial);
        SetStatus(result.IsSuccess ? "ADB shell открыт" : result.Message, !result.IsSuccess);
    }

    private async void ClipboardButton_Click(object? sender, RoutedEventArgs e)
    {
        if (SelectedAdbDevice() is not { } device)
            return;
        var clipboard = GetTopLevel(this)?.Clipboard;
        var text = clipboard is null ? null : await clipboard.TryGetTextAsync();
        if (string.IsNullOrWhiteSpace(text))
        {
            SetStatus("В буфере обмена нет текста", true);
            return;
        }
        if (text.Length > 1000)
            text = text[..1000];
        await RunAdbOperationAsync(token => _adb.SendTextAsync(device.Serial, text, token),
            result => result.IsSuccess ? "Текст отправлен ✓" : result.Message);
    }

    private async void PowerButton_Click(object? sender, RoutedEventArgs e)
    {
        if (SelectedAdbDevice() is not { } device)
            return;
        await RunAdbOperationAsync(token => _adb.TogglePowerAsync(device.Serial, token),
            result => result.IsSuccess ? "Команда экрана отправлена ✓" : result.Message);
    }

    private void WirelessButton_Click(object? sender, RoutedEventArgs e)
    {
        CompanionPanel.IsVisible = false;
        WirelessPanel.IsVisible = !WirelessPanel.IsVisible;
    }

    private void CompanionButton_Click(object? sender, RoutedEventArgs e)
    {
        WirelessPanel.IsVisible = false;
        CompanionPanel.IsVisible = !CompanionPanel.IsVisible;
    }

    private async void WirelessPairButton_Click(object? sender, RoutedEventArgs e) =>
        await RunAdbOperationAsync(token => _adb.PairAsync(WirelessEndpointText.Text ?? string.Empty,
                WirelessCodeText.Text ?? string.Empty, token),
            result => result.IsSuccess ? "Wireless debugging сопряжён ✓" : result.Message);

    private async void WirelessConnectButton_Click(object? sender, RoutedEventArgs e)
    {
        await RunAdbOperationAsync(token => _adb.ConnectAsync(WirelessEndpointText.Text ?? string.Empty, token),
            result => result.IsSuccess ? "Wi-Fi ADB подключён ✓" : result.Message);
        await RefreshAdbAsync();
    }

    private void PairButton_Click(object? sender, RoutedEventArgs e)
    {
        var pairing = _host.CreatePairingSession();
        PairCodeText.Text = $"{pairing.Code[..3]} {pairing.Code[3..]}";
        PairingUriText.Text = pairing.Uri;
        PairingHintText.Text = $"Действует до {pairing.ExpiresAt.ToLocalTime():HH:mm:ss}";
    }

    private async void CopyPairingButton_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PairingUriText.Text))
            return;
        var clipboard = GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(PairingUriText.Text);
            PairingHintText.Text = "Ссылка скопирована";
        }
    }

    private AdbDeviceChoice? SelectedAdbDevice(bool showError = true)
    {
        if (_activeDevice is { } device)
            return device;
        if (showError)
            SetStatus("Сначала подключите и выберите Android-устройство", true);
        return null;
    }

    private async Task RunAdbOperationAsync(Func<CancellationToken, Task<PortableCommandResult>> operation,
        Func<PortableCommandResult, string> message)
    {
        _adbOperation?.Cancel();
        _adbOperation?.Dispose();
        _adbOperation = new CancellationTokenSource();
        try
        {
            SetStatus("Выполняется…");
            var result = await operation(_adbOperation.Token);
            SetStatus(message(result), !result.IsSuccess);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Операция отменена");
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, true);
        }
    }

    private void SetStatus(string message, bool error = false)
    {
        StatusText.Text = message;
        PanelStatusText.Text = message;
        var color = error ? Color.FromRgb(255, 120, 120) : Color.FromRgb(152, 160, 179);
        StatusText.Foreground = new SolidColorBrush(color);
        PanelStatusText.Foreground = new SolidColorBrush(color);
    }

    private void HandleCompanionDeviceChanged(object? sender, CompanionDeviceState state) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (!state.IsConnected || state.LatestNotification is not { } notification)
                return;
            ShowNotification($"{notification.Title}: {notification.Preview}".Trim(':', ' '));
        });

    private void HandleNotification(object? sender, CompanionNotification received) =>
        Dispatcher.UIThread.Post(() =>
            ShowNotification($"{received.Notification.Title}: {received.Notification.Preview}".Trim(':', ' ')));

    private void ShowNotification(string message)
    {
        if (!_settings.Current.ShowNotifications || string.IsNullOrWhiteSpace(message))
            return;
        LatestMessageText.Text = message;
        LatestMessageBorder.IsVisible = true;
        _ = HideNotificationLaterAsync(message);
    }

    private async Task HideNotificationLaterAsync(string message)
    {
        await Task.Delay(TimeSpan.FromSeconds(_settings.Current.NotificationDurationSeconds));
        Dispatcher.UIThread.Post(() =>
        {
            if (LatestMessageText.Text == message)
                LatestMessageBorder.IsVisible = false;
        });
    }

    private static void RevealPath(string path)
    {
        try
        {
            if (OperatingSystem.IsMacOS())
                Process.Start("open", ["-R", path]);
            else if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo("xdg-open", Path.GetDirectoryName(path)!) { UseShellExecute = false });
        }
        catch
        {
            // Saving succeeded; revealing the file is an optional desktop integration.
        }
    }

    private static string ResolveUserFolder(Environment.SpecialFolder folder, string child)
    {
        var root = Environment.GetFolderPath(folder);
        if (string.IsNullOrWhiteSpace(root))
            root = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(root, child);
    }

    private static string FriendlyToolError(string message, string tool) =>
        message.Contains("No such file", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("cannot find", StringComparison.OrdinalIgnoreCase)
            ? $"{tool} не найден в PATH"
            : message;

    private sealed record AdbDeviceChoice(string Serial, string Name, string Manufacturer,
        string AndroidVersion, int? BatteryPercent, bool Wireless)
    {
        public string Label => $"{Name} · {(Wireless ? "Wi-Fi" : "USB")} · {Serial}";

        public static AdbDeviceChoice From(PortableAdbDevice device) =>
            new(device.Serial, device.Name, device.Manufacturer, device.AndroidVersion,
                device.BatteryPercent, device.Wireless);

        public override string ToString() => Label;
    }
}
