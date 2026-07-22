using System.Threading;
using System.Windows;
using DMShot.Editor;
using Xunit;

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
}
