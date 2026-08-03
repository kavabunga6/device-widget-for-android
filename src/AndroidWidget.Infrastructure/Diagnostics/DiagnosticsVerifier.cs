using AndroidWidget.Core.Abstractions;
using AndroidWidget.Infrastructure.Adb;
using AndroidWidget.Infrastructure.Companion;
using AndroidWidget.Infrastructure.Scrcpy;

namespace AndroidWidget.Infrastructure.Diagnostics;

public sealed class DiagnosticsVerifier : IDiagnosticsVerifier
{
    private readonly ScrcpyBundleManager _bundleManager;

    public DiagnosticsVerifier(ScrcpyBundleManager bundleManager) => _bundleManager = bundleManager;

    public bool VerifyScrcpyBundle(out string details)
    {
        var path = _bundleManager.Prepare(out var error);
        details = path ?? error ?? "Unknown scrcpy bundle error.";
        return path is not null;
    }

    public bool VerifyCompanionBundle(out string details)
    {
        var bundleValid = new CompanionPackageProvider().Verify(out details);
        var versionDetectionValid = CompanionService.VerifyVersionDetection();
        if (!versionDetectionValid)
            details += "; companion update detection failed";
        return bundleValid && versionDetectionValid;
    }

    public bool VerifySmsParser() => SmsNotificationReader.VerifyParser();

    public bool VerifyWirelessPairingParser() => AndroidDeviceService.VerifyWirelessPairingParser();
}
