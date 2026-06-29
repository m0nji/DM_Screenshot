using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DMShot.Localization;

namespace DMShot.Editor;

/// <summary>
/// Builds the frame-preset control panel (on/off toggle, padding, corners, fill swatches) as a
/// reusable WPF element. Shared by EditorWindow's toolbar popup and QuickEditOverlayWindow's flyout
/// so the panel code is written once and never duplicated.
///
/// Usage:
///   var panel = FramePanelFactory.Build(model, () => { canvas.InvalidateVisual(); RaiseFrameStyleChanged(); });
///   // then add panel to a StackPanel, Popup content, or flyout.
/// </summary>
public static class FramePanelFactory
{
    // ── Segment button style: rounded pill, accent highlight when selected ──────────────────────
    private static Style? _segStyle;
    private static Style SegStyle => _segStyle ??= ParseStyle(
        @"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                 xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'>
          <Setter Property='Padding' Value='9,3'/>
          <Setter Property='Cursor' Value='Hand'/>
          <Setter Property='FontSize' Value='11'/>
          <Setter Property='BorderThickness' Value='1'/>
          <Setter Property='Margin' Value='0,0,4,0'/>
          <Setter Property='Template'>
            <Setter.Value>
              <ControlTemplate TargetType='Button'>
                <Border x:Name='bd' CornerRadius='5'
                        Background='{TemplateBinding Background}'
                        BorderBrush='{TemplateBinding BorderBrush}'
                        BorderThickness='{TemplateBinding BorderThickness}'
                        Padding='{TemplateBinding Padding}'>
                  <TextBlock Text='{TemplateBinding Content}'
                             Foreground='{TemplateBinding Foreground}'
                             HorizontalAlignment='Center' VerticalAlignment='Center'/>
                </Border>
                <ControlTemplate.Triggers>
                  <Trigger Property='IsMouseOver' Value='True'>
                    <Setter TargetName='bd' Property='Opacity' Value='0.85'/>
                  </Trigger>
                  <Trigger Property='IsPressed' Value='True'>
                    <Setter TargetName='bd' Property='Opacity' Value='0.70'/>
                  </Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
        </Style>");

    // ── Circular swatch button: Tag='sel' activates the accent ring ─────────────────────────────
    private static Style? _swatchStyle;
    private static Style SwatchStyle => _swatchStyle ??= ParseStyle(
        @"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                 xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'>
          <Setter Property='Width' Value='24'/>
          <Setter Property='Height' Value='24'/>
          <Setter Property='Cursor' Value='Hand'/>
          <Setter Property='Margin' Value='0,0,4,0'/>
          <Setter Property='BorderThickness' Value='0'/>
          <Setter Property='Template'>
            <Setter.Value>
              <ControlTemplate TargetType='Button'>
                <Grid>
                  <Ellipse Fill='{TemplateBinding Background}'/>
                  <Ellipse x:Name='ring' Stroke='Transparent' StrokeThickness='2.5'/>
                </Grid>
                <ControlTemplate.Triggers>
                  <DataTrigger Binding='{Binding Tag, RelativeSource={RelativeSource TemplatedParent}}' Value='sel'>
                    <Setter TargetName='ring' Property='Stroke' Value='{DynamicResource DmAccent}'/>
                  </DataTrigger>
                </ControlTemplate.Triggers>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
        </Style>");

    private static Style ParseStyle(string xaml) =>
        (Style)System.Windows.Markup.XamlReader.Parse(xaml);

    // ────────────────────────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Builds and returns the frame-preset panel as a <see cref="FrameworkElement"/> you can add
    /// to any container. The <paramref name="onChanged"/> callback fires after every mutation
    /// (toggle on/off, padding, corner, fill). Wire it to invalidate the canvas and persist the
    /// new style (call <c>Canvas.InvalidateVisual()</c> and <c>RaiseFrameStyleChanged()</c> from
    /// EditorWindow, or the overlay's <c>FrameStyleChanged</c> event in QuickEditOverlayWindow).
    /// </summary>
    public static FrameworkElement Build(EditorModel model, Action onChanged)
    {
        var root = new StackPanel { Width = 240 };

        // ── Enable / disable toggle ────────────────────────────────────────────
        var toggle = new CheckBox
        {
            Content = Loc.Instance["background"],
            IsChecked = model.BackgroundEnabled,
            Margin = new Thickness(0, 0, 0, 10),
            FontSize = 13,
        };

        // ── Sub-panel: dimmed and non-interactive while toggle is off ──────────
        // Use IsHitTestVisible + Opacity (not IsEnabled) so each child doesn't
        // apply its own 0.4 opacity trigger on top of the container's 0.4 — that
        // would give 0.16 total, far too dark.  IsHitTestVisible still blocks all
        // mouse input on the sub-panel when off.
        var sub = new StackPanel();

        void SyncSubPanel()
        {
            bool on = model.BackgroundEnabled;
            sub.IsHitTestVisible = on;
            sub.Opacity = on ? 1.0 : 0.4;
        }

        toggle.Checked += (_, _) =>
        {
            model.BackgroundEnabled = true;
            SyncSubPanel();
            onChanged();
        };
        toggle.Unchecked += (_, _) =>
        {
            model.BackgroundEnabled = false;
            SyncSubPanel();
            onChanged();
        };

        // ── Padding row ────────────────────────────────────────────────────────
        sub.Children.Add(LabelRow(
            Loc.Instance["bgPadding"],
            SegmentedRow(
                new[]
                {
                    (FramePadding.Small,  Loc.Instance["bgPadSmall"]),
                    (FramePadding.Medium, Loc.Instance["bgPadMedium"]),
                    (FramePadding.Large,  Loc.Instance["bgPadLarge"]),
                },
                () => model.FramePadding,
                v => { model.FramePadding = v; onChanged(); })));

        // ── Corners row ────────────────────────────────────────────────────────
        sub.Children.Add(LabelRow(
            Loc.Instance["bgCorners"],
            SegmentedRow(
                new[]
                {
                    (FrameCorner.None,  Loc.Instance["bgCornerNone"]),
                    (FrameCorner.Soft,  Loc.Instance["bgCornerSoft"]),
                    (FrameCorner.Round, Loc.Instance["bgCornerRound"]),
                },
                () => model.FrameCorner,
                v => { model.FrameCorner = v; onChanged(); })));

        // ── Fill row ───────────────────────────────────────────────────────────
        sub.Children.Add(FillRow(model, onChanged));

        SyncSubPanel();     // apply initial state

        root.Children.Add(toggle);
        root.Children.Add(sub);
        return root;
    }

    // ── Private helpers ──────────────────────────────────────────────────────────────────────────

    private static UIElement LabelRow(string label, UIElement content)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        var lbl = new TextBlock { Text = label, FontSize = 11, Margin = new Thickness(0, 0, 0, 4) };
        lbl.SetResourceReference(TextBlock.ForegroundProperty, "DmTextDim");
        panel.Children.Add(lbl);
        panel.Children.Add(content);
        return panel;
    }

    private static UIElement SegmentedRow<T>(
        (T value, string label)[] items,
        Func<T> get,
        Action<T> set)
        where T : struct, Enum
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var buttons = new Button[items.Length];

        for (int i = 0; i < items.Length; i++)
        {
            var (value, label) = items[i];
            var btn = new Button { Style = SegStyle, Content = label };
            buttons[i] = btn;

            var captured = value;
            btn.Click += (_, _) =>
            {
                set(captured);
                ApplySegmentStyles(buttons, items, get());
            };
            row.Children.Add(btn);
        }

        ApplySegmentStyles(buttons, items, get());
        return row;
    }

