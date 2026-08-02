using System.Windows;
using System.Windows.Media.Imaging;
using QRCoder;

namespace AndroidWidget;

public partial class WirelessPairingWindow : Window
{
    private readonly IAndroidDeviceService _devices;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _qrSession;

    public WirelessPairingWindow(IAndroidDeviceService devices)
    {
        _devices = devices;
        InitializeComponent();
        Closed += (_, _) =>
        {
            _qrSession?.Cancel();
            _lifetime.Cancel();
        };
    }

    private async void CreateQrButton_Click(object sender, RoutedEventArgs e)
    {
        _qrSession?.Cancel();
        _qrSession?.Dispose();
        _qrSession = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var serviceName = "studio-" + RandomText(10);
        var password = RandomText(16);
        var payload = $"WIFI:T:ADB;S:{serviceName};P:{password};;";
        QrImage.Source = CreateQrImage(payload);
        CreateQrButton.IsEnabled = false;
        StatusText.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondary");
        StatusText.Text = "QR готов · ожидаю сканирование телефоном…";
        try
        {
            var result = await _devices.PairWirelessQrAsync(serviceName, password, _qrSession.Token);
            StatusText.Text = result.IsSuccess
                ? "QR-сопряжение выполнено. Устройство появится после публикации _adb-tls-connect."
                : result.BestMessage;
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource(result.IsSuccess
                ? "TextSecondary" : "DangerText");
        }
        catch (OperationCanceledException) { }
        finally
        {
            CreateQrButton.IsEnabled = true;
        }
    }

    private async void PairButton_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true, "Выполняю защищённое сопряжение…");
        try
        {
            var endpoint = PairEndpointText.Text.Trim();
            var result = await _devices.PairWirelessAsync(endpoint, PairCodeText.Text.Trim(), _lifetime.Token);
            StatusText.Text = result.IsSuccess
                ? "Сопряжение выполнено. Теперь укажите адрес подключения с основного экрана Wireless debugging."
                : result.BestMessage;
            if (result.IsSuccess && string.IsNullOrWhiteSpace(ConnectEndpointText.Text))
                ConnectEndpointText.Text = endpoint;
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource(result.IsSuccess
                ? "TextSecondary" : "DangerText");
        }
        catch (OperationCanceledException) { }
        finally { SetBusy(false); }
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true, "Подключаю устройство…");
        try
        {
            var result = await _devices.ConnectWirelessAsync(ConnectEndpointText.Text.Trim(), _lifetime.Token);
            StatusText.Text = result.IsSuccess ? "Устройство подключено по Wi-Fi ADB." : result.BestMessage;
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource(result.IsSuccess
                ? "TextSecondary" : "DangerText");
        }
        catch (OperationCanceledException) { }
        finally { SetBusy(false); }
    }

    private void SetBusy(bool busy, string? status = null)
    {
        IsEnabled = !busy;
        if (status is not null)
            StatusText.Text = status;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private static BitmapImage CreateQrImage(string payload)
    {
        var bytes = PngByteQRCodeHelper.GetQRCode(payload, QRCodeGenerator.ECCLevel.Q, 12);
        using var stream = new MemoryStream(bytes);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    internal static bool VerifyQrRenderer(out string details)
    {
        try
        {
            var image = CreateQrImage("WIFI:T:ADB;S:studio-A1B2C3D4E5;P:A1B2C3D4E5F6G7H8;;");
            details = $"{image.PixelWidth}x{image.PixelHeight}";
            return image.PixelWidth > 0 && image.PixelHeight > 0;
        }
        catch (Exception ex)
        {
            details = ex.Message;
            return false;
        }
    }

    private static string RandomText(int length)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
        return string.Create(length, alphabet, static (span, source) =>
        {
            for (var index = 0; index < span.Length; index++)
                span[index] = source[System.Security.Cryptography.RandomNumberGenerator.GetInt32(source.Length)];
        });
    }
}
