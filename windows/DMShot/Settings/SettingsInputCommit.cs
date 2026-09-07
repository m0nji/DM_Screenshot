using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace DMShot.Settings;

/// Commits a draft when clicking non-focusable content, leaving the window, or
/// removing the pane. Normal LostFocus/Enter behavior remains with the input.
public sealed class SettingsInputCommit
{
    private readonly TextBox _box;
    private readonly Action _commit;
    private Window? _window;
    public SettingsInputCommit(TextBox box, Action commit)
    {
        _box = box; _commit = commit;
        box.Loaded += (_, _) => Attach();
        box.Unloaded += (_, _) => { _commit(); Detach(); };
        if (box.IsLoaded) Attach();
    }
    private void Attach()
    {
        Detach();
        _window = Window.GetWindow(_box);
        if (_window is null) return;
        _window.PreviewMouseDown += MouseDown;
        _window.Deactivated += Deactivated;
        _window.Closing += Closing;
    }
    private void Detach()
    {
        if (_window is null) return;
        _window.PreviewMouseDown -= MouseDown;
        _window.Deactivated -= Deactivated;
        _window.Closing -= Closing;
        _window = null;
    }
    private void MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_box.IsKeyboardFocusWithin) return;
        if (e.OriginalSource is DependencyObject source &&
            (source == _box || ((source is Visual || source is Visual3D) && _box.IsAncestorOf(source)))) return;
        _commit(); // do not swallow the user's original click
    }
    private void Deactivated(object? sender, EventArgs e) => _commit();
    private void Closing(object? sender, CancelEventArgs e) => _commit();
}
