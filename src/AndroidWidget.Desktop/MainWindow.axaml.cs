using System.Collections.ObjectModel;
using AndroidWidget.CompanionHost;
using AndroidWidget.Core;
using AndroidWidget.Protocol;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace AndroidWidget.Desktop;

public sealed partial class MainWindow : Window
{
    private readonly CompanionHostService _host;
    private readonly PortableAdbService _adb = new();
    private readonly ObservableCollection<DeviceCard> _devices = new();
    private readonly ObservableCollection<AdbDeviceChoice> _adbDevices = new();
    private readonly ObservableCollection<NotificationCard> _notifications = new();
    private readonly Dictionary<string, string> _lastNotificationSignatures = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _adbRefreshTimer;
    private CancellationTokenSource? _adbOperation;

    public MainWindow()
    {
        InitializeComponent();
        ProductVersionText.Text = ProductVersion.Label;
        var dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AndroidWidget", "companion-v1");
        _host = new CompanionHostService(new CompanionHostOptions(dataDirectory));
        _host.DeviceChanged += HandleDeviceChanged;
        _host.NotificationReceived += HandleNotification;
        DevicesList.ItemsSource = _devices;
        AdbDeviceCombo.ItemsSource = _adbDevices;
        NotificationsList.ItemsSource = _notifications;
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
            HostStatusText.Foreground = Avalonia.Media.Brushes.IndianRed;
        }
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

    private async void RefreshAdbButton_Click(object? sender, RoutedEventArgs e) => await RefreshAdbAsync();

    private async Task RefreshAdbAsync()
    {
        try
        {
            var selectedSerial = (AdbDeviceCombo.SelectedItem as AdbDeviceChoice)?.Serial;
            var discovered = await _adb.GetDevicesAsync(CancellationToken.None);
            _adbDevices.Clear();
            foreach (var device in discovered)
                _adbDevices.Add(new AdbDeviceChoice(device.Serial, device.Name, device.Wireless));
            AdbDeviceCombo.SelectedItem = _adbDevices.FirstOrDefault(device => device.Serial == selectedSerial)
                                          ?? _adbDevices.FirstOrDefault();
            AdbStatusText.Text = _adbDevices.Count == 0
                ? "ADB-устройства не найдены"
                : $"Устройств: {_adbDevices.Count}";
        }
        catch (Exception ex)
        {
            AdbStatusText.Text = $"ADB: {ex.Message}";
        }
    }

    private void AdbScreenButton_Click(object? sender, RoutedEventArgs e)
    {
        if (SelectedAdbDevice() is not { } device)
            return;
        var result = _adb.StartScrcpy(device.Serial);
        AdbStatusText.Text = result.IsSuccess ? "scrcpy запущен" : result.Message;
    }

