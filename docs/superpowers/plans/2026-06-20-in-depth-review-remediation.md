# DM Screenshot Review Remediation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the in-depth review gaps found in the Windows and macOS DM_Screenshot apps.

**Architecture:** Keep the two native apps separate, with macOS remaining the behavioral source of truth. Fix shared behavior in parallel where it affects users, and add pure unit tests around geometry, settings, localization, and planning math before touching UI-heavy code.

**Tech Stack:** Swift 6 / SwiftUI / AppKit / ScreenCaptureKit / Sparkle for `mac/`; C# / .NET 8 / WPF / Windows.Graphics.Capture / Velopack for `windows/`.

---

## Files To Modify

- `mac/Sources/DMShot/App.swift`: seed localization before building menus; align version fallback; localize GIF viewer title.
- `mac/Info.plist`: sync checked-in bundle versions with repo-root `VERSION`.
- `mac/Sources/DMShot/Settings.swift`: replace launch-at-login "Coming soon" row with a real toggle.
- `mac/Sources/DMShot/AppSettings.swift`: persist launch-at-login preference if a bundled API call cannot be made during tests.
- `mac/Sources/DMShot/LaunchAtLogin.swift`: new macOS ServiceManagement helper.
- `mac/Sources/DMShot/SelectionGeometry.swift`: new pure selection handle and resize geometry.
- `mac/Sources/DMShot/CanvasView.swift`: draw selection handles; support resize; record move/resize/crop in undo state.
- `mac/Sources/DMShot/EditorModel.swift`: store undo snapshots as document state, not annotations only.
- `mac/Sources/DMShot/EditorView.swift`: localize hard-coded help text.
- `mac/Sources/DMShot/VideoPreviewWindow.swift`: show estimated GIF size already computed by `PreviewState`.
- `mac/Sources/DMShot/Localization.swift`: add missing localized keys.
- `mac/Tests/DMShotTests/SelectionGeometryTests.swift`: new geometry tests.
- `mac/Tests/DMShotTests/EditorModelUndoTests.swift`: new undo/document-state tests.
- `mac/Tests/DMShotTests/VersionConsistencyTests.swift`: new `VERSION` / `Info.plist` consistency test.
- `windows/DMShot/DMShot.csproj`: enable non-Windows targeting support for review builds.
- `windows/DMShot.Tests/DMShot.Tests.csproj`: same Windows targeting support for tests.
- `windows/DMShot/Editor/EditorModel.cs`: add document-state reset and undoable mutation APIs.
- `windows/DMShot/Editor/CanvasControl.cs`: use undoable mutations for move/resize/style; clear state on new image.
- `windows/DMShot/Editor/EditorWindow.xaml.cs`: use document-state loading; localize file dialog filters.
- `windows/DMShot/App.xaml.cs`: localize video error dialogs and PNG filter.
- `windows/DMShot/Localization/Loc.cs`: add missing keys for dialogs, tooltips, filters, and preview labels.
- `windows/DMShot/Video/VideoPreviewWindow.xaml`: localize Play/In/Out labels and add estimated GIF size row.
- `windows/DMShot/Video/VideoPreviewWindow.xaml.cs`: update estimated GIF size when trim range changes.
- `windows/DMShot/Video/GifViewerWindow.xaml`: localize `GIF ready`.
- `windows/DMShot/Editor/QuickEditOverlayWindow.xaml.cs`: use localized tooltips.
- `windows/DMShot/Settings/ShortcutRecorderControl.cs`: localize the empty recorder prompt.
- `windows/DMShot/MainPlaceholderWindow.xaml`: delete unused scaffold window.
- `windows/DMShot/MainPlaceholderWindow.xaml.cs`: delete unused scaffold window code-behind.
- `windows/DMShot.Tests/EditorModelTests.cs`: add undo/load-state tests.
- `windows/DMShot.Tests/LocTests.cs`: keep key parity and add spot checks for new keys.
- `scripts/sync-to-github.sh`: already updated so `AGENTS.md` is excluded from GitHub snapshots.

---

### Task 1: Keep AGENTS.md Out Of GitHub Snapshots

