using System.IO;
using System.Windows.Media.Imaging;

namespace DMShot.History;

/// <summary>Bounded by active history assets, keyed by immutable revision path. Decode and
/// file reads run off Dispatcher; OnLoad releases the file before publication.</summary>
public sealed class ThumbnailCache
{
    private readonly Dictionary<string, Task<BitmapSource?>> _loads = new();
    public void Retain(IEnumerable<string> paths)
    {
        var keep = paths.Where(p => !string.IsNullOrEmpty(p)).ToHashSet();
        foreach (var path in _loads.Keys.Where(p => !keep.Contains(p)).ToArray()) _loads.Remove(path);
    }
    public BitmapSource? GetReady(string path)
        => _loads.TryGetValue(path, out var task) && task.IsCompletedSuccessfully ? task.Result : null;

    public Task<BitmapSource?> GetAsync(string path)
    {
        if (string.IsNullOrEmpty(path)) return Task.FromResult<BitmapSource?>(null);
        if (_loads.TryGetValue(path, out var cached)) return cached;
        return _loads[path] = Task.Run<BitmapSource?>(() =>
        {
            try
            {
                using var stream = File.OpenRead(path);
                var image = new BitmapImage(); image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream;
                image.EndInit(); image.Freeze(); return image;
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); return null; }
        });
    }
}
