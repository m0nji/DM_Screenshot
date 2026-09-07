using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using DMShot.History;
using Xunit;

public class ThumbnailCacheTests
{
    [Fact]
    public async Task RepeatedRefreshReusesFrozenDecodeAndReleasesFileHandle()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
        using (var image = new Bitmap(8, 8)) image.Save(path, ImageFormat.Png);
        try
        {
            var cache = new ThumbnailCache();
            var first = cache.GetAsync(path);
            Assert.Same(first, cache.GetAsync(path));
            var decoded = await first;
            Assert.NotNull(decoded);
            Assert.True(decoded.IsFrozen);
            File.Delete(path); // decoder must not pin history files
            Assert.Same(decoded, await cache.GetAsync(path));
            cache.Retain(Array.Empty<string>());
            Assert.Null(await cache.GetAsync(path)); // deleted revision is not returned from cache
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task NewAssetRevisionDoesNotReusePreviousThumbnail()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string firstPath = Path.Combine(root, "first.png"), secondPath = Path.Combine(root, "second.png");
            using (var image = new Bitmap(8, 8)) image.Save(firstPath, ImageFormat.Png);
            using (var image = new Bitmap(16, 8)) image.Save(secondPath, ImageFormat.Png);
            var cache = new ThumbnailCache();
            Assert.Equal(8, (await cache.GetAsync(firstPath))!.PixelWidth);
            cache.Retain(new[] { secondPath });
            Assert.Equal(16, (await cache.GetAsync(secondPath))!.PixelWidth);
        }
        finally { Directory.Delete(root, true); }
    }
}
