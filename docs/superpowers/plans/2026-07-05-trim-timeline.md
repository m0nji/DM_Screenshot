# Trim Timeline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The video/GIF preview loops only the trimmed range, trim handles live-scrub the video, and one QuickTime-style timeline (two handles + playhead) replaces the separate sliders — on macOS and Windows.

**Architecture:** Pure range/mapping math lives in a UI-free helper (`TrimTimeline` enum on mac, `TrimTimelineMath` static class on win) with unit tests. The mac SwiftUI view and win code-behind stay thin and call into it. Playback: mac adds an `AVPlayer` periodic time observer for the range loop; win already loops in range (`Advance()`), it only swaps three sliders for one timeline.

**Tech Stack:** Swift/SwiftUI/AVFoundation (mac), C#/WPF (win), XCTest + xUnit-style MSTest already in repo.

**Spec:** `docs/superpowers/specs/2026-07-05-trim-timeline-design.md`

## Global Constraints

- Branch: `feat/trim-timeline` (already created; spec committed).
- Min handle gap: `0.1` s; handles can never cross.
- Time pill format: `%.1fs / %.1fs` = (current − start) clamped to range / (end − start). Numeric only — NO new localization keys on either platform.
- GIF export pipeline untouched: `GIFRenderer.render(asset:start:end:)` (mac) and `CreateGifRequested?.Invoke(_trimStart, _trimEnd)` (win) keep their signatures.
- macOS: `cd mac && swift test` green before every commit. Windows cannot be built here — win tasks are committed unverified and flagged in `docs/PARITY.md`.
- Localization: existing keys `startLabel`/`endLabel` (mac), `videoTrimIn`/`videoTrimOut` (win) are reused; the win `videoPlayhead` key becomes unused but STAYS defined in both languages (LocTests key parity).

---

### Task 1: `TrimTimeline` pure math (mac) — TDD

**Files:**
- Create: `mac/Sources/DMShot/TrimTimeline.swift`
- Test: `mac/Tests/DMShotTests/TrimTimelineTests.swift`

**Interfaces:**
- Produces (used by Tasks 2–3):
  - `TrimTimeline.minGap: Double` (= 0.1)
  - `TrimTimeline.xPosition(time: Double, duration: Double, width: CGFloat) -> CGFloat`
  - `TrimTimeline.time(atX: CGFloat, duration: Double, width: CGFloat) -> Double`
  - `TrimTimeline.clampedStart(_ proposed: Double, end: Double) -> Double`
  - `TrimTimeline.clampedEnd(_ proposed: Double, start: Double, duration: Double) -> Double`
  - `TrimTimeline.clampedPlayhead(_ t: Double, start: Double, end: Double) -> Double`
  - `TrimTimeline.shouldLoopBack(current: Double, end: Double) -> Bool`
  - `TrimTimeline.displayTime(current: Double, start: Double, end: Double) -> (elapsed: Double, total: Double)`

- [ ] **Step 1: Write the failing tests**

`mac/Tests/DMShotTests/TrimTimelineTests.swift`:

