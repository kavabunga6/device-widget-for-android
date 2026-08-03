using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace AndroidWidget.Desktop;

public sealed partial class App : Application
{
    private MainWindow? _mainWindow;
    private TrayIcon? _trayIcon;
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private int _deviceCount = -1;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _mainWindow = new MainWindow { Opacity = 0 };
            _mainWindow.DeviceCountChanged += MainWindow_DeviceCountChanged;
            _mainWindow.HideRequested += (_, _) => HideWidget();
            desktop.MainWindow = _mainWindow;
            desktop.Exit += (_, _) => DisposeTrayIcon();
            CreateTrayIcon(_mainWindow);
        }
        base.OnFrameworkInitializationCompleted();
    }

    private void CreateTrayIcon(MainWindow window)
    {
        var menu = new NativeMenu();
        menu.Items.Add(CreateMenuItem("Показать виджет", (_, _) => ShowWidget()));
        menu.Items.Add(CreateMenuItem("Обновить устройства", async (_, _) => await window.RefreshDevicesAsync()));
        menu.Items.Add(CreateMenuItem("Настройки", (_, _) => window.OpenSettings(false)));
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(CreateMenuItem("Выход", (_, _) => ExitApplication()));

        _trayIcon = new TrayIcon
        {
            Icon = window.Icon,
            ToolTipText = "Device Widget · устройств нет",
            Menu = menu,
            IsVisible = true
        };
        _trayIcon.Clicked += (_, _) => ShowWidget();
    }

    private static NativeMenuItem CreateMenuItem(string header, EventHandler handler)
    {
        var item = new NativeMenuItem { Header = header };
        item.Click += handler;
        return item;
    }

    private void MainWindow_DeviceCountChanged(object? sender, int count)
    {
        var previousCount = _deviceCount;
        _deviceCount = count;
        if (_trayIcon is not null)
            _trayIcon.ToolTipText = count == 0
                ? "Device Widget · устройств нет"
                : count == 1
                    ? "Device Widget · Android подключён"
                    : $"Device Widget · устройств: {count}";

        if (count == 0)
        {
            HideWidget();
            return;
        }

        if (previousCount <= 0)
            ShowWidget();
    }

    private void ShowWidget()
    {
        if (_mainWindow is null)
            return;
        _mainWindow.Opacity = 1;
        _mainWindow.Show();
        _mainWindow.Activate();
    }

    private void HideWidget()
    {
        if (_mainWindow is not null)
        {
            _mainWindow.Opacity = 1;
            _mainWindow.Hide();
        }
    }

    private void ExitApplication()
    {
        DisposeTrayIcon();
        _mainWindow?.Close();
        _desktop?.Shutdown();
    }

    private void DisposeTrayIcon()
    {
        _trayIcon?.Dispose();
        _trayIcon = null;
    }
}
