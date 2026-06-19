using System.Drawing;
using DMShot.Capture;
namespace DMShot.Platform;

public sealed class WpfClipboard : IClipboardService
{
    public void SetImage(Bitmap bmp)
    {
        var src = ImageInterop.ToBitmapSource(bmp);
        System.Windows.Clipboard.SetImage(src);
    }
}
