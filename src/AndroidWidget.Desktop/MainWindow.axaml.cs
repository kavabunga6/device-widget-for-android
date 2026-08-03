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
    private readonly DesktopRuntime _runtime;
    private readonly CompanionHostService _host;
    private readonly PortableAdbService _adb;
    private readonly DesktopSettingsStore _settings;
    private readonly string _boundSerial;
    private CancellationTokenSource? _adbOperation;
    private readonly Dictionary<Guid, DesktopTransferState> _transferStates = [];
    private readonly List<NotificationBubble> _notificationBubbles = [];
    private readonly Dictionary<Guid, CancellationTokenSource> _notificationTimers = [];
    private string? _recordingPath;
    private bool _drawerOpen;
    private bool _drawerOnLeft;
    private bool _miniMode;
    private PixelPoint? _miniReturnPosition;
    private AdbDeviceChoice? _activeDevice;

    internal event EventHandler? HideRequested;

    public MainWindow() : this(new DesktopRuntime(),
        new PortableAdbDevice("design", "Android", string.Empty, string.Empty, null, false, "device", true, false))
    {
    }

    internal MainWindow(DesktopRuntime runtime, PortableAdbDevice device)
    {
        _runtime = runtime;
        _host = runtime.Host;
        _adb = runtime.Adb;
        _settings = runtime.Settings;
        _boundSerial = device.Serial;
        _activeDevice = AdbDeviceChoice.From(device);
        InitializeComponent();
        ApplySettings();
        _settings.Changed += Settings_Changed;
        ProductVersionText.Text = ProductVersion.ProductLabel;
        _host.DeviceChanged += HandleCompanionDeviceChanged;
        _host.NotificationReceived += HandleNotification;
        _runtime.Transfers.Changed += Transfers_Changed;
        _runtime.PhotoDetected += Runtime_PhotoDetected;
        Opened += (_, _) =>
        {
            HostStatusText.Text = runtime.HostError is null
                ? $"Защищённый host · {ProtocolConstants.DefaultPort}"
                : $"Ошибка host: {runtime.HostError}";
            ApplySelectedDevice();
        };
        Closed += (_, _) =>
        {
            _adbOperation?.Cancel();
            _settings.Changed -= Settings_Changed;
            _host.DeviceChanged -= HandleCompanionDeviceChanged;
            _host.NotificationReceived -= HandleNotification;
            _runtime.Transfers.Changed -= Transfers_Changed;
            _runtime.PhotoDetected -= Runtime_PhotoDetected;
            foreach (var timer in _notificationTimers.Values)
            {
                timer.Cancel();
                timer.Dispose();
            }
            _notificationTimers.Clear();
        };
    }

    private void Settings_Changed(object? sender, EventArgs e) => ApplySettings();

    private async Task RefreshAdbAsync()
    {
        await _runtime.RefreshAsync();
        if (_runtime.AdbError is not null)
            SetStatus($"ADB: {FriendlyToolError(_runtime.AdbError, "adb")}", true);
        if (_runtime.Devices.FirstOrDefault(device => device.Serial == _boundSerial) is { } current)
            UpdateDevice(current);
    }

    private void ApplySelectedDevice() => ApplyDevice(_activeDevice);

    internal void UpdateDevice(PortableAdbDevice device)
    {
        if (device.Serial != _boundSerial)
            return;
        _activeDevice = AdbDeviceChoice.From(device);
        ApplySelectedDevice();
    }

    internal void CloseForDisconnect() => Close();

    internal void CloseForExit() => Close();

    internal void PlaceInSlot(int index)
    {
        var screen = Screens.Primary;
        if (screen is null)
            return;
        var offset = (int)Math.Round(28 * screen.Scaling) * Math.Max(0, index);
        var width = (int)Math.Ceiling(PhoneWindowWidth * screen.Scaling);
        var height = (int)Math.Ceiling(PhoneWindowHeight * screen.Scaling);
        Position = new PixelPoint(
            Math.Max(screen.WorkingArea.X, screen.WorkingArea.Right - width - offset),
            Math.Max(screen.WorkingArea.Y, screen.WorkingArea.Bottom - height - offset));
    }

    private void ApplyDevice(AdbDeviceChoice? device)
    {
        if (device is null)
        {
            StateOverlayText.IsVisible = false;
            DeviceIconBorder.Background = new SolidColorBrush(Color.FromRgb(48, 43, 80));
            DeviceNameText.Text = "Устройство не найдено";
            ConnectionText.Text = "Подключите USB и разрешите отладку";
            BatteryText.Text = "—";
            BatteryPanel.IsVisible = false;
            DropHintText.Text = "Ожидаю Android по ADB";
            MiniDeviceNameText.Text = "Android";
            MiniConnectionText.Text = "ADB не подключён";
            MiniBatteryText.Text = "—";
            MiniBatteryText.IsVisible = true;
            MiniStateText.IsVisible = false;
            MiniDetailText.Text = "Ожидаю устройство";
            MiniStatusDot.Fill = new SolidColorBrush(Color.FromRgb(105, 115, 142));
            return;
        }

        DeviceNameText.Text = device.Name;
        DeviceIconBorder.Background = new SolidColorBrush(!device.Authorized || device.Locked || !device.ScreenOn
            ? Color.FromRgb(19, 21, 27)
            : Color.FromRgb(48, 43, 80));
        StateOverlayText.IsVisible = !device.Authorized || device.Locked || !device.ScreenOn;
        StateOverlayText.Text = !device.Authorized || device.Locked ? "🔒" : "⏻";
        StateOverlayText.Foreground = new SolidColorBrush(Color.FromRgb(255, 92, 92));
        ConnectionText.Text = $"{(device.Wireless ? "Wi-Fi" : "USB")} / ADB" +
                              (string.IsNullOrWhiteSpace(device.AndroidVersion)
                                  ? string.Empty
                                  : $" · Android {device.AndroidVersion}");
        BatteryText.Text = device.BatteryPercent is int battery ? $"{battery}%" : "—";
        BatteryPanel.IsVisible = device.BatteryPercent is not null;
        DropHintText.Text = "Отправить файл или APK";
        MiniDeviceNameText.Text = device.Name;
        MiniConnectionText.Text = device.Wireless ? "Wi-Fi / ADB" : "USB / ADB";
        var showState = !device.Authorized || device.Locked || !device.ScreenOn;
        MiniBatteryText.Text = BatteryText.Text;
        MiniBatteryText.IsVisible = !showState;
        MiniStateText.Text = !device.Authorized || device.Locked ? "🔒" : "⏻";
        MiniStateText.IsVisible = showState;
        MiniDetailText.Text = !device.Authorized ? "Разрешите USB-отладку"
            : device.Locked ? "Телефон заблокирован"
            : !device.ScreenOn ? "Экран выключен"
            : "Готов к работе";
        MiniStatusDot.Fill = new SolidColorBrush(device.Authorized && device.ScreenOn && !device.Locked
            ? Color.FromRgb(78, 205, 132)
            : Color.FromRgb(255, 92, 92));
        SetStatus(!device.Authorized ? "Требуется авторизация ADB на телефоне"
            : device.Locked ? "Телефон заблокирован"
            : !device.ScreenOn ? "Экран телефона выключен"
            : $"Подключено: {device.Serial}", !device.Authorized);
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

    private void ResizeGrip_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        BeginResizeDrag(WindowEdge.SouthEast, e);
        e.Handled = true;
    }

    private void PhoneShell_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
            e.Handled = true;
        }
    }

    private void PhoneShell_DragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void PhoneShell_Drop(object? sender, DragEventArgs e)
    {
        var files = e.DataTransfer.TryGetFiles();
        QueuePaths(files?.Select(file => file.Path.LocalPath) ?? []);
        e.Handled = true;
    }

    private void QueuePaths(IEnumerable<string> paths, bool? installApk = null)
    {
        if (SelectedAdbDevice() is not { } device)
            return;
        var accepted = 0;
        foreach (var path in paths.Where(path => File.Exists(path) || Directory.Exists(path)))
        {
            _runtime.Transfers.Enqueue(device.Serial, path, installApk);
            accepted++;
        }
        if (accepted > 0)
            ShowNotification(accepted == 1 ? "Добавлено в очередь" : $"В очереди файлов: {accepted}");
    }

    private void Transfers_Changed(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var transfer in _runtime.Transfers.Snapshot.Where(item => item.Serial == _boundSerial))
            {
                var previous = _transferStates.GetValueOrDefault(transfer.Id, DesktopTransferState.Queued);
                _transferStates[transfer.Id] = transfer.State;
                if (previous == transfer.State || transfer.State == DesktopTransferState.Running)
                    continue;
                if (transfer.State is DesktopTransferState.Completed or DesktopTransferState.Failed or DesktopTransferState.Cancelled)
                    ShowNotification($"{transfer.Name}: {transfer.Message}");
            }
        });
    }

    private void Runtime_PhotoDetected(object? sender, DesktopPhotoEvent photo) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (photo.Serial != _boundSerial)
                return;
            ShowNotification(photo.Message, photo.LocalPath is { } path ? () => RevealPath(path) : null);
        });

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
        MinWidth = Math.Max(96, width * 0.7);
        MinHeight = Math.Max(150, height * 0.7);
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

    internal Task RefreshDevicesAsync() => _runtime.RefreshAsync();

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
        var result = _adb.StartScrcpy(device.Serial, preset: _settings.Current.ScrcpyPreset);
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
            if (stopped.IsSuccess && _recordingPath is { } savedPath)
                ShowNotification($"Видео сохранено: {Path.GetFileName(savedPath)} · нажмите, чтобы открыть",
                    () => RevealPath(savedPath));
            SetStatus(stopped.IsSuccess && _recordingPath is not null
                ? $"Видео сохранено: {Path.GetFileName(_recordingPath)}"
                : stopped.Message, !stopped.IsSuccess);
            return;
        }
        var folder = _settings.Current.RecordingFolder;
        Directory.CreateDirectory(folder);
        _recordingPath = Path.Combine(folder, $"Android_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.mkv");
        var result = _adb.StartScrcpy(device.Serial, _recordingPath, _settings.Current.ScrcpyPreset);
        if (result.IsSuccess)
            RecordButtonText.Text = "Остановить";
        SetStatus(result.IsSuccess ? "Идёт запись · нажмите ещё раз для остановки" : result.Message,
            !result.IsSuccess);
    }

    private void FilesButton_Click(object? sender, RoutedEventArgs e)
    {
        if (SelectedAdbDevice() is not { } device)
            return;
        new RemoteFilesWindow(_adb, device.Serial).Show(this);
    }

    private void TransfersButton_Click(object? sender, RoutedEventArgs e)
    {
        var window = new TransferQueueWindow(_runtime.Transfers, _boundSerial);
        window.Show(this);
    }

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
            ShowNotification($"Скриншот сохранён: {Path.GetFileName(path)} · нажмите, чтобы открыть",
                () => RevealPath(path));
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
        QueuePaths(files.Select(file => file.Path.LocalPath), true);
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

    private async void CompanionButton_Click(object? sender, RoutedEventArgs e)
    {
        WirelessPanel.IsVisible = false;
        CompanionPanel.IsVisible = !CompanionPanel.IsVisible;
        if (!CompanionPanel.IsVisible || SelectedAdbDevice(false) is not { } device)
            return;
        var state = await _runtime.Companion.GetStateAsync(device.Serial, CancellationToken.None);
        var installerAvailable = _runtime.Companion.IsAvailable;
        CompanionInstallButton.IsEnabled = installerAvailable || state == DesktopCompanionState.Installed;
        CompanionInstallButton.Content = !installerAvailable && state != DesktopCompanionState.Installed
            ? "APK не входит в сборку"
            : state switch
            {
                DesktopCompanionState.UpdateAvailable => "Обновить компаньон",
                DesktopCompanionState.Installed => "Открыть компаньон",
                _ => "Установить компаньон"
            };
    }

    private async void CompanionInstallButton_Click(object? sender, RoutedEventArgs e)
    {
        if (SelectedAdbDevice() is not { } device)
            return;
        var state = await _runtime.Companion.GetStateAsync(device.Serial, CancellationToken.None);
        await RunAdbOperationAsync(token => state == DesktopCompanionState.Installed
                ? _runtime.Companion.LaunchAsync(device.Serial, token)
                : _runtime.Companion.InstallOrUpdateAsync(device.Serial, token),
            result => result.IsSuccess ? "Компаньон открыт ✓" : result.Message);
        var updated = await _runtime.Companion.GetStateAsync(device.Serial, CancellationToken.None);
        CompanionInstallButton.Content = updated == DesktopCompanionState.Installed
            ? "Открыть компаньон"
            : updated == DesktopCompanionState.UpdateAvailable ? "Обновить компаньон" : "Установить компаньон";
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

    private async void PairButton_Click(object? sender, RoutedEventArgs e)
    {
        if (SelectedAdbDevice() is not { } device)
            return;
        var pairing = _host.CreatePairingSession(_boundSerial, "127.0.0.1");
        PairCodeText.Text = $"{pairing.Code[..3]} {pairing.Code[3..]}";
        PairingUriText.Text = pairing.Uri;
        PairingHintText.Text = $"Действует до {pairing.ExpiresAt.ToLocalTime():HH:mm:ss}";
        var reverse = await _runtime.Companion.PrepareReverseAsync(device.Serial, ProtocolConstants.DefaultPort,
            CancellationToken.None);
        var opened = reverse.IsSuccess
            ? await _runtime.Companion.OpenPairingAsync(device.Serial, pairing.Uri, CancellationToken.None)
            : reverse;
        if (!opened.IsSuccess)
            PairingHintText.Text = $"Не удалось открыть на телефоне: {opened.Message}";
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
        if (_activeDevice is { Authorized: true } device)
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
            if (!string.Equals(state.ClientTag, _boundSerial, StringComparison.Ordinal))
                return;
            HostStatusText.Text = state.IsConnected
                ? "Компаньон сопряжён · уведомления включены"
                : "Компаньон не подключён";
        });

    private void HandleNotification(object? sender, CompanionNotification received) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (!string.Equals(received.ClientTag, _boundSerial, StringComparison.Ordinal))
                return;
            ShowNotification($"{received.Notification.Title}: {received.Notification.Preview}".Trim(':', ' '));
        });

    private void ShowNotification(string message, Action? action = null)
    {
        if (!_settings.Current.ShowNotifications || string.IsNullOrWhiteSpace(message))
            return;
        var bubble = new NotificationBubble(Guid.NewGuid(), message, action);
        _notificationBubbles.Add(bubble);
        while (_notificationBubbles.Count > 5)
            RemoveNotification(_notificationBubbles[0].Id);
        RenderNotifications();
        var timer = new CancellationTokenSource();
        _notificationTimers[bubble.Id] = timer;
        _ = HideNotificationLaterAsync(bubble.Id, timer.Token);
    }

    private async Task HideNotificationLaterAsync(Guid id, CancellationToken token)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(_settings.Current.NotificationDurationSeconds), token);
            Dispatcher.UIThread.Post(() => RemoveNotification(id));
        }
        catch (OperationCanceledException) { }
    }

    private void RemoveNotification(Guid id)
    {
        if (_notificationTimers.Remove(id, out var timer))
        {
            timer.Cancel();
            timer.Dispose();
        }
        _notificationBubbles.RemoveAll(item => item.Id == id);
        RenderNotifications();
    }

    private void RenderNotifications()
    {
        FullBubblePanel.Children.Clear();
        MiniBubblePanel.Children.Clear();
        foreach (var bubble in _notificationBubbles)
        {
            FullBubblePanel.Children.Add(CreateBubble(bubble, false));
            MiniBubblePanel.Children.Add(CreateBubble(bubble, true));
        }
    }

    private static Border CreateBubble(NotificationBubble bubble, bool compact)
    {
        var view = new Border
        {
            Padding = compact ? new Thickness(6, 4) : new Thickness(10, 6),
            CornerRadius = new CornerRadius(10, 10, 10, 3),
            Background = new SolidColorBrush(Color.FromRgb(51, 45, 86)),
            Child = new TextBlock
            {
                Text = bubble.Message,
                FontSize = compact ? 8 : 9.5,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = compact ? 23 : 34,
                Foreground = new SolidColorBrush(Color.FromRgb(232, 228, 255))
            }
        };
        if (bubble.Action is not null)
        {
            view.Cursor = new Cursor(StandardCursorType.Hand);
            view.PointerReleased += (_, _) => bubble.Action();
        }
        return view;
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

    private sealed record NotificationBubble(Guid Id, string Message, Action? Action);

    private sealed record AdbDeviceChoice(string Serial, string Name, string Manufacturer,
        string AndroidVersion, int? BatteryPercent, bool Wireless, string AdbState, bool ScreenOn, bool Locked)
    {
        public bool Authorized => AdbState == "device";
        public string Label => $"{Name} · {(Wireless ? "Wi-Fi" : "USB")} · {Serial}";

        public static AdbDeviceChoice From(PortableAdbDevice device) =>
            new(device.Serial, device.Name, device.Manufacturer, device.AndroidVersion,
                device.BatteryPercent, device.Wireless, device.AdbState, device.ScreenOn, device.Locked);

        public override string ToString() => Label;
    }
}