    private static void ApplySegmentStyles<T>(Button[] buttons, (T value, string label)[] items, T current)
        where T : struct, Enum
    {
        for (int i = 0; i < buttons.Length; i++)
        {
            bool sel = items[i].value.Equals(current);
            buttons[i].SetResourceReference(Control.BackgroundProperty, sel ? "DmAccentTint" : "DmSurfaceAlt");
            buttons[i].SetResourceReference(Control.BorderBrushProperty, sel ? "DmAccent" : "DmBorderControl");
            buttons[i].SetResourceReference(Control.ForegroundProperty,  sel ? "DmTextStrong" : "DmText");
        }
    }

    private static UIElement FillRow(EditorModel model, Action onChanged)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
        var lbl = new TextBlock { Text = Loc.Instance["bgFill"], FontSize = 11, Margin = new Thickness(0, 0, 0, 4) };
        lbl.SetResourceReference(TextBlock.ForegroundProperty, "DmTextDim");
        panel.Children.Add(lbl);

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        var allBtns = new List<Button>();

        // 4 solid colour swatches
        foreach (var hex in FramePresets.SolidColors)
        {
            var h = hex;
            var btn = MakeSwatch(new SolidColorBrush(HexToColor(hex)));
            allBtns.Add(btn);
            btn.Click += (_, _) =>
            {
                model.FrameBackgroundKind = FrameBackgroundKind.Solid;
                model.FrameSolidHex = h;
                SelectSwatch(allBtns, btn);
                onChanged();
            };
            row.Children.Add(btn);
        }