```swift
import XCTest
@testable import DMShot

final class TrimTimelineTests: XCTestCase {
    // Mapping
    func testXPositionMapsLinearly() {
        XCTAssertEqual(TrimTimeline.xPosition(time: 0, duration: 10, width: 200), 0)
        XCTAssertEqual(TrimTimeline.xPosition(time: 5, duration: 10, width: 200), 100)
        XCTAssertEqual(TrimTimeline.xPosition(time: 10, duration: 10, width: 200), 200)
    }
    func testXPositionClampsOutOfRange() {
        XCTAssertEqual(TrimTimeline.xPosition(time: -1, duration: 10, width: 200), 0)
        XCTAssertEqual(TrimTimeline.xPosition(time: 11, duration: 10, width: 200), 200)
    }
    func testXPositionZeroDurationOrWidthIsSafe() {
        XCTAssertEqual(TrimTimeline.xPosition(time: 3, duration: 0, width: 200), 0)
        XCTAssertEqual(TrimTimeline.xPosition(time: 3, duration: 10, width: 0), 0)
    }
    func testTimeAtXRoundTrips() {
        let t = TrimTimeline.time(atX: 100, duration: 10, width: 200)
        XCTAssertEqual(t, 5, accuracy: 1e-9)
        XCTAssertEqual(TrimTimeline.time(atX: -5, duration: 10, width: 200), 0)
        XCTAssertEqual(TrimTimeline.time(atX: 500, duration: 10, width: 200), 10)
        XCTAssertEqual(TrimTimeline.time(atX: 100, duration: 0, width: 200), 0)
    }
    // Handle clamping
    func testClampedStartRespectsMinGapAndZero() {
        XCTAssertEqual(TrimTimeline.clampedStart(-2, end: 5), 0)
        XCTAssertEqual(TrimTimeline.clampedStart(3, end: 5), 3)
        XCTAssertEqual(TrimTimeline.clampedStart(4.99, end: 5), 4.9, accuracy: 1e-9)
        XCTAssertEqual(TrimTimeline.clampedStart(7, end: 5), 4.9, accuracy: 1e-9)
    }
    func testClampedEndRespectsMinGapAndDuration() {
        XCTAssertEqual(TrimTimeline.clampedEnd(12, start: 2, duration: 10), 10)
        XCTAssertEqual(TrimTimeline.clampedEnd(6, start: 2, duration: 10), 6)
        XCTAssertEqual(TrimTimeline.clampedEnd(2.01, start: 2, duration: 10), 2.1, accuracy: 1e-9)
        XCTAssertEqual(TrimTimeline.clampedEnd(-1, start: 2, duration: 10), 2.1, accuracy: 1e-9)
    }
    func testClampsDegenerateShortClip() {
        // duration shorter than minGap must not produce start > end or NaN
        let start = TrimTimeline.clampedStart(0.04, end: 0.05)
        let end = TrimTimeline.clampedEnd(0.01, start: 0, duration: 0.05)
        XCTAssertGreaterThanOrEqual(start, 0)
        XCTAssertLessThanOrEqual(start, 0.05)
        XCTAssertEqual(end, 0.05, accuracy: 1e-9)
    }
    // Playhead + loop
    func testClampedPlayheadStaysInRange() {
        XCTAssertEqual(TrimTimeline.clampedPlayhead(1, start: 2, end: 5), 2)
        XCTAssertEqual(TrimTimeline.clampedPlayhead(3, start: 2, end: 5), 3)
        XCTAssertEqual(TrimTimeline.clampedPlayhead(9, start: 2, end: 5), 5)
    }
    func testShouldLoopBackAtOrPastEnd() {
        XCTAssertFalse(TrimTimeline.shouldLoopBack(current: 4.9, end: 5))
        XCTAssertTrue(TrimTimeline.shouldLoopBack(current: 5, end: 5))
        XCTAssertTrue(TrimTimeline.shouldLoopBack(current: 6, end: 5))
    }
    // Display
    func testDisplayTimeRelativeToRange() {
        let d = TrimTimeline.displayTime(current: 3.4, start: 2, end: 5)
        XCTAssertEqual(d.elapsed, 1.4, accuracy: 1e-9)
        XCTAssertEqual(d.total, 3.0, accuracy: 1e-9)
    }
    func testDisplayTimeClampsOutsideRange() {
        XCTAssertEqual(TrimTimeline.displayTime(current: 0, start: 2, end: 5).elapsed, 0)
        XCTAssertEqual(TrimTimeline.displayTime(current: 9, start: 2, end: 5).elapsed, 3, accuracy: 1e-9)
        XCTAssertEqual(TrimTimeline.displayTime(current: 3, start: 5, end: 5).total, 0)
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd mac && swift test --filter TrimTimelineTests 2>&1 | tail -5`
Expected: compile error `cannot find 'TrimTimeline' in scope`.

- [ ] **Step 3: Write the implementation**

`mac/Sources/DMShot/TrimTimeline.swift`:

```swift
import CoreGraphics

/// Pure math for the preview's trim timeline (spec 2026-07-05): time↔track-x
/// mapping, handle clamping, range-loop decision, and the relative time pill.
/// No UI/AVFoundation here so it stays unit-testable (like GIFPlan/LoupeMath).
enum TrimTimeline {
    /// Handles may never come closer than this (seconds); also the smallest
    /// exportable range, so "Create GIF disabled by crossed sliders" is gone.
    static let minGap = 0.1

    /// Track x for a time. Clamped; safe for zero duration/width.
    static func xPosition(time: Double, duration: Double, width: CGFloat) -> CGFloat {
        guard duration > 0, width > 0 else { return 0 }
        return CGFloat(min(max(time / duration, 0), 1)) * width
    }

    /// Time for a track x. Clamped; safe for zero duration/width.
    static func time(atX x: CGFloat, duration: Double, width: CGFloat) -> Double {
        guard duration > 0, width > 0 else { return 0 }
        return min(max(Double(x / width), 0), 1) * duration
    }

    /// Start-handle drag: within [0, end − minGap].
    static func clampedStart(_ proposed: Double, end: Double) -> Double {
        min(max(proposed, 0), max(0, end - minGap))
    }

    /// End-handle drag: within [start + minGap, duration] (degenerates safely
    /// when the whole clip is shorter than minGap).
    static func clampedEnd(_ proposed: Double, start: Double, duration: Double) -> Double {
        max(min(proposed, duration), min(duration, start + minGap))
    }

    /// Playhead confined to the kept range.
    static func clampedPlayhead(_ t: Double, start: Double, end: Double) -> Double {
        min(max(t, start), end)
    }

    /// Range loop: jump back once the playhead reaches the trim end.
    static func shouldLoopBack(current: Double, end: Double) -> Bool {
        current >= end
    }

    /// Time pill: elapsed-within-range (clamped) and range length.
    static func displayTime(current: Double, start: Double, end: Double)
        -> (elapsed: Double, total: Double) {
        let total = max(0, end - start)
        let elapsed = min(max(current - start, 0), total)
        return (elapsed, total)
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd mac && swift test --filter TrimTimelineTests 2>&1 | tail -3`
Expected: all TrimTimelineTests PASS. Then run the full suite once: `swift test 2>&1 | grep "Executed"` — expected 161 + new = all green.

