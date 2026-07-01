# Full app review — findings & fix plan (2026-07-01, main @ 087f96c)

Five-track review (mac capture/recording, mac editor, mac app shell, Windows port,
mac↔win parity). All findings verified against source; file:line references are from
`main` at 087f96c. Phase 1 is already implemented on branch `fix/review-quickwins`.

Legend: 🍎 mac, 🪟 windows, 🍎🪟 both. Severity: C critical, H high, M medium, L low.

---

## Phase 1 — Quick wins (DONE on `fix/review-quickwins`)

| # | Sev | Fix |
|---|-----|-----|
| 1 | H 🍎 | Full-screen video recorded at point resolution (blurry on Retina): `VideoRecorder.swift` full-display branch now multiplies `SCDisplay.width/height` (points) by `backingScaleFactor`. |
| 2 | H 🍎 | Own app (Stop HUD, region frame, editor) was recorded into every recording: filter is now `SCContentFilter(display:excludingApplications:exceptingWindows:)` with our own app — covers windows created after the snapshot. **Needs on-device verification (agent can't see capture output).** |
| 3 | H 🍎 | `VideoPreviewWindow` missing `isReleasedWhenClosed = false` → over-release when closed via title-bar X (same crash class already documented in that file). |
| 4 | H 🪟 | Hotkey crash loop: an OEM key (e.g. `Ctrl+Ö`) was stored as `"Ctrl+0xBA"`, `HotkeySpec.Parse` threw on every startup. Added `TryParse` + `RoundTrips`; `App.RegisterHotkeysFromSettings` falls back to per-action defaults; recorder control rejects non-round-trippable keys. |
| 5 | H 🪟 | Space in the inline text editor armed canvas pan and swallowed the character (spaces untypeable in text/step annotations): `CanvasControl.OnKeyDown/OnKeyUp` now pass through while `_textBox` is active. |
| 6 | C 🪟 | WGC recorder buffered **every** frame (60+ fps × 60 s of uncompressed bitmaps ⇒ 30 GB on 1080p video content): `OnFrameArrived` now throttles to the 10 fps grid GifPlan samples anyway. Cuts memory ~6×; see Phase 5 for the rest. |
| 7 | M 🍎 | Updater stuck on "Checking…" forever when a scheduled check already found an update (`showUpdateInFocus` was a no-op); "Ready to install — v" with empty version. Offer is now stashed (`lastFound`) and restored. |
| 8 | M 🍎 | Localization violations: `ShortcutRecorderView` "Press keys…" → new `L.pressKeys` (en/de); `EditorView` tooltip literal → existing `L.deleteCapture`. |
| 9 | L 🍎 | GIF viewer orphaned on history click (`loadHistory` didn't close the previous viewer → dead Save/Copy buttons); failed recordings leaked `dmshot-rec-*.mov` in tmp. |

Verified: `swift build` + `swift test` green (151 tests). Windows changes are
code-reviewed but unbuilt (no Windows machine) — verify before release.

---

## Phase 2 — macOS editor performance (DONE on `perf/editor-caches` — items 1–5; item 6 deferred)

These compound: with Background=Blur on a 5K capture, one mouse-drag currently does
a full-image CIGaussianBlur + per-blur-annotation blurs + up to 10 thumbnail PNG
reads *per mouse-move*.

1. **H Cache the frame-background blur.** `CanvasView.swift:127-131` → `FrameRenderer.drawBackground` (`FrameRenderer.swift:71-96`) re-runs CIGaussianBlur over the whole screenshot on every `draw(_:)`. Cache the blurred CGImage keyed by (source, crop, padding); invalidate on those changes only. Same for `model.blurSourceImage` re-cropping per draw (`EditorModel.swift:79-83`).
2. **H Cache blur-annotation renders.** `Rendering.swift:195-206` re-runs CIGaussianBlur per blur annotation per redraw. Cache per annotation keyed by (rect, radius); invalidate on mutation.
3. **H In-memory thumbnail cache.** `HistoryStore.thumbnail` (`HistoryStore.swift:126-129`) does disk read + PNG decode; `EditorView.swift:149-157` calls it on every model tick (i.e. every drag move). Cache `NSImage` by id, invalidate on updateEntry/delete.
4. **M Move auto-persist off the main thread.** `App.swift:192-202` debounced persist runs full-res `flatten()` + PNG encode synchronously on main; also fires on plain `model.load(...)` (rewrites files just for opening an entry). Flatten+encode on a background queue; skip persist when nothing changed.
5. **M Capture delivery I/O off main thread.** `App.swift:342-345` + `HistoryStore.addCapture` PNG-encode + write the full capture before Quick-Edit appears (beachball on 5K). Copy to clipboard first, persist async. Same for `deliverGIF`'s double multi-MB write (`App.swift:309-315`).
6. **L Overlay repaint.** `Overlay.swift:68-93` full-image repaint + new `NSImage` wrapper per mouse-move. Optional: layer-backed dim + mask. *(Deferred — not part of the editor-caches branch.)*

Parity note: 1–3 are mac-only perf internals. Windows has its own equivalents in Phase 5.

## Phase 3 — macOS recording robustness (DONE on `fix/recording-robustness` — items 1–6; item 7 deferred)

1. **H Surface recording failures.** No `SCStreamDelegate` (`VideoRecorder.swift:69` passes nil) → mid-recording death (display unplugged, TCC revoked) keeps HUD counting; `App.swift:293` `guard let url … else { return }` makes a failed stop produce *nothing*. Implement `stream(_:didStopWithError:)`, propagate error → alert + HUD teardown; alert on nil stop result; guard `finishWriting` when `startWriting` never ran (`VideoRecorder.swift:124-125`).
2. **M Recorder state machine.** Hotkey double-press interleaves two lifecycles on the shared recorder (`App.swift:227-241, 289-296` vs async `start/stop`): add `.idle/.starting/.recording/.stopping` and ignore toggles while transitioning.
3. **M Make cancel reachable.** `RecordingControlWindow.swift:31` `.onExitCommand` never fires (panel never key). Give the HUD an explicit ✕ Cancel button (parity: Windows control window — check `RecordingControlWindow.xaml`).
4. **M GIF memory + speed.** `VideoPreviewWindow.swift:22-37` holds all kept frames as CGImages + whole GIF in one NSMutableData (`GIFEncoder.swift:19-38`); ~1.4 GB for 60 s dynamic content. Stream with `CGImageDestinationCreateWithURL`, drop frames after add. Also `GIFEncoder.fractionDiffering` (`:44-65`): cache the last-kept frame's RGBA bytes, early-exit when past tolerance (currently 2 full re-renders + full scan per compared frame).
5. **M Clamp drag selections to the display.** `Overlay.swift:207-227`: unclamped drags → `screenRect` disagrees with cropped image (Quick-Edit misplacement) and out-of-bounds `sourceRect` for section recordings. Clamp selection to `bounds` during drag. 🍎🪟 check `windows/DMShot/Capture/Selection.cs` for the same.
6. **L Spaces/fullscreen behavior.** Overlay windows + recording HUD lack `.canJoinAllSpaces`/`.fullScreenAuxiliary` (`Overlay.swift:252-299`, `RecordingControlWindow.swift:55-73`) — area capture over a full-screen app can switch Spaces; HUD stays behind on Space switch.
7. **L Loupe edge accuracy.** `LoupeMath.sampleRect` clamps the window but the crosshair stays centered (`Overlay.swift:163-170`) — wrong pixel indicated within 10 px of edges. Offset the crosshair by the clamp delta. 🍎🪟 (LoupeMath is mirrored). *(Deferred — parity-coupled, own change.)*
   Notes from implementation: mac selection clamp (item 5) matches the existing `SelectionMath.Clamp` on Windows; the visible ✕ discard button (item 3) was added on BOTH platforms; win recording-failure surfacing remains Phase 5.

## Phase 4 — Undo & editing correctness (🍎🪟 parity-coupled)

1. **H mac: color-wheel drag floods undo.** `EditorControls.swift:35-41` records a snapshot per tick (and wipes redo); cap-50 evicts real history. One snapshot per color-panel gesture.
2. **M mac: stroke/blur slider edits invisible to undo** (`record: false`, no gesture snapshot — `EditorControls.swift:114-122`). 🪟 mirror-image bug: one undo entry *per slider tick* (`CanvasControl.cs:496-505`, `EditorModel.cs:111-128`). Fix both to: snapshot at gesture start, record once at gesture end.
3. **M mac: crop not clamped to image.** `CanvasView.swift:416-419` — crop can include the pad gutter; export (`ImageUtils.crop` intersects) silently differs from preview + W×H readout. Clamp to pixel bounds. 🪟 `CanvasControl.cs:478-483` additionally commits a 0×0 crop on a bare click → `new Bitmap(0,0)` throws on Copy/Save. Add min-size guard.
4. **M mac: blur annotation half off-image renders stretched** (`Rendering.swift:196-205` draws clamped source into unclamped rect). Draw into the clamped rect. Check `windows/DMShot/Editor/Renderer.cs` mosaic path for the same.
5. **M mac: step numbering skips after delete** (`EditorModel.swift:140-152` doesn't recompute `stepCounter`; undo path does). Recompute on remove.
6. **M mac: Quick-Edit Esc monitor closes overlay while typing** (`QuickEditOverlay.swift:135-141` app-wide monitor beats the text view; also closes overlay from the *main editor*). Let Esc commit text first; scope monitor to the overlay window.
7. **L mac: arrow/underline hit-test = full bounding box** (`SelectionGeometry.swift:174-183`) — long diagonal arrow occludes everything under its box. Distance-to-segment test. 🍎🪟 (SelectionGeometry mirrored).
8. **L both: redo asymmetries.** Quick-Edit has undo but no redo (both platforms); win main editor lacks Ctrl+Shift+Z.

## Phase 5 — Windows performance & correctness

1. **H GIF pipeline off the UI thread.** `App.xaml.cs:350-367` → `GifRenderer.Render` + `GifEncoder.EncodeWithDelays` run synchronously on the dispatcher (app "Not Responding" for a long trim). `Task.Run` + progress UI (parity: mac shows `state.rendering`). Kill the per-frame PNG round-trip in `ToImageSharp` (`GifEncoder.cs:102-107`) — `LockBits` → `LoadPixelData<Bgra32>`.
2. **H Canvas full recomposite per render.** `CanvasControl.cs:200-201` + `Renderer.RenderComposite`: full-size GDI bitmap + ToBitmapSource copy per mouse-move. Cache committed-annotation composite; draw only the draft on top.
3. **M Text renders ~33% larger than editor/selection box.** `Renderer.cs:96` GDI `Font` in **points** vs WPF px everywhere else (`TextLayout.cs`). Use `GraphicsUnit.Pixel` (also step-badge font `Renderer.cs:103`).
4. **M Bitmap disposal.** `CaptureCoordinator.cs:38-56` (frozen displays), `App.xaml.cs:174-186` (capture original), `QuickEditOverlayWindow.xaml.cs:32,61` — dispose after handoff/close instead of waiting for finalizers (~100 MB per capture on multi-4K).
5. **M Preview playback speed.** `VideoPreviewWindow.xaml.cs:60-71` steps one frame per 100 ms tick ignoring timestamps. Time-based advance; cache converted BitmapSources.
6. **M RegisterHotKey failures silent + no single-instance mutex** (`Win32HotkeyManager.cs:24-29`, `Program.cs`). Surface conflicts (mac has `systemInUse` UX — parity), add mutex.
7. **M Recording HUD placement.** `App.xaml.cs:310-315, 388-399`: wrong monitor's DPI + `Bounds` instead of `rcWork` → overlaps taskbar on mixed-DPI. Reuse the Quick-Edit `rcWork` clamp.
8. **L `Mouse.OverrideCursor` stuck on abnormal overlay close** (`OverlayWindow.xaml.cs:72,171`): clear in `OnClosed`.
9. **L capture→overlay latency:** history PNG write on hot path (`App.xaml.cs:174-186`) — background it (mirror of Phase 2.5).

## Phase 6 — Parity reconciliation (decide: fix or document in PARITY.md)

Zero TODOs referencing PARITY.md exist; every divergence below is undocumented.
Mac is source of truth — default = align Windows to mac unless noted.

| # | Divergence | mac | win | Proposed |
|---|-----------|-----|-----|----------|
| 1 | Default annotation color | `#EF4444` red (`EditorModel.swift:8`) | brand orange `0xFFC97B4A` (`CanvasControl.cs:77`) | win → red; decide whether the extra orange swatch stays (then add to mac too) |
| 2 | Quick-Edit palette | = editor palette | third, different palette (`QuickEditOverlayWindow.xaml.cs:292-295`) | win → same 7 hexes as mac |
| 3 | Blur slider range | 2–60 (`EditorControls.swift:98`) | 4–40 (`EditorWindow.xaml:176`); QE stroke max 24 vs 20 | win → 2–60 / 20 |
| 4 | Default stroke | 4 (`EditorModel.swift:11`) | 3 (`Settings.cs:20`, comment claims "match") | win → 4 |
| 5 | Shortcut validation/reset | needs-modifier, duplicate, system-conflict, Reset button | none | port validation + Reset to win |
| 6 | Selection size readout | points (`Overlay.swift:100`) | physical px (`OverlayWindow.xaml.cs:109`) | align (recommend px on both — it's a screenshot tool) — needs a product decision |
| 7 | Recording region frame | yes | absent | port to win (WGC crops in software, frame is just chrome) |
| 8 | Editor keyboard shortcuts | none (⌘C/⌘S/⌘Z do nothing!) | Del/Ctrl+C/S/Z/Y | **add to mac** (source-of-truth gap); mac Esc-deselect → win |
| 9 | Cursor in recording | recorded (`showsCursor = true`) | disabled (`TryDisableCaptureCursor`) | win → capture cursor, or document |
| 10 | Dim overlay alpha | 0.35 | 0.50 | align to 0.35 |
| 11 | EN copy drift (tray "Open Window"/"Open Editor", video labels, GIF preview strings, login "log in/sign in") | — | — | align EN strings to mac wording |
| 12 | Video thumb play-badge, thumbnail sizing 320/no-upscale vs 200/upscale, tray hotkey hints | mac has | win lacks | port to win |
| 13 | PARITY.md video step 6 claims mac does inter-frame delta optimization | false (`GIFEncoder.swift:12-17`) | true on win | correct the doc; also fix mac GIF size estimate rationale (`GIFPlan.swift:7-10`) |
| 14 | Custom color input | native picker | hex field | document as intentional, or converge |

## Phase 7 — App-shell polish (mac unless noted)

1. **H Launch-time silent update check reports nothing** — `Updater.swift:61` `checkForUpdateInformation()` needs `SPUUpdaterDelegate.updater(_:didFindValidUpdate:)`/`updaterDidNotFindUpdate` (guard against recursion when a real session triggers the same delegate). Consider a status-item badge when an update is available.
2. **M Dock icon click after closing editor does nothing** — implement `applicationShouldHandleReopen` → `showEditor()` (`App.swift:459-462` hides via `orderOut`).
3. **M Launch-at-login: reconcile with `SMAppService.mainApp.status` at init; don't persist success when `apply` was a silent no-op** (`LaunchAtLogin.swift:9-17`, `AppSettings.swift:70-78`).
4. **L What's New shows empty "v[Unreleased]"** — filter empty sections (`Settings.swift:70`).
5. **L Two shortcut recorders can arm simultaneously** (`ShortcutRecorderView.swift:64-81`); only first registration failure surfaced (`App.swift:157-165`).
6. **L First-run: denied launch prompt → first hotkey press silent** (`App.swift:331-338`).
7. **L German overflow risks:** Settings pickers fixed at 220 pt in non-resizable 640×420 window; `DMTooltip` clamp assumes ~88 pt bubble; editor toolbar overflow is silent (`ScrollView` without indicator). Quick-Edit toolbar jumps when flyout opens (`QuickEditOverlay.swift:45-54` measures flyout into layout).
8. **L HotkeyManager: remove Carbon handler in deinit / drop `passUnretained` footgun** (`HotkeyManager.swift:49-65`).
9. **L History: don't index entries whose PNG write failed** (`HistoryStore.swift:56-63`); surface save errors instead of `try?` in GIF save panel (`GIFViewerWindow.swift:74-76`).

---

## Suggested order & effort

1. ~~Phase 1~~ done (this branch) — mac tested, win needs a Windows build + smoke test.
2. Phase 2 (editor perf) — biggest daily-feel win; ~1 day; mac-only internals.
3. Phase 3.1–3.3 (recording robustness) — user-visible reliability; ~1 day; recording behavior needs on-device verification.
4. Phase 4 (undo/crop correctness) — 🍎🪟 in one change each, per parity contract.
5. Phase 5 (win perf) — needs a Windows machine for verification.
6. Phase 6 — mostly small code changes, but each needs a product decision (fix vs document); update PARITY.md in the same change.
7. Phase 7 — background polish, batch as convenient.
