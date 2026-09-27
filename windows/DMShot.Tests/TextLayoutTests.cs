using System.Threading;
using System.Windows;
using DMShot.Editor;
using Xunit;

public class TextLayoutTests
{
    /// <summary>Runs <paramref name="f"/> on a dedicated STA thread (WPF FormattedText
    /// wants STA; xUnit runs MTA by default).</summary>
    private static T OnSta<T>(Func<T> f)
    {
        T result = default!;
        Exception? ex = null;
        var t = new Thread(() => { try { result = f(); } catch (Exception e) { ex = e; } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (ex != null) throw ex;
        return result;
    }

    [Fact]
    public void FontSizeForStroke_ClampsToMinimum()
    {
        Assert.Equal(10, TextLayout.FontSizeForStroke(1), 3);   // 1*5=5 < 10 → 10
        Assert.Equal(50, TextLayout.FontSizeForStroke(10), 3);  // 10*5
    }

    [Fact]
    public void StrokeForFontSize_IsInverseAboveMinimum()
    {
        Assert.Equal(10, TextLayout.StrokeForFontSize(50), 3);
        Assert.Equal(10.0 / 5.0, TextLayout.StrokeForFontSize(5), 3);  // pinned to 10 first
    }

    [Fact]
    public void FontSizeForDragHeight_Clamps()
    {
        Assert.Equal(10, TextLayout.FontSizeForDragHeight(4), 3);
        Assert.Equal(40, TextLayout.FontSizeForDragHeight(40), 3);
    }

    [Fact]
    public void Measure_MultiLine_GrowsWithLinesAndLongestLine()
    {
        var one = OnSta(() => TextLayout.Measure("Ag", 24));
        var two = OnSta(() => TextLayout.Measure("Ag\nAg", 24));
        Assert.True(two.Height > one.Height * 1.6);
        Assert.True(Math.Abs(two.Width - one.Width) < 2.0);
        var wide = OnSta(() => TextLayout.Measure("Agnnnnnn", 24));
        Assert.True(wide.Width > one.Width);
    }
}
