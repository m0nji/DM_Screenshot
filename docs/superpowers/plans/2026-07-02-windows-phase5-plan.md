# Windows session plan — verify committed changes + Phase 5 (perf & correctness)

For the on-device Windows session (planned 2026-07-03). Two parts: **A)** smoke-test
the Windows changes that were committed unverified from a Mac (phases 1/3/4 of
`2026-07-01-full-review-fix-plan.md`), **B)** implement Phase 5.
Build: `dotnet build windows/DMShot` · Tests: `dotnet test windows/DMShot.Tests`.

## A. Verify committed-unverified Windows changes first

Run the build + LocTests, then smoke-test each item. If something is broken,
fix it before starting Phase 5 — these are already on `main`.

1. **Hotkey crash-loop fix** (`Platform/HotkeySpec.cs`, `App.xaml.cs`,
   `Settings/ShortcutRecorderControl.cs`): in Settings, try recording `Ctrl+Ö`,
   `Esc`, and a punctuation key → the field must NOT accept them (prompt stays);
   record `Ctrl+Shift+5` → works. Manually write `"Ctrl+0xBA"` into settings.json
   → app must start normally with default hotkeys (no crash loop).
2. **Space in inline text editor** (`Editor/CanvasControl.cs OnKeyDown/OnKeyUp`):
   text tool → type "hello world bar" → spaces must appear; cursor must NOT flip
   to the pan hand while typing. After committing, Space+drag panning still works.
3. **WGC 10 fps throttle** (`Platform/WgcScreenRecorder.cs`): record 30 s of a
   playing video → watch Task-Manager memory (should stay in the hundreds of MB,
   not gigabytes); GIF preview/trim/create still works; frame timestamps still
   drive dedup.
4. **Recording ✕ discard button** (`Video/RecordingControlWindow.xaml/.cs`):
   button renders inside the pill, tooltip "Discard"/"Verwerfen", click discards
   (no preview window, nothing in history).
5. **Phase 4 mirrors**: slider drag on a selected shape → ONE Ctrl+Z undoes the
   whole drag (`EditorModel.MutateCoalesced`); bare click with crop tool → no
   0×0 crop, no crash on Copy/Save; crop drag past the image edge clamps; delete
   step 3 of 1-2-3 → next step is 3; Quick-Edit has a redo button; Ctrl+Shift+Z
   redoes in editor + Quick-Edit.

## B. Phase 5 tasks (in this order)

### 5.1 GIF pipeline off the UI thread (HIGH — app freezes "Not Responding")
- `App.xaml.cs:~350` `DeliverGif` → `GifRenderer.Render` + `GifEncoder.EncodeWithDelays`
  run synchronously on the dispatcher.
- Move render+encode to `Task.Run`; marshal the result back; disable the
  Create-GIF button + show a rendering state meanwhile (mac parity:
  `state.rendering` disables the button).
- Kill the per-frame PNG round-trip in `GifEncoder.ToImageSharp`
  (`GifEncoder.cs:~102`: `bmp.Save(PNG)` → `Image.Load`): use
  `LockBits(Format32bppArgb)` → `Image.LoadPixelData<Bgra32>` directly.
- Verify: create a GIF from a 30 s trim → UI stays responsive; output GIF
  byte-identical-ish (spot-check visually + size).

### 5.2 Canvas composite caching (HIGH — editor drag perf; mirrors mac Phase 2)
- `Editor/CanvasControl.cs OnRender` (~line 200): every mouse-move re-runs
  `Renderer.RenderComposite` (full-size GDI bitmap incl. all mosaics) + a full
  `ToBitmapSource` copy.
- Cache the composite of COMMITTED annotations as a frozen BitmapSource,
  invalidated on `Model.Changed` / `Load` / crop; draw only the draft/selection
  on top per frame. NOTE: the new `_blurPreview` cache (bot commit 5260f9d) is
  for the frame background — coordinate, don't duplicate.
- Verify: 4K capture + 3 mosaic regions → dragging an arrow stays smooth.

