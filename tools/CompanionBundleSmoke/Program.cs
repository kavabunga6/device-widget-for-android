using AndroidWidget.Infrastructure.Adb;
using AndroidWidget.Infrastructure.Companion;
using AndroidWidget.Infrastructure.Diagnostics;
using AndroidWidget.Infrastructure.Scrcpy;

var bundle = new ScrcpyBundleManager();
var verifier = new DiagnosticsVerifier(bundle);
var valid = verifier.VerifyCompanionBundle(out var details);
Console.WriteLine(details);
if (!valid)
    return 1;

if (!args.Contains("--connected-device", StringComparer.OrdinalIgnoreCase))
    return 0;

var commands = new AdbCommandRunner(new AdbExecutableProvider(bundle));
var devices = await commands.RunAsync(["devices"]);
if (!devices.IsSuccess)
    return 2;
var serials = devices.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
    .Skip(1)
    .Select(line => line.Split(['\t', ' '], StringSplitOptions.RemoveEmptyEntries))
    .Where(parts => parts.Length >= 2 && parts[1] == "device")
    .Select(parts => parts[0])
    .ToArray();
var companion = new CompanionService(commands);
for (var index = 0; index < serials.Length; index++)
{
    var state = await companion.GetInstallationStateAsync(serials[index]);
    Console.WriteLine($"Device {index + 1}: {state}");
}
return serials.Length > 0 ? 0 : 3;
