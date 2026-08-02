using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AndroidWidget.Core.Messaging;

namespace AndroidWidget.Infrastructure.Adb;

public sealed class SmsNotificationReader
{
    private static readonly HashSet<string> KnownPackages = new(StringComparer.OrdinalIgnoreCase)
    {
        "com.google.android.apps.messaging",
        "com.samsung.android.messaging",
        "com.android.messaging",
        "com.android.mms",
        "com.miui.mms"
    };

    private readonly AdbCommandRunner _commands;
    private readonly Dictionary<string, string> _packageCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _seenNotifications = new(StringComparer.Ordinal);
    private readonly HashSet<string> _baselineInitialized = new(StringComparer.Ordinal);

    public SmsNotificationReader(AdbCommandRunner commands) => _commands = commands;

    public async Task<PhoneMessage?> ReadNewAsync(string serial, CancellationToken cancellationToken)
    {
        var smsPackage = await GetDefaultPackageAsync(serial, cancellationToken);
        var result = await _commands.RunAsync(
            new[] { "-s", serial, "shell", "dumpsys", "notification", "--noredact" }, cancellationToken,
            TimeSpan.FromSeconds(12));
        if (!result.IsSuccess)
            return null;

        var notifications = Parse(result.StandardOutput, smsPackage)
            .OrderByDescending(notification => notification.PostedAt)
            .ToList();
        if (!_seenNotifications.TryGetValue(serial, out var seen))
            _seenNotifications[serial] = seen = new HashSet<string>(StringComparer.Ordinal);

        if (_baselineInitialized.Add(serial))
        {
            foreach (var notification in notifications)
                seen.Add(notification.Message.Fingerprint);
            return null;
        }

        PhoneMessage? newest = null;
        foreach (var notification in notifications)
        {
            if (seen.Add(notification.Message.Fingerprint) && newest is null)
                newest = notification.Message;
        }

        if (seen.Count > 512)
        {
            seen.Clear();
            foreach (var notification in notifications)
                seen.Add(notification.Message.Fingerprint);
        }
        return newest;
    }

    public static bool VerifyParser()
    {
        const string sample =
            "NotificationRecord(0x01: pkg=com.google.android.apps.messaging user=UserHandle{0} id=1 " +
            "importance=3 key=0|com.google.android.apps.messaging|1|null|1000: Notification(category=msg))\n" +
            "  postTime=1770000000000\n" +
            "  android.title=String (Test sender)\n" +
            "  android.text=String (Test message)";
        var parsed = Parse(sample, "com.google.android.apps.messaging").SingleOrDefault();
        return parsed?.Message is
        {
            Sender: "Test sender",
            Preview: "Test message",
            PackageName: "com.google.android.apps.messaging"
        };
    }

    private async Task<string> GetDefaultPackageAsync(string serial, CancellationToken cancellationToken)
    {
        if (_packageCache.TryGetValue(serial, out var cached))
            return cached;
        var result = await _commands.RunAsync(new[]
        {
            "-s", serial, "shell", "cmd", "role", "get-role-holders", "--user", "0", "android.app.role.SMS"
        }, cancellationToken);
        var packageName = result.IsSuccess
            ? result.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Trim())
                .FirstOrDefault(value => Regex.IsMatch(value, "^[a-zA-Z0-9._]+$")) ?? string.Empty
            : string.Empty;
        _packageCache[serial] = packageName;
        return packageName;
    }

    private static IEnumerable<ParsedPhoneMessage> Parse(string output, string smsPackage)
    {
        foreach (var record in Regex.Split(output, @"(?=^\s*NotificationRecord\()", RegexOptions.Multiline))
        {
            var packageMatch = Regex.Match(record, @"^\s*NotificationRecord\([^\r\n]*?\bpkg=([a-zA-Z0-9._]+)",
                RegexOptions.Multiline);
            if (!packageMatch.Success)
                continue;
            var packageName = packageMatch.Groups[1].Value;
            if (!packageName.Equals(smsPackage, StringComparison.OrdinalIgnoreCase) &&
                !(string.IsNullOrWhiteSpace(smsPackage) && KnownPackages.Contains(packageName)))
                continue;
            if (record.Contains("GROUP_SUMMARY", StringComparison.OrdinalIgnoreCase))
                continue;

            var sender = ReadExtra(record, "android.title");
            var preview = FirstNonEmpty(ReadExtra(record, "android.text"), ReadExtra(record, "android.bigText"));
            if (string.IsNullOrWhiteSpace(sender) || string.IsNullOrWhiteSpace(preview))
                continue;

            var keyMatch = Regex.Match(record, @"\bkey=(.*?):\s*Notification\(");
            var key = keyMatch.Success ? keyMatch.Groups[1].Value.Trim() : packageName;
            var postedMatch = Regex.Match(record, @"\bpostTime=(\d+)");
            _ = long.TryParse(postedMatch.Groups[1].Value, out var postedAt);
            sender = Normalize(sender, 80);
            preview = Normalize(preview, 220);
            yield return new ParsedPhoneMessage(
                new PhoneMessage(Fingerprint($"{key}|{sender}|{preview}"), sender, preview, packageName), postedAt);
        }
    }

    private static string ReadExtra(string record, string key)
    {
        var match = Regex.Match(record, $@"^\s*{Regex.Escape(key)}=[^\r\n]*?\((.*)\)\s*$",
            RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private static string Normalize(string value, int maximumLength)
    {
        var normalized = Regex.Replace(value, @"\s+", " ").Trim();
        if (normalized.Equals("null", StringComparison.OrdinalIgnoreCase))
            return string.Empty;
        return normalized.Length <= maximumLength ? normalized : normalized[..(maximumLength - 1)] + "…";
    }

    private static string Fingerprint(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private sealed record ParsedPhoneMessage(PhoneMessage Message, long PostedAt);
}