**Files:**
- Modify: `scripts/sync-to-github.sh`
- Create: `AGENTS.md`

- [ ] **Step 1: Verify current sync exclusion**

Run:

```bash
git diff -- scripts/sync-to-github.sh
```

Expected: `EXCLUDE` contains `AGENTS.md`.

- [ ] **Step 2: Verify AGENTS.md is present locally**

Run:

```bash
test -f AGENTS.md && sed -n '1,80p' AGENTS.md
```

Expected: file exists and includes the GitLab/GitHub remote rule.

- [ ] **Step 3: Commit**

```bash
git add AGENTS.md scripts/sync-to-github.sh
git commit -m "chore: add agent instructions"
```

### Task 2: Fix macOS Version And Startup Localization

**Files:**
- Modify: `mac/Sources/DMShot/App.swift`
- Modify: `mac/Info.plist`
- Create: `mac/Tests/DMShotTests/VersionConsistencyTests.swift`

- [ ] **Step 1: Write failing version consistency test**

Create `mac/Tests/DMShotTests/VersionConsistencyTests.swift`:

```swift
import XCTest

final class VersionConsistencyTests: XCTestCase {
    func testInfoPlistMatchesRootVersion() throws {
        let testFile = URL(fileURLWithPath: #filePath)
        let repoRoot = testFile
            .deletingLastPathComponent()
            .deletingLastPathComponent()
            .deletingLastPathComponent()
            .deletingLastPathComponent()
        let version = try String(contentsOf: repoRoot.appendingPathComponent("VERSION"))
            .trimmingCharacters(in: .whitespacesAndNewlines)
        let infoURL = repoRoot.appendingPathComponent("mac/Info.plist")
        let data = try Data(contentsOf: infoURL)
        let plist = try XCTUnwrap(PropertyListSerialization.propertyList(from: data) as? [String: Any])
        XCTAssertEqual(plist["CFBundleShortVersionString"] as? String, version)
        XCTAssertEqual(plist["CFBundleVersion"] as? String, version)
    }
}
```

- [ ] **Step 2: Run test to verify it fails before the plist fix**

Run:

```bash
cd mac && swift test --filter VersionConsistencyTests
```

Expected: FAIL while `mac/Info.plist` is still `0.2.5` and `VERSION` is `0.2.8`.

- [ ] **Step 3: Sync `mac/Info.plist` and `App.swift` fallback**

Set both plist values to `0.2.8`:

```xml
<key>CFBundleShortVersionString</key>
<string>0.2.8</string>
<key>CFBundleVersion</key>
<string>0.2.8</string>
```

Change `App.swift` settings version fallback:

```swift
let version = Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "0.2.8"
```

- [ ] **Step 4: Seed language before menu/window setup**

In `applicationDidFinishLaunching`, move language seeding before `setupStatusItem()`:

```swift
func applicationDidFinishLaunching(_ notification: Notification) {
    Localizer.shared.language = appSettings.language
    setupStatusItem()
    setupHotkeys()
    setupPersistence()
    Localizer.shared.$language
        .receive(on: RunLoop.main)
        .sink { [weak self] _ in
            self?.updateMenuTitles()
            self?.settingsWindow?.title = tr(.settingsTitle)
        }
        .store(in: &cancellables)
    overlay.onComplete = { [weak self] image, frame in self?.deliver(image, at: frame) }
    showEditor()
    updater.start()
    Task { await ScreenCapture.registerForScreenRecording() }
}
```

- [ ] **Step 5: Run tests**

Run:

```bash
cd mac && swift test
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add mac/Info.plist mac/Sources/DMShot/App.swift mac/Tests/DMShotTests/VersionConsistencyTests.swift
git commit -m "fix: sync mac version and startup language"
```

### Task 3: Implement macOS Launch At Login

**Files:**
- Create: `mac/Sources/DMShot/LaunchAtLogin.swift`
- Modify: `mac/Sources/DMShot/Settings.swift`
- Modify: `mac/Sources/DMShot/AppSettings.swift`
- Modify: `mac/Tests/DMShotTests/AppSettingsTests.swift`