- [ ] **Step 5: Commit**

```bash
git add mac/Sources/DMShot/TrimTimeline.swift mac/Tests/DMShotTests/TrimTimelineTests.swift
git commit -m "feat(mac): TrimTimeline pure math for the preview trim timeline

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: Range-looping playback + time observer (mac)

**Files:**
- Modify: `mac/Sources/DMShot/VideoPreviewWindow.swift` (class `VideoPreviewWindow`, `PreviewState`)

**Interfaces:**
- Consumes: `TrimTimeline.shouldLoopBack`, `TrimTimeline.clampedPlayhead` (Task 1).
- Produces (used by Task 3):
  - `PreviewState.current: Double` (`@Published`) — playhead seconds (asset time).
  - `VideoPreviewWindow.scrub(to: Double)` — pause + tolerance-zero seek (private method, wired into the view via closures in Task 3).
  - `VideoPreviewWindow.endScrub(returnToStart: Bool)` — resume playback.

No unit test possible (AVPlayer + windows); correctness is covered by Task 1's
logic tests plus the manual checklist in Task 4. Keep the diff exactly this:

- [ ] **Step 1: Add `current` to `PreviewState`**

In `PreviewState` (below `@Published var rendering = false`):

```swift
    /// Playhead position in asset seconds (driven by the periodic time observer).
    @Published var current: Double = 0
```

- [ ] **Step 2: Range-loop the player**

In `VideoPreviewWindow`, add fields next to `loopObserver`:

```swift
    private var timeObserver: Any?
    private var state: PreviewState?
    /// True while a timeline handle/playhead drag owns the player position —
    /// suppresses the range-loop seek so it can't fight the user's scrub.
    private var isScrubbing = false
```

In `show()`, the loop observer currently seeks to `.zero`. It is registered
before `state` exists; move its registration INTO the `Task { @MainActor in … }`
block (right after `let state = PreviewState(…)`; also set `self.state = state`)
and seek to the trim start instead:

```swift
            self.state = state
            // Loop within the kept range: end-of-file case…
            loopObserver = NotificationCenter.default.addObserver(
                forName: .AVPlayerItemDidPlayToEndTime, object: player.currentItem, queue: .main) { [weak state] _ in
                    let start = state?.start ?? 0
                    player.seek(to: CMTime(seconds: start, preferredTimescale: 600),
                                toleranceBefore: .zero, toleranceAfter: .zero)
                    player.play()
                }
            // …and mid-file case (trim end before the clip ends), plus playhead publishing.
            timeObserver = player.addPeriodicTimeObserver(
                forInterval: CMTime(value: 1, timescale: 30), queue: .main) { [weak self, weak state] time in
                    guard let self, let state else { return }
                    let t = CMTimeGetSeconds(time)
                    state.current = t
                    if !self.isScrubbing, TrimTimeline.shouldLoopBack(current: t, end: state.end),
                       state.end < state.duration || t >= state.duration {
                        player.seek(to: CMTime(seconds: state.start, preferredTimescale: 600),
                                    toleranceBefore: .zero, toleranceAfter: .zero)
                    }
                }
```

Delete the old `loopObserver = …` block (the one seeking to `.zero`) that runs
before the Task. `player.play()` before the Task stays.

Note the loop condition: when `state.end == state.duration` the mid-file seek
must not fire on every frame near the end (the end-of-file observer handles the
wrap); it only fires when the user actually trimmed the end, or as a fallback
when playback ran past the full duration.

- [ ] **Step 3: Scrub API**

Add to `VideoPreviewWindow` (below `show()`):

```swift
    /// A timeline drag owns the position: pause and show the exact frame.
    private func scrub(to time: Double) {
        isScrubbing = true
        player?.pause()
        player?.seek(to: CMTime(seconds: time, preferredTimescale: 600),
                     toleranceBefore: .zero, toleranceAfter: .zero)
        state?.current = time
    }

    /// Drag ended. Handle drags restart the loop at the trim start so the user
    /// immediately sees the kept range; playhead drags resume in place.
    private func endScrub(returnToStart: Bool) {
        isScrubbing = false
        if returnToStart, let state {
            player?.seek(to: CMTime(seconds: state.start, preferredTimescale: 600),
                         toleranceBefore: .zero, toleranceAfter: .zero)
            state.current = state.start
        }
        player?.play()
    }
