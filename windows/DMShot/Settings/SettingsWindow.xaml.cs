using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DMShot.Localization;
using DMShot.Update;
// `Window.Language` (XmlLanguage) shadows the DMShot.Localization.Language enum in
// instance context, so reference the enum through this alias.
using LocLanguage = DMShot.Localization.Language;
namespace DMShot.Settings;

public partial class SettingsWindow : Window
{
    private readonly Settings _settings;
    private readonly SettingsStore _store;
    private readonly UpdaterService _updater;
    public event Action<Settings>? Saved;

    /// <summary>Queried per hotkey id (App.HK_*) to show the "in use by the system"
    /// row error — RegisterHotKey failures are re-evaluated on every Saved.</summary>
    public Func<int, bool>? IsHotkeyRegistrationFailed { get; init; }

    private Brush Text => (Brush)FindResource("DmText");
    private Brush TextDim => (Brush)FindResource("DmTextDim");

    public SettingsWindow(Settings settings, SettingsStore store, UpdaterService updater)
    {
        InitializeComponent();
        DMShot.Platform.DarkTitleBar.Apply(this);
        _settings = settings; _store = store; _updater = updater;
        _updater.StateChanged += OnUpdaterStateChanged;
        Loc.Instance.LanguageChanged += OnLanguageChanged;
        Closed += (_, _) =>
        {
            _updater.StateChanged -= OnUpdaterStateChanged;
            Loc.Instance.LanguageChanged -= OnLanguageChanged;
        };
        ShowGeneral();
    }

