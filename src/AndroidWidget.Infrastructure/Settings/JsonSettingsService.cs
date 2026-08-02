using System.Text.Json;
using AndroidWidget.Core.Abstractions;
using AndroidWidget.Core.Operations;
using AndroidWidget.Core.Settings;
using Microsoft.Win32;

namespace AndroidWidget.Infrastructure.Settings;

public sealed class JsonSettingsService : ISettingsService
{
    private const string RunValueName = "AndroidWidget";
    private readonly object _sync = new();
    private AppSettings _current;

    public JsonSettingsService() => _current = LoadFromDisk();

    public AppSettings Current => _current;
    public event EventHandler? Changed;

    public void Update(Func<AppSettings, AppSettings> update)
    {
        lock (_sync)
        {
            _current = update(_current);
            SaveToDisk(_current);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public OperationResult SetAutoStart(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (enabled)
                key.SetValue(RunValueName, BuildLaunchCommand(), RegistryValueKind.String);
            else
                key.DeleteValue(RunValueName, false);
            Update(settings => settings with { AutoStart = enabled });
            return OperationResult.Success();
        }
        catch (Exception ex)
        {
            return OperationResult.Failure(ex.Message);
        }
    }

    private static AppSettings LoadFromDisk()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new AppSettings();
            var json = File.ReadAllText(FilePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            using var document = JsonDocument.Parse(json);
            settings = document.RootElement.TryGetProperty(nameof(AppSettings.ShowSmsBubbles), out _)
                ? settings
                : settings with { ShowSmsBubbles = true };
            return settings with
            {
                NotificationDisplaySeconds = settings.NotificationDisplaySeconds is 5 or 10 or 15 or 30 or 60
                    ? settings.NotificationDisplaySeconds
                    : 10
            };
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
            File.WriteAllText(FilePath,
                JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Settings persistence must not stop device monitoring.
        }
    }

    private static string BuildLaunchCommand()
    {
        var processPath = Environment.ProcessPath ??
                          throw new InvalidOperationException("Не найден путь приложения.");
        if (Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Автозапуск доступен в опубликованной EXE-версии.");
        return $"\"{processPath}\"";
    }

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AndroidWidget", "settings.json");
}