- [ ] **Step 1: Add persisted setting test**

Append to `AppSettingsTests`:

```swift
func testLaunchAtLoginDefaultsToFalseAndPersists() {
    let d = fresh()
    let s = AppSettingsStore(defaults: d)
    XCTAssertFalse(s.launchAtLogin)
    s.launchAtLogin = true
    XCTAssertTrue(AppSettingsStore(defaults: d).launchAtLogin)
}
```

- [ ] **Step 2: Add setting storage**

In `AppSettingsStore`:

```swift
static let launchAtLoginKey = "launchAtLogin"

@Published var launchAtLogin: Bool {
    didSet { defaults.set(launchAtLogin, forKey: Self.launchAtLoginKey) }
}
```

Initialize it:

```swift
launchAtLogin = defaults.object(forKey: Self.launchAtLoginKey) as? Bool ?? false
```

- [ ] **Step 3: Add ServiceManagement helper**

Create `mac/Sources/DMShot/LaunchAtLogin.swift`:

```swift
import Foundation
import ServiceManagement

enum LaunchAtLoginController {
    static var isSupportedBundle: Bool {
        Bundle.main.bundleURL.pathExtension == "app"
    }

    static func apply(_ enabled: Bool) throws {
        guard isSupportedBundle else { return }
        if enabled {
            try SMAppService.mainApp.register()
        } else {
            try SMAppService.mainApp.unregister()
        }
    }
}
```

- [ ] **Step 4: Replace Settings row**

In `Settings.swift`, replace the "Coming soon" row with:

```swift
settingRow(tr(.launchAtLogin), tr(.launchAtLoginHelp)) {
    Toggle("", isOn: Binding(
        get: { settings.launchAtLogin },
        set: { enabled in
            do {
                try LaunchAtLoginController.apply(enabled)
                settings.launchAtLogin = enabled
            } catch {
                settings.launchAtLogin = !enabled
            }
        }
    ))
    .labelsHidden()
}
```

- [ ] **Step 5: Run tests**

Run:

```bash
cd mac && swift test --filter AppSettingsTests
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add mac/Sources/DMShot/LaunchAtLogin.swift mac/Sources/DMShot/Settings.swift mac/Sources/DMShot/AppSettings.swift mac/Tests/DMShotTests/AppSettingsTests.swift
git commit -m "feat: enable mac launch at login"
```

### Task 4: Add macOS Selection Resize And Undoable Document State

**Files:**
- Create: `mac/Sources/DMShot/SelectionGeometry.swift`
- Create: `mac/Tests/DMShotTests/SelectionGeometryTests.swift`
- Create: `mac/Tests/DMShotTests/EditorModelUndoTests.swift`
- Modify: `mac/Sources/DMShot/EditorModel.swift`
- Modify: `mac/Sources/DMShot/CanvasView.swift`

- [ ] **Step 1: Add pure geometry tests**

Create `mac/Tests/DMShotTests/SelectionGeometryTests.swift`:

```swift
import XCTest
import CoreGraphics
@testable import DMShot

final class SelectionGeometryTests: XCTestCase {
    private func rect(_ x: CGFloat, _ y: CGFloat, _ w: CGFloat, _ h: CGFloat) -> Annotation {
        Annotation(kind: .rect, colorHex: "#EF4444", strokeWidth: 4, x: x, y: y, width: w, height: h)
    }

    func testRectHasFourHandles() {
        XCTAssertEqual(SelectionGeometry.handles(for: rect(10, 20, 100, 50)).count, 4)
    }

    func testHitHandleFindsBottomRight() {
        let a = rect(10, 20, 100, 50)
        XCTAssertEqual(SelectionGeometry.hitHandle(CGPoint(x: 110, y: 70), annotation: a, radius: 6), 3)
        XCTAssertNil(SelectionGeometry.hitHandle(CGPoint(x: 60, y: 45), annotation: a, radius: 6))
    }

    func testResizeBottomRightKeepsTopLeftAnchored() {
        var a = rect(10, 20, 100, 50)
        SelectionGeometry.resize(&a, handle: 3, to: CGPoint(x: 210, y: 170))
        XCTAssertEqual(a.normalizedRect.minX, 10)
        XCTAssertEqual(a.normalizedRect.minY, 20)
        XCTAssertEqual(a.normalizedRect.width, 200)
        XCTAssertEqual(a.normalizedRect.height, 150)
    }
}
```