### 5.3 GDI text size: points → pixels (MEDIUM — text jumps ~33% on commit)
- `Editor/Renderer.cs:~96`: `new Font("Segoe UI", size)` is in POINTS; the
  inline TextBox + `TextLayout.Measure` + `SelectionGeometry.BBox` use the same
  number as WPF PIXELS. Use `GraphicsUnit.Pixel` (also the step-badge font at
  `Renderer.cs:~103`, and check its `d*0.45` vs WPF `d*0.5` factor).
- Verify: type inline text → committed text is the SAME size as while typing;
  selection box hugs the text; step badge number unchanged.
- ⚠️ Existing saved annotations will render smaller after this fix — that's the
  correct size (matches selection box and mac).

### 5.4 Bitmap disposal (MEDIUM — ~100 MB unmanaged per capture on multi-4K)
- `Capture/CaptureCoordinator.cs:~38-56`: dispose every overlay's `Frozen`
  bitmap after the winning crop is produced (and on cancel).
- `App.xaml.cs:~174-186`: dispose the capture `bmp` after `EditorWindow.LoadImage`
  / Quick-Edit clone it.
- `Editor/QuickEditOverlayWindow.xaml.cs`: dispose `_capture` (and the canvas's
  cloned `_source` via a Dispose path) on close.
- Verify: repeated area captures → process memory returns to baseline.

### 5.5 Preview playback timing (MEDIUM — wrong speed)
- `Video/VideoPreviewWindow.xaml.cs:~60-71`: advances one frame per 100 ms tick,
  ignoring `RecordedFrame.TimeSec` (with the new 10 fps capture throttle the
  error is smaller but still wrong for sparse/static captures).
- Advance time-based: `t += 0.1; idx = nearest frame ≤ t`; cache converted
  BitmapSources instead of `ToBitmapSource` per tick.
- Verify: a 10 s recording plays back in ~10 s; scrub label is linear.

### 5.6 Hotkey conflicts surfaced + single instance (MEDIUM)
- `Platform/Win32HotkeyManager.cs:~24`: `RegisterHotKey` return value is dropped
  → report failures; show them in Settings like mac's "already in use by the
  system" row (`systemInUse` loc key exists on mac — add win keys en+de,
  keep LocTests parity green).
- `Program.cs`: add a named Mutex; second instance exits (optionally activates
  the first). Verify: launch the exe twice → one tray icon.

### 5.7 Recording pill placement on mixed DPI (MEDIUM)
- `App.xaml.cs:~310/~388`: uses the PRIMARY monitor's `TransformToDevice` for
  the target display and `Bounds` instead of the work area → off-center pill,
  overlaps the taskbar.
- Position with the TARGET display's scale and clamp to `rcWork` (reuse the
  Quick-Edit `rcWork` logic). Verify on 100% + 150% dual setup, taskbar bottom.

### 5.8 Small fixes (LOW — batch)
- `Capture/OverlayWindow.xaml.cs:~72/171`: clear `Mouse.OverrideCursor` in
  `OnClosed` too (crosshair stuck after Alt+F4 on the overlay).
- `Editor/EditorWindow.xaml.cs:~235`: `HistorySelected` — replace
  `new Bitmap(path)` + `Clone()` with a decoupled copy so the history PNG isn't
  file-locked (delete of the open entry currently can fail silently).
- Quick-Edit stroke slider max 24 vs editor 20 (`QuickEditOverlayWindow` vs
  `EditorWindow.xaml:171`) — align to 20 (full range alignment with mac is a
  Phase 6 decision; 20 removes win's internal inconsistency).

## Out of scope tomorrow (needs product decisions — Phase 6)
Default color red-vs-orange, the three palettes, blur range 2–60 vs 4–40,
points-vs-pixels readout, cursor-in-recording, EN copy drift, recording region
frame on win, PARITY.md corrections. See the main plan, Phase 6 table.

## Done criteria
`dotnet build` clean, `dotnet test` green (incl. Loc key parity), items A1–A5
eyeballed, 5.1–5.5 verified per their checks, then commit per-topic branches and
merge to main (fetch+rebase first — the GitLab bot pushes to main).
