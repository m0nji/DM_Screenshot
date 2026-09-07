using System.IO;
using DMShot.Settings;
using Xunit;

/// <summary>Zielordner fuers Speichern: eingestellter Ordner, sonst Bilder\Screenshots.</summary>
public class SaveLocationTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "dmshot_save_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Fallback_IsScreenshotsUnderPictures()
    {
        Assert.Equal(Path.Combine(@"C:\Users\Someone\Pictures", "Screenshots"),
            SaveLocation.Fallback(@"C:\Users\Someone\Pictures"));
    }

    [Fact]
    public void Fallback_WithoutArgument_UsesPicturesFolder()
    {
        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        Assert.Equal(Path.Combine(pictures, "Screenshots"), SaveLocation.Fallback());
    }

    [Fact]
    public void Configured_EmptySetting_IsNull()
    {
        Assert.Null(SaveLocation.Configured(""));
        Assert.Null(SaveLocation.Configured("   "));
        Assert.Null(SaveLocation.Configured(null));
    }

    [Fact]
    public void Configured_TrimsSetting()
    {
        Assert.Equal(@"D:\Bilder", SaveLocation.Configured(@"  D:\Bilder  "));
    }

    [Fact]
    public void EnsureExists_CreatesMissingFolder()
    {
        var target = Path.Combine(_temp, "Screenshots");
        Assert.False(Directory.Exists(target));
        SaveLocation.EnsureExists(target);
        Assert.True(Directory.Exists(target));
    }

    [Fact]
    public void EnsureExists_IsIdempotent()
    {
        var target = Path.Combine(_temp, "Screenshots");
        SaveLocation.EnsureExists(target);
        SaveLocation.EnsureExists(target);
        Assert.True(Directory.Exists(target));
    }

    public void Dispose() { try { Directory.Delete(_temp, true); } catch { } }
}
