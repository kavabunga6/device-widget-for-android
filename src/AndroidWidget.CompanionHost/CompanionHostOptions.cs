using AndroidWidget.Protocol;

namespace AndroidWidget.CompanionHost;

public sealed record CompanionHostOptions(string DataDirectory, int Port = ProtocolConstants.DefaultPort);

public sealed record PairingSession(string Uri, string Code, DateTimeOffset ExpiresAt);

public sealed record CompanionDeviceState(
    DeviceIdentity Identity,
    bool IsConnected,
    DateTimeOffset LastSeen,
    DeviceStatusMessage? Status = null,
    NotificationMessage? LatestNotification = null,
    string? ClientTag = null);

public sealed record CompanionNotification(
    string? ClientTag,
    DeviceIdentity Identity,
    NotificationMessage Notification);
