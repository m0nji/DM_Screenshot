using DMShot.Settings;
using Xunit;

public class AfterCaptureSettingTests
{
    [Fact]
    public void DefaultsToMainWindow()
    {
        Assert.Equal(AfterCaptureMode.MainWindow, new Settings().AfterCapture);
    }

    [Fact]
    public void RoundTripsThroughStore()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "dmshot-test-" + System.Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new SettingsStore(path);
            store.Save(new Settings { AfterCapture = AfterCaptureMode.QuickEdit });
            Assert.Equal(AfterCaptureMode.QuickEdit, store.Load().AfterCapture);
        }
        finally { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); }
    }

    [Fact]
    public void MissingKeyFallsBackToMainWindow()
    {
        // Settings JSON from an older install without the AfterCapture field.
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "dmshot-test-" + System.Guid.NewGuid().ToString("N") + ".json");
        try
        {
            System.IO.File.WriteAllText(path,
                "{ \"FullScreenHotkey\": \"Ctrl+Shift+1\", \"AreaHotkey\": \"Ctrl+Shift+2\" }");
            Assert.Equal(AfterCaptureMode.MainWindow, new SettingsStore(path).Load().AfterCapture);
        }
        finally { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); }
    }
}
