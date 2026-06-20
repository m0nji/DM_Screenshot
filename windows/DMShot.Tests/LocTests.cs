using DMShot.Localization;
using Xunit;

public class LocTests
{
    [Fact]
    public void EnAndDeHaveIdenticalKeySets()
    {
        Assert.Equal(
            new SortedSet<string>(Loc.En.Keys),
            new SortedSet<string>(Loc.De.Keys));
    }

    [Fact]
    public void NoEmptyValues()
    {
        foreach (var kv in Loc.En) Assert.False(string.IsNullOrWhiteSpace(kv.Value), $"EN empty: {kv.Key}");
        foreach (var kv in Loc.De) Assert.False(string.IsNullOrWhiteSpace(kv.Value), $"DE empty: {kv.Key}");
    }

    [Fact]
    public void IndexerFollowsCurrentLanguage()
    {
        Loc.Instance.Current = Language.German;
        Assert.Equal("Abbrechen", Loc.Instance["cancel"]);
        Loc.Instance.Current = Language.English;
        Assert.Equal("Cancel", Loc.Instance["cancel"]);
    }

    [Fact]
    public void UnknownKeyFallsBackToKeyName()
    {
        Assert.Equal("nope.missing", Loc.Instance["nope.missing"]);
    }

    [Theory]
    [InlineData("close")]
    [InlineData("estimatedGifSize")]
    [InlineData("gifReady")]
    [InlineData("quickEditEditInMain")]
    [InlineData("quickEditSizeBlur")]
    [InlineData("resetZoomToFit")]
    [InlineData("saveDialogGifFilter")]
    [InlineData("saveDialogPngFilter")]
    [InlineData("shortcutRecorderPrompt")]
    [InlineData("videoPlayhead")]
    [InlineData("videoStartFailedMessage")]
    [InlineData("videoTrimIn")]
    [InlineData("videoTrimOut")]
    [InlineData("videoUnsupportedMessage")]
    public void WindowsUiKeysExistInBothLanguages(string key)
    {
        Assert.True(Loc.En.ContainsKey(key), $"EN missing: {key}");
        Assert.True(Loc.De.ContainsKey(key), $"DE missing: {key}");
    }
}
