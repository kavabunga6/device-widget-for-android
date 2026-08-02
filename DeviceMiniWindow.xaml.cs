using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AndroidWidget.Models;
using AndroidWidget.Presentation.Media;
using AndroidWidget.Presentation.Notifications;
using AndroidWidget.Presentation.Screenshots;
using AndroidWidget.Presentation.Transfers;
using AndroidWidget.Services;
using Microsoft.Win32;

namespace AndroidWidget;

public partial class DeviceMiniWindow : Window
{
    private const double DesignAspectRatio = 188d / 120d;
    private readonly IAndroidDeviceService _devices;
    private readonly ISettingsService _settings;
    private readonly IDesktopIntegration _desktop;
    private readonly ScreenshotStorage _screenshots;
    private readonly RecordingStorage _recordings;
    private readonly TransferQueueService _transfers;
    private readonly PhotoImportService _photoImport;
    private readonly ICompanionService _companion;
    private readonly CompanionCoordinator _companionCoordinator;
    private AndroidDevice _device;
    private Point _mouseDownPoint;
    private bool _dragStarted;
    private bool _actionRunning;
    private readonly NotificationBubbleStack _smsBubbles = new();
    private readonly DispatcherTimer _operationBubbleTimer;

    public string Serial => _device.Serial;

    public DeviceMiniWindow(AndroidDevice device, IAndroidDeviceService devices,
        ISettingsService settings, IDesktopIntegration desktop, ScreenshotStorage screenshots,
        RecordingStorage recordings, TransferQueueService transfers, PhotoImportService photoImport,
        ICompanionService companion, CompanionCoordinator companionCoordinator)
    {
        _devices = devices;
        _settings = settings;
        _desktop = desktop;
        _screenshots = screenshots;
        _recordings = recordings;
        _transfers = transfers;
        _photoImport = photoImport;
        _companion = companion;
        _companionCoordinator = companionCoordinator;
        InitializeComponent();
        SmsBubbleItems.ItemsSource = _smsBubbles.Items;
        _smsBubbles.Changed += SmsBubblesChanged;
        _settings.Changed += SettingsChanged;
        _transfers.Changed += TransfersChanged;
        _photoImport.PhotoDetected += PhotoDetected;
        _operationBubbleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _operationBubbleTimer.Tick += (_, _) => HideOperationBubble();
        _device = device;
        Topmost = _settings.Current.Topmost;
        Closed += (_, _) =>
        {
            _settings.Changed -= SettingsChanged;
            _transfers.Changed -= TransfersChanged;
            _photoImport.PhotoDetected -= PhotoDetected;
            _smsBubbles.Changed -= SmsBubblesChanged;
            _smsBubbles.Dispose();
            _operationBubbleTimer.Stop();
            SmsBubblePopup.IsOpen = false;
            OperationBubblePopup.IsOpen = false;
        };
        Loaded += (_, _) => RefreshSmsBubbleVisibility();
        UpdateDevice(device);
    }

