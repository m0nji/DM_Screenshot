using DMShot.Editor;
using Xunit;

public class AnnotationBatchTests
{
    [Fact]
    public void BatchColorIsOneUndoStepAndPreservesOtherAnnotations()
    {
        var model = new EditorModel();
        var first = new Annotation { Kind = ToolKind.Rectangle, ColorArgb = 0xFFFF0000 };
        var second = new Annotation { Kind = ToolKind.Rectangle, ColorArgb = 0xFF00FF00 };
        var untouched = new Annotation { Kind = ToolKind.Blur };
        model.ReplaceDocument(new[] { first, second, untouched }, null);
        var targets = model.Annotations.Take(2).ToArray();
        model.MutateMany(targets, annotation => annotation.ColorArgb = 0xFF0000FF);
        Assert.All(targets, annotation => Assert.Equal(0xFF0000FF, annotation.ColorArgb));
        model.Undo();
        Assert.Equal(first.ColorArgb, targets[0].ColorArgb);
        Assert.Equal(second.ColorArgb, targets[1].ColorArgb);
        Assert.False(model.CanUndo);
        model.Redo();
        Assert.All(targets, annotation => Assert.Equal(0xFF0000FF, annotation.ColorArgb));
        Assert.Equal(untouched.ColorArgb, model.Annotations[2].ColorArgb);
    }

    [Fact]
    public void BatchDeleteRestoresOrderAndBlurWithOneUndo()
    {
        var model = new EditorModel();
        model.ReplaceDocument(new[] {
            new Annotation { Kind = ToolKind.Arrow },
            new Annotation { Kind = ToolKind.Rectangle },
            new Annotation { Kind = ToolKind.Blur }
        }, null);
        var original = model.Annotations.ToArray();
        model.RemoveMany(new[] { original[0], original[2] });
        Assert.Same(original[1], Assert.Single(model.Annotations));
        model.Undo();
        Assert.Equal(original, model.Annotations);
        Assert.False(model.CanUndo);
        model.Redo();
        Assert.Same(original[1], Assert.Single(model.Annotations));
    }
}