```

- [ ] **Step 4: Teardown**

In `teardown()`, before `player?.pause()`:

```swift
        if let timeObserver { player?.removeTimeObserver(timeObserver) }
        timeObserver = nil
        state = nil
```

- [ ] **Step 5: Build + full tests**

Run: `cd mac && swift build 2>&1 | tail -2 && swift test 2>&1 | grep "Executed"`
Expected: build clean, all tests pass (Task 3 wires the UI; the app still shows
the old sliders at this point and must still compile).

- [ ] **Step 6: Commit**

```bash
git add mac/Sources/DMShot/VideoPreviewWindow.swift
git commit -m "feat(mac): preview loops only the trimmed range; scrub API + playhead publishing

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: TrimTimelineView + time pill (mac UI)

**Files:**
- Modify: `mac/Sources/DMShot/VideoPreviewWindow.swift` (`PreviewView`, new `TrimTimelineView`, wiring in `show()`)

**Interfaces:**
- Consumes: `TrimTimeline` mapping/clamping (Task 1), `PreviewState.current`, `scrub(to:)` / `endScrub(returnToStart:)` (Task 2).
- Produces: `TrimDragKind` enum + `TrimTimelineView` (private to this file).

- [ ] **Step 1: Add the timeline view**

Add above `PreviewView` in `VideoPreviewWindow.swift`:

```swift
private enum TrimDragKind { case start, end, playhead }

/// One QuickTime-style timeline: full-duration track, accent-highlighted kept
/// range, two drag handles, and a playhead line. All math in TrimTimeline.
private struct TrimTimelineView: View {
    @ObservedObject var state: PreviewState
    let onScrub: (Double, TrimDragKind) -> Void
    let onRelease: (TrimDragKind) -> Void

    @State private var drag: TrimDragKind?
    private let handleHitSlop: CGFloat = 12

    var body: some View {
        GeometryReader { geo in
            let w = geo.size.width
            let startX = TrimTimeline.xPosition(time: state.start, duration: state.duration, width: w)
            let endX = TrimTimeline.xPosition(time: state.end, duration: state.duration, width: w)
            let playX = TrimTimeline.xPosition(
                time: TrimTimeline.clampedPlayhead(state.current, start: state.start, end: state.end),
                duration: state.duration, width: w)
            ZStack(alignment: .leading) {
                Capsule().fill(.white.opacity(0.14)).frame(height: 6)
                Rectangle()
                    .fill(Color(nsColor: .dmAccent).opacity(0.55))
                    .frame(width: max(0, endX - startX), height: 6)
                    .offset(x: startX)
                Rectangle()                                     // playhead
                    .fill(.white.opacity(0.9))
                    .frame(width: 2, height: 18)
                    .offset(x: playX - 1)
                handle(at: startX)
                handle(at: endX)
            }
            .frame(maxHeight: .infinity, alignment: .center)
            .contentShape(Rectangle())
            .gesture(
                DragGesture(minimumDistance: 0)
                    .onChanged { g in
                        let kind = drag ?? nearestTarget(x: g.startLocation.x, startX: startX, endX: endX)
                        drag = kind
                        let t = TrimTimeline.time(atX: g.location.x, duration: state.duration, width: w)
                        switch kind {
                        case .start:
                            state.start = TrimTimeline.clampedStart(t, end: state.end)
                            onScrub(state.start, .start)
                        case .end:
                            state.end = TrimTimeline.clampedEnd(t, start: state.start, duration: state.duration)
                            onScrub(state.end, .end)
                        case .playhead:
                            let p = TrimTimeline.clampedPlayhead(t, start: state.start, end: state.end)
                            onScrub(p, .playhead)
                        }
                    }
                    .onEnded { _ in
                        if let kind = drag { onRelease(kind) }
                        drag = nil
                    })
        }
        .frame(height: 28)
    }

    private func nearestTarget(x: CGFloat, startX: CGFloat, endX: CGFloat) -> TrimDragKind {
        if abs(x - startX) <= handleHitSlop { return .start }
        if abs(x - endX) <= handleHitSlop { return .end }
        return .playhead
    }

    private func handle(at x: CGFloat) -> some View {
        RoundedRectangle(cornerRadius: 3)
            .fill(Color(nsColor: .dmAccent))
            .frame(width: 8, height: 22)
            .overlay(RoundedRectangle(cornerRadius: 3).stroke(.white.opacity(0.35), lineWidth: 1))
            .offset(x: x - 4)
    }
}
```

- [ ] **Step 2: Replace the slider row in `PreviewView` and add the pill**

`PreviewView` gains two closures and swaps its controls. Replace the whole
`var body` `VStack` content of `PreviewView` with:

```swift
    let onScrub: (Double, TrimDragKind) -> Void
    let onRelease: (TrimDragKind) -> Void
```

(added as stored properties after `onDiscard`), and the body:

