using System.IO.Compression;

namespace AndroidWidget.Desktop;

internal sealed class DesktopToolResolver
{
    private const string ScrcpyVersion = "4.0";
    private const string ArchiveResource = "AndroidWidget.Desktop.Bundled.scrcpy-win64-v4.0.zip";
    private const string LicenseResource = "AndroidWidget.Desktop.Bundled.scrcpy-LICENSE.txt";
    private readonly Lazy<ToolPaths> _paths = new(Resolve);

    public string Adb => _paths.Value.Adb;
    public string Scrcpy => _paths.Value.Scrcpy;

    private static ToolPaths Resolve()
    {
        if (!OperatingSystem.IsWindows())
            return FindBundledUnixTools() ?? new ToolPaths("adb", "scrcpy");

        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DeviceWidget", "tools", $"scrcpy-{ScrcpyVersion}");
        var cached = FindTools(root);
        if (cached is not null)
            return cached;

        Directory.CreateDirectory(root);
        var assembly = typeof(DesktopToolResolver).Assembly;
        using var stream = assembly.GetManifestResourceStream(ArchiveResource)
            ?? throw new InvalidOperationException("Встроенный архив scrcpy отсутствует.");
        ExtractSafely(stream, root);
        using var license = assembly.GetManifestResourceStream(LicenseResource);
        if (license is not null)
        {
            using var output = File.Create(Path.Combine(root, "LICENSE-scrcpy.txt"));
            license.CopyTo(output);
        }
        return FindTools(root) ?? throw new InvalidOperationException("В архиве scrcpy не найдены adb.exe и scrcpy.exe.");
    }

    private static ToolPaths? FindBundledUnixTools()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var roots = new[]
        {
            Path.Combine(baseDirectory, "tools", $"scrcpy-{ScrcpyVersion}"),
            Path.Combine(baseDirectory, "..", "Resources", "tools", $"scrcpy-{ScrcpyVersion}")
        };
        foreach (var root in roots.Select(Path.GetFullPath))
        {
            var adb = Path.Combine(root, "adb");
            var scrcpy = Path.Combine(root, "scrcpy");
            if (File.Exists(adb) && File.Exists(scrcpy) && File.Exists(Path.Combine(root, "scrcpy-server")))
                return new ToolPaths(adb, scrcpy);
        }
        return null;
    }

    private static ToolPaths? FindTools(string root)
    {
        if (!Directory.Exists(root))
            return null;
        var scrcpy = Directory.EnumerateFiles(root, "scrcpy.exe", SearchOption.AllDirectories)
            .FirstOrDefault(path => File.Exists(Path.Combine(Path.GetDirectoryName(path)!, "scrcpy-server")));
        if (scrcpy is null)
            return null;
        var adb = Path.Combine(Path.GetDirectoryName(scrcpy)!, "adb.exe");
        return File.Exists(adb) ? new ToolPaths(adb, scrcpy) : null;
    }

    private static void ExtractSafely(Stream stream, string root)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        foreach (var entry in archive.Entries)
        {
            var destination = Path.GetFullPath(Path.Combine(root, entry.FullName));
            if (!destination.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Архив scrcpy содержит небезопасный путь.");
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destination);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, true);
        }
    }

    private sealed record ToolPaths(string Adb, string Scrcpy);
}
