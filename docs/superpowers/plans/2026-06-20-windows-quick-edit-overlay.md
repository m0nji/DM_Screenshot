# Windows Quick-Edit Overlay Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Port the macOS in-place Quick-Edit markup overlay to the Windows app so a capture can be annotated in place (dimmed backdrop + framed capture + floating reduced toolbar) instead of always opening the main editor — with every macOS Quick-Edit bugfix baked in from the start.

**Architecture:** Add an `AfterCapture` setting (MainWindow | QuickEdit). When set to QuickEdit, the captured image is routed to a new borderless, full-screen, `Topmost` `QuickEditOverlayWindow` on the capture's display. The overlay reuses the existing `CanvasControl`/`EditorModel`/`Renderer` for byte-identical drawing, hosts a reduced floating toolbar, and can escalate to the main `EditorWindow` carrying annotations over via a shared/transferred model.

**Tech Stack:** .NET 8, WPF, C#, xUnit. No new NuGet dependencies (pure UI + existing rendering).

## Global Constraints

- **Behavioral source of truth:** `docs/superpowers/specs/2026-06-19-quick-edit-bar-design.md` (final design = in-place overlay, NOT the earlier floating panel). Fix-mapping: `docs/superpowers/specs/2026-06-20-windows-quickedit-video-port-design.md` (table "Plan A").
- **Brand accent:** `#C97B4A` (already `0xFFC97B4A` in code). On-accent label `#1A1A1A`.
- **Reduced toolset (exact):** Select, Arrow, Rectangle, Highlighter, Text, Blur. **Omit** Ellipse, Underline, Step, Crop.
- **Default `AfterCapture` = MainWindow** (preserves current behavior for existing installs; unknown persisted value also falls back to MainWindow).
- **Toolbar horizontal clamp:** center ∈ `[160, screenWidthDip - 160]`.
- **Toolbar vertical:** default `captureBottomDip + 44`; if off-bottom flip to `captureTopDip - 44`; else dock near screen bottom edge.
- **Overlay is `Topmost`, `ShowInTaskbar = false`, `WindowStyle = None`, `AllowsTransparency = true`, `Background` semi-transparent.** Backdrop opacity 40% black.
- **Backdrop click only deselects** the current annotation; it does NOT close. Esc and the ✕ button close.
- Run `dotnet test` from `windows/` before every commit. Every commit message ends with the project's trailer is NOT required here; keep messages conventional (`feat:`/`test:`/`docs:`).
- Parity: this is a Windows-only change that closes an existing macOS↔Windows gap; on completion, update `docs/PARITY.md` (Task 9).

---

### Task 1: `AfterCapture` setting + persistence tolerance

**Files:**
- Modify: `windows/DMShot/Settings/Settings.cs`
- Test: `windows/DMShot.Tests/SettingsTests.cs` (add cases)

**Interfaces:**
- Produces: `enum DMShot.Settings.AfterCaptureMode { MainWindow, QuickEdit }`; `Settings.AfterCapture` property (default `MainWindow`).

- [ ] **Step 1: Write the failing tests**

Add to `windows/DMShot.Tests/SettingsTests.cs`:

```csharp
using DMShot.Settings;
using Xunit;

public class AfterCaptureSettingTests
{
    [Fact]
    public void DefaultsToMainWindow()
    {
        Assert.Equal(AfterCaptureMode.MainWindow, new Settings().AfterCapture);
    }

    [Fact]
    public void RoundTripsThroughStore()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "dmshot-test-" + System.Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new SettingsStore(path);
            store.Save(new Settings { AfterCapture = AfterCaptureMode.QuickEdit });
            Assert.Equal(AfterCaptureMode.QuickEdit, store.Load().AfterCapture);
        }
        finally { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); }
    }

    [Fact]
    public void MissingKeyFallsBackToMainWindow()
    {
        // Settings JSON from an older install without the AfterCapture field.
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "dmshot-test-" + System.Guid.NewGuid().ToString("N") + ".json");
        try
        {
            System.IO.File.WriteAllText(path,
                "{ \"FullScreenHotkey\": \"Ctrl+Shift+1\", \"AreaHotkey\": \"Ctrl+Shift+2\" }");
            Assert.Equal(AfterCaptureMode.MainWindow, new SettingsStore(path).Load().AfterCapture);
        }
        finally { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd windows && dotnet test --filter AfterCaptureSettingTests`
Expected: FAIL — `AfterCaptureMode` / `Settings.AfterCapture` do not exist (compile error).

- [ ] **Step 3: Add the enum + property**

Edit `windows/DMShot/Settings/Settings.cs`:

```csharp
namespace DMShot.Settings;

public enum AfterCaptureMode { MainWindow, QuickEdit }

public sealed class Settings
{
    public string FullScreenHotkey { get; set; } = "Ctrl+Shift+1";
    public string AreaHotkey { get; set; } = "Ctrl+Shift+2";
    public bool LaunchAtLogin { get; set; } = false;
    public AfterCaptureMode AfterCapture { get; set; } = AfterCaptureMode.MainWindow;
}
```

