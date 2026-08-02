using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using AndroidWidget.CompanionHost;
using AndroidWidget.Protocol;

var dataDirectory = Path.Combine(Path.GetTempPath(), $"android-widget-smoke-{Guid.NewGuid():N}");
Directory.CreateDirectory(dataDirectory);

try
{
    var port = ReserveAvailablePort();
    await using var host = new CompanionHostService(new CompanionHostOptions(dataDirectory, port));
    var notificationCount = 0;
    host.NotificationReceived += (_, _) => Interlocked.Increment(ref notificationCount);

    await host.StartAsync();
    await RunTlsClientAsync(port, host.CreatePairingSession("adb-serial-first"), "smoke-device-0001", "first");
    await RunTlsClientAsync(port, host.CreatePairingSession("adb-serial-second"), "smoke-device-0002", "second");

    var devices = host.Devices.ToDictionary(device => device.Identity.InstallationId, StringComparer.Ordinal);
    Ensure(devices.Count == 2, "the second device replaced the first one");
    Ensure(devices["smoke-device-0001"].Status?.BatteryPercent == 73,
        "first device status was not preserved");
    Ensure(devices["smoke-device-0002"].Status?.BatteryPercent == 73,
        "second device status was not delivered");
    Ensure(devices["smoke-device-0001"].LatestNotification?.Preview == "Companion protocol works: first",
        "first device notification was not preserved");
    Ensure(devices["smoke-device-0002"].LatestNotification?.Preview == "Companion protocol works: second",
        "second device notification was not delivered");
    Ensure(devices["smoke-device-0001"].ClientTag == "adb-serial-first",
        "first device was not bound to its ADB serial");
    Ensure(devices["smoke-device-0002"].ClientTag == "adb-serial-second",
        "second device was not bound to its ADB serial");
    Ensure(notificationCount == 2, "notification events were not delivered independently");
    Console.WriteLine("Companion host smoke (two independent devices): PASS");
}
finally
{
    Directory.Delete(dataDirectory, recursive: true);
}

static async Task RunTlsClientAsync(int port, PairingSession pairing, string installationId, string label)
{
    var pairingValues = ParseQuery(new Uri(pairing.Uri).Query);
    var startInfo = new ProcessStartInfo
    {
        FileName = "node",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };
    startInfo.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "client.mjs"));
    startInfo.ArgumentList.Add(port.ToString());
    startInfo.ArgumentList.Add(pairingValues["fingerprint"]);
    startInfo.ArgumentList.Add(pairing.Code);
    startInfo.ArgumentList.Add(installationId);
    startInfo.ArgumentList.Add(label);
    using var process = Process.Start(startInfo) ??
                        throw new InvalidOperationException("Unable to start Node TLS smoke client.");
    var outputTask = process.StandardOutput.ReadToEndAsync();
    var errorTask = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
    var output = await outputTask;
    var error = await errorTask;
    Ensure(process.ExitCode == 0, $"TLS client failed: {error}");
    Ensure(output.Contains("TLS client: PASS", StringComparison.Ordinal), "TLS client did not finish cleanly");
}

static int ReserveAvailablePort()
{
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    listener.Stop();
    return port;
}

static Dictionary<string, string> ParseQuery(string query) => query.TrimStart('?')
    .Split('&', StringSplitOptions.RemoveEmptyEntries)
    .Select(part => part.Split('=', 2))
    .ToDictionary(part => Uri.UnescapeDataString(part[0]), part => Uri.UnescapeDataString(part[1]),
        StringComparer.Ordinal);

static void Ensure(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