    private void AdbRecordButton_Click(object? sender, RoutedEventArgs e)
    {
        if (SelectedAdbDevice() is not { } device)
            return;
        var folder = ResolveUserFolder(Environment.SpecialFolder.MyVideos, "Device Widget");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"Android_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.mkv");
        var result = _adb.StartScrcpy(device.Serial, path);
        AdbStatusText.Text = result.IsSuccess ? $"Запись: {path}" : result.Message;
    }

    private async void AdbScreenshotButton_Click(object? sender, RoutedEventArgs e)
    {
        if (SelectedAdbDevice() is not { } device)
            return;
        var folder = ResolveUserFolder(Environment.SpecialFolder.MyPictures, "Device Widget");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"Android_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png");
        await RunAdbOperationAsync(token => _adb.ScreenshotAsync(device.Serial, path, token),
            result => result.IsSuccess ? $"Скриншот: {path}" : result.Message);
    }

    private async void AdbPushButton_Click(object? sender, RoutedEventArgs e)
    {
        if (SelectedAdbDevice() is not { } device)
            return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Отправить файл на Android",
            AllowMultiple = true
        });
        foreach (var file in files)
        {
            var path = file.Path.LocalPath;
            await RunAdbOperationAsync(token => _adb.PushAsync(device.Serial, path, token),
                result => result.IsSuccess ? $"Передано: {Path.GetFileName(path)}" : result.Message);
        }
    }

    private async void WirelessPairButton_Click(object? sender, RoutedEventArgs e) =>
        await RunAdbOperationAsync(token => _adb.PairAsync(WirelessEndpointText.Text ?? string.Empty,
                WirelessCodeText.Text ?? string.Empty, token),
            result => result.IsSuccess ? "Wireless debugging сопряжён" : result.Message);

    private async void WirelessConnectButton_Click(object? sender, RoutedEventArgs e)
    {
        await RunAdbOperationAsync(token => _adb.ConnectAsync(WirelessEndpointText.Text ?? string.Empty, token),
            result => result.IsSuccess ? "Wi-Fi ADB подключён" : result.Message);
        await RefreshAdbAsync();
    }

    private AdbDeviceChoice? SelectedAdbDevice()
    {
        if (AdbDeviceCombo.SelectedItem is AdbDeviceChoice device)
            return device;
        AdbStatusText.Text = "Выберите ADB-устройство";
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
            AdbStatusText.Text = "Выполняется…";
            var result = await operation(_adbOperation.Token);
            AdbStatusText.Text = message(result);
        }
        catch (OperationCanceledException)
        {
            AdbStatusText.Text = "Операция отменена";
        }
    }

    private static string ResolveUserFolder(Environment.SpecialFolder folder, string child)
    {
        var root = Environment.GetFolderPath(folder);
        if (string.IsNullOrWhiteSpace(root))
            root = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(root, child);
    }

    private void HandleDeviceChanged(object? sender, CompanionDeviceState state) =>
        Dispatcher.UIThread.Post(() =>
        {
            var previous = _devices.FirstOrDefault(device => device.Id == state.Identity.InstallationId);
            if (!state.IsConnected)
            {
                if (previous is not null)
                    _devices.Remove(previous);
                NoDevicesText.IsVisible = _devices.Count == 0;
                return;
            }
            var index = previous is null ? -1 : _devices.IndexOf(previous);
            var signature = state.LatestNotification is { } notification
                ? $"{notification.NotificationId}\n{notification.Title}\n{notification.Preview}"
                : string.Empty;
            var hasNewNotification = signature.Length > 0 &&
                                     (!_lastNotificationSignatures.TryGetValue(state.Identity.InstallationId,
                                          out var previousSignature) || previousSignature != signature);
            if (hasNewNotification)
                _lastNotificationSignatures[state.Identity.InstallationId] = signature;
            var updated = DeviceCard.From(state, hasNewNotification);
            if (!hasNewNotification && previous?.HasLatestMessage == true)
                updated = updated with
                {
                    LatestMessage = previous.LatestMessage,
                    HasLatestMessage = true,
                    LatestNotificationSignature = previous.LatestNotificationSignature
                };
            if (index >= 0)
                _devices[index] = updated;
            else
                _devices.Add(updated);
            if (updated.HasLatestMessage)
                _ = ClearNotificationBubbleAsync(updated.Id, updated.LatestNotificationSignature);
            NoDevicesText.IsVisible = _devices.Count == 0;
        });

    private async Task ClearNotificationBubbleAsync(string deviceId, string notificationSignature)
    {
        await Task.Delay(TimeSpan.FromSeconds(10));
        Dispatcher.UIThread.Post(() =>
        {
            var current = _devices.FirstOrDefault(device => device.Id == deviceId);
            if (current is null || current.LatestNotificationSignature != notificationSignature)
                return;
            var index = _devices.IndexOf(current);
            _devices[index] = current with
            {
                LatestMessage = string.Empty,
                HasLatestMessage = false,
                LatestNotificationSignature = string.Empty
            };
        });
    }

    private void HandleNotification(object? sender, CompanionNotification received) =>
        Dispatcher.UIThread.Post(() =>
        {
            var notification = received.Notification;
            _notifications.Insert(0, new NotificationCard(notification.AppName, notification.Title,
                notification.Preview));
            while (_notifications.Count > 30)
                _notifications.RemoveAt(_notifications.Count - 1);
            NoNotificationsText.IsVisible = false;
        });

    private sealed record DeviceCard(string Id, string Name, string Detail, string Battery, string StatusColor,
        string LatestMessage, bool HasLatestMessage, string LatestNotificationSignature)
    {
        public static DeviceCard From(CompanionDeviceState state, bool showLatestNotification)
        {
            var status = state.Status;
            var battery = status?.BatteryPercent is int value
                ? $"Заряд {value}%{(status.IsCharging ? " · зарядка" : string.Empty)}"
                : "Заряд неизвестен";
            return new DeviceCard(state.Identity.InstallationId, state.Identity.DisplayName,
                $"{state.Identity.Manufacturer} {state.Identity.Model} · Android {state.Identity.AndroidVersion}",
                battery, "#74D8A4",
                state.LatestNotification is { } notification
                    ? $"{notification.Title}: {notification.Preview}".Trim(':', ' ')
                    : string.Empty,
                showLatestNotification,
                showLatestNotification && state.LatestNotification is { } latest
                    ? $"{latest.NotificationId}\n{latest.Title}\n{latest.Preview}"
                    : string.Empty);
        }
    }

    private sealed record NotificationCard(string AppName, string Title, string Preview);
    private sealed record AdbDeviceChoice(string Serial, string Name, bool Wireless)
    {
        public string Label => $"{Name} · {(Wireless ? "Wi-Fi" : "USB")} · {Serial}";
        public override string ToString() => Label;
    }
}
