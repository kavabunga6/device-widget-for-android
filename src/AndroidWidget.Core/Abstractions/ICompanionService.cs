using AndroidWidget.Core.Devices;
using AndroidWidget.Core.Operations;

namespace AndroidWidget.Core.Abstractions;

public interface ICompanionService
{
    bool IsInstallerAvailable { get; }

    Task<CompanionInstallationState> GetInstallationStateAsync(string serial,
        CancellationToken cancellationToken = default);

    Task<CompanionInstallResult> InstallAsync(string serial, CancellationToken cancellationToken = default);

    Task<OperationResult> ReinstallAsync(string serial, CancellationToken cancellationToken = default);

    Task<OperationResult> LaunchAsync(string serial, CancellationToken cancellationToken = default);

    Task<OperationResult> PreparePortReverseAsync(string serial, int port,
        CancellationToken cancellationToken = default);

    Task<OperationResult> OpenPairingAsync(string serial, string pairingUri,
        CancellationToken cancellationToken = default);

    Task<bool?> HasNotificationAccessAsync(string serial,
        CancellationToken cancellationToken = default);
}
