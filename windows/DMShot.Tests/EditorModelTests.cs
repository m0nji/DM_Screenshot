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
}