- [ ] **Step 2: Implement geometry**

Create `mac/Sources/DMShot/SelectionGeometry.swift`:

```swift
import CoreGraphics

enum SelectionGeometry {
    static func handles(for annotation: Annotation) -> [CGPoint] {
        let r = annotation.normalizedRect
        switch annotation.kind {
        case .arrow, .underline:
            return [CGPoint(x: annotation.x, y: annotation.y),
                    CGPoint(x: annotation.x + annotation.width, y: annotation.y + annotation.height)]
        default:
            return [CGPoint(x: r.minX, y: r.minY),
                    CGPoint(x: r.maxX, y: r.minY),
                    CGPoint(x: r.minX, y: r.maxY),
                    CGPoint(x: r.maxX, y: r.maxY)]
        }
    }

    static func hitHandle(_ point: CGPoint, annotation: Annotation, radius: CGFloat) -> Int? {
        for (idx, handle) in handles(for: annotation).enumerated() {
            if abs(point.x - handle.x) <= radius && abs(point.y - handle.y) <= radius {
                return idx
            }
        }
        return nil
    }

    static func resize(_ annotation: inout Annotation, handle: Int, to point: CGPoint) {
        if annotation.kind == .arrow || annotation.kind == .underline {
            if handle == 0 {
                let end = CGPoint(x: annotation.x + annotation.width, y: annotation.y + annotation.height)
                annotation.x = point.x
                annotation.y = point.y
                annotation.width = end.x - point.x
                annotation.height = end.y - point.y
            } else {
                annotation.width = point.x - annotation.x
                annotation.height = point.y - annotation.y
            }
            return
        }
        let handles = handles(for: annotation)
        let anchor = handles[3 - handle]
        annotation.x = anchor.x
        annotation.y = anchor.y
        annotation.width = point.x - anchor.x
        annotation.height = point.y - anchor.y
    }
}
```

- [ ] **Step 3: Add undo state tests**

Create `mac/Tests/DMShotTests/EditorModelUndoTests.swift`:

```swift
import XCTest
import CoreGraphics
@testable import DMShot

final class EditorModelUndoTests: XCTestCase {
    private func image() -> CGImage {
        let ctx = CGContext(data: nil, width: 100, height: 100, bitsPerComponent: 8, bytesPerRow: 0,
                            space: CGColorSpaceCreateDeviceRGB(),
                            bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
        return ctx.makeImage()!
    }

    func testCropUndoRestoresPreviousCrop() {
        let m = EditorModel()
        m.load(image: image(), entryID: "x")
        m.setCrop(CGRect(x: 1, y: 2, width: 30, height: 40))
        XCTAssertNotNil(m.crop)
        m.undo()
        XCTAssertNil(m.crop)
    }

    func testMoveUndoRestoresAnnotationPosition() {
        let m = EditorModel()
        m.load(image: image(), entryID: "x")
        let a = Annotation(kind: .rect, colorHex: "#EF4444", strokeWidth: 4, x: 1, y: 2, width: 30, height: 40)
        m.add(a)
        let id = m.annotations[0].id
        m.snapshot()
        m.update(id, record: false) { $0.x = 10; $0.y = 20 }
        m.undo()
        XCTAssertEqual(m.annotations[0].x, 1)
        XCTAssertEqual(m.annotations[0].y, 2)
    }
}
```

- [ ] **Step 4: Change undo snapshots to include crop**

In `EditorModel.swift`, introduce:

```swift
private struct DocumentState {
    var annotations: [Annotation]
    var crop: CGRect?
}

private var undoStack: [DocumentState] = []
private var redoStack: [DocumentState] = []

private var documentState: DocumentState {
    DocumentState(annotations: annotations, crop: crop)
}

private func restore(_ state: DocumentState) {
    annotations = state.annotations
    crop = state.crop
    selectedID = nil
}

func setCrop(_ rect: CGRect?) {
    snapshot()
    crop = rect
}
```

