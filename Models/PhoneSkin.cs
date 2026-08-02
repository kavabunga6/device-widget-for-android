using System.Windows;
using System.Windows.Media;
using AndroidWidget.Core.Devices;

namespace AndroidWidget.Models;

public enum CameraCutout
{
    None,
    CenterPunch,
    LeftPunch,
    Pill
}

public sealed record PhoneSkin(
    string Family,
    Color Accent,
    Color Body,
    double ShellRadius,
    double ScreenRadius,
    Thickness Bezel,
    CameraCutout Camera,
    bool HasSideButtons = true);

public static class PhoneSkinResolver
{
    public static PhoneSkin Resolve(AndroidDevice device)
    {
        var identity = $"{device.Manufacturer} {device.Brand} {device.Model} {device.DeviceCode}".ToLowerInvariant();

        if (Contains(identity, "tablet", "pad", "tab ") || LooksLikeTablet(device.ScreenResolution))
            return Skin("Android tablet", "#58A6FF", "#252A33", 22, 19, 8, CameraCutout.None, false);

        if (Contains(identity, "samsung", "sm-s", "sm-a", "sm-f"))
        {
            var foldable = Contains(identity, "sm-f", "fold", "flip");
            return Skin(foldable ? "Samsung Galaxy Fold/Flip" : "Samsung Galaxy",
                "#70A5FF", "#171B24", foldable ? 28 : 35, foldable ? 24 : 29,
                foldable ? 7 : 8, CameraCutout.CenterPunch);
        }

        if (Contains(identity, "google", "pixel"))
            return Skin("Google Pixel", "#8AB4F8", "#24272C", 34, 27, 9, CameraCutout.CenterPunch);

        if (Contains(identity, "oneplus"))
            return Skin("OnePlus", "#F14C4C", "#15171B", 36, 29, 8, CameraCutout.CenterPunch);

        if (Contains(identity, "oppo", "realme", "oplus", "cph", "rmx"))
            return Skin(Contains(identity, "realme", "rmx") ? "realme" : "OPPO",
                "#42C98D", "#171C1A", 37, 30, 8, CameraCutout.CenterPunch);

        if (Contains(identity, "xiaomi", "redmi", "poco"))
            return Skin("Xiaomi / Redmi / POCO", "#FF8A45", "#18191D", 34, 27, 8, CameraCutout.CenterPunch);

        if (Contains(identity, "huawei", "honor"))
            return Skin("Huawei / HONOR", "#61A5FF", "#191B23", 38, 31, 8,
                Contains(identity, "pro", "magic") ? CameraCutout.Pill : CameraCutout.CenterPunch);

        if (Contains(identity, "sony", "xperia"))
            return Skin("Sony Xperia", "#8D7CFF", "#17171C", 20, 15, 10, CameraCutout.None);

        if (Contains(identity, "motorola", "moto"))
            return Skin("Motorola", "#59BCEB", "#161B20", 38, 31, 8, CameraCutout.CenterPunch);

        if (Contains(identity, "asus", "rog"))
            return Skin("ASUS / ROG", "#F05252", "#15161A", 25, 20, 10, CameraCutout.None);

        if (Contains(identity, "nothing", "a063", "a104"))
            return Skin("Nothing Phone", "#E8E8E8", "#151515", 32, 25, 8, CameraCutout.CenterPunch);

        return Skin("Android", "#8A73FF", "#090C13", 38, 30, 9, CameraCutout.CenterPunch);
    }

    private static PhoneSkin Skin(string family, string accent, string body, double shellRadius,
        double screenRadius, double bezel, CameraCutout camera, bool buttons = true) =>
        new(family, Parse(accent), Parse(body), shellRadius, screenRadius,
            new Thickness(bezel), camera, buttons);

    private static bool Contains(string source, params string[] values) =>
        values.Any(value => source.Contains(value, StringComparison.OrdinalIgnoreCase));

    private static bool LooksLikeTablet(string resolution)
    {
        var parts = resolution.Split('x', 'X');
        if (parts.Length != 2 || !double.TryParse(parts[0], out var width) ||
            !double.TryParse(parts[1], out var height) || width <= 0 || height <= 0)
            return false;
        var ratio = Math.Max(width, height) / Math.Min(width, height);
        return ratio < 1.55;
    }

    private static Color Parse(string value) => (Color)ColorConverter.ConvertFromString(value);
}
