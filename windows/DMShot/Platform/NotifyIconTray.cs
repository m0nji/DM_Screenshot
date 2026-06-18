using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hardcodet.Wpf.TaskbarNotification;
namespace DMShot.Platform;

public sealed class NotifyIconTray : ITrayIcon
{
    private readonly TaskbarIcon _icon;
    public event Action? OpenRequested;
    public event Action? FullScreenRequested;
    public event Action? AreaRequested;
    public event Action? QuitRequested;

    public NotifyIconTray()
    {
        _icon = new TaskbarIcon
        {
            ToolTipText = "DM_Screenshot",
            IconSource = LoadIcon()
        };
        var menu = new ContextMenu();
        menu.Items.Add(Item("New Fullscreen Shot", () => FullScreenRequested?.Invoke()));
        menu.Items.Add(Item("New Area Shot", () => AreaRequested?.Invoke()));
        menu.Items.Add(Item("Open Editor", () => OpenRequested?.Invoke()));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Quit", () => QuitRequested?.Invoke()));
        _icon.ContextMenu = menu;
        _icon.TrayMouseDoubleClick += (_, _) => OpenRequested?.Invoke();
    }

    // Tries the bundled .ico (added in Task 17); falls back to a generated orange
    // marker so the tray icon is still visible before the final art ships.
    private static ImageSource LoadIcon()
    {
        try
        {
            return new BitmapImage(new Uri("pack://application:,,,/Resources/AppIcon.ico"));
        }
        catch
        {
            var rtb = new RenderTargetBitmap(16, 16, 96, 96, PixelFormats.Pbgra32);
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x24)), null,
                    new System.Windows.Rect(0, 0, 16, 16), 3, 3);
                dc.DrawRectangle(null, new System.Windows.Media.Pen(new SolidColorBrush(Color.FromRgb(0xC9, 0x7B, 0x4A)), 1.5),
                    new System.Windows.Rect(3, 3, 10, 10));
            }
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }
    }

    private static MenuItem Item(string header, Action onClick)
    { var m = new MenuItem { Header = header }; m.Click += (_, _) => onClick(); return m; }

    public void Show() => _icon.Visibility = System.Windows.Visibility.Visible;
    public void Dispose() => _icon.Dispose();
}
