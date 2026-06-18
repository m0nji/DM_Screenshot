using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace DMShot.Settings;

public partial class SettingsWindow : Window
{
    private readonly Settings _settings;
    private readonly SettingsStore _store;
    public event Action<Settings>? Saved;

    private Brush Text => (Brush)FindResource("DmText");
    private Brush TextDim => (Brush)FindResource("DmTextDim");

    public SettingsWindow(Settings settings, SettingsStore store)
    {
        InitializeComponent();
        DMShot.Platform.DarkTitleBar.Apply(this);
        _settings = settings; _store = store;
        ShowGeneral();
    }

    private void NavChanged(object sender, SelectionChangedEventArgs e)
    {
        switch (Nav.SelectedIndex)
        {
            case 0: ShowGeneral(); break;
            case 1: ShowShortcuts(); break;
            case 2: ShowLanguage(); break;
            case 3: ShowUpdates(); break;
        }
    }

    private TextBlock SectionTitle(string t) => new()
    {
        Text = t, Foreground = Text, FontSize = 24, FontWeight = FontWeights.Bold,
        Margin = new Thickness(0, 0, 0, 18)
    };

    private void ShowGeneral()
    {
        if (Pane is null) return;
        Pane.Children.Clear();
        Pane.Children.Add(SectionTitle("General"));

        var cb = new CheckBox
        {
            Content = "Launch at login", Foreground = Text, FontSize = 14,
            IsChecked = LaunchAtLogin.Get()
        };
        cb.Checked += (_, _) => { _settings.LaunchAtLogin = true; LaunchAtLogin.Set(true); Commit(); };
        cb.Unchecked += (_, _) => { _settings.LaunchAtLogin = false; LaunchAtLogin.Set(false); Commit(); };
        Pane.Children.Add(cb);
        Pane.Children.Add(new TextBlock
        {
            Text = "Start DM_Screenshot automatically when you sign in.",
            Foreground = TextDim, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap
        });
    }

    private void ShowShortcuts()
    {
        if (Pane is null) return;
        Pane.Children.Clear();
        Pane.Children.Add(SectionTitle("Shortcuts"));
        Pane.Children.Add(Row("Full screen", _settings.FullScreenHotkey, h => { _settings.FullScreenHotkey = h; Commit(); }));
        Pane.Children.Add(Row("Area selection", _settings.AreaHotkey, h => { _settings.AreaHotkey = h; Commit(); }));
        Pane.Children.Add(new TextBlock
        {
            Text = "Click a field and press the new key combination.",
            Foreground = TextDim, Margin = new Thickness(0, 14, 0, 0), TextWrapping = TextWrapping.Wrap
        });
    }

    private FrameworkElement Row(string label, string current, Action<string> onSet)
    {
        var rec = new ShortcutRecorderControl
        {
            Text = current, Width = 220, Height = 30,
            Style = (Style)FindResource("DmTextBox")
        };
        rec.HotkeyChanged += onSet;
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 5) };
        sp.Children.Add(new TextBlock
        {
            Text = label, Foreground = Text, Width = 150, FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center
        });
        sp.Children.Add(rec);
        return sp;
    }

    private void ShowLanguage()
    {
        if (Pane is null) return;
        Pane.Children.Clear();
        Pane.Children.Add(SectionTitle("Language"));
        Pane.Children.Add(new TextBlock
        {
            Text = "English (more languages later).",
            Foreground = TextDim, TextWrapping = TextWrapping.Wrap
        });
    }

    private void ShowUpdates()
    {
        if (Pane is null) return;
        Pane.Children.Clear();
        Pane.Children.Add(SectionTitle("Updates"));

        var row = new Grid { Margin = new Thickness(0, 0, 0, 18) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel();
        left.Children.Add(new TextBlock { Text = "Version", Foreground = Text, FontSize = 15 });
        left.Children.Add(new TextBlock { Text = "Installed version.", Foreground = TextDim });
        Grid.SetColumn(left, 0);
        var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        string verText = v is null ? "0.1.1" : $"{v.Major}.{v.Minor}.{v.Build}";
        var ver = new TextBlock { Text = verText, Foreground = Text, FontSize = 15, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(ver, 1);
        row.Children.Add(left); row.Children.Add(ver);
        Pane.Children.Add(row);

        var btn = new Button
        {
            Content = "Check for Updates", Style = (Style)FindResource("AccentButton"),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        Pane.Children.Add(btn);
        Pane.Children.Add(new TextBlock
        {
            Text = "Automatic update checks will be added later.",
            Foreground = TextDim, Margin = new Thickness(0, 14, 0, 0), TextWrapping = TextWrapping.Wrap
        });
    }

    private void Commit() { _store.Save(_settings); Saved?.Invoke(_settings); }
}
