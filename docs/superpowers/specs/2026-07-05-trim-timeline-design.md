# Trim Timeline for the Video/GIF Preview — Design

**Date:** 2026-07-05
**Status:** Approved
**Platforms:** macOS (source of truth) + Windows (mirrored, on-device verification deferred)

## Problem

The "Preview & Trim" window plays the whole recording in a loop regardless of the
Start/End trim sliders — the sliders only affect the GIF export. The user cannot
see whether the trim is right ("man kann es nur erahnen"), there is no playback
time readout, and two separate full-width sliders for one range feel clumsy.

## Goals

1. The preview loops **only the kept range** [start, end].
2. Dragging a trim handle scrubs the video to that exact frame (live feedback).
3. A time readout shows playback position **relative to the trim range**.
4. Replace the two separate sliders with **one timeline** with two handles
   (QuickTime-style) and a playhead marker.

Non-goals: filmstrip thumbnails inside the track (deliberate YAGNI; can be added
later), any change to the GIF export pipeline (`GIFRenderer`/`GIFPlan` untouched,
still driven by `state.start`/`state.end`).

## UI

Replaces the `Start …s [slider]  End …s [slider]` row in the preview window:

- One horizontal **timeline track** spanning the full video duration: dark gray
  base track; the kept range between the handles is highlighted in the DM accent
  color.
- **Two drag handles** (start / end), rounded, hit-target ≥ 24 pt. Handles clamp
  so `start ≤ end − 0.1 s`; they can never cross (the previous "Create GIF
  disabled because end ≤ start" state disappears).
- A thin light **playhead line** moves through the kept range during playback.
  Clicking/dragging on the track between the handles scrubs the playhead
  directly.
- Below the timeline: `Start 0,3s` (left-aligned) and `Ende 4,2s` (right-aligned)
  using the existing localized `startLabel`/`endLabel` keys. Total kept length +
  estimated GIF size stay in the bottom row as today.
- **Time pill** overlaid top-right on the video: `1,4s / 2,7s` = (current −
  start) / (end − start), clamped to the range. Numeric only — no new
  localization keys.

## Behavior

- **Range loop:** a periodic time observer (~30 Hz) watches the player; when
  `current ≥ end`, seek back to `start` (tolerance zero) and keep playing. The
  existing `AVPlayerItemDidPlayToEndTime` observer seeks to `start` instead of
  `.zero` (covers end == full duration).
- **Handle scrub:** while a handle drags, pause the player and seek (tolerance
  zero) to the handle's time — start handle shows the first kept frame, end
  handle the last. On release, seek to `start` and resume playing.
- **Playhead scrub:** dragging on the track seeks within [start, end]; playback
  resumes from there on release.
- Trim values continue to live in `PreviewState`; `GIFRenderer.render(asset:
  start:end:)` is called unchanged.

## Architecture

- **`TrimTimeline.swift` (pure logic, no UI):** time↔x-position mapping for a
  given track width, handle clamping (`minGap = 0.1 s`), loop decision
  (`shouldLoopBack(current:end:)`), display-time mapping (relative position and
  range length). Unit-tested like `GIFPlan`/`LoupeMath`.
- **`TrimTimelineView` (SwiftUI):** thin view over `TrimTimeline`, owns only
  drag-gesture state; reports `onScrub(time:, kind:)` / `onRelease()` events.
  Lives in `VideoPreviewWindow.swift` (file stays focused; extract only if it
  grows).
- **`VideoPreviewWindow`:** adds the periodic time observer (removed in
  `teardown()` alongside the loop observer — same lifecycle discipline that
  fixed the earlier dealloc crash), publishes `current` into `PreviewState`,
  handles scrub/release by pausing/seeking/resuming.

## Windows parity

Same behavior in `windows/`: a WPF control (track + two Thumbs + playhead line)
replacing the two sliders in its preview window, a `DispatcherTimer`-driven
position watch on the `MediaElement` for the range loop, pause+`Position` set
while a Thumb drags, and the same time pill. Shared logic mirrored in a testable
static class with `LocTests`-style unit tests where practical. Committed
**unverified** (no Windows build available here) and flagged for the next
on-device Windows session in `docs/PARITY.md`.

## Testing

- `TrimTimelineTests` (mac): mapping round-trips, clamping (cross-over, edges,
  min gap), loop decision boundaries, display-time clamping.
- Existing 161 tests stay green; GIF export path untouched.
- Manual (user): trim start/end → preview loops exactly the kept range; handle
  drag shows the boundary frame; time pill matches; Create GIF produces the same
  cut as before.