```swift
        VStack(spacing: 12) {
            PlayerView(player: player)
                .frame(minWidth: 480, minHeight: 300)
                .overlay(alignment: .topTrailing) {
                    let d = TrimTimeline.displayTime(
                        current: state.current, start: state.start, end: state.end)
                    Text(String(format: "%.1fs / %.1fs", d.elapsed, d.total))
                        .font(.caption.monospacedDigit())
                        .foregroundStyle(.white)
                        .padding(.horizontal, 8).padding(.vertical, 3)
                        .background(Capsule().fill(.black.opacity(0.55)))
                        .padding(8)
                }
            TrimTimelineView(state: state, onScrub: onScrub, onRelease: onRelease)
            HStack {
                Text("\(tr(.startLabel)) \(String(format: "%.1f", state.start))s")
                Spacer()
                Text("\(tr(.endLabel)) \(String(format: "%.1f", state.end))s")
            }.font(.caption).foregroundStyle(.secondary)
            HStack {
                Group {
                    Text("\(String(format: "%.1f", max(0, state.end - state.start)))s")
                    Text(String(format: tr(.estimatedGIFSize), sizeLabel(state.estimatedBytes)))
                }
                .font(.caption)
                .foregroundStyle(.secondary)
                Spacer()
                Button(tr(.discard), action: onDiscard)
                Button(tr(.createGIF), action: onCreate)
                    .buttonStyle(AccentFilledButtonStyle())
                    .disabled(state.rendering || state.end <= state.start)
            }
        }
        .padding(16)
```

- [ ] **Step 3: Wire the closures in `show()`**

In `show()`, extend the `PreviewView(...)` call:

```swift
            let view = PreviewView(
                player: player, state: state,
                onCreate: { … unchanged … },
                onDiscard: { [weak self] in self?.onDiscard(); self?.close() },
                onScrub: { [weak self] time, _ in self?.scrub(to: time) },
                onRelease: { [weak self] kind in self?.endScrub(returnToStart: kind != .playhead) })
```

(`onCreate` stays byte-identical to the current code.)

- [ ] **Step 4: Build + tests + app build**

Run: `cd mac && swift build 2>&1 | tail -2 && swift test 2>&1 | grep "Executed"`
Expected: green.
Then: `DMSHOT_SIGN_ID="Developer ID Application: Thomas Schwabe (FLG4M553XP)" ./build_app.sh release` and relaunch (`osascript -e 'quit app "DM Screenshot"'; open "build/DM Screenshot.app"`). NEVER build without `DMSHOT_SIGN_ID` — a "DMShot Dev"-signed build loses the user's Screen Recording grant.

- [ ] **Step 5: Commit**

```bash
git add mac/Sources/DMShot/VideoPreviewWindow.swift
git commit -m "feat(mac): trim timeline UI — one track, two handles, playhead, time pill

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: Manual verification checkpoint (mac, USER)

No files. The agent cannot see video playback — the USER verifies:

- [ ] Record a short section (⌘⌃2), stop → preview opens, plays, loops.
- [ ] Drag the end handle left ~3 s → video pauses on the end frame while dragging; on release playback restarts at trim start and the loop is visibly ~3 s shorter.
- [ ] Drag the start handle → pause on start frame; release → loop from new start.
- [ ] Drag in the middle of the kept range → playhead scrubs; release resumes in place.
- [ ] Time pill counts `0.0s → total` and matches the kept length shown bottom-left.
- [ ] Create GIF → result matches the previewed range (same as before).

STOP if any item fails; fix before Windows work.

---

### Task 5: `TrimTimelineMath` (win) — TDD, committed unverified

**Files:**
- Create: `windows/DMShot/Video/TrimTimelineMath.cs`
- Test: `windows/DMShot.Tests/TrimTimelineMathTests.cs`

**Interfaces:**
- Produces (used by Task 6): static class `DMShot.Video.TrimTimelineMath` with
  `MinGap = 0.1`, `XPosition(double time, double duration, double width)`,
  `TimeAtX(double x, double duration, double width)`,
  `ClampedStart(double proposed, double end)`,
  `ClampedEnd(double proposed, double start, double duration)`,
  `ClampedPlayhead(double t, double start, double end)`,
  `DisplayElapsed(double current, double start, double end)` (returns clamped elapsed; total is `Math.Max(0, end - start)` at call sites).

- [ ] **Step 1: Write the tests**

`windows/DMShot.Tests/TrimTimelineMathTests.cs` (mirror of the mac cases —
follow the assertion style of the existing test files, e.g. `CropMathTests.cs`):

```csharp
using DMShot.Video;
using Xunit;

namespace DMShot.Tests;

