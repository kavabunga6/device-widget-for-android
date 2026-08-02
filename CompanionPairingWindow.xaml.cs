using System.Windows;
using System.Windows.Media;
using AndroidWidget.CompanionHost;
using AndroidWidget.Services;

namespace AndroidWidget;

public partial class CompanionPairingWindow : Window
{
    private readonly string _serial;
    private readonly CompanionCoordinator _coordinator;
    private readonly PairingSession _session;

    public CompanionPairingWindow(string serial, string displayName, CompanionPairingResult pairing,
        CompanionCoordinator coordinator)
    {
        if (pairing.Session is null)
            throw new ArgumentException("Сеанс сопряжения не создан.", nameof(pairing));
        _serial = serial;
        _coordinator = coordinator;
        _session = pairing.Session;
        InitializeComponent();
        DeviceText.Text = displayName;
        PairCodeText.Text = $"{_session.Code[..3]} {_session.Code[3..]}";
        PairingUriText.Text = _session.Uri;
        SetLaunchStatus(pairing);
        _coordinator.LinkChanged += HandleLinkChanged;
        Closed += (_, _) => _coordinator.LinkChanged -= HandleLinkChanged;
    }

    private void HandleLinkChanged(object? sender, CompanionLinkState state)
    {
        if (state.Serial != _serial || !state.IsConnected)
            return;
        Dispatcher.Invoke(() =>
        {
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(116, 216, 164));
            StatusText.Text = state.HasNotificationAccess
                ? "Телефон сопряжён · SMS и уведомления включены."
                : "Телефон сопряжён. Теперь разрешите компаньону доступ к уведомлениям на телефоне.";
        });
    }

    private void SetLaunchStatus(CompanionPairingResult pairing)
    {
        if (!pairing.LaunchResult.IsSuccess)
        {
            StatusText.Foreground = (Brush)FindResource("DangerText");
            StatusText.Text = "Ссылка создана, но открыть компаньон через ADB не удалось: " +
                              pairing.LaunchResult.BestMessage;
            return;
        }
        StatusText.Text = pairing.UsesAdbTunnel
            ? "Компаньон открыт на телефоне. Защищённое соединение пойдёт через текущий ADB-канал."
            : "Компаньон открыт. Телефон и компьютер должны находиться в одной локальной сети.";
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(_session.Uri);
        StatusText.Text = "Ссылка сопряжения скопирована.";
    }

    private async void OpenOnPhoneButton_Click(object sender, RoutedEventArgs e)
    {
        var result = await _coordinator.ReopenPairingAsync(_serial, _session.Uri);
        StatusText.Foreground = (Brush)FindResource(result.IsSuccess ? "TextSecondary" : "DangerText");
        StatusText.Text = result.IsSuccess
            ? "Компаньон снова открыт на телефоне."
            : "Не удалось открыть компаньон: " + result.BestMessage;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
