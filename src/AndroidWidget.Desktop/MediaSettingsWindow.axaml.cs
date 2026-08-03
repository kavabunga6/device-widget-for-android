using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace AndroidWidget.Desktop;

internal sealed partial class MediaSettingsWindow : Window
{
    private readonly DesktopSettingsStore _store;
    private bool _loading = true;

    public MediaSettingsWindow(DesktopSettingsStore store)
    {
        _store = store;
        InitializeComponent();
        PresetCombo.ItemsSource = new[] { "Performance", "Balanced", "Quality" };
        var settings = store.Current;
        PresetCombo.SelectedItem = settings.ScrcpyPreset;
        RecordingFolderText.Text = settings.RecordingFolder;
        NotifyPhotosCheck.IsChecked = settings.NotifyNewPhotos;
        AutoImportCheck.IsChecked = settings.AutoImportPhotos;
        PhotoFolderText.Text = settings.PhotoImportFolder;
        _loading = false;
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void PresetCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loading || PresetCombo.SelectedItem is not string preset)
            return;
        _store.Update(current => current with { ScrcpyPreset = preset });
    }

    private void PhotoOption_Click(object? sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        _store.Update(current => current with
        {
            NotifyNewPhotos = NotifyPhotosCheck.IsChecked == true,
            AutoImportPhotos = AutoImportCheck.IsChecked == true
        });
    }

    private async void RecordingFolderButton_Click(object? sender, RoutedEventArgs e)
    {
        var path = await PickFolderAsync("Папка записей");
        if (path is null)
            return;
        RecordingFolderText.Text = path;
        _store.Update(current => current with { RecordingFolder = path });
    }

    private async void PhotoFolderButton_Click(object? sender, RoutedEventArgs e)
    {
        var path = await PickFolderAsync("Папка импорта фотографий");
        if (path is null)
            return;
        PhotoFolderText.Text = path;
        _store.Update(current => current with { PhotoImportFolder = path });
    }

    private async Task<string?> PickFolderAsync(string title)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });
        return folders.FirstOrDefault()?.Path.LocalPath;
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();
}
