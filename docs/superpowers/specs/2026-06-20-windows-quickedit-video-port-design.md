# Windows Port: Quick-Edit Overlay + Video/GIF — Design & Fix-Mapping

**Date:** 2026-06-20
**Status:** Approved (brainstorm); feeds two implementation plans.
**Scope:** Bring the Windows app (`windows/`, .NET 8 / WPF / C#) to behavioral parity
with macOS for two features that currently land only on macOS:

1. **Quick-Edit in-place markup overlay**
2. **Video/GIF capture**

macOS is the behavioral source of truth. The authoritative behavior specs already
exist and do **not** change:

- `docs/superpowers/specs/2026-06-19-quick-edit-bar-design.md`
- `docs/superpowers/specs/2026-06-19-video-gif-capture-design.md`
- `docs/PARITY.md` (pipeline contract + checklist)

This document does **not** re-specify behavior. It records (a) the Windows-specific
**stack decisions**, (b) the **architecture deltas** against the current Windows
code, and (c) the **fix-mapping tables** — every bug macOS already fixed, mapped to
a Windows acceptance criterion so we do **not** rediscover ~25 bugs the hard way.

## Why this document exists

The macOS features reached their current state through many fix commits **after**
the initial feature landed: **20** for Video/GIF and **5** for Quick-Edit. A naive
port of only the "happy path" would reproduce every one of those bugs on Windows.
The tables below turn each macOS fix into a Windows requirement up front.

## Stack decisions (approved 2026-06-20)

| Decision | Choice | Rationale |
|---|---|---|
| Plan sequencing | **Two plans; Quick-Edit first**, then Video/GIF | Quick-Edit is pure WPF UI + reuse of existing `EditorModel`/`CanvasControl`/`Renderer` (no native code, low risk). Video/GIF carries the native-capture risk; do it second. Each plan = own branch, tests, merge. |
| Screen recording API | **Windows.Graphics.Capture (WGC)** | Modern frame-capture API, clean from .NET 8, per-monitor + DPI aware, region crop. Requires **Windows 10 1803+** (acceptable; flagged below). Maps to macOS `SCStream`. |
| Intermediate format | **Raw frames (memory/temp)** — no encoded video file | The parity contract marks capture/container (steps 2–3) as platform-specific. Keeping frames avoids Media Foundation COM interop entirely; preview is a frame-scrubber, GIF encodes straight from frames. Preview UX (scrubber + trim handles) stays identical to macOS — only there is no real video file behind it. |
| GIF encoder | **SixLabors.ImageSharp** | Pure .NET (no native installer footprint), animated GIF with per-frame delays + loop=0, built-in scaling. Frame-dedup (≤0.2% RGB change) is our own logic in front of it — a direct port of the macOS invariants. Maps to macOS `ImageIO`/`CGImageDestination`. |
| Windows video hotkeys | Default **`Ctrl+Alt+1`** (full) / **`Ctrl+Alt+2`** (area), user-configurable | Windows equivalent of macOS `Cmd+Ctrl+1/2`. `Cmd` has no Windows analogue; `Ctrl+Alt` is the closest non-conflicting pair and matches the existing `Ctrl+Shift+1/2` image-capture style. |

### Platform constraint to flag

WGC needs **Windows 10 version 1803 (build 17134)+**. Document in the plan; if a
user is older, video capture should degrade gracefully (feature disabled with a
clear message), not crash.

## Architecture deltas vs. current Windows code

Current capture flow (verified in source):

`CaptureCoordinator.ImageCaptured(Bitmap)` → `App.OnImageCaptured(bmp)` →
auto-copy to clipboard → open/`LoadImage` on `EditorWindow` → `History.Add(...)`.

Key gaps to close:

1. **Capture geometry is not threaded through.** `ImageCaptured` carries only a
   `Bitmap`; the Quick-Edit overlay needs the capture's **on-screen rect** (in
   screen pixels, plus the owning display) to draw the framed capture in place.
   → Introduce a capture-result type that carries `Bitmap` + on-screen
   `PixelRect` + `DisplayInfo`, and a pure `CaptureGeometry` helper
   (selection → screen rect) with unit tests. Full-screen capture's rect is the
   display bounds; area capture's rect is the committed selection.

2. **`OnImageCaptured` hard-codes "open editor".** → Branch on
   `Settings.AfterCapture` (`MainWindow` default | `QuickEdit`). For `QuickEdit`,
   hide the main editor window first, then show the overlay.

