using System.Diagnostics;
using System.Text.RegularExpressions;

namespace AndroidWidget.Desktop;

internal sealed record PortableAdbDevice(string Serial, string Name, bool Wireless);
internal sealed record PortableCommandResult(int ExitCode, string Output, string Error)
{
    public bool IsSuccess => ExitCode == 0;
    public string Message => string.IsNullOrWhiteSpace(Error) ? Output.Trim() : Error.Trim();
}

internal sealed class PortableAdbService
{
    public async Task<IReadOnlyList<PortableAdbDevice>> GetDevicesAsync(CancellationToken cancellationToken)
    {
        var result = await RunAsync("adb", ["devices", "-l"], cancellationToken, TimeSpan.FromSeconds(10));
        if (!result.IsSuccess)
            throw new InvalidOperationException(result.Message);
        return result.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim()).Where(line => !line.StartsWith("List of devices") && !line.StartsWith('*'))
            .Select(ParseDevice).Where(device => device is not null).Cast<PortableAdbDevice>().ToList();
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

    public PortableCommandResult StartScrcpy(string serial, string? recordingPath = null)
    {
        if (!Regex.IsMatch(serial, "^[a-zA-Z0-9._:-]+$"))
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
            Process.Start(info);
            return new PortableCommandResult(0, recordingPath ?? "scrcpy запущен", "");
        }
        catch (Exception ex)
        {
            return new PortableCommandResult(1, "", $"Не удалось запустить scrcpy из PATH: {ex.Message}");
        }
    }

    private static PortableAdbDevice? ParseDevice(string line)
    {
        var parts = Regex.Split(line, "\\s+");
        if (parts.Length < 2 || parts[1] != "device")
            return null;
        var model = parts.FirstOrDefault(part => part.StartsWith("model:", StringComparison.OrdinalIgnoreCase))?[6..]
            ?.Replace('_', ' ') ?? "Android";
        return new PortableAdbDevice(parts[0], model, parts[0].Contains(':'));
    }

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
