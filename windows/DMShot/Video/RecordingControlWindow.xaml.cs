using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
namespace DMShot.Video;

public partial class RecordingControlWindow : Window
{
    public event Action? StopRequested;
    public event Action? CancelRequested;

    public RecordingControlWindow()
    {
        InitializeComponent();
        StopButton.Click += (_, _) => StopRequested?.Invoke();           // V7: Stop = finish
        KeyDown += (_, e) => { if (e.Key == Key.Escape) CancelRequested?.Invoke(); }; // V7: Esc = cancel
        // V11: a non-activating window can miss the first click; force activation on show.
        Loaded += (_, _) => { Activate(); Focus(); };
    }

    public void SetElapsed(double sec)
    {
        int s = (int)sec;
        TimerText.Text = $"{s / 60:00}:{s % 60:00}";
        bool runningOut = 60 - sec <= 10;                                // red at <=10s left
        TimerText.Foreground = runningOut ? new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D)) : Brushes.White;
    }
}
