# Changelog

All notable changes to DM_Screenshot. Newest version first. Always written in English.

## [Unreleased]

## 0.4.22 – 2026-06-27
- fix: Windows no longer crashes on startup when applying the selected design after update; theme resources are replaced instead of mutating frozen WPF brushes.

## 0.4.21 – 2026-06-27
- feat: Windows Settings now mirrors the macOS design controls with the Standard Design / Black Utility switcher, compact settings rows and switch-style toggles.
- fix: The Windows tray icon now uses the modern DM Screenshot capture-corners and aperture mark instead of the old camera glyph.

## 0.4.20 – 2026-06-24
- feat: Settings now lets you switch DM Screenshot between Standard Design and Black Utility; Standard keeps the pre-black native macOS feel while Black Utility keeps the pure-black layered chrome (macOS)
- change: Standard Design now restores the pre-black gray app/canvas and settings surfaces, native macOS switches, native Quick-Edit material and the orange sidebar hover state (macOS)

## 0.4.19 – 2026-06-24
- change: The new Black Utility BrandDesign is applied across the editor, Settings and Quick-Edit surfaces, with pure black app chrome, brighter layered control frames, softer orange accent states and matching Windows theme tokens (macOS + Windows)

## 0.4.18 – 2026-06-24
- change: The app is now called "DM Screenshot" (no underscore) to match the other DM apps — this affects the display name, window titles and installer/shortcut titles only; the bundle identifier, existing permissions, auto-update and saved screenshot filenames (still `DM_Screenshot_…`) are unchanged (macOS + Windows)

## 0.4.17 – 2026-06-23
- change: The app icon now uses the colorful DM BrandDesign direction on macOS and Windows, with a full-bleed multi-size Windows export.

## 0.4.16 – 2026-06-23
- fix: The Windows taskbar/Explorer app icon now fills the full icon canvas and ships every size as its own crisp frame, so it no longer looks small, cropped or pixellated (Windows)

## 0.4.15 – 2026-06-23
- fix: Toolbar tooltips no longer stop working after the first annotation — in both the Quick-Edit overlay and the main editor they keep appearing for the whole session (macOS)

## 0.4.14 – 2026-06-23
- change: In the Quick-Edit toolbar, the Save and "Edit in main window" buttons swapped places (macOS + Windows)

## 0.4.13 – 2026-06-23
- change: The numbered-step comment bubble is now translucent so the capture shows through behind it (macOS + Windows)

## 0.4.12 – 2026-06-23
- change: The numbered-step comment bubble now has a softer speech-bubble shape — the whole left side is one rounded arrow pointing at the number, with rounded shoulders and tip (macOS + Windows)

## 0.4.10 – 2026-06-23
- change: A numbered step's comment now sits in a translucent speech bubble with a pointed tail toward the number, set off by a clear gap, and stays readable on any background (macOS + Windows)

## 0.4.9 – 2026-06-23
- feat: Numbered steps can now carry an optional comment typed right next to the badge; the number and its comment move and resize together, and an empty comment leaves just the numbered circle (macOS + Windows)
- feat: The Quick-Edit toolbar gains the Ellipse and numbered-Step tools, and the Copy-to-clipboard button now sits at the far right of the action group (macOS + Windows)
- fix: The numbered-step counter now resets correctly after Undo, so a removed number is reused instead of the count climbing (macOS + Windows)

## 0.4.8 – 2026-06-22
- change: The macOS menu bar icon now uses the modern DM Screenshot mark, matching the new app icon while staying a monochrome template symbol for light/dark menu bars (macOS)

## 0.4.7 – 2026-06-22
- change: The app icon now uses the modern DM Screenshot BrandDesign mark with capture corners, aperture mark, subtle depth, and the shared orange glint (macOS + Windows)

## 0.4.6 – 2026-06-22
- fix: The Quick-Edit toolbar no longer ends up behind the Dock (macOS) / taskbar (Windows). For captures dragged near the bottom of the screen and for full-screen captures it now stays fully on-screen and clickable (macOS + Windows)

## 0.4.5 – 2026-06-21
- fix: The Windows app and tray icons now show the soft off-white camera-and-viewfinder motif, matching macOS; the previous build still shipped the old stark-white icon (Windows)

## 0.4.4 – 2026-06-21
- change: The white camera-and-viewfinder in the app icon is now a soft off-white instead of stark white, so it sits more calmly in the Dock (macOS)

## 0.4.3 – 2026-06-21
- fix: Text annotations can now be moved — click a text to select it and drag it to reposition; double-click anywhere on the text to edit it, and the corner resize handles are easier to grab (macOS)
- fix: The Quick-Edit bar is no longer cut off when you capture a region near the edge of the screen (macOS)
- fix: Annotation resize handles are easier to grab (Windows)

