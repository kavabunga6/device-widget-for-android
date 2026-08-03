using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AndroidWidget.Desktop;

internal sealed partial class ScreenRecordingWindow : Window
{
    private readonly DesktopSettingsStore _settings;

    public ScreenRecordingWindow(DesktopSettingsStore settings, string deviceName, string outputPath)
    {
        _settings = settings;
        InitializeComponent();
        DeviceNameText.Text = deviceName;
        RecordingPathText.Text = outputPath;
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void StartButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DoNotShowAgainCheck.IsChecked == true)
            _settings.Update(current => current with { ShowScreenRecordingGuide = false });
        Close(true);
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e) => Close(false);
}