Update `snapshot()`, `undo()`, and `redo()` to push and restore `DocumentState`.

- [ ] **Step 5: Wire CanvasView resize and undo**

Add fields:

```swift
private var resizingHandle: Int?
private var dragSnapshotTaken = false
```

When selecting, check handles before moving:

```swift
if let id = model.selectedID,
   let selected = model.annotations.first(where: { $0.id == id }),
   let handle = SelectionGeometry.hitHandle(p, annotation: selected, radius: 8 / scale) {
    model.snapshot()
    dragSnapshotTaken = true
    resizingHandle = handle
    return
}
```

When moving, snapshot once before the first direct mutation:

```swift
if !dragSnapshotTaken {
    model.snapshot()
    dragSnapshotTaken = true
}
```

When resizing:

```swift
if let handle = resizingHandle, let id = model.selectedID {
    model.update(id, record: false) { ann in
        SelectionGeometry.resize(&ann, handle: handle, to: p)
    }
}
```

When crop completes, call `model.setCrop(r)` instead of assigning `model.crop = r`.

- [ ] **Step 6: Draw handles**

After the selection rectangle, draw handle squares using `SelectionGeometry.handles(for:)` transformed into view space.

- [ ] **Step 7: Run tests**

Run:

```bash
cd mac && swift test --filter SelectionGeometryTests
cd mac && swift test --filter EditorModelUndoTests
cd mac && swift test
```

Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add mac/Sources/DMShot/SelectionGeometry.swift mac/Sources/DMShot/CanvasView.swift mac/Sources/DMShot/EditorModel.swift mac/Tests/DMShotTests/SelectionGeometryTests.swift mac/Tests/DMShotTests/EditorModelUndoTests.swift
git commit -m "feat: add mac selection resize and undo state"
```

### Task 5: Fix Windows Editor State And Undo Coverage

**Files:**
- Modify: `windows/DMShot/Editor/EditorModel.cs`
- Modify: `windows/DMShot/Editor/CanvasControl.cs`
- Modify: `windows/DMShot/Editor/EditorWindow.xaml.cs`
- Modify: `windows/DMShot.Tests/EditorModelTests.cs`

- [ ] **Step 1: Add failing model tests**

Append to `windows/DMShot.Tests/EditorModelTests.cs`:

```csharp
[Fact]
public void ReplaceDocumentClearsUndoRedoAndLoadsState()
{
    var m = new EditorModel();
    m.Add(new Annotation { Kind = ToolKind.Arrow });
    Assert.True(m.CanUndo);

    var loaded = new[] { new Annotation { Kind = ToolKind.Rectangle, X0 = 1, Y0 = 2, X1 = 3, Y1 = 4 } };
    m.ReplaceDocument(loaded, new PixelRect(0, 0, 10, 10));

    Assert.Single(m.Annotations);
    Assert.Equal(ToolKind.Rectangle, m.Annotations[0].Kind);
    Assert.Equal(new PixelRect(0, 0, 10, 10), m.Crop);
    Assert.False(m.CanUndo);
    Assert.False(m.CanRedo);
}

[Fact]
public void MutateAnnotationIsUndoable()
{
    var m = new EditorModel();
    var a = new Annotation { Kind = ToolKind.Rectangle, X0 = 1, Y0 = 2, X1 = 3, Y1 = 4 };
    m.Add(a);
    m.Mutate(a, x => x.X0 = 10);
    Assert.Equal(10, m.Annotations[0].X0);
    m.Undo();
    Assert.Equal(1, m.Annotations[0].X0);
}
```

- [ ] **Step 2: Implement document replacement and mutation**

In `EditorModel.cs`, add:

```csharp
public void ReplaceDocument(IEnumerable<Annotation> annotations, PixelRect? crop)
{
    _items.Clear();
    _items.AddRange(annotations.Select(a => a.Clone()));
    Crop = crop;
    _undo.Clear();
    _redo.Clear();
    _stepCounter = _items.Where(a => a.Kind == ToolKind.Step).Select(a => a.StepNumber).DefaultIfEmpty(0).Max();
    ResetZoom();
    Changed?.Invoke();
}

