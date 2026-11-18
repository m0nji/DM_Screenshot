using DMShot.Platform;
using Xunit;

/// Mirrors mac/Tests/DMShotTests/OCRTextOrderTests.swift — keep the cases in sync.
public class OcrTextOrderTests
{
    private static RecognizedLine Line(string text, double x, double y, double w = 50, double h = 10)
        => new(text, x, y, w, h);

    [Fact]
    public void EmptyInputGivesEmptyString() => Assert.Equal("", OcrTextOrder.Join(Array.Empty<RecognizedLine>()));

    [Fact]
    public void SortsLinesTopToBottom()
        => Assert.Equal("first\nsecond\nthird",
            OcrTextOrder.Join(new[] { Line("third", 0, 40), Line("first", 0, 0), Line("second", 0, 20) }));

    [Fact]
    public void SameRowFragmentsJoinLeftToRightWithSpace()
        => Assert.Equal("left middle right",
            OcrTextOrder.Join(new[] { Line("right", 200, 2), Line("left", 0, 0), Line("middle", 100, 1) }));

    [Fact]
    public void SlightlySkewedRowStaysTogether()
        => Assert.Equal("Name: Thomas\nNext line",
            OcrTextOrder.Join(new[] { Line("Name:", 0, 10), Line("Thomas", 80, 14), Line("Next line", 0, 30) }));

    [Fact]
    public void BarelyOverlappingLinesAreSeparateRows()
        => Assert.Equal("upper\nlower", OcrTextOrder.Join(new[] { Line("upper", 0, 0), Line("lower", 60, 8) }));

    [Fact]
    public void TrimsAndDropsBlankLines()
        => Assert.Equal("hello\nworld",
            OcrTextOrder.Join(new[] { Line("  hello  ", 0, 0), Line("   ", 0, 20), Line("world\n", 0, 40) }));

    [Fact]
    public void KeepsUmlautsAndPunctuation()
        => Assert.Equal("Grüße, Straße!\nÜber 100 €",
            OcrTextOrder.Join(new[] { Line("Grüße, Straße!", 0, 0), Line("Über 100 €", 0, 20) }));

    [Fact]
    public void TwoColumnTableReadsRowByRow()
        => Assert.Equal("A1 B1\nA2 B2",
            OcrTextOrder.Join(new[] { Line("B2", 300, 30), Line("A1", 0, 0), Line("B1", 300, 0), Line("A2", 0, 30) }));

    [Fact]
    public void FragmentsOfDifferentHeightsShareARow()
        => Assert.Equal("Heading a b",
            OcrTextOrder.Join(new[] { Line("Heading", 0, 0, 100, 40), Line("b", 140, 30), Line("a", 120, 0) }));
}
