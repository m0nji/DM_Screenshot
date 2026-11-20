using System.Drawing;
using DMShot.Capture;
using DMShot.Platform;
using Xunit;

public class CaptureCoordinatorReliabilityTests
{
    [Fact]
    public void FailedCaptureReleasesGuardAndReportsFailure()
    {
        var capturer = new FailingCapturer();
        var coordinator = new CaptureCoordinator(capturer);
        int failures = 0;
        coordinator.CaptureFailed += _ =>
        {
            failures++;
            coordinator.CaptureFullScreen(); // reentrant request must be ignored
        };
        coordinator.CaptureFullScreen();
        coordinator.CaptureArea();
        Assert.Equal(2, failures);
        Assert.Equal(2, capturer.Attempts);
    }

    [Fact]
    public void PartialMultiMonitorFailureDisposesEarlierCaptureAndAllowsRetry()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var capturer = new PartialCapturer();
                var coordinator = new CaptureCoordinator(capturer);
                int errors = 0;
                coordinator.CaptureFailed += _ => errors++;
                coordinator.CaptureArea();
                Assert.Equal(1, errors);
                Assert.ThrowsAny<Exception>(() => capturer.First!.GetPixel(0, 0));
                coordinator.CaptureArea();
                Assert.Equal(2, errors);
                Assert.ThrowsAny<Exception>(() => capturer.First!.GetPixel(0, 0));
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        Assert.Null(failure);
    }

    private sealed class PartialCapturer : IScreenCapturer
    {
        public Bitmap? First;
        public IReadOnlyList<DisplayInfo> GetDisplays() => new[]
        {
            new DisplayInfo(0, new Rectangle(0, 0, 10, 10), true),
            new DisplayInfo(1, new Rectangle(10, 0, 10, 10), false)
        };
        public Bitmap CaptureDisplay(DisplayInfo display)
        {
            if (display.Index == 1) throw new InvalidOperationException("second monitor failed");
            return First = new Bitmap(10, 10);
        }
        public Bitmap CaptureVirtualDesktop(out Rectangle bounds) => throw new NotImplementedException();
    }

    private sealed class FailingCapturer : IScreenCapturer
    {
        public int Attempts;
        public IReadOnlyList<DisplayInfo> GetDisplays() { Attempts++; throw new InvalidOperationException("capture unavailable"); }
        public Bitmap CaptureVirtualDesktop(out Rectangle bounds) => throw new NotImplementedException();
        public Bitmap CaptureDisplay(DisplayInfo display) => throw new NotImplementedException();
    }
}
