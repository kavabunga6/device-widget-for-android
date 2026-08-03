using AndroidWidget.Core;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace AndroidWidget.Desktop;

internal sealed partial class WirelessAdbWindow : Window
{
    private readonly DesktopRuntime _runtime;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _busy;

    public WirelessAdbWindow(DesktopRuntime runtime)
    {
        _runtime = runtime;
        InitializeComponent();
        VersionText.Text = ProductVersion.ProductLabel;
        Opened += Window_Opened;
        Closed += Window_Closed;
    }

    private void Window_Opened(object? sender, EventArgs e) => PairEndpointText.Focus();

    private void Window_Closed(object? sender, EventArgs e)
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private async void PairButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_busy || !TryEndpoint(PairEndpointText.Text, out var endpoint))
        {
            if (!_busy)
                SetStatus("Укажите адрес сопряжения в формате host:port.", true);
            return;
        }

        var code = (PairCodeText.Text ?? string.Empty).Trim();
        if (code.Length != 6 || code.Any(character => !char.IsAsciiDigit(character)))
        {
            SetStatus("Код сопряжения должен состоять из шести цифр.", true);
            return;
        }

        try
        {
            SetBusy(true, "Выполняю защищённое сопряжение…");
            var result = await _runtime.Adb.PairAsync(endpoint, code, _lifetime.Token);
            SetStatus(result.IsSuccess
                ? "Сопряжение выполнено ✓ Теперь укажите адрес подключения с основного экрана беспроводной отладки."
                : result.Message, !result.IsSuccess);
            if (result.IsSuccess)
                ConnectEndpointText.Focus();
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

    private async void ConnectButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_busy || !TryEndpoint(ConnectEndpointText.Text, out var endpoint))
        {
            if (!_busy)
                SetStatus("Укажите адрес подключения в формате host:port.", true);
            return;
        }

        try
        {
            SetBusy(true, "Подключаю устройство…");
            var result = await _runtime.Adb.ConnectAsync(endpoint, _lifetime.Token);
            if (result.IsSuccess)
                await _runtime.RefreshAsync();
            SetStatus(result.IsSuccess ? "Устройство подключено по Wi-Fi ADB ✓" : result.Message,
                !result.IsSuccess);
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

    private void SetBusy(bool busy, string? status = null)
    {
        _busy = busy;
        PairButton.IsEnabled = !busy;
        ConnectButton.IsEnabled = !busy;
        PairEndpointText.IsEnabled = !busy;
        PairCodeText.IsEnabled = !busy;
        ConnectEndpointText.IsEnabled = !busy;
        if (status is not null)
            SetStatus(status);
    }

    private void SetStatus(string message, bool error = false)
    {
        StatusText.Text = message;
        StatusText.Foreground = new SolidColorBrush(error
            ? Color.FromRgb(255, 120, 120)
            : Color.FromRgb(152, 160, 179));
    }

    private static bool TryEndpoint(string? input, out string endpoint)
    {
        endpoint = (input ?? string.Empty).Trim();
        if (endpoint.Any(char.IsWhiteSpace))
            return false;
        var separator = endpoint.LastIndexOf(':');
        return separator > 0 && separator < endpoint.Length - 1 &&
               int.TryParse(endpoint[(separator + 1)..], out var port) && port is > 0 and <= 65535;
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();
}
