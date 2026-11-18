using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using DMShot.Editor;
using DMShot.Platform;
using DMShot.Settings;
using Xunit;

/// <summary>Settings → Saving (auto-save, image format, default folder) and "Reset all
/// settings". macOS parity: SavingSettingsTests.swift.</summary>
public class SavingSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dmshot_saving_" + Guid.NewGuid().ToString("N"));
    private string SettingsPath => Path.Combine(_dir, "settings.json");

    public SavingSettingsTests() => Directory.CreateDirectory(_dir);

    // ── Settings defaults + persistence ──

    [Fact]
    public void AutoSaveIsOff_AndFormatIsPng_ByDefault()
    {
        var s = new SettingsStore(SettingsPath).Load();
        Assert.False(s.AutoSaveCaptures);
        Assert.Equal(SaveImageFormat.Png, s.SaveFormat);
    }

    [Fact]
    public void OldSettingsFile_WithoutSavingKeys_LoadsDefaults()
    {
        File.WriteAllText(SettingsPath, """
        { "AreaHotkey": "Alt+A", "DesignMigratedToGraphiteSand": true }
        """);
        var s = new SettingsStore(SettingsPath).Load();
        Assert.False(s.AutoSaveCaptures);
        Assert.Equal(SaveImageFormat.Png, s.SaveFormat);
        Assert.Equal("Alt+A", s.AreaHotkey);
    }

    [Fact]
    public void SavingOptions_RoundTrip()
    {
        var store = new SettingsStore(SettingsPath);
        store.Save(new Settings { AutoSaveCaptures = true, SaveFormat = SaveImageFormat.Jpeg });
        var loaded = store.Load();
        Assert.True(loaded.AutoSaveCaptures);
        Assert.Equal(SaveImageFormat.Jpeg, loaded.SaveFormat);
    }

    // ── Format helpers ──

    [Fact]
    public void Extensions_AndDialogFilters()
    {
        Assert.Equal("png", SaveFormats.Extension(SaveImageFormat.Png));
        Assert.Equal("jpg", SaveFormats.Extension(SaveImageFormat.Jpeg));
        Assert.Equal("saveDialogPngFilter", SaveFormats.DialogFilterKey(SaveImageFormat.Png));
        Assert.Equal("saveDialogJpegFilter", SaveFormats.DialogFilterKey(SaveImageFormat.Jpeg));
    }

    [Theory]
    [InlineData(@"C:\x\shot.PNG", SaveImageFormat.Png)]
    [InlineData(@"C:\x\shot.jpg", SaveImageFormat.Jpeg)]
    [InlineData(@"C:\x\shot.JPEG", SaveImageFormat.Jpeg)]
    public void FormatFollowsTypedExtension(string path, SaveImageFormat expected)
        => Assert.Equal(expected, SaveFormats.ForFileName(path));

    [Theory]
    [InlineData(@"C:\x\shot.gif")]
    [InlineData(@"C:\x\shot")]
    public void UnknownExtension_HasNoFormat(string path) => Assert.Null(SaveFormats.ForFileName(path));

    [Fact]
    public void Jpeg_FlattensTransparencyOntoWhite()
    {
        using var transparent = new Bitmap(8, 8, PixelFormat.Format32bppArgb);   // all pixels 0,0,0,0
        using var ms = new MemoryStream();
        ImageInterop.Encode(transparent, ms, SaveImageFormat.Jpeg);
        var bytes = ms.ToArray();
        Assert.Equal(new byte[] { 0xFF, 0xD8 }, bytes.Take(2).ToArray());   // JPEG SOI marker

        ms.Position = 0;
        using var read = new Bitmap(ms);
        var c = read.GetPixel(4, 4);
        Assert.True(c.R > 240 && c.G > 240 && c.B > 240, $"expected white, got {c}");
    }

    [Fact]
    public void Png_StaysPng()
    {
        using var bmp = new Bitmap(4, 4);
        using var ms = new MemoryStream();
        ImageInterop.Encode(bmp, ms, SaveImageFormat.Png);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, ms.ToArray().Take(4).ToArray());
    }

    // ── Auto-save ──

    [Fact]
    public void AutoSaveFolder_IsConfigured_ElseFallback_NeverSession()
    {
        var previous = SaveLocation.SessionFolder;
        try
        {
            SaveLocation.SessionFolder = @"E:\Elsewhere";
            Assert.Equal(@"D:\Out", AutoSave.Folder(@" D:\Out "));
            Assert.Equal(SaveLocation.Fallback(), AutoSave.Folder(""));
        }
        finally { SaveLocation.SessionFolder = previous; }
    }

    [Fact]
    public void AutoSave_CreatesFolder_AndUsesScreenshotName()
    {
        var folder = Path.Combine(_dir, "nested", "out");
        var when = new DateTime(2026, 11, 18, 9, 41, 0);
        var path = AutoSave.Write(new byte[] { 1, 2, 3 }, "jpg", folder, when);
        Assert.Equal(ScreenshotFilename.Base(when) + ".jpg", Path.GetFileName(path));
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(path));
    }

    [Fact]
    public void AutoSave_NeverOverwrites_WithinTheSameMinute()
    {
        var when = new DateTime(2026, 11, 18, 9, 41, 0);
        var first = AutoSave.Write(new byte[] { 1 }, "png", _dir, when);
        var second = AutoSave.Write(new byte[] { 2 }, "png", _dir, when);
        Assert.NotEqual(first, second);
        Assert.Equal(ScreenshotFilename.Base(when) + "_1.png", Path.GetFileName(second));
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(first));
        Assert.Equal(new byte[] { 2 }, File.ReadAllBytes(second));
    }

    // ── Reset all settings ──

    [Fact]
    public void Reset_RestoresEveryPreference_ButKeepsHistoryAndBookkeeping()
    {
        var snooze = DateTimeOffset.Now.AddHours(3);
        var s = new Settings
        {
            FullScreenHotkey = "Alt+F", AreaHotkey = "Alt+A", VideoFullHotkey = "Alt+V", VideoAreaHotkey = "Alt+W",
            LaunchAtLogin = true, AfterCapture = AfterCaptureMode.QuickEdit, AppDesign = AppDesign.Black,
            ShowZoomLoupe = false, Language = "de", DefaultSaveFolder = @"D:\Out",
            AutoSaveCaptures = true, SaveFormat = SaveImageFormat.Jpeg,
            StrokeWidth = 9, BlurStrength = 30, BackgroundEnabled = true, FramePadding = "Large",
            FrameCorner = "Round", FrameBackgroundKind = "Solid", FrameSolidHex = "#2b2b2b", FrameGradient = "Cool",
            HistoryLimit = 42, HistoryUnlimited = true, DesignMigratedToGraphiteSand = true,
            UpdateSnoozeVersion = "9.9.9", UpdateSnoozeUntil = snooze,
        };

        s.ResetToDefaults();

        var d = new Settings();
        foreach (var p in typeof(Settings).GetProperties())
        {
            if (Settings.KeptOnReset.Contains(p.Name)) continue;
            Assert.Equal(p.GetValue(d), p.GetValue(s));
        }
        Assert.Equal(42, s.HistoryLimit);            // a lower limit would delete captures
        Assert.True(s.HistoryUnlimited);
        Assert.True(s.DesignMigratedToGraphiteSand);
        Assert.Equal("9.9.9", s.UpdateSnoozeVersion);
        Assert.Equal(snooze, s.UpdateSnoozeUntil);
    }

    [Fact]
    public void Reset_KeepList_NamesRealProperties()
    {
        var names = typeof(Settings).GetProperties().Select(p => p.Name).ToHashSet();
        Assert.All(Settings.KeptOnReset, name => Assert.Contains(name, names));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
    }
}
