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

/// <summary>Session-Ordner: ein beim Speichern gewählter anderer Ordner gilt bis zum Beenden.</summary>
public class SaveLocationSessionTests
{
    private const string Default = @"C:\Users\Someone\Pictures\Shots";
    private const string Fallback = @"C:\Users\Someone\Pictures\Screenshots";
    private const string Other = @"C:\Users\Someone\Desktop\Project";

    [Fact]
    public void Effective_PrefersSessionThenSettingThenFallback()
    {
        Assert.Equal(Other, SaveLocation.Effective(Default, Other, Fallback));
        Assert.Equal(Default, SaveLocation.Effective(Default, null, Fallback));
        Assert.Equal(Fallback, SaveLocation.Effective("  ", null, Fallback));
    }

    [Fact]
    public void SavingIntoAnotherFolder_BecomesSessionFolder()
    {
        Assert.Equal(Other, SaveLocation.SessionAfterSavingIn(Other, Default, Fallback));
    }

    [Fact]
    public void SavingBackIntoDefault_ClearsSession_IgnoringCaseAndTrailingSlash()
    {
        Assert.Null(SaveLocation.SessionAfterSavingIn(Default.ToUpperInvariant() + @"\", Default, Fallback));
    }

    [Fact]
    public void WithoutSetting_FallbackCountsAsDefault()
    {
        Assert.Null(SaveLocation.SessionAfterSavingIn(Fallback, "", Fallback));
        Assert.Equal(Other, SaveLocation.SessionAfterSavingIn(Other, "", Fallback));
    }
}
