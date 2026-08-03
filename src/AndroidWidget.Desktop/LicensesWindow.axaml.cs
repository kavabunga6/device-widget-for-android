using System.Reflection;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AndroidWidget.Desktop;

internal sealed partial class LicensesWindow : Window
{
    public LicensesWindow()
    {
        InitializeComponent();
        LicenseText.Text = ReadEmbeddedText("AndroidWidget.Desktop.LICENSE");
        ThirdPartyText.Text = ReadEmbeddedText("AndroidWidget.Desktop.THIRD_PARTY_NOTICES.md");
    }

    private static string ReadEmbeddedText(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName)
                           ?? throw new InvalidOperationException($"Не найден встроенный ресурс {resourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();
}