public class TrimTimelineMathTests
{
    [Fact]
    public void XPositionMapsLinearlyAndClamps()
    {
        Assert.Equal(0, TrimTimelineMath.XPosition(0, 10, 200), 9);
        Assert.Equal(100, TrimTimelineMath.XPosition(5, 10, 200), 9);
        Assert.Equal(200, TrimTimelineMath.XPosition(11, 10, 200), 9);
        Assert.Equal(0, TrimTimelineMath.XPosition(-1, 10, 200), 9);
        Assert.Equal(0, TrimTimelineMath.XPosition(3, 0, 200), 9);
        Assert.Equal(0, TrimTimelineMath.XPosition(3, 10, 0), 9);
    }

    [Fact]
    public void TimeAtXRoundTripsAndClamps()
    {
        Assert.Equal(5, TrimTimelineMath.TimeAtX(100, 10, 200), 9);
        Assert.Equal(0, TrimTimelineMath.TimeAtX(-5, 10, 200), 9);
        Assert.Equal(10, TrimTimelineMath.TimeAtX(500, 10, 200), 9);
        Assert.Equal(0, TrimTimelineMath.TimeAtX(100, 0, 200), 9);
    }

    [Fact]
    public void ClampedStartRespectsMinGapAndZero()
    {
        Assert.Equal(0, TrimTimelineMath.ClampedStart(-2, 5), 9);
        Assert.Equal(3, TrimTimelineMath.ClampedStart(3, 5), 9);
        Assert.Equal(4.9, TrimTimelineMath.ClampedStart(7, 5), 9);
    }

    [Fact]
    public void ClampedEndRespectsMinGapAndDuration()
    {
        Assert.Equal(10, TrimTimelineMath.ClampedEnd(12, 2, 10), 9);
        Assert.Equal(6, TrimTimelineMath.ClampedEnd(6, 2, 10), 9);
        Assert.Equal(2.1, TrimTimelineMath.ClampedEnd(-1, 2, 10), 9);
        Assert.Equal(0.05, TrimTimelineMath.ClampedEnd(0.01, 0, 0.05), 9);
    }

    [Fact]
    public void PlayheadAndDisplayClampToRange()
    {
        Assert.Equal(2, TrimTimelineMath.ClampedPlayhead(1, 2, 5), 9);
        Assert.Equal(5, TrimTimelineMath.ClampedPlayhead(9, 2, 5), 9);
        Assert.Equal(1.4, TrimTimelineMath.DisplayElapsed(3.4, 2, 5), 9);
        Assert.Equal(0, TrimTimelineMath.DisplayElapsed(0, 2, 5), 9);
        Assert.Equal(3, TrimTimelineMath.DisplayElapsed(9, 2, 5), 9);
    }
}
```

(If the test project uses a different framework than xUnit, mirror whatever
`CropMathTests.cs` uses — same cases, same tolerances.)

- [ ] **Step 2: Write the implementation**

`windows/DMShot/Video/TrimTimelineMath.cs`:

```csharp
namespace DMShot.Video;

/// <summary>
/// Pure math for the preview's trim timeline (spec 2026-07-05). Mirrors the
/// macOS TrimTimeline enum: time↔track-x mapping, handle clamping, playhead
/// clamp, and the relative time readout. No WPF types — unit-testable.
/// </summary>
public static class TrimTimelineMath
{
    /// <summary>Handles may never come closer than this (seconds).</summary>
    public const double MinGap = 0.1;

    public static double XPosition(double time, double duration, double width)
    {
        if (duration <= 0 || width <= 0) return 0;
        return Math.Min(Math.Max(time / duration, 0), 1) * width;
    }

    public static double TimeAtX(double x, double duration, double width)
    {
        if (duration <= 0 || width <= 0) return 0;
        return Math.Min(Math.Max(x / width, 0), 1) * duration;
    }

    public static double ClampedStart(double proposed, double end)
        => Math.Min(Math.Max(proposed, 0), Math.Max(0, end - MinGap));

    public static double ClampedEnd(double proposed, double start, double duration)
        => Math.Max(Math.Min(proposed, duration), Math.Min(duration, start + MinGap));

    public static double ClampedPlayhead(double t, double start, double end)
        => Math.Min(Math.Max(t, start), end);

    /// <summary>Elapsed-within-range for the time pill, clamped to [0, end-start].</summary>
    public static double DisplayElapsed(double current, double start, double end)
    {
        double total = Math.Max(0, end - start);
        return Math.Min(Math.Max(current - start, 0), total);
    }
}
```

- [ ] **Step 3: Commit (unverified — no Windows build here)**

```bash
git add windows/DMShot/Video/TrimTimelineMath.cs windows/DMShot.Tests/TrimTimelineMathTests.cs
git commit -m "feat(win): TrimTimelineMath — mirror of mac TrimTimeline (UNVERIFIED: no win build env)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 6: One-timeline UI (win) — committed unverified

**Files:**
- Modify: `windows/DMShot/Video/VideoPreviewWindow.xaml` (replace the three slider Grids)
- Modify: `windows/DMShot/Video/VideoPreviewWindow.xaml.cs` (drag logic, pill, remove slider handlers)

