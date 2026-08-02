namespace AndroidWidget.Models;

public sealed record RemoteEntry(string Name, string FullPath, bool IsDirectory)
{
    public string Icon => IsDirectory ? "▰" : GetIcon(Name);
    public string DisplayName => Name.TrimEnd('/');

    private static string GetIcon(string name)
    {
        var extension = Path.GetExtension(name).ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif" => "▧",
            ".mp4" or ".mkv" or ".webm" => "▶",
            ".apk" => "◆",
            _ => "▪"
        };
    }
}
