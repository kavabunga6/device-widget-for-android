using System.Security.Cryptography;

namespace AndroidWidget.Infrastructure.Companion;

internal sealed class CompanionPackageProvider
{
    private const string ResourceName = "AndroidWidget.Infrastructure.Bundled.DeviceWidget-Companion.apk";

    public bool IsAvailable => typeof(CompanionPackageProvider).Assembly.GetManifestResourceInfo(ResourceName) is not null;

    public bool Verify(out string details)
    {
        using var resource = typeof(CompanionPackageProvider).Assembly.GetManifestResourceStream(ResourceName);
        if (resource is null)
        {
            details = "Companion APK resource is missing.";
            return false;
        }
        using var memory = new MemoryStream();
        resource.CopyTo(memory);
        var bytes = memory.ToArray();
        if (bytes.Length < 100_000 || bytes[0] != (byte)'P' || bytes[1] != (byte)'K')
        {
            details = "Companion APK resource is not a valid ZIP/APK payload.";
            return false;
        }
        details = $"{bytes.Length} bytes, SHA-256 {Convert.ToHexString(SHA256.HashData(bytes))}";
        return true;
    }

    public string Extract()
    {
        using var resource = typeof(CompanionPackageProvider).Assembly.GetManifestResourceStream(ResourceName)
                             ?? throw new InvalidOperationException(
                                 "APK компаньона не входит в эту сборку. Сначала соберите companion-android.");
        using var memory = new MemoryStream();
        resource.CopyTo(memory);
        var bytes = memory.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()[..16];
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AndroidWidget", "companion");
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, $"DeviceWidget-Companion-{hash}.apk");
        if (File.Exists(target))
            return target;

        var temporary = $"{target}.tmp";
        File.WriteAllBytes(temporary, bytes);
        File.Move(temporary, target, overwrite: true);
        return target;
    }
}
