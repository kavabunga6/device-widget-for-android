namespace AndroidWidget.Presentation.Screenshots;

public sealed class ScreenshotStorage
{
    private readonly ISettingsService _settings;

    public ScreenshotStorage(ISettingsService settings) => _settings = settings;

    public string Folder => string.IsNullOrWhiteSpace(_settings.Current.ScreenshotFolder)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Android Widget")
        : _settings.Current.ScreenshotFolder;

    public void SetFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            throw new ArgumentException("Папка для скриншотов не указана.", nameof(folder));
        Directory.CreateDirectory(folder);
        _settings.Update(settings => settings with { ScreenshotFolder = Path.GetFullPath(folder) });
    }

    public string CreateFilePath(AndroidDevice device)
    {
        Directory.CreateDirectory(Folder);
        var safeName = SafeFileName(device.DisplayName);
        var path = Path.Combine(Folder, $"{safeName}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png");
        return !File.Exists(path)
            ? path
            : Path.Combine(Folder, $"{safeName}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss-fff}.png");
    }

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var result = string.Concat(value.Select(character => invalid.Contains(character) ? '_' : character));
        return string.IsNullOrWhiteSpace(result) ? "Android" : result;
    }
}