public void ClearDocument() => ReplaceDocument(Array.Empty<Annotation>(), null);

public void Mutate(Annotation annotation, Action<Annotation> mutate)
{
    int idx = _items.IndexOf(annotation);
    if (idx < 0) return;
    var before = annotation.Clone();
    Do(
        () => mutate(annotation),
        () => _items[idx] = before.Clone());
}
```

- [ ] **Step 3: Use replacement on image load**

In `CanvasControl.Load`, replace `Reset()` with `Model.ClearDocument()`. In `EditorWindow.LoadWithState`, replace the loop and crop call with `Canvas.Model.ReplaceDocument(annotations, crop)`.

- [ ] **Step 4: Make move/resize/style undoable**

In `CanvasControl`, snapshot direct move and resize mutations with `Model.Mutate` once per gesture. For sliders, replace direct property assignment:

```csharp
Model.Mutate(_selected, a => a.StrokeWidth = w);
Model.Mutate(_selected, a => a.BlurStrength = strength);
```

- [ ] **Step 5: Run Windows tests**

Run on a Windows machine or on a machine with .NET 8 SDK and Windows targeting enabled:

```bash
dotnet test windows/DMShot.sln
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add windows/DMShot/Editor/EditorModel.cs windows/DMShot/Editor/CanvasControl.cs windows/DMShot/Editor/EditorWindow.xaml.cs windows/DMShot.Tests/EditorModelTests.cs
git commit -m "fix: reset windows editor state and undo mutations"
```

### Task 6: Localize Remaining User-Facing Strings

**Files:**
- Modify: `mac/Sources/DMShot/Localization.swift`
- Modify: `mac/Sources/DMShot/EditorView.swift`
- Modify: `mac/Sources/DMShot/App.swift`
- Modify: `windows/DMShot/Localization/Loc.cs`
- Modify: `windows/DMShot/App.xaml.cs`
- Modify: `windows/DMShot/Editor/EditorWindow.xaml.cs`
- Modify: `windows/DMShot/Editor/QuickEditOverlayWindow.xaml.cs`
- Modify: `windows/DMShot/Video/VideoPreviewWindow.xaml`
- Modify: `windows/DMShot/Video/GifViewerWindow.xaml`
- Modify: `windows/DMShot/Settings/ShortcutRecorderControl.cs`
- Modify: `windows/DMShot.Tests/LocTests.cs`

- [ ] **Step 1: Add localization keys**

Add these macOS keys to `L` and translations to `value(_:)`:

```swift
case resetZoomToFit, gifViewerTitle, estimatedGifSize
```

Use:

```swift
case .resetZoomToFit: return ("Reset zoom to fit", "Zoom an Fenster anpassen")
case .gifViewerTitle: return ("DM_Screenshot - GIF", "DM_Screenshot - GIF")
case .estimatedGifSize: return ("Estimated GIF: %@", "Geschätztes GIF: %@")
```

Add these Windows keys to `Loc.En` and `Loc.De`:

```csharp
["resetZoomToFit"] = "Reset zoom to fit",
["gifReady"] = "GIF ready",
["videoPreviewPlay"] = "Play",
["videoPreviewIn"] = "In",
["videoPreviewOut"] = "Out",
["estimatedGifSize"] = "Estimated GIF: {0}",
["videoUnsupported"] = "Video capture requires Windows 10 version 1803 or newer.",
["videoStartFailed"] = "Could not start recording on this display.",
["pngFilter"] = "PNG image|*.png",
["gifFilter"] = "GIF image (*.gif)|*.gif",
["shortcutRecorderPrompt"] = "Click and press keys...",
["editInMainWindow"] = "Edit in main window",
["close"] = "Close",
```

German values:

```csharp
["resetZoomToFit"] = "Zoom an Fenster anpassen",
["gifReady"] = "GIF bereit",
["videoPreviewPlay"] = "Wiedergabe",
["videoPreviewIn"] = "Start",
["videoPreviewOut"] = "Ende",
["estimatedGifSize"] = "Geschätztes GIF: {0}",
["videoUnsupported"] = "Videoaufnahme erfordert Windows 10 Version 1803 oder neuer.",
["videoStartFailed"] = "Aufnahme auf diesem Bildschirm konnte nicht gestartet werden.",
["pngFilter"] = "PNG-Bild|*.png",
["gifFilter"] = "GIF-Bild (*.gif)|*.gif",
["shortcutRecorderPrompt"] = "Klicken und Tasten drücken...",
["editInMainWindow"] = "Im Hauptfenster bearbeiten",
["close"] = "Schließen",
```

- [ ] **Step 2: Replace hard-coded macOS strings**

Use:

```swift
.help(tr(.resetZoomToFit))
viewer.show(gifData: data, title: tr(.gifViewerTitle))
```

- [ ] **Step 3: Replace hard-coded Windows strings**

Use:

```csharp
MessageBox.Show(Loc.Instance["videoUnsupported"], "DM_Screenshot", MessageBoxButton.OK, MessageBoxImage.Warning);
MessageBox.Show(Loc.Instance["videoStartFailed"], "DM_Screenshot", MessageBoxButton.OK, MessageBoxImage.Warning);
Filter = Loc.Instance["pngFilter"];
```

In `QuickEditOverlayWindow.xaml.cs`, replace string tooltips with `Loc.Instance[...]`, using existing tool keys where possible.

- [ ] **Step 4: Update tests**

Run:

```bash
cd mac && swift test --filter LocalizationTests
dotnet test windows/DMShot.sln --filter LocTests
```

Expected: both pass.

- [ ] **Step 5: Commit**

```bash
git add mac/Sources/DMShot/Localization.swift mac/Sources/DMShot/EditorView.swift mac/Sources/DMShot/App.swift windows/DMShot/Localization/Loc.cs windows/DMShot/App.xaml.cs windows/DMShot/Editor/EditorWindow.xaml.cs windows/DMShot/Editor/QuickEditOverlayWindow.xaml.cs windows/DMShot/Video/VideoPreviewWindow.xaml windows/DMShot/Video/GifViewerWindow.xaml windows/DMShot/Settings/ShortcutRecorderControl.cs windows/DMShot.Tests/LocTests.cs
git commit -m "fix: localize remaining UI strings"
```

### Task 7: Show Estimated GIF Size In Both Preview Windows

**Files:**
- Modify: `mac/Sources/DMShot/VideoPreviewWindow.swift`
- Modify: `windows/DMShot/Video/VideoPreviewWindow.xaml`
- Modify: `windows/DMShot/Video/VideoPreviewWindow.xaml.cs`
- Modify: `windows/DMShot.Tests/GifPlanTests.cs`
- Modify: `mac/Tests/DMShotTests/GIFPlanTests.swift`

- [ ] **Step 1: Add macOS UI row**

In `PreviewView`, add:

```swift
Text(String(format: tr(.estimatedGifSize), sizeLabel(state.estimatedBytes)))
    .font(.caption)
    .foregroundStyle(.secondary)
