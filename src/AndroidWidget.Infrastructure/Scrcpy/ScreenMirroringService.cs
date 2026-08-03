using System.Diagnostics;
using AndroidWidget.Core.Devices;
using AndroidWidget.Core.Operations;

namespace AndroidWidget.Infrastructure.Scrcpy;

public sealed class ScreenMirroringService
{
    private readonly ScrcpyBundleManager _bundleManager;
    private readonly object _recordingGate = new();
    private readonly Dictionary<string, ActiveRecording> _recordings = new(StringComparer.Ordinal);

    public ScreenMirroringService(ScrcpyBundleManager bundleManager) => _bundleManager = bundleManager;

    public OperationResult Start(string serial, ScrcpyPreset preset) => StartProcess(serial, preset, null);

    public OperationResult StartRecording(string serial, string outputPath, ScrcpyPreset preset)
    {
        try
        {
            if (IsRecording(serial))
                return OperationResult.Failure("Для этого устройства запись уже идёт.");

            var fullPath = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            return StartProcess(serial, preset, fullPath);
        }
        catch (Exception ex)
        {
            return OperationResult.Failure(ex.Message);
        }
    }

    public bool IsRecording(string serial)
    {
        Process? completed = null;
        lock (_recordingGate)
        {
            if (!_recordings.TryGetValue(serial, out var recording))
                return false;

            try
            {
                if (!recording.Process.HasExited)
                    return true;
            }
            catch (InvalidOperationException) { }

            _recordings.Remove(serial);
            completed = recording.Process;
        }

        completed.Dispose();
        return false;
    }

    public string? GetRecordingPath(string serial)
    {
        lock (_recordingGate)
            return _recordings.TryGetValue(serial, out var recording) ? recording.OutputPath : null;
    }

    public OperationResult StopRecording(string serial)
    {
        ActiveRecording? recording;
        lock (_recordingGate)
            _recordings.TryGetValue(serial, out recording);

        if (recording is null)
            return OperationResult.Failure("Активная запись для этого устройства не найдена.");

        var process = recording.Process;
        try
        {
            if (!process.HasExited)
            {
                var closeRequested = process.CloseMainWindow();
                if (closeRequested && !process.WaitForExit(5000))
                    process.Kill(entireProcessTree: true);
                else if (!closeRequested)
                    process.Kill(entireProcessTree: true);

                if (!process.WaitForExit(5000))
                    return OperationResult.Failure("scrcpy не завершился вовремя.");
            }

            ReleaseRecording(serial, process);
            return OperationResult.Success();
        }
        catch (Exception ex)
        {
            return OperationResult.Failure($"Не удалось остановить запись: {ex.Message}");
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
            info.ArgumentList.Add(recordingPath is null ? "Device Widget · Screen" : "Device Widget · Recording");
            AddPresetArguments(info.ArgumentList, preset);
            if (recordingPath is not null)
                info.ArgumentList.Add($"--record={recordingPath}");
            var process = Process.Start(info);
            if (process is null)
                return OperationResult.Failure("scrcpy не вернул запущенный процесс.");

            if (recordingPath is not null)
                TrackRecording(serial, process, recordingPath);
            return OperationResult.Success(recordingPath ?? string.Empty);
        }
        catch (Exception ex)
        {
            return OperationResult.Failure(ex.Message);
        }
    }

    private void TrackRecording(string serial, Process process, string outputPath)
    {
        lock (_recordingGate)
            _recordings[serial] = new ActiveRecording(process, outputPath);
    }

    private void ReleaseRecording(string serial, Process process)
    {
        var removed = false;
        lock (_recordingGate)
        {
            if (_recordings.TryGetValue(serial, out var active) && ReferenceEquals(active.Process, process))
            {
                _recordings.Remove(serial);
                removed = true;
            }
        }

        if (removed)
            process.Dispose();
    }

    private sealed record ActiveRecording(Process Process, string OutputPath);

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
