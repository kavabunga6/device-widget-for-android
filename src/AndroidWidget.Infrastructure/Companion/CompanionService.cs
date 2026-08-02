using AndroidWidget.Core.Abstractions;
using AndroidWidget.Core.Devices;
using AndroidWidget.Core.Operations;
using AndroidWidget.Infrastructure.Adb;

namespace AndroidWidget.Infrastructure.Companion;

public sealed class CompanionService : ICompanionService
{
    public const string PackageName = "dev.androidwidget.companion";
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(8);
    private readonly AdbCommandRunner _commands;
    private readonly CompanionPackageProvider _package = new();

    public CompanionService(AdbCommandRunner commands) => _commands = commands;

    public bool IsInstallerAvailable => _package.IsAvailable;

    public async Task<CompanionInstallationState> GetInstallationStateAsync(string serial,
        CancellationToken cancellationToken = default)
    {
        var result = await _commands.RunAsync(new[] { "-s", serial, "shell", "pm", "path", PackageName },
            cancellationToken, QueryTimeout);
        if (!result.IsSuccess)
            return CompanionInstallationState.Unknown;
        return result.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Any(line => line.TrimStart().StartsWith("package:", StringComparison.Ordinal))
            ? CompanionInstallationState.Installed
            : CompanionInstallationState.NotInstalled;
    }

    public async Task<OperationResult> InstallAsync(string serial, CancellationToken cancellationToken = default)
    {
        if (!IsInstallerAvailable)
            return OperationResult.Failure(
                "APK компаньона не входит в эту desktop-сборку. Соберите companion-android перед публикацией.");
        if (await GetInstallationStateAsync(serial, cancellationToken) == CompanionInstallationState.Installed)
            return await LaunchAsync(serial, cancellationToken);

        string packagePath;
        try
        {
            packagePath = _package.Extract();
        }
        catch (Exception ex)
        {
            return OperationResult.Failure(ex.Message);
        }
        var install = await _commands.RunAsync(new[] { "-s", serial, "install", "-r", packagePath },
            cancellationToken, TimeSpan.FromMinutes(5));
        if (!install.IsSuccess)
            return install;
        return await LaunchAsync(serial, cancellationToken);
    }

    public Task<OperationResult> LaunchAsync(string serial, CancellationToken cancellationToken = default) =>
        _commands.RunAsync(new[]
        {
            "-s", serial, "shell", "am", "start", "-W", "-n", $"{PackageName}/.MainActivity"
        }, cancellationToken, QueryTimeout);

    public Task<OperationResult> PreparePortReverseAsync(string serial, int port,
        CancellationToken cancellationToken = default) =>
        _commands.RunAsync(new[] { "-s", serial, "reverse", $"tcp:{port}", $"tcp:{port}" },
            cancellationToken, QueryTimeout);

    public Task<OperationResult> OpenPairingAsync(string serial, string pairingUri,
        CancellationToken cancellationToken = default) =>
        _commands.RunAsync(new[]
        {
            "-s", serial, "shell", "am", "start", "-W", "-a", "android.intent.action.VIEW", "-d",
            QuoteForRemoteShell(pairingUri), "-n", $"{PackageName}/.MainActivity"
        }, cancellationToken, QueryTimeout);

    public async Task<bool?> HasNotificationAccessAsync(string serial,
        CancellationToken cancellationToken = default)
    {
        var result = await _commands.RunAsync(new[]
        {
            "-s", serial, "shell", "settings", "get", "secure", "enabled_notification_listeners"
        }, cancellationToken, QueryTimeout);
        return result.IsSuccess
            ? result.StandardOutput.Contains(PackageName, StringComparison.OrdinalIgnoreCase)
            : null;
    }

    private static string QuoteForRemoteShell(string value) => $"'{value.Replace("'", "'\\''")}'";
}
