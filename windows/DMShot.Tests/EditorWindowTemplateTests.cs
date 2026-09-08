using System.Windows;
using System.Windows.Controls;
using DMShot.Editor;
using Xunit;

public class EditorWindowTemplateTests
{
    [Fact]
    public async Task HistoryRowTemplateLoadsInsideEditor()
    {
        // WPF Application and resource lookup are process-wide; isolate them from
        // the test runner and other STA tests, just as in the actual executable.
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = System.IO.Path.ChangeExtension(typeof(EditorWindowTemplateTests).Assembly.Location, ".exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        })!;
        var output = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30000))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("Editor template load timed out.");
        }
        Assert.True(process.ExitCode == 0, await output);
    }

    [STAThread]
    public static int Main(string[] args)
    {
            Window? window = null;
            Application? app = null;
            try
            {
                // Supply application resources before InitializeComponent without
                // starting the app (tray, hotkeys, updater and user history).
                app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("/DMShot;component/Theme/DmTheme.xaml", UriKind.Relative)
                });
                var editor = new EditorWindow();
                window = editor;
                var list = (ListBox)editor.FindName("HistoryList");
                var row = Assert.IsAssignableFrom<FrameworkElement>(list.ItemTemplate.LoadContent());
                row.DataContext = new EditorWindow.HistoryVM("test", null, false, DateTime.UtcNow);
                row.Measure(new Size(168, 120));
                row.Arrange(new Rect(0, 0, 168, 120));
                Assert.IsType<Button>(row.FindName("DeleteButton"));
                list.ItemsSource = new[] { row.DataContext };
                editor.ShowActivated = false;
                editor.ShowInTaskbar = false;
                editor.Left = -32000;
                editor.Top = -32000;
                editor.Show();
                editor.UpdateLayout();
                if (args.Length == 2 && args[0] == "--render-settings")
                {
                    var settings = (Button)editor.FindName("SettingsButton");
                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(128, 128, 96, 96,
                        System.Windows.Media.PixelFormats.Pbgra32);
                    var drawing = new System.Windows.Media.DrawingVisual();
                    using (var context = drawing.RenderOpen())
                        context.DrawRectangle(new System.Windows.Media.VisualBrush(settings), null, new Rect(0, 0, 128, 128));
                    bitmap.Render(drawing);
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    using var output = System.IO.File.Create(args[1]);
                    encoder.Save(output);
                }
                var container = Assert.IsType<ListBoxItem>(list.ItemContainerGenerator.ContainerFromIndex(0));
                // Exercise the registered opening callback without sending input
                // to the user's desktop. The same menu can serve recycled rows.
                var opening = list.ItemContainerStyle.Setters.OfType<EventSetter>()
                    .Single(setter => setter.Event == ContextMenuService.ContextMenuOpeningEvent);
                opening.Handler.DynamicInvoke(container, null);
                Assert.Same(row.DataContext, container.ContextMenu.DataContext);
                var next = new EditorWindow.HistoryVM("next", null, true, DateTime.UtcNow);
                container.DataContext = next;
                opening.Handler.DynamicInvoke(container, null);
                Assert.Same(next, container.ContextMenu.DataContext);
                Assert.Same(next, ((MenuItem)container.ContextMenu.Items[0]).DataContext);
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
            finally { window?.Close(); app?.Shutdown(); }
    }
}
