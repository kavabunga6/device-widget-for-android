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
    private bool _busy;

    public CompanionWindow(DesktopRuntime runtime, string serial, string deviceName)
    {
        _runtime = runtime;
        _serial = serial;
        InitializeComponent();
        VersionText.Text = ProductVersion.ProductLabel;
        DeviceNameText.Text = deviceName;
        HostStatusText.Text = runtime.HostError is null
            ? $"Защищённый локальный host · порт {ProtocolConstants.DefaultPort}"
            : $"Ошибка host: {runtime.HostError}";
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
            DesktopCompanionState.Installed => "Установлен · готов к запуску",
            DesktopCompanionState.UpdateAvailable => "Доступно обновление",
            DesktopCompanionState.NotInstalled => "Не установлен на телефоне",
            DesktopCompanionState.Unavailable => "Недоступен в этой сборке",
            _ => "Не удалось определить состояние"
        };
        CompanionActionButton.Content = !available && _state != DesktopCompanionState.Installed
            ? "APK отсутствует"
            : _state switch
            {
                DesktopCompanionState.Installed => "Открыть",
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
            SetBusy(true, _state == DesktopCompanionState.Installed
                ? "Открываю компаньон…"
                : "Устанавливаю компаньон с разрешения пользователя…");
            var result = _state == DesktopCompanionState.Installed
                ? await _runtime.Companion.LaunchAsync(_serial, _lifetime.Token)
                : await _runtime.Companion.InstallOrUpdateAsync(_serial, _lifetime.Token);
            SetStatus(result.IsSuccess ? "Компаньон открыт на телефоне ✓" : result.Message, !result.IsSuccess);
            _state = await _runtime.Companion.GetStateAsync(_serial, _lifetime.Token);
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
            var pairing = _runtime.Host.CreatePairingSession(_serial, "127.0.0.1");
            PairCodeText.Text = $"{pairing.Code[..3]} {pairing.Code[3..]}";
            PairingUriText.Text = pairing.Uri;
            CopyPairingButton.IsEnabled = true;
            PairingHintText.Text = $"Код действует до {pairing.ExpiresAt.ToLocalTime():HH:mm:ss}";

            var reverse = await _runtime.Companion.PrepareReverseAsync(_serial,
                ProtocolConstants.DefaultPort, _lifetime.Token);
            var opened = reverse.IsSuccess
                ? await _runtime.Companion.OpenPairingAsync(_serial, pairing.Uri, _lifetime.Token)
                : reverse;
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
            HostStatusText.Text = state.IsConnected
                ? "Компаньон сопряжён · уведомления включены"
                : "Компаньон не подключён";
        });
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