```

- [ ] **Step 2: Add Windows label**

In `VideoPreviewWindow.xaml`, add a label next to duration:

```xml
<TextBlock x:Name="EstimatedSizeLabel"
           Foreground="{StaticResource DmTextDim}"
           FontSize="12"
           Margin="12,0,0,0"/>
```

- [ ] **Step 3: Update Windows size on trim changes**

In `VideoPreviewWindow.xaml.cs`, add:

```csharp
private void UpdateEstimatedSize()
{
    if (_frames.Count == 0)
    {
        EstimatedSizeLabel.Text = string.Format(DMShot.Localization.Loc.Instance["estimatedGifSize"], "0 KB");
        return;
    }
    var (w, h) = GifPlan.ScaledSize(_frames[0].Image.Width, _frames[0].Image.Height);
    int frameCount = GifPlan.FrameTimes(Math.Max(0.0, _trimEnd - _trimStart)).Length;
    long bytes = GifPlan.EstimatedBytes(frameCount, w, h);
    EstimatedSizeLabel.Text = string.Format(
        DMShot.Localization.Loc.Instance["estimatedGifSize"],
        FormatBytes(bytes));
}

private static string FormatBytes(long bytes)
{
    string[] units = { "B", "KB", "MB", "GB" };
    double value = bytes;
    int unit = 0;
    while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
    return unit == 0 ? $"{value:0} {units[unit]}" : $"{value:0.0} {units[unit]}";
}
```

Call `UpdateEstimatedSize()` from `UpdateLabels()` and `UpdateDuration()`.

- [ ] **Step 4: Run GIF plan tests**

Run:

```bash
cd mac && swift test --filter GIFPlanTests
dotnet test windows/DMShot.sln --filter GifPlanTests
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add mac/Sources/DMShot/VideoPreviewWindow.swift windows/DMShot/Video/VideoPreviewWindow.xaml windows/DMShot/Video/VideoPreviewWindow.xaml.cs mac/Tests/DMShotTests/GIFPlanTests.swift windows/DMShot.Tests/GifPlanTests.cs
git commit -m "feat: show estimated gif size"
```

### Task 8: Clean Windows Build And Dead Scaffold Files

**Files:**
- Modify: `windows/DMShot/DMShot.csproj`
- Modify: `windows/DMShot.Tests/DMShot.Tests.csproj`
- Delete: `windows/DMShot/MainPlaceholderWindow.xaml`
- Delete: `windows/DMShot/MainPlaceholderWindow.xaml.cs`

- [ ] **Step 1: Enable Windows targeting outside Windows**

Add to both Windows project files:

```xml
<EnableWindowsTargeting>true</EnableWindowsTargeting>
```

- [ ] **Step 2: Remove unused scaffold window**

Delete:

```bash
git rm windows/DMShot/MainPlaceholderWindow.xaml windows/DMShot/MainPlaceholderWindow.xaml.cs
```

- [ ] **Step 3: Verify Windows tests**

Install .NET 8 SDK if `dotnet` is absent, then run:

```bash
dotnet --info
dotnet test windows/DMShot.sln
```

Expected: `dotnet --info` reports .NET 8 SDK and tests pass.

- [ ] **Step 4: Commit**

```bash
git add windows/DMShot/DMShot.csproj windows/DMShot.Tests/DMShot.Tests.csproj
git commit -m "chore: clean windows build configuration"
```

### Task 9: Final Verification

**Files:**
- No file changes.

- [ ] **Step 1: macOS unit tests**

Run:

```bash
cd mac && swift test
```

Expected: PASS.

- [ ] **Step 2: Windows unit tests**

Run:

```bash
dotnet test windows/DMShot.sln
```

Expected: PASS.

- [ ] **Step 3: macOS app bundle build**

Run only after confirming that resetting Screen Recording permission is acceptable on this machine:

```bash
cd mac && ./build_app.sh release
```

Expected: `mac/build/DM_Screenshot.app` exists and launches.

- [ ] **Step 4: Manual parity checks**

Manually verify on both platforms:

```text
Full screenshot, area screenshot, Esc cancel, copy, save, history delete,
Quick-Edit copy/save/edit-in-main, video full/area, 60s auto-stop,
trimmed GIF creation, GIF history click, language switch, update pane.
```

- [ ] **Step 5: Commit verification note**

```bash
git status --short
```

Expected: clean working tree after commits.

---

## Self-Review

Spec coverage: The plan covers AGENTS/GitHub exclusion, macOS incomplete launch-at-login, version mismatch, startup localization, macOS resize parity, editor undo/reset state, hard-coded UI strings, GIF estimate display, Windows build prerequisites, and dead scaffold files.

Placeholder scan: The plan contains concrete file paths, code snippets, commands, and expected results for each task.

Type consistency: New Swift types use `Annotation`, `CGRect`, `CGPoint`, and `EditorModel` as currently defined. New C# APIs use existing `Annotation`, `PixelRect`, and `ToolKind` types.
