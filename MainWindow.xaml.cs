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

public partial class MainWindow : Window
{
    public event EventHandler<IReadOnlyList<AndroidDevice>>? DevicesUpdated;
    private const double CompactWidth = 258;
    private const double CompactHeight = 392;
    private const double CompactMinWidth = 230;
    private const double ExpandedPanelSpace = 330;
    private const double ExpandedMinWidth = CompactMinWidth + ExpandedPanelSpace;
    private readonly IAndroidDeviceService _devicesService;
    private readonly ISettingsService _settings;
    private readonly IDesktopIntegration _desktop;
    private readonly IAppLogger _logger;
    private readonly ScreenshotStorage _screenshots;
    private readonly ICompanionService _companion;
    private readonly CompanionCoordinator _companionCoordinator;
    private readonly DispatcherTimer _refreshTimer;
    private readonly NotificationBubbleStack _smsBubbles = new();
    private readonly CancellationTokenSource _lifetime = new();
    private IReadOnlyList<AndroidDevice> _devices = Array.Empty<AndroidDevice>();
    private AndroidDevice? _activeDevice;
    private string? _boundSerial;
    private bool _refreshing;
    private bool _menuOpen;
    private bool _operationInProgress;

    public MainWindow(IAndroidDeviceService devicesService, ISettingsService settings,
        IDesktopIntegration desktop, IAppLogger logger, ScreenshotStorage screenshots,
        ICompanionService companion, CompanionCoordinator companionCoordinator)
    {
        _devicesService = devicesService;
        _settings = settings;
        _desktop = desktop;
        _logger = logger;
        _screenshots = screenshots;
        _companion = companion;
        _companionCoordinator = companionCoordinator;
        _companionCoordinator.LinkChanged += CompanionLinkChanged;
        _companionCoordinator.MessageReceived += CompanionMessageReceived;
        _logger.Write("MainWindow constructor begin");
        InitializeComponent();
        _logger.Write("MainWindow XAML initialized");
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _refreshTimer.Tick += async (_, _) => await RefreshDevicesAsync();
        SmsBubbleItems.ItemsSource = _smsBubbles.Items;
        _smsBubbles.Changed += SmsBubblesChanged;
        _settings.Changed += SettingsChanged;
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible)
                ClearSmsBubbles();
            else
                RefreshSmsBubbleVisibility();
        };
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _logger.Write("MainWindow loaded");
        RestoreSettings();
        var companionHost = await _companionCoordinator.StartAsync(_lifetime.Token);
        if (!companionHost.IsSuccess)
            SetOperationStatus($"Не удалось запустить Companion Host: {companionHost.BestMessage}", true);
        await RefreshDevicesAsync();
        _refreshTimer.Start();
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (System.Windows.Application.Current is App app && !app.IsExiting)
        {
            e.Cancel = true;
            app.HideToTray();
            return;
        }
        _refreshTimer.Stop();
        _smsBubbles.Changed -= SmsBubblesChanged;
        _smsBubbles.Dispose();
        _settings.Changed -= SettingsChanged;
        _lifetime.Cancel();
        _companionCoordinator.LinkChanged -= CompanionLinkChanged;
        _companionCoordinator.MessageReceived -= CompanionMessageReceived;
        SaveSettings();
    }

    private async Task RefreshDevicesAsync(bool force = false)
    {
        if (_refreshing || (_operationInProgress && !force))
            return;

        _refreshing = true;
        try
        {
            var discovered = await _devicesService.GetDevicesAsync(_lifetime.Token);
            _companionCoordinator.RetainAdbRoutes(discovered.Select(device => device.Serial));
            foreach (var installedDevice in discovered.Where(device =>
                         device.State == DeviceConnectionState.Online &&
                         device.CompanionState == CompanionInstallationState.Installed))
                await _companionCoordinator.EnsureAdbRouteAsync(installedDevice.Serial, _lifetime.Token);
            var devices = discovered.Select(device =>
            {
                var link = _companionCoordinator.GetLinkState(device.Serial);
                return device with
                {
                    IsCompanionConnected = link.IsConnected,
                    CompanionNotificationAccess = link.HasNotificationAccess
                };
            }).ToList();
            _devices = devices;
            var selected = _boundSerial is not null
                ? devices.FirstOrDefault(device => device.Serial == _boundSerial)
                : devices.FirstOrDefault(device => device.State == DeviceConnectionState.Online)
                  ?? devices.FirstOrDefault();
            SetActiveDevice(selected);
            DevicesUpdated?.Invoke(this, devices);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _devices = Array.Empty<AndroidDevice>();
            SetActiveDevice(null, ex.Message);
            DevicesUpdated?.Invoke(this, _devices);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void CompanionLinkChanged(object? sender, CompanionLinkState state) => Dispatcher.BeginInvoke(() =>
    {
        var changed = false;
        _devices = _devices.Select(device =>
        {
            if (device.Serial != state.Serial)
                return device;
            changed = true;
            return device with
            {
                IsCompanionConnected = state.IsConnected,
                CompanionNotificationAccess = state.HasNotificationAccess
            };
        }).ToList();
        if (!changed)
            return;
        if (_activeDevice?.Serial == state.Serial)
            SetActiveDevice(_devices.First(device => device.Serial == state.Serial));
        DevicesUpdated?.Invoke(this, _devices);
    });

    private void CompanionMessageReceived(object? sender, CompanionPhoneMessage received) =>
        Dispatcher.BeginInvoke(() =>
        {
            if (!_settings.Current.ShowSmsBubbles)
                return;
            var changed = false;
            _devices = _devices.Select(device =>
            {
                if (device.Serial != received.Serial)
                    return device;
                changed = true;
                return device with { LatestMessage = received.Message };
            }).ToList();
            if (!changed)
                return;
            if (_activeDevice?.Serial == received.Serial)
                SetActiveDevice(_devices.First(device => device.Serial == received.Serial));
            DevicesUpdated?.Invoke(this, _devices);
        });

    private void SetActiveDevice(AndroidDevice? device, string? error = null)
    {
        _activeDevice = device;
        if (device is null)
        {
            ClearSmsBubbles();
            PowerStateOverlay.Visibility = Visibility.Collapsed;
            AuthorizationOverlay.Visibility = Visibility.Collapsed;
            DeviceNameText.Text = "Устройство не найдено";
            ConnectionText.Text = "Подключите USB и разрешите отладку";
            BatteryText.Text = "—";
            BatteryBar.Value = 0;
            DropHintText.Text = "Ожидаю Android по ADB";
            StatusText.Text = error ?? "Проверьте USB debugging или Wi-Fi ADB";
            PanelStatusText.Text = error ?? "Нет подключённых устройств";
            UpdateCompanionUi(null);
            return;
        }

        ApplyDeviceSkin(device);
        var authorizationRequired = device.State == DeviceConnectionState.Unauthorized;
        AuthorizationOverlay.Visibility = authorizationRequired ? Visibility.Visible : Visibility.Collapsed;
        if (authorizationRequired)
            PhoneShell.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 184, 77));

        DeviceNameText.Text = device.DisplayName;
        ConnectionText.Text = $"{device.ConnectionLabel}  ·  Android {device.AndroidVersion}";
        BatteryText.Text = device.BatteryPercent is int battery ? $"{battery}%" : "—";
        BatteryBar.Value = device.BatteryPercent ?? 0;
        BatteryBar.Foreground = new SolidColorBrush(device.BatteryPercent switch
        {
            < 20 => Color.FromRgb(255, 105, 105),
            < 45 => Color.FromRgb(255, 190, 92),
            _ => Color.FromRgb(114, 216, 162)
        });
        var sleepingOrLocked = device.State == DeviceConnectionState.Online && (!device.IsScreenOn || device.IsLocked);
        PowerStateOverlay.Visibility = sleepingOrLocked ? Visibility.Visible : Visibility.Collapsed;
        MainLockIcon.Visibility = sleepingOrLocked && device.IsLocked ? Visibility.Visible : Visibility.Collapsed;
        MainPowerIcon.Visibility = sleepingOrLocked && !device.IsLocked ? Visibility.Visible : Visibility.Collapsed;
        PowerStateText.Text = device.IsLocked ? "Телефон заблокирован" : "Экран выключен";

        switch (device.State)
        {
            case DeviceConnectionState.Online:
                DropHintText.Text = "Перетащите файл или APK";
                StatusText.Text = _operationInProgress ? StatusText.Text : "Нажмите, чтобы открыть действия";
                PanelStatusText.Text = _operationInProgress ? PanelStatusText.Text : $"Подключено: {device.Serial}";
                break;
            case DeviceConnectionState.Unauthorized:
                DropHintText.Text = "Подтвердите RSA-ключ на телефоне";
                StatusText.Text = "ADB ожидает разрешение отладки";
                PanelStatusText.Text = "Устройство не авторизовано";
                break;
            default:
                DropHintText.Text = "Устройство недоступно";
                StatusText.Text = "Переподключите кабель или Wi-Fi ADB";
                PanelStatusText.Text = "ADB: устройство offline";
                break;
        }

        UpdateCompanionUi(device);

        if (!_settings.Current.ShowSmsBubbles)
            ClearSmsBubbles();
        else if (CanShowMessageBubble && device.LatestMessage is not null)
            ShowSmsBubble(device.LatestMessage);
    }

    private AndroidDevice? RequireOnlineDevice()
    {
        if (_activeDevice?.State == DeviceConnectionState.Online)
            return _activeDevice;

        SetOperationStatus("Сначала подключите и авторизуйте Android-устройство", true);
        return null;
    }

    private void ApplyDeviceSkin(AndroidDevice device)
    {
        var skin = PhoneSkinResolver.Resolve(device);
        var accent = new SolidColorBrush(skin.Accent);
        PhoneShell.CornerRadius = new CornerRadius(skin.ShellRadius);
        PhoneShell.Background = new SolidColorBrush(skin.Body);
        PhoneShell.BorderBrush = accent;
        PhoneBezelGrid.Margin = skin.Bezel;
        PhoneScreen.CornerRadius = new CornerRadius(skin.ScreenRadius);
        SkinDeviceBadge.BorderBrush = accent;
        SkinAndroidFace.Fill = accent;
        SkinAntennaLeft.Fill = accent;
        SkinAntennaRight.Fill = accent;
        SkinPowerButton.Background = accent;
        SkinVolumeButton.Background = accent;
        SkinPowerButton.Visibility = skin.HasSideButtons ? Visibility.Visible : Visibility.Collapsed;
        SkinVolumeButton.Visibility = skin.HasSideButtons ? Visibility.Visible : Visibility.Collapsed;
        ApplyCameraCutout(skin.Camera);
        PhoneShell.ToolTip = $"Скин: {skin.Family}\n{device.Manufacturer} {device.Model}";
    }

    private void ApplyCameraCutout(CameraCutout camera)
    {
        SkinCameraCutout.Visibility = camera == CameraCutout.None ? Visibility.Collapsed : Visibility.Visible;
        SkinCameraCutout.Width = camera == CameraCutout.Pill ? 22 : 7;
        SkinCameraCutout.Height = 7;
        SkinCameraCutout.CornerRadius = new CornerRadius(4);
        SkinCameraCutout.HorizontalAlignment = camera == CameraCutout.LeftPunch
            ? HorizontalAlignment.Left
            : HorizontalAlignment.Center;
        SkinCameraCutout.Margin = camera == CameraCutout.LeftPunch
            ? new Thickness(28, 6, 0, 0)
            : new Thickness(0, 6, 0, 0);
    }

    private void Phone_DragEnter(object sender, DragEventArgs e)
    {
        var valid = e.Data.GetDataPresent(DataFormats.FileDrop) && _activeDevice?.State == DeviceConnectionState.Online;
        e.Effects = valid ? DragDropEffects.Copy : DragDropEffects.None;
        DropOverlay.Visibility = valid ? Visibility.Visible : Visibility.Collapsed;
        e.Handled = true;
    }

    private void Phone_DragLeave(object sender, DragEventArgs e) => DropOverlay.Visibility = Visibility.Collapsed;

    private async void Phone_Drop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        var device = RequireOnlineDevice();
        if (device is null || !e.Data.GetDataPresent(DataFormats.FileDrop))
            return;

        var paths = (string[]?)e.Data.GetData(DataFormats.FileDrop) ?? Array.Empty<string>();
        paths = paths.Where(path => File.Exists(path) || Directory.Exists(path)).ToArray();
        if (paths.Length == 0)
            return;

        await RunOperationAsync(async token =>
        {
            for (var index = 0; index < paths.Length; index++)
            {
                var path = paths[index];
                var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
                var isApk = File.Exists(path) && Path.GetExtension(path).Equals(".apk", StringComparison.OrdinalIgnoreCase);
                SetOperationStatus(isApk
                    ? $"Устанавливаю {name} ({index + 1}/{paths.Length})…"
                    : $"Копирую {name} ({index + 1}/{paths.Length})…");

                var result = isApk
                    ? await _devicesService.InstallApkAsync(device.Serial, path, token)
                    : await _devicesService.PushFileAsync(device.Serial, path, token);
                if (!result.IsSuccess)
                    throw new InvalidOperationException($"{name}: {result.BestMessage}");
            }

            SetOperationStatus(paths.Length == 1 ? "Готово ✓" : $"Готово: {paths.Length} объектов ✓");
        });
    }

    private void DragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not Button)
            DragMove();
    }

    private void ResizeGrip_DragDelta(object sender, DragDeltaEventArgs e)
    {
        var workArea = SystemParameters.WorkArea;
        var maximumWidth = Math.Min(MaxWidth, workArea.Right - Left);
        var maximumHeight = Math.Min(MaxHeight, workArea.Bottom - Top);
        Width = Math.Clamp(Width + e.HorizontalChange, MinWidth, Math.Max(MinWidth, maximumWidth));
        Height = Math.Clamp(Height + e.VerticalChange, MinHeight, Math.Max(MinHeight, maximumHeight));
    }

    private void ResizeGrip_DragCompleted(object sender, DragCompletedEventArgs e) => SaveSettings();

    private void PhoneScreen_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is Button)
            return;
        ToggleActionPanel();
    }

    private void MiniModeButton_Click(object sender, RoutedEventArgs e)
    {
        SaveSettings();
        ((App)System.Windows.Application.Current).EnterMiniMode();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e) =>
        ((App)System.Windows.Application.Current).ShowSettings();

    private void ToggleActionPanel(bool? open = null)
    {
        var nextOpen = open ?? !_menuOpen;
        if (nextOpen == _menuOpen)
            return;

        if (nextOpen)
        {
            MinWidth = ExpandedMinWidth;
            Width = Math.Min(Math.Max(ExpandedMinWidth, Width + ExpandedPanelSpace), MaxWidth);
        }
        else
        {
            MinWidth = CompactMinWidth;
            Width = Math.Max(CompactMinWidth, Width - ExpandedPanelSpace);
        }

        _menuOpen = nextOpen;
        ActionPanel.Visibility = _menuOpen ? Visibility.Visible : Visibility.Collapsed;
        KeepWindowOnScreen();
    }

    private void CollapsePanel_Click(object sender, RoutedEventArgs e) => ToggleActionPanel(false);
    private void CloseButton_Click(object sender, RoutedEventArgs e) =>
        ((App)System.Windows.Application.Current).HideToTray();

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        _settings.Update(settings => settings with { Topmost = Topmost });
        PinButton.Foreground = new SolidColorBrush(Topmost ? Color.FromRgb(138, 115, 255) : Color.FromRgb(120, 132, 163));
        SetOperationStatus(Topmost ? "Виджет закреплён поверх окон" : "Режим «поверх окон» выключен");
    }

    public void SelectDevice(string serial)
    {
        _boundSerial = serial;
        var selected = _devices.FirstOrDefault(device => device.Serial == serial);
        if (selected is null)
        {
            SetActiveDevice(null);
            return;
        }
        SetActiveDevice(selected);
    }

    private void ScreenButton_Click(object sender, RoutedEventArgs e)
    {
        var device = RequireOnlineDevice();
        if (device is null)
            return;
        var result = _devicesService.StartScreenMirroring(device.Serial);
        if (result.IsSuccess)
            SetOperationStatus("scrcpy запущен ✓");
        else
            SetOperationStatus(result.BestMessage, true);
    }

    private void SmsBubble_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: NotificationBubbleItem item })
            _smsBubbles.Remove(item);
        e.Handled = true;
    }

    private void ShowSmsBubble(PhoneMessage message)
    {
        // A WPF Popup can remain visible even when its owning window is hidden.
        // In mini mode the per-device mini window owns notifications, so the
        // background main window must never open a duplicate popup.
        if (!CanShowMessageBubble)
        {
            ClearSmsBubbles();
            return;
        }

        _smsBubbles.Add(message);
        RefreshSmsBubbleVisibility();
    }

    private bool CanShowMessageBubble => IsVisible && WindowState != WindowState.Minimized;

    private TimeSpan NotificationDisplayDuration => TimeSpan.FromSeconds(
        Math.Clamp(_settings.Current.NotificationDisplaySeconds, 5, 60));

    private void SmsBubblesChanged(object? sender, EventArgs e) => RefreshSmsBubbleVisibility();

    private void RefreshSmsBubbleVisibility()
    {
        var show = CanShowMessageBubble && _settings.Current.ShowSmsBubbles && _smsBubbles.Items.Count > 0;
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

    private void FilesButton_Click(object sender, RoutedEventArgs e)
    {
        var device = RequireOnlineDevice();
        if (device is null)
            return;
        new RemoteFilesWindow(_devicesService, _desktop, device) { Owner = this }.Show();
        SetOperationStatus("Открыт ADB-браузер файлов");
    }

    private async void ScreenshotButton_Click(object sender, RoutedEventArgs e)
    {
        var device = RequireOnlineDevice();
        if (device is null)
            return;

        await RunOperationAsync(async token =>
        {
            SetOperationStatus("Делаю снимок экрана…");
            var file = _screenshots.CreateFilePath(device);
            var result = await _devicesService.TakeScreenshotAsync(device.Serial, file, token);
            if (!result.IsSuccess)
                throw new InvalidOperationException(result.BestMessage);
            SetOperationStatus($"Сохранено: {Path.GetFileName(file)} ✓");
            var reveal = _desktop.RevealFile(file);
            if (!reveal.IsSuccess)
                throw new InvalidOperationException(reveal.BestMessage);
        });
    }

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        var device = RequireOnlineDevice();
        if (device is null)
            return;

        var dialog = new OpenFileDialog { Filter = "Android package (*.apk)|*.apk", Multiselect = true };
        if (dialog.ShowDialog(this) != true)
            return;
        await InstallApksAsync(device, dialog.FileNames);
    }

    private async void CompanionButton_Click(object sender, RoutedEventArgs e)
    {
        var device = RequireOnlineDevice();
        if (device is null)
            return;
        if (device.CompanionState == CompanionInstallationState.Installed)
        {
            if (device.IsCompanionConnected)
            {
                var open = await _companionCoordinator.OpenCompanionAsync(device.Serial, _lifetime.Token);
                SetOperationStatus(open.IsSuccess
                    ? "Компаньон открыт на телефоне"
                    : open.BestMessage, !open.IsSuccess);
                return;
            }
            await ShowPairingAsync(device);
            return;
        }
        if (!_companion.IsInstallerAvailable)
        {
            SetOperationStatus("APK компаньона не входит в эту desktop-сборку", true);
            return;
        }

        var consent = MessageBox.Show(this,
            $"Установить Android Widget Companion на «{device.DisplayName}»?\n\n" +
            "Установка начнётся только после вашего подтверждения. Доступ к уведомлениям приложение " +
            "попросит отдельно на телефоне — автоматически он не выдаётся.",
            "Установка компаньона", MessageBoxButton.YesNo, MessageBoxImage.Question,
            MessageBoxResult.No);
        if (consent != MessageBoxResult.Yes)
        {
            SetOperationStatus("Установка компаньона отменена");
            return;
        }

        await RunOperationAsync(async token =>
        {
            SetOperationStatus("Устанавливаю Android Widget Companion…");
            var result = await _companion.InstallAsync(device.Serial, token);
            if (!result.IsSuccess)
                throw new InvalidOperationException(result.BestMessage);
            SetOperationStatus("Компаньон установлен и открыт на телефоне · нажмите «Сопрячь»");
            await RefreshDevicesAsync(force: true);
        });
    }

    private async Task ShowPairingAsync(AndroidDevice device)
    {
        SetOperationStatus("Создаю защищённую ссылку сопряжения…");
        var pairing = await _companionCoordinator.CreateAndOpenPairingAsync(device.Serial, _lifetime.Token);
        if (pairing.Session is null)
        {
            SetOperationStatus(pairing.LaunchResult.BestMessage, true);
            return;
        }
        var window = new CompanionPairingWindow(device.Serial, device.DisplayName, pairing,
            _companionCoordinator)
        {
            Owner = this
        };
        window.Show();
        SetOperationStatus(pairing.LaunchResult.IsSuccess
            ? "Ссылка сопряжения создана и открыта на телефоне"
            : "Ссылка создана · откройте или скопируйте её из окна сопряжения",
            !pairing.LaunchResult.IsSuccess);
    }

    private async Task InstallApksAsync(AndroidDevice device, IReadOnlyList<string> paths)
    {
        await RunOperationAsync(async token =>
        {
            for (var i = 0; i < paths.Count; i++)
            {
                SetOperationStatus($"Устанавливаю {Path.GetFileName(paths[i])} ({i + 1}/{paths.Count})…");
                var result = await _devicesService.InstallApkAsync(device.Serial, paths[i], token);
                if (!result.IsSuccess)
                    throw new InvalidOperationException(result.BestMessage);
            }
            SetOperationStatus("Приложение установлено ✓");
        });
    }

    private void ShellButton_Click(object sender, RoutedEventArgs e)
    {
        var device = RequireOnlineDevice();
        if (device is null)
            return;
        try
        {
            var result = _devicesService.StartShell(device.Serial);
            SetOperationStatus(result.IsSuccess ? "ADB shell открыт" : result.BestMessage, !result.IsSuccess);
        }
        catch (Exception ex) { SetOperationStatus(ex.Message, true); }
    }

    private async void ClipboardButton_Click(object sender, RoutedEventArgs e)
    {
        var device = RequireOnlineDevice();
        if (device is null)
            return;
        if (!Clipboard.ContainsText())
        {
            SetOperationStatus("В буфере обмена нет текста", true);
            return;
        }

        var text = Clipboard.GetText();
        if (text.Length > 1000)
            text = text[..1000];
        await RunOperationAsync(async token =>
        {
            SetOperationStatus("Отправляю текст в активное поле телефона…");
            var result = await _devicesService.SendTextAsync(device.Serial, text, token);
            if (!result.IsSuccess)
                throw new InvalidOperationException(result.BestMessage);
            SetOperationStatus("Текст отправлен ✓");
        });
    }

    private async void PowerButton_Click(object sender, RoutedEventArgs e)
    {
        var device = RequireOnlineDevice();
        if (device is null)
            return;
        await RunOperationAsync(async token =>
        {
            var result = await _devicesService.TogglePowerAsync(device.Serial, token);
            if (!result.IsSuccess)
                throw new InvalidOperationException(result.BestMessage);
            SetOperationStatus("Команда экрана отправлена ✓");
        });
    }

    private void MtpButton_Click(object sender, RoutedEventArgs e)
    {
        var device = RequireOnlineDevice();
        if (device is null)
            return;
        var result = _desktop.OpenMtpDevice(device);
        SetOperationStatus(result.BestMessage, !result.IsSuccess);
    }

    private async Task RunOperationAsync(Func<CancellationToken, Task> operation)
    {
        if (_operationInProgress)
        {
            SetOperationStatus("Дождитесь завершения текущей операции", true);
            return;
        }

        _operationInProgress = true;
        try
        {
            await operation(_lifetime.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            SetOperationStatus(ex.Message, true);
        }
        finally
        {
            _operationInProgress = false;
            UpdateCompanionUi(_activeDevice);
        }
    }

    private void UpdateCompanionUi(AndroidDevice? device)
    {
        var installed = device?.CompanionState == CompanionInstallationState.Installed;
        var connected = device?.IsCompanionConnected == true;
        var notificationAccess = device?.CompanionNotificationAccess == true;
        var online = device?.State == DeviceConnectionState.Online;
        CompanionButtonText.Text = !installed ? "Компаньон" : connected ? "Открыть" : "Сопрячь";
        CompanionButton.IsEnabled = online && !_operationInProgress &&
                                    (installed || _companion.IsInstallerAvailable);
        CompanionButton.ToolTip = installed
            ? connected
                ? "Открыть Android Widget Companion и настройки доступа"
                : "Создать код и ссылку сопряжения"
            : _companion.IsInstallerAvailable
                ? "Установить компаньон только после подтверждения"
                : "APK компаньона не входит в эту сборку";
        CompanionStatusText.Text = connected && notificationAccess
            ? "Компаньон сопряжён · уведомления включены"
            : connected
                ? "Компаньон сопряжён · разрешите доступ к уведомлениям на телефоне"
            : installed
                ? "Компаньон установлен · нажмите «Сопрячь»"
            : device is null
                ? "Companion-функции отключены: телефон не подключён"
                : device.CompanionState == CompanionInstallationState.Unknown
                    ? "Companion-функции отключены: статус установки не определён"
                : !_companion.IsInstallerAvailable
                    ? "Companion-функции отключены: установщик не входит в сборку"
                    : "Компаньон не установлен · companion-функции отключены";
    }

    private void SetOperationStatus(string message, bool isError = false)
    {
        StatusText.Text = message;
        PanelStatusText.Text = message;
        var brush = isError
            ? (Brush)FindResource("DangerText")
            : (Brush)FindResource("TextSecondary");
        StatusText.Foreground = brush;
        PanelStatusText.Foreground = brush;
    }

    private void KeepWindowOnScreen()
    {
        var workArea = SystemParameters.WorkArea;
        if (Left + Width > workArea.Right)
            Left = Math.Max(workArea.Left, workArea.Right - Width);
        if (Top + Height > workArea.Bottom)
            Top = Math.Max(workArea.Top, workArea.Bottom - Height);
    }

    private void RestoreSettings()
    {
        var settings = _settings.Current;
        Topmost = settings.Topmost;
        PinButton.Foreground = new SolidColorBrush(Topmost ? Color.FromRgb(138, 115, 255) : Color.FromRgb(120, 132, 163));
        var workArea = SystemParameters.WorkArea;
        Width = Math.Clamp(settings.MainCardWidth ?? CompactWidth, CompactMinWidth,
            Math.Max(CompactMinWidth, Math.Min(MaxWidth, workArea.Width)));
        Height = Math.Clamp(settings.MainCardHeight ?? CompactHeight, MinHeight,
            Math.Max(MinHeight, Math.Min(MaxHeight, workArea.Height)));
        Left = settings.Left is double left && left >= workArea.Left && left < workArea.Right - 80
            ? left : workArea.Right - Width - 30;
        Top = settings.Top is double top && top >= workArea.Top && top < workArea.Bottom - 80
            ? top : workArea.Bottom - Height - 40;
    }

    private void SaveSettings()
    {
        _settings.Update(settings => settings with
        {
            Left = Left,
            Top = Top,
            Topmost = Topmost,
            MainCardWidth = Math.Clamp(_menuOpen ? Width - ExpandedPanelSpace : Width,
                CompactMinWidth, MaxWidth),
            MainCardHeight = Math.Clamp(Height, MinHeight, MaxHeight)
        });
    }
}