3. **`Settings` has only two hotkeys + `LaunchAtLogin`.** → Add `AfterCapture`
   enum/property and two video hotkeys; `SettingsStore.Load()` must tolerate the
   missing keys (older installs) and fall back to safe defaults.

4. **`HistoryStore`/`HistoryEntry` are image-only.** → Add `Kind { Image, Video }`
   (absent → `Image`, backward compatible), `AddVideo(...)`, store `.gif` +
   first-frame thumbnail, and branch history-click on kind (video → re-copy GIF +
   open viewer).

5. **No "recording in progress" app state / foreground flow.** → Add a recording
   controller that hides the app during capture and restores foreground around the
   preview/viewer, mirroring the macOS hand-off.

## Reuse (do NOT rebuild)

- `EditorModel` (annotations, undo/redo, selection, crop) — shared instance between
  overlay and main window enables seamless "Edit in main window" escalation.
- `CanvasControl` / `Renderer` — same WYSIWYG draw + `Flatten()` so reduced tools
  render byte-identically to the main editor and to macOS.
- `ImageInterop`, `WpfClipboard` — extend for GIF clipboard, don't replace.
- `OverlayWindow` selection flow — reused unchanged to pick the video region.
- `Win32HotkeyManager` / `HotkeySpec` — add two IDs, same registration path.

---

# Plan A — Quick-Edit Overlay (Windows)

**Authoritative behavior:** `2026-06-19-quick-edit-bar-design.md` (final design =
in-place overlay, NOT the earlier floating panel).

## New / changed files

| File | Change |
|---|---|
| `Settings/Settings.cs` | `enum AfterCaptureMode { MainWindow, QuickEdit }` + property (default `MainWindow`). |
| `Settings/SettingsStore.cs` | Tolerate missing `AfterCapture` key → default. |
| `Settings/SettingsWindow.xaml(.cs)` | "After capture" picker in General. |
| `Capture/CaptureGeometry.cs` (new) | Pure selection→screen-rect; **unit tests**. |
| `Capture/CaptureCoordinator.cs` | Carry on-screen rect + display in the capture result. |
| `Editor/QuickEditOverlayWindow.xaml(.cs)` (new) | Borderless full-screen `Topmost` overlay: dimmed backdrop, framed capture, floating toolbar. |
| `Editor/QuickEditToolbar` (new control) | Reduced toolset + color/size flyouts + Copy/Save/Edit-in-main/Close. |
| `App.xaml.cs` | Branch `OnImageCaptured` on `AfterCapture`; `ShowQuickEdit(...)`; hide main window first. |
| `docs/PARITY.md` | Replace Quick-Edit "TODO" with Windows file paths. |
| `windows/CHANGELOG.md` (new) | Add the Quick-Edit entry (mirrors macOS). |

## Fix-mapping — macOS Quick-Edit fixes → Windows acceptance criteria

| # | macOS bug (commit) | Windows acceptance criterion |
|---|---|---|
| Q1 | Double-`show()` leaked the Esc monitor / double close (`866257e`) | `ShowQuickEdit` is idempotent: if an overlay is already up, do nothing; Esc handler attached exactly once; detached on close. |
| Q2 | Capture rendered fit-to-window (24pt pad) instead of true size (`b0bf45e`) | Framed capture renders at **true pixel size** (pad = 0), positioned at its on-screen rect — not scaled-to-fit. |
| Q3 | Toolbar ran off-screen at display edges (`b0bf45e`) | Toolbar horizontal center clamped to `160…(screenWidth-160)`; never clipped. |
| Q4 | (positioning) toolbar below/above capture | Toolbar default at `captureBottom+44`; flip to `captureTop-44` if off-bottom; else dock near screen edge (full-screen capture). |
| Q5 | Wrong window level broke modality (`b0bf45e`) | Overlay is `Topmost` on the capture display; stays above other windows; not taskbar-listed. |
| Q6 | Main window + overlay both visible split focus (`5c1dc10`) | Main editor window hidden before overlay shows; overlay is the sole key window. |
| Q7 | (design) backdrop tap behavior | Click on dimmed backdrop **only deselects** the current annotation; it does **not** close the overlay. Esc / ✕ close. |
| Q8 | (design) escalation carries annotations | "Edit in main window" shares the **same `EditorModel` instance**; annotations + crop carry over with zero loss; overlay dismisses. |
| Q9 | (design) focus return | After Copy, overlay dismisses and foreground returns to the previously active app so `Ctrl+V` pastes immediately. |

