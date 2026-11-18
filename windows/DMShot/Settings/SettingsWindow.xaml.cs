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
    /// <summary>Raised after "Reset all settings" (following its Saved), for state that lives
    /// outside settings.json's consumers — e.g. the open editor's stroke/blur sliders.</summary>
    public event Action? SettingsReset;

    /// <summary>Queried per hotkey id (App.HK_*) to show the "in use by the system"
    /// row error — RegisterHotKey failures are re-evaluated on every Saved.</summary>
    public Func<int, bool>? IsHotkeyRegistrationFailed { get; init; }

    private Brush Text => (Brush)FindResource("DmText");
    private Brush TextDim => (Brush)FindResource("DmTextDim");
    private Brush ErrorBrush => (Brush)FindResource("DmError");

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

    // Nav order: General, Capture, Saving, Shortcuts, Language, Updates —
    // must match the ListBoxItems in SettingsWindow.xaml.
    private const int NavGeneral = 0, NavCapture = 1, NavSaving = 2, NavShortcuts = 3, NavLanguage = 4, NavUpdates = 5;

    private void OnUpdaterStateChanged()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(OnUpdaterStateChanged); return; }
        if (Nav.SelectedIndex == NavUpdates) ShowUpdates();
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
            case NavGeneral: ShowGeneral(); break;
            case NavCapture: ShowCapture(); break;
            case NavSaving: ShowSaving(); break;
            case NavShortcuts: ShowShortcuts(); break;
            case NavLanguage: ShowLanguage(); break;
            case NavUpdates: ShowUpdates(); break;
        }
    }

    /// <summary>Starts a pane: the title goes into <c>PaneTitle</c> above the grouped
    /// surface (macOS parity), the rows into the cleared <c>Pane</c> inside it.</summary>
    private void BeginSection(string title)
    {
        PaneTitle.Text = title;
        Pane.Children.Clear();
    }

    private void ShowGeneral()
    {
        if (Pane is null) return;
        BeginSection(Loc.Instance["sectionGeneral"]);

        Pane.Children.Add(SettingRow(
            Loc.Instance["launchAtLogin"],
            Loc.Instance["launchAtLoginHelp"],
            SwitchToggle(LaunchAtLogin.Get(), enabled =>
            {
                // HKCU\...\Run is writable for a normal user but can be denied by group
                // policy on managed machines — tell the user the toggle did not take
                // rather than taking the app down from a settings switch.
                if (!Platform.Alerts.Guard(() => LaunchAtLogin.Set(enabled))) return;
                _settings.LaunchAtLogin = enabled;
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

        Pane.Children.Add(SettingRow(
            Loc.Instance["historyLimit"],
            Loc.Instance["historyLimitHelp"],
            HistoryLimitEditor()));

        var resetAll = new Button
        {
            Content = Loc.Instance["resetAllButton"],
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        resetAll.Click += (_, _) => ResetAllSettings();
        Pane.Children.Add(SettingRow(
            Loc.Instance["resetAllSettings"],
            Loc.Instance["resetAllSettingsHelp"],
            resetAll));
    }

    private void ShowSaving()
    {
        if (Pane is null) return;
        BeginSection(Loc.Instance["sectionSaving"]);

        Pane.Children.Add(SaveFolderRow());

        Pane.Children.Add(SettingRow(
            Loc.Instance["autoSave"],
            Loc.Instance["autoSaveHelp"],
            SwitchToggle(_settings.AutoSaveCaptures, enabled =>
            {
                _settings.AutoSaveCaptures = enabled;
                Commit();
            })));

        Pane.Children.Add(SettingRow(
            Loc.Instance["imageFormat"],
            Loc.Instance["imageFormatHelp"],
            Picker(
                new[]
                {
                    (Loc.Instance["formatPng"], SaveImageFormat.Png),
                    (Loc.Instance["formatJpeg"], SaveImageFormat.Jpeg)
                },
                _settings.SaveFormat,
                format =>
                {
                    if (_settings.SaveFormat == format) return;
                    _settings.SaveFormat = format;
                    Commit();
                })));
    }

    /// <summary>"Reset all settings" after a confirmation. Every preference returns to its
    /// default (see <see cref="Settings.ResetToDefaults"/> — history and its limit stay);
    /// launch-at-login is actually removed from the registry. Applied live: Commit → App
    /// re-applies theme and hotkeys, the language switch rebuilds every localized surface.</summary>
    private void ResetAllSettings()
    {
        if (MessageBox.Show(this, Loc.Instance["resetAllConfirmMessage"], Loc.Instance["resetAllConfirmTitle"],
                MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel) != MessageBoxResult.OK)
            return;

        // Same guard as the toggle: group policy can deny HKCU\...\Run. If the entry stays,
        // the setting keeps saying so instead of pretending it is off.
        bool unregistered = Platform.Alerts.Guard(() => LaunchAtLogin.Set(false));
        _settings.ResetToDefaults();
        if (!unregistered) _settings.LaunchAtLogin = true;
        SaveLocation.SessionFolder = null;
        _shortcutErrors.Clear();
        Commit();
        SettingsReset?.Invoke();
        Loc.Instance.Current = LanguageCodes.FromCode(_settings.Language);   // rebuilds the pane if it changed
        ShowGeneral();
    }

    /// <summary>Settings → Capture (mac parity): self-timer, pointer in screenshots / recordings.</summary>
    private void ShowCapture()
    {
        if (Pane is null) return;
        BeginSection(Loc.Instance["sectionCapture"]);

        Pane.Children.Add(SettingRow(
            Loc.Instance["captureDelay"],
            Loc.Instance["captureDelayHelp"],
            Picker<int>(
                global::DMShot.Capture.CaptureDelay.Choices
                    .Select(seconds => (seconds == 0
                        ? Loc.Instance["captureDelayOff"]
                        : string.Format(Loc.Instance["captureDelaySeconds"], seconds), seconds))
                    .ToList(),
                global::DMShot.Capture.CaptureDelay.Normalize(_settings.CaptureDelaySeconds),
                seconds =>
                {
                    if (_settings.CaptureDelaySeconds == seconds) return;
                    _settings.CaptureDelaySeconds = seconds;
                    Commit();
                })));

        Pane.Children.Add(SettingRow(
            Loc.Instance["cursorInScreenshots"],
            Loc.Instance["cursorInScreenshotsHelp"],
            SwitchToggle(_settings.CursorInScreenshots, enabled =>
            {
                _settings.CursorInScreenshots = enabled;
                Commit();
            })));

        Pane.Children.Add(SettingRow(
            Loc.Instance["cursorInRecordings"],
            Loc.Instance["cursorInRecordingsHelp"],
            SwitchToggle(_settings.CursorInRecordings, enabled =>
            {
                _settings.CursorInRecordings = enabled;
                Commit();
            })));
    }

    /// <summary>Zahlenfeld plus Häkchen "unbegrenzt" in einer Zeile. Die Zahl bleibt beim Umschalten stehen,
    /// damit sie beim Zurückschalten wieder da ist.</summary>
    private FrameworkElement HistoryLimitEditor()
    {
        var box = new TextBox
        {
            Width = 80,
            MaxLength = 3,
            TextAlignment = TextAlignment.Right,
            Text = _settings.HistoryLimit.ToString(System.Globalization.CultureInfo.CurrentCulture),
            IsEnabled = !_settings.HistoryUnlimited,
            VerticalAlignment = VerticalAlignment.Center,
        };
        System.Windows.Automation.AutomationProperties.SetName(box, Loc.Instance["historyLimit"]);

        // Nur Ziffern annehmen — sonst müsste jede Eingabe hinterher repariert werden.
        box.PreviewTextInput += (_, e) => e.Handled = !e.Text.All(char.IsAsciiDigit);
        DataObject.AddPastingHandler(box, (_, e) =>
        {
            var text = e.DataObject.GetData(typeof(string)) as string;
            if (text is null || !text.All(char.IsAsciiDigit)) e.CancelCommand();
        });

        void CommitBox()
        {
            int value = int.TryParse(box.Text, out var parsed) ? HistoryLimit.Clamp(parsed) : _settings.HistoryLimit;
            box.Text = value.ToString(System.Globalization.CultureInfo.CurrentCulture);
            if (_settings.HistoryLimit == value) return;
            _settings.HistoryLimit = value;
            Commit();
        }
        _ = new SettingsInputCommit(box, CommitBox);
        box.LostFocus += (_, _) => CommitBox();
        box.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            CommitBox();
            e.Handled = true;
        };

        var unlimited = new CheckBox
        {
            Content = Loc.Instance["historyUnlimited"],
            IsChecked = _settings.HistoryUnlimited,
            Foreground = Text,
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        void SetUnlimited(bool on)
        {
            box.IsEnabled = !on;
            if (_settings.HistoryUnlimited == on) return;
            _settings.HistoryUnlimited = on;
            Commit();
        }
        unlimited.Checked += (_, _) => SetUnlimited(true);
        unlimited.Unchecked += (_, _) => SetUnlimited(false);

        // One line, right-aligned as a unit: [ 10 ]  [x] Unlimited
        var stack = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        stack.Children.Add(box);
        stack.Children.Add(unlimited);
        return stack;
    }

    /// <summary>Zeigt den eingestellten Zielordner (oder den Ersatz Bilder\Screenshots, den
    /// automatisches Speichern dann nutzt) und lässt einen anderen wählen bzw. ihn öffnen.
    /// Der Pfad steht als gedimmte Zeile unter dem Hilfetext; rechts die beiden Buttons.</summary>
    private FrameworkElement SaveFolderRow()
    {
        var path = new TextBlock
        {
            Foreground = TextDim,
            FontSize = 12,
            Margin = new Thickness(0, 4, 18, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var pick = new Button
        {
            Content = Loc.Instance["chooseFolder"],
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var open = new Button
        {
            Content = Loc.Instance["openFolder"],
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 6, 0, 0),
        };

        void Refresh()
        {
            var folder = SaveLocation.DefaultFolder(_settings.DefaultSaveFolder);
            path.Text = SaveLocation.Configured(_settings.DefaultSaveFolder)
                ?? string.Format(Loc.Instance["folderNotSetFallback"], folder);
            path.ToolTip = folder;
            open.IsEnabled = System.IO.Directory.Exists(folder);
        }
        Refresh();

        pick.Click += (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = Loc.Instance["batchSaveFolderTitle"],
                InitialDirectory = SaveLocation.Configured(_settings.DefaultSaveFolder) ?? SaveLocation.Fallback(),
            };
            if (dlg.ShowDialog(this) != true) return;
            _settings.DefaultSaveFolder = dlg.FolderName;
            SaveLocation.SessionFolder = null;   // neuer Standard gilt sofort
            Refresh();
            Commit();
        };
        open.Click += (_, _) =>
        {
            var folder = SaveLocation.DefaultFolder(_settings.DefaultSaveFolder);
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"open folder failed: {ex}"); }
            Refresh();   // removed meanwhile → button turns off
        };

        var buttons = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(pick);
        buttons.Children.Add(open);

        return SettingRow(
            Loc.Instance["defaultSaveFolder"],
            Loc.Instance["defaultSaveFolderHelp"],
            buttons,
            detail: path);
    }

    // Per-row validation errors (needs-modifier / duplicate), keyed by hotkey id —
    // mirrors mac's lastError map next to the registration failure (systemInUse).
    private readonly Dictionary<int, string> _shortcutErrors = new();

    private (int Id, string Title, Func<string> Get, Action<string> Set)[] ShortcutRows() => new (int, string, Func<string>, Action<string>)[]
    {
        (App.HK_FULL, Loc.Instance["actionFullScreen"], () => _settings.FullScreenHotkey, v => _settings.FullScreenHotkey = v),
        (App.HK_AREA, Loc.Instance["actionAreaSelection"], () => _settings.AreaHotkey, v => _settings.AreaHotkey = v),
        (App.HK_REPEAT_AREA, Loc.Instance["actionRepeatArea"], () => _settings.RepeatAreaHotkey, v => _settings.RepeatAreaHotkey = v),
        (App.HK_VIDEO_FULL, Loc.Instance["actionVideoFull"], () => _settings.VideoFullHotkey, v => _settings.VideoFullHotkey = v),
        (App.HK_VIDEO_AREA, Loc.Instance["actionVideoArea"], () => _settings.VideoAreaHotkey, v => _settings.VideoAreaHotkey = v),
        (App.HK_CAPTURE_TEXT, Loc.Instance["actionCaptureText"], () => _settings.CaptureTextHotkey, v => _settings.CaptureTextHotkey = v),
    };

    private void ShowShortcuts()
    {
        if (Pane is null) return;
        BeginSection(Loc.Instance["sectionShortcuts"]);
        foreach (var row in ShortcutRows())
            Pane.Children.Add(Row(row.Title, row.Get(), row.Id, h => SetHotkey(row.Id, h)));

        var reset = new Button
        {
            Content = Loc.Instance["resetToDefaults"],
            HorizontalAlignment = HorizontalAlignment.Left
        };
        reset.Click += (_, _) =>
        {
            var d = new Settings();
            _settings.FullScreenHotkey = d.FullScreenHotkey;
            _settings.AreaHotkey = d.AreaHotkey;
            _settings.RepeatAreaHotkey = d.RepeatAreaHotkey;
            _settings.VideoFullHotkey = d.VideoFullHotkey;
            _settings.VideoAreaHotkey = d.VideoAreaHotkey;
            _settings.CaptureTextHotkey = d.CaptureTextHotkey;
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

    /// <summary>One shortcut row on the same two-column grid as <see cref="SettingRow"/>:
    /// label left, 220 px recorder right, the row error (if any) under the label.</summary>
    private FrameworkElement Row(string label, string current, int hotkeyId, Action<string> onSet)
    {
        var rec = new ShortcutRecorderControl(current)
        {
            Width = 220,
            Style = (Style)FindResource("DmTextBox"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(18, 0, 0, 0),
        };
        rec.HotkeyChanged += onSet;

        var row = RowGrid();
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = RowTitle(label);
        title.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(title, 0);
        Grid.SetColumn(rec, 1);
        row.Children.Add(title);
        row.Children.Add(rec);

        // Row error, mac precedence: OS registration failure first, then validation.
        string? error = IsHotkeyRegistrationFailed?.Invoke(hotkeyId) == true
            ? Loc.Instance["systemInUse"]
            : _shortcutErrors.TryGetValue(hotkeyId, out var msg) ? msg : null;
        if (error is not null)
        {
            var errorText = new TextBlock
            {
                Text = error,
                Foreground = ErrorBrush,
                FontSize = 12, Margin = new Thickness(0, 4, 18, 0), TextWrapping = TextWrapping.Wrap
            };
            Grid.SetRow(errorText, 1);
            Grid.SetColumn(errorText, 0);
            row.Children.Add(errorText);
        }
        return row;
    }

    private void ShowLanguage()
    {
        if (Pane is null) return;
        BeginSection(Loc.Instance["sectionLanguage"]);

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
        BeginSection(Loc.Instance["sectionUpdates"]);

        // Installed-version row — same look as the General rows.
        string verText = !string.IsNullOrEmpty(_updater.CurrentVersion) ? _updater.CurrentVersion : AssemblyVersionText();
        var ver = new TextBlock { Text = verText, Foreground = Text, FontSize = 14 };
        Pane.Children.Add(SettingRow(Loc.Instance["version"], Loc.Instance["versionHelp"], ver));

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
                Pane.Children.Add(new TextBlock { Text = string.Format(Loc.Instance["updateAvailable"], st.Version), Foreground = Text, FontSize = 14, Margin = new Thickness(0, 0, 0, 10) });
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
                Pane.Children.Add(new TextBlock { Text = st.Message, Foreground = ErrorBrush, FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
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

    private TextBlock Dim(string t) => new() { Text = t, Foreground = TextDim, FontSize = 14, TextWrapping = TextWrapping.Wrap };
    private TextBlock Info(string t) => new() { Text = t, Foreground = Text, FontSize = 14, Margin = new Thickness(0, 0, 0, 6) };

    /// <summary>Two-column row grid shared by every settings row: a star label column and
    /// an Auto column for the control.</summary>
    private static Grid RowGrid()
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 18) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        return row;
    }

    private TextBlock RowTitle(string title) => new()
    {
        Text = title,
        Foreground = Text,
        FontSize = 14,
        FontWeight = FontWeights.SemiBold,
        TextWrapping = TextWrapping.Wrap
    };

    /// <param name="detail">Optional extra line under the help text in the label column
    /// (e.g. the chosen save folder).</param>
    private FrameworkElement SettingRow(string title, string subtitle, FrameworkElement trailing, FrameworkElement? detail = null)
    {
        var row = RowGrid();

        // Left-aligned: a capped StackPanel in a star column otherwise centres itself, so the
        // labels drifted right whenever the column was wider than MaxWidth.
        var labels = new StackPanel { MaxWidth = 380, HorizontalAlignment = HorizontalAlignment.Left };
        labels.Children.Add(RowTitle(title));
        labels.Children.Add(new TextBlock
        {
            Text = subtitle,
            Foreground = TextDim,
            FontSize = 12,
            Margin = new Thickness(0, 3, 18, 0),
            TextWrapping = TextWrapping.Wrap
        });
        if (detail is not null) labels.Children.Add(detail);
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
            (Loc.Instance["designGraphiteSand"], AppDesign.GraphiteSand),
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

    // Persisting is best-effort (as in App.OnExit): every settings control calls this, so
    // an unwritable settings.json — roaming AppData offline, full disk — must cost the
    // NEXT launch's restore, not the running session. The change is applied either way.
    private void Commit()
    {
        try { _store.Save(_settings); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"settings save failed: {ex}"); }
        Saved?.Invoke(_settings);
    }
}
