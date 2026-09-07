using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DMShot.Settings;
using Xunit;

public class SettingsInputCommitTests
{
    [Fact]
    public void BlankAreaClickAndCloseCommitWithoutEnter()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                var panel = new StackPanel();
                var box = new TextBox { Text = "10" };
                var blank = new Border { Height = 60, Background = System.Windows.Media.Brushes.Transparent };
                panel.Children.Add(box); panel.Children.Add(blank);
                window = new Window { Content = panel, Width = 300, Height = 200, ShowInTaskbar = false };
                int saved = 10;
                _ = new SettingsInputCommit(box, () => saved = int.Parse(box.Text));
                window.Show();
                window.Activate();
                box.Focus();
                Keyboard.Focus(box);
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                box.Text = "25";
                Assert.Equal(10, saved); // no retention change for each typed digit
                box.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                    { RoutedEvent = Mouse.PreviewMouseDownEvent });
                Assert.Equal(10, saved); // clicking inside the editor must keep the draft
                blank.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                    { RoutedEvent = Mouse.PreviewMouseDownEvent });
                Assert.Equal(25, saved);
                box.Text = "37";
                window.Close();
                Assert.Equal(37, saved);
                window = null;
            }
            catch (Exception ex) { error = ex; }
            finally { window?.Close(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        if (error != null) throw error;
    }
}
