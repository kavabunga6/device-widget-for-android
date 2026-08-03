using System.Diagnostics;
using System.Text.RegularExpressions;

namespace AndroidWidget.Desktop;

internal sealed record PortableAdbDevice(string Serial, string Name, string Manufacturer,
    string AndroidVersion, int? BatteryPercent, bool Wireless);
internal sealed record PortableCommandResult(int ExitCode, string Output, string Error)
{
    public bool IsSuccess => ExitCode == 0;
    public string Message => string.IsNullOrWhiteSpace(Error) ? Output.Trim() : Error.Trim();
}

internal sealed class PortableAdbService
{
    private readonly Dictionary<string, Process> _recordings = new(StringComparer.Ordinal);

    public async Task<IReadOnlyList<PortableAdbDevice>> GetDevicesAsync(CancellationToken cancellationToken)
    {
        var result = await RunAsync("adb", ["devices", "-l"], cancellationToken, TimeSpan.FromSeconds(10));
        if (!result.IsSuccess)
            throw new InvalidOperationException(result.Message);
        var devices = result.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim()).Where(line => !line.StartsWith("List of devices") && !line.StartsWith('*'))
            .Select(ParseDevice).Where(device => device is not null).Cast<PortableAdbDevice>().ToList();
        return await Task.WhenAll(devices.Select(device => EnrichAsync(device, cancellationToken)));
    }

    public Task<PortableCommandResult> PushAsync(string serial, string localPath, CancellationToken token) =>
        RunAsync("adb", ["-s", serial, "push", localPath,
            $"/sdcard/Download/{Path.GetFileName(localPath)}"], token, TimeSpan.FromMinutes(10));

    public Task<PortableCommandResult> PairAsync(string endpoint, string code, CancellationToken token) =>
        RunAsync("adb", ["pair", endpoint, code], token, TimeSpan.FromSeconds(45));

    public Task<PortableCommandResult> ConnectAsync(string endpoint, CancellationToken token) =>
        RunAsync("adb", ["connect", endpoint], token, TimeSpan.FromSeconds(30));

    public Task<PortableCommandResult> ScreenshotAsync(string serial, string outputPath, CancellationToken token) =>
        RunBinaryAsync("adb", ["-s", serial, "exec-out", "screencap", "-p"], outputPath, token,
            TimeSpan.FromSeconds(30));

    public Task<PortableCommandResult> InstallAsync(string serial, string apkPath, CancellationToken token) =>
        RunAsync("adb", ["-s", serial, "install", "-r", apkPath], token, TimeSpan.FromMinutes(10));

    public Task<PortableCommandResult> SendTextAsync(string serial, string text, CancellationToken token)
    {
        var escaped = text.Replace("%", "%25", StringComparison.Ordinal)
            .Replace(" ", "%s", StringComparison.Ordinal)
            .Replace("&", "\\&", StringComparison.Ordinal)
            .Replace("<", "\\<", StringComparison.Ordinal)
            .Replace(">", "\\>", StringComparison.Ordinal);
        return RunAsync("adb", ["-s", serial, "shell", "input", "text", escaped], token,
            TimeSpan.FromSeconds(30));
    }

    public Task<PortableCommandResult> TogglePowerAsync(string serial, CancellationToken token) =>
        RunAsync("adb", ["-s", serial, "shell", "input", "keyevent", "26"], token,
            TimeSpan.FromSeconds(15));

    public PortableCommandResult StartShell(string serial)
    {
        if (!IsValidSerial(serial))
            return new PortableCommandResult(1, "", "Некорректный serial устройства.");
        try
        {
            ProcessStartInfo info;
            if (OperatingSystem.IsWindows())
            {
                info = new ProcessStartInfo("cmd.exe") { UseShellExecute = true };
                info.ArgumentList.Add("/k");
                info.ArgumentList.Add($"adb -s {serial} shell");
            }
            else if (OperatingSystem.IsMacOS())
            {
                info = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false };
                info.ArgumentList.Add("-a");
                info.ArgumentList.Add("Terminal");
                info.ArgumentList.Add("--args");
                info.ArgumentList.Add("adb");
                info.ArgumentList.Add("-s");
                info.ArgumentList.Add(serial);
                info.ArgumentList.Add("shell");
            }
            else
            {
                info = new ProcessStartInfo("x-terminal-emulator") { UseShellExecute = false };
                info.ArgumentList.Add("-e");
                info.ArgumentList.Add("adb");
                info.ArgumentList.Add("-s");
                info.ArgumentList.Add(serial);
                info.ArgumentList.Add("shell");
            }
            Process.Start(info);
            return new PortableCommandResult(0, "ADB shell открыт", "");
        }
        catch (Exception ex)
        {
            return new PortableCommandResult(1, "", $"Не удалось открыть терминал: {ex.Message}");
        }
    }

    public PortableCommandResult StartScrcpy(string serial, string? recordingPath = null)
    {
        if (!IsValidSerial(serial))
            return new PortableCommandResult(1, "", "Некорректный serial устройства.");
        try
        {
            var info = new ProcessStartInfo("scrcpy") { UseShellExecute = false };
            info.ArgumentList.Add("--serial");
            info.ArgumentList.Add(serial);
            info.ArgumentList.Add("--max-size=1920");
            info.ArgumentList.Add("--video-bit-rate=8M");
            info.ArgumentList.Add("--max-fps=60");
            if (recordingPath is not null)
                info.ArgumentList.Add($"--record={Path.GetFullPath(recordingPath)}");
            var process = Process.Start(info);
            if (recordingPath is not null && process is not null)
            {
                if (_recordings.Remove(serial, out var previous))
                    previous.Dispose();
                _recordings[serial] = process;
            }
            return new PortableCommandResult(0, recordingPath ?? "scrcpy запущен", "");
        }
        catch (Exception ex)
        {
            return new PortableCommandResult(1, "", $"Не удалось запустить scrcpy из PATH: {ex.Message}");
        }
    }

    public bool IsRecording(string serial) =>
        _recordings.TryGetValue(serial, out var process) && !process.HasExited;

    public PortableCommandResult StopRecording(string serial)
    {
        if (!_recordings.Remove(serial, out var process))
            return new PortableCommandResult(1, "", "Активная запись не найдена.");
        try
        {
            if (!process.HasExited)
            {
                process.CloseMainWindow();
                if (!process.WaitForExit(3000))
                    process.Kill(true);
            }
            process.Dispose();
            return new PortableCommandResult(0, "Запись сохранена", "");
        }
        catch (Exception ex)
        {
            process.Dispose();
            return new PortableCommandResult(1, "", ex.Message);
        }
    }

    private static PortableAdbDevice? ParseDevice(string line)
    {
        var parts = Regex.Split(line, "\\s+");
        if (parts.Length < 2 || parts[1] != "device")
            return null;
        var model = parts.FirstOrDefault(part => part.StartsWith("model:", StringComparison.OrdinalIgnoreCase))?[6..]
            ?.Replace('_', ' ') ?? "Android";
        return new PortableAdbDevice(parts[0], model, string.Empty, string.Empty, null,
            parts[0].Contains(':'));
    }

    private static async Task<PortableAdbDevice> EnrichAsync(PortableAdbDevice device,
        CancellationToken cancellationToken)
    {
        var manufacturerTask = GetPropertyAsync(device.Serial, "ro.product.manufacturer", cancellationToken);
        var modelTask = GetPropertyAsync(device.Serial, "ro.product.model", cancellationToken);
        var versionTask = GetPropertyAsync(device.Serial, "ro.build.version.release", cancellationToken);
        var batteryTask = RunAsync("adb", ["-s", device.Serial, "shell", "dumpsys", "battery"],
            cancellationToken, TimeSpan.FromSeconds(8));
        await Task.WhenAll(manufacturerTask, modelTask, versionTask, batteryTask);
        var batteryMatch = Regex.Match(batteryTask.Result.Output, @"(?m)^\s*level:\s*(\d+)");
        var battery = batteryMatch.Success && int.TryParse(batteryMatch.Groups[1].Value, out var value)
            ? value
            : (int?)null;
        var model = modelTask.Result;
        return device with
        {
            Name = string.IsNullOrWhiteSpace(model) ? device.Name : model,
            Manufacturer = manufacturerTask.Result,
            AndroidVersion = versionTask.Result,
            BatteryPercent = battery
        };
    }

    private static async Task<string> GetPropertyAsync(string serial, string property,
        CancellationToken cancellationToken)
    {
        var result = await RunAsync("adb", ["-s", serial, "shell", "getprop", property], cancellationToken,
            TimeSpan.FromSeconds(8));
        return result.IsSuccess ? result.Output.Trim() : string.Empty;
    }

    private static bool IsValidSerial(string serial) => Regex.IsMatch(serial, "^[a-zA-Z0-9._:-]+$");

    private static async Task<PortableCommandResult> RunAsync(string executable, IEnumerable<string> arguments,
        CancellationToken cancellationToken, TimeSpan timeout)
    {
        var info = CreateStartInfo(executable, arguments);
        using var process = new Process { StartInfo = info };
        try { process.Start(); }
        catch (Exception ex) { return new PortableCommandResult(1, "", ex.Message); }
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try { await process.WaitForExitAsync(timeoutSource.Token); }
        catch (OperationCanceledException)
        {
            TryKill(process);
            if (cancellationToken.IsCancellationRequested)
                throw;
            return new PortableCommandResult(-2, await output, "Превышено время ожидания.");
        }
        return new PortableCommandResult(process.ExitCode, await output, await error);
    }

    private static async Task<PortableCommandResult> RunBinaryAsync(string executable,
        IEnumerable<string> arguments, string outputPath, CancellationToken cancellationToken, TimeSpan timeout)
    {
        var info = CreateStartInfo(executable, arguments);
        using var process = new Process { StartInfo = info };
        try { process.Start(); }
        catch (Exception ex) { return new PortableCommandResult(1, "", ex.Message); }
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await using var file = File.Create(outputPath);
        var copy = process.StandardOutput.BaseStream.CopyToAsync(file, cancellationToken);
        var error = process.StandardError.ReadToEndAsync();
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
            await copy;
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            if (cancellationToken.IsCancellationRequested)
                throw;
            return new PortableCommandResult(-2, "", "Превышено время ожидания.");
        }
        return new PortableCommandResult(process.ExitCode, outputPath, await error);
    }

    private static ProcessStartInfo CreateStartInfo(string executable, IEnumerable<string> arguments)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);
        return info;
    }

    private static void TryKill(Process process)
    {
        try { process.Kill(true); } catch { }
    }
}
