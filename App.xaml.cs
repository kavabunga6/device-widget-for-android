using System.Windows;
using System.Windows.Threading;
using AndroidWidget.Composition;
using AndroidWidget.Presentation.Tray;
using AndroidWidget.Services;

namespace AndroidWidget;

public partial class App : System.Windows.Application
{
    private readonly AppServices _services = AppServices.Create();
    private Mutex? _singleInstanceMutex;
    private TrayIconController? _tray;
    private MainWindow? _mainWindow;
    private SettingsWindow? _settingsWindow;
    private readonly Dictionary<string, DeviceMiniWindow> _miniWindows = new();
    private IReadOnlyList<AndroidDevice> _devices = Array.Empty<AndroidDevice>();
    private bool _manuallyHidden;
    private bool _exiting;
    private string? _expandedSerial;
    private int _previousDeviceCount = -1;
    private HashSet<string> _unauthorizedSerials = new(StringComparer.Ordinal);

    public bool IsExiting => _exiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Initialize WPF before any early-return path. Headless diagnostics
        // terminate explicitly because they never create a window/dispatcher
        // lifetime in which a deferred Shutdown() could complete.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        base.OnStartup(e);
        _services.Logger.Write($"Startup begin, PID={Environment.ProcessId}");

        if (e.Args.Contains("--verify-scrcpy-bundle", StringComparer.OrdinalIgnoreCase))
        {
            var valid = _services.Diagnostics.VerifyScrcpyBundle(out var details);
            _services.Logger.Write(valid
                ? $"Bundled scrcpy verified: {details}"
                : $"Bundled scrcpy verification failed: {details}");
            Environment.Exit(valid ? 0 : 1);
            return;
        }

        if (e.Args.Contains("--verify-sms-parser", StringComparer.OrdinalIgnoreCase))
        {
            var valid = _services.Diagnostics.VerifySmsParser();
            _services.Logger.Write(valid ? "SMS parser verified" : "SMS parser verification failed");
            Environment.Exit(valid ? 0 : 1);
            return;
        }

        if (e.Args.Contains("--verify-companion-bundle", StringComparer.OrdinalIgnoreCase))
        {
            var valid = _services.Diagnostics.VerifyCompanionBundle(out var details);
            _services.Logger.Write(valid
                ? $"Bundled companion verified: {details}"
                : $"Bundled companion verification failed: {details}");
            Environment.Exit(valid ? 0 : 1);
            return;
        }

        if (e.Args.Contains("--verify-wireless-qr", StringComparer.OrdinalIgnoreCase))
        {
            var qrValid = WirelessPairingWindow.VerifyQrRenderer(out var details);
            var parserValid = _services.Diagnostics.VerifyWirelessPairingParser();
            var valid = qrValid && parserValid;
            _services.Logger.Write(valid
                ? $"Wireless QR and mDNS parser verified: {details}"
                : $"Wireless QR verification failed: renderer={qrValid}, parser={parserValid}, {details}");
            Environment.Exit(valid ? 0 : 1);
            return;
        }

