using System.IO;
using DMShot.Localization;
using DMShot.Settings;
using Xunit;

public class SettingsLanguageTests
{
    [Fact]
    public void DefaultLanguageIsEnglish()
    {
        Assert.Equal("en", new Settings().Language);
        Assert.Equal(Language.English, LanguageCodes.FromCode(null));
        Assert.Equal(Language.English, LanguageCodes.FromCode("fr"));
        Assert.Equal(Language.German, LanguageCodes.FromCode("de"));
    }

    [Fact]
    public void RoundTripsThroughJsonFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dmshot-{Guid.NewGuid():N}.json");
        var store = new SettingsStore(path);
        store.Save(new Settings { Language = "de" });
        Assert.Equal("de", store.Load().Language);
        File.Delete(path);
    }

    [Fact]
    public void DisplayNamesAreNative()
    {
        Assert.Equal("English", Language.English.DisplayName());
        Assert.Equal("Deutsch", Language.German.DisplayName());
        Assert.Equal("en", Language.English.Code());
        Assert.Equal("de", Language.German.Code());
    }
}