**Interfaces:**
- Consumes: `TrimTimelineMath` (Task 5). Existing fields `_playhead`, `_trimStart`, `_trimEnd`, `_timer`, `ShowFrameAt(double)`, `UpdateDuration()`, `UpdateCreateGifEnabled()` stay.

- [ ] **Step 1: XAML — replace the three slider Grids**

Delete the three `<Grid …>` blocks labeled `<!-- Playhead scrub -->`,
`<!-- Trim start -->`, `<!-- Trim end -->` and insert instead:

```xml
        <!-- Trim timeline: full-duration track, accent kept-range, two handles,
             playhead line. All math in TrimTimelineMath; drag logic in code-behind. -->
        <Canvas x:Name="Timeline" Height="28" Margin="0,0,0,6" Background="Transparent"
                MouseLeftButtonDown="Timeline_MouseDown" MouseMove="Timeline_MouseMove"
                MouseLeftButtonUp="Timeline_MouseUp" SizeChanged="Timeline_SizeChanged">
          <Border x:Name="TrackBase" Height="6" Canvas.Top="11" CornerRadius="3"
                  Background="#33FFFFFF"/>
          <Rectangle x:Name="TrackRange" Height="6" Canvas.Top="11"
                     Fill="{DynamicResource DmAccent}" Opacity="0.55"/>
          <Rectangle x:Name="PlayheadLine" Width="2" Height="18" Canvas.Top="5"
                     Fill="#E6FFFFFF"/>
          <Border x:Name="HandleStart" Width="8" Height="22" Canvas.Top="3" CornerRadius="3"
                  Background="{DynamicResource DmAccent}" BorderBrush="#59FFFFFF" BorderThickness="1"/>
          <Border x:Name="HandleEnd" Width="8" Height="22" Canvas.Top="3" CornerRadius="3"
                  Background="{DynamicResource DmAccent}" BorderBrush="#59FFFFFF" BorderThickness="1"/>
        </Canvas>

        <!-- Start / End labels under the timeline -->
        <Grid Margin="0,0,0,12">
          <TextBlock FontSize="11" FontFamily="Consolas" Foreground="{DynamicResource DmTextDim}"
                     HorizontalAlignment="Left">
            <Run Text="{loc:Tr videoTrimIn}"/><Run Text=" "/><Run x:Name="TrimStartValue" Text="0.0s"/>
          </TextBlock>
          <TextBlock FontSize="11" FontFamily="Consolas" Foreground="{DynamicResource DmTextDim}"
                     HorizontalAlignment="Right">
            <Run Text="{loc:Tr videoTrimOut}"/><Run Text=" "/><Run x:Name="TrimEndValue" Text="0.0s"/>
          </TextBlock>
        </Grid>
```

And overlay the time pill on the preview — replace the preview `Border` child:

```xml
    <Border Background="#0D0D10" Margin="16,16,16,0" CornerRadius="6"
            BorderBrush="{DynamicResource DmBorder}" BorderThickness="1">
      <Grid>
        <Image x:Name="Preview" Stretch="Uniform"/>
        <Border HorizontalAlignment="Right" VerticalAlignment="Top" Margin="8"
                Background="#8C000000" CornerRadius="9" Padding="8,3">
          <TextBlock x:Name="TimePill" FontSize="11" FontFamily="Consolas"
                     Foreground="White" Text="0.0s / 0.0s"/>
        </Border>
      </Grid>
    </Border>
```

If `DmAccent` is not an existing brush resource in `DmTheme.xaml`, use the brush
name that IS defined there for the accent color (check `DmTheme.xaml` first;
the mac accent is #c97b4a).

- [ ] **Step 2: Code-behind — drag logic**

In `VideoPreviewWindow.xaml.cs`: remove `Scrub_ValueChanged`,
`TrimStart_ValueChanged`, `TrimEnd_ValueChanged` and their `+=` wiring, and the
`Scrub.*`/`TrimStart.*`/`TrimEnd.*` configuration lines in the constructor.
Replace `ShowPlayhead()`'s slider-sync block (the `Scrub.ValueChanged -= …`
three lines) and `PlayheadLabel.Text = …` with `SyncTimelineUI();`. Add:

