using AndroidWidget.Core.Abstractions;

namespace AndroidWidget.Infrastructure.Diagnostics;

public sealed class FileAppLogger : IAppLogger
{
    private readonly object _sync = new();

    public string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AndroidWidget", "widget.log");

    public void Write(string message)
    {
        try
        {
            lock (_sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.AppendAllText(FilePath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Diagnostics must never affect the widget.
        }
    }
}