Note: `SettingsStore.Load()` already deserializes with `System.Text.Json`, which leaves
`AfterCapture` at its C# default (`MainWindow`) when the JSON key is absent — so the
missing-key fallback works without further code. (Verified: `SettingsStore.Load` returns
`new()` on any parse error, and the property initializer supplies the default otherwise.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd windows && dotnet test --filter AfterCaptureSettingTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add windows/DMShot/Settings/Settings.cs windows/DMShot.Tests/SettingsTests.cs
git commit -m "feat(quickedit): AfterCapture setting (mainWindow|quickEdit), default mainWindow"
```

---

### Task 2: `CaptureGeometry` pure helper (selection → on-screen rect)

**Files:**
- Create: `windows/DMShot/Capture/CaptureGeometry.cs`
- Test: `windows/DMShot.Tests/CaptureGeometryTests.cs`

**Interfaces:**
- Consumes: `DMShot.Capture.PixelRect` (existing), `System.Drawing.Rectangle` (display bounds).
- Produces: `static PixelRect CaptureGeometry.ScreenRect(PixelRect selectionInDisplay, Rectangle displayBoundsPx)` — returns the capture's rect in **global physical screen pixels**.

> Windows is top-left origin throughout (unlike macOS' bottom-left), so there is **no Y-flip** — only the display's origin offset is added. This is the Windows analogue of macOS `CaptureGeometry.screenRect`.

- [ ] **Step 1: Write the failing test**

Create `windows/DMShot.Tests/CaptureGeometryTests.cs`:

```csharp
using System.Drawing;
using DMShot.Capture;
using Xunit;

public class CaptureGeometryTests
{
    [Fact]
    public void AddsPrimaryDisplayOrigin()
    {
        // Primary display at (0,0) 1000x800. Selection 50px from the top, 200x150.
        var r = CaptureGeometry.ScreenRect(
            new PixelRect(100, 50, 200, 150),
            new Rectangle(0, 0, 1000, 800));
        Assert.Equal(new PixelRect(100, 50, 200, 150), r);
    }

    [Fact]
    public void HonoursSecondaryDisplayOriginOffset()
    {
        // Second display to the right at x=1440. Selection at that display's top-left.
        var r = CaptureGeometry.ScreenRect(
            new PixelRect(0, 0, 50, 50),
            new Rectangle(1440, 0, 1440, 900));
        Assert.Equal(new PixelRect(1440, 0, 50, 50), r);
    }

    [Fact]
    public void HonoursNegativeOriginDisplay()
    {
        // Display left of primary at x=-1920 (a common multi-monitor layout).
        var r = CaptureGeometry.ScreenRect(
            new PixelRect(10, 20, 30, 40),
            new Rectangle(-1920, 0, 1920, 1080));
        Assert.Equal(new PixelRect(-1910, 20, 30, 40), r);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd windows && dotnet test --filter CaptureGeometryTests`
Expected: FAIL — `CaptureGeometry` does not exist.

- [ ] **Step 3: Implement the helper**

Create `windows/DMShot/Capture/CaptureGeometry.cs`:

```csharp
using System.Drawing;
namespace DMShot.Capture;

/// <summary>
/// Pure geometry for the capture → in-place overlay handoff. Converts a selection
/// rect expressed in a display's local pixel space into a global physical-screen
/// pixel rect. Windows is top-left origin (no Y-flip), so we only add the display
/// origin. Mirrors macOS CaptureGeometry.screenRect (which flips Y for AppKit).
/// </summary>
public static class CaptureGeometry
{
    public static PixelRect ScreenRect(PixelRect selectionInDisplay, Rectangle displayBoundsPx)
        => new(displayBoundsPx.Left + selectionInDisplay.X,
               displayBoundsPx.Top + selectionInDisplay.Y,
               selectionInDisplay.Width,
               selectionInDisplay.Height);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd windows && dotnet test --filter CaptureGeometryTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add windows/DMShot/Capture/CaptureGeometry.cs windows/DMShot.Tests/CaptureGeometryTests.cs
git commit -m "feat(quickedit): CaptureGeometry — selection to global screen rect (Windows top-left)"
```

---

### Task 3: Thread capture geometry through `CaptureCoordinator`

**Files:**
- Modify: `windows/DMShot/Capture/CaptureCoordinator.cs`
- Modify: `windows/DMShot/App.xaml.cs:92` (handler signature)

**Interfaces:**
- Produces: `readonly record struct CaptureResult(System.Drawing.Bitmap Image, PixelRect ScreenRectPx, Rectangle DisplayBoundsPx)`; `event Action<CaptureResult>? CaptureProduced`.
- Consumes (Task 2): `CaptureGeometry.ScreenRect`.

> Rationale (architecture delta #1): the overlay must draw the framed capture at its
> real on-screen location. The current `ImageCaptured(Bitmap)` event drops that
> geometry. We add a richer event and keep `App` as the only consumer. For a
> full-screen capture, the on-screen rect is the whole display bounds; for an area
> capture it is the committed selection mapped to screen pixels.

- [ ] **Step 1: Write the failing test (coordinator emits geometry for full-screen)**

Create `windows/DMShot.Tests/CaptureCoordinatorTests.cs`:

```csharp
using System.Drawing;
using DMShot.Capture;
using DMShot.Platform;
using Xunit;

public class CaptureCoordinatorTests
{
    private sealed class FakeCapturer : IScreenCapturer
    {
        public IReadOnlyList<DisplayInfo> GetDisplays() =>
            new[] { new DisplayInfo(0, new Rectangle(0, 0, 1920, 1080), true) };
        public Bitmap CaptureDisplay(DisplayInfo d) => new(d.Bounds.Width, d.Bounds.Height);
        public Bitmap CaptureVirtualDesktop(out Rectangle bounds)
        { bounds = new Rectangle(0, 0, 1920, 1080); return new(1920, 1080); }
    }

    [Fact]
    public void FullScreenEmitsDisplayRectAsScreenRect()
    {
        var c = new CaptureCoordinator(new FakeCapturer());
        CaptureResult? got = null;
        c.CaptureProduced += r => got = r;
        c.CaptureFullScreen();
        Assert.NotNull(got);
        Assert.Equal(new PixelRect(0, 0, 1920, 1080), got!.Value.ScreenRectPx);
        Assert.Equal(new Rectangle(0, 0, 1920, 1080), got!.Value.DisplayBoundsPx);
    }
}
```

(Area capture spawns real `OverlayWindow`s and can't run headless, so it is verified
manually in Task 9. The full-screen path covers the geometry plumbing under test.)

- [ ] **Step 2: Run test to verify it fails**

Run: `cd windows && dotnet test --filter CaptureCoordinatorTests`
Expected: FAIL — `CaptureProduced` / `CaptureResult` do not exist.

- [ ] **Step 3: Add `CaptureResult` + `CaptureProduced`, keep `ImageCaptured` working**

Edit `windows/DMShot/Capture/CaptureCoordinator.cs`. Add the record near the top of the
namespace and a new event; populate it in both capture paths:

```csharp
using System.Drawing;
using System.Runtime.InteropServices;
using DMShot.Platform;
namespace DMShot.Capture;

public readonly record struct CaptureResult(Bitmap Image, PixelRect ScreenRectPx, Rectangle DisplayBoundsPx);

public sealed class CaptureCoordinator
{
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    private struct POINT { public int X, Y; }

    private readonly IScreenCapturer _capturer;
    public event Action<CaptureResult>? CaptureProduced;
    public CaptureCoordinator(IScreenCapturer capturer) => _capturer = capturer;

    public void CaptureFullScreen()
    {
        var displays = _capturer.GetDisplays();
        var target = DisplayUnderCursor(displays);
        var bmp = _capturer.CaptureDisplay(target);
        CaptureProduced?.Invoke(new CaptureResult(bmp,
            new PixelRect(target.Bounds.Left, target.Bounds.Top, target.Bounds.Width, target.Bounds.Height),
            target.Bounds));
    }

    public void CaptureArea()
    {
        var displays = _capturer.GetDisplays();
        var overlays = new List<OverlayWindow>();
        bool done = false;

        foreach (var d in displays)
        {
            var frozen = _capturer.CaptureDisplay(d);
            var o = new OverlayWindow(d, frozen);
            o.Finished += (win, committed) =>
            {
                if (done) return;
                done = true;
                foreach (var ov in overlays) ov.Close();
                if (committed && win.Result is { } r && r.Width > 0 && r.Height > 0)
                {
                    var cropped = ImageInterop.Crop(win.Frozen, r);
                    var screenRect = CaptureGeometry.ScreenRect(r, d.Bounds);
                    CaptureProduced?.Invoke(new CaptureResult(cropped, screenRect, d.Bounds));
                }
            };
            overlays.Add(o);
        }
        foreach (var o in overlays) o.Show();
    }

    private static DisplayInfo DisplayUnderCursor(IReadOnlyList<DisplayInfo> displays)
    {
        GetCursorPos(out var p);
        return displays.FirstOrDefault(d => d.Bounds.Contains(p.X, p.Y))
               ?? displays.First(d => d.IsPrimary);
    }
}
```

- [ ] **Step 4: Update the `App` handler to the new event**

Edit `windows/DMShot/App.xaml.cs`: change the subscription and handler signature (full
body of `OnImageCaptured` is rewritten in Task 7; for now just keep it compiling by
adapting the existing body to read `result.Image`):

```csharp
// in OnStartup:
_coordinator.CaptureProduced += OnCaptureProduced;

// rename OnImageCaptured -> OnCaptureProduced, take CaptureResult:
private void OnCaptureProduced(CaptureResult result)
{
    var bmp = result.Image;
    // ... existing body unchanged, using `bmp` ...
}
```

- [ ] **Step 5: Run test + build to verify**

Run: `cd windows && dotnet test --filter CaptureCoordinatorTests && dotnet build`
Expected: test PASS; solution builds.

- [ ] **Step 6: Commit**

```bash
git add windows/DMShot/Capture/CaptureCoordinator.cs windows/DMShot/App.xaml.cs windows/DMShot.Tests/CaptureCoordinatorTests.cs
git commit -m "feat(quickedit): carry on-screen capture rect through CaptureCoordinator"
```

---

### Task 4: `EditorWindow` adopts overlay annotations (escalation carry-over)

**Files:**
- Modify: `windows/DMShot/Editor/EditorWindow.xaml.cs`

**Interfaces:**
- Produces: `void EditorWindow.LoadWithState(Bitmap image, IReadOnlyList<Annotation> annotations, PixelRect? crop)` — loads the base image then re-adds the given annotations + crop into `Canvas.Model`, so "Edit in main window" carries the overlay's work over (fix **Q8**).
- Consumes: existing `Canvas.Load(Bitmap)`, `Canvas.Model.Add(Annotation)`, `Canvas.Model.SetCrop(PixelRect?)`.

> `CanvasControl` owns its own `EditorModel` instance, so the overlay and the main
> window have separate models. Rather than refactor model ownership, escalation
> transfers state: re-add the (cloned) annotations and crop into the editor's model.

- [ ] **Step 1: Write the failing test**

`EditorWindow` is a WPF `Window` and can't be instantiated headless in xUnit. Instead,
test the transfer logic on `CanvasControl`/`EditorModel` directly. Add
`windows/DMShot.Tests/EscalationTransferTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run test to verify it fails or passes-trivially**

Run: `cd windows && dotnet test --filter EscalationTransferTests`
Expected: PASS (this asserts the transfer contract the method below must honor). If it
fails, `EditorModel`/`Annotation` APIs changed — reconcile before continuing.

- [ ] **Step 3: Add `LoadWithState` to `EditorWindow`**

In `windows/DMShot/Editor/EditorWindow.xaml.cs`, add (near `LoadImage`):

```csharp
public void LoadWithState(System.Drawing.Bitmap image,
                          IReadOnlyList<Annotation> annotations,
                          Capture.PixelRect? crop)
{
    LoadImage(image);
    foreach (var a in annotations) Canvas.Model.Add(a.Clone());
    if (crop is { } c) Canvas.Model.SetCrop(c);
}
```

(If `LoadImage` does not already set `_baseImage`, ensure `LoadWithState` results in the
same `_baseImage` used by `CopyClick`/`SaveClick`. Verify by reading `LoadImage`.)

- [ ] **Step 4: Build to verify**

Run: `cd windows && dotnet build`
Expected: builds clean.

- [ ] **Step 5: Commit**

```bash
git add windows/DMShot/Editor/EditorWindow.xaml.cs windows/DMShot.Tests/EscalationTransferTests.cs
git commit -m "feat(quickedit): EditorWindow.LoadWithState for escalation carry-over"
```

---

### Task 5: `QuickEditOverlayWindow` — backdrop + framed capture host + window setup

**Files:**
- Create: `windows/DMShot/Editor/QuickEditOverlayWindow.xaml`
- Create: `windows/DMShot/Editor/QuickEditOverlayWindow.xaml.cs`

**Interfaces:**
- Produces:
  - `QuickEditOverlayWindow(Bitmap capture, PixelRect screenRectPx, Rectangle displayBoundsPx)`
  - `CanvasControl Canvas { get; }` (the in-overlay annotation surface)
  - events: `event Action? CopyRequested; event Action? SaveRequested; event Action? EditInMainRequested; event Action? Closed;`
  - `void ShowOverlay()` — idempotent; positions on the capture display, installs Esc, shows topmost (fix **Q1**, **Q5**).
- Consumes: `CanvasControl` (Task: existing), `OverlayWindow`'s `SetWindowPos`/`ForceForeground` pattern for physical-pixel placement.

> DPI handling (fix **Q2**, true-size): the capture bitmap is in physical pixels and the
> screen rect is physical pixels, but WPF lays out in DIPs. We host the `CanvasControl`
> (which sizes itself to bitmap pixels) inside a `Viewbox` whose DIP width/height =
> `physicalRect / dpiScale`. The Viewbox scales bitmap-pixel content to true on-screen
> size and keeps mouse hit-testing correct. The Viewbox is placed on a `Canvas` at the
> capture's DIP offset within the display.

- [ ] **Step 1: Create the XAML shell**

Create `windows/DMShot/Editor/QuickEditOverlayWindow.xaml`:

```xml
<Window x:Class="DMShot.Editor.QuickEditOverlayWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        WindowStyle="None" AllowsTransparency="True" ShowInTaskbar="False"
        Topmost="True" ResizeMode="NoResize" Background="#00000000">
  <Grid x:Name="Root">
    <!-- 40% black dimmed backdrop; click only deselects (handled in code-behind) -->
    <Rectangle x:Name="Backdrop" Fill="#66000000"/>
    <!-- Absolute-positioned host for the framed capture + toolbar -->
    <Canvas x:Name="Stage">
      <Border x:Name="Frame" CornerRadius="10" BorderThickness="2"
              BorderBrush="#C97B4A">
        <Border.Effect>
          <DropShadowEffect BlurRadius="16" ShadowDepth="6" Opacity="0.5" Color="Black"/>
        </Border.Effect>
        <Viewbox x:Name="CaptureBox" Stretch="Fill"/>
      </Border>
      <!-- Toolbar host injected from code-behind (Task 6) -->
      <ContentControl x:Name="ToolbarHost"/>
    </Canvas>
  </Grid>
</Window>
```

- [ ] **Step 2: Create the code-behind (window setup, positioning, Esc, idempotency)**

Create `windows/DMShot/Editor/QuickEditOverlayWindow.xaml.cs`:

```csharp
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using DMShot.Capture;
using DMShot.Platform;
namespace DMShot.Editor;

public partial class QuickEditOverlayWindow : Window
{
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    private const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;

    private readonly Bitmap _capture;
    private readonly PixelRect _screenRectPx;
    private readonly Rectangle _displayPx;
    private bool _shown;

    public CanvasControl Canvas { get; } = new();
    public event Action? CopyRequested;
    public event Action? SaveRequested;
    public event Action? EditInMainRequested;
    public event Action? Closed;

    public QuickEditOverlayWindow(Bitmap capture, PixelRect screenRectPx, Rectangle displayBoundsPx)
    {
        InitializeComponent();
        _capture = capture; _screenRectPx = screenRectPx; _displayPx = displayBoundsPx;
        Canvas.ActiveTool = ToolKind.Arrow;
        Canvas.Load(capture);
        CaptureBox.Child = Canvas;
        // Backdrop click = deselect only, never close (fix Q7).
        Backdrop.MouseLeftButtonDown += (_, _) => Canvas.SelectAt(new Point(-1, -1));
        SourceInitialized += OnSourceInit;
        Loaded += OnLoaded;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) CloseOverlay(); };
    }

    /// <summary>Idempotent (fix Q1): a second call while already shown is a no-op.</summary>
    public void ShowOverlay()
    {
        if (_shown) return;
        _shown = true;
        Show();
    }

    private void OnSourceInit(object? s, EventArgs e)
    {
        // Cover the whole capture display in PHYSICAL pixels (DPI-independent).
        var h = new WindowInteropHelper(this).Handle;
        SetWindowPos(h, IntPtr.Zero, _displayPx.Left, _displayPx.Top, _displayPx.Width, _displayPx.Height,
                     SWP_NOZORDER | SWP_NOACTIVATE);
    }

    private void OnLoaded(object? s, RoutedEventArgs e)
    {
        Activate(); Focus();
        double scale = DpiScale();
        // Capture offset within the display, converted physical px -> DIP.
        double capLeftDip = (_screenRectPx.X - _displayPx.Left) / scale;
        double capTopDip  = (_screenRectPx.Y - _displayPx.Top) / scale;
        double capWDip    = _screenRectPx.Width / scale;
        double capHDip    = _screenRectPx.Height / scale;

        CaptureBox.Width = capWDip; CaptureBox.Height = capHDip;
        System.Windows.Controls.Canvas.SetLeft(Frame, capLeftDip);
        System.Windows.Controls.Canvas.SetTop(Frame, capTopDip);

        PositionToolbar(capLeftDip, capTopDip, capWDip, capHDip); // implemented in Task 6
    }

    private double DpiScale()
    {
        var src = PresentationSource.FromVisual(this);
        return src?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
    }

    public void CloseOverlay()
    {
        Closed?.Invoke();
        Close();
    }
}
```

> Note: `PositionToolbar(...)` is declared/used here but its body lands in Task 6 — the
> two tasks ship together (Task 6 finishes the partial class). Until Task 6, add a
> temporary empty `private void PositionToolbar(double l, double t, double w, double h) {}`
> so Task 5 builds in isolation, then replace it in Task 6.

- [ ] **Step 3: Add the temporary stub + build**

Add the empty `PositionToolbar` stub noted above. Run: `cd windows && dotnet build`
Expected: builds clean.

- [ ] **Step 4: Commit**

```bash
git add windows/DMShot/Editor/QuickEditOverlayWindow.xaml windows/DMShot/Editor/QuickEditOverlayWindow.xaml.cs
git commit -m "feat(quickedit): overlay window — topmost backdrop + true-size framed capture (Q1,Q2,Q5,Q7)"
```

---

### Task 6: Floating reduced toolbar + flyouts + actions + positioning

**Files:**
- Modify: `windows/DMShot/Editor/QuickEditOverlayWindow.xaml.cs` (complete the partial class)

**Interfaces:**
- Consumes: `Canvas.ActiveTool/ActiveColor/ActiveStroke/ActiveBlurStrength`, `Canvas.Model.Undo()`, `Canvas.DeleteSelected()`, the window's `CopyRequested/SaveRequested/EditInMainRequested` events.
- Produces: `PositionToolbar(...)` body; builds the toolbar control into `ToolbarHost`.

> Reduced toolset exactly: Select, Arrow, Rectangle, Highlighter, Text, Blur. Actions:
> Color flyout, Size/Blur flyout, Undo, Copy, Save, Edit-in-main, Close (✕). Toolbar
> X-center clamped to `[160, screenWidthDip-160]` (fix **Q3**); Y default below capture,
> flips above if off-bottom, else docks to screen bottom (fix **Q4**).

- [ ] **Step 1: Build the toolbar in code-behind**

Replace the temporary `PositionToolbar` stub with a real toolbar builder + positioner.
Add to `QuickEditOverlayWindow.xaml.cs`:

```csharp
using System.Windows.Media;
// inside the class:

private static readonly (ToolKind kind, string glyph)[] QuickTools =
{
    (ToolKind.Select, ""),     // pointer
    (ToolKind.Arrow, ""),      // arrow
    (ToolKind.Rectangle, ""),  // rectangle
    (ToolKind.Highlighter, ""),// highlighter
    (ToolKind.Text, ""),       // text
    (ToolKind.Blur, ""),       // blur
};

private Border BuildToolbar()
{
    var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6) };

    foreach (var (kind, glyph) in QuickTools)
    {
        var b = ToolButton(glyph, kind.ToString());
        b.Click += (_, _) => { Canvas.ActiveTool = kind; };
        row.Children.Add(b);
    }
    row.Children.Add(Divider());
    // Color flyout
    var color = ToolButton("", "Color");
    color.Click += (_, _) => ToggleColorFlyout();
    row.Children.Add(color);
    // Size/Blur flyout
    var size = ToolButton("", "Size");
    size.Click += (_, _) => ToggleSizeFlyout();
    row.Children.Add(size);
    // Undo
    var undo = ToolButton("", "Undo");
    undo.Click += (_, _) => Canvas.Model.Undo();
    row.Children.Add(undo);
    row.Children.Add(Divider());
    // Copy / Save / Edit-in-main / Close
    var copy = TextButton("Copy"); copy.Click += (_, _) => CopyRequested?.Invoke(); row.Children.Add(copy);
    var save = TextButton("Save"); save.Click += (_, _) => SaveRequested?.Invoke(); row.Children.Add(save);
    var edit = TextButton("Edit in main"); edit.Click += (_, _) => EditInMainRequested?.Invoke(); row.Children.Add(edit);
    var close = ToolButton("", "Close"); close.Click += (_, _) => CloseOverlay(); row.Children.Add(close);

    return new Border
    {
        Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x20, 0x20, 0x20)),
        CornerRadius = new CornerRadius(12),
        BorderBrush = new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)),
        BorderThickness = new Thickness(1),
        Child = row,
    };
}

private Button ToolButton(string glyph, string tip) => new()
{
    Content = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 16 },
    Width = 34, Height = 34, Margin = new Thickness(2), ToolTip = tip,
    Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Brushes.White,
};

private Button TextButton(string text) => new()
{
    Content = text, Height = 34, Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(2),
    Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Brushes.White,
};

private static UIElement Divider() => new Border
{
    Width = 1, Margin = new Thickness(4, 6, 4, 6),
    Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
};
```

> The glyph codes are Segoe MDL2 placeholders; the implementer should pick icons that
> visually match the main editor's toolbar on-device (this is a visual-polish detail,
> not a behavior contract). Match the main editor's `EditorWindow` toolbar styling.

- [ ] **Step 2: Implement the color + size flyouts**

Add inline flyouts that reuse the existing palette/stroke conventions. Keep them simple —
a horizontal swatch row and a slider — toggled below the toolbar row:

```csharp
private static readonly uint[] Palette =
{
    0xFFE5484D, 0xFFF5A623, 0xFF2E9E4F, 0xFF3B7DD8, 0xFF8E5AC8, 0xFF1A1A1A, 0xFFFFFFFF, 0xFFC97B4A
};
private FrameworkElement? _flyout;

private void ToggleColorFlyout()
{
    if (RemoveFlyoutIfKind("color")) return;
    var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6), Tag = "color" };
    foreach (var argb in Palette)
    {
        var c = Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
        var sw = new Button { Width = 22, Height = 22, Margin = new Thickness(3),
            Background = new SolidColorBrush(c), BorderBrush = Brushes.White, BorderThickness = new Thickness(1) };
        sw.Click += (_, _) => { Canvas.ActiveColor = argb; Canvas.ApplyColorToSelected(argb); RemoveFlyout(); };
        row.Children.Add(sw);
    }
    ShowFlyout(row);
}

private void ToggleSizeFlyout()
{
    if (RemoveFlyoutIfKind("size")) return;
    bool blur = Canvas.ActiveTool == ToolKind.Blur || Canvas.Selected?.Kind == ToolKind.Blur;
    var slider = new System.Windows.Controls.Slider
    {
        Minimum = blur ? 4 : 1, Maximum = blur ? 40 : 24, Width = 120, Margin = new Thickness(8),
        Value = blur ? Canvas.ActiveBlurStrength : Canvas.ActiveStroke, Tag = "size",
    };
    slider.ValueChanged += (_, ev) =>
    {
        if (blur) { Canvas.ActiveBlurStrength = (int)ev.NewValue; Canvas.ApplyBlurToSelected((int)ev.NewValue); }
        else { Canvas.ActiveStroke = ev.NewValue; Canvas.ApplyStrokeToSelected(ev.NewValue); }
    };
    ShowFlyout(slider);
}

private void ShowFlyout(FrameworkElement content)
{
    RemoveFlyout();
    var bar = (Border)ToolbarHost.Content!;
    var stack = new StackPanel();
    var row = (UIElement)bar.Child;
    bar.Child = null;
    stack.Children.Add(row);
    var flyoutBar = new Border
    {
        Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x2A, 0x2A, 0x2A)),
        CornerRadius = new CornerRadius(0, 0, 12, 12), Child = content,
    };
    stack.Children.Add(flyoutBar);
    bar.Child = stack;
    _flyout = flyoutBar;
}

private bool RemoveFlyoutIfKind(string kind)
{
    if (_flyout is Border b && b.Child is FrameworkElement fe && (fe.Tag as string) == kind) { RemoveFlyout(); return true; }
    return false;
}

private void RemoveFlyout()
{
    if (_flyout is null) return;
    var bar = (Border)ToolbarHost.Content!;
    if (bar.Child is StackPanel sp && sp.Children.Count > 0)
    {
        var row = sp.Children[0];
        sp.Children.Clear();
        bar.Child = row;
    }
    _flyout = null;
}
```

> This is intentionally lightweight; on-device the implementer should align the flyout's
> look with the main editor. The behavior contract is: color swatch sets `ActiveColor`
> and applies to a current selection; size slider switches between stroke and blur by
> the active tool/selection (matches macOS `EditorContextualSlider`).

- [ ] **Step 3: Implement `PositionToolbar` (clamp + flip — fix Q3, Q4)**

Replace the temporary stub:

```csharp
private void PositionToolbar(double capLeftDip, double capTopDip, double capWDip, double capHDip)
{
    var toolbar = BuildToolbar();
    ToolbarHost.Content = toolbar;
    toolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
    double tbW = toolbar.DesiredSize.Width, tbH = toolbar.DesiredSize.Height;

    double screenW = ActualWidth, screenH = ActualHeight;
    // X: center on capture, clamped so a ~tbW toolbar stays on-screen (Q3).
    double half = Math.Max(160, tbW / 2);
    double centerX = Math.Clamp(capLeftDip + capWDip / 2, half, Math.Max(half, screenW - half));
    double tbLeft = centerX - tbW / 2;

    // Y: default below capture; flip above if off-bottom; else dock to screen bottom (Q4).
    double belowY = capTopDip + capHDip + 12;
    double aboveY = capTopDip - tbH - 12;
    double tbTop;
    if (belowY + tbH <= screenH) tbTop = belowY;
    else if (aboveY >= 0) tbTop = aboveY;
    else tbTop = screenH - tbH - 12;

    System.Windows.Controls.Canvas.SetLeft(toolbar, tbLeft);
    System.Windows.Controls.Canvas.SetTop(toolbar, tbTop);
}
```

> `ToolbarHost` is the XAML `ContentControl` on the `Stage` `Canvas`; `Canvas.SetLeft/Top`
> on a child of a `Canvas` positions it. Since the toolbar lives in `ToolbarHost`, set the
> attached `Canvas.Left/Top` on `ToolbarHost` instead if needed — verify which element is
> the direct `Canvas` child and position that one. (Simplest: drop `ToolbarHost` and add
> the `toolbar` `Border` directly to `Stage` with `Stage.Children.Add(toolbar)`.)

- [ ] **Step 4: Add Delete-key handling for the selection**

In the constructor's `KeyDown`, extend:

```csharp
KeyDown += (_, e) =>
{
    if (e.Key == Key.Escape) CloseOverlay();
    else if (e.Key == Key.Delete) Canvas.DeleteSelected();
    else if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Control) != 0) Canvas.Model.Undo();
};
```

- [ ] **Step 5: Build to verify**

Run: `cd windows && dotnet build`
Expected: builds clean.

- [ ] **Step 6: Commit**

```bash
git add windows/DMShot/Editor/QuickEditOverlayWindow.xaml.cs
git commit -m "feat(quickedit): reduced toolbar + color/size flyouts + clamp/flip positioning (Q3,Q4)"
```

---

### Task 7: App wiring — route capture, hide main window, Copy/Save/escalate/close, foreground return

**Files:**
- Modify: `windows/DMShot/App.xaml.cs`

**Interfaces:**
- Consumes: `Settings.AfterCapture` (Task 1), `CaptureResult`/`CaptureProduced` (Task 3), `EditorWindow.LoadWithState` (Task 4), `QuickEditOverlayWindow` + events (Tasks 5–6), existing `_clipboard`, `Renderer.Flatten`, `ScreenshotFilename`.
- Produces: `ShowQuickEdit(CaptureResult)`; updated `OnCaptureProduced` branching.

> Fixes wired here: **Q6** (hide main window before overlay), **Q9** (focus return after
> Copy), **Q8** (escalation uses `LoadWithState`). The overlay is held in a single field so
> a second capture-while-showing is idempotent (re-uses `ShowOverlay`'s guard, **Q1**).

- [ ] **Step 1: Add the overlay field + branch in `OnCaptureProduced`**

In `windows/DMShot/App.xaml.cs`, add a field and route based on the setting. Replace the
body of `OnCaptureProduced` (from Task 3) with:

```csharp
private QuickEditOverlayWindow? _quickEdit;

private void OnCaptureProduced(CaptureResult result)
{
    var bmp = result.Image;
    _clipboard.SetImage(bmp);                 // auto-copy the raw capture immediately
    _history.Add(bmp, Array.Empty<Annotation>(), null, DateTime.UtcNow);

    if (_settings.AfterCapture == AfterCaptureMode.QuickEdit)
        ShowQuickEdit(result);
    else
        ShowEditorWithImage(bmp);
}

private void ShowEditorWithImage(System.Drawing.Bitmap bmp)
{
    if (_editor is null || !_editor.IsLoaded)
    {
        _editor = new EditorWindow
        {
            OnRequestFullScreen = () => _coordinator.CaptureFullScreen(),
            OnRequestArea = () => _coordinator.CaptureArea(),
            OnRequestSettings = OpenSettings
        };
    }
    _editor.LoadImage(bmp);
    if (!_editor.IsVisible) _editor.Show();
    _editor.Activate();
    _editor.WindowState = WindowState.Normal;
    _editor.Store = _history;
    _editor.RefreshHistory();
}
```

- [ ] **Step 2: Implement `ShowQuickEdit` (hide main window, wire actions)**

```csharp
private void ShowQuickEdit(CaptureResult result)
{
    if (_quickEdit is not null) return;                 // idempotent (Q1)
    _editor?.Hide();                                    // single key window (Q6)

    var overlay = new QuickEditOverlayWindow(result.Image, result.ScreenRectPx, result.DisplayBoundsPx);
    _quickEdit = overlay;

    overlay.CopyRequested += () =>
    {
        using var flat = Renderer.Flatten(result.Image, overlay.Canvas.Model);
        _clipboard.SetImage(flat);
        DismissQuickEdit(restoreForeground: true);      // return focus so Ctrl+V pastes (Q9)
    };
    overlay.SaveRequested += () =>
    {
        using var flat = Renderer.Flatten(result.Image, overlay.Canvas.Model);
        SaveFlattened(flat);
    };
    overlay.EditInMainRequested += () =>
    {
        var anns = overlay.Canvas.Model.Annotations.ToList();
        var crop = overlay.Canvas.Model.Crop;
        DismissQuickEdit(restoreForeground: false);
        ShowEditorWithState(result.Image, anns, crop);  // carry annotations over (Q8)
    };
    overlay.Closed += () => { _quickEdit = null; };

    overlay.ShowOverlay();
}

private void ShowEditorWithState(System.Drawing.Bitmap bmp,
                                 IReadOnlyList<Annotation> anns, Capture.PixelRect? crop)
{
    if (_editor is null || !_editor.IsLoaded)
    {
        _editor = new EditorWindow
        {
            OnRequestFullScreen = () => _coordinator.CaptureFullScreen(),
            OnRequestArea = () => _coordinator.CaptureArea(),
            OnRequestSettings = OpenSettings
        };
    }
    _editor.LoadWithState(bmp, anns, crop);
    _editor.Show(); _editor.WindowState = WindowState.Normal; _editor.Activate();
    _editor.Store = _history; _editor.RefreshHistory();
}

private void DismissQuickEdit(bool restoreForeground)
{
    var ov = _quickEdit;
    _quickEdit = null;
    ov?.Close();
    // restoreForeground: overlay was topmost; closing returns focus to the prior app
    // automatically because the overlay never stole activation from a persistent window.
    // No explicit SetForegroundWindow needed for Q9 in the common case.
}

private void SaveFlattened(System.Drawing.Bitmap flat)
{
    var baseName = ScreenshotFilename.Base(DateTime.Now);
    var dlg = new Microsoft.Win32.SaveFileDialog
    {
        FileName = ScreenshotFilename.Unique(baseName, _ => false),
        Filter = "PNG image (*.png)|*.png",
        DefaultExt = "png",
    };
    if (dlg.ShowDialog() == true)
        flat.Save(dlg.FileName, System.Drawing.Imaging.ImageFormat.Png);
}
```

> Confirm the exact `SaveClick` logic in `EditorWindow.xaml.cs:182` and mirror its
> filename/dialog conventions so Quick-Edit Save matches main-window Save exactly.

- [ ] **Step 3: Build to verify**

Run: `cd windows && dotnet build`
Expected: builds clean.

- [ ] **Step 4: Commit**

```bash
git add windows/DMShot/App.xaml.cs
git commit -m "feat(quickedit): route capture to overlay; hide main window; Copy/Save/escalate (Q6,Q8,Q9)"
```

---

### Task 8: Settings UI — "After capture" picker

**Files:**
- Modify: `windows/DMShot/Settings/SettingsWindow.xaml.cs` (General section)

**Interfaces:**
- Consumes: `Settings.AfterCapture`, the window's existing `Saved` event + `Commit()` path.

- [ ] **Step 1: Add the picker to the General pane**

In `SettingsWindow.xaml.cs`, inside `ShowGeneral()`, add a labeled `ComboBox` (or two
radio buttons) bound to `_settings.AfterCapture`:

```csharp
var afterLabel = new TextBlock { Text = "After capture", Margin = new Thickness(0, 12, 0, 4) };
var afterCombo = new System.Windows.Controls.ComboBox { Width = 240, HorizontalAlignment = HorizontalAlignment.Left };
afterCombo.Items.Add("Open main window");
afterCombo.Items.Add("Show Quick-Edit overlay");
afterCombo.SelectedIndex = _settings.AfterCapture == AfterCaptureMode.QuickEdit ? 1 : 0;
afterCombo.SelectionChanged += (_, _) =>
    _settings.AfterCapture = afterCombo.SelectedIndex == 1 ? AfterCaptureMode.QuickEdit : AfterCaptureMode.MainWindow;
// add afterLabel and afterCombo to the General panel's container
```

> Read `ShowGeneral()` first to match the existing layout container and styling
> (it builds the launch-at-login checkbox the same way). Ensure the value is included
> in whatever `Commit()`/`Saved` already persists via `_settingsStore.Save(...)`.

- [ ] **Step 2: Build + manual sanity**

Run: `cd windows && dotnet build`
Expected: builds clean. (UI verified in Task 9.)

- [ ] **Step 3: Commit**

```bash
git add windows/DMShot/Settings/SettingsWindow.xaml.cs
git commit -m "feat(quickedit): Settings 'After capture' picker (main window | quick-edit)"
```

---

### Task 9: Parity docs, changelog, manual verification

**Files:**
- Modify: `docs/PARITY.md`
- Create: `windows/CHANGELOG.md` (if absent; else modify)

- [ ] **Step 1: Update `docs/PARITY.md`**

Replace the Quick-Edit feature-map Windows cell `TODO` with the real paths:

```
| Quick-Edit bar | `QuickEditOverlay.swift`, … | `Editor/QuickEditOverlayWindow.xaml(.cs)`, `Capture/CaptureGeometry.cs`, `Settings/Settings.cs` (`AfterCapture`), `Settings/SettingsWindow.xaml.cs`, `App.xaml.cs` |
```

And update the `After-capture mode` constants row Windows cell from `TODO` to
`Settings/Settings.cs` (`AfterCapture`).

- [ ] **Step 2: Add the Windows changelog entry**

Add to `windows/CHANGELOG.md` (match the macOS `CHANGELOG.md` format — `## <version> – <date>`
with `- feat:`/`- fix:` bullets):

```markdown
- feat: Quick-Edit in-place markup overlay — annotate a capture in place (dimmed
  backdrop, framed capture, floating reduced toolbar) without opening the main editor.
  Toggle in Settings → After capture. "Edit in main window" carries annotations over.
```

- [ ] **Step 3: Run the full test suite**

Run: `cd windows && dotnet test`
Expected: all tests PASS (existing + new `AfterCaptureSettingTests`, `CaptureGeometryTests`,
`CaptureCoordinatorTests`, `EscalationTransferTests`).

- [ ] **Step 4: Manual verification on a Windows machine (user)**

Verify each — these map 1:1 to the fix-mapping table (Q1–Q9):
- [ ] Settings → After capture → "Show Quick-Edit overlay"; take an **area** capture →
      overlay appears with the capture framed **at its on-screen location, true size** (Q2),
      dimmed backdrop behind (Q5,Q6 — main window not visible).
- [ ] Take a **full-screen** capture in Quick-Edit mode → toolbar docks at the screen
      bottom, not off-screen (Q4).
- [ ] Capture near the **right/left screen edge** → toolbar stays fully on-screen (Q3).
- [ ] Reduced tools (Select/Arrow/Rect/Highlighter/Text/Blur) draw **identically** to the
      main editor; color + size/blur flyouts work; Undo works; Delete removes selection.
- [ ] **Click the dimmed backdrop** → only deselects; overlay stays open (Q7).
- [ ] **Esc** and the **✕** button close the overlay.
- [ ] **Copy** → overlay closes, focus returns to the previous app, `Ctrl+V` pastes the
      annotated image immediately (Q9).
- [ ] **Save** → PNG dialog with screenshot-style filename; saved file matches.
- [ ] **Edit in main window** → main editor opens with the **same image + annotations**
      preserved; further editing/undo works (Q8).
- [ ] Trigger a second capture while the overlay is up → no duplicate overlay / no double
      Esc handler (Q1).

- [ ] **Step 5: Commit docs**

```bash
git add docs/PARITY.md windows/CHANGELOG.md
git commit -m "docs(quickedit): parity map + Windows changelog for in-place overlay"
```

---

## Self-Review (completed by plan author)

- **Spec coverage:** Settings toggle (T1, T8) ✓; in-place overlay + dimmed backdrop +
  framed capture (T5) ✓; reduced tools + flyouts (T6) ✓; Copy/Save (T7) ✓; Edit-in-main
  carry-over (T4, T7) ✓; capture geometry (T2, T3) ✓; all 9 fix criteria mapped to tasks
  and to the manual checklist (T9) ✓.
- **Placeholder scan:** UI-polish notes (glyphs, flyout styling) are explicitly flagged as
  on-device visual choices, not behavior; all behavior steps carry real code.
- **Type consistency:** `CaptureResult`, `ScreenRect`, `LoadWithState`, `ShowOverlay`,
  `AfterCaptureMode`, `CaptureProduced` are used identically across tasks.
- **Known follow-through:** Task 6 Step 3 notes the `ToolbarHost` vs direct-`Stage`-child
  positioning choice — implementer must pick one (simplest: add the toolbar Border directly
  to `Stage`). This is the one spot needing an on-device decision; called out explicitly.
