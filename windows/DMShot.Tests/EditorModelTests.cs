using DMShot.Capture;
using DMShot.Editor;
using System.Windows;
using Xunit;

public class EditorModelTests
{
    [Fact]
    public void Add_ThenUndo_Redo_RestoresState()
    {
        var m = new EditorModel();
        m.Add(new Annotation { Kind = ToolKind.Arrow });
        Assert.Single(m.Annotations);
        Assert.True(m.CanUndo);
        m.Undo();
        Assert.Empty(m.Annotations);
        Assert.True(m.CanRedo);
        m.Redo();
        Assert.Single(m.Annotations);
    }

    [Fact]
    public void Step_AutoIncrements()
    {
        var m = new EditorModel();
        var a = m.CreateStep(); var b = m.CreateStep();
        Assert.Equal(1, a.StepNumber);
        Assert.Equal(2, b.StepNumber);
    }

    [Fact]
    public void NewAction_ClearsRedo()
    {
        var m = new EditorModel();
        m.Add(new Annotation { Kind = ToolKind.Rectangle });
        m.Undo();
        m.Add(new Annotation { Kind = ToolKind.Ellipse });
        Assert.False(m.CanRedo);
        Assert.Equal(ToolKind.Ellipse, m.Annotations[0].Kind);
    }

    [Fact]
    public void SetCrop_StoresRect()
    {
        var m = new EditorModel();
        m.SetCrop(new PixelRect(1, 2, 3, 4));
        Assert.Equal(new PixelRect(1, 2, 3, 4), m.Crop);
    }

    [Fact]
    public void ResetZoom_SetsFitAndZeroPan()
    {
        var m = new EditorModel { IsFitMode = false, UserScale = 3, Pan = new Point(5, 6) };
        m.ResetZoom();
        Assert.True(m.IsFitMode);
        Assert.Equal(new Point(0, 0), m.Pan);
    }

    [Fact]
    public void SetCrop_ResetsZoom()
    {
        var m = new EditorModel { IsFitMode = false, Pan = new Point(5, 6) };
        m.SetCrop(new PixelRect(0, 0, 10, 10));
        Assert.True(m.IsFitMode);
        Assert.Equal(new Point(0, 0), m.Pan);
    }

    [Fact]
    public void UndoRedoCrop_ResetsZoom()
    {
        var m = new EditorModel();
        m.SetCrop(new PixelRect(0, 0, 10, 10));

        m.IsFitMode = false;
        m.Pan = new Point(5, 6);
        m.Undo();

        Assert.Null(m.Crop);
        Assert.True(m.IsFitMode);
        Assert.Equal(new Point(0, 0), m.Pan);

        m.IsFitMode = false;
        m.Pan = new Point(7, 8);
        m.Redo();

        Assert.Equal(new PixelRect(0, 0, 10, 10), m.Crop);
        Assert.True(m.IsFitMode);
        Assert.Equal(new Point(0, 0), m.Pan);
    }

    [Fact]
    public void ClearDocument_RemovesStateAndUndoHistory()
    {
        var m = new EditorModel();
        m.Add(new Annotation { Kind = ToolKind.Arrow });
        m.SetCrop(new PixelRect(1, 2, 3, 4));

        m.ClearDocument();

        Assert.Empty(m.Annotations);
        Assert.Null(m.Crop);
        Assert.False(m.CanUndo);
        Assert.False(m.CanRedo);

        m.Undo();

        Assert.Empty(m.Annotations);
        Assert.Null(m.Crop);
    }

    [Fact]
    public void ReplaceDocument_ReplacesStateWithoutKeepingPreviousUndoHistory()
    {
        var m = new EditorModel();
        m.Add(new Annotation { Kind = ToolKind.Arrow, X0 = 1 });
        m.SetCrop(new PixelRect(1, 2, 3, 4));

        m.ReplaceDocument(
            new[] { new Annotation { Kind = ToolKind.Rectangle, X0 = 10, Y0 = 20, X1 = 30, Y1 = 40 } },
            new PixelRect(5, 6, 7, 8));

        Assert.Single(m.Annotations);
        Assert.Equal(ToolKind.Rectangle, m.Annotations[0].Kind);
        Assert.Equal(new PixelRect(5, 6, 7, 8), m.Crop);
        Assert.False(m.CanUndo);
        Assert.False(m.CanRedo);

        m.Undo();

        Assert.Single(m.Annotations);
        Assert.Equal(ToolKind.Rectangle, m.Annotations[0].Kind);
        Assert.Equal(new PixelRect(5, 6, 7, 8), m.Crop);
    }

    [Fact]
    public void Mutate_RecordsUndoAndRedoForDirectAnnotationChanges()
    {
        var m = new EditorModel();
        m.ReplaceDocument(new[] { new Annotation { Kind = ToolKind.Blur, X0 = 1, Y0 = 2, X1 = 3, Y1 = 4, StrokeWidth = 3, BlurStrength = 12 } }, null);
        var a = m.Annotations[0];

        m.Mutate(a, x =>
        {
            x.X0 = 10;
            x.Y0 = 20;
            x.X1 = 30;
            x.Y1 = 40;
            x.StrokeWidth = 8;
            x.BlurStrength = 24;
        });

        Assert.True(m.CanUndo);
        Assert.Equal(10, a.X0);
        Assert.Equal(8, a.StrokeWidth);
        Assert.Equal(24, a.BlurStrength);

        m.Undo();

        Assert.Equal(1, a.X0);
        Assert.Equal(2, a.Y0);
        Assert.Equal(3, a.X1);
        Assert.Equal(4, a.Y1);
        Assert.Equal(3, a.StrokeWidth);
        Assert.Equal(12, a.BlurStrength);

        m.Redo();

        Assert.Equal(10, a.X0);
        Assert.Equal(20, a.Y0);
        Assert.Equal(30, a.X1);
        Assert.Equal(40, a.Y1);
        Assert.Equal(8, a.StrokeWidth);
        Assert.Equal(24, a.BlurStrength);
    }
}
