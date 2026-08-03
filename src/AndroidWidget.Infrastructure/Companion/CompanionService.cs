using AndroidWidget.Core.Abstractions;
using AndroidWidget.Core.Devices;
using AndroidWidget.Core.Operations;
using AndroidWidget.Infrastructure.Adb;
using System.Text.RegularExpressions;

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
        var installed = result.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Any(line => line.TrimStart().StartsWith("package:", StringComparison.Ordinal));
        if (!installed)
            return CompanionInstallationState.NotInstalled;
        if (!IsInstallerAvailable)
            return CompanionInstallationState.Installed;

        var version = await _commands.RunAsync(new[]
        {
            "-s", serial, "shell", "dumpsys", "package", PackageName
        }, cancellationToken, QueryTimeout);
        if (!version.IsSuccess)
            return CompanionInstallationState.Installed;
        return DetermineInstalledState(version.StandardOutput, _package.VersionCode);
    }

    public async Task<CompanionInstallResult> InstallAsync(string serial,
        CancellationToken cancellationToken = default)
    {
        if (!IsInstallerAvailable)
            return CompanionInstallResult.From(OperationResult.Failure(
                "APK компаньона не входит в эту desktop-сборку. Соберите companion-android перед публикацией."));
        if (await GetInstallationStateAsync(serial, cancellationToken) == CompanionInstallationState.Installed)
            return CompanionInstallResult.From(await LaunchAsync(serial, cancellationToken));

        var packagePath = ExtractPackage();
        if (!packagePath.IsSuccess)
            return CompanionInstallResult.From(packagePath);
        var install = await _commands.RunAsync(new[] { "-s", serial, "install", "-r", packagePath.StandardOutput },
            cancellationToken, TimeSpan.FromMinutes(5));
        if (!install.IsSuccess)
            return new CompanionInstallResult(install, IsSignatureMismatch(install)
                ? CompanionInstallFailureKind.SignatureMismatch
                : CompanionInstallFailureKind.Other);
        return CompanionInstallResult.From(await LaunchAsync(serial, cancellationToken));
    }

    public async Task<OperationResult> ReinstallAsync(string serial, CancellationToken cancellationToken = default)
    {
        if (!IsInstallerAvailable)
            return OperationResult.Failure("APK компаньона не входит в эту desktop-сборку.");
        var packagePath = ExtractPackage();
        if (!packagePath.IsSuccess)
            return packagePath;
        var uninstall = await _commands.RunAsync(new[] { "-s", serial, "uninstall", PackageName },
            cancellationToken, TimeSpan.FromMinutes(2));
        if (!uninstall.IsSuccess)
            return uninstall;
        var install = await _commands.RunAsync(new[] { "-s", serial, "install", packagePath.StandardOutput },
            cancellationToken, TimeSpan.FromMinutes(5));
        return install.IsSuccess ? await LaunchAsync(serial, cancellationToken) : install;
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

    private OperationResult ExtractPackage()
    {
        try
        {
            return OperationResult.Success(_package.Extract());
        }
        catch (Exception ex)
        {
            return OperationResult.Failure(ex.Message);
        }
    }

    private static bool IsSignatureMismatch(OperationResult result) =>
        result.BestMessage.Contains("INSTALL_FAILED_UPDATE_INCOMPATIBLE", StringComparison.OrdinalIgnoreCase) ||
        result.BestMessage.Contains("signatures do not match", StringComparison.OrdinalIgnoreCase) ||
        result.BestMessage.Contains("signature mismatch", StringComparison.OrdinalIgnoreCase);

    internal static CompanionInstallationState DetermineInstalledState(string packageDump, int bundledVersionCode)
    {
        var match = Regex.Match(packageDump, @"\bversionCode=(\d+)\b", RegexOptions.CultureInvariant);
        return match.Success && int.TryParse(match.Groups[1].Value, out var installedVersion) &&
               installedVersion < bundledVersionCode
            ? CompanionInstallationState.UpdateAvailable
            : CompanionInstallationState.Installed;
    }

    internal static bool VerifyVersionDetection() =>
        DetermineInstalledState("versionCode=2 minSdk=26", 3) == CompanionInstallationState.UpdateAvailable &&
        DetermineInstalledState("versionCode=3 minSdk=26", 3) == CompanionInstallationState.Installed &&
        DetermineInstalledState("package dump unavailable", 3) == CompanionInstallationState.Installed &&
        IsSignatureMismatch(OperationResult.Failure(
            "Failure [INSTALL_FAILED_UPDATE_INCOMPATIBLE: Package signatures do not match]")) &&
        !IsSignatureMismatch(OperationResult.Failure("Failure [INSTALL_FAILED_VERSION_DOWNGRADE]"));
}
