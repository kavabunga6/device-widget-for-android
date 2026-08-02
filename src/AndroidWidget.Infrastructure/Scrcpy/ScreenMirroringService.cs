using System.Diagnostics;
using AndroidWidget.Core.Devices;
using AndroidWidget.Core.Operations;

namespace AndroidWidget.Infrastructure.Scrcpy;

public sealed class ScreenMirroringService
{
    private readonly ScrcpyBundleManager _bundleManager;

    public ScreenMirroringService(ScrcpyBundleManager bundleManager) => _bundleManager = bundleManager;

    public OperationResult Start(string serial, ScrcpyPreset preset) => StartProcess(serial, preset, null);

    public OperationResult StartRecording(string serial, string outputPath, ScrcpyPreset preset)
    {
        try
        {
            var fullPath = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            return StartProcess(serial, preset, fullPath);
        }
        catch (Exception ex)
        {
            return OperationResult.Failure(ex.Message);
        }
    }

    private OperationResult StartProcess(string serial, ScrcpyPreset preset, string? recordingPath)
    {
        var bundled = _bundleManager.Prepare(out var error);
        if (string.IsNullOrWhiteSpace(bundled))
            return OperationResult.Failure($"Не удалось подготовить встроенный scrcpy: {error}");

        try
        {
            var info = new ProcessStartInfo(bundled)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(bundled)!
            };
            info.ArgumentList.Add("--serial");
            info.ArgumentList.Add(serial);
            info.ArgumentList.Add("--window-title");
            info.ArgumentList.Add(recordingPath is null ? "Android Widget · Screen" : "Android Widget · Recording");
            AddPresetArguments(info.ArgumentList, preset);
            if (recordingPath is not null)
                info.ArgumentList.Add($"--record={recordingPath}");
            Process.Start(info);
            return OperationResult.Success(recordingPath ?? string.Empty);
        }
        catch (Exception ex)
        {
            return OperationResult.Failure(ex.Message);
        }
    }

    private static void AddPresetArguments(ICollection<string> arguments, ScrcpyPreset preset)
    {
        var values = preset switch
        {
            ScrcpyPreset.Quality => new[] { "--max-size=2560", "--video-bit-rate=16M", "--max-fps=60" },
            ScrcpyPreset.LowLatency => new[]
                { "--max-size=1280", "--video-bit-rate=4M", "--max-fps=30", "--video-buffer=0" },
            ScrcpyPreset.Presentation => new[]
                { "--max-size=1920", "--video-bit-rate=10M", "--max-fps=30", "--stay-awake" },
            _ => new[] { "--max-size=1920", "--video-bit-rate=8M", "--max-fps=60" }
        };
        foreach (var value in values)
            arguments.Add(value);
    }
}
