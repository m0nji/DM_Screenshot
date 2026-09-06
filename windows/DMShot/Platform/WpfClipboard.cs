using System.Collections.Specialized;
using System.Drawing;
using System.IO;
using System.Windows;
using DMShot.Capture;
namespace DMShot.Platform;

public sealed class WpfClipboard : IClipboardService
{
    public void SetImage(Bitmap bmp)
    {
        var src = ImageInterop.ToBitmapSource(bmp);
        ClipboardRetry.Run(() => System.Windows.Clipboard.SetImage(src));
    }

    public void SetGif(byte[] gifBytes, string gifFilePath)
    {
        var data = new DataObject();
        using var ms = new MemoryStream(gifBytes);
        data.SetData("GIF", ms);                              // raw GIF bytes
        var files = new StringCollection { ClipboardFiles.WriteGif(gifBytes) };
        data.SetFileDropList(files);                          // file reference (Teams/Outlook)
        ClipboardRetry.Run(() => System.Windows.Clipboard.SetDataObject(data, true));
    }
}