    public void UpdateDevice(AndroidDevice device)
    {
        _device = device;
        ApplySkin(device);
        DeviceNameText.Text = device.DisplayName;
        ConnectionText.Text = string.IsNullOrWhiteSpace(device.AndroidVersion)
            ? device.ConnectionLabel
            : $"{device.ConnectionLabel}\nAndroid {device.AndroidVersion}";

        var sleepingOrLocked = device.State == DeviceConnectionState.Online && (!device.IsScreenOn || device.IsLocked);
        var authorizationRequired = device.State == DeviceConnectionState.Unauthorized;
        var statusColor = authorizationRequired
            ? Color.FromRgb(255, 190, 92)
            : sleepingOrLocked || device.State == DeviceConnectionState.Offline
                ? Color.FromRgb(255, 105, 105)
                : device.State == DeviceConnectionState.Online
                    ? Color.FromRgb(66, 201, 141)
                    : Color.FromRgb(105, 115, 142);
        var statusBrush = new SolidColorBrush(statusColor);
        Card.BorderBrush = statusBrush;
        StatusDot.Fill = statusBrush;

        MiniInactiveScreenSurface.Visibility = sleepingOrLocked ? Visibility.Visible : Visibility.Collapsed;
        BatteryContent.Visibility = device.State == DeviceConnectionState.Online && !sleepingOrLocked
                                    ? Visibility.Visible
                                    : Visibility.Collapsed;
        BatteryContent.Text = device.BatteryPercent is int battery ? $"{battery}%" : "—";
        PowerIcon.Visibility = sleepingOrLocked && !device.IsLocked ? Visibility.Visible : Visibility.Collapsed;
        LockIcon.Visibility = sleepingOrLocked && device.IsLocked ? Visibility.Visible : Visibility.Collapsed;
        AuthorizationIcon.Visibility = authorizationRequired ? Visibility.Visible : Visibility.Collapsed;
        UnavailableStateText.Visibility = device.State is DeviceConnectionState.Offline or DeviceConnectionState.Unknown
            ? Visibility.Visible
            : Visibility.Collapsed;
        UnavailableStateText.Text = device.State == DeviceConnectionState.Offline ? "OFFLINE" : "НЕТ СВЯЗИ";
        UnavailableStateText.Foreground = device.State == DeviceConnectionState.Offline
            ? (Brush)FindResource("DangerText")
            : (Brush)FindResource("TextSecondary");
        DetailText.Text = authorizationRequired
            ? "Подтвердите RSA-ключ"
            : sleepingOrLocked
            ? device.IsLocked ? "Телефон заблокирован" : "Экран выключен"
            : device.State == DeviceConnectionState.Offline
                ? "Устройство offline"
                : device.State == DeviceConnectionState.Online
                    ? "Готов к работе"
                    : "Устройство недоступно";
        DetailText.Foreground = authorizationRequired
            ? (Brush)FindResource("WarningText")
            : sleepingOrLocked || device.State == DeviceConnectionState.Offline
                ? (Brush)FindResource("DangerText")
                : (Brush)FindResource("TextSecondary");
        ToolTip = $"{device.DisplayName}\n{device.Serial}\n{DetailText.Text}";

        if (!_settings.Current.ShowSmsBubbles)
            ClearSmsBubbles();
        else if (device.LatestMessage is not null)
            ShowSmsBubble(device.LatestMessage);
    }

    private void ApplySkin(AndroidDevice device)
    {
        var skin = PhoneSkinResolver.Resolve(device);
        var accent = new SolidColorBrush(skin.Accent);
        Card.Background = new SolidColorBrush(skin.Body);
        Card.CornerRadius = new CornerRadius(Math.Clamp(skin.ShellRadius * 0.76, 18, 29));
        MiniPhoneScreen.CornerRadius = new CornerRadius(Math.Clamp(skin.ScreenRadius * 0.72, 14, 22));
        MiniSkinHomeBar.Background = accent;
    }

    public void PlaceAt(int index)
    {
        var workArea = SystemParameters.WorkArea;
        const double gap = 10;
        var perColumn = Math.Max(1, (int)((workArea.Height - 30) / (Height + gap)));
        var column = index / perColumn;
        var row = index % perColumn;
        Left = workArea.Right - Width - 18 - column * (Width + gap);
        Top = workArea.Bottom - Height - 18 - row * (Height + gap);
    }

    private void ResizeGrip_DragDelta(object sender, DragDeltaEventArgs e)
    {
        var horizontalWidth = Width + e.HorizontalChange;
        var verticalWidth = Width + e.VerticalChange / DesignAspectRatio;
        var targetWidth = Math.Abs(e.HorizontalChange) >= Math.Abs(e.VerticalChange / DesignAspectRatio)
            ? horizontalWidth
            : verticalWidth;
        var workArea = SystemParameters.WorkArea;
        var maximumWidth = Math.Min(MaxWidth, Math.Min(workArea.Right - Left, (workArea.Bottom - Top) / DesignAspectRatio));
        targetWidth = Math.Clamp(targetWidth, MinWidth, Math.Max(MinWidth, maximumWidth));
        Width = targetWidth;
        Height = targetWidth * DesignAspectRatio;
    }

