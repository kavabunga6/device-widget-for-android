using System.Text;
using AndroidWidget.Core.Abstractions;
using AndroidWidget.Core.Devices;

namespace AndroidWidget.Presentation.Media;

public sealed class RecordingStorage
{
    private readonly ISettingsService _settings;

    public RecordingStorage(ISettingsService settings) => _settings = settings;

    public string Folder => ResolveFolder(_settings.Current.RecordingFolder,
        Environment.SpecialFolder.MyVideos, "Device Widget");

    public string CreateFilePath(AndroidDevice device)
    {
        return Path.Combine(Folder,
            $"{Sanitize(device.DisplayName)}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.mkv");
    }

    public void SetFolder(string folder)
    {
        var fullPath = Path.GetFullPath(folder);
        Directory.CreateDirectory(fullPath);
        _settings.Update(settings => settings with { RecordingFolder = fullPath });
    }

    internal static string ResolveFolder(string? configured, Environment.SpecialFolder systemFolder,
        string childFolder)
    {
        if (!string.IsNullOrWhiteSpace(configured))
            return Path.GetFullPath(configured);
        var root = Environment.GetFolderPath(systemFolder);
        if (string.IsNullOrWhiteSpace(root))
            root = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(root, childFolder);
    }

    internal static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var result = new StringBuilder(value.Length);
        foreach (var character in value)
            result.Append(invalid.Contains(character) ? '_' : character);
        return string.IsNullOrWhiteSpace(result.ToString()) ? "Android" : result.ToString().Trim();
    }
}
