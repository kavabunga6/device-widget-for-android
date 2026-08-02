using System.Text.RegularExpressions;
using AndroidWidget.Core.Abstractions;
using AndroidWidget.Core.Devices;
using AndroidWidget.Core.Messaging;

namespace AndroidWidget.Infrastructure.Adb;

public sealed class DeviceSnapshotReader
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);
    private readonly AdbCommandRunner _commands;
    private readonly SmsNotificationReader _messages;
    private readonly ICompanionService _companion;
    private readonly Dictionary<string, StaticDeviceDetails> _detailsCache = new(StringComparer.Ordinal);

    public DeviceSnapshotReader(AdbCommandRunner commands, SmsNotificationReader messages,
        ICompanionService companion)
    {
        _commands = commands;
        _messages = messages;
        _companion = companion;
    }

    public async Task<DeviceSnapshot> ReadAsync(string serial, string adbModel, bool readMessages,
        CancellationToken cancellationToken)
    {
        var detailsTask = ReadStaticDetailsAsync(serial, adbModel, cancellationToken);
        var batteryTask = ReadBatteryAsync(serial, cancellationToken);
        var powerTask = ReadPowerStateAsync(serial, cancellationToken);
        var messageTask = readMessages
            ? _messages.ReadNewAsync(serial, cancellationToken)
            : Task.FromResult<PhoneMessage?>(null);
        var companionTask = _companion.GetInstallationStateAsync(serial, cancellationToken);
        await Task.WhenAll(detailsTask, batteryTask, powerTask, messageTask, companionTask);
        var details = detailsTask.Result;
        var power = powerTask.Result;
        return new DeviceSnapshot(details.DisplayName, details.Model, details.AndroidVersion,
            batteryTask.Result, power.ScreenOn, power.Locked, details.Manufacturer, details.Brand,
            details.DeviceCode, details.ScreenResolution, messageTask.Result, companionTask.Result);
    }

    private async Task<StaticDeviceDetails> ReadStaticDetailsAsync(string serial, string adbModel,
        CancellationToken cancellationToken)
    {
        if (_detailsCache.TryGetValue(serial, out var cached))
            return cached;
        var propertiesTask = _commands.RunAsync(new[] { "-s", serial, "shell", "getprop" },
            cancellationToken, Timeout);
        var sizeTask = _commands.RunAsync(new[] { "-s", serial, "shell", "wm", "size" },
            cancellationToken, Timeout);
        await Task.WhenAll(propertiesTask, sizeTask);

        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches(propertiesTask.Result.StandardOutput,
                     @"^\[([^]]+)\]:\s*\[(.*)\]\s*$", RegexOptions.Multiline))
            properties[match.Groups[1].Value] = match.Groups[2].Value.Trim();
        string Property(params string[] names) => names
            .Select(name => properties.TryGetValue(name, out var value) ? value : string.Empty)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

        var model = FirstNonEmpty(Property("ro.product.model", "ro.product.system.model"), adbModel, "Android");
        var marketName = Property("ro.product.marketname", "ro.product.odm.marketname", "ro.oplus.market.name",
            "ro.vendor.oplus.market.name", "ro.config.marketing_name");
        var sizeMatch = Regex.Match(sizeTask.Result.StandardOutput, @"Physical size:\s*(\d+x\d+)",
            RegexOptions.IgnoreCase);
        if (!sizeMatch.Success)
            sizeMatch = Regex.Match(sizeTask.Result.StandardOutput, @"\b(\d+x\d+)\b", RegexOptions.IgnoreCase);

        var details = new StaticDeviceDetails(FirstNonEmpty(marketName, model), model,
            FirstNonEmpty(Property("ro.build.version.release"), "—"),
            FirstNonEmpty(Property("ro.product.manufacturer"), "Android"),
            FirstNonEmpty(Property("ro.product.brand"), "Android"),
            Property("ro.product.device", "ro.product.vendor.device"),
            sizeMatch.Success ? sizeMatch.Groups[1].Value : string.Empty);
        _detailsCache[serial] = details;
        return details;
    }

    private async Task<int?> ReadBatteryAsync(string serial, CancellationToken cancellationToken)
    {
        var result = await _commands.RunAsync(new[] { "-s", serial, "shell", "dumpsys", "battery" },
            cancellationToken, Timeout);
        var match = Regex.Match(result.StandardOutput, @"(?:^|\n)\s*level:\s*(\d+)");
        return match.Success && int.TryParse(match.Groups[1].Value, out var value) ? value : null;
    }

    private async Task<(bool ScreenOn, bool Locked)> ReadPowerStateAsync(string serial,
        CancellationToken cancellationToken)
    {
        var powerTask = _commands.RunAsync(new[] { "-s", serial, "shell", "dumpsys", "power" },
            cancellationToken, Timeout);
        var windowTask = _commands.RunAsync(new[] { "-s", serial, "shell", "dumpsys", "window", "policy" },
            cancellationToken, Timeout);
        await Task.WhenAll(powerTask, windowTask);
        var wakefulness = Regex.Match(powerTask.Result.StandardOutput, @"^\s*mWakefulness=(\w+)",
            RegexOptions.IgnoreCase | RegexOptions.Multiline);
        var screenOn = wakefulness.Success
            ? wakefulness.Groups[1].Value.Equals("Awake", StringComparison.OrdinalIgnoreCase)
            : !Regex.IsMatch(powerTask.Result.StandardOutput,
                @"Display Power:\s*state=OFF|mInteractive=false|mScreenOn=false", RegexOptions.IgnoreCase);
        var locked = Regex.IsMatch(windowTask.Result.StandardOutput,
            @"mShowingLockscreen=true|mKeyguardShowing=true|isStatusBarKeyguard=true|keyguardShowing=true|mInputRestricted=true",
            RegexOptions.IgnoreCase);
        return (screenOn, locked);
    }

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private sealed record StaticDeviceDetails(string DisplayName, string Model, string AndroidVersion,
        string Manufacturer, string Brand, string DeviceCode, string ScreenResolution);
}

public sealed record DeviceSnapshot(string DisplayName, string Model, string AndroidVersion,
    int? BatteryPercent, bool ScreenOn, bool Locked, string Manufacturer, string Brand,
    string DeviceCode, string ScreenResolution, PhoneMessage? LatestMessage,
    CompanionInstallationState CompanionState);