    private void OnUpdaterStateChanged()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(OnUpdaterStateChanged); return; }
        if (Nav.SelectedIndex == 3) ShowUpdates();
    }

    private void OnLanguageChanged()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(OnLanguageChanged); return; }
        // {loc:Tr}-bound nav labels + Title update automatically; rebuild the
        // imperatively-built content pane for the current section.
        NavChanged(Nav, null!);
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
        Pane.Children.Add(SectionTitle(Loc.Instance["sectionGeneral"]));

        Pane.Children.Add(SettingRow(
            Loc.Instance["launchAtLogin"],
            Loc.Instance["launchAtLoginHelp"],
            SwitchToggle(LaunchAtLogin.Get(), enabled =>
            {
                _settings.LaunchAtLogin = enabled;
                LaunchAtLogin.Set(enabled);
                Commit();
            })));

        Pane.Children.Add(SettingRow(
            Loc.Instance["afterCapture"],
            Loc.Instance["afterCaptureHelp"],
            AfterCapturePicker()));

        Pane.Children.Add(SettingRow(
            Loc.Instance["design"],
            Loc.Instance["designHelp"],
            ShowDesignPicker()));

        Pane.Children.Add(SettingRow(
            Loc.Instance["showLoupe"],
            Loc.Instance["showLoupeHelp"],
            SwitchToggle(_settings.ShowZoomLoupe, enabled =>
            {
                _settings.ShowZoomLoupe = enabled;
                Commit();
            })));
    }

    // Per-row validation errors (needs-modifier / duplicate), keyed by hotkey id —
    // mirrors mac's lastError map next to the registration failure (systemInUse).
    private readonly Dictionary<int, string> _shortcutErrors = new();

    private (int Id, string Title, Func<string> Get, Action<string> Set)[] ShortcutRows() => new (int, string, Func<string>, Action<string>)[]
    {
        (App.HK_FULL, Loc.Instance["actionFullScreen"], () => _settings.FullScreenHotkey, v => _settings.FullScreenHotkey = v),
        (App.HK_AREA, Loc.Instance["actionAreaSelection"], () => _settings.AreaHotkey, v => _settings.AreaHotkey = v),
        (App.HK_VIDEO_FULL, Loc.Instance["actionVideoFull"], () => _settings.VideoFullHotkey, v => _settings.VideoFullHotkey = v),
        (App.HK_VIDEO_AREA, Loc.Instance["actionVideoArea"], () => _settings.VideoAreaHotkey, v => _settings.VideoAreaHotkey = v),
    };

    private void ShowShortcuts()
    {
        if (Pane is null) return;
        Pane.Children.Clear();
        Pane.Children.Add(SectionTitle(Loc.Instance["sectionShortcuts"]));
        foreach (var row in ShortcutRows())
            Pane.Children.Add(Row(row.Title, row.Get(), row.Id, h => SetHotkey(row.Id, h)));

        var reset = new Button
        {
            Content = Loc.Instance["resetToDefaults"],
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 14, 0, 0)
        };
        reset.Click += (_, _) =>
        {
            var d = new Settings();
            _settings.FullScreenHotkey = d.FullScreenHotkey;
            _settings.AreaHotkey = d.AreaHotkey;
            _settings.VideoFullHotkey = d.VideoFullHotkey;
            _settings.VideoAreaHotkey = d.VideoAreaHotkey;
            _shortcutErrors.Clear();
            Commit();
            ShowShortcuts();
        };
        Pane.Children.Add(reset);

        Pane.Children.Add(new TextBlock
        {
            Text = Loc.Instance["shortcutsHint"],
            Foreground = TextDim, Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap
        });
    }

    /// <summary>Validate (modifier required, no duplicates — mac parity) and save a recorded
    /// combo. Commit re-registers the hotkeys (App handles Saved synchronously), so the
    /// rebuild right after also shows/clears the RegisterHotKey systemInUse row error.</summary>
    private void SetHotkey(int id, string combo)
    {
        _shortcutErrors.Remove(id);
        if (!Platform.HotkeySpec.TryParse(combo, out var spec) || spec.Modifiers == Platform.HotkeyModifiers.None)
        {
            _shortcutErrors[id] = Loc.Instance["needsModifier"];
            ShowShortcuts();
            return;
        }
        var conflict = ShortcutRows().FirstOrDefault(r => r.Id != id && r.Get() == combo);
        if (conflict.Title is not null)
        {
            _shortcutErrors[id] = string.Format(Loc.Instance["alreadyUsedBy"], conflict.Title);
            ShowShortcuts();
            return;
        }
        foreach (var row in ShortcutRows())
            if (row.Id == id) row.Set(combo);
        Commit();
        ShowShortcuts();
    }

    private FrameworkElement Row(string label, string current, int hotkeyId, Action<string> onSet)
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

        // Row error, mac precedence: OS registration failure first, then validation.
        string? error = IsHotkeyRegistrationFailed?.Invoke(hotkeyId) == true
            ? Loc.Instance["systemInUse"]
            : _shortcutErrors.TryGetValue(hotkeyId, out var msg) ? msg : null;
        if (error is null) return sp;

        var panel = new StackPanel();
        panel.Children.Add(sp);
        panel.Children.Add(new TextBlock
        {
            Text = error,
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x8A)),   // mac's error tint
            FontSize = 12, Margin = new Thickness(150, 0, 0, 4), TextWrapping = TextWrapping.Wrap
        });
        return panel;
    }

    private void ShowLanguage()
    {
        if (Pane is null) return;
        Pane.Children.Clear();
        Pane.Children.Add(SectionTitle(Loc.Instance["sectionLanguage"]));

        var combo = new ComboBox { Width = 220, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var lang in new[] { LocLanguage.English, LocLanguage.German })
            combo.Items.Add(new ComboBoxItem { Content = lang.DisplayName(), Tag = lang });
        var current = LanguageCodes.FromCode(_settings.Language);
        combo.SelectedItem = combo.Items.Cast<ComboBoxItem>().First(i => (LocLanguage)i.Tag! == current);
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem item && item.Tag is LocLanguage lang)
            {
                _settings.Language = lang.Code();
                Commit();
                Loc.Instance.Current = lang;
            }
        };
        Pane.Children.Add(SettingRow(Loc.Instance["languageLabel"], Loc.Instance["languageHelp"], combo));
    }

    private void ShowUpdates()
    {
        if (Pane is null) return;
        Pane.Children.Clear();
        Pane.Children.Add(SectionTitle(Loc.Instance["sectionUpdates"]));

        // Installed-version row.
        var row = new Grid { Margin = new Thickness(0, 0, 0, 18) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel();
        left.Children.Add(new TextBlock { Text = Loc.Instance["version"], Foreground = Text, FontSize = 15 });
        left.Children.Add(new TextBlock { Text = Loc.Instance["versionHelp"], Foreground = TextDim });
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
                Pane.Children.Add(Dim(Loc.Instance["updatesDisabled"]));
                break;
            case UpdateStatus.Checking:
                Pane.Children.Add(Info(Loc.Instance["checkingForUpdates"]));
                break;
            case UpdateStatus.UpToDate:
                Pane.Children.Add(Info(Loc.Instance["upToDate"]));
                Pane.Children.Add(AccentButtonControl(Loc.Instance["checkForUpdates"], async (_, _) => await _updater.CheckAsync()));
                break;
            case UpdateStatus.Available:
                Pane.Children.Add(new TextBlock { Text = string.Format(Loc.Instance["updateAvailable"], st.Version), Foreground = Text, FontSize = 15, Margin = new Thickness(0, 0, 0, 10) });
                AddReleaseNotes(st.Notes);
                Pane.Children.Add(AccentButtonControl(Loc.Instance["downloadInstall"], async (_, _) => await _updater.DownloadAsync()));
                Pane.Children.Add(LinkText(Loc.Instance["later"], () => _updater.Dismiss()));
                break;
            case UpdateStatus.Downloading:
                Pane.Children.Add(Info(string.Format(Loc.Instance["downloading"], st.Percent)));
                Pane.Children.Add(new ProgressBar
                {
                    Minimum = 0, Maximum = 100, Value = st.Percent, Height = 6, Width = 280,
                    HorizontalAlignment = HorizontalAlignment.Left, BorderThickness = new Thickness(0),
                    Foreground = (Brush)FindResource("DmAccent"), Background = (Brush)FindResource("DmSurfaceLight"),
                    Margin = new Thickness(0, 8, 0, 0)
                });
                break;
            case UpdateStatus.ReadyToInstall:
                Pane.Children.Add(Info(string.Format(Loc.Instance["readyToInstall"], st.Version)));
                Pane.Children.Add(AccentButtonControl(Loc.Instance["restartInstall"], (_, _) => _updater.Relaunch()));
                break;
            case UpdateStatus.Error:
                Pane.Children.Add(new TextBlock { Text = st.Message, Foreground = (Brush)FindResource("DmAccent"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
                Pane.Children.Add(AccentButtonControl(Loc.Instance["tryAgain"], async (_, _) => await _updater.CheckAsync()));
                break;
            default: // Idle
                Pane.Children.Add(AccentButtonControl(Loc.Instance["checkForUpdates"], async (_, _) => await _updater.CheckAsync()));
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

    private FrameworkElement SettingRow(string title, string subtitle, FrameworkElement trailing)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 18) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var labels = new StackPanel { MaxWidth = 330 };
        labels.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = Text,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold
        });
        labels.Children.Add(new TextBlock
        {
            Text = subtitle,
            Foreground = TextDim,
            FontSize = 12,
            Margin = new Thickness(0, 3, 18, 0),
            TextWrapping = TextWrapping.Wrap
        });
        Grid.SetColumn(labels, 0);

        trailing.VerticalAlignment = VerticalAlignment.Top;
        trailing.Margin = new Thickness(18, 0, 0, 0);
        Grid.SetColumn(trailing, 1);

        row.Children.Add(labels);
        row.Children.Add(trailing);
        return row;
    }

    private ToggleButton SwitchToggle(bool isChecked, Action<bool> onChanged)
    {
        var toggle = new ToggleButton
        {
            IsChecked = isChecked,
            Style = (Style)FindResource("SwitchToggle")
        };
        toggle.Checked += (_, _) => onChanged(true);
        toggle.Unchecked += (_, _) => onChanged(false);
        return toggle;
    }

    private ComboBox AfterCapturePicker() => Picker(
        new[]
        {
            (Loc.Instance["afterCaptureMainWindow"], AfterCaptureMode.MainWindow),
            (Loc.Instance["afterCaptureQuickEdit"], AfterCaptureMode.QuickEdit)
        },
        _settings.AfterCapture,
        mode =>
        {
            if (_settings.AfterCapture == mode) return;
            _settings.AfterCapture = mode;
            Commit();
        });

    private ComboBox ShowDesignPicker() => Picker(
        new[]
        {
            (Loc.Instance["designStandard"], AppDesign.Standard),
            (Loc.Instance["designBlack"], AppDesign.Black)
        },
        _settings.AppDesign,
        design =>
        {
            if (_settings.AppDesign == design) return;
            _settings.AppDesign = design;
            Commit();
        });

    private ComboBox Picker<T>(IReadOnlyList<(string Title, T Value)> items, T selected, Action<T> onChanged)
        where T : notnull
    {
        var combo = new ComboBox { Width = 220, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var item in items)
            combo.Items.Add(new ComboBoxItem { Content = item.Title, Tag = item.Value });

        var selectedItem = combo.Items
            .Cast<ComboBoxItem>()
            .FirstOrDefault(i => i.Tag is T value && EqualityComparer<T>.Default.Equals(value, selected));
        combo.SelectedItem = selectedItem ?? combo.Items[0];
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem item && item.Tag is T value)
                onChanged(value);
        };
        return combo;
    }

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
            Pane.Children.Add(new TextBlock { Text = string.Format(Loc.Instance["whatsNewIn"], v.Version), Foreground = Text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 4) });
            foreach (var entry in v.Entries)
                Pane.Children.Add(new TextBlock { Text = "•  " + entry.Text, Foreground = TextDim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 0, 0, 3) });
        }
    }

    private void Commit() { _store.Save(_settings); Saved?.Invoke(_settings); }
}
