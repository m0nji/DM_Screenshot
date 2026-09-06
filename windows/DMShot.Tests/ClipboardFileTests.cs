using System.IO;
using DMShot.Platform;
using Xunit;

public class ClipboardFileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dmshot-clipboard-test-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void EachCopyOwnsItsBytesAndEarlierCopyRemainsAvailable()
    {
        var original = new byte[] { 71, 73, 70, 1 };
        var converted = new byte[] { 71, 73, 70, 2 };
        var first = ClipboardFiles.WriteGif(original, _root);
        var second = ClipboardFiles.WriteGif(converted, _root);
        Assert.NotEqual(first, second);
        Assert.Equal(original, File.ReadAllBytes(first));
        Assert.Equal(converted, File.ReadAllBytes(second));
        File.Delete(second);
        Assert.Equal(original, File.ReadAllBytes(first));
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
