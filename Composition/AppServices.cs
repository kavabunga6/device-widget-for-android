using AndroidWidget.CompanionHost;
using AndroidWidget.Infrastructure.Adb;
using AndroidWidget.Infrastructure.Companion;
using AndroidWidget.Infrastructure.Diagnostics;
using AndroidWidget.Infrastructure.Scrcpy;
using AndroidWidget.Infrastructure.Settings;
using AndroidWidget.Infrastructure.Windows;
using AndroidWidget.Presentation.Screenshots;
using AndroidWidget.Services;

namespace AndroidWidget.Composition;

public sealed class AppServices
{
    private AppServices(IAndroidDeviceService devices, ISettingsService settings,
        IDesktopIntegration desktop, IAppLogger logger, IDiagnosticsVerifier diagnostics,
        ScreenshotStorage screenshots, ICompanionService companion, CompanionCoordinator companionCoordinator)
    {
        Devices = devices;
        Settings = settings;
        Desktop = desktop;
        Logger = logger;
        Diagnostics = diagnostics;
        Screenshots = screenshots;
        Companion = companion;
        CompanionCoordinator = companionCoordinator;
    }

    public IAndroidDeviceService Devices { get; }
    public ISettingsService Settings { get; }
    public IDesktopIntegration Desktop { get; }
    public IAppLogger Logger { get; }
    public IDiagnosticsVerifier Diagnostics { get; }
    public ScreenshotStorage Screenshots { get; }
    public ICompanionService Companion { get; }
    public CompanionCoordinator CompanionCoordinator { get; }

    public static AppServices Create()
    {
        var settings = new JsonSettingsService();
        var logger = new FileAppLogger();
        var bundle = new ScrcpyBundleManager();
        var executable = new AdbExecutableProvider(bundle);
        var commands = new AdbCommandRunner(executable);
        var companion = new CompanionService(commands);
        var messages = new SmsNotificationReader(commands);
        var snapshots = new DeviceSnapshotReader(commands, messages, companion);
        var mirroring = new ScreenMirroringService(bundle);
        var devices = new AndroidDeviceService(commands, snapshots, mirroring, settings);
        var companionDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AndroidWidget", "companion-v1");
        var host = new CompanionHostService(new CompanionHostOptions(companionDataDirectory));
        var companionCoordinator = new CompanionCoordinator(host, companion, logger);
        return new AppServices(devices, settings, new WindowsDesktopIntegration(),
            logger, new DiagnosticsVerifier(bundle), new ScreenshotStorage(settings), companion,
            companionCoordinator);
    }
}
