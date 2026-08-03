using System.Security.Cryptography;
using System.Text.Json;

namespace AndroidWidget.CompanionHost;

internal sealed class PairingStore
{
    private readonly string _path;
    private readonly string _linksPath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Dictionary<string, string> _tokens = new(StringComparer.Ordinal);
    private Dictionary<string, string> _clientTags = new(StringComparer.Ordinal);

    public PairingStore(string dataDirectory)
    {
        _path = Path.Combine(dataDirectory, "paired-devices.json");
        _linksPath = Path.Combine(dataDirectory, "paired-device-links.json");
    }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _tokens = await LoadDictionaryAsync(_path, cancellationToken);
            _clientTags = await LoadDictionaryAsync(_linksPath, cancellationToken);
        }
        catch (JsonException)
        {
            _tokens = new Dictionary<string, string>(StringComparer.Ordinal);
        }
        finally
        {
            _gate.Release();
        }
    }

    public bool Validate(string installationId, string token) =>
        _tokens.TryGetValue(installationId, out var expected) && FixedTimeEquals(expected, token);

    public string? GetClientTag(string installationId) =>
        _clientTags.TryGetValue(installationId, out var clientTag) ? clientTag : null;

    public bool HasClientTag(string clientTag) =>
        _clientTags.Values.Contains(clientTag, StringComparer.Ordinal);

    public async Task<string> PairAsync(string installationId, string? clientTag,
        CancellationToken cancellationToken)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _tokens[installationId] = token;
            if (!string.IsNullOrWhiteSpace(clientTag))
                _clientTags[installationId] = clientTag;
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            await SaveDictionaryAsync(_path, _tokens, cancellationToken);
            await SaveDictionaryAsync(_linksPath, _clientTags, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
        return token;
    }

    private static async Task<Dictionary<string, string>> LoadDictionaryAsync(string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return new Dictionary<string, string>(StringComparer.Ordinal);
        await using var stream = File.OpenRead(path);
        var loaded = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(stream,
            cancellationToken: cancellationToken);
        return loaded is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(loaded, StringComparer.Ordinal);
    }

    private static async Task SaveDictionaryAsync(string path, Dictionary<string, string> values,
        CancellationToken cancellationToken)
    {
        var temporaryPath = $"{path}.tmp";
        await using (var stream = CreatePrivateFile(temporaryPath))
            await JsonSerializer.SerializeAsync(stream, values, cancellationToken: cancellationToken);
        File.Move(temporaryPath, path, overwrite: true);
        TryRestrictFilePermissions(path);
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(left),
                Convert.FromBase64String(right));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static void TryRestrictFilePermissions(string path)
    {
#if NET8_0_OR_GREATER
        if (OperatingSystem.IsWindows())
            return;
        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch
        {
            // The application data directory is still user-scoped on unsupported file systems.
        }
#endif
    }

    private static FileStream CreatePrivateFile(string path)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.None
        };
#if NET8_0_OR_GREATER
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
#endif
        return new FileStream(path, options);
    }
}
