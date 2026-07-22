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
        System.Windows.Clipboard.SetImage(src);
    }

    public void SetGif(byte[] gifBytes, string gifFilePath)
    {
        var data = new DataObject();
        var ms = new MemoryStream(gifBytes);
        data.SetData("GIF", ms);                              // raw GIF bytes
        var files = new StringCollection { gifFilePath };
        data.SetFileDropList(files);                          // file reference (Teams/Outlook)
        System.Windows.Clipboard.SetDataObject(data, true);
    }
}
