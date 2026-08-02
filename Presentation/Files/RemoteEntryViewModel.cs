namespace AndroidWidget.Presentation.Files;

public sealed record RemoteEntryViewModel(RemoteEntry Entry)
{
    public string DisplayName => Entry.DisplayName;
    public string Icon => Entry.IsDirectory ? "▰" : ResolveFileIcon(Entry.Name);

    private static string ResolveFileIcon(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif" => "▧",
        ".mp4" or ".mkv" or ".webm" => "▶",
        ".apk" => "◆",
        _ => "▪"
    };
}
