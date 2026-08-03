using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;

namespace AndroidWidget.Desktop;

public sealed partial class App : Application
{
    private readonly Dictionary<string, MainWindow> _windows = new(StringComparer.Ordinal);
    private DesktopRuntime? _runtime;
    private TrayIcon? _trayIcon;
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private bool _exitRequested;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _runtime = new DesktopRuntime();
            RequestedThemeVariant = _runtime.Settings.Current.Theme == "Light"
                ? ThemeVariant.Light
                : ThemeVariant.Dark;
            _runtime.DevicesChanged += Runtime_DevicesChanged;
            desktop.Exit += (_, _) => DisposeTrayIcon();
            CreateTrayIcon();
            _ = _runtime.StartAsync();
        }
        base.OnFrameworkInitializationCompleted();
    }

    private void Runtime_DevicesChanged(object? sender, IReadOnlyList<PortableAdbDevice> devices) =>
        Dispatcher.UIThread.Post(() => SynchronizeWindows(devices));

    private void SynchronizeWindows(IReadOnlyList<PortableAdbDevice> devices)
    {
        if (_runtime is null)
            return;
        var connected = devices.Select(device => device.Serial).ToHashSet(StringComparer.Ordinal);
        foreach (var stale in _windows.Keys.Where(serial => !connected.Contains(serial)).ToList())
        {
            var window = _windows[stale];
            _windows.Remove(stale);
            window.CloseForDisconnect();
        }

        foreach (var device in devices)
        {
            if (_windows.TryGetValue(device.Serial, out var existing))
            {
                existing.UpdateDevice(device);
                continue;
            }
            var window = new MainWindow(_runtime, device);
            window.HideRequested += (_, _) => window.Hide();
            window.Closed += (_, _) => _windows.Remove(device.Serial);
            _windows.Add(device.Serial, window);
            window.Show();
            window.PlaceInSlot(_windows.Count - 1);
        }

        if (_trayIcon is not null)
            _trayIcon.ToolTipText = devices.Count == 0
                ? "Device Widget · устройств нет"
                : devices.Count == 1
                    ? $"Device Widget · {devices[0].Name}"
                    : $"Device Widget · устройств: {devices.Count}";
    }

    private void CreateTrayIcon()
    {
        var menu = new NativeMenu();
        menu.Items.Add(CreateMenuItem("Показать виджеты", (_, _) => ShowWidgets()));
        menu.Items.Add(CreateMenuItem("Обновить устройства", async (_, _) =>
        {
            if (_runtime is not null)
                await _runtime.RefreshAsync();
        }));
        menu.Items.Add(CreateMenuItem("Настройки", (_, _) => ShowSettings()));
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(CreateMenuItem("Выход", (_, _) => RequestExitApplication()));

        using var iconStream = AssetLoader.Open(new Uri("avares://DeviceWidget/Assets/AppIcon.png"));
        _trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(iconStream),
            ToolTipText = "Device Widget · устройств нет",
            Menu = menu,
            IsVisible = true
        };
        _trayIcon.Clicked += (_, _) => ShowWidgets();
    }

    private static NativeMenuItem CreateMenuItem(string header, EventHandler handler)
    {
        var item = new NativeMenuItem { Header = header };
        item.Click += handler;
        return item;
    }

    private void ShowWidgets()
    {
        foreach (var window in _windows.Values)
        {
            window.Show();
            window.Activate();
        }
    }

    private void ShowSettings()
    {
        if (_runtime is null)
            return;
        var settings = new SettingsWindow(_runtime.Settings) { Topmost = _runtime.Settings.Current.Topmost };
        settings.Show();
        settings.Activate();
    }

    private void RequestExitApplication()
    {
        if (_exitRequested)
            return;
        _exitRequested = true;
        _ = ExitAfterTrayMenuDismissalAsync();
    }

    private async Task ExitAfterTrayMenuDismissalAsync()
    {
        // Native tray menus dismiss after their click callback returns. Keeping the
        // dispatcher alive briefly prevents an orphaned popup on every desktop OS.
        await Task.Delay(TimeSpan.FromMilliseconds(150));
        try
        {
            DisposeTrayIcon();
            foreach (var window in _windows.Values.ToList())
                window.CloseForExit();
            _windows.Clear();
            if (_runtime is not null)
            {
                _runtime.DevicesChanged -= Runtime_DevicesChanged;
                await _runtime.DisposeAsync();
                _runtime = null;
            }
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"Device Widget shutdown cleanup failed: {exception}");
        }
        finally
        {
            _desktop?.Shutdown();
        }
    }

    private void DisposeTrayIcon()
    {
        if (_trayIcon is null)
            return;
        _trayIcon.IsVisible = false;
        _trayIcon.Menu = null;
        _trayIcon.Dispose();
        _trayIcon = null;
    }
}
