using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using AndroidWidget.Core.Abstractions;
using AndroidWidget.Core.Devices;
using AndroidWidget.Core.Files;
using AndroidWidget.Core.Operations;
using AndroidWidget.Infrastructure.Scrcpy;

namespace AndroidWidget.Infrastructure.Adb;

public sealed class AndroidDeviceService : IAndroidDeviceService
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);
    private readonly AdbCommandRunner _commands;
    private readonly DeviceSnapshotReader _snapshotReader;
    private readonly ScreenMirroringService _screenMirroring;
    private readonly ISettingsService _settings;

    public AndroidDeviceService(AdbCommandRunner commands, DeviceSnapshotReader snapshotReader,
        ScreenMirroringService screenMirroring, ISettingsService settings)
    {
        _commands = commands;
        _snapshotReader = snapshotReader;
        _screenMirroring = screenMirroring;
        _settings = settings;
    }

    public async Task<IReadOnlyList<AndroidDevice>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        var result = await _commands.RunAsync(new[] { "devices", "-l" }, cancellationToken,
            TimeSpan.FromSeconds(8));
        if (!result.IsSuccess)
            throw new InvalidOperationException(result.BestMessage);

        var discovered = ParseDeviceList(result.StandardOutput);
        var devices = new List<AndroidDevice>(discovered.Count);
        foreach (var item in discovered)
        {
            if (item.State != DeviceConnectionState.Online)
            {
                devices.Add(new AndroidDevice(item.Serial, item.Model, item.Model, "—", null,
                    item.State, IsWirelessSerial(item.Serial)));
                continue;
            }

            var snapshot = await _snapshotReader.ReadAsync(item.Serial, item.Model,
                _settings.Current.ShowSmsBubbles, cancellationToken);
            devices.Add(new AndroidDevice(item.Serial, snapshot.DisplayName, snapshot.Model,
                snapshot.AndroidVersion, snapshot.BatteryPercent, item.State, IsWirelessSerial(item.Serial),
                snapshot.ScreenOn, snapshot.Locked, snapshot.Manufacturer, snapshot.Brand, snapshot.DeviceCode,
                snapshot.ScreenResolution, snapshot.LatestMessage, snapshot.CompanionState));
        }
        return devices;
    }

    public Task<OperationResult> InstallApkAsync(string serial, string filePath,
        CancellationToken cancellationToken = default) =>
        _commands.RunAsync(new[] { "-s", serial, "install", "-r", filePath }, cancellationToken,
            TimeSpan.FromMinutes(5));

    public Task<OperationResult> PushFileAsync(string serial, string filePath,
        CancellationToken cancellationToken = default) =>
        _commands.RunAsync(new[] { "-s", serial, "push", filePath, $"/sdcard/Download/{Path.GetFileName(filePath)}" },
            cancellationToken, TimeSpan.FromMinutes(10));

    public Task<OperationResult> PullFileAsync(string serial, string remotePath, string localPath,
        CancellationToken cancellationToken = default) =>
        _commands.RunAsync(new[] { "-s", serial, "pull", remotePath, localPath }, cancellationToken,
            TimeSpan.FromMinutes(10));

    public async Task<IReadOnlyList<RemoteEntry>> ListDirectoryAsync(string serial, string remotePath,
        CancellationToken cancellationToken = default)
    {
        var result = await _commands.RunAsync(new[] { "-s", serial, "shell", "ls", "-1Ap", "--", remotePath },
            cancellationToken, DefaultTimeout);
        if (!result.IsSuccess)
            throw new InvalidOperationException(result.BestMessage);
        return result.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(name => name is not "." and not ".." && !string.IsNullOrWhiteSpace(name))
            .Select(name => new RemoteEntry(name, CombineRemotePath(remotePath, name.TrimEnd('/')), name.EndsWith('/')))
            .OrderByDescending(entry => entry.IsDirectory)
            .ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public Task<OperationResult> TakeScreenshotAsync(string serial, string localPath,
        CancellationToken cancellationToken = default) =>
        _commands.RunBinaryToFileAsync(new[] { "-s", serial, "exec-out", "screencap", "-p" }, localPath,
            cancellationToken, TimeSpan.FromSeconds(30));

    public Task<OperationResult> SendTextAsync(string serial, string text,
        CancellationToken cancellationToken = default) =>
        _commands.RunAsync(new[] { "-s", serial, "shell", "input", "text", EscapeInputText(text) },
            cancellationToken, DefaultTimeout);

    public Task<OperationResult> TogglePowerAsync(string serial, CancellationToken cancellationToken = default) =>
        _commands.RunAsync(new[] { "-s", serial, "shell", "input", "keyevent", "26" }, cancellationToken,
            DefaultTimeout);

    public OperationResult StartScreenMirroring(string serial) => _screenMirroring.Start(serial);

    public OperationResult StartShell(string serial)
    {
        if (!Regex.IsMatch(serial, "^[a-zA-Z0-9._:-]+$"))
            return OperationResult.Failure("Некорректный серийный номер устройства.");
        try
        {
            var adbPath = Path.GetFullPath(_commands.ExecutablePath);
            if (adbPath.Contains('"'))
                return OperationResult.Failure("Путь к ADB содержит недопустимую кавычку.");

            // cmd.exe has its own quoting rules. Passing the whole command through
            // ArgumentList makes .NET escape the inner quotes as literal \" characters.
            var info = new ProcessStartInfo("cmd.exe")
            {
                UseShellExecute = true,
                Arguments = $"/d /k \"\"{adbPath}\" -s {serial} shell\""
            };
            Process.Start(info);
            return OperationResult.Success();
        }
        catch (Exception ex)
        {
            return OperationResult.Failure(ex.Message);
        }
    }

    private static List<DiscoveredDevice> ParseDeviceList(string output)
    {
        var devices = new List<DiscoveredDevice>();
        foreach (var rawLine in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase) || line.StartsWith('*'))
                continue;
            var parts = Regex.Split(line, "\\s+");
            if (parts.Length < 2)
                continue;
            var state = parts[1] switch
            {
                "device" => DeviceConnectionState.Online,
                "offline" => DeviceConnectionState.Offline,
                "unauthorized" => DeviceConnectionState.Unauthorized,
                _ => DeviceConnectionState.Unknown
            };
            var model = parts.FirstOrDefault(part => part.StartsWith("model:", StringComparison.OrdinalIgnoreCase))?[6..]
                ?.Replace('_', ' ') ?? "Android";
            devices.Add(new DiscoveredDevice(parts[0], state, model));
        }
        return devices;
    }

    private static string EscapeInputText(string value)
    {
        const string metacharacters = "\\\"'&|;<>()$`!*?[]{}#~";
        var builder = new StringBuilder(value.Length * 2);
        foreach (var character in value)
        {
            if (character == '\r')
                continue;
            if (character is ' ' or '\n' or '\t')
            {
                builder.Append("%s");
                continue;
            }
            if (character == '%')
                builder.Append("\\%");
            else
            {
                if (metacharacters.Contains(character))
                    builder.Append('\\');
                builder.Append(character);
            }
        }
        return builder.ToString();
    }

    private static bool IsWirelessSerial(string serial) => serial.Contains(':');
    private static string CombineRemotePath(string parent, string child) => $"{parent.TrimEnd('/')}/{child}";

    private sealed record DiscoveredDevice(string Serial, DeviceConnectionState State, string Model);
}
