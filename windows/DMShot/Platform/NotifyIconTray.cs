using System.Windows.Controls;
using System.Windows.Markup;
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
    public event Action? SettingsRequested;
    public event Action? QuitRequested;

    public NotifyIconTray()
    {
        _icon = new TaskbarIcon
        {
            ToolTipText = "DM_Screenshot",
            IconSource = LoadIcon()
        };
        var menu = new ContextMenu { Style = MenuStyle };
        menu.Resources.Add(typeof(MenuItem), MenuItemStyle);
        // A Separator inside a menu is styled via MenuItem.SeparatorStyleKey, NOT typeof(Separator)
        // — keying it wrong left the OS default (a bright white line) showing on the dark menu.
        menu.Resources.Add(MenuItem.SeparatorStyleKey, SeparatorStyle);
        menu.Items.Add(Item("New Fullscreen Shot", () => FullScreenRequested?.Invoke()));
        menu.Items.Add(Item("New Area Shot", () => AreaRequested?.Invoke()));
        menu.Items.Add(Item("Open Editor", () => OpenRequested?.Invoke()));
        menu.Items.Add(Item("Settings…", () => SettingsRequested?.Invoke()));
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

    // ===== Dark context-menu styling (the default WPF menu rendered dark text on the
    // OS dark menu background — unreadable). Dark surface + light text + dark hover. =====

    private static System.Windows.Style St(string xaml) => (System.Windows.Style)XamlReader.Parse(xaml);

    private static readonly System.Windows.Style MenuStyle = St(
@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ContextMenu'>
  <Setter Property='Foreground' Value='#E8E8EA'/><Setter Property='FontSize' Value='13'/>
  <Setter Property='Template'><Setter.Value>
    <ControlTemplate TargetType='ContextMenu'>
      <Border Background='#1E1E22' BorderBrush='#3A3A42' BorderThickness='1' CornerRadius='8' Padding='3'>
        <ItemsPresenter/>
      </Border>
    </ControlTemplate>
  </Setter.Value></Setter>
</Style>");

    private static readonly System.Windows.Style MenuItemStyle = St(
@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='MenuItem'>
  <Setter Property='Foreground' Value='#E8E8EA'/>
  <Setter Property='Template'><Setter.Value>
    <ControlTemplate TargetType='MenuItem'>
      <Border x:Name='b' Background='Transparent' CornerRadius='5' Padding='12,6' Margin='2,1'>
        <ContentPresenter ContentSource='Header' RecognizesAccessKey='True' VerticalAlignment='Center'/>
      </Border>
      <ControlTemplate.Triggers>
        <Trigger Property='IsHighlighted' Value='True'><Setter TargetName='b' Property='Background' Value='#3A3A42'/></Trigger>
        <Trigger Property='IsEnabled' Value='False'><Setter Property='Foreground' Value='#6A6A70'/></Trigger>
      </ControlTemplate.Triggers>
    </ControlTemplate>
  </Setter.Value></Setter>
</Style>");

    private static readonly System.Windows.Style SeparatorStyle = St(
@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Separator'>
  <Setter Property='Template'><Setter.Value>
    <ControlTemplate TargetType='Separator'>
      <Border Height='1' Background='#2E2E35' Margin='8,4'/>
    </ControlTemplate>
  </Setter.Value></Setter>
</Style>");

    public void Show() => _icon.Visibility = System.Windows.Visibility.Visible;
    public void Dispose() => _icon.Dispose();
}