```csharp
    private enum TrimDrag { None, Start, End, Playhead }
    private TrimDrag _drag = TrimDrag.None;
    private const double HandleHitSlop = 12;
    private double _duration;   // set once in the constructor: frames[^1].TimeSec

    private void Timeline_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        TrackBase.Width = Timeline.ActualWidth;
        SyncTimelineUI();
    }

    private void SyncTimelineUI()
    {
        double w = Timeline.ActualWidth;
        double sx = TrimTimelineMath.XPosition(_trimStart, _duration, w);
        double ex = TrimTimelineMath.XPosition(_trimEnd, _duration, w);
        double px = TrimTimelineMath.XPosition(
            TrimTimelineMath.ClampedPlayhead(_playhead, _trimStart, _trimEnd), _duration, w);
        Canvas.SetLeft(TrackRange, sx);
        TrackRange.Width = Math.Max(0, ex - sx);
        Canvas.SetLeft(PlayheadLine, px - 1);
        Canvas.SetLeft(HandleStart, sx - 4);
        Canvas.SetLeft(HandleEnd, ex - 4);
        TrimStartValue.Text = $"{_trimStart:F1}s";
        TrimEndValue.Text = $"{_trimEnd:F1}s";
        double total = Math.Max(0, _trimEnd - _trimStart);
        TimePill.Text = $"{TrimTimelineMath.DisplayElapsed(_playhead, _trimStart, _trimEnd):F1}s / {total:F1}s";
    }

    private void Timeline_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_rendering) return;
        double w = Timeline.ActualWidth;
        double x = e.GetPosition(Timeline).X;
        double sx = TrimTimelineMath.XPosition(_trimStart, _duration, w);
        double ex = TrimTimelineMath.XPosition(_trimEnd, _duration, w);
        _drag = Math.Abs(x - sx) <= HandleHitSlop ? TrimDrag.Start
              : Math.Abs(x - ex) <= HandleHitSlop ? TrimDrag.End
              : TrimDrag.Playhead;
        _timer.Stop();                       // scrub owns the playhead while dragging
        Timeline.CaptureMouse();
        Timeline_MouseMove(sender, e);
    }

    private void Timeline_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_drag == TrimDrag.None || _rendering) return;
        double t = TrimTimelineMath.TimeAtX(e.GetPosition(Timeline).X, _duration, Timeline.ActualWidth);
        switch (_drag)
        {
            case TrimDrag.Start:
                _trimStart = TrimTimelineMath.ClampedStart(t, _trimEnd);
                ShowFrameAt(_trimStart);     // live feedback: the exact first kept frame
                break;
            case TrimDrag.End:
                _trimEnd = TrimTimelineMath.ClampedEnd(t, _trimStart, _duration);
                ShowFrameAt(_trimEnd);       // live feedback: the exact last kept frame
                break;
            case TrimDrag.Playhead:
                ShowFrameAt(TrimTimelineMath.ClampedPlayhead(t, _trimStart, _trimEnd));
                break;
        }
        UpdateDuration();
        UpdateCreateGifEnabled();
        SyncTimelineUI();
    }

    private void Timeline_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_drag == TrimDrag.None) return;
        bool wasHandle = _drag != TrimDrag.Playhead;
        _drag = TrimDrag.None;
        Timeline.ReleaseMouseCapture();
        if (wasHandle) _playhead = _trimStart;   // restart the loop at the kept range
        if (!_rendering) _timer.Start();
        SyncTimelineUI();
    }
```

In the constructor set `_duration = _trimEnd;` right after `_trimEnd = …`, and
replace the old `Scrub.Maximum…TrimEnd.Value` block with nothing (the Canvas
sizes itself via `Timeline_SizeChanged`). In `UpdateLabels()` drop the
`PlayheadLabel`/`TrimStartLabel`/`TrimEndLabel` lines and call `SyncTimelineUI()`.
`Advance()` is unchanged (already range-loops). `Raise()` is unchanged.

- [ ] **Step 3: Localization check**

`videoPlayhead` is now unused — leave the key defined in BOTH languages in
`Loc.cs` (LocTests enforces key parity, not usage). No new keys.

- [ ] **Step 4: Commit (unverified)**

```bash
git add windows/DMShot/Video/VideoPreviewWindow.xaml windows/DMShot/Video/VideoPreviewWindow.xaml.cs
git commit -m "feat(win): one-timeline trim UI with handles + playhead + time pill (UNVERIFIED: no win build env)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 7: PARITY.md + wrap-up

**Files:**
- Modify: `docs/PARITY.md`

- [ ] **Step 1: Add the parity note**

Append under the existing TODO section (match the file's list format):

```markdown
- **Trim timeline (2026-07-05)**: preview trim reworked to a single timeline
  (two handles + playhead, range-only loop, relative time pill) per
  `docs/superpowers/specs/2026-07-05-trim-timeline-design.md`. macOS verified
  on-device; Windows implemented in the same change but **needs an on-device
  build + eyeball** (Canvas drag logic, DPI, dark theme).
```

- [ ] **Step 2: Full mac test run + commit**

Run: `cd mac && swift test 2>&1 | grep "Executed"` — green.

```bash
git add docs/PARITY.md
git commit -m "docs: PARITY note for the trim-timeline rework (win on-device check pending)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

- [ ] **Step 3: Merge gate**

Merge `feat/trim-timeline` → `main` ONLY after Task 4 (user verification)
passed. Push only if the user asks (fetch+rebase first — a GitLab bot pushes to
main).