        _singleInstanceMutex = new Mutex(true, "AndroidWidget.SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
        {
            System.Windows.MessageBox.Show("Android Widget уже запущен.", "Android Widget",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnUnhandledException;

        ThemeService.Apply(_services.Settings.Current.Theme);
        _tray = new TrayIconController(
            () => Dispatcher.Invoke(() => ShowMainFor()),
            () => Dispatcher.Invoke(EnterMiniMode),
            () => Dispatcher.Invoke(ShowSettings),
            () => Dispatcher.Invoke(ExitApplication));

        _mainWindow = new MainWindow(_services.Devices, _services.Settings, _services.Desktop,
            _services.Logger, _services.Screenshots, _services.Recordings, _services.Transfers,
            _services.PhotoImport, _services.Companion, _services.CompanionCoordinator)
        { Opacity = 0 };
        _mainWindow.DevicesUpdated += HandleDevicesUpdated;
        _mainWindow.Show(); // Loads the background monitor.
        _mainWindow.Hide();
        _mainWindow.Opacity = 1;
        _services.Logger.Write("Startup completed");
    }

    public void EnterMiniMode()
    {
        _manuallyHidden = false;
        _expandedSerial = null;
        _services.Settings.Update(settings => settings with { IsMini = true });
        _mainWindow?.Hide();
        SyncMiniWindows(_devices);
    }

    public void ShowMainFor(string? serial = null)
    {
        if (_mainWindow is null || _devices.Count == 0)
        {
            ShowSettingsOrNoDeviceMessage();
            return;
        }

        _manuallyHidden = false;
        _services.Settings.Update(settings => settings with { IsMini = false });
        var selected = !string.IsNullOrWhiteSpace(serial) && _devices.Any(device => device.Serial == serial)
            ? serial
            : _expandedSerial is not null && _devices.Any(device => device.Serial == _expandedSerial)
                ? _expandedSerial
                : _devices[0].Serial;
        _expandedSerial = selected;
        _mainWindow.SelectDevice(selected);
        _mainWindow.Show();
        _mainWindow.Activate();
        SyncMiniWindows(_devices.Where(device => device.Serial != selected).ToList());
    }

    public void HideToTray()
    {
        _manuallyHidden = true;
        _mainWindow?.Hide();
        CloseMiniWindows();
        _tray?.ShowInfo("Android Widget", "Виджет продолжает работать в трее.", 1200);
    }

    public void ShowSettings()
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_services.Settings, _services.Screenshots, _services.Recordings,
            _services.PhotoImport);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    public void ExitApplication()
    {
        _exiting = true;
        CloseMiniWindows();
        _settingsWindow?.Close();
        _mainWindow?.Close();
        _tray?.Dispose();
        Shutdown();
    }

    private void HandleDevicesUpdated(object? sender, IReadOnlyList<AndroidDevice> devices)
    {
        _devices = devices;
        _tray?.Update(devices);

        var unauthorizedNow = devices
            .Where(device => device.State == DeviceConnectionState.Unauthorized)
            .Select(device => device.Serial)
            .ToHashSet(StringComparer.Ordinal);
        var newlyUnauthorized = unauthorizedNow.Except(_unauthorizedSerials).ToList();
        if (newlyUnauthorized.Count > 0)
        {
            var device = devices.First(item => item.Serial == newlyUnauthorized[0]);
            _tray?.ShowWarning("Требуется авторизация Android",
                $"Разблокируйте {device.DisplayName} и подтвердите RSA-ключ для USB-отладки.");
        }
        _unauthorizedSerials = unauthorizedNow;

        if (devices.Count == 0)
        {
            _expandedSerial = null;
            _mainWindow?.Hide();
            CloseMiniWindows();
        }
        else if (_manuallyHidden)
        {
            _mainWindow?.Hide();
            CloseMiniWindows();
        }
        else
        {
            var expandedDeviceDisconnected = _expandedSerial is not null &&
                                             devices.All(device => device.Serial != _expandedSerial);
            if (expandedDeviceDisconnected)
            {
                // An expanded card belongs to one serial for its whole lifetime.
                // Other devices keep their own mini cards and never replace it.
                _expandedSerial = null;
                _mainWindow?.Hide();
                _services.Settings.Update(settings => settings with { IsMini = true });
                SyncMiniWindows(devices);
            }
            else if (_expandedSerial is not null)
            {
                _mainWindow?.SelectDevice(_expandedSerial);
                _mainWindow?.Show();
                SyncMiniWindows(devices.Where(device => device.Serial != _expandedSerial).ToList());
            }
            else if (_services.Settings.Current.IsMini || devices.Count > 1)
            {
                // In multi-device mode every phone must have its own visible card.
                _mainWindow?.Hide();
                SyncMiniWindows(devices);
            }
            else
            {
                _expandedSerial = devices[0].Serial;
                _mainWindow?.SelectDevice(_expandedSerial);
                _mainWindow?.Show();
                CloseMiniWindows();
            }
        }

        if (_previousDeviceCount == 0 && devices.Count > 0)
            _tray?.ShowInfo("Android подключён", TrayIconController.GetDeviceSummary(devices), 1400);
        _previousDeviceCount = devices.Count;
    }

    private void SyncMiniWindows(IReadOnlyList<AndroidDevice> devices)
    {
        var serials = devices.Select(device => device.Serial).ToHashSet(StringComparer.Ordinal);
        foreach (var stale in _miniWindows.Keys.Where(serial => !serials.Contains(serial)).ToList())
        {
            _miniWindows[stale].Close();
            _miniWindows.Remove(stale);
        }

        for (var index = 0; index < devices.Count; index++)
        {
            var device = devices[index];
            if (_miniWindows.TryGetValue(device.Serial, out var existing))
            {
                existing.UpdateDevice(device);
                continue;
            }

            var mini = new DeviceMiniWindow(device, _services.Devices, _services.Settings, _services.Desktop,
                _services.Screenshots, _services.Recordings, _services.Transfers, _services.PhotoImport,
                _services.Companion, _services.CompanionCoordinator);
            mini.PlaceAt(index);
            _miniWindows.Add(device.Serial, mini);
            mini.Show();
        }
    }

    private void CloseMiniWindows()
    {
        foreach (var window in _miniWindows.Values.ToList())
            window.Close();
        _miniWindows.Clear();
    }

    private void ShowSettingsOrNoDeviceMessage()
    {
        if (_devices.Count == 0)
        {
            _tray?.ShowInfo("Android Widget", "Подключите телефон по USB или Wi-Fi ADB.");
            return;
        }
        ShowSettings();
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _services.Logger.Write($"Unhandled UI exception: {e.Exception}");
        System.Windows.MessageBox.Show(e.Exception.Message, "Android Widget",
            MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services.Logger.Write($"Exit, code={e.ApplicationExitCode}");
        _tray?.Dispose();
        if (_singleInstanceMutex is not null)
        {
            try { _singleInstanceMutex.ReleaseMutex(); } catch { }
            _singleInstanceMutex.Dispose();
        }
        try
        {
            _services.Transfers.Dispose();
            _services.CompanionCoordinator.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _services.Logger.Write($"Companion host shutdown failed: {ex}");
        }
        base.OnExit(e);
    }
}
