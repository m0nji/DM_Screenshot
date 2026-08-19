using System.Reflection;
using System.Threading;
using System.Windows;
using DMShot.Localization;
using DMShot.Settings;
using Xunit;

public class ShortcutRecorderControlTests
{
    private static void OnSta(Action act)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try { act(); }
            catch (Exception caught) { exception = caught; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (exception is not null) throw exception;
    }

    [Fact]
    public void RecordingPrompt_CanBeCancelledWithoutLosingCurrentHotkey() => OnSta(() =>
    {
        const string current = "Ctrl+Shift+1";
        var recorder = new ShortcutRecorderControl(current);

        Assert.Equal(current, recorder.Hotkey);
        Assert.Equal(current, recorder.Text);

        Invoke(recorder, "BeginRecording");
        Assert.Equal(Loc.Instance["shortcutRecorderPrompt"], recorder.Text);

        Invoke(recorder, "EndRecording");
        Assert.Equal(current, recorder.Hotkey);
        Assert.Equal(current, recorder.Text);

        recorder.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent, recorder));
    });

    private static void Invoke(ShortcutRecorderControl recorder, string method)
    {
        var target = typeof(ShortcutRecorderControl).GetMethod(
            method, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(target);
        target.Invoke(recorder, null);
    }
}
