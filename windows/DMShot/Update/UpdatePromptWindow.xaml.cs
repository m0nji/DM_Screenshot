using System.Linq;
using System.Windows;
using DMShot.Localization;

namespace DMShot.Update;

/// <summary>
/// The active update prompt (spec 2026-08-02). Deliberately not a MessageBox: it
/// cannot show the changelog and looks foreign next to the rest of the app.
/// Escape and the close button both mean "Later" — a prompt that could be closed
/// without answering would never come back for this version.
/// macOS parity: UpdatePromptWindow.swift.
/// </summary>
public partial class UpdatePromptWindow : Window
{
    private readonly Action _onRestart;
    private readonly Action _onLater;
    private bool _answered;

    public UpdatePromptWindow(string version, IReadOnlyList<ChangelogVersion> notes,
                              Action onRestart, Action onLater)
    {
        InitializeComponent();
        _onRestart = onRestart;
        _onLater = onLater;
        MessageText.Text = string.Format(Loc.Instance["updateReadyMessage"], version);
        Notes.ItemsSource = (notes.Count > 0 ? notes[0].Entries : Array.Empty<ChangelogEntry>())
            .Take(4).Select(e => "• " + e.Text).ToList();
    }

    private void RestartClick(object sender, RoutedEventArgs e) => Answer(_onRestart);
    private void LaterClick(object sender, RoutedEventArgs e) => Answer(_onLater);

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (_answered) return;
        _answered = true;
        _onLater();
    }

    private void Answer(Action action)
    {
        if (_answered) return;
        _answered = true;
        action();
        Close();
    }
}
