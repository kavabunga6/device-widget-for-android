using AndroidWidget.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Styling;

namespace AndroidWidget.Desktop;

internal sealed partial class SettingsWindow : Window
{
    private readonly DesktopSettingsStore _store;
    private bool _loading = true;

    public SettingsWindow(DesktopSettingsStore store)
    {
        _store = store;
        InitializeComponent();
        VersionText.Text = ProductVersion.ProductLabel;
        DurationCombo.ItemsSource = new[] { 5, 10, 15, 30, 60 };
        var settings = store.Current;
        ThemeToggle.IsChecked = settings.Theme == "Light";
        AutoStartToggle.IsChecked = settings.AutoStart;
        NotificationsToggle.IsChecked = settings.ShowNotifications;
        DurationCombo.SelectedItem = settings.NotificationDurationSeconds;
        ScreenshotFolderText.Text = settings.ScreenshotFolder;
        _loading = false;
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void ThemeToggle_Changed(object? sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        var theme = ThemeToggle.IsChecked == true ? "Light" : "Dark";
        _store.Update(current => current with { Theme = theme });
        if (Application.Current is { } app)
            app.RequestedThemeVariant = theme == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
    }

    private void SettingToggle_Changed(object? sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        var autoStart = AutoStartToggle.IsChecked == true;
        if (autoStart != _store.Current.AutoStart && !_store.SetAutoStart(autoStart, out _))
        {
            _loading = true;
            AutoStartToggle.IsChecked = _store.Current.AutoStart;
            _loading = false;
        }
        _store.Update(current => current with
        {
            ShowNotifications = NotificationsToggle.IsChecked == true
        });
    }

    private void DurationCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loading || DurationCombo.SelectedItem is not int seconds)
            return;
        _store.Update(current => current with { NotificationDurationSeconds = seconds });
    }

    private async void ScreenshotFolderButton_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Папка для скриншотов",
            AllowMultiple = false
        });
        if (folders.FirstOrDefault() is not { } folder)
            return;
        ScreenshotFolderText.Text = folder.Path.LocalPath;
        _store.Update(current => current with { ScreenshotFolder = folder.Path.LocalPath });
    }

    private void MediaSettingsButton_Click(object? sender, RoutedEventArgs e) =>
        new MediaSettingsWindow(_store).ShowDialog(this);

    private void LicensesButton_Click(object? sender, RoutedEventArgs e) =>
        new LicensesWindow().ShowDialog(this);

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();
}