        // 3 gradient swatches
        foreach (var g in new[] { FrameGradient.Warm, FrameGradient.Cool, FrameGradient.Neutral })
        {
            var gradient = g;
            var (startHex, endHex) = FramePresets.GradientStops(g);
            var brush = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new GradientStop(HexToColor(startHex), 0),
                    new GradientStop(HexToColor(endHex), 1),
                },
                startPoint: new Point(0, 0),
                endPoint:   new Point(1, 1));
            var btn = MakeSwatch(brush);
            allBtns.Add(btn);
            btn.Click += (_, _) =>
            {
                model.FrameBackgroundKind = FrameBackgroundKind.Gradient;
                model.FrameGradient = gradient;
                SelectSwatch(allBtns, btn);
                onChanged();
            };
            row.Children.Add(btn);
        }

        // Blur swatch — neutral grey circle, tooltip "Blur"
        var blurBrush = new SolidColorBrush(Color.FromRgb(0x88, 0x99, 0xAA));
        var blurBtn = MakeSwatch(blurBrush);
        blurBtn.ToolTip = Loc.Instance["bgBlur"];
        allBtns.Add(blurBtn);
        blurBtn.Click += (_, _) =>
        {
            model.FrameBackgroundKind = FrameBackgroundKind.Blur;
            SelectSwatch(allBtns, blurBtn);
            onChanged();
        };
        row.Children.Add(blurBtn);

        SetInitialSwatchSelection(allBtns, model);

        panel.Children.Add(row);
        return panel;
    }

    private static Button MakeSwatch(Brush background)
    {
        var btn = new Button { Style = SwatchStyle, Background = background };
        return btn;
    }

    private static void SelectSwatch(List<Button> all, Button selected)
    {
        foreach (var b in all) b.Tag = null;
        selected.Tag = "sel";
    }

    private static void SetInitialSwatchSelection(List<Button> buttons, EditorModel model)
    {
        // buttons order: [4 solids][3 gradients][1 blur]
        var solids    = FramePresets.SolidColors;
        var gradients = new[] { FrameGradient.Warm, FrameGradient.Cool, FrameGradient.Neutral };

        switch (model.FrameBackgroundKind)
        {
            case FrameBackgroundKind.Solid:
                for (int i = 0; i < solids.Length; i++)
                {
                    if (string.Equals(solids[i], model.FrameSolidHex, StringComparison.OrdinalIgnoreCase))
                    { buttons[i].Tag = "sel"; return; }
                }
                break;
            case FrameBackgroundKind.Gradient:
                for (int i = 0; i < gradients.Length; i++)
                {
                    if (gradients[i] == model.FrameGradient)
                    { buttons[solids.Length + i].Tag = "sel"; return; }
                }
                break;
            case FrameBackgroundKind.Blur:
                buttons[^1].Tag = "sel";
                break;
        }
    }

    private static Color HexToColor(string hex)
    {
        hex = hex.TrimStart('#');
        uint v = Convert.ToUInt32(hex, 16);
        if (hex.Length == 6) v |= 0xFF000000u;
        return Color.FromArgb((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }
}
