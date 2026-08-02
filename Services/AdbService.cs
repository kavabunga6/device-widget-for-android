using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text.RegularExpressions;
using AndroidWidget.Models;

namespace AndroidWidget.Services;

public sealed class AdbService
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);
    private const string BundledScrcpyVersion = "4.0";
    private const string ScrcpyArchiveResource = "AndroidWidget.Bundled.scrcpy-win64-v4.0.zip";
    private const string ScrcpyLicenseResource = "AndroidWidget.Bundled.scrcpy-LICENSE.txt";
    private readonly Dictionary<string, StaticDeviceDetails> _staticDeviceCache = new(StringComparer.Ordinal);
    private string? _adbExecutable;

    public async Task<IReadOnlyList<AndroidDevice>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(new[] { "devices", "-l" }, cancellationToken, TimeSpan.FromSeconds(8));
        if (!result.IsSuccess)
            throw new InvalidOperationException(ExplainAdbFailure(result));

        var parsed = new List<(string Serial, DeviceConnectionState State, string Model)>();
        foreach (var rawLine in result.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
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

            var model = parts.FirstOrDefault(p => p.StartsWith("model:", StringComparison.OrdinalIgnoreCase))?[6..]
                        ?.Replace('_', ' ') ?? "Android";
            parsed.Add((parts[0], state, model));
        }

        var devices = new List<AndroidDevice>();
        foreach (var item in parsed)
        {
            if (item.State != DeviceConnectionState.Online)
            {
                devices.Add(new AndroidDevice(item.Serial, item.Model, item.Model, "—", null,
                    item.State, IsWirelessSerial(item.Serial)));
                continue;
            }

            var staticTask = GetStaticDetailsAsync(item.Serial, item.Model, cancellationToken);
            var batteryTask = GetBatteryAsync(item.Serial, cancellationToken);
            var powerTask = GetPowerStateAsync(item.Serial, cancellationToken);
            await Task.WhenAll(staticTask, batteryTask, powerTask);

            var details = staticTask.Result;
            var power = powerTask.Result;
            devices.Add(new AndroidDevice(item.Serial, details.DisplayName, details.Model,
                details.AndroidVersion, batteryTask.Result, item.State, IsWirelessSerial(item.Serial),
                power.ScreenOn, power.Locked, details.Manufacturer, details.Brand,
                details.DeviceCode, details.ScreenResolution));
        }

        return devices;
    }

    public Task<CommandResult> InstallApkAsync(string serial, string filePath,
        CancellationToken cancellationToken = default) =>
        RunAsync(new[] { "-s", serial, "install", "-r", filePath }, cancellationToken, TimeSpan.FromMinutes(5));

    public Task<CommandResult> PushFileAsync(string serial, string filePath,
        CancellationToken cancellationToken = default)
    {
        var remotePath = $"/sdcard/Download/{Path.GetFileName(filePath)}";
        return RunAsync(new[] { "-s", serial, "push", filePath, remotePath }, cancellationToken,
            TimeSpan.FromMinutes(10));
    }

    public Task<CommandResult> PullFileAsync(string serial, string remotePath, string localPath,
        CancellationToken cancellationToken = default) =>
        RunAsync(new[] { "-s", serial, "pull", remotePath, localPath }, cancellationToken,
            TimeSpan.FromMinutes(10));

    public async Task<IReadOnlyList<RemoteEntry>> ListDirectoryAsync(string serial, string remotePath,
        CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(new[] { "-s", serial, "shell", "ls", "-1Ap", "--", remotePath },
            cancellationToken, DefaultTimeout);
        if (!result.IsSuccess)
            throw new InvalidOperationException(result.BestMessage);

        return result.StandardOutput
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(name => name is not "." and not ".." && !string.IsNullOrWhiteSpace(name))
            .Select(name => new RemoteEntry(name, CombineRemotePath(remotePath, name.TrimEnd('/')), name.EndsWith('/')))
            .OrderByDescending(entry => entry.IsDirectory)
            .ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public Task<CommandResult> TakeScreenshotAsync(string serial, string localPath,
        CancellationToken cancellationToken = default) =>
        RunBinaryToFileAsync(ResolveAdbExecutable(),
            new[] { "-s", serial, "exec-out", "screencap", "-p" }, localPath,
            cancellationToken, TimeSpan.FromSeconds(30));

    public Task<CommandResult> SendTextAsync(string serial, string text,
        CancellationToken cancellationToken = default)
    {
        var inputText = EscapeInputText(text);
        return RunAsync(new[] { "-s", serial, "shell", "input", "text", inputText }, cancellationToken,
            DefaultTimeout);
    }

    public Task<CommandResult> TogglePowerAsync(string serial, CancellationToken cancellationToken = default) =>
        RunAsync(new[] { "-s", serial, "shell", "input", "keyevent", "26" }, cancellationToken, DefaultTimeout);

    public Task<CommandResult> RunAsync(IEnumerable<string> arguments,
        CancellationToken cancellationToken = default, TimeSpan? timeout = null) =>
        RunProcessAsync(ResolveAdbExecutable(), arguments, cancellationToken, timeout ?? DefaultTimeout);

    public bool TryStartScrcpy(string serial, out string? error)
    {
        var candidates = new List<string>();
        var bundledError = string.Empty;
        var bundled = PrepareBundledScrcpy(out bundledError);
        if (!string.IsNullOrWhiteSpace(bundled))
            candidates.Add(bundled);
        candidates.AddRange(new[]
        {
            Path.Combine(AppContext.BaseDirectory, "tools", "scrcpy", "scrcpy.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "WinGet", "Links", "scrcpy.exe"),
            "scrcpy.exe"
        });

        foreach (var candidate in candidates)
        {
            if (Path.IsPathRooted(candidate) && !File.Exists(candidate))
                continue;

            try
            {
                var info = new ProcessStartInfo(candidate)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.IsPathRooted(candidate)
                        ? Path.GetDirectoryName(candidate)!
                        : AppContext.BaseDirectory
                };
                info.ArgumentList.Add("--serial");
                info.ArgumentList.Add(serial);
                info.ArgumentList.Add("--window-title");
                info.ArgumentList.Add("Android Widget · Screen");
                Process.Start(info);
                error = null;
                return true;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
            {
                error = ex.Message;
            }
        }

        error = string.IsNullOrWhiteSpace(bundledError)
            ? "Не удалось запустить встроенный scrcpy."
            : $"Не удалось подготовить встроенный scrcpy: {bundledError}";
        return false;
    }

    internal static string? PrepareBundledScrcpy(out string? error)
    {
        var cacheRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AndroidWidget", "scrcpy", $"v{BundledScrcpyVersion}");
        try
        {
            if (Directory.Exists(cacheRoot))
            {
                var cached = Directory.EnumerateFiles(cacheRoot, "scrcpy.exe", SearchOption.AllDirectories)
                    .FirstOrDefault();
                if (cached is not null && File.Exists(Path.Combine(Path.GetDirectoryName(cached)!, "scrcpy-server")))
                {
                    error = null;
                    return cached;
                }
            }

            Directory.CreateDirectory(cacheRoot);
            var assembly = Assembly.GetExecutingAssembly();
            using var archiveStream = assembly.GetManifestResourceStream(ScrcpyArchiveResource)
                                      ?? throw new InvalidOperationException("Ресурс scrcpy отсутствует в сборке.");
            ExtractArchive(archiveStream, cacheRoot);

            using var licenseStream = assembly.GetManifestResourceStream(ScrcpyLicenseResource);
            if (licenseStream is not null)
            {
                using var licenseFile = File.Create(Path.Combine(cacheRoot, "LICENSE-scrcpy.txt"));
                licenseStream.CopyTo(licenseFile);
            }

            var executable = Directory.EnumerateFiles(cacheRoot, "scrcpy.exe", SearchOption.AllDirectories)
                .FirstOrDefault() ?? throw new InvalidOperationException("В архиве не найден scrcpy.exe.");
            if (!File.Exists(Path.Combine(Path.GetDirectoryName(executable)!, "scrcpy-server")))
                throw new InvalidOperationException("В архиве не найден scrcpy-server.");
            error = null;
            return executable;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    private static void ExtractArchive(Stream archiveStream, string destinationRoot)
    {
        var normalizedRoot = Path.GetFullPath(destinationRoot)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: false);
        foreach (var entry in archive.Entries)
        {
            var destination = Path.GetFullPath(Path.Combine(destinationRoot, entry.FullName));
            if (!destination.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Архив scrcpy содержит небезопасный путь.");
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destination);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: true);
        }
    }

    public void StartShell(string serial)
    {
        if (!Regex.IsMatch(serial, "^[a-zA-Z0-9._:-]+$"))
            throw new InvalidOperationException("Некорректный серийный номер устройства.");

        var adb = ResolveAdbExecutable();
        var info = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = true
        };
        info.ArgumentList.Add("/k");
        info.ArgumentList.Add($"\"{adb}\" -s {serial} shell");
        Process.Start(info);
    }

    private string ResolveAdbExecutable()
    {
        if (!string.IsNullOrWhiteSpace(_adbExecutable) &&
            (!Path.IsPathRooted(_adbExecutable) || File.Exists(_adbExecutable)))
            return _adbExecutable;

        var scrcpy = PrepareBundledScrcpy(out _);
        if (!string.IsNullOrWhiteSpace(scrcpy))
        {
            var bundledAdb = Path.Combine(Path.GetDirectoryName(scrcpy)!, "adb.exe");
            if (File.Exists(bundledAdb))
                return _adbExecutable = bundledAdb;
        }
        return _adbExecutable = "adb";
    }

    private async Task<StaticDeviceDetails> GetStaticDetailsAsync(string serial, string adbModel,
        CancellationToken cancellationToken)
    {
        if (_staticDeviceCache.TryGetValue(serial, out var cached))
            return cached;

        var propertiesTask = RunAsync(new[] { "-s", serial, "shell", "getprop" },
            cancellationToken, DefaultTimeout);
        var sizeTask = RunAsync(new[] { "-s", serial, "shell", "wm", "size" },
            cancellationToken, DefaultTimeout);
        await Task.WhenAll(propertiesTask, sizeTask);

        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches(propertiesTask.Result.StandardOutput,
                     @"^\[([^]]+)\]:\s*\[(.*)\]\s*$", RegexOptions.Multiline))
            properties[match.Groups[1].Value] = match.Groups[2].Value.Trim();

        string Property(params string[] names)
        {
            foreach (var name in names)
                if (properties.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
                    return value;
            return string.Empty;
        }

        var model = FirstNonEmpty(Property("ro.product.model", "ro.product.system.model"), adbModel, "Android");
        var marketName = Property("ro.product.marketname", "ro.product.odm.marketname",
            "ro.oplus.market.name", "ro.vendor.oplus.market.name", "ro.config.marketing_name");
        var sizeMatch = Regex.Match(sizeTask.Result.StandardOutput,
            @"Physical size:\s*(\d+x\d+)", RegexOptions.IgnoreCase);
        if (!sizeMatch.Success)
            sizeMatch = Regex.Match(sizeTask.Result.StandardOutput, @"\b(\d+x\d+)\b", RegexOptions.IgnoreCase);

        var details = new StaticDeviceDetails(
            FirstNonEmpty(marketName, model),
            model,
            FirstNonEmpty(Property("ro.build.version.release"), "—"),
            FirstNonEmpty(Property("ro.product.manufacturer"), "Android"),
            FirstNonEmpty(Property("ro.product.brand"), "Android"),
            Property("ro.product.device", "ro.product.vendor.device"),
            sizeMatch.Success ? sizeMatch.Groups[1].Value : string.Empty);
        _staticDeviceCache[serial] = details;
        return details;
    }

    private async Task<int?> GetBatteryAsync(string serial, CancellationToken cancellationToken)
    {
        var result = await RunAsync(new[] { "-s", serial, "shell", "dumpsys", "battery" },
            cancellationToken, DefaultTimeout);
        var match = Regex.Match(result.StandardOutput, @"(?:^|\n)\s*level:\s*(\d+)");
        return match.Success && int.TryParse(match.Groups[1].Value, out var value) ? value : null;
    }

    private async Task<(bool ScreenOn, bool Locked)> GetPowerStateAsync(string serial,
        CancellationToken cancellationToken)
    {
        var powerTask = RunAsync(new[] { "-s", serial, "shell", "dumpsys", "power" },
            cancellationToken, DefaultTimeout);
        var windowTask = RunAsync(new[] { "-s", serial, "shell", "dumpsys", "window", "policy" },
            cancellationToken, DefaultTimeout);
        await Task.WhenAll(powerTask, windowTask);

        var power = powerTask.Result.StandardOutput;
        var window = windowTask.Result.StandardOutput;
        var wakefulness = Regex.Match(power, @"^\s*mWakefulness=(\w+)",
            RegexOptions.IgnoreCase | RegexOptions.Multiline);
        var screenOn = wakefulness.Success
            ? wakefulness.Groups[1].Value.Equals("Awake", StringComparison.OrdinalIgnoreCase)
            : !Regex.IsMatch(power,
                @"Display Power:\s*state=OFF|mInteractive=false|mScreenOn=false",
                RegexOptions.IgnoreCase);
        var locked = Regex.IsMatch(window,
            @"mShowingLockscreen=true|mKeyguardShowing=true|isStatusBarKeyguard=true|keyguardShowing=true|mInputRestricted=true",
            RegexOptions.IgnoreCase);
        return (screenOn, locked);
    }

    private static async Task<CommandResult> RunProcessAsync(string fileName, IEnumerable<string> arguments,
        CancellationToken cancellationToken, TimeSpan timeout)
    {
        var info = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = info };
        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            return new CommandResult(-1, string.Empty,
                $"ADB не найден. Установите Android Platform Tools и добавьте adb в PATH. ({ex.Message})");
        }

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch { /* already exited */ }
            if (cancellationToken.IsCancellationRequested)
                throw;
            return new CommandResult(-2, await outputTask, "Команда ADB превысила время ожидания.");
        }

        return new CommandResult(process.ExitCode, await outputTask, await errorTask);
    }

    private static async Task<CommandResult> RunBinaryToFileAsync(string fileName,
        IEnumerable<string> arguments, string outputPath, CancellationToken cancellationToken, TimeSpan timeout)
    {
        var info = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = info };
        try { process.Start(); }
        catch (System.ComponentModel.Win32Exception ex)
        {
            return new CommandResult(-1, string.Empty, $"ADB не найден: {ex.Message}");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await using var file = File.Create(outputPath);
        var copyTask = process.StandardOutput.BaseStream.CopyToAsync(file, cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
            await copyTask;
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch { }
            if (cancellationToken.IsCancellationRequested)
                throw;
            return new CommandResult(-2, string.Empty, "Снимок экрана превысил время ожидания.");
        }

        return new CommandResult(process.ExitCode, outputPath, await errorTask);
    }

    private static bool IsWirelessSerial(string serial) => serial.Contains(':');
    private static string CleanProperty(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    private static string CombineRemotePath(string parent, string child) =>
        $"{parent.TrimEnd('/')}/{child}";

    private static string EscapeInputText(string value)
    {
        // `adb shell` joins its trailing arguments into a remote shell command. Escape shell
        // metacharacters so clipboard content can never turn into a second command.
        const string shellMetacharacters = "\\\"'&|;<>()$`!*?[]{}#~";
        var builder = new System.Text.StringBuilder(value.Length * 2);
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
                if (shellMetacharacters.Contains(character))
                    builder.Append('\\');
                builder.Append(character);
            }
        }
        return builder.ToString();
    }

    private static string ExplainAdbFailure(CommandResult result) =>
        string.IsNullOrWhiteSpace(result.BestMessage) ? "Не удалось получить список устройств ADB." : result.BestMessage;

    private sealed record StaticDeviceDetails(string DisplayName, string Model, string AndroidVersion,
        string Manufacturer, string Brand, string DeviceCode, string ScreenResolution);
}
