using DMShot.Capture;
using System.Windows;
namespace DMShot.Editor;

public sealed class EditorModel
{
    private readonly List<Annotation> _items = new();
    private readonly Stack<EditCommand> _undo = new();
    private readonly Stack<EditCommand> _redo = new();
    private int _stepCounter;
    private sealed record EditCommand(Action Apply, Action Revert);

    public IReadOnlyList<Annotation> Annotations => _items;
    public Annotation? Selected { get; set; }
    public PixelRect? Crop { get; private set; }
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public event Action? Changed;

    // ── Frame style (persisted via Settings; seeded by App.EnsureEditor) ──
    public bool BackgroundEnabled { get; set; }
    public FramePadding FramePadding { get; set; } = FramePadding.Medium;
    public FrameCorner FrameCorner { get; set; } = FrameCorner.Soft;
    public FrameBackgroundKind FrameBackgroundKind { get; set; } = FrameBackgroundKind.Blur;
    public string FrameSolidHex { get; set; } = "#ffffff";
    public FrameGradient FrameGradient { get; set; } = FrameGradient.Warm;

    private int _imgW, _imgH;
    /// <summary>Record the base image pixel size. Call whenever a new image loads (EditorWindow.LoadImage).</summary>
    public void SetImageSize(int w, int h) { _imgW = w; _imgH = h; }

    /// <summary>Snapshot of the current frame style parameters.</summary>
    public BackgroundStyle Style => new(
        BackgroundEnabled, FramePadding, FrameCorner, FrameBackgroundKind, FrameSolidHex, FrameGradient);

    /// <summary>The plain view rect (crop or full image) in image pixels.</summary>
    public Rect ViewRect => Crop is { } c
        ? new Rect(c.X, c.Y, c.Width, c.Height)
        : new Rect(0, 0, _imgW, _imgH);

    /// <summary>Outer (framed) content extent when the frame is on, else the plain view rect.</summary>
    public Rect FramedContentRect => BackgroundEnabled
        ? FrameGeometry.OuterRect(ViewRect, FramePadding)
        : ViewRect;

    // View-state for canvas zoom/pan (see ViewportMath). Authoritative.
    public double UserScale { get; set; } = 1;     // absolute image→view scale (used when !IsFitMode)
    public Point Pan { get; set; }                 // view-space pan beyond centering
    public bool IsFitMode { get; set; } = true;    // true → follow BaseScale (auto-fit on resize)
    public int ZoomPercent { get; set; } = 100;    // for the toolbar indicator (canvas updates it)
    public event Action? ZoomChanged;

    public Annotation CreateStep() => new() { Kind = ToolKind.Step, StepNumber = ++_stepCounter };

    public void RaiseZoomChanged() => ZoomChanged?.Invoke();

    public void ResetZoom()
    {
        IsFitMode = true;
        Pan = new Point(0, 0);
        ZoomChanged?.Invoke();
    }

    public void Add(Annotation a)
    {
        Do(() => _items.Add(a), () => _items.Remove(a));
    }

    public void Remove(Annotation a)
    {
        int idx = _items.IndexOf(a);
        if (idx < 0) return;
        Do(() => _items.Remove(a), () => _items.Insert(idx, a));
    }

    public void SetCrop(PixelRect? rect)
    {
        var prev = Crop;
        Do(
            () =>
            {
                Crop = rect;
                ResetZoom();
            },
            () =>
            {
                Crop = prev;
                ResetZoom();
            });
    }

    public void ClearDocument()
    {
        ReplaceDocument(Array.Empty<Annotation>(), null);
    }

    public void ReplaceDocument(IEnumerable<Annotation> annotations, PixelRect? crop)
    {
        var replacement = annotations.Select(a => a.Clone()).ToList();
        _items.Clear();
        _items.AddRange(replacement);
        Selected = null;
        Crop = crop;
        _undo.Clear();
        _redo.Clear();
        _stepCounter = _items.Select(a => a.StepNumber).DefaultIfEmpty(0).Max();
        ResetZoom();
        Changed?.Invoke();
    }

    public void Mutate(Annotation a, Action<Annotation> mutate)
    {
        if (!_items.Contains(a)) return;
        var before = a.Clone();
        mutate(a);
        RecordMutation(a, before);
    }

    public void RecordMutation(Annotation a, Annotation before)
    {
        if (!_items.Contains(a)) return;
        var beforeSnapshot = before.Clone();
        var afterSnapshot = a.Clone();
        if (SameAnnotation(beforeSnapshot, afterSnapshot)) return;
        Record(
            () => CopyAnnotation(afterSnapshot, a),
            () => CopyAnnotation(beforeSnapshot, a));
    }

    private void Do(Action apply, Action revert)
    {
        apply();
        Record(apply, revert);
    }

    private void Record(Action apply, Action revert)
    {
        _undo.Push(new EditCommand(apply, revert));
        _redo.Clear();
        Changed?.Invoke();
    }

    private static bool SameAnnotation(Annotation a, Annotation b)
        => a.Kind == b.Kind
           && a.X0.Equals(b.X0)
           && a.Y0.Equals(b.Y0)
           && a.X1.Equals(b.X1)
           && a.Y1.Equals(b.Y1)
           && a.ColorArgb == b.ColorArgb
           && a.StrokeWidth.Equals(b.StrokeWidth)
           && a.Text == b.Text
           && a.StepNumber == b.StepNumber
           && a.BlurStrength == b.BlurStrength;

    private static void CopyAnnotation(Annotation source, Annotation target)
    {
        target.Kind = source.Kind;
        target.X0 = source.X0;
        target.Y0 = source.Y0;
        target.X1 = source.X1;
        target.Y1 = source.Y1;
        target.ColorArgb = source.ColorArgb;
        target.StrokeWidth = source.StrokeWidth;
        target.Text = source.Text;
        target.StepNumber = source.StepNumber;
        target.BlurStrength = source.BlurStrength;
    }

    public void Undo()
    {
        if (_undo.Count == 0) return;
        var command = _undo.Pop();
        command.Revert();
        _redo.Push(command);
        _stepCounter = _items.Select(a => a.StepNumber).DefaultIfEmpty(0).Max();
        Changed?.Invoke();
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        var command = _redo.Pop();
        command.Apply();
        _undo.Push(command);
        _stepCounter = _items.Select(a => a.StepNumber).DefaultIfEmpty(0).Max();
        Changed?.Invoke();
    }
}
