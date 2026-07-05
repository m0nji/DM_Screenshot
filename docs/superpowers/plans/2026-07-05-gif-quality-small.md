# GIF Quality "Small" Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Second GIF quality level (Small = 5 fps / 800 px) selectable at creation, plus a one-way post-hoc Standard→Small conversion in the GIF viewer that replaces the history entry.

**Architecture:** `GIFQuality` constants in the plan layer; pure `GIFResample` for the 5-fps regrid (unit-tested); decode/scale/encode composed from existing `CGImageSource`/`ImageUtils.scaled`/`GIFEncoder` (mac) and ImageSharp/`GifEncoder` (win); `HistoryStore.updateVideo` overwrites in place; viewer gets a conditional Convert button wired by the app layer.

**Tech Stack:** Swift/SwiftUI/ImageIO (mac), C#/WPF/ImageSharp (win).

**Spec:** `docs/superpowers/specs/2026-07-05-gif-quality-small-design.md`

## Global Constraints

- Branch `feat/gif-quality-small` (created). Standard output stays byte-identical.
- Small = 5 fps / max 800 px; selection not persisted; convert button only when GIF width > 800 px.
- New loc keys both languages both platforms: `gifQualityStandard`, `gifQualitySmall`, `gifConvertToSmall`. Reuse `creatingGIF`.
- mac: `swift test` green before each commit; app builds via `DMSHOT_SIGN_ID="Developer ID Application: Thomas Schwabe (FLG4M553XP)" ./build_app.sh release` (NEVER without the env var). Windows committed unverified + PARITY note.

---

### Task 1 (mac): `GIFQuality` + estimate honors quality — TDD
Files: `mac/Sources/DMShot/GIFPlan.swift`, `mac/Tests/DMShotTests/GIFPlanTests.swift` (extend), `mac/Sources/DMShot/VideoPreviewWindow.swift` (`PreviewState.estimatedBytes`, `GIFRenderer.render` quality param).
- [ ] Test: `GIFQuality.standard` = (10, 1000), `.small` = (5, 800); `frameTimes(duration:fps:5)` count ≈ half; `scaledSize(maxWidth: 800)` caps at 800.
- [ ] Add `enum GIFQuality { case standard, small; var fps: Double; var maxWidth: Int }` to GIFPlan.swift; thread `quality` through `GIFRenderer.render(asset:start:end:quality:)` (default `.standard`) and `PreviewState.estimatedBytes(for:)`.
- [ ] `swift test` green; commit.

### Task 2 (mac): quality picker in Preview & Trim
Files: `mac/Sources/DMShot/VideoPreviewWindow.swift`, `mac/Sources/DMShot/Localization.swift` (+`gifQualityStandard`/`gifQualitySmall`/`gifConvertToSmall`).
- [ ] `PreviewState.quality: GIFQuality = .standard` (@Published); segmented `Picker` in the bottom row before the size labels; estimate + `onCreate` use `state.quality`.
- [ ] Build + tests green; app build + relaunch; commit.

### Task 3 (mac): `GIFResample` pure regrid — TDD
Files: `mac/Sources/DMShot/GIFResample.swift`, `mac/Tests/DMShotTests/GIFResampleTests.swift`.
- [ ] Tests: uniform 0.1 s × 10 frames @5 fps → indices 0,2,4,6,8 each 0.2 s; one 0.1 s frame + 1.9 s held frame → [(0,0.2),(1,1.8)]-style span preservation (sum ≈ total, ±one tick); empty input → []; single frame → [(0, ≥0.2)]; ticks hitting the same source frame extend the previous delay (no duplicate indices in a row).
- [ ] Implement `static func resample(delays: [Double], targetFPS: Double) -> [(index: Int, delay: Double)]`: cumulative starts; ticks at `stride(from: 0, to: total, by: 1/targetFPS)`; per tick find the active source frame (last with start ≤ t); same-as-last extends the previous entry's delay by the tick length, else append `(index, tickLength)`.
- [ ] `swift test` green; commit.

### Task 4 (mac): converter + `HistoryStore.updateVideo` + viewer button
Files: `mac/Sources/DMShot/GIFResample.swift` (add `makeSmall(gifData:) async -> (data: Data, thumbnail: CGImage)?`), `mac/Sources/DMShot/HistoryStore.swift`, `mac/Sources/DMShot/GIFViewerWindow.swift`, `mac/Sources/DMShot/App.swift`.
- [ ] `makeSmall`: `CGImageSource` frames + unclamped GIF delays → skip if width ≤ 800 (return nil upstream guard) → `GIFResample.resample(delays:targetFPS:5)` → `ImageUtils.scaled(_:toWidth:800)` per kept frame → `GIFEncoder.encode(frames:delays:)`; thumbnail = first kept frame.
- [ ] `HistoryStore.updateVideo(id:gifData:thumbnail:)`: mirror `addVideo`'s pendingGIFs + ioQueue write, but NO insert/evict (entry keeps id/position).
- [ ] `GIFViewerWindow.show(gifData:title:onConvert:)`: Convert button (`gifConvertToSmall`) visible only when the GIF's pixel width > 800; while converting disable + show `creatingGIF` label (NSProgressIndicator spinning); on success re-show with new data (button disappears via width check), on nil result re-enable silently.
- [ ] `App.swift`: in `deliverGIF` and `loadHistory`, pass `onConvert` that calls `GIFResample.makeSmall`, then `history.updateVideo`, clipboard via `ImageUtils.copyGIF`, and hands the new data back to the viewer.
- [ ] Build + full tests + app build + relaunch; commit.

### Task 5 (mac, USER): manual check
- [ ] Preview: picker defaults Standard; Small shrinks the estimate; created Small GIF is smaller/coarser.
- [ ] Viewer (new GIF + history GIF > 800 px): Convert button shows, converts with feedback, entry replaced (sidebar thumb updates), clipboard has small GIF, button gone afterwards; ≤ 800 px GIFs show no button.

### Task 6 (win): mirror — committed unverified
Files: `windows/DMShot/Video/GifPlan.cs` (GifQuality), `windows/DMShot/Video/GifResample.cs` + `windows/DMShot.Tests/GifResampleTests.cs`, `windows/DMShot/Video/VideoPreviewWindow.xaml(.cs)` (quality picker + estimate + render param), `windows/DMShot/Video/GifRenderer.cs`, `windows/DMShot/Video/GifViewerWindow.xaml(.cs)` (convert button), `windows/DMShot/History/HistoryStore*.cs` (UpdateVideo), `windows/DMShot/App.xaml.cs` (wiring), `windows/DMShot/Localization/Loc.cs` (3 keys × 2 languages).
- [ ] Port 1:1 from mac (read the mac diff first); ImageSharp: `image.Frames[i].Metadata.GetGifMetadata().FrameDelay` (1/100 s units).
- [ ] Commit with UNVERIFIED marker.

### Task 7: PARITY note + wrap-up
- [ ] Extend the existing 2026-07-05 trim-timeline TODO block in `docs/PARITY.md` (or add sibling) with the quality/convert feature for the same on-device session.
- [ ] Full mac tests; commit; merge gate = Task 5 user OK.
