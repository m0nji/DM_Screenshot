using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using DMShot.Platform;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

public sealed class ImageImportTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dmshot_import_" + Guid.NewGuid().ToString("N"));
    public ImageImportTests() => Directory.CreateDirectory(_dir);

    [Fact]
    public void PngRetainsAlphaAndReleasesSourceFile()
    {
        string path = Path.Combine(_dir, "alpha.png");
        using (var image = new Image<Rgba32>(2, 1))
        {
            image[0, 0] = new Rgba32(10, 20, 30, 64);
            image[1, 0] = new Rgba32(40, 50, 60, 255);
            image.SaveAsPng(path);
        }

        using var result = ImageImport.LoadFile(path);
        Assert.Equal(2, result.Width);
        Assert.Equal(64, result.GetPixel(0, 0).A);
        using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Fact]
    public void JpegExifOrientationIsNormalized()
    {
        string path = Path.Combine(_dir, "oriented.jpg");
        using (var image = new Image<Rgba32>(3, 2))
        {
            image.Metadata.ExifProfile = new ExifProfile();
            image.Metadata.ExifProfile.SetValue(ExifTag.Orientation, (ushort)6);
            image.Save(path, new JpegEncoder());
        }

        using var result = ImageImport.LoadFile(path);
        Assert.Equal(2, result.Width);
        Assert.Equal(3, result.Height);
    }

    [Fact]
    public void AnimatedPngImportsOnlyItsFirstCanvas()
    {
        string path = Path.Combine(_dir, "animated.png");
        using (var image = new Image<Rgba32>(2, 2, new Rgba32(220, 10, 20, 255)))
        {
            using var later = new Image<Rgba32>(2, 2, new Rgba32(10, 20, 220, 255));
            for (int i = 0; i < 24; i++) image.Frames.AddFrame(later.Frames.RootFrame);
            image.SaveAsPng(path);
        }

        using var result = ImageImport.LoadFile(path);
        var pixel = result.GetPixel(0, 0);
        Assert.True(pixel.R > 200 && pixel.B < 40);
    }

    [Fact]
    public void RejectsUnsupportedAndCorruptFiles()
    {
        string gif = Path.Combine(_dir, "image.gif");
        File.WriteAllBytes(gif, "GIF89a"u8.ToArray());
        Assert.Equal(ImageImportFailure.Invalid, Assert.Throws<ImageImportException>(() => ImageImport.LoadFile(gif)).Failure);

        string png = Path.Combine(_dir, "broken.png");
        File.WriteAllBytes(png, new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a });
        Assert.Equal(ImageImportFailure.Invalid, Assert.Throws<ImageImportException>(() => ImageImport.LoadFile(png)).Failure);
    }

    [Fact]
    public void RejectsEncodedFileOverBudgetBeforeDecode()
    {
        string path = Path.Combine(_dir, "large.png");
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            stream.SetLength(ImageImport.MaxEncodedBytes + 1);

        Assert.Equal(ImageImportFailure.TooLarge, Assert.Throws<ImageImportException>(() => ImageImport.LoadFile(path)).Failure);
    }

    [Theory]
    [InlineData(32769, 1)]
    [InlineData(10000, 4001)]
    public void MetadataLimitsRejectDimensionsBeforeDecode(int width, int height)
    {
        Assert.Equal(ImageImportFailure.TooLarge,
            Assert.Throws<ImageImportException>(() => ImageImport.ValidateDimensions(width, height)).Failure);
    }

    [Fact]
    public void NativeClipboardBitmapIsDetachedAndPreservesAlpha()
    {
        using var source = new Bitmap(1, 1, PixelFormat.Format32bppArgb);
        source.SetPixel(0, 0, System.Drawing.Color.FromArgb(73, 10, 20, 30));
        using var imported = ImageImport.FromClipboardBitmap(source);
        source.SetPixel(0, 0, System.Drawing.Color.Black);
        Assert.Equal(73, imported.GetPixel(0, 0).A);
    }

    [Theory]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, true, false)]
    [InlineData(false, false, true, false)]
    [InlineData(true, false, false, false)]
    public void PasteRoutingRequiresEditorContextAndAnImage(bool controlOnly, bool textInput, bool hasImage, bool expected)
        => Assert.Equal(expected, ImageImport.ShouldHandlePaste(controlOnly, textInput, hasImage));

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }
}

public sealed class ImageImportSessionTests
{
    [Fact]
    public async Task PersistsBeforeReplacingAndSerializesImports()
    {
        var events = new List<string>();
        var session = new ImageImportSession(
            persistCurrent: () => { events.Add("persist"); return true; },
            accept: bitmap => { events.Add($"accept:{bitmap.Width}"); return Task.CompletedTask; });

        await Task.WhenAll(
            session.ImportAsync(() => { events.Add("decode:1"); return new Bitmap(1, 1); }),
            session.ImportAsync(() => { events.Add("decode:2"); return new Bitmap(2, 1); }));

        Assert.Equal(new[] { "persist", "decode:1", "accept:1", "persist", "decode:2", "accept:2" }, events);
    }

    [Fact]
    public async Task FailedPersistenceDoesNotDecodeOrReplace()
    {
        bool decoded = false, accepted = false;
        var session = new ImageImportSession(() => false, _ => { accepted = true; return Task.CompletedTask; });
        await Assert.ThrowsAsync<ImageImportException>(() => session.ImportAsync(() => { decoded = true; return new Bitmap(1, 1); }));
        Assert.False(decoded);
        Assert.False(accepted);
    }

    [Fact]
    public async Task CancelPendingRejectsLateResultAndDoesNotStartQueuedDecoder()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool secondDecoded = false, accepted = false;
        var session = new ImageImportSession(() => true, _ => { accepted = true; return Task.CompletedTask; });
        var first = session.ImportAsync(() =>
        {
            started.SetResult();
            release.Task.GetAwaiter().GetResult();
            return new Bitmap(1, 1);
        });
        await started.Task;
        var second = session.ImportAsync(() => { secondDecoded = true; return new Bitmap(1, 1); });

        session.CancelPending();
        release.SetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        Assert.False(secondDecoded);
        Assert.False(accepted);

        session.Resume();
        await session.ImportAsync(() => new Bitmap(1, 1));
        Assert.True(accepted);
    }
}
