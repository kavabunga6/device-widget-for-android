using System.Collections.ObjectModel;
using AndroidWidget.CompanionHost;
using AndroidWidget.Protocol;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace AndroidWidget.Desktop;

public sealed partial class MainWindow : Window
{
    private readonly CompanionHostService _host;
    private readonly ObservableCollection<DeviceCard> _devices = new();
    private readonly ObservableCollection<NotificationCard> _notifications = new();
    private readonly Dictionary<string, string> _lastNotificationSignatures = new(StringComparer.Ordinal);

    public MainWindow()
    {
        InitializeComponent();
        var dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AndroidWidget", "companion-v1");
        _host = new CompanionHostService(new CompanionHostOptions(dataDirectory));
        _host.DeviceChanged += HandleDeviceChanged;
        _host.NotificationReceived += HandleNotification;
        DevicesList.ItemsSource = _devices;
        NotificationsList.ItemsSource = _notifications;
        Opened += async (_, _) => await StartHostAsync();
        Closed += async (_, _) => await _host.DisposeAsync();
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
}
