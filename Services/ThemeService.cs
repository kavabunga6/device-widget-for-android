using System.Windows;
using System.Windows.Media;

namespace AndroidWidget.Services;

public static class ThemeService
{
    public static void Apply(WidgetTheme theme)
    {
        var light = theme == WidgetTheme.Light;
        Set("TextPrimary", light ? "#182033" : "#F5F7FF");
        Set("TextSecondary", light ? "#5D6980" : "#9AA6C1");
        Set("Accent", light ? "#6652D9" : "#7C5CFC");
        Set("WindowSurface", light ? "#F4F6FB" : "#0E1220");
        Set("PhoneShellBrush", light ? "#D8DEEA" : "#090C13");
        Set("SurfaceBrush", light ? "#FFFFFF" : "#171C2C");
        Set("SurfaceHoverBrush", light ? "#E9ECF5" : "#222A42");
        Set("BorderBrush", light ? "#C8CFDE" : "#29314A");
        Set("ScreenBorderBrush", light ? "#C4CBDB" : "#202842");
        Set("MutedSurfaceBrush", light ? "#E9EDF5" : "#12182A");

        Application.Current.Resources["ScreenBackground"] = new LinearGradientBrush(
            new GradientStopCollection
            {
                new(Parse(light ? "#FDFEFF" : "#151A2B"), 0),
                new(Parse(light ? "#F0F3F9" : "#0E1220"), 0.5),
                new(Parse(light ? "#E8ECF5" : "#12152A"), 1)
            }, new Point(0, 0), new Point(1, 1));
    }

    private static void Set(string key, string color)
    {
        Application.Current.Resources[key] = new SolidColorBrush(Parse(color));
    }

    private static System.Windows.Media.Color Parse(string color) =>
        (System.Windows.Media.Color)ColorConverter.ConvertFromString(color);
}
