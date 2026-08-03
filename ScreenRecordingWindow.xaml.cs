using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using AndroidWidget.Core.Abstractions;
using AndroidWidget.Core.Devices;
using AndroidWidget.Presentation.Media;

namespace AndroidWidget;

public partial class ScreenRecordingWindow : Window
{
    private readonly AndroidDevice _device;
    private readonly IAndroidDeviceService _devices;
    private readonly ISettingsService _settings;
    private readonly IDesktopIntegration _desktop;
    private readonly DispatcherTimer _stateTimer;
    private readonly Stopwatch _elapsed = new();
    private bool _isRecording;
    private bool _stopping;
    private bool _allowClose;

    public ScreenRecordingWindow(AndroidDevice device, IAndroidDeviceService devices,
        ISettingsService settings, IDesktopIntegration desktop, RecordingStorage recordings)
    {
        _device = device;
        _devices = devices;
        _settings = settings;
        _desktop = desktop;
        OutputPath = recordings.CreateFilePath(device);

        InitializeComponent();
        DeviceNameText.Text = device.DisplayName;
        RecordingPathText.Text = OutputPath;
        _stateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _stateTimer.Tick += StateTimer_Tick;
        Closing += Window_Closing;
    }

    public string OutputPath { get; }

    public bool RecordingCompleted { get; private set; }

    private void PrimaryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_stopping)
            return;
        if (RecordingCompleted)
        {
            _allowClose = true;
            Close();
            return;
        }
        if (_isRecording)
        {
            _ = StopRecordingAsync(closeAfter: false);
            return;
        }

        StartRecording();
    }

    private void StartRecording()
    {
        var result = _devices.StartScreenRecording(_device.Serial, OutputPath,
            _settings.Current.ScrcpyPreset);
        if (!result.IsSuccess)
        {
            StateDot.Fill = (Brush)FindResource("DangerText");
            StateText.Text = "Не удалось начать запись";
            HintText.Text = result.BestMessage;
            return;
        }

        _isRecording = true;
        _elapsed.Restart();
        _stateTimer.Start();
        StateDot.Fill = (Brush)FindResource("DangerText");
        StateText.Text = "Запись идёт · 00:00";
        HintText.Text = "Окно scrcpy показывает записываемый экран. Для сохранения файла остановите запись здесь.";
        PrimaryButton.Content = "Остановить запись";
        PrimaryButton.Background = (Brush)FindResource("DangerText");
        CloseDialogButton.Content = "Закрыть";
    }

    private async Task StopRecordingAsync(bool closeAfter)
    {
        _stopping = true;
        PrimaryButton.IsEnabled = false;
        StateText.Text = "Завершаю запись…";
        var result = await Task.Run(() => _devices.StopScreenRecording(_device.Serial));
        _stopping = false;

        if (!result.IsSuccess && _devices.IsScreenRecording(_device.Serial))
        {
            PrimaryButton.IsEnabled = true;
            StateText.Text = "Запись всё ещё идёт";
            HintText.Text = result.BestMessage;
            return;
        }

        CompleteRecording();
        if (closeAfter)
        {
            _allowClose = true;
            Close();
        }
    }

    private void StateTimer_Tick(object? sender, EventArgs e)
    {
        if (!_isRecording)
            return;
        if (!_devices.IsScreenRecording(_device.Serial))
        {
            CompleteRecording();
            return;
        }

        var elapsed = _elapsed.Elapsed;
        StateText.Text = elapsed.TotalHours >= 1
            ? $"Запись идёт · {elapsed:hh\\:mm\\:ss}"
            : $"Запись идёт · {elapsed:mm\\:ss}";
    }

    private void CompleteRecording()
    {
        _isRecording = false;
        _stateTimer.Stop();
        _elapsed.Stop();
        RecordingCompleted = File.Exists(OutputPath);
        StateDot.Fill = RecordingCompleted ? Brushes.MediumSeaGreen : (Brush)FindResource("DangerText");
        StateText.Text = RecordingCompleted ? "Запись завершена и сохранена" : "Запись завершена без файла";
        HintText.Text = RecordingCompleted
            ? "Проводник не открывается автоматически. При необходимости нажмите «Показать файл»."
            : "scrcpy завершился, но файл записи не найден.";
        RevealButton.Visibility = RecordingCompleted ? Visibility.Visible : Visibility.Collapsed;
        PrimaryButton.IsEnabled = true;
        PrimaryButton.Content = "Готово";
        PrimaryButton.ClearValue(BackgroundProperty);
        CloseDialogButton.Visibility = Visibility.Collapsed;
    }

    private void RevealButton_Click(object sender, RoutedEventArgs e)
    {
        var result = _desktop.RevealFile(OutputPath);
        if (!result.IsSuccess)
            HintText.Text = result.BestMessage;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_isRecording || _allowClose)
        {
            _stateTimer.Stop();
            return;
        }

        e.Cancel = true;
        var answer = MessageBox.Show(this,
            "Запись ещё идёт. Остановить запись, сохранить файл и закрыть окно?",
            "Запись экрана", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Yes)
            _ = StopRecordingAsync(closeAfter: true);
    }
}
