using DMShot.Capture;
using System.Windows;
namespace DMShot.Editor;

public sealed class EditorModel
{
    private readonly List<Annotation> _items = new();
    private readonly Stack<Action> _undo = new();
    private readonly Stack<Action> _redo = new();
    private int _stepCounter;

    public IReadOnlyList<Annotation> Annotations => _items;
    public Annotation? Selected { get; set; }
    public PixelRect? Crop { get; private set; }
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public event Action? Changed;

    // View-state for canvas zoom/pan (see ViewportMath). Authoritative.
    public double UserScale { get; set; } = 1;     // absolute image→view scale (used when !IsFitMode)
    public Point Pan { get; set; }                 // view-space pan beyond centering
    public bool IsFitMode { get; set; } = true;    // true → follow BaseScale (auto-fit on resize)
    public int ZoomPercent { get; set; } = 100;    // for the toolbar indicator (canvas updates it)
    public event Action? ZoomChanged;

    public Annotation CreateStep() => new() { Kind = ToolKind.Step, StepNumber = ++_stepCounter };

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
        Do(() => Crop = rect, () => Crop = prev);
        ResetZoom();
    }

    private void Do(Action apply, Action revert)
    {
        apply();
        _undo.Push(() => { revert(); _redoPush(apply, revert); });
        _redo.Clear();
        Changed?.Invoke();
    }

    private void _redoPush(Action apply, Action revert)
        => _redo.Push(() => { apply(); _undo.Push(() => { revert(); _redoPush(apply, revert); }); Changed?.Invoke(); });

    public void Undo() { if (_undo.Count > 0) { _undo.Pop()(); Changed?.Invoke(); } }
    public void Redo() { if (_redo.Count > 0) { _redo.Pop()(); } }
}
