using System.Windows;
using System.Windows.Controls;
namespace DMShot.Settings;

public partial class SettingsWindow : Window
{
    private readonly Settings _settings;
    private readonly SettingsStore _store;
    public event Action<Settings>? Saved;

    public SettingsWindow(Settings settings, SettingsStore store)
    { InitializeComponent(); _settings = settings; _store = store; ShowShortcuts(); }

    private void NavChanged(object sender, SelectionChangedEventArgs e)
    {
        switch ((Nav.SelectedItem as ListBoxItem)?.Content)
        {
            case "Shortcuts": ShowShortcuts(); break;
            case "General": ShowGeneral(); break;
            case "Updates": ShowText("Updates", "Check github.com/m0nji/DM_Screenshot for new versions."); break;
            case "Language": ShowText("Language", "English (more languages later)."); break;
        }
    }

    private void ShowShortcuts()
    {
        Pane.Children.Clear();
        Pane.Children.Add(new TextBlock { Text = "Global Shortcuts", Foreground = System.Windows.Media.Brushes.White, FontSize = 16, Margin = new Thickness(0,0,0,12) });
        Pane.Children.Add(Row("Full screen", _settings.FullScreenHotkey, h => { _settings.FullScreenHotkey = h; Commit(); }));
        Pane.Children.Add(Row("Area selection", _settings.AreaHotkey, h => { _settings.AreaHotkey = h; Commit(); }));
    }

    private FrameworkElement Row(string label, string current, Action<string> onSet)
    {
        var rec = new ShortcutRecorderControl { Text = current, Width = 200 };
        rec.HotkeyChanged += onSet;
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0,4,0,4) };
        sp.Children.Add(new TextBlock { Text = label, Foreground = System.Windows.Media.Brushes.White, Width = 140, VerticalAlignment = VerticalAlignment.Center });
        sp.Children.Add(rec);
        return sp;
    }

    private void ShowGeneral()
    {
        Pane.Children.Clear();
        var cb = new CheckBox { Content = "Launch at login", Foreground = System.Windows.Media.Brushes.White, IsChecked = _settings.LaunchAtLogin };
        cb.Checked += (_, _) => { _settings.LaunchAtLogin = true; LaunchAtLogin.Set(true); Commit(); };
        cb.Unchecked += (_, _) => { _settings.LaunchAtLogin = false; LaunchAtLogin.Set(false); Commit(); };
        Pane.Children.Add(cb);
    }

    private void ShowText(string title, string body)
    {
        Pane.Children.Clear();
        Pane.Children.Add(new TextBlock { Text = title, Foreground = System.Windows.Media.Brushes.White, FontSize = 16, Margin = new Thickness(0,0,0,8) });
        Pane.Children.Add(new TextBlock { Text = body, Foreground = System.Windows.Media.Brushes.LightGray, TextWrapping = TextWrapping.Wrap });
    }

    private void Commit() { _store.Save(_settings); Saved?.Invoke(_settings); }
}
