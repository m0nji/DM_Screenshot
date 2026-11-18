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
                var selectionButton = (Button)editor.FindName("SelectionModeButton");
                var selectionBorder = Assert.IsType<Border>(selectionButton.Template.FindName("Bd", selectionButton));
                Assert.Equal(new Thickness(1), selectionBorder.BorderThickness);
                Assert.Same(editor.FindResource("IconButton"), selectionButton.Style);
                selectionButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(editor.IsBatchSelecting);
                selectionButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.False(editor.IsBatchSelecting);
                if (args.Length == 2 && args[0] == "--render-sidebar")
                {
                    var header = (FrameworkElement)selectionButton.Parent;
                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                        (int)Math.Ceiling(header.ActualWidth * 3), (int)Math.Ceiling(header.ActualHeight * 3),
                        288, 288, System.Windows.Media.PixelFormats.Pbgra32);
                    var drawing = new System.Windows.Media.DrawingVisual();
                    using (var context = drawing.RenderOpen())
                        context.DrawRectangle(new System.Windows.Media.VisualBrush(header), null,
                            new Rect(0, 0, header.ActualWidth, header.ActualHeight));
                    bitmap.Render(drawing);
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    using var output = System.IO.File.Create(args[1]);
                    encoder.Save(output);
                }
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
                list.ItemsSource = Enumerable.Range(0, 30).Select(index =>
                    new EditorWindow.HistoryVM(index.ToString(), null, false, DateTime.UtcNow)).ToArray();
                editor.UpdateLayout();
                var scroll = (ScrollViewer)editor.FindName("HistoryScroll");
                Assert.True(scroll.ScrollableHeight > 0);
                var wheel = new System.Windows.Input.MouseWheelEventArgs(
                    System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, -120)
                {
                    RoutedEvent = System.Windows.Input.Mouse.PreviewMouseWheelEvent
                };
                list.RaiseEvent(wheel);
                editor.UpdateLayout();
                Assert.True(wheel.Handled);
                Assert.True(scroll.VerticalOffset > 0);
                var canvas = (CanvasControl)editor.FindName("Canvas");
                using var capture = new System.Drawing.Bitmap(200, 200);
                canvas.Load(capture);
                canvas.Visibility = Visibility.Visible;
                ((FrameworkElement)editor.FindName("EmptyCanvas")).Visibility = Visibility.Collapsed;
                editor.UpdateLayout();
                var first = new Annotation { Kind = ToolKind.Rectangle, X0 = 20, Y0 = 20, X1 = 60, Y1 = 60 };
                var second = new Annotation { Kind = ToolKind.Blur, X0 = 100, Y0 = 100, X1 = 140, Y1 = 140 };
                canvas.Model.Add(first);
                canvas.Model.Add(second);
                var viewport = new Size(canvas.ActualWidth, canvas.ActualHeight);
                var content = new Size(200, 200);
                double scale = ViewportMath.BaseScale(content, viewport, canvas.FitPadding);
                var offset = ViewportMath.Offset(content, viewport, scale, new Point());
                var firstPoint = ViewportMath.ImageToView(new Point(20, 40), new Point(), scale, offset);
                var secondPoint = ViewportMath.ImageToView(new Point(100, 120), new Point(), scale, offset);
                canvas.SelectAt(firstPoint);
                canvas.SelectAt(secondPoint, extend: true);
                canvas.ApplyColorToSelected(0xFF00FF00);
                Assert.Equal(0xFF00FF00, first.ColorArgb);
                Assert.Equal(0xFF00FF00, second.ColorArgb);
                foreach (var key in new[] { System.Windows.Input.Key.Delete, System.Windows.Input.Key.Back })
                {
                    var delete = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
                        PresentationSource.FromVisual(editor), Environment.TickCount, key)
                    {
                        RoutedEvent = System.Windows.Input.Keyboard.KeyDownEvent
                    };
                    canvas.RaiseEvent(delete);
                    Assert.True(delete.Handled);
                    Assert.Empty(canvas.Model.Annotations);
                    canvas.Model.Undo();
                    Assert.Equal(2, canvas.Model.Annotations.Count);
                    canvas.SelectAt(firstPoint);
                    canvas.SelectAt(secondPoint, extend: true);
                }
                canvas.SelectAt(secondPoint, extend: true);
                canvas.DeleteSelected();
                Assert.Same(second, Assert.Single(canvas.Model.Annotations));
                canvas.DisposeImage();
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
            finally { window?.Close(); app?.Shutdown(); }
    }
}
