using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AndroidWidget.Services;

namespace AndroidWidget;

public partial class SettingsWindow : Window
{
    private bool _loading = true;

    public SettingsWindow()
    {
        InitializeComponent();
        var settings = SettingsService.Current;
        DarkThemeRadio.IsChecked = settings.Theme == WidgetTheme.Dark;
        LightThemeRadio.IsChecked = settings.Theme == WidgetTheme.Light;
        AutoStartToggle.IsChecked = settings.AutoStart;
        _loading = false;
    }

    private void ThemeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        var theme = LightThemeRadio.IsChecked == true ? WidgetTheme.Light : WidgetTheme.Dark;
        SettingsService.Update(settings => settings with { Theme = theme });
        ThemeService.Apply(theme);
        StatusText.Text = theme == WidgetTheme.Light ? "Включена светлая тема" : "Включена тёмная тема";
    }

    private void AutoStartToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        var enabled = AutoStartToggle.IsChecked == true;
        if (SettingsService.TrySetAutoStart(enabled, out var error))
        {
            StatusText.Foreground = (Brush)FindResource("TextSecondary");
            StatusText.Text = enabled ? "Автозапуск включён" : "Автозапуск выключен";
            return;
        }

        _loading = true;
        AutoStartToggle.IsChecked = !enabled;
        _loading = false;
        StatusText.Foreground = new SolidColorBrush(Color.FromRgb(220, 70, 70));
        StatusText.Text = $"Не удалось изменить автозапуск: {error}";
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && e.OriginalSource is not Button)
            DragMove();
    }
}
