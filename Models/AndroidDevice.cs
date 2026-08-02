namespace AndroidWidget.Models;

public enum DeviceConnectionState
{
    Online,
    Offline,
    Unauthorized,
    Unknown
}

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
    string ScreenResolution = "")
{
    public string ConnectionLabel => IsWireless ? "Wi-Fi ADB" : "USB / ADB";
    public string PickerLabel => State == DeviceConnectionState.Online
        ? $"{DisplayName} · {ConnectionLabel}{(!IsScreenOn ? " · спит" : IsLocked ? " · заблокирован" : string.Empty)}"
        : $"{DisplayName} · {State}";
}
