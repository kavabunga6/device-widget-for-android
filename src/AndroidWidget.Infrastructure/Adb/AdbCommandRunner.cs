using System.Diagnostics;
using AndroidWidget.Core.Operations;

namespace AndroidWidget.Infrastructure.Adb;

public sealed class AdbCommandRunner
{
    private readonly AdbExecutableProvider _executableProvider;

    public AdbCommandRunner(AdbExecutableProvider executableProvider) =>
        _executableProvider = executableProvider;

    public string ExecutablePath => _executableProvider.GetPath();

    public Task<OperationResult> RunAsync(IEnumerable<string> arguments, CancellationToken cancellationToken = default,
        TimeSpan? timeout = null) =>
        RunProcessAsync(ExecutablePath, arguments, cancellationToken,
            timeout ?? TimeSpan.FromSeconds(15));

    public Task<OperationResult> RunBinaryToFileAsync(IEnumerable<string> arguments, string outputPath,
        CancellationToken cancellationToken = default, TimeSpan? timeout = null) =>
        RunBinaryToFileAsync(ExecutablePath, arguments, outputPath, cancellationToken,
            timeout ?? TimeSpan.FromSeconds(30));

    private static async Task<OperationResult> RunProcessAsync(string fileName, IEnumerable<string> arguments,
        CancellationToken cancellationToken, TimeSpan timeout)
    {
        var info = CreateStartInfo(fileName, arguments);
        using var process = new Process { StartInfo = info };
        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            return OperationResult.Failure($"ADB не найден. Установите Android Platform Tools или используйте встроенный scrcpy. ({ex.Message})", -1);
        }

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            if (cancellationToken.IsCancellationRequested)
                throw;
            return new OperationResult(-2, await outputTask, "Команда ADB превысила время ожидания.");
        }

        return new OperationResult(process.ExitCode, await outputTask, await errorTask);
    }

    private static async Task<OperationResult> RunBinaryToFileAsync(string fileName,
        IEnumerable<string> arguments, string outputPath, CancellationToken cancellationToken, TimeSpan timeout)
    {
        var info = CreateStartInfo(fileName, arguments);
        using var process = new Process { StartInfo = info };
        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            return OperationResult.Failure($"ADB не найден: {ex.Message}", -1);
        }

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        await using var file = File.Create(outputPath);
        var copyTask = process.StandardOutput.BaseStream.CopyToAsync(file, cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync();
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
            await copyTask;
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            if (cancellationToken.IsCancellationRequested)
                throw;
            return OperationResult.Failure("Снимок экрана превысил время ожидания.", -2);
        }

        return new OperationResult(process.ExitCode, outputPath, await errorTask);
    }

    private static ProcessStartInfo CreateStartInfo(string fileName, IEnumerable<string> arguments)
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
        return info;
    }

    private static void TryKill(Process process)
    {
        try { process.Kill(true); } catch { /* Process already exited. */ }
    }
}
