using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace AndroidWidget.CompanionHost;

internal sealed class CompanionCertificateStore
{
    private readonly string _certificatePath;

    public CompanionCertificateStore(string dataDirectory) =>
        _certificatePath = Path.Combine(dataDirectory, "companion-host.pfx");

    public X509Certificate2 LoadOrCreate()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_certificatePath)!);
        if (File.Exists(_certificatePath))
            return LoadCertificate(_certificatePath);

        using var key = RSA.Create(3072);
        var request = new CertificateRequest("CN=Device Widget Companion Host", key,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new("1.3.6.1.5.5.7.3.1", "TLS Web Server Authentication") }, true));
        var alternativeNames = new SubjectAlternativeNameBuilder();
        alternativeNames.AddDnsName("localhost");
        alternativeNames.AddIpAddress(IPAddress.Loopback);
        alternativeNames.AddIpAddress(IPAddress.IPv6Loopback);
        request.CertificateExtensions.Add(alternativeNames.Build());
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(5));
        WriteCertificate(_certificatePath, generated.Export(X509ContentType.Pfx));
        TryRestrictFilePermissions(_certificatePath);
        return LoadCertificate(_certificatePath);
    }

    public static string GetSha256Fingerprint(X509Certificate2 certificate) =>
        Convert.ToHexString(SHA256.HashData(certificate.RawData)).ToLowerInvariant();

    private static X509Certificate2 LoadCertificate(string path) =>
        X509CertificateLoader.LoadPkcs12FromFile(path, (string?)null,
            OperatingSystem.IsLinux()
                ? X509KeyStorageFlags.EphemeralKeySet
                : X509KeyStorageFlags.DefaultKeySet,
            loaderLimits: null);

    private static void TryRestrictFilePermissions(string path)
    {
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
    }

    private static void WriteCertificate(string path, byte[] data)
    {
        if (OperatingSystem.IsWindows())
        {
            File.WriteAllBytes(path, data);
            return;
        }
        using var stream = new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.None,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
        });
        stream.Write(data);
    }
}
