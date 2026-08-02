using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AndroidWidget.Presentation.Chrome;

public partial class DialogTitleBar : UserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(DialogTitleBar), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(
        nameof(Subtitle), typeof(string), typeof(DialogTitleBar), new PropertyMetadata(string.Empty));

    public DialogTitleBar() => InitializeComponent();

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            var window = Window.GetWindow(this);
            if (window?.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip)
                window.WindowState = window.WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized;
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
            Window.GetWindow(this)?.DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Window.GetWindow(this)?.Close();
}
