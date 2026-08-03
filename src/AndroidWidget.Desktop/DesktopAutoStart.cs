using System.Runtime.Versioning;
using System.Security;
using Microsoft.Win32;

namespace AndroidWidget.Desktop;

internal static class DesktopAutoStart
{
    private const string AppName = "Device Widget for Android";
    private const string MacLabel = "dev.devicewidget.desktop";

    public static bool TrySet(bool enabled, out string? error)
    {
        try
        {
            var executable = Environment.ProcessPath
                ?? throw new InvalidOperationException("Не удалось определить путь к приложению.");
            if (OperatingSystem.IsWindows())
                SetWindows(enabled, executable);
            else if (OperatingSystem.IsMacOS())
                SetMacOs(enabled, executable);
            else if (OperatingSystem.IsLinux())
                SetLinux(enabled, executable);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static void SetWindows(bool enabled, string executable)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled)
            key.SetValue(AppName, $"\"{executable}\"", RegistryValueKind.String);
        else
            key.DeleteValue(AppName, false);
    }

    private static void SetMacOs(bool enabled, string executable)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "LaunchAgents");
        var path = Path.Combine(directory, $"{MacLabel}.plist");
        if (!enabled)
        {
            if (File.Exists(path))
                File.Delete(path);
            return;
        }
        Directory.CreateDirectory(directory);
        var escaped = SecurityElement.Escape(executable) ?? executable;
        File.WriteAllText(path, $"""
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>Label</key><string>{MacLabel}</string>
<key>ProgramArguments</key><array><string>{escaped}</string></array>
<key>RunAtLoad</key><true/>
</dict></plist>
""");
    }

    private static void SetLinux(bool enabled, string executable)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config", "autostart");
        var path = Path.Combine(directory, "device-widget.desktop");
        if (!enabled)
        {
            if (File.Exists(path))
                File.Delete(path);
            return;
        }
        Directory.CreateDirectory(directory);
        var command = executable.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
        File.WriteAllText(path, $"""
[Desktop Entry]
Type=Application
Name={AppName}
Exec="{command}"
Terminal=false
X-GNOME-Autostart-enabled=true
""");
    }
}
