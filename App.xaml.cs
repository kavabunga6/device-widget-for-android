using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using AndroidWidget.Models;
using AndroidWidget.Services;

namespace AndroidWidget;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstanceMutex;
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private Icon? _ownedTrayIcon;
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
        AppLog.Write($"Startup begin, PID={Environment.ProcessId}");

        if (e.Args.Contains("--verify-scrcpy-bundle", StringComparer.OrdinalIgnoreCase))
        {
            var bundledPath = AdbService.PrepareBundledScrcpy(out var bundleError);
            AppLog.Write(bundledPath is null
                ? $"Bundled scrcpy verification failed: {bundleError}"
                : $"Bundled scrcpy verified: {bundledPath}");
            Shutdown(bundledPath is null ? 1 : 0);
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
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        base.OnStartup(e);

        ThemeService.Apply(SettingsService.Current.Theme);
        InitializeTray();

        _mainWindow = new MainWindow { Opacity = 0 };
        _mainWindow.DevicesUpdated += HandleDevicesUpdated;
        _mainWindow.Show(); // Loads the background monitor.
        _mainWindow.Hide();
        _mainWindow.Opacity = 1;
        AppLog.Write("Startup completed");
    }

    public void EnterMiniMode()
    {
        _manuallyHidden = false;
        _expandedSerial = null;
        SettingsService.Update(settings => settings with { IsMini = true });
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
        SettingsService.Update(settings => settings with { IsMini = false });
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
        _trayIcon?.ShowBalloonTip(1200, "Android Widget", "Виджет продолжает работать в трее.",
            System.Windows.Forms.ToolTipIcon.Info);
    }

    public void ShowSettings()
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow();
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
        _trayIcon?.Dispose();
        _ownedTrayIcon?.Dispose();
        Shutdown();
    }

    private void HandleDevicesUpdated(object? sender, IReadOnlyList<AndroidDevice> devices)
    {
        _devices = devices;
        UpdateTray(devices);

        var unauthorizedNow = devices
            .Where(device => device.State == DeviceConnectionState.Unauthorized)
            .Select(device => device.Serial)
            .ToHashSet(StringComparer.Ordinal);
        var newlyUnauthorized = unauthorizedNow.Except(_unauthorizedSerials).ToList();
        if (newlyUnauthorized.Count > 0 && _trayIcon is not null)
        {
            var device = devices.First(item => item.Serial == newlyUnauthorized[0]);
            _trayIcon.ShowBalloonTip(3000, "Требуется авторизация Android",
                $"Разблокируйте {device.DisplayName} и подтвердите RSA-ключ для USB-отладки.",
                System.Windows.Forms.ToolTipIcon.Warning);
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
            var expandedStillConnected = _expandedSerial is not null &&
                                         devices.Any(device => device.Serial == _expandedSerial);
            if (!expandedStillConnected)
                _expandedSerial = null;

            if (_expandedSerial is not null)
            {
                _mainWindow?.SelectDevice(_expandedSerial);
                _mainWindow?.Show();
                SyncMiniWindows(devices.Where(device => device.Serial != _expandedSerial).ToList());
            }
            else if (SettingsService.Current.IsMini || devices.Count > 1)
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

        if (_previousDeviceCount == 0 && devices.Count > 0 && _trayIcon is not null)
            _trayIcon.ShowBalloonTip(1400, "Android подключён", DeviceSummary(devices),
                System.Windows.Forms.ToolTipIcon.Info);
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

            var mini = new DeviceMiniWindow(device);
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

    private void InitializeTray()
    {
        _ownedTrayIcon = CreatePhoneIcon();
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Открыть виджет", null, (_, _) => Dispatcher.Invoke(() => ShowMainFor()));
        menu.Items.Add("Мини-виджеты", null, (_, _) => Dispatcher.Invoke(EnterMiniMode));
        menu.Items.Add("Настройки", null, (_, _) => Dispatcher.Invoke(ShowSettings));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => Dispatcher.Invoke(ExitApplication));

        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = _ownedTrayIcon,
            Text = "Android Widget · устройств нет",
            Visible = true,
            ContextMenuStrip = menu
        };
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(() => ShowMainFor());
    }

    private void UpdateTray(IReadOnlyList<AndroidDevice> devices)
    {
        if (_trayIcon is null)
            return;
        var unauthorized = devices.FirstOrDefault(device => device.State == DeviceConnectionState.Unauthorized);
        _trayIcon.Icon = unauthorized is null ? _ownedTrayIcon : SystemIcons.Warning;
        _trayIcon.Text = unauthorized is not null
            ? TruncateTrayText($"Android Widget · авторизуйте {unauthorized.DisplayName}")
            : devices.Count == 0
                ? "Android Widget · устройств нет"
                : TruncateTrayText($"Android Widget · {DeviceSummary(devices)}");
    }

    private void ShowSettingsOrNoDeviceMessage()
    {
        if (_devices.Count == 0)
        {
            _trayIcon?.ShowBalloonTip(1600, "Android Widget",
                "Подключите телефон по USB или Wi-Fi ADB.", System.Windows.Forms.ToolTipIcon.Info);
            return;
        }
        ShowSettings();
    }

    private static string DeviceSummary(IReadOnlyList<AndroidDevice> devices) =>
        devices.Count == 1 ? devices[0].DisplayName : $"устройств: {devices.Count}";

    private static string TruncateTrayText(string value) => value.Length <= 63 ? value : value[..60] + "…";

    private static Icon CreatePhoneIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);
        using var body = new SolidBrush(Color.FromArgb(35, 40, 58));
        using var screen = new SolidBrush(Color.FromArgb(124, 92, 252));
        graphics.FillRoundedRectangle(body, new Rectangle(7, 2, 18, 28), 5);
        graphics.FillRoundedRectangle(screen, new Rectangle(10, 6, 12, 18), 2);
        graphics.FillEllipse(Brushes.White, 15, 26, 2, 2);
        var handle = bitmap.GetHicon();
        try { return (Icon)Icon.FromHandle(handle).Clone(); }
        finally { DestroyIcon(handle); }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Write($"Unhandled UI exception: {e.Exception}");
        System.Windows.MessageBox.Show(e.Exception.Message, "Android Widget",
            MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLog.Write($"Exit, code={e.ApplicationExitCode}");
        _trayIcon?.Dispose();
        _ownedTrayIcon?.Dispose();
        if (_singleInstanceMutex is not null)
        {
            try { _singleInstanceMutex.ReleaseMutex(); } catch { }
            _singleInstanceMutex.Dispose();
        }
        base.OnExit(e);
    }
}

internal static class DrawingExtensions
{
    public static void FillRoundedRectangle(this Graphics graphics, Brush brush, Rectangle rectangle, int radius)
    {
        using var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        graphics.FillPath(brush, path);
    }
}
