using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using DMShot.Localization;
using DMShot.Editor;
using Xunit;

[Collection("Localization")]
public class FramePanelFactoryTests
{
    /// <summary>Runs <paramref name="act"/> on a dedicated STA thread (the panel builds WPF
    /// controls / parses inline XAML styles, which want STA; xUnit runs MTA by default).</summary>
    private static void OnSta(Action act)
    {
        Exception? ex = null;
        var t = new Thread(() => { try { act(); } catch (Exception e) { ex = e; } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (ex != null) throw ex;
    }

    // Regression: clicking the Background button builds this panel. The inline ControlTemplate
    // styles use the x: prefix (x:Name) but only declared the default namespace, so XamlReader.Parse
    // threw XamlParseException ("'x' is an undeclared prefix") and crashed the app on first open.
    [Fact]
    public void Build_ParsesInlineStyles_WithoutThrowing() => OnSta(() =>
    {
        var panel = FramePanelFactory.Build(new EditorModel(), () => { });
        Assert.NotNull(panel);
    });
    [Fact]
    public void BackgroundOffDisablesKeyboardAndAutomationControls() => OnSta(() =>
    {
        var model = new EditorModel { BackgroundEnabled = false };
        var panel = (StackPanel)FramePanelFactory.Build(model, () => { });
        var toggle = (CheckBox)panel.Children[0];
        var subpanel = (StackPanel)panel.Children[1];
        Assert.False(subpanel.IsEnabled);
        Assert.All(Descendants(subpanel).OfType<Button>(), button => Assert.False(button.IsEnabled));
        toggle.IsChecked = true;
        Assert.True(subpanel.IsEnabled);
        Assert.All(Descendants(subpanel).OfType<Button>(), button => Assert.True(button.IsEnabled));
    });

    [Fact]
    public void ExistingFramePanelLabelsAndAutomationNamesFollowLanguageChanges() => OnSta(() =>
    {
        var original = Loc.Instance.Current;
        try
        {
            Loc.Instance.Current = Language.English;
            var panel = (StackPanel)FramePanelFactory.Build(new EditorModel(), () => { });
            var toggle = (CheckBox)panel.Children[0];
            var buttons = Descendants(panel).OfType<Button>().ToArray();
            var blur = buttons.Single(button => AutomationProperties.GetName(button) == Loc.En["bgBlur"]);
            Loc.Instance.Current = Language.German;
            Assert.Equal(Loc.De["background"], toggle.Content);
            Assert.Equal(Loc.De["bgBlur"], blur.ToolTip);
            Assert.Equal(Loc.De["bgBlur"], AutomationProperties.GetName(blur));
            Assert.Contains(buttons, button => Equals(button.Content, Loc.De["bgPadMedium"]));
        }
        finally { Loc.Instance.Current = original; }
    });

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

}
