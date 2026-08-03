using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AndroidWidget;
using AndroidWidget.CompanionHost;
using AndroidWidget.Core.Abstractions;
using AndroidWidget.Core.Devices;
using AndroidWidget.Core.Files;
using AndroidWidget.Core.Operations;
using AndroidWidget.Core.Settings;
using AndroidWidget.Presentation.Files;
using AndroidWidget.Presentation.Media;
using AndroidWidget.Presentation.Screenshots;
using AndroidWidget.Presentation.Transfers;
using AndroidWidget.Services;

namespace DocsCapture;

internal static class Program
{
    private static readonly AndroidDevice DemoDevice = new(
        "demo-device", "Aurora Phone", "Aurora X1", "16", 87,
        DeviceConnectionState.Online, false, Manufacturer: "Demo",
        Brand: "Aurora", DeviceCode: "aurora_x1", ScreenResolution: "1440x3200",
        CompanionState: CompanionInstallationState.Installed,
        IsCompanionConnected: true, CompanionNotificationAccess: true);

    [STAThread]
    private static int Main(string[] args)
    {
        var output = args.Length > 0
            ? Path.GetFullPath(args[0])
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "images"));
        Directory.CreateDirectory(output);

        var application = new App();
        application.InitializeComponent();
        var settings = new DemoSettingsService(new AppSettings(
            ScreenshotFolder: Path.Combine("Demo", "Pictures", "Device Widget"),
            RecordingFolder: Path.Combine("Demo", "Videos", "Device Widget"),
            PhotoImportFolder: Path.Combine("Demo", "Pictures", "Device Widget Imports"),
            ShowSmsBubbles: true,
            NotificationDisplaySeconds: 10,
            NotifyNewPhotos: true));
        var devices = new DemoDeviceService();
        var desktop = new DemoDesktopIntegration();
        var logger = new DemoLogger();
        var screenshots = new ScreenshotStorage(settings);
        var recordings = new RecordingStorage(settings);
        var imports = new PhotoImportService(devices, settings);
        using var transfers = new TransferQueueService(devices);
        var companion = new DemoCompanionService();
        var hostData = Path.Combine(Path.GetTempPath(), "DeviceWidgetDocs", Guid.NewGuid().ToString("N"));
        var host = new CompanionHostService(new CompanionHostOptions(hostData, 0));
        var coordinator = new CompanionCoordinator(host, companion, logger);

        try
        {
            CaptureMain(output, devices, settings, desktop, logger, screenshots, recordings, transfers, imports,
                companion, coordinator);
            CaptureMini(output, devices, settings, desktop, screenshots, recordings, transfers, imports,
                companion, coordinator);
            CaptureSettings(output, settings, screenshots, recordings, imports);
            CaptureMediaSettings(output, settings, recordings, imports);
            CaptureScreenRecording(output, devices, settings, desktop, recordings);
            CaptureTransfers(output, transfers);
            CaptureFiles(output, devices, desktop, transfers);
            CaptureWireless(output, devices);
            CapturePairing(output, coordinator);
        }
        finally
        {
            coordinator.DisposeAsync().AsTask().GetAwaiter().GetResult();
            if (Directory.Exists(hostData))
                Directory.Delete(hostData, true);
        }

        return 0;
    }

    private static void CaptureMain(string output, IAndroidDeviceService devices, ISettingsService settings,
        IDesktopIntegration desktop, IAppLogger logger, ScreenshotStorage screenshots,
        RecordingStorage recordings, TransferQueueService transfers, PhotoImportService imports,
        ICompanionService companion, CompanionCoordinator coordinator)
    {
        var window = new MainWindow(devices, settings, desktop, logger, screenshots, recordings, transfers,
            imports, companion, coordinator)
        {
            Width = 330,
            Height = 500
        };
        Invoke(window, "SetActiveDevice", DemoDevice, null);
        CaptureWindow(window, Path.Combine(output, "main-card.png"));

        var panel = Require<Border>(window, "ActionPanel");
        var margin = panel.Margin;
        panel.Margin = new Thickness(0);
        CaptureVisual(panel, new Size(310, 500), Path.Combine(output, "actions-menu.png"));
        panel.Margin = margin;
    }

    private static void CaptureMini(string output, IAndroidDeviceService devices, ISettingsService settings,
        IDesktopIntegration desktop, ScreenshotStorage screenshots, RecordingStorage recordings,
        TransferQueueService transfers, PhotoImportService imports, ICompanionService companion,
        CompanionCoordinator coordinator)
    {
        var window = new DeviceMiniWindow(DemoDevice, devices, settings, desktop, screenshots, recordings,
            transfers, imports, companion, coordinator);
        CaptureWindow(window, Path.Combine(output, "mini-widget.png"));
    }

    private static void CaptureTransfers(string output, TransferQueueService transfers)
    {
        var window = new TransferQueueWindow(transfers);
        var list = Require<ItemsControl>(window, "JobsList");
        list.ItemsSource = new[]
        {
            new DemoTransfer("holiday-photo.jpg", "На телефон · готово", 100, false, false),
            new DemoTransfer("notes.pdf", "На телефон · передача…", 64, false, true),
            new DemoTransfer("sample-app.apk", "Установка APK · в очереди", 0, true, true)
        };
        Require<FrameworkElement>(window, "EmptyText").Visibility = Visibility.Collapsed;
        CaptureWindow(window, Path.Combine(output, "transfer-queue.png"));
    }

    private static void CaptureSettings(string output, ISettingsService settings, ScreenshotStorage screenshots,
        RecordingStorage recordings, PhotoImportService imports)
    {
        var window = new SettingsWindow(settings, screenshots, recordings, imports);
        Require<TextBlock>(window, "ScreenshotFolderText").Text = @"Pictures\Device Widget";
        CaptureWindow(window, Path.Combine(output, "settings.png"));
    }

    private static void CaptureMediaSettings(string output, ISettingsService settings, RecordingStorage recordings,
        PhotoImportService imports)
    {
        var window = new MediaSettingsWindow(settings, recordings, imports);
        Require<TextBox>(window, "RecordingFolderText").Text = @"Videos\Device Widget";
        Require<TextBox>(window, "PhotoFolderText").Text = @"Pictures\Device Widget Imports";
        CaptureWindow(window, Path.Combine(output, "media-settings.png"));
    }

    private static void CaptureScreenRecording(string output, IAndroidDeviceService devices,
        ISettingsService settings, IDesktopIntegration desktop, RecordingStorage recordings)
    {
        var window = new ScreenRecordingWindow(DemoDevice, devices, settings, desktop, recordings);
        Require<TextBox>(window, "RecordingPathText").Text = @"Videos\Device Widget\Aurora Phone_2026-08-03_12-30-00.mkv";
        CaptureWindow(window, Path.Combine(output, "screen-recording.png"));
    }

    private static void CaptureFiles(string output, IAndroidDeviceService devices, IDesktopIntegration desktop,
        TransferQueueService transfers)
    {
        var window = new RemoteFilesWindow(devices, desktop, transfers, DemoDevice);
        Require<TextBlock>(window, "PathText").Text = "/sdcard/Download";
        Require<ItemsControl>(window, "FilesList").ItemsSource = DemoDeviceService.DownloadEntries
            .Select(entry => new RemoteEntryViewModel(entry)).ToList();
        Require<FrameworkElement>(window, "EmptyState").Visibility = Visibility.Collapsed;
        Require<TextBlock>(window, "StatusText").Text = $"Объектов: {DemoDeviceService.DownloadEntries.Count}";
        CaptureWindow(window, Path.Combine(output, "file-browser.png"));
    }

    private static void CaptureWireless(string output, IAndroidDeviceService devices)
    {
        var window = new WirelessPairingWindow(devices);
        var createQr = typeof(WirelessPairingWindow).GetMethod("CreateQrImage",
            BindingFlags.NonPublic | BindingFlags.Static) ?? throw new MissingMethodException("CreateQrImage");
        var image = (BitmapImage)createQr.Invoke(null,
            ["WIFI:T:ADB;S:DeviceWidget-Demo;P:DEMO1234567890;;"])!;
        Require<Image>(window, "QrImage").Source = image;
        Require<TextBox>(window, "PairEndpointText").Text = "192.0.2.10:37123";
        Require<TextBox>(window, "PairCodeText").Text = "482731";
        Require<TextBox>(window, "ConnectEndpointText").Text = "192.0.2.10:39001";
        Require<TextBlock>(window, "StatusText").Text = "QR готов · отсканируйте его на телефоне";
        CaptureWindow(window, Path.Combine(output, "wireless-debugging.png"));
    }

    private static void CapturePairing(string output, CompanionCoordinator coordinator)
    {
        const string fingerprint = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        const string uri = "awidget://pair?host=192.0.2.10&port=39817&fingerprint=" + fingerprint + "&code=482731";
        var session = new PairingSession(uri, "482731", DateTimeOffset.UtcNow.AddMinutes(5));
        var pairing = new CompanionPairingResult(session, OperationResult.Success(), true);
        var window = new CompanionPairingWindow(DemoDevice.Serial, DemoDevice.DisplayName, pairing, coordinator)
        {
            Height = 450
        };
        CaptureWindow(window, Path.Combine(output, "companion-pairing.png"));
    }

    private static void CaptureWindow(Window window, string path)
    {
        var width = double.IsNaN(window.Width) ? 640 : window.Width;
        var height = double.IsNaN(window.Height) ? 480 : window.Height;
        if (window.Content is not FrameworkElement content)
            throw new InvalidOperationException($"{window.GetType().Name} has no renderable content.");
        CaptureVisual(content, new Size(width, height), path, window.Background);
    }

    private static void CaptureVisual(FrameworkElement visual, Size size, string path, Brush? background = null)
    {
        visual.Measure(size);
        visual.Arrange(new Rect(size));
        visual.UpdateLayout();
        var width = Math.Max(1, (int)Math.Ceiling(size.Width));
        var height = Math.Max(1, (int)Math.Ceiling(size.Height));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        if (background is not null && background != Brushes.Transparent)
        {
            var backdrop = new DrawingVisual();
            using (var context = backdrop.RenderOpen())
                context.DrawRectangle(background, null, new Rect(size));
            bitmap.Render(backdrop);
        }
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static T Require<T>(FrameworkElement root, string name) where T : class =>
        root.FindName(name) as T ?? throw new InvalidOperationException($"Element '{name}' was not found.");

    private static void Invoke(object target, string method, params object?[] arguments)
    {
        var member = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
                     ?? throw new MissingMethodException(target.GetType().Name, method);
        member.Invoke(target, arguments);
    }

    private sealed record DemoTransfer(string Name, string Detail, double ProgressPercent,
        bool IsIndeterminate, bool CanCancel);

    private sealed class DemoSettingsService(AppSettings current) : ISettingsService
    {
        public AppSettings Current { get; private set; } = current;
        public event EventHandler? Changed;
        public void Update(Func<AppSettings, AppSettings> update)
        {
            Current = update(Current);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        public OperationResult SetAutoStart(bool enabled)
        {
            Update(value => value with { AutoStart = enabled });
            return OperationResult.Success();
        }
    }

    private sealed class DemoDeviceService : IAndroidDeviceService
    {
        public static readonly IReadOnlyList<RemoteEntry> DownloadEntries =
        [
            new("Camera", "/sdcard/Download/Camera", true),
            new("Documents", "/sdcard/Download/Documents", true),
            new("holiday-photo.jpg", "/sdcard/Download/holiday-photo.jpg", false),
            new("notes.pdf", "/sdcard/Download/notes.pdf", false),
            new("sample-app.apk", "/sdcard/Download/sample-app.apk", false)
        ];

        public Task<IReadOnlyList<AndroidDevice>> GetDevicesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AndroidDevice>>([DemoDevice]);
        public Task<OperationResult> InstallApkAsync(string serial, string filePath,
            CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Success());
        public Task<OperationResult> PushFileAsync(string serial, string filePath,
            IProgress<double>? progress = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Success());
        public Task<OperationResult> PullFileAsync(string serial, string remotePath, string localPath,
            IProgress<double>? progress = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Success());
        public Task<IReadOnlyList<RemoteEntry>> ListDirectoryAsync(string serial, string remotePath,
            CancellationToken cancellationToken = default) => Task.FromResult(DownloadEntries);
        public Task<OperationResult> TakeScreenshotAsync(string serial, string localPath,
            CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Success());
        public Task<OperationResult> SendTextAsync(string serial, string text,
            CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Success());
        public Task<OperationResult> TogglePowerAsync(string serial,
            CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Success());
        public Task<OperationResult> PairWirelessAsync(string endpoint, string pairingCode,
            CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Success());
        public Task<OperationResult> PairWirelessQrAsync(string serviceName, string password,
            CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Success());
        public Task<OperationResult> ConnectWirelessAsync(string endpoint,
            CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Success());
        public OperationResult StartScreenMirroring(string serial,
            ScrcpyPreset preset = ScrcpyPreset.Balanced) => OperationResult.Success();
        public OperationResult StartScreenRecording(string serial, string localPath,
            ScrcpyPreset preset = ScrcpyPreset.Balanced) => OperationResult.Success();
        public bool IsScreenRecording(string serial) => false;
        public OperationResult StopScreenRecording(string serial) => OperationResult.Success();
        public OperationResult StartShell(string serial) => OperationResult.Success();
    }

    private sealed class DemoDesktopIntegration : IDesktopIntegration
    {
        public OperationResult OpenMtpDevice(AndroidDevice device) => OperationResult.Success();
        public OperationResult OpenFile(string path) => OperationResult.Success();
        public OperationResult OpenFolder(string path) => OperationResult.Success();
        public OperationResult RevealFile(string path) => OperationResult.Success();
    }

    private sealed class DemoCompanionService : ICompanionService
    {
        public bool IsInstallerAvailable => true;
        public Task<CompanionInstallationState> GetInstallationStateAsync(string serial,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CompanionInstallationState.Installed);
        public Task<CompanionInstallResult> InstallAsync(string serial,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CompanionInstallResult.From(OperationResult.Success()));
        public Task<OperationResult> ReinstallAsync(string serial,
            CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Success());
        public Task<OperationResult> LaunchAsync(string serial, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Success());
        public Task<OperationResult> PreparePortReverseAsync(string serial, int port,
            CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Success());
        public Task<OperationResult> OpenPairingAsync(string serial, string pairingUri,
            CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Success());
        public Task<bool?> HasNotificationAccessAsync(string serial,
            CancellationToken cancellationToken = default) => Task.FromResult<bool?>(true);
    }

    private sealed class DemoLogger : IAppLogger
    {
        public string FilePath => "demo.log";
        public void Write(string message) { }
    }
}
