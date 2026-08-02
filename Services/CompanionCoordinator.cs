using AndroidWidget.CompanionHost;
using AndroidWidget.Protocol;

namespace AndroidWidget.Services;

public sealed class CompanionCoordinator : IAsyncDisposable
{
    private readonly CompanionHostService _host;
    private readonly ICompanionService _companion;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private readonly object _stateSync = new();
    private readonly Dictionary<string, CompanionLinkState> _links = new(StringComparer.Ordinal);
    private readonly HashSet<string> _preparedRoutes = new(StringComparer.Ordinal);
    private bool _started;

    public CompanionCoordinator(CompanionHostService host, ICompanionService companion, IAppLogger logger)
    {
        _host = host;
        _companion = companion;
        _logger = logger;
        _host.DeviceChanged += HandleDeviceChanged;
        _host.NotificationReceived += HandleNotificationReceived;
    }

    public event EventHandler<CompanionLinkState>? LinkChanged;
    public event EventHandler<CompanionPhoneMessage>? MessageReceived;

    public string? StartError { get; private set; }

    public async Task<OperationResult> StartAsync(CancellationToken cancellationToken = default)
    {
        await _startGate.WaitAsync(cancellationToken);
        try
        {
            if (_started)
                return OperationResult.Success();
            try
            {
                await _host.StartAsync(cancellationToken);
                _started = true;
                StartError = null;
                _logger.Write($"Companion host started on port {ProtocolConstants.DefaultPort}");
                return OperationResult.Success();
            }
            catch (Exception ex)
            {
                StartError = ex.Message;
                _logger.Write($"Companion host failed: {ex}");
                return OperationResult.Failure(ex.Message);
            }
        }
        finally
        {
            _startGate.Release();
        }
    }

    public CompanionLinkState GetLinkState(string serial)
    {
        lock (_stateSync)
            return _links.TryGetValue(serial, out var state)
                ? state
                : new CompanionLinkState(serial, false, false, null);
    }

    public void RetainAdbRoutes(IEnumerable<string> connectedSerials)
    {
        var connected = connectedSerials.ToHashSet(StringComparer.Ordinal);
        lock (_stateSync)
            _preparedRoutes.RemoveWhere(serial => !connected.Contains(serial));
    }

    public async Task EnsureAdbRouteAsync(string serial, CancellationToken cancellationToken = default)
    {
        lock (_stateSync)
        {
            if (_preparedRoutes.Contains(serial))
                return;
        }
        var result = await _companion.PreparePortReverseAsync(serial, ProtocolConstants.DefaultPort,
            cancellationToken);
        if (!result.IsSuccess)
            return;
        lock (_stateSync)
            _preparedRoutes.Add(serial);
    }

    public async Task<CompanionPairingResult> CreateAndOpenPairingAsync(string serial,
        CancellationToken cancellationToken = default)
    {
        var start = await StartAsync(cancellationToken);
        if (!start.IsSuccess)
            return new CompanionPairingResult(null, start, false);

        var tunnel = await _companion.PreparePortReverseAsync(serial, ProtocolConstants.DefaultPort,
            cancellationToken);
        var usesAdbTunnel = tunnel.IsSuccess;
        if (usesAdbTunnel)
        {
            lock (_stateSync)
                _preparedRoutes.Add(serial);
        }
        var session = _host.CreatePairingSession(serial, usesAdbTunnel ? "127.0.0.1" : null);
        var launch = await _companion.OpenPairingAsync(serial, session.Uri, cancellationToken);
        return new CompanionPairingResult(session, launch, usesAdbTunnel);
    }

    public Task<OperationResult> ReopenPairingAsync(string serial, string pairingUri,
        CancellationToken cancellationToken = default) =>
        _companion.OpenPairingAsync(serial, pairingUri, cancellationToken);

    public Task<OperationResult> OpenCompanionAsync(string serial,
        CancellationToken cancellationToken = default) =>
        _companion.LaunchAsync(serial, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        _host.DeviceChanged -= HandleDeviceChanged;
        _host.NotificationReceived -= HandleNotificationReceived;
        await _host.DisposeAsync();
        _startGate.Dispose();
    }

    private void HandleDeviceChanged(object? sender, CompanionDeviceState device)
    {
        if (string.IsNullOrWhiteSpace(device.ClientTag))
            return;
        var state = new CompanionLinkState(device.ClientTag, device.IsConnected,
            device.Status?.HasNotificationAccess == true, device.Identity.DisplayName);
        lock (_stateSync)
            _links[device.ClientTag] = state;
        LinkChanged?.Invoke(this, state);
        if (device.IsConnected && !state.HasNotificationAccess)
            _ = RefreshNotificationAccessAsync(device.ClientTag, state);
    }

    private void HandleNotificationReceived(object? sender, CompanionNotification received)
    {
        if (string.IsNullOrWhiteSpace(received.ClientTag))
            return;
        var notification = received.Notification;
        var senderName = string.IsNullOrWhiteSpace(notification.Title)
            ? notification.AppName
            : notification.Title.Equals(notification.AppName, StringComparison.OrdinalIgnoreCase)
                ? notification.Title
                : $"{notification.AppName} · {notification.Title}";
        var message = new PhoneMessage(notification.NotificationId, senderName, notification.Preview,
            notification.PackageName);
        CompanionLinkState? accessUpdated = null;
        lock (_stateSync)
        {
            if (_links.TryGetValue(received.ClientTag, out var state) && !state.HasNotificationAccess)
            {
                accessUpdated = state with { HasNotificationAccess = true };
                _links[received.ClientTag] = accessUpdated;
            }
        }
        if (accessUpdated is not null)
            LinkChanged?.Invoke(this, accessUpdated);
        MessageReceived?.Invoke(this, new CompanionPhoneMessage(received.ClientTag, message));
    }

    private async Task RefreshNotificationAccessAsync(string serial, CompanionLinkState previous)
    {
        try
        {
            var enabled = await _companion.HasNotificationAccessAsync(serial);
            if (enabled is null || enabled == previous.HasNotificationAccess)
                return;
            var updated = previous with { HasNotificationAccess = enabled.Value };
            lock (_stateSync)
                _links[serial] = updated;
            LinkChanged?.Invoke(this, updated);
        }
        catch (Exception ex)
        {
            _logger.Write($"Notification access check failed for {serial}: {ex.Message}");
        }
    }
}

public sealed record CompanionLinkState(
    string Serial,
    bool IsConnected,
    bool HasNotificationAccess,
    string? DisplayName);

public sealed record CompanionPhoneMessage(string Serial, PhoneMessage Message);

public sealed record CompanionPairingResult(
    PairingSession? Session,
    OperationResult LaunchResult,
    bool UsesAdbTunnel);
