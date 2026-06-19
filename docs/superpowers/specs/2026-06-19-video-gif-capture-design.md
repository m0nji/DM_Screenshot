# Design — Video/GIF Capture (Spec 1)

Date: 2026-06-19
Status: Approved (brainstorming), pending implementation plan
Platforms: macOS (source of truth) + Windows (parity, deferred via TODO)

## Goal

Let the user record short screen videos — full screen or a selected region — and
turn them into **animated GIFs** that can be pasted directly into a chat or email
as small "how-to" clips that play inline. This sits alongside the existing
screenshot capture (full screen / area).

Primary use case: short instructional GIFs pasted into **Microsoft Teams** and
**Outlook** (and ideally **Telegram**).

## Non-goals

- No audio (GIF has none).
- No annotation/editing of video (the annotation editor is image-only).
- No long recordings: **60 s is a hard cap**, not a target. Realistic
  paste-into-email clips are ~10–20 s.
- No live-GIF-during-recording and no `.mov`/`.mp4` export to the user in v1
  ("save as video" is explicitly deferred — YAGNI).

## Platform-neutral capture pipeline (the parity contract)

The pipeline is defined as **abstract steps**. Steps 1, 4, 5, 6, 7, 8 are
**behaviorally binding and must be identical** across macOS and Windows. Only
steps 2 + 3 (frame API + intermediate container) are platform-specific. Document
every step so Windows can mirror it without reverse-engineering the macOS code —
note in particular that the macOS `.mov` intermediate is just the macOS *binding*
of step 3, not part of the contract; Windows will most likely use `.mp4` (Media
Foundation).

| # | Step (platform-neutral contract) | macOS binding | Windows binding (planned) |
|---|---|---|---|
| 1 | **Choose source**: whole display *or* a pixel rect (top-left origin) | `SCContentFilter` (display + crop rect) | `GraphicsCaptureItem` + crop |
| 2 | **Acquire frames** (raw frames + timestamps) | `SCStream` delegate (CMSampleBuffer) | `Direct3D11CaptureFramePool` |
| 3 | **Write intermediate video** (lossless-ish temp container) | `AVAssetWriter` → temp `.mov` | `SinkWriter` (Media Foundation) → temp `.mp4` |
| 4 | **Recording control**: start, live timer, stop, auto-stop @60 s, cancel | floating control + tray + Esc | same UX |
| 5 | **Preview + trim** (in/out points over the temp video) | `AVPlayer` + range | `MediaElement` + range |
| 6 | **GIF encode** from trimmed range: downscale to ≤1000 px width, decimate to ~10 fps, optimize palette + inter-frame transparency | ImageIO `CGImageDestination` (`com.compuserve.gif`) | GIF encoder (lib TBD) |
| 7 | **Deliver**: clipboard (GIF data + temp file URL) + history | `NSPasteboard` | `Clipboard`/`IDataObject` |
| 8 | **Store in history** as a video/GIF entry (.gif + first-frame thumbnail) | `HistoryStore` | `History/HistoryStore.cs` |

### Why `.mov` on macOS but not necessarily on Windows

`SCRecordingOutput` (direct-to-file) requires macOS 15; our minimum is macOS 14,
so we drive `AVAssetWriter` from `SCStream` frames into a temp `.mov`. The `.mov`
exists only to (a) keep memory bounded (no 60 s of raw frames in RAM) and (b)
give us an `AVPlayer`-backed preview + trim for free. Windows should pick whatever
intermediate is natural there (`.mp4` via Media Foundation `SinkWriter`); the
container choice is an implementation detail of step 3.

## Components (macOS)

- **`ShortcutAction`** (extend `Shortcuts.swift`): add `.videoFullScreen`,
  `.videoAreaSelection`.
  - Defaults: `⌘⌃1` (video full screen, keyCode `0x12`, mods `cmd|control`) and
    `⌘⌃2` (video area, keyCode `0x13`, mods `cmd|control`). Mnemonic: same number
    as the screenshot, **Control instead of Shift = video**. Conflict-free
    (`⌘⇧3/4/5` are macOS system screenshots and are avoided). User-editable and
    persisted like the existing shortcuts.
- **`VideoRecorder`** (new): owns steps 1–3. Starts an `SCStream` for the chosen
  source, writes frames into a temp `.mov` via `AVAssetWriter`, exposes
  `start`, `stop`, `cancel`, elapsed-time publishing, and the resulting temp URL.
- **`RecordingControlWindow`** (new): borderless floating panel (step 4),
  bottom-center of the active screen. Shows a red dot, **live mm:ss timer**, and a
  **Stop** button. Last 10 s before the 60 s auto-stop are color-highlighted.
- **`VideoPreviewWindow`** (new): `NSWindow` with `AVPlayer` (play/pause/loop), a
  **trim bar** with start/end handles, a live **estimated GIF size** readout, and
  buttons **"Create GIF"** (primary), **"Discard"** (step 5).
