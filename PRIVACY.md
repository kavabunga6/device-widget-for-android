# Privacy

Device Widget for Android works locally. It does not include telemetry,
advertising, analytics, crash upload, cloud synchronization, or an account
system.

The desktop application communicates with connected devices through ADB and,
when the user installs and pairs the optional companion, through an encrypted
connection on the local network. The companion requests notification access
only after an explicit user action. It does not request contacts, call log,
phone state, or SMS permissions.

Settings, pairing credentials and diagnostics remain in the current user's
local application-data directory. A diagnostic log can contain operating-system
error text and local file paths; review it before sharing it with anyone.

No device serial, model, account identifier, certificate, key, pairing token,
or user-specific absolute path is embedded in the source tree or release
configuration.
