using System.Windows;
using System.Windows.Controls;
using AndroidWidget.Core.Devices;
using AndroidWidget.Presentation.Media;
using Forms = System.Windows.Forms;

namespace AndroidWidget;

public partial class MediaSettingsWindow : Window
{
    private readonly ISettingsService _settings;
    private readonly RecordingStorage _recordings;
    private readonly PhotoImportService _photoImport;
    private bool _loading = true;

    public MediaSettingsWindow(ISettingsService settings, RecordingStorage recordings,
        PhotoImportService photoImport)
    {
        _settings = settings;
        _recordings = recordings;
        _photoImport = photoImport;
        InitializeComponent();
        PresetCombo.ItemsSource = Enum.GetValues<ScrcpyPreset>();
        PresetCombo.SelectedItem = settings.Current.ScrcpyPreset;
        RecordingFolderText.Text = recordings.Folder;
        NotifyPhotosCheck.IsChecked = settings.Current.NotifyNewPhotos;
        AutoImportCheck.IsChecked = settings.Current.AutoImportPhotos;
        PhotoFolderText.Text = photoImport.Folder;
        _loading = false;
    }

    private void PresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || PresetCombo.SelectedItem is not ScrcpyPreset preset)
            return;
        _settings.Update(settings => settings with { ScrcpyPreset = preset });
        StatusText.Text = $"Выбран пресет: {preset}";
    }

    private void PhotoOption_Click(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        _settings.Update(settings => settings with
        {
            NotifyNewPhotos = NotifyPhotosCheck.IsChecked == true,
            AutoImportPhotos = AutoImportCheck.IsChecked == true
        });
        StatusText.Text = AutoImportCheck.IsChecked == true
            ? "Автоматический импорт включён"
            : "Настройки фотографий обновлены";
    }

    private void RecordingFolderButton_Click(object sender, RoutedEventArgs e) =>
        SelectFolder("Выберите папку для записей экрана", _recordings.Folder, folder =>
        {
            _recordings.SetFolder(folder);
            RecordingFolderText.Text = _recordings.Folder;
        });

    private void PhotoFolderButton_Click(object sender, RoutedEventArgs e) =>
        SelectFolder("Выберите папку автоматического импорта", _photoImport.Folder, folder =>
        {
            _photoImport.SetFolder(folder);
            PhotoFolderText.Text = _photoImport.Folder;
        });

    private void SelectFolder(string description, string selectedPath, Action<string> apply)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = description,
            UseDescriptionForTitle = true,
            SelectedPath = selectedPath,
            ShowNewFolderButton = true
        };
        if (dialog.ShowDialog() != Forms.DialogResult.OK)
            return;
        try
        {
            apply(dialog.SelectedPath);
            StatusText.Text = "Папка обновлена";
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
