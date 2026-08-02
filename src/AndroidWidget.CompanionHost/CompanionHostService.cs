using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AndroidWidget.Protocol;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging;

namespace AndroidWidget.CompanionHost;

public sealed class CompanionHostService : IAsyncDisposable
{
    private const int MaximumMessageBytes = 64 * 1024;
    private readonly CompanionHostOptions _options;
    private readonly PairingStore _pairings;
    private readonly ConcurrentDictionary<string, CompanionDeviceState> _devices = new(StringComparer.Ordinal);
    private readonly object _pairingSync = new();
    private readonly System.Security.Cryptography.X509Certificates.X509Certificate2 _certificate;
    private WebApplication? _application;
    private ActivePairing? _activePairing;

    public CompanionHostService(CompanionHostOptions options)
    {
        _options = options;
        _pairings = new PairingStore(options.DataDirectory);
        _certificate = new CompanionCertificateStore(options.DataDirectory).LoadOrCreate();
    }

    public event EventHandler<CompanionDeviceState>? DeviceChanged;
    public event EventHandler<CompanionNotification>? NotificationReceived;

    public IReadOnlyCollection<CompanionDeviceState> Devices => _devices.Values.ToArray();

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_application is not null)
            return;
        await _pairings.LoadAsync(cancellationToken);

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel(server =>
            server.ListenAnyIP(_options.Port, listen => ConfigureHttps(listen)));
        var application = builder.Build();
        application.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(25) });
        application.Map(ProtocolConstants.SocketPath, HandleSocketRequestAsync);
        await application.StartAsync(cancellationToken);
        _application = application;
    }

    public PairingSession CreatePairingSession(string? clientTag = null, string? hostOverride = null,
        TimeSpan? lifetime = null)
    {
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var expiresAt = DateTimeOffset.UtcNow.Add(lifetime ?? TimeSpan.FromMinutes(5));
        lock (_pairingSync)
            _activePairing = new ActivePairing(code, expiresAt, clientTag);

        var host = string.IsNullOrWhiteSpace(hostOverride) ? GetPreferredLocalAddress().ToString() : hostOverride;
        var fingerprint = CompanionCertificateStore.GetSha256Fingerprint(_certificate);
        var uri = $"{ProtocolConstants.PairingScheme}://pair?host={Uri.EscapeDataString(host)}" +
                  $"&port={_options.Port}&fingerprint={fingerprint}&code={code}";
        return new PairingSession(uri, code, expiresAt);
    }

    public async ValueTask DisposeAsync()
    {
        if (_application is not null)
        {
            await _application.StopAsync();
            await _application.DisposeAsync();
            _application = null;
        }
        _certificate.Dispose();
    }

    private void ConfigureHttps(ListenOptions listen) => listen.UseHttps(_certificate);

    private async Task HandleSocketRequestAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        DeviceIdentity? identity = null;
        try
        {
            var helloJson = await ReceiveTextAsync(socket, context.RequestAborted);
            var hello = helloJson is null ? null : ProtocolJson.Deserialize<ClientHello>(helloJson);
            if (hello is null || hello.ProtocolVersion != ProtocolConstants.Version || !IsValidIdentity(hello.Device))
            {
                await SendAsync(socket, new ServerHello(ProtocolConstants.Version, false,
                    Error: "Unsupported protocol or invalid identity."), context.RequestAborted);
                return;
            }

            identity = hello.Device;
            string? issuedToken = null;
            ActivePairing? consumedPairing = null;
            var accepted = hello.Mode switch
            {
                "pair" when (consumedPairing = ConsumePairingCode(hello.Credential)) is not null => true,
                "auth" when _pairings.Validate(identity.InstallationId, hello.Credential) => true,
                _ => false
            };
            if (!accepted)
            {
                await SendAsync(socket, new ServerHello(ProtocolConstants.Version, false,
                    Error: "Pairing code or device token is invalid."), context.RequestAborted);
                return;
            }

            if (hello.Mode == "pair")
                issuedToken = await _pairings.PairAsync(identity.InstallationId, consumedPairing?.ClientTag,
                    context.RequestAborted);
            await SendAsync(socket, new ServerHello(ProtocolConstants.Version, true, issuedToken),
                context.RequestAborted);

            var clientTag = consumedPairing?.ClientTag ?? _pairings.GetClientTag(identity.InstallationId);
            UpdateDevice(identity, true, clientTag: clientTag);
            while (socket.State == WebSocketState.Open && !context.RequestAborted.IsCancellationRequested)
            {
                var message = await ReceiveTextAsync(socket, context.RequestAborted);
                if (message is null)
                    break;
                ProcessMessage(identity, clientTag, message);
            }
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Normal host shutdown or disconnected request.
        }
        catch (WebSocketException)
        {
            // The device disconnected without a close frame.
        }
        finally
        {
            if (identity is not null)
                UpdateDevice(identity, false, clientTag: _pairings.GetClientTag(identity.InstallationId));
        }
    }

    private void ProcessMessage(DeviceIdentity identity, string? clientTag, string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("type", out var typeElement))
            return;
        switch (typeElement.GetString())
        {
            case "status":
                var status = ProtocolJson.Deserialize<DeviceStatusMessage>(json);
                if (status is not null)
                    UpdateDevice(identity, true, status: status, clientTag: clientTag);
                break;
            case "notification":
                var notification = ProtocolJson.Deserialize<NotificationMessage>(json);
                if (notification is not null)
                {
                    UpdateDevice(identity, true, notification: notification, clientTag: clientTag);
                    NotificationReceived?.Invoke(this,
                        new CompanionNotification(clientTag, identity, notification));
                }
                break;
            case "ping":
                UpdateDevice(identity, true, clientTag: clientTag);
                break;
        }
    }

    private void UpdateDevice(DeviceIdentity identity, bool connected, DeviceStatusMessage? status = null,
        NotificationMessage? notification = null, string? clientTag = null)
    {
        var state = _devices.AddOrUpdate(identity.InstallationId,
            _ => new CompanionDeviceState(identity, connected, DateTimeOffset.UtcNow, status, notification,
                clientTag),
            (_, current) => current with
            {
                Identity = identity,
                IsConnected = connected,
                LastSeen = DateTimeOffset.UtcNow,
                Status = status ?? current.Status,
                LatestNotification = notification ?? current.LatestNotification,
                ClientTag = clientTag ?? current.ClientTag
            });
        DeviceChanged?.Invoke(this, state);
    }

    private ActivePairing? ConsumePairingCode(string candidate)
    {
        lock (_pairingSync)
        {
            if (_activePairing is null || _activePairing.ExpiresAt < DateTimeOffset.UtcNow ||
                !FixedTimeEquals(_activePairing.Code, candidate))
                return null;
            var pairing = _activePairing;
            _activePairing = null;
            return pairing;
        }
    }

    private static bool FixedTimeEquals(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));

    private static bool IsValidIdentity(DeviceIdentity identity) =>
        identity.InstallationId.Length is >= 16 and <= 128 &&
        identity.InstallationId.All(character => char.IsLetterOrDigit(character) || character is '-' or '_') &&
        identity.DisplayName.Length is > 0 and <= 128;

    private static async Task<string?> ReceiveTextAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        using var stream = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                if (socket.State == WebSocketState.CloseReceived)
                    await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Connection closed.",
                        cancellationToken);
                return null;
            }
            if (result.MessageType != WebSocketMessageType.Text)
                throw new WebSocketException("Only text protocol frames are supported.");
            stream.Write(buffer, 0, result.Count);
            if (stream.Length > MaximumMessageBytes)
                throw new WebSocketException("Protocol message is too large.");
            if (result.EndOfMessage)
                return Encoding.UTF8.GetString(stream.GetBuffer(), 0, checked((int)stream.Length));
        }
    }

    private static Task SendAsync<T>(WebSocket socket, T message, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(ProtocolJson.Serialize(message));
        return socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
    }

    private static IPAddress GetPreferredLocalAddress()
    {
        var candidates = NetworkInterface.GetAllNetworkInterfaces()
            .Where(network => network.OperationalStatus == OperationalStatus.Up &&
                              network.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(network => network.GetIPProperties().UnicastAddresses)
            .Select(address => address.Address)
            .Where(address => address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
            .ToList();
        return candidates.FirstOrDefault(IsPrivateAddress) ?? candidates.FirstOrDefault() ?? IPAddress.Loopback;
    }

    private static bool IsPrivateAddress(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes[0] == 10 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31 ||
               bytes[0] == 192 && bytes[1] == 168;
    }

    private sealed record ActivePairing(string Code, DateTimeOffset ExpiresAt, string? ClientTag);
}
