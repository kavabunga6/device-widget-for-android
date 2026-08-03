using AndroidWidget.Core.Messaging;

namespace AndroidWidget.Core.Devices;

public sealed record AndroidDevice(
    string Serial,
    string DisplayName,
    string Model,
    string AndroidVersion,
    int? BatteryPercent,
    DeviceConnectionState State,
    bool IsWireless,
    bool IsScreenOn = true,
    bool IsLocked = false,
    string Manufacturer = "Android",
    string Brand = "Android",
    string DeviceCode = "",
    string ScreenResolution = "",
    PhoneMessage? LatestMessage = null,
    CompanionInstallationState CompanionState = CompanionInstallationState.Unknown,
    bool IsCompanionConnected = false,
    bool CompanionNotificationAccess = false)
{
    public string ConnectionLabel => IsWireless ? "Wi-Fi ADB" : "USB / ADB";
    public bool CompanionFeaturesAvailable =>
        State == DeviceConnectionState.Online && CompanionState.IsInstalled() &&
        IsCompanionConnected;
}
