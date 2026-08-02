using System.IO.Compression;
using System.Reflection;

namespace AndroidWidget.Infrastructure.Scrcpy;

public sealed class ScrcpyBundleManager
{
    private const string Version = "4.0";
    private const string ArchiveResource = "AndroidWidget.Infrastructure.Bundled.scrcpy-win64-v4.0.zip";
    private const string LicenseResource = "AndroidWidget.Infrastructure.Bundled.scrcpy-LICENSE.txt";

    public string? Prepare(out string? error)
    {
        var cacheRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AndroidWidget", "scrcpy", $"v{Version}");
        try
        {
            var cached = FindExecutable(cacheRoot);
            if (cached is not null)
            {
                error = null;
                return cached;
            }

            Directory.CreateDirectory(cacheRoot);
            var assembly = typeof(ScrcpyBundleManager).Assembly;
            using var archiveStream = assembly.GetManifestResourceStream(ArchiveResource)
                ?? throw new InvalidOperationException("Ресурс scrcpy отсутствует в сборке.");
            ExtractArchive(archiveStream, cacheRoot);

            using var licenseStream = assembly.GetManifestResourceStream(LicenseResource);
            if (licenseStream is not null)
            {
                using var licenseFile = File.Create(Path.Combine(cacheRoot, "LICENSE-scrcpy.txt"));
                licenseStream.CopyTo(licenseFile);
            }

            var executable = FindExecutable(cacheRoot)
                ?? throw new InvalidOperationException("В архиве не найдены scrcpy.exe и scrcpy-server.");
            error = null;
            return executable;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    private static string? FindExecutable(string root)
    {
        if (!Directory.Exists(root))
            return null;
        return Directory.EnumerateFiles(root, "scrcpy.exe", SearchOption.AllDirectories)
            .FirstOrDefault(path => File.Exists(Path.Combine(Path.GetDirectoryName(path)!, "scrcpy-server")));
    }

    private static void ExtractArchive(Stream stream, string destinationRoot)
    {
        var normalizedRoot = Path.GetFullPath(destinationRoot).TrimEnd(Path.DirectorySeparatorChar) +
                             Path.DirectorySeparatorChar;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        foreach (var entry in archive.Entries)
        {
            var destination = Path.GetFullPath(Path.Combine(destinationRoot, entry.FullName));
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
}
