namespace AndroidWidget.Protocol;

public sealed record DeviceIdentity(
    string InstallationId,
    string DisplayName,
    string Manufacturer,
    string Model,
    string AndroidVersion,
    int ApiLevel);

public sealed record ClientHello(
    int ProtocolVersion,
    string Mode,
    DeviceIdentity Device,
    string Credential);

public sealed record ServerHello(
    int ProtocolVersion,
    bool Accepted,
    string? AuthToken = null,
    string? Error = null);

public sealed record DeviceStatusMessage(
    string Type,
    int? BatteryPercent,
    bool IsCharging,
    bool IsScreenOn,
    bool IsLocked,
    long SentAtUnixMilliseconds,
    bool HasNotificationAccess = false);

public sealed record NotificationMessage(
    string Type,
    string NotificationId,
    string PackageName,
    string AppName,
    string Title,
    string Preview,
    long PostedAtUnixMilliseconds,
    bool IsConversation = false);

public sealed record PingMessage(string Type, long SentAtUnixMilliseconds);
