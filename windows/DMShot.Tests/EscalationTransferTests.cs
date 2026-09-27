using DMShot.Capture;
using DMShot.Editor;
using Xunit;

public class EscalationTransferTests
{
    [Fact]
    public void ReAddingAnnotationsReproducesThemInTargetModel()
    {
        var source = new EditorModel();
        var a1 = new Annotation { Kind = ToolKind.Arrow, X0 = 1, Y0 = 2, X1 = 3, Y1 = 4 };
        var a2 = new Annotation { Kind = ToolKind.Rectangle, X0 = 5, Y0 = 6, X1 = 7, Y1 = 8 };
        source.Add(a1); source.Add(a2); source.SetCrop(new PixelRect(0, 0, 10, 10));

        // Simulate LoadWithState's transfer step against a fresh target model.
        var target = new EditorModel();
        foreach (var a in source.Annotations) target.Add(a.Clone());
        if (source.Crop is { } c) target.SetCrop(c);

        Assert.Equal(2, target.Annotations.Count);
        Assert.Equal(ToolKind.Arrow, target.Annotations[0].Kind);
        Assert.Equal(ToolKind.Rectangle, target.Annotations[1].Kind);
        Assert.Equal(new PixelRect(0, 0, 10, 10), target.Crop);
    }
}