    private void Card_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _mouseDownPoint = e.GetPosition(this);
        _dragStarted = false;
        Card.CaptureMouse();
        e.Handled = true;
    }

    private void Card_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || !Card.IsMouseCaptured || _dragStarted)
            return;
        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _mouseDownPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _mouseDownPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;
        _dragStarted = true;
        Card.ReleaseMouseCapture();
        DragMove();
        e.Handled = true;
    }

    private void Card_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (Card.IsMouseCaptured)
            Card.ReleaseMouseCapture();
        if (!_dragStarted)
            ((App)System.Windows.Application.Current).ShowMainFor(_device.Serial);
        e.Handled = true;
    }

    private void ActionMenu_Opening(object sender, RoutedEventArgs e)
    {
        var enabled = _device.State == DeviceConnectionState.Online && !_actionRunning;
        ScreenMenuItem.IsEnabled = enabled;
        RecordMenuItem.IsEnabled = enabled;
        FilesMenuItem.IsEnabled = enabled;
        ScreenshotMenuItem.IsEnabled = enabled;
        InstallMenuItem.IsEnabled = enabled;
        ShellMenuItem.IsEnabled = enabled;
        ClipboardMenuItem.IsEnabled = enabled;
        PowerMenuItem.IsEnabled = enabled;
        var installed = _device.CompanionState == CompanionInstallationState.Installed;
        CompanionMenuItem.Header = !installed ? "Компаньон" : _device.IsCompanionConnected
            ? "Открыть"
            : "Сопрячь";
        CompanionMenuItem.IsEnabled = enabled && (installed || _companion.IsInstallerAvailable);
        CompanionMenuItem.ToolTip = installed
            ? _device.IsCompanionConnected
                ? "Открыть Device Widget Companion"
                : "Создать код и ссылку сопряжения"
            : _companion.IsInstallerAvailable
                ? "Установить компаньон после подтверждения"
                : "APK компаньона не входит в эту сборку";
    }

    private void ScreenMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var result = _devices.StartScreenMirroring(_device.Serial, _settings.Current.ScrcpyPreset);
        if (result.IsSuccess)
            SetActionStatus("scrcpy запущен ✓");
        else
            SetActionStatus(result.BestMessage, true);
    }

    private void RecordMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var file = _recordings.CreateFilePath(_device);
        var result = _devices.StartScreenRecording(_device.Serial, file, _settings.Current.ScrcpyPreset);
        if (!result.IsSuccess)
        {
            ShowOperationBubble("Запись не начата", result.BestMessage, OperationBubbleState.Error);
            return;
        }
        ShowOperationBubble("Запись экрана", $"{Path.GetFileName(file)} · закройте scrcpy для завершения",
            OperationBubbleState.Success);
        _desktop.OpenFolder(_recordings.Folder);
    }

    private void TransfersMenuItem_Click(object sender, RoutedEventArgs e) =>
        new TransferQueueWindow(_transfers) { Owner = this }.Show();

    private void WirelessMenuItem_Click(object sender, RoutedEventArgs e) =>
        new WirelessPairingWindow(_devices) { Owner = this }.Show();

    private void FilesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        new RemoteFilesWindow(_devices, _desktop, _transfers, _device) { Owner = this }.Show();
        SetActionStatus("Открыт браузер файлов");
    }

    private void SmsBubble_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: NotificationBubbleItem item })
            _smsBubbles.Remove(item);
        e.Handled = true;
    }

    private void ShowSmsBubble(PhoneMessage message)
    {
        _smsBubbles.Add(message);
        RefreshSmsBubbleVisibility();
    }

    private TimeSpan NotificationDisplayDuration => TimeSpan.FromSeconds(
        Math.Clamp(_settings.Current.NotificationDisplaySeconds, 5, 60));

    private void SmsBubblesChanged(object? sender, EventArgs e) => RefreshSmsBubbleVisibility();

    private void RefreshSmsBubbleVisibility()
    {
        var show = IsVisible && !OperationBubblePopup.IsOpen && _settings.Current.ShowSmsBubbles &&
                   _smsBubbles.Items.Count > 0;
        SmsBubblePopup.IsOpen = show;
        if (show)
            _smsBubbles.Start(NotificationDisplayDuration);
        else
            _smsBubbles.Pause();
    }

    private void ClearSmsBubbles()
    {
        SmsBubblePopup.IsOpen = false;
        _smsBubbles.Clear();
    }

    private void SettingsChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        if (!_settings.Current.ShowSmsBubbles)
        {
            ClearSmsBubbles();
            return;
        }

        _smsBubbles.Restart(NotificationDisplayDuration);
        RefreshSmsBubbleVisibility();
    });

    private void TransfersChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        var job = _transfers.Snapshot.FirstOrDefault(item => item.DeviceSerial == _device.Serial);
        if (job is null)
            return;
        var state = job.State switch
        {
            TransferJobState.Queued => OperationBubbleState.Progress,
            TransferJobState.Running => OperationBubbleState.Progress,
            TransferJobState.Completed => OperationBubbleState.Success,
            TransferJobState.Failed => OperationBubbleState.Error,
            TransferJobState.Cancelled => OperationBubbleState.Error,
            _ => OperationBubbleState.Progress
        };
        var message = job.State == TransferJobState.Running && job.Progress is double progress
            ? $"{job.Name} · {progress:P0}"
            : $"{job.Name} · {job.Message}";
        ShowOperationBubble(job.Kind == TransferJobKind.InstallApk ? "Установка APK" : "Передача", message, state);
        OperationProgressBar.IsIndeterminate = job.Progress is null;
        OperationProgressBar.Value = (job.Progress ?? 0) * 100;
    });

    private void PhotoDetected(object? sender, PhotoImportEvent e) => Dispatcher.BeginInvoke(() =>
    {
        if (e.DeviceSerial != _device.Serial)
            return;
        var error = e.Message.StartsWith("Не удалось", StringComparison.Ordinal);
        ShowOperationBubble(e.Imported ? "Новое фото импортировано" : "Новое фото", e.Message,
            error ? OperationBubbleState.Error : OperationBubbleState.Success);
    });

    private void OperationBubble_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        HideOperationBubble();
        e.Handled = true;
    }

    private void ShowOperationBubble(string title, string message, OperationBubbleState state)
    {
        SmsBubblePopup.IsOpen = false;
        _smsBubbles.Pause();
        _operationBubbleTimer.Stop();
        OperationBubbleTitleText.Text = title;
        OperationBubbleMessageText.Text = message;
        OperationProgressBar.Visibility = state == OperationBubbleState.Progress
            ? Visibility.Visible
            : Visibility.Collapsed;

        var color = state switch
        {
            OperationBubbleState.Success => Color.FromRgb(74, 174, 112),
            OperationBubbleState.Error => Color.FromRgb(213, 75, 67),
            _ => Color.FromRgb(128, 106, 244)
        };
        var brush = new SolidColorBrush(color);
        OperationBubbleBorder.BorderBrush = brush;
        OperationBubbleIconBackground.Background = brush;
        OperationBubbleHintText.Foreground = brush;
        OperationBubbleIcon.Text = state switch
        {
            OperationBubbleState.Success => "✓",
            OperationBubbleState.Error => "!",
            _ => "\uE898"
        };
        OperationBubbleIcon.FontFamily = state == OperationBubbleState.Progress
            ? new FontFamily("Segoe Fluent Icons")
            : new FontFamily("Segoe UI");
        OperationBubbleHintText.Text = state switch
        {
            OperationBubbleState.Success => "Готово · нажмите, чтобы скрыть",
            OperationBubbleState.Error => "Не выполнено · нажмите, чтобы скрыть",
            _ => "Не отключайте телефон"
        };
        OperationBubblePopup.IsOpen = true;
        if (state != OperationBubbleState.Progress)
        {
            _operationBubbleTimer.Interval = state == OperationBubbleState.Error
                ? TimeSpan.FromSeconds(10)
                : TimeSpan.FromSeconds(6);
            _operationBubbleTimer.Start();
        }
    }

    private void HideOperationBubble()
    {
        _operationBubbleTimer.Stop();
        OperationBubblePopup.IsOpen = false;
        RefreshSmsBubbleVisibility();
    }

    private async void ScreenshotMenuItem_Click(object sender, RoutedEventArgs e)
    {
        await RunMenuActionAsync(async () =>
        {
            var file = _screenshots.CreateFilePath(_device);
            var result = await _devices.TakeScreenshotAsync(_device.Serial, file);
            if (!result.IsSuccess)
                throw new InvalidOperationException(result.BestMessage);
            var reveal = _desktop.RevealFile(file);
            if (!reveal.IsSuccess)
                throw new InvalidOperationException(reveal.BestMessage);
        }, "Делаю снимок…", "Скриншот сохранён ✓");
    }

    private void InstallMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = $"Установить APK на {_device.DisplayName}",
            Filter = "Android package (*.apk)|*.apk",
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != true)
            return;

        foreach (var path in dialog.FileNames)
            _transfers.EnqueueUpload(_device.Serial, path);
        SetActionStatus(dialog.FileNames.Length == 1
            ? "APK добавлен в очередь"
            : $"В очередь добавлено APK: {dialog.FileNames.Length}");
    }

    private async void CompanionMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_device.CompanionState == CompanionInstallationState.Installed)
        {
            if (_device.IsCompanionConnected)
            {
                var open = await _companionCoordinator.OpenCompanionAsync(_device.Serial);
                SetActionStatus(open.IsSuccess ? "Компаньон открыт" : open.BestMessage,
                    !open.IsSuccess);
                return;
            }
            await ShowPairingAsync();
            return;
        }
        var consent = MessageBox.Show(this,
            $"Установить Device Widget Companion на «{_device.DisplayName}»?\n\n" +
            "Без вашего подтверждения установка не выполняется. Доступ к уведомлениям выдаётся " +
            "отдельно в настройках телефона.",
            "Установка компаньона", MessageBoxButton.YesNo, MessageBoxImage.Question,
            MessageBoxResult.No);
        if (consent != MessageBoxResult.Yes)
        {
            SetActionStatus("Установка отменена");
            return;
        }

        await RunMenuActionAsync(async () =>
        {
            var result = await _companion.InstallAsync(_device.Serial);
            if (!result.IsSuccess)
                throw new InvalidOperationException(result.BestMessage);
            _device = _device with { CompanionState = CompanionInstallationState.Installed };
        }, "Устанавливаю компаньон…",
            "Компаньон установлен и открыт · теперь нажмите «Сопрячь»");
    }

    private async Task ShowPairingAsync()
    {
        SetActionStatus("Создаю ссылку сопряжения…");
        var pairing = await _companionCoordinator.CreateAndOpenPairingAsync(_device.Serial);
        if (pairing.Session is null)
        {
            SetActionStatus(pairing.LaunchResult.BestMessage, true);
            return;
        }
        new CompanionPairingWindow(_device.Serial, _device.DisplayName, pairing, _companionCoordinator)
        {
            Owner = this
        }.Show();
        SetActionStatus(pairing.LaunchResult.IsSuccess
            ? "Компаньон открыт для сопряжения"
            : "Ссылка создана · используйте окно сопряжения",
            !pairing.LaunchResult.IsSuccess);
    }

    private void ShellMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = _devices.StartShell(_device.Serial);
            SetActionStatus(result.IsSuccess ? "ADB shell открыт" : result.BestMessage, !result.IsSuccess);
        }
        catch (Exception ex) { SetActionStatus(ex.Message, true); }
    }

    private async void ClipboardMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!System.Windows.Clipboard.ContainsText())
        {
            SetActionStatus("В буфере нет текста", true);
            return;
        }
        var text = System.Windows.Clipboard.GetText();
        if (text.Length > 1000)
            text = text[..1000];
        await RunMenuActionAsync(async () =>
        {
            var result = await _devices.SendTextAsync(_device.Serial, text);
            if (!result.IsSuccess)
                throw new InvalidOperationException(result.BestMessage);
        }, "Отправляю текст…", "Текст отправлен ✓");
    }

    private async void PowerMenuItem_Click(object sender, RoutedEventArgs e)
    {
        await RunMenuActionAsync(async () =>
        {
            var result = await _devices.TogglePowerAsync(_device.Serial);
            if (!result.IsSuccess)
                throw new InvalidOperationException(result.BestMessage);
        }, "Отправляю команду…", "Команда экрана отправлена ✓");
    }

    private void MtpMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var result = _desktop.OpenMtpDevice(_device);
        SetActionStatus(result.BestMessage, !result.IsSuccess);
    }

    private void SettingsMenuItem_Click(object sender, RoutedEventArgs e) =>
        ((App)System.Windows.Application.Current).ShowSettings();

    private async Task RunMenuActionAsync(Func<Task> action, string progress, string success)
    {
        if (_actionRunning)
            return;
        _actionRunning = true;
        SetActionStatus(progress);
        try
        {
            await action();
            SetActionStatus(success);
        }
        catch (Exception ex)
        {
            SetActionStatus(ex.Message, true);
        }
        finally
        {
            _actionRunning = false;
        }
    }

    private void SetActionStatus(string message, bool error = false)
    {
        DetailText.Text = message;
        DetailText.Foreground = error
            ? (Brush)FindResource("DangerText")
            : (Brush)FindResource("TextSecondary");
    }

    private void Card_DragEnter(object sender, DragEventArgs e)
    {
        var valid = e.Data.GetDataPresent(DataFormats.FileDrop) && _device.State == DeviceConnectionState.Online;
        e.Effects = valid ? DragDropEffects.Copy : DragDropEffects.None;
        DropOverlay.Visibility = valid ? Visibility.Visible : Visibility.Collapsed;
        e.Handled = true;
    }

    private void Card_DragLeave(object sender, DragEventArgs e) => DropOverlay.Visibility = Visibility.Collapsed;

    private void Card_Drop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        if (_device.State != DeviceConnectionState.Online ||
            !e.Data.GetDataPresent(DataFormats.FileDrop))
            return;

        var paths = ((string[]?)e.Data.GetData(DataFormats.FileDrop) ?? Array.Empty<string>())
            .Where(path => File.Exists(path) || Directory.Exists(path)).ToArray();
        if (paths.Length == 0)
            return;

        foreach (var path in paths)
            _transfers.EnqueueUpload(_device.Serial, path);
        ShowOperationBubble("Очередь передач",
            paths.Length == 1 ? Path.GetFileName(paths[0]) : $"Добавлено объектов: {paths.Length}",
            OperationBubbleState.Progress);
        SetActionStatus("Передача добавлена в очередь");
        e.Handled = true;
    }

    private enum OperationBubbleState
    {
        Progress,
        Success,
        Error
    }

}
