namespace AndroidWidget.Core.Devices;

public enum CompanionInstallationState
{
    Unknown,
    NotInstalled,
    Installed,
    UpdateAvailable
}

public static class CompanionInstallationStateExtensions
{
    public static bool IsInstalled(this CompanionInstallationState state) =>
        state is CompanionInstallationState.Installed or CompanionInstallationState.UpdateAvailable;
}
