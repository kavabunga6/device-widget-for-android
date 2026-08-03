using AndroidWidget.CompanionHost;
using AndroidWidget.Core;
using AndroidWidget.Protocol;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace AndroidWidget.Desktop;

internal sealed partial class CompanionWindow : Window
{
    private readonly DesktopRuntime _runtime;
    private readonly string _serial;
    private readonly CancellationTokenSource _lifetime = new();
    private DesktopCompanionState _state = DesktopCompanionState.Unknown;
    private bool _paired;
    private bool _connected;
    private bool _busy;

    public CompanionWindow(DesktopRuntime runtime, string serial, string deviceName)
    {
        _runtime = runtime;
        _serial = serial;
        InitializeComponent();
        VersionText.Text = ProductVersion.ProductLabel;
        DeviceNameText.Text = deviceName;
        RefreshPairingState();
        RenderHostStatus();
        _runtime.CompanionDeviceChanged += Runtime_CompanionDeviceChanged;
        Opened += Window_Opened;
        Closed += Window_Closed;
    }

    private async void Window_Opened(object? sender, EventArgs e) => await RefreshStateAsync();

    private void Window_Closed(object? sender, EventArgs e)
    {
        _runtime.CompanionDeviceChanged -= Runtime_CompanionDeviceChanged;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private async Task RefreshStateAsync()
    {
        try
        {
            SetBusy(true, "Проверяю компаньон…");
            _state = await _runtime.Companion.GetStateAsync(_serial, _lifetime.Token);
            RefreshPairingState();
            RenderState();
            SetStatus(string.Empty);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _state = DesktopCompanionState.Unknown;
            RenderState();
            SetStatus(ex.Message, true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void RenderState()
    {
        var available = _runtime.Companion.IsAvailable;
        CompanionStateText.Text = _state switch
        {
            DesktopCompanionState.Installed when _connected => "Сопряжён · подключён",
            DesktopCompanionState.Installed when _paired => "Сопряжён · ожидает подключения",
            DesktopCompanionState.Installed => "Установлен · требуется сопряжение",
            DesktopCompanionState.UpdateAvailable => "Доступно обновление",
            DesktopCompanionState.NotInstalled => "Не установлен на телефоне",
            DesktopCompanionState.Unavailable => "Недоступен в этой сборке",
            _ => "Не удалось определить состояние"
        };
        CompanionActionButton.Content = !available && _state != DesktopCompanionState.Installed
            ? "APK отсутствует"
            : _state switch
            {
                DesktopCompanionState.Installed when _paired => "Открыть",
                DesktopCompanionState.Installed => "Сопрячь",
                DesktopCompanionState.UpdateAvailable => "Обновить",
                _ => "Установить"
            };
        CompanionActionButton.IsEnabled = !_busy && (available || _state == DesktopCompanionState.Installed);
        NewCodeButton.IsEnabled = !_busy && _state is DesktopCompanionState.Installed or
            DesktopCompanionState.UpdateAvailable;
    }

    private async void CompanionActionButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_busy)
            return;
        try
        {
            var requiresPairing = _state == DesktopCompanionState.Installed && !_paired;
            var pairingOpened = requiresPairing;
            SetBusy(true, requiresPairing
                ? "Создаю ссылку сопряжения…"
                : _state == DesktopCompanionState.Installed
                    ? "Открываю компаньон…"
                    : "Устанавливаю компаньон с разрешения пользователя…");
            var result = requiresPairing
                ? await CreateAndOpenPairingAsync()
                : _state == DesktopCompanionState.Installed
                    ? await _runtime.Companion.LaunchAsync(_serial, _lifetime.Token)
                    : await _runtime.Companion.InstallOrUpdateAsync(_serial, _lifetime.Token);
            _state = await _runtime.Companion.GetStateAsync(_serial, _lifetime.Token);
            if (result.IsSuccess && _state == DesktopCompanionState.Installed && !pairingOpened && !_connected)
            {
                if (_paired)
                {
                    SetStatus("Ожидаю подключение по сохранённому сопряжению…");
                    await WaitForCompanionConnectionAsync(TimeSpan.FromSeconds(2));
                }
                if (!_connected)
                {
                    SetStatus("Сохранённое сопряжение не ответило · создаю новую ссылку…");
                    pairingOpened = true;
                    result = await CreateAndOpenPairingAsync();
                }
            }
            SetStatus(result.IsSuccess
                ? pairingOpened
                    ? "Ссылка сопряжения открыта в компаньоне ✓"
                    : "Компаньон открыт на телефоне ✓"
                : result.Message, !result.IsSuccess);
            RenderState();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void NewCodeButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_busy)
            return;
        try
        {
            SetBusy(true, "Создаю одноразовый код…");
            var opened = await CreateAndOpenPairingAsync();
            SetStatus(opened.IsSuccess
                ? "Ссылка открыта в компаньоне на телефоне ✓"
                : $"Не удалось открыть на телефоне: {opened.Message}", !opened.IsSuccess);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void CopyPairingButton_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PairingUriText.Text) || GetTopLevel(this)?.Clipboard is not { } clipboard)
            return;
        await clipboard.SetTextAsync(PairingUriText.Text);
        PairingHintText.Text = "Ссылка скопирована";
    }

    private void Runtime_CompanionDeviceChanged(object? sender, CompanionDeviceState state)
    {
        if (!string.Equals(state.ClientTag, _serial, StringComparison.Ordinal))
            return;
        Dispatcher.UIThread.Post(() =>
        {
            _paired = true;
            _connected = state.IsConnected;
            RenderHostStatus();
            RenderState();
        });
    }

    private async Task<PortableCommandResult> CreateAndOpenPairingAsync()
    {
        if (_runtime.HostError is not null)
            return new PortableCommandResult(1, "", $"Локальный companion-host недоступен: {_runtime.HostError}");

        var pairing = _runtime.Host.CreatePairingSession(_serial, "127.0.0.1");
        PairCodeText.Text = $"{pairing.Code[..3]} {pairing.Code[3..]}";
        PairingUriText.Text = pairing.Uri;
        CopyPairingButton.IsEnabled = true;
        PairingHintText.Text = $"Код действует до {pairing.ExpiresAt.ToLocalTime():HH:mm:ss}";

        var reverse = await _runtime.Companion.PrepareReverseAsync(_serial,
            ProtocolConstants.DefaultPort, _lifetime.Token);
        return reverse.IsSuccess
            ? await _runtime.Companion.OpenPairingAsync(_serial, pairing.Uri, _lifetime.Token)
            : reverse;
    }

    private async Task WaitForCompanionConnectionAsync(TimeSpan timeout)
    {
        var expiresAt = DateTimeOffset.UtcNow.Add(timeout);
        while (!_connected && DateTimeOffset.UtcNow < expiresAt)
            await Task.Delay(TimeSpan.FromMilliseconds(100), _lifetime.Token);
    }

    private void RefreshPairingState()
    {
        _paired = _runtime.Host.HasPairingForClient(_serial);
        _connected = _runtime.Host.Devices.Any(device =>
            device.IsConnected && string.Equals(device.ClientTag, _serial, StringComparison.Ordinal));
    }

    private void RenderHostStatus()
    {
        HostStatusText.Foreground = new SolidColorBrush(_runtime.HostError is not null
            ? Color.FromRgb(255, 120, 120)
            : Color.FromRgb(114, 216, 162));
        HostStatusText.Text = _runtime.HostError is not null
            ? $"Ошибка host: {_runtime.HostError}"
            : _connected
                ? "Компаньон сопряжён · уведомления включены"
                : _paired
                    ? "Компаньон сопряжён · ожидает подключения"
                    : $"Защищённый локальный host · порт {ProtocolConstants.DefaultPort}";
    }

    private void SetBusy(bool busy, string? status = null)
    {
        _busy = busy;
        if (status is not null)
            SetStatus(status);
        CompanionActionButton.IsEnabled = !busy;
        NewCodeButton.IsEnabled = !busy;
        if (!busy)
            RenderState();
    }

    private void SetStatus(string message, bool error = false)
    {
        StatusText.Text = message;
        StatusText.Foreground = new SolidColorBrush(error
            ? Color.FromRgb(255, 120, 120)
            : Color.FromRgb(152, 160, 179));
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();
}
