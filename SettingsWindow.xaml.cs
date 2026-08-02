using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AndroidWidget.Presentation.Media;
using AndroidWidget.Presentation.Screenshots;
using AndroidWidget.Services;
using Forms = System.Windows.Forms;

namespace AndroidWidget;

public partial class SettingsWindow : Window
{
    private readonly ISettingsService _settings;
    private readonly ScreenshotStorage _screenshots;
    private readonly RecordingStorage _recordings;
    private readonly PhotoImportService _photoImport;
    private bool _loading = true;

    public SettingsWindow(ISettingsService settings, ScreenshotStorage screenshots,
        RecordingStorage recordings, PhotoImportService photoImport)
    {
        _settings = settings;
        _screenshots = screenshots;
        _recordings = recordings;
        _photoImport = photoImport;
        InitializeComponent();
        var current = _settings.Current;
        DarkThemeRadio.IsChecked = current.Theme == WidgetTheme.Dark;
        LightThemeRadio.IsChecked = current.Theme == WidgetTheme.Light;
        AutoStartToggle.IsChecked = current.AutoStart;
        SmsBubblesToggle.IsChecked = current.ShowSmsBubbles;
        SetNotificationDurationChoice(current.NotificationDisplaySeconds);
        ScreenshotFolderText.Text = _screenshots.Folder;
        _loading = false;
    }

    private void ThemeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        var theme = LightThemeRadio.IsChecked == true ? WidgetTheme.Light : WidgetTheme.Dark;
        _settings.Update(settings => settings with { Theme = theme });
        ThemeService.Apply(theme);
        StatusText.Text = theme == WidgetTheme.Light ? "Включена светлая тема" : "Включена тёмная тема";
    }

    private void AutoStartToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        var enabled = AutoStartToggle.IsChecked == true;
        var result = _settings.SetAutoStart(enabled);
        if (result.IsSuccess)
        {
            StatusText.Foreground = (Brush)FindResource("TextSecondary");
            StatusText.Text = enabled ? "Автозапуск включён" : "Автозапуск выключен";
            return;
        }

        _loading = true;
        AutoStartToggle.IsChecked = !enabled;
        _loading = false;
        StatusText.Foreground = (Brush)FindResource("DangerText");
        StatusText.Text = $"Не удалось изменить автозапуск: {result.BestMessage}";
    }

    private void SmsBubblesToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        var enabled = SmsBubblesToggle.IsChecked == true;
        _settings.Update(settings => settings with { ShowSmsBubbles = enabled });
        StatusText.Foreground = (Brush)FindResource("TextSecondary");
        StatusText.Text = enabled ? "Баблы уведомлений включены" : "Баблы уведомлений выключены";
    }

    private void NotificationDuration_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;

        var seconds = Duration5Radio.IsChecked == true ? 5
            : Duration15Radio.IsChecked == true ? 15
            : Duration30Radio.IsChecked == true ? 30
            : Duration60Radio.IsChecked == true ? 60
            : 10;
        _settings.Update(settings => settings with { NotificationDisplaySeconds = seconds });
        StatusText.Foreground = (Brush)FindResource("TextSecondary");
        StatusText.Text = $"Уведомления будут показаны {seconds} секунд";
    }

    private void SetNotificationDurationChoice(int seconds)
    {
        var choice = new[] { 5, 10, 15, 30, 60 }
            .OrderBy(value => Math.Abs(value - seconds))
            .First();
        Duration5Radio.IsChecked = choice == 5;
        Duration10Radio.IsChecked = choice == 10;
        Duration15Radio.IsChecked = choice == 15;
        Duration30Radio.IsChecked = choice == 30;
        Duration60Radio.IsChecked = choice == 60;
    }

    private void ScreenshotFolderButton_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Выберите папку для скриншотов Android Widget",
            UseDescriptionForTitle = true,
            SelectedPath = _screenshots.Folder,
            ShowNewFolderButton = true
        };
        if (dialog.ShowDialog() != Forms.DialogResult.OK)
            return;

        try
        {
            _screenshots.SetFolder(dialog.SelectedPath);
            ScreenshotFolderText.Text = _screenshots.Folder;
            StatusText.Foreground = (Brush)FindResource("TextSecondary");
            StatusText.Text = "Папка для скриншотов обновлена";
        }
        catch (Exception ex)
        {
            StatusText.Foreground = (Brush)FindResource("DangerText");
            StatusText.Text = $"Не удалось использовать папку: {ex.Message}";
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void MediaSettingsButton_Click(object sender, RoutedEventArgs e) =>
        new MediaSettingsWindow(_settings, _recordings, _photoImport) { Owner = this }.ShowDialog();

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && e.OriginalSource is not Button)
            DragMove();
    }
}
