using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using AndroidWidget.Core.Devices;

namespace AndroidWidget.Presentation.Tray;

public sealed class TrayIconController : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _icon;
    private readonly Icon _phoneIcon;

    public TrayIconController(Action openWidget, Action showMiniWidgets, Action openSettings, Action exit)
    {
        _phoneIcon = CreatePhoneIcon();
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Открыть виджет", null, (_, _) => openWidget());
        menu.Items.Add("Мини-виджеты", null, (_, _) => showMiniWidgets());
        menu.Items.Add("Настройки", null, (_, _) => openSettings());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => exit());

        _icon = new System.Windows.Forms.NotifyIcon
        {
            Icon = _phoneIcon,
            Text = "Android Widget · устройств нет",
            Visible = true,
            ContextMenuStrip = menu
        };
        _icon.DoubleClick += (_, _) => openWidget();
    }

    public void Update(IReadOnlyList<AndroidDevice> devices)
    {
        var unauthorized = devices.FirstOrDefault(device => device.State == DeviceConnectionState.Unauthorized);
        _icon.Icon = unauthorized is null ? _phoneIcon : SystemIcons.Warning;
        _icon.Text = unauthorized is not null
            ? Truncate($"Android Widget · авторизуйте {unauthorized.DisplayName}")
            : devices.Count == 0
                ? "Android Widget · устройств нет"
                : Truncate($"Android Widget · {GetDeviceSummary(devices)}");
    }

    public void ShowInfo(string title, string message, int timeout = 1600) =>
        _icon.ShowBalloonTip(timeout, title, message, System.Windows.Forms.ToolTipIcon.Info);

    public void ShowWarning(string title, string message, int timeout = 3000) =>
        _icon.ShowBalloonTip(timeout, title, message, System.Windows.Forms.ToolTipIcon.Warning);

    public static string GetDeviceSummary(IReadOnlyList<AndroidDevice> devices) =>
        devices.Count == 1 ? devices[0].DisplayName : $"устройств: {devices.Count}";

    public void Dispose()
    {
        _icon.Dispose();
        _phoneIcon.Dispose();
    }

    private static string Truncate(string value) => value.Length <= 63 ? value : value[..60] + "…";

    private static Icon CreatePhoneIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);
        using var body = new SolidBrush(Color.FromArgb(35, 40, 58));
        using var screen = new SolidBrush(Color.FromArgb(124, 92, 252));
        FillRoundedRectangle(graphics, body, new Rectangle(7, 2, 18, 28), 5);
        FillRoundedRectangle(graphics, screen, new Rectangle(10, 6, 12, 18), 2);
        graphics.FillEllipse(Brushes.White, 15, 26, 2, 2);
        var handle = bitmap.GetHicon();
        try { return (Icon)Icon.FromHandle(handle).Clone(); }
        finally { DestroyIcon(handle); }
    }

    private static void FillRoundedRectangle(Graphics graphics, Brush brush, Rectangle rectangle, int radius)
    {
        using var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        graphics.FillPath(brush, path);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}
