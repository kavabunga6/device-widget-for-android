using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AndroidWidget.Models;
using AndroidWidget.Presentation.Notifications;
using AndroidWidget.Presentation.Screenshots;
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
    private readonly ICompanionService _companion;
    private readonly CompanionCoordinator _companionCoordinator;
    private AndroidDevice _device;
    private Point _mouseDownPoint;
    private bool _dragStarted;
    private bool _transferring;
    private bool _actionRunning;
    private readonly NotificationBubbleStack _smsBubbles = new();
    private readonly DispatcherTimer _operationBubbleTimer;

    public string Serial => _device.Serial;

    public DeviceMiniWindow(AndroidDevice device, IAndroidDeviceService devices,
        ISettingsService settings, IDesktopIntegration desktop, ScreenshotStorage screenshots,
        ICompanionService companion, CompanionCoordinator companionCoordinator)
    {
        _devices = devices;
        _settings = settings;
        _desktop = desktop;
        _screenshots = screenshots;
        _companion = companion;
        _companionCoordinator = companionCoordinator;
        InitializeComponent();
        SmsBubbleItems.ItemsSource = _smsBubbles.Items;
        _smsBubbles.Changed += SmsBubblesChanged;
        _settings.Changed += SettingsChanged;
        _operationBubbleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _operationBubbleTimer.Tick += (_, _) => HideOperationBubble();
        _device = device;
        Topmost = _settings.Current.Topmost;
        Closed += (_, _) =>
        {
            _settings.Changed -= SettingsChanged;
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
        if (!_dragStarted && !_transferring)
            ((App)System.Windows.Application.Current).ShowMainFor(_device.Serial);
        e.Handled = true;
    }

    private void ActionMenu_Opening(object sender, RoutedEventArgs e)
    {
        var enabled = _device.State == DeviceConnectionState.Online && !_actionRunning && !_transferring;
        ScreenMenuItem.IsEnabled = enabled;
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
                ? "Открыть Android Widget Companion"
                : "Создать код и ссылку сопряжения"
            : _companion.IsInstallerAvailable
                ? "Установить компаньон после подтверждения"
                : "APK компаньона не входит в эту сборку";
    }

    private void ScreenMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var result = _devices.StartScreenMirroring(_device.Serial);
        if (result.IsSuccess)
            SetActionStatus("scrcpy запущен ✓");
        else
            SetActionStatus(result.BestMessage, true);
    }

    private void FilesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        new RemoteFilesWindow(_devices, _desktop, _device) { Owner = this }.Show();
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

    private async void InstallMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = $"Установить APK на {_device.DisplayName}",
            Filter = "Android package (*.apk)|*.apk",
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != true)
            return;

        await RunMenuActionAsync(async () =>
        {
            for (var index = 0; index < dialog.FileNames.Length; index++)
            {
                SetActionStatus($"Установка {index + 1}/{dialog.FileNames.Length}…");
                var result = await _devices.InstallApkAsync(_device.Serial, dialog.FileNames[index]);
                if (!result.IsSuccess)
                    throw new InvalidOperationException(result.BestMessage);
            }
        }, "Устанавливаю APK…", "Приложение установлено ✓");
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
            $"Установить Android Widget Companion на «{_device.DisplayName}»?\n\n" +
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

    private async void Card_Drop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        if (_transferring || _device.State != DeviceConnectionState.Online ||
            !e.Data.GetDataPresent(DataFormats.FileDrop))
            return;

        var paths = ((string[]?)e.Data.GetData(DataFormats.FileDrop) ?? Array.Empty<string>())
            .Where(path => File.Exists(path) || Directory.Exists(path)).ToArray();
        if (paths.Length == 0)
            return;

        _transferring = true;
        string? currentName = null;
        try
        {
            var apkCount = 0;
            for (var index = 0; index < paths.Length; index++)
            {
                var path = paths[index];
                var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
                currentName = name;
                var isApk = File.Exists(path) && Path.GetExtension(path).Equals(".apk", StringComparison.OrdinalIgnoreCase);
                if (isApk)
                    apkCount++;
                var progress = paths.Length == 1 ? name : $"{name} · {index + 1} из {paths.Length}";
                if (File.Exists(path))
                    progress += $" · {FormatFileSize(new FileInfo(path).Length)}";
                ShowOperationBubble(isApk ? "Установка APK" : "Передача на телефон", progress,
                    OperationBubbleState.Progress);
                SetActionStatus(isApk ? $"Устанавливаю {name}…" : $"Передаю {name}…");
                var result = isApk
                    ? await _devices.InstallApkAsync(_device.Serial, path)
                    : await _devices.PushFileAsync(_device.Serial, path);
                if (!result.IsSuccess)
                    throw new InvalidOperationException(result.BestMessage);
            }
            var successTitle = apkCount == paths.Length
                ? paths.Length == 1 ? "APK установлен" : "APK установлены"
                : apkCount == 0
                    ? paths.Length == 1 ? "Файл передан" : "Файлы переданы"
                    : "Файлы обработаны";
            var successMessage = paths.Length == 1
                ? Path.GetFileName(paths[0].TrimEnd(Path.DirectorySeparatorChar))
                : $"Успешно: {paths.Length}";
            ShowOperationBubble(successTitle, successMessage, OperationBubbleState.Success);
            SetActionStatus(paths.Length == 1 ? "Готово ✓" : $"Обработано: {paths.Length} ✓");
        }
        catch (Exception ex)
        {
            var error = string.IsNullOrWhiteSpace(currentName) ? ex.Message : $"{currentName}: {ex.Message}";
            ShowOperationBubble("Ошибка операции", error, OperationBubbleState.Error);
            SetActionStatus(ex.Message, true);
        }
        finally
        {
            _transferring = false;
        }
        e.Handled = true;
    }

    private static string FormatFileSize(long bytes)
    {
        string[] units = { "Б", "КБ", "МБ", "ГБ" };
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes} {units[unit]}" : $"{value:0.#} {units[unit]}";
    }

    private enum OperationBubbleState
    {
        Progress,
        Success,
        Error
    }

}
