namespace AndroidWidget.Core.Messaging;

public sealed record PhoneMessage(
    string Fingerprint,
    string Sender,
    string Preview,
    string PackageName);
