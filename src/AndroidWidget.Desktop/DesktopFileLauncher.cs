using System.Diagnostics;

namespace AndroidWidget.Desktop;

internal static class DesktopFileLauncher
{
    public static void Open(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
            throw new FileNotFoundException("Файл или папка больше не существует.", fullPath);

        ProcessStartInfo info;
        if (OperatingSystem.IsWindows())
        {
            info = new ProcessStartInfo(fullPath) { UseShellExecute = true };
        }
        else if (OperatingSystem.IsMacOS())
        {
            info = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false };
            info.ArgumentList.Add(fullPath);
        }
        else
        {
            info = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
            info.ArgumentList.Add(fullPath);
        }

        if (Process.Start(info) is null)
            throw new InvalidOperationException("Системное приложение для этого типа файла не найдено.");
    }
}
