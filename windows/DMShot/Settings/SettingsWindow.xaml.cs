using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DMShot.Update;
namespace DMShot.Settings;

public partial class SettingsWindow : Window
{
    private readonly Settings _settings;
    private readonly SettingsStore _store;
    private readonly UpdaterService _updater;
    public event Action<Settings>? Saved;

    private Brush Text => (Brush)FindResource("DmText");
    private Brush TextDim => (Brush)FindResource("DmTextDim");

    public SettingsWindow(Settings settings, SettingsStore store, UpdaterService updater)
    {
        InitializeComponent();
        DMShot.Platform.DarkTitleBar.Apply(this);
        _settings = settings; _store = store; _updater = updater;
        _updater.StateChanged += OnUpdaterStateChanged;
        Closed += (_, _) => _updater.StateChanged -= OnUpdaterStateChanged;
        ShowGeneral();
    }

    private void OnUpdaterStateChanged()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(OnUpdaterStateChanged); return; }
        if (Nav.SelectedIndex == 3) ShowUpdates();
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

        Pane.Children.Add(new TextBlock
        {
            Text = "After capture", Foreground = Text, FontSize = 14,
            Margin = new Thickness(0, 18, 0, 4)
        });
        var afterCombo = new ComboBox { Width = 240, HorizontalAlignment = HorizontalAlignment.Left };
        afterCombo.Items.Add("Open main window");
        afterCombo.Items.Add("Show Quick-Edit overlay");
        afterCombo.SelectedIndex = _settings.AfterCapture == AfterCaptureMode.QuickEdit ? 1 : 0;
        afterCombo.SelectionChanged += (_, _) =>
        {
            _settings.AfterCapture = afterCombo.SelectedIndex == 1 ? AfterCaptureMode.QuickEdit : AfterCaptureMode.MainWindow;
            Commit();
        };
        Pane.Children.Add(afterCombo);
    }

    private void ShowShortcuts()
    {
        if (Pane is null) return;
        Pane.Children.Clear();
        Pane.Children.Add(SectionTitle("Shortcuts"));
        Pane.Children.Add(Row("Full screen", _settings.FullScreenHotkey, h => { _settings.FullScreenHotkey = h; Commit(); }));
        Pane.Children.Add(Row("Area selection", _settings.AreaHotkey, h => { _settings.AreaHotkey = h; Commit(); }));
        Pane.Children.Add(Row("Record full screen", _settings.VideoFullHotkey, h => { _settings.VideoFullHotkey = h; Commit(); }));
        Pane.Children.Add(Row("Record area", _settings.VideoAreaHotkey, h => { _settings.VideoAreaHotkey = h; Commit(); }));
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
            Text = current, Width = 220,
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

        // Installed-version row.
        var row = new Grid { Margin = new Thickness(0, 0, 0, 18) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel();
        left.Children.Add(new TextBlock { Text = "Version", Foreground = Text, FontSize = 15 });
        left.Children.Add(new TextBlock { Text = "Installed version.", Foreground = TextDim });
        Grid.SetColumn(left, 0);
        string verText = !string.IsNullOrEmpty(_updater.CurrentVersion) ? _updater.CurrentVersion : AssemblyVersionText();
        var ver = new TextBlock { Text = verText, Foreground = Text, FontSize = 15, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(ver, 1);
        row.Children.Add(left); row.Children.Add(ver);
        Pane.Children.Add(row);

        var st = _updater.State;
        switch (st.Status)
        {
            case UpdateStatus.Disabled:
                Pane.Children.Add(Dim("Automatic updates work in the installed version of DM_Screenshot."));
                break;
            case UpdateStatus.Checking:
                Pane.Children.Add(Info("Checking for updates…"));
                break;
            case UpdateStatus.UpToDate:
                Pane.Children.Add(Info("You’re up to date."));
                Pane.Children.Add(AccentButtonControl("Check for Updates", async (_, _) => await _updater.CheckAsync()));
                break;
            case UpdateStatus.Available:
                Pane.Children.Add(new TextBlock { Text = $"Version {st.Version} is available.", Foreground = Text, FontSize = 15, Margin = new Thickness(0, 0, 0, 10) });
                AddReleaseNotes(st.Notes);
                Pane.Children.Add(AccentButtonControl("Download & Install", async (_, _) => await _updater.DownloadAsync()));
                Pane.Children.Add(LinkText("Later", () => _updater.Dismiss()));
                break;
            case UpdateStatus.Downloading:
                Pane.Children.Add(Info($"Downloading… {st.Percent}%"));
                Pane.Children.Add(new ProgressBar
                {
                    Minimum = 0, Maximum = 100, Value = st.Percent, Height = 6, Width = 280,
                    HorizontalAlignment = HorizontalAlignment.Left, BorderThickness = new Thickness(0),
                    Foreground = (Brush)FindResource("DmAccent"), Background = (Brush)FindResource("DmSurfaceLight"),
                    Margin = new Thickness(0, 8, 0, 0)
                });
                break;
            case UpdateStatus.ReadyToInstall:
                Pane.Children.Add(Info($"Version {st.Version} is ready to install."));
                Pane.Children.Add(AccentButtonControl("Restart & Install", (_, _) => _updater.Relaunch()));
                break;
            case UpdateStatus.Error:
                Pane.Children.Add(new TextBlock { Text = st.Message, Foreground = (Brush)FindResource("DmAccent"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
                Pane.Children.Add(AccentButtonControl("Try Again", async (_, _) => await _updater.CheckAsync()));
                break;
            default: // Idle
                Pane.Children.Add(AccentButtonControl("Check for Updates", async (_, _) => await _updater.CheckAsync()));
                break;
        }
    }

    private static string AssemblyVersionText()
    {
        var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        return v is null ? "" : $"{v.Major}.{v.Minor}.{v.Build}";
    }

    private TextBlock Dim(string t) => new() { Text = t, Foreground = TextDim, TextWrapping = TextWrapping.Wrap };
    private TextBlock Info(string t) => new() { Text = t, Foreground = Text, FontSize = 14, Margin = new Thickness(0, 0, 0, 6) };

    private Button AccentButtonControl(string content, RoutedEventHandler onClick)
    {
        var b = new Button
        {
            Content = content, Style = (Style)FindResource("AccentButton"),
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0)
        };
        b.Click += onClick;
        return b;
    }

    private FrameworkElement LinkText(string t, Action onClick)
    {
        var tb = new TextBlock
        {
            Text = t, Foreground = TextDim, Cursor = Cursors.Hand,
            TextDecorations = TextDecorations.Underline, Margin = new Thickness(2, 12, 0, 0)
        };
        tb.MouseLeftButtonUp += (_, _) => onClick();
        return tb;
    }

    private void AddReleaseNotes(IReadOnlyList<ChangelogVersion>? notes)
    {
        if (notes is null) return;
        foreach (var v in notes)
        {
            Pane.Children.Add(new TextBlock { Text = $"What’s new in {v.Version}", Foreground = Text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 4) });
            foreach (var entry in v.Entries)
                Pane.Children.Add(new TextBlock { Text = "•  " + entry.Text, Foreground = TextDim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 0, 0, 3) });
        }
    }

    private void Commit() { _store.Save(_settings); Saved?.Invoke(_settings); }
}
