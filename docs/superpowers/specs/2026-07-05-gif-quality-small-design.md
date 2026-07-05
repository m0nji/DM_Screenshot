# GIF Quality Level "Small" — Design

**Date:** 2026-07-05
**Status:** Approved
**Platforms:** macOS (source of truth) + Windows (mirrored, on-device verification deferred)

## Problem

GIFs of longer/moving recordings get large (~12 MB for 20 s). The only remedy
today is trimming. Users want a smaller output option — at creation time and,
because clips shouldn't be kept twice, as a one-way post-hoc downgrade of an
already-created GIF.

## Goals

1. Two quality levels: **Standard** = today's 10 fps / max 1000 px (unchanged
   default), **Small** = 5 fps / max 800 px.
2. Selectable in the "Preview & Trim" window; the size estimate follows the
   selection live. Selection is NOT persisted — every preview starts at Standard.
3. Post-hoc downgrade in the GIF viewer: a "Convert to Small" button re-encodes
   the stored GIF, **replaces** the history entry, updates the clipboard, and
   shows the small GIF in place. Original is not kept (that's the point).
4. One-way only: the button is hidden when the GIF is already ≤ 800 px wide
   (including right after a conversion).

Non-goals: arbitrary quality settings, persisting the choice, upscaling,
converting screenshot (non-video) entries.

## Behavior

- **Creation:** segmented control `Standard | Small` next to the estimated-size
  label. Estimate = `GIFPlan.estimatedBytes` with the selected level's fps
  (frame count) and max width (scaled size). "Create GIF" renders with the
  selected level.
- **Post-hoc:** the conversion decodes the stored GIF's frames + per-frame
  delays, scales frames to ≤ 800 px, resamples the timeline onto the 5 fps grid
  (0.2 s ticks; consecutive ticks that hit the same source frame extend one
  delay instead of duplicating the frame — static runs stay single frames), and
  re-encodes. On success: history entry's GIF + thumbnail replaced, clipboard
  updated, viewer swaps to the small GIF, button disappears. On failure: nothing
  changes (original untouched), button stays.
- Conversion runs off the main thread; the viewer shows the same
  "Creating GIF…" style feedback (button disabled + progress) while it runs.

## Architecture

- **`GIFQuality`** (mac: in `GIFPlan.swift`; win: in `GifPlan.cs`): two cases
  with `fps` and `maxWidth`. `GIFRenderer.render` (mac) / the win render path
  take a quality parameter; Standard reproduces today's output byte-for-byte.
- **`GIFResample`** (mac: new `GIFResample.swift`; win: new
  `GifResample.cs`): pure, unit-tested resampling —
  `resample(delays: [Double], targetFPS: Double) -> [(index: Int, delay: Double)]`.
  Decode/encode stay in platform code: mac `CGImageSource` (unclamped GIF delay)
  + existing `GIFEncoder.encode(frames:delays:)` / `ImageUtils.scaled`;
  win ImageSharp frames + existing `GifEncoder`.
- **`HistoryStore.updateVideo(id:gifData:thumbnail:)`** (both platforms):
  overwrite the entry's `.gif` + thumbnail in place (same id, same position, no
  re-insert).
- **GIF viewer** (mac `GIFViewerWindow`, win `GifViewerWindow`): gains the
  conditional Convert button and an `onConvert` callback; the app layer
  (mac `App.swift` `deliverGIF`/`loadHistory`, win `App.xaml.cs`) wires it to
  the converter + history update + clipboard.

## Localization

New keys, both languages, both platforms: quality names
(`gifQualityStandard` = "Standard"/"Standard", `gifQualitySmall` =
"Small"/"Klein") and the viewer action (`gifConvertToSmall` =
"Convert to Small"/"In Klein umwandeln"). The existing `creatingGIF` key is
reused for conversion feedback.

## Testing

- Mac + win unit tests for `GIFResample`/`GifResample`: 10 fps→5 fps drops
  every other uniform frame; extended (deduped) delays keep their span; static
  tail collapses; ≥ 1 frame out for any non-empty input; degenerate inputs safe.
- `GIFPlan` estimate honors quality (frame count halves, size shrinks).
- Existing suites stay green; Standard path byte-identical (no test churn).
- Manual (user): create Small GIF (smaller file, visibly coarser), convert an
  existing Standard GIF (entry replaced, clipboard small, button gone).
