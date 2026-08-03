using AndroidWidget.CompanionHost;
using AndroidWidget.Protocol;
using Avalonia.Threading;

namespace AndroidWidget.Desktop;

internal sealed class DesktopRuntime : IAsyncDisposable
{
    private readonly DispatcherTimer _refreshTimer;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _refreshing;
    private bool _started;

    public DesktopRuntime()
    {
        Settings = new DesktopSettingsStore();
        Adb = new PortableAdbService();
        Companion = new DesktopCompanionInstaller(Adb);
        Photos = new DesktopPhotoMonitor(Adb);
        Photos.PhotoDetected += (_, photo) => PhotoDetected?.Invoke(this, photo);
        Transfers = new DesktopTransferQueue(Adb);
        var dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AndroidWidget", "companion-v1");
        Host = new CompanionHostService(new CompanionHostOptions(dataDirectory));
        Host.DeviceChanged += (_, state) => CompanionDeviceChanged?.Invoke(this, state);
        Host.NotificationReceived += (_, notification) => NotificationReceived?.Invoke(this, notification);
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
    }

    public DesktopSettingsStore Settings { get; }
    public PortableAdbService Adb { get; }
    public DesktopCompanionInstaller Companion { get; }
    public DesktopPhotoMonitor Photos { get; }
    public DesktopTransferQueue Transfers { get; }
    public CompanionHostService Host { get; }
    public IReadOnlyList<PortableAdbDevice> Devices { get; private set; } = [];
    public string? HostError { get; private set; }
    public string? AdbError { get; private set; }

    public event EventHandler<IReadOnlyList<PortableAdbDevice>>? DevicesChanged;
    public event EventHandler<CompanionDeviceState>? CompanionDeviceChanged;
    public event EventHandler<CompanionNotification>? NotificationReceived;
    public event EventHandler<DesktopPhotoEvent>? PhotoDetected;

    public async Task StartAsync()
    {
        if (_started)
            return;
        _started = true;
        try
        {
            await Host.StartAsync(_lifetime.Token);
        }
        catch (Exception ex)
        {
            HostError = ex.Message;
        }
        await RefreshAsync();
        _refreshTimer.Start();
    }

    public async Task RefreshAsync()
    {
        if (_refreshing)
            return;
        _refreshing = true;
        try
        {
            Devices = await Adb.GetDevicesAsync(_lifetime.Token);
            AdbError = null;
            DevicesChanged?.Invoke(this, Devices);
            foreach (var device in Devices)
                _ = Photos.CheckAsync(device, Settings.Current, _lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            AdbError = ex.Message;
        }
        finally
        {
            _refreshing = false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _refreshTimer.Stop();
        _lifetime.Cancel();
        Transfers.Dispose();
        await Host.DisposeAsync();
        _lifetime.Dispose();
    }
}
