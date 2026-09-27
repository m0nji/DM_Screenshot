using System;
using System.Collections.Generic;
using DMShot.Editor;
using Xunit;

public class ScreenshotFilenameTests
{
    [Fact]
    public void Base_FormatsDDMMYYYY_HH_MM()
    {
        var name = ScreenshotFilename.Base(new DateTime(2026, 6, 18, 14, 30, 0));
        Assert.Equal("DM_Screenshot_18062026_14_30", name);
    }

    [Fact]
    public void Base_ZeroPads()
    {
        var name = ScreenshotFilename.Base(new DateTime(2026, 1, 3, 9, 5, 0));
        Assert.Equal("DM_Screenshot_03012026_09_05", name);
    }

    [Fact]
    public void Unique_ReturnsBaseWhenFree()
    {
        var name = ScreenshotFilename.Unique("DM_Screenshot_18062026_14_30", _ => false);
        Assert.Equal("DM_Screenshot_18062026_14_30.png", name);
    }

    [Fact]
    public void Unique_AppendsSuffixOnCollision()
    {
        var taken = new HashSet<string>
        {
            "DM_Screenshot_18062026_14_30.png",
            "DM_Screenshot_18062026_14_30_1.png",
        };
        var name = ScreenshotFilename.Unique("DM_Screenshot_18062026_14_30", taken.Contains);
        Assert.Equal("DM_Screenshot_18062026_14_30_2.png", name);
    }
}