## Tests (Plan A)

- `CaptureGeometryTests` (xUnit) — port of macOS `CaptureGeometryTests`: selection
  rect → screen rect, including a secondary-display origin offset. (Windows is
  top-left origin throughout; assert no Y-flip but correct display offset.)
- `SettingsTests` — `AfterCapture` defaults to `MainWindow`; round-trips; unknown
  persisted value falls back to `MainWindow` (port of macOS `AppSettingsTests`).
- Manual (user, on Windows): overlay appears in place, reduced tools draw
  identically, color/size flyouts, Copy→paste, Save→PNG, Edit-in-main carries
  annotations, Esc/✕ close, backdrop click only deselects.

---

# Plan B — Video/GIF (Windows)

**Authoritative behavior:** `2026-06-19-video-gif-capture-design.md` + the
PARITY.md pipeline contract (steps 1, 4, 5, 6, 7, 8 binding; 2, 3 platform-specific).

## Binding constants (must match macOS exactly)

| Constant | Value |
|---|---|
| Max recording duration (hard cap) | 60 s |
| Capture frame rate (source) | up to 60 fps |
| GIF playback fps | 10 |
| GIF max width (aspect-preserved) | 1000 px |
| GIF loop count | 0 (infinite) |
| Frame-dedup tolerance | ≤ 0.2% RGB pixels changed (0.002) |
| Size-estimate constant | 0.25 bytes/pixel/frame |
| "Running out" timer threshold | ≤ 10 s → red |
| History max entries | 10 (shared with images) |

## New / changed files

| File | Change |
|---|---|
| `Platform/IScreenRecorder.cs` + `Platform/WgcScreenRecorder.cs` (new) | WGC session → frames + timestamps; region crop; 60s cap. |
| `Video/GifPlan.cs` (new, **pure**) | Port of `GIFPlan.swift`: `frameTimes`, `scaledSize`, `estimatedBytes`, constants. **Unit tests**. |
| `Video/GifEncoder.cs` (new) | ImageSharp; per-frame delays; loop=0; **full opaque frames** (no transparency diff); `FractionDiffering` (RGB only). **Unit tests**. |
| `Video/RecordingControlWindow.xaml(.cs)` (new) | Floating timer (mm:ss, red ≤10s) + Stop; Esc = discard. |
| `Video/VideoPreviewWindow.xaml(.cs)` (new) | Frame-scrubber + trim handles; Create GIF / Discard. |
| `Video/GifViewerWindow.xaml(.cs)` (new) | Animated GIF view + Save… + Copy. |
| `History/HistoryStore.cs`, `History/HistoryEntry.cs` | `Kind { Image, Video }`, `AddVideo`, `.gif` + thumbnail, click→re-copy GIF + viewer. |
| `Platform/WpfClipboard.cs`, `Platform/ImageInterop.cs` | GIF clipboard: **GIF bytes + file drop** (Teams/Outlook). |
| `Settings/Settings.cs` + UI | Two video hotkeys (`Ctrl+Alt+1/2` default, configurable). |
| `App.xaml.cs`, `Capture/CaptureCoordinator.cs` | Hotkey IDs `HK_VIDEO_FULL/AREA`; `StartVideoFull/Area`; foreground/background flow; re-trigger = stop. |
| `docs/PARITY.md` | Replace Video/GIF "TODO" rows with Windows file paths. |
| `windows/CHANGELOG.md` | Add the Video/GIF entry. |

## Fix-mapping — macOS Video/GIF fixes → Windows acceptance criteria

Grouped by area. Each row is a requirement the Windows implementation must satisfy
from day one (derived from a macOS fix commit).

### Recording correctness

| # | macOS bug | Windows acceptance criterion |
|---|---|---|
| V1 | Auto-stop fired repeatedly after 60s | Auto-stop fires **exactly once**: stop the duration timer before invoking the stop callback. |
| V2 | Writer start failure was silent | Recorder start failure is detected, logged, and aborts cleanly (no phantom "recording" with no output). |
| V3 | In-flight frames after stop raced the finalize | Drain/await pending frame callbacks before finalizing the frame buffer. |
| V4 | Cancel went through the slow finish path | Cancel is a fast path: stop capture, drop frames, delete temp, no finalize. |
| V5 | Static section recording aborted (idle/blank frames) | Append **only complete/valid frames**; skip idle frames (WGC may deliver no-change frames for static regions). Recording a static panel must succeed. |
| V8 | Re-pressing the hotkey did nothing | While recording, pressing the video hotkey again **stops** (toggle), not start-new. |

