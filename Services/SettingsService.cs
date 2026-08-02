using System.Text.Json;
using Microsoft.Win32;

namespace AndroidWidget.Services;

public enum WidgetTheme
{
    Dark,
    Light
}

public sealed record AppSettings(
    double? Left = null,
    double? Top = null,
    bool Topmost = true,
    string? ScreenshotFolder = null,
    bool IsMini = false,
    WidgetTheme Theme = WidgetTheme.Dark,
    bool AutoStart = false);

public static class SettingsService
{
    private const string RunValueName = "AndroidWidget";
    private static readonly object Sync = new();
    private static AppSettings _current = LoadFromDisk();

    public static AppSettings Current => _current;
    public static event EventHandler? Changed;

    public static void Update(Func<AppSettings, AppSettings> update)
    {
        lock (Sync)
        {
            _current = update(_current);
            SaveToDisk(_current);
        }
        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static bool TrySetAutoStart(bool enabled, out string? error)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (enabled)
                key.SetValue(RunValueName, BuildLaunchCommand(), RegistryValueKind.String);
            else
                key.DeleteValue(RunValueName, false);

            Update(settings => settings with { AutoStart = enabled });
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static AppSettings LoadFromDisk()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings()
                : new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    private static void SaveToDisk(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // A settings failure must not stop device monitoring.
        }
    }

    private static string BuildLaunchCommand()
    {
        var processPath = Environment.ProcessPath ?? throw new InvalidOperationException("Не найден путь приложения.");
        if (Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Автозапуск доступен в опубликованной EXE-версии.");
        return $"\"{processPath}\"";
    }

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AndroidWidget", "settings.json");
}
