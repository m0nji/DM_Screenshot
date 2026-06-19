using DMShot.Capture;
using DMShot.Editor;
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
}