### Preview / lifecycle

| # | macOS bug | Windows acceptance criterion |
|---|---|---|
| V6 | SwiftUI video player crashed on open | (N/A tech) Preview uses the frame-scrubber; no media-player control needed. |
| V15 | Preview window dealloc crash (observer/player) | Deterministic teardown: `VideoPreviewWindow`/recorder implement `IDisposable`; dispose WGC session, frame buffers, timers on close; closing a prior preview before opening a new one. |
| V16 | Preview opened on a black still | Preview auto-plays + loops the frame sequence on open. |
| V9 | Temp `.mov` leaked if preview closed early | Temp frames/files deleted when the preview closes without "Create GIF". |

### Recording control UI

| # | macOS bug | Windows acceptance criterion |
|---|---|---|
| V10 | "Stop" label truncated to "…" | Control panel sizes to content; "Stop" always fully visible. |
| V11 | First click on Stop didn't register | First click on Stop registers even though the panel is a non-activating floating window. |
| V7 | Esc was wired to "finish", not discard | Esc on the control = **discard** (cancel); Stop = **finish**. Distinct paths. |
| V20 | App stayed hidden / foreground confusion | Clean hand-off: hide app while recording (so the user's app is in front and not in-frame); preview and GIF viewer come to foreground; Discard returns focus to the user's app. |

### GIF encoding

| # | macOS bug | Windows acceptance criterion |
|---|---|---|
| V12 | Transparency-diff frames rendered as noise | Encode **full opaque frames** only — no inter-frame transparency/disposal tricks. |
| V13 | One frame per 100ms → huge GIF | Merge consecutive near-identical frames (≤0.2% RGB change) into one held frame with summed delay (the dedup that replaces transparency diffing). |
| V14 | Size estimate was ~200× too high | Size estimate uses 0.25 bytes/px/frame after dedup; or omit a misleading estimate and show duration (matches current macOS preview). |

### GIF viewer & history

| # | macOS bug | Windows acceptance criterion |
|---|---|---|
| V17 | No viewer when opening a video from history | Clicking a video history entry opens the animated GIF viewer (the source frames are gone after the session). |
| V18 | No Save in the GIF viewer | GIF viewer has **Save…** (file dialog, screenshot-naming + `.gif`). |
| V19 | No Copy in the GIF viewer | GIF viewer has **Copy** (re-copies GIF to clipboard). |
| —  | (binding step 7) clipboard representation | Clipboard carries **GIF bytes + a file reference** so Teams/Outlook/etc. paste an animating GIF. Verify against those apps. |

## Tests (Plan B)

- `GifPlanTests` (xUnit) — port of macOS `GIFPlanTests`: frame-time count/spacing,
  always ≥1 frame, downscale preserving aspect, small images untouched, linear
  size estimate.
- `GifEncoderTests` (xUnit) — port of macOS `GIFEncoderTests`: 3-frame animated GIF
  with loop=0; `FractionDiffering` = 0 for identical, 0.25 for one-of-four changed,
  1.0 for mismatched sizes; per-frame delays honored; mismatched delay count
  rejected.
- Manual (user, on Windows): full + area video, 60s auto-stop, static-section
  recording, trim, GIF animates, clipboard pastes into Teams/Outlook, history
  re-copy + viewer Save/Copy.

---

## Out of scope (tracked, not in these plans)

- **Version bump.** Repo-root `VERSION` = `0.1.4` while macOS shipped `0.2.1`; the
  CI tags Windows assets with the git tag regardless. Reconciling the version is a
  separate release decision, not part of these feature ports.
- Audio capture (macOS records video only). No change.

## Parity bookkeeping

On completion of each plan, update `docs/PARITY.md`:
- Feature→file-map rows (Quick-Edit; Video/GIF) — replace `TODO` with Windows paths.
- The Video/GIF pipeline-contract table — fill the Windows column.
- Tick the relevant lines in the release parity checklist.
