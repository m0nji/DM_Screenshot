using System.Drawing;
using System.IO;
using DMShot.Platform;
using Xunit;

/// <summary>
/// The save error dialog is only useful if it names the actual cause. Bitmap.Save(path)
/// hands every failure to GDI+, which reports "A generic error occurred in GDI+" and
/// nothing else — verified on-device 2026-07-24.
/// </summary>
public class SavePngTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dmshot_png_" + Guid.NewGuid().ToString("N"));

    public SavePngTests() => Directory.CreateDirectory(_dir);

    [Fact]
    public void WritesAReadablePng()
    {
        var path = Path.Combine(_dir, "out.png");
        using (var bmp = new Bitmap(12, 8))
            ImageInterop.SavePng(bmp, path);

        using var read = new Bitmap(path);
        Assert.Equal(12, read.Width);
        Assert.Equal(8, read.Height);
    }

    [Fact]
    public void OverwritesAnExistingFile()
    {
        var path = Path.Combine(_dir, "out.png");
        File.WriteAllBytes(path, new byte[5000]);   // larger than the PNG we are about to write
        using (var bmp = new Bitmap(4, 4))
            ImageInterop.SavePng(bmp, path);

        using var read = new Bitmap(path);   // truncated, not appended to
        Assert.Equal(4, read.Width);
    }

    [Fact]
    public void LockedTargetReportsTheRealReason()
    {
        var path = Path.Combine(_dir, "locked.png");
        using var hold = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);

        using var bmp = new Bitmap(4, 4);
        var ex = Assert.ThrowsAny<Exception>(() => ImageInterop.SavePng(bmp, path));

        Assert.IsAssignableFrom<IOException>(ex);
        Assert.DoesNotContain("GDI+", ex.Message);
        Assert.Contains("another process", ex.Message);
    }

    [Fact]
    public void MissingDirectoryReportsTheRealReason()
    {
        var path = Path.Combine(_dir, "nope", "out.png");
        using var bmp = new Bitmap(4, 4);

        var ex = Assert.ThrowsAny<Exception>(() => ImageInterop.SavePng(bmp, path));

        Assert.IsAssignableFrom<DirectoryNotFoundException>(ex);
        Assert.DoesNotContain("GDI+", ex.Message);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }
}