## 0.4.2 – 2026-06-21
- feat: Text annotations are now added directly on the image — pick the Text tool, drag a box to set the size, and type right there (multi-line; press Enter for a new line). Double-click a text to edit it again, and drag a corner handle to scale it. The separate text-entry pop-up window is gone (macOS + Windows)

## 0.4.1 – 2026-06-21 — withdrawn (auto-update signature mismatch from a re-tag; re-released as 0.4.2)

## 0.4.0 – 2026-06-21
- feat: A zoom loupe now appears while you select a capture area — a magnifier follows the cursor with a crosshair and live pixel coordinates, so you can line up the selection edges precisely. Turn it off under Settings → General → Zoom loupe (macOS + Windows)

## 0.3.4 – 2026-06-21
- fix: The taskbar / Alt-Tab app icon now reads at full size — the white camera motif was small inside the dark tile and made the icon look smaller than its neighbours (Windows)

## 0.3.3 – 2026-06-21
- feat: The Quick-Edit overlay's size / blur-strength slider is now always visible instead of tucked behind a button, so you can set it before drawing; it shows size for shapes and blur strength when the blur tool is active (Windows)
- feat: Stroke size and blur strength are now remembered across restarts and shared by the editor and the Quick-Edit overlay (Windows)
- fix: Quick-Edit annotations work again — drawing arrows, blurring a region and the strength slider had stopped working in the overlay because its drawing surface collapsed to zero size (Windows)
- fix: A freshly drawn shape is selected right away, so the size and colour controls apply to it without first switching to the Select tool, matching macOS (Windows)

## 0.3.2 – 2026-06-21
- fix: The sidebar Settings button also shows the orange hover border on mouse-over now, and its gear icon lines up with the capture buttons above (macOS; Windows already had it)
- fix: The video / GIF preview now plays back correctly instead of showing a zoomed-in crop of part of the last frame (Windows)
- fix: Recorded GIFs are much smaller — the encoder no longer dithers, which removes the coloured fringing on text and cuts file size dramatically, matching macOS (Windows)
- fix: The app and tray icons now fill their frame edge-to-edge instead of sitting inside an empty bracket border, matching the macOS icon (Windows)

## 0.3.1 – 2026-06-20
- fix: The editor sidebar capture buttons (Full Screen / Selection / Video Full Screen / Video Section) now show the orange hover border on mouse-over, matching the Settings sidebar (macOS; Windows already had it)

## 0.3.0 – 2026-06-20
- feat: New black-and-white app icon — a camera-in-viewfinder on a dark squircle, matching the DM family look. The menu-bar / tray icon uses the same motif, sized to fill (macOS + Windows)
- feat: Launch DM_Screenshot automatically at login, from a toggle under Settings (macOS)
- feat: Selections can now be moved and resized with handles in the editor, and Undo restores the full document — moves, resizes and crops, not just annotations (macOS + Windows)
- feat: The video / GIF preview now shows the estimated GIF file size, updating live as you trim (macOS + Windows)
- fix: More of the interface is fully translated now — editor help text, file dialogs, tooltips and the "GIF ready" viewer (macOS + Windows)
- fix: The Settings → Language dropdown is dark-themed instead of the light system dropdown; combo boxes and text fields are now dark by default (Windows)

## 0.2.8 – 2026-06-20
- feat: DM_Screenshot is now available in German as well as English. Switch live (no restart) under Settings → Language; English stays the default. Menus, tooltips, settings, dialogs and the editor are all translated (macOS + Windows)
- fix: Sidebar capture-button labels (Full Screen / Selection / Video Full Screen / Video Section) now line up consistently, instead of "Selection" sitting slightly off (macOS)
- fix: The Settings → Language dropdown is dark-themed instead of the light system dropdown; combo boxes and text fields are now dark by default (Windows)

## 0.2.7 – 2026-06-20
- fix: Active/selected controls now use white labels and icons on the orange accent instead of near-black, which read as muted (macOS + Windows)
- fix: Settings sidebar entries show an orange border on hover (macOS + Windows)
- fix: Sliders, checkboxes and radio buttons are now properly dark-themed instead of the light system chrome (Windows)
- feat: The tray menu now has "New Video (Full Screen)" and "New Video (Area)" entries, matching the macOS menu (Windows)

## 0.2.6 – 2026-06-20
- fix: Quick-Edit toolbar Copy / Save / Edit-in-main are now icon-only buttons matching the macOS toolbar (Windows)
- fix: Tray menu no longer shows a stray white separator line above Quit (Windows)
- fix: Settings panes scroll when the content is tall, so the Updates "Download & Install" button is always reachable (Windows)
- fix: The "What's new" in Settings → Updates now shows only the latest release's notes instead of the entire changelog history (macOS + Windows)

