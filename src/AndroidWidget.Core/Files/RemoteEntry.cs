namespace AndroidWidget.Core.Files;

public sealed record RemoteEntry(string Name, string FullPath, bool IsDirectory)
{
    public string DisplayName => Name.TrimEnd('/');
}
