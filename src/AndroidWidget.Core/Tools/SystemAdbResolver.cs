using System.Diagnostics;

namespace AndroidWidget.Core.Tools;

public static class SystemAdbResolver
{
    public static string? Find()
    {
        var configured = ResolveCandidate(Environment.GetEnvironmentVariable("ADB"));
        if (configured is not null)
            return configured;

        var onPath = FindOnPath();
        if (onPath is not null)
            return onPath;

        return FindRunningAdb();
    }

    private static string? FindRunningAdb()
    {
        foreach (var process in Process.GetProcessesByName("adb"))
        {
            using (process)
            {
                try
                {
                    var path = process.MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                        return Path.GetFullPath(path);
                }
                catch
                {
                    // Process may exit or deny module inspection between enumeration and lookup.
                }
            }
        }
        return null;
    }

    private static string? ResolveCandidate(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return null;

        candidate = candidate.Trim().Trim('"');
        if (Path.IsPathRooted(candidate))
            return File.Exists(candidate) ? Path.GetFullPath(candidate) : null;

        return FindOnPath(candidate);
    }

    private static string? FindOnPath(string executable = "adb")
    {
        var extensions = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT;.COM")
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
            : [string.Empty];
        if (Path.HasExtension(executable))
            extensions = [string.Empty];

        foreach (var rawDirectory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var directory = rawDirectory.Trim().Trim('"');
            foreach (var extension in extensions)
            {
                try
                {
                    var path = Path.Combine(directory, executable + extension.ToLowerInvariant());
                    if (File.Exists(path))
                        return Path.GetFullPath(path);
                }
                catch
                {
                    // Ignore malformed or inaccessible PATH entries.
                }
            }
        }
        return null;
    }
}