## 0.2.5 – 2026-06-20
- fix: Settings → Updates now shows the actual installed version. The app version is stamped from the single-source-of-truth VERSION file at build time, instead of a stale hard-coded value (macOS)

## 0.2.4 – 2026-06-20
- fix: A zoomed-in screenshot now stays inside the editor canvas instead of painting out over the sidebar and the rest of the window (macOS; Windows hardening)

## 0.2.3 – 2026-06-20
- feat: Editor zoom & pan — the screenshot now fits the editor window instead of resizing it to the capture; zoom toward the cursor with Ctrl/⌘+mouse-wheel or a trackpad pinch, pan with scroll / Shift+scroll or Space-drag, and reset from the toolbar zoom-% indicator (⌘/Ctrl 0 = fit, 1 = 100%). Small captures open at 100% and large ones scale to fit, while the window stays a stable, resizable size (macOS + Windows)

## 0.2.2 – 2026-06-20
- feat: Quick-Edit in-place markup overlay is now on Windows — a capture appears framed over a dimmed backdrop with a compact floating toolbar so you can mark it up in place; enable via Settings → General → After capture, with the same reduced tools, color/size flyouts, undo, and one-click "Edit in main window" that carries annotations over (Windows)
- feat: Video/GIF capture is now on Windows — record the full screen or a section (Ctrl+Alt+1 / Ctrl+Alt+2), trim the clip, and copy an optimized animated GIF (≤1000px, 10fps, max 60s) that pastes into Teams/Outlook; clips are kept in history and can be re-copied or saved (Windows)
- feat: Record full-screen / section buttons added to the editor sidebar alongside the image-capture buttons (Windows)
- fix: Windows theming polish — dark title bars on the preview and GIF windows, a readable dark tray menu and dark tooltips, and a restyled Quick-Edit toolbar with proper icons and buttons; the preview/GIF action buttons are no longer clipped at the default window size (Windows)

## 0.2.1 – 2026-06-19
- fix: Section (area) video recordings of mostly-static content now work — previously the GIF preview never appeared after Stop (macOS)
- fix: Recording a second clip no longer crashes the app while a preview is still open (macOS)
- fix: The trim/preview window, and the created GIF, now come to the front automatically (after Stop, and after “Create GIF”) (macOS)
- feat: A highlight frame marks the recorded region during a section recording, and DM_Screenshot now steps aside while you record so it stays out of the way and out of the recording (macOS)
- fix: The Screen Recording permission notice offers a one-click “Relaunch Now” so a freshly granted permission applies immediately (macOS)

## 0.2.0 – 2026-06-19
- feat: Video/GIF capture — record the full screen or a section (Cmd+Ctrl+1 / Cmd+Ctrl+2), trim the clip, and copy an animated GIF for pasting into chat/email (max 60s)
- feat: Quick-Edit bar — optionally mark up a screenshot in place: the capture appears framed over a dimmed backdrop with a compact floating toolbar (Settings → General → After capture), offering the same tools, color/size flyouts, undo, and one-click escalation to the main window

## 0.1.4 – 2026-06-19
- fix: Area capture (⌘⇧2) now lets you drag a selection on the very first click — previously the first click was swallowed to activate the overlay, so a selection only worked on the second click (macOS)
- fix: The resizable left-sidebar handle is grabbable again and no longer draws a stray line across the editor canvas (macOS)
- feat: Windows automatic updates — DM_Screenshot for Windows now checks for new versions on launch and from Settings → Updates, shows a themed "What's new" from the changelog, and installs the update with one click (Velopack, matching the macOS auto-updater)

## 0.1.3 – 2026-06-19
- fix: Area-capture overlay now shows the crosshair cursor immediately when it appears — no initial click needed to take focus (macOS and Windows)
- feat: Delete a single history capture by hovering its thumbnail and clicking the trash button
- feat: The left sidebar is now resizable by dragging its edge; history previews scale with the sidebar width

## 0.1.2 – 2026-06-19
- feat: Automatic updates — DM_Screenshot now checks for new versions on launch and from Settings → Updates, shows a themed "What's new" with the changelog, and installs the update with one click

## 0.1.1 – 2026-06-18
- feat: Editor crosshair cursor and a menu-bar icon
- fix: Saved screenshots use a timestamped name (DM_Screenshot_DDMMYYYY_HH_MM) with _1/_2 suffixes for same-minute collisions

## 0.1.0 – 2026-06-16
- feat: First native macOS release — full-screen and area capture, annotation editor (arrow, box, ellipse, line, pen, mosaic blur, text, step numbers, highlighter, crop), copy and save, history sidebar, editable shortcuts, launch-at-login