- **`GIFEncoder`** (new): step 6. Decimates the trimmed range to the target fps,
  downscales each frame to ≤1000 px width, writes incrementally via
  `CGImageDestination` with per-frame delay and infinite loop. Implements
  **inter-frame optimization** (unchanged pixels made transparent with
  appropriate disposal so LZW collapses static regions — the dominant size
  lever). Exact mechanism (ImageIO transparency/disposal vs. a small GIF library)
  is validated during implementation.
- **`App.swift`**: two new sidebar buttons ("Video Full Screen" / "Video
  Section"), two tray menu items with shortcut display, hotkey wiring, and the
  capture entry points (`captureVideoFull`, `captureVideoArea`). Area video
  reuses the existing `Overlay.swift` selection to pick the rect, then starts
  recording on that rect.
- **`ImageUtils` / clipboard**: a `copyGIFToClipboard(data:, fileURL:)` that
  writes **both** `com.compuserve.gif` data **and** a `.fileURL` to a temp `.gif`.
- **`HistoryStore.swift`**: `HistoryItemMeta` gains `kind: image | video`
  (backward compatible: missing ⇒ `image`). Video entries store the `.gif` + a
  first-frame thumbnail; a small play/GIF badge marks them in the sidebar.

## Recording & stop UX (step 4)

- Trigger video-area → existing selection overlay to drag the rect; video-full →
  start immediately.
- Floating control shows red dot + live timer + Stop. Tray icon switches to a
  "recording" state.
- Stop via: Stop button, pressing the video shortcut again, or tray click.
- **Esc cancels** (discards the recording).
- **Auto-stop at 60 s** (hard cap); the control counts down and highlights the
  last 10 s.

## Preview & trim (step 5)

- After stop, the preview window opens with the temp video.
- `AVPlayer` with play/pause + loop.
- Trim bar with start/end handles over a timeline; shows selected duration.
- **"Create GIF"** → steps 6+7. **"Discard"** → delete temp video, close.
- Live **estimated GIF size** for the selected range (rough heuristic from
  duration × fps × area) so the user can trim before encoding — important because
  a 60 s clip will far exceed email-friendly sizes.

## GIF encoding, clipboard & paste (steps 6–7)

- Encoding defaults (fixed, no quality UI): **~10 fps**, **≤1000 px width**,
  optimized palette + **inter-frame transparency** optimization, infinite loop,
  incremental write to bound memory.
- **Size reality (informational):** GIF size scales ~linearly with
  frames × pixel area; the dominant levers are duration (trim), resolution, and
  inter-frame optimization — fps is a smaller, linear lever. A 60 s ~1000 px
  full-screen GIF will be tens of MB regardless of 7 vs 12 fps; ~10 MB is
  realistic for ~10–20 s clips. The trim step + size estimate exist to steer
  users to short clips. 10 fps chosen as the smooth-enough default (7 fps looks
  choppy on cursor moves; 12–15 fps adds frames without instructional benefit).
- **Clipboard:** put **two** representations on the pasteboard simultaneously:
  1. GIF data (`com.compuserve.gif`) — many web/rich editors animate this.
  2. A `.fileURL` to a temp `.gif` — Mail/Outlook-style apps insert the file,
     which then animates.
- **Reality:** whether a pasted GIF *animates* is decided by the target app, not
  by us. Some apps grab only a still frame.

### Verification checklist (paste targets)

- [ ] **Microsoft Teams** — pasted GIF animates inline. (required)
- [ ] **Outlook** — pasted GIF animates in a composed mail. (required)
- [ ] **Telegram** — pasted GIF animates. (optional)
- Fallback if any target only yields a still frame: **drag-and-drop the `.gif`
  from history** and **"Save as…"** — both are built in.

## History & parity (step 8)

- `HistoryItemMeta.kind` (`image | video`), backward compatible.
- Video entry stores `.gif` + first-frame thumbnail; sidebar shows a play/GIF
  badge.
- Clicking a video history entry: re-copy the GIF to the clipboard + open the
  preview window (no annotation editor for video). Drag from the sidebar = the
  `.gif` file.
- History limit stays **10** (mixed image + video).
- `PARITY.md`: new "Video/GIF capture" feature row (macOS files + Windows
  `TODO`), the step 1–8 contract table, and the two new default shortcuts in the
  hotkeys row.

## Testing

- Unit: shortcut defaults/persistence for the two new actions; GIF encoder
  decimation + downscale math; estimated-size heuristic; history `kind`
  round-trip and backward compatibility (old entries load as `image`).
- Manual: full-screen + area recording, stop paths (button/shortcut/tray),
  Esc-cancel, 60 s auto-stop, trim, the paste verification checklist above.

## Open implementation risks (validate during build)

- Inter-frame GIF optimization mechanism via ImageIO (transparency + disposal)
  vs. a small GIF library — pick whichever actually shrinks output and animates
  correctly in the target apps.
- Animated-paste behavior in Teams/Outlook/Telegram (the verification checklist
  is the gate).
