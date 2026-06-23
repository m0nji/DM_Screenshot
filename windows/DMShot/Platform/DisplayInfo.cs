using System.Drawing;
namespace DMShot.Platform;

public record DisplayInfo(int Index, Rectangle Bounds, bool IsPrimary);

public interface IScreenCapturer
{
    IReadOnlyList<DisplayInfo> GetDisplays();
    Bitmap CaptureDisplay(DisplayInfo display);
    Bitmap CaptureVirtualDesktop(out Rectangle virtualBounds);
}
