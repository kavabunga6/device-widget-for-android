using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace AndroidWidget.Desktop;

internal enum DesktopCompanionState { Unavailable, NotInstalled, Installed, UpdateAvailable, Unknown }

internal sealed class DesktopCompanionInstaller(PortableAdbService adb)
{
    private const string PackageName = "dev.androidwidget.companion";
    private const string ApkResource = "AndroidWidget.Desktop.Bundled.DeviceWidget-Companion.apk";
    private const string VersionResource = "AndroidWidget.Desktop.Bundled.companion-version.properties";

    public bool IsAvailable => typeof(DesktopCompanionInstaller).Assembly.GetManifestResourceInfo(ApkResource) is not null;

    public async Task<DesktopCompanionState> GetStateAsync(string serial, CancellationToken token)
    {
        var path = await adb.RunDeviceAsync(serial, ["shell", "pm", "path", PackageName], token);
        if (!path.IsSuccess || !path.Output.Contains("package:", StringComparison.Ordinal))
            return DesktopCompanionState.NotInstalled;
        if (!IsAvailable)
            return DesktopCompanionState.Installed;
        var dump = await adb.RunDeviceAsync(serial, ["shell", "dumpsys", "package", PackageName], token);
        if (!dump.IsSuccess)
            return DesktopCompanionState.Unknown;
        var match = Regex.Match(dump.Output, @"\bversionCode=(\d+)\b", RegexOptions.CultureInvariant);
        return match.Success && int.TryParse(match.Groups[1].Value, out var installed) && installed < ReadVersionCode()
            ? DesktopCompanionState.UpdateAvailable
            : DesktopCompanionState.Installed;
    }

    public async Task<PortableCommandResult> InstallOrUpdateAsync(string serial, CancellationToken token)
    {
        if (!IsAvailable)
            return new PortableCommandResult(1, "", "APK компаньона не входит в эту сборку.");
        var install = await adb.InstallAsync(serial, ExtractApk(), token);
        return install.IsSuccess ? await LaunchAsync(serial, token) : install;
    }

    public Task<PortableCommandResult> LaunchAsync(string serial, CancellationToken token) =>
        adb.RunDeviceAsync(serial, ["shell", "am", "start", "-W", "-n", $"{PackageName}/.MainActivity"], token);

    public Task<PortableCommandResult> PrepareReverseAsync(string serial, int port, CancellationToken token) =>
        adb.RunDeviceAsync(serial, ["reverse", $"tcp:{port}", $"tcp:{port}"], token);

    public Task<PortableCommandResult> OpenPairingAsync(string serial, string uri, CancellationToken token) =>
        adb.RunDeviceAsync(serial,
            ["shell", "am", "start", "-W", "-a", "android.intent.action.VIEW", "-d", uri,
                "-n", $"{PackageName}/.MainActivity"], token);

    private static string ExtractApk()
    {
        using var resource = typeof(DesktopCompanionInstaller).Assembly.GetManifestResourceStream(ApkResource)
            ?? throw new InvalidOperationException("APK компаньона отсутствует.");
        using var memory = new MemoryStream();
        resource.CopyTo(memory);
        var bytes = memory.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()[..16];
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DeviceWidget", "companion");
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, $"DeviceWidget-Companion-{hash}.apk");
        if (!File.Exists(target))
            File.WriteAllBytes(target, bytes);
        return target;
    }

    private static int ReadVersionCode()
    {
        using var resource = typeof(DesktopCompanionInstaller).Assembly.GetManifestResourceStream(VersionResource)
            ?? throw new InvalidOperationException("Метаданные версии компаньона отсутствуют.");
        using var reader = new StreamReader(resource);
        while (reader.ReadLine() is { } line)
            if (line.StartsWith("VERSION_CODE=", StringComparison.Ordinal) &&
                int.TryParse(line["VERSION_CODE=".Length..], out var code))
                return code;
        throw new InvalidOperationException("Некорректная версия компаньона.");
    }
}
