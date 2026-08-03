using System.Windows;
using AndroidWidget.Core.Devices;

namespace AndroidWidget;

public partial class ScreenRecordingWindow : Window
{
    private readonly ISettingsService _settings;

    public ScreenRecordingWindow(AndroidDevice device, ISettingsService settings, string outputPath)
    {
        _settings = settings;
        InitializeComponent();
        DeviceNameText.Text = device.DisplayName;
        RecordingPathText.Text = outputPath;
    }

    private void PrimaryButton_Click(object sender, RoutedEventArgs e)
    {
        if (DoNotShowAgainCheck.IsChecked == true)
            _settings.Update(settings => settings with { ShowScreenRecordingGuide = false });
        DialogResult = true;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
