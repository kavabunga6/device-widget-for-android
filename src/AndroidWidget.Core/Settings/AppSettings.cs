using AndroidWidget.Core.Devices;

namespace AndroidWidget.Core.Settings;

public sealed record AppSettings(
    double? Left = null,
    double? Top = null,
    bool Topmost = true,
    string? ScreenshotFolder = null,
    bool IsMini = false,
    WidgetTheme Theme = WidgetTheme.Dark,
    bool AutoStart = false,
    bool ShowSmsBubbles = true,
    int NotificationDisplaySeconds = 10,
    double? MainCardWidth = null,
    double? MainCardHeight = null,
    ScrcpyPreset ScrcpyPreset = ScrcpyPreset.Balanced,
    string? RecordingFolder = null,
    bool ShowScreenRecordingGuide = true,
    bool NotifyNewPhotos = true,
    bool AutoImportPhotos = false,
    string? PhotoImportFolder = null);
