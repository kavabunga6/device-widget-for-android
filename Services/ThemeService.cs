using System.Windows;
using System.Windows.Media;
using AndroidWidget.Core.Settings;

namespace AndroidWidget.Services;

public static class ThemeService
{
    public static void Apply(WidgetTheme theme)
    {
        var light = theme == WidgetTheme.Light;
        Set("TextPrimary", light ? "#182033" : "#F7F7F8");
        Set("TextSecondary", light ? "#5D6980" : "#C5C7CE");
        Set("DangerText", light ? "#B4232C" : "#FF8585");
        Set("WarningText", light ? "#865000" : "#FFD18A");
        Set("Accent", light ? "#6652D9" : "#8B7CFF");
        Set("WindowSurface", light ? "#F4F6FB" : "#202124");
        Set("PhoneShellBrush", light ? "#D8DEEA" : "#151619");
        Set("SurfaceBrush", light ? "#FFFFFF" : "#2B2D31");
        Set("SurfaceHoverBrush", light ? "#E9ECF5" : "#35383E");
        Set("BorderBrush", light ? "#C8CFDE" : "#454850");
        Set("ScreenBorderBrush", light ? "#C4CBDB" : "#4A4D56");
        Set("MutedSurfaceBrush", light ? "#E9EDF5" : "#27292D");

        Application.Current.Resources["ScreenBackground"] = new LinearGradientBrush(
            new GradientStopCollection
            {
                new(Parse(light ? "#FDFEFF" : "#2A2C31"), 0),
                new(Parse(light ? "#F0F3F9" : "#222428"), 0.5),
                new(Parse(light ? "#E8ECF5" : "#26282D"), 1)
            }, new Point(0, 0), new Point(1, 1));
    }

    private static void Set(string key, string color)
    {
        Application.Current.Resources[key] = new SolidColorBrush(Parse(color));
    }

    private static System.Windows.Media.Color Parse(string color) =>
        (System.Windows.Media.Color)ColorConverter.ConvertFromString(color);
}
