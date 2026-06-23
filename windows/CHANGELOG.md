# Changelog

All notable changes to DM_Screenshot for Windows. Newest version first. Always written in English.

## [Unreleased]
- fix: The taskbar/Explorer app icon now fills the full icon canvas and ships every size as its own crisp frame, so it no longer looks small, cropped or pixellated (Windows)

## 0.2.8 – 2026-06-20
- feat: DM_Screenshot is now available in German as well as English — switch live (no restart) under Settings → Language; English stays the default (Windows)
- fix: The Settings → Language dropdown is dark-themed instead of the light system dropdown; combo boxes and text fields are now dark by default (Windows)

## 0.2.7 – 2026-06-20
- fix: Active/selected controls use white labels and icons on the orange accent instead of near-black (Windows)
- fix: Sliders, checkboxes and radio buttons are now dark-themed instead of light system chrome (Windows)
- fix: Settings sidebar entries show an orange border on hover, matching the main window (Windows)
- feat: Tray menu now has "New Video (Full Screen)" and "New Video (Area)" entries (Windows)

## 0.2.6 – 2026-06-20
- fix: Quick-Edit toolbar Copy / Save / Edit-in-main are now icon-only buttons, matching the macOS toolbar (Windows)
- fix: Tray menu no longer shows a stray white separator line above Quit (the dark separator style was keyed wrong) (Windows)
- fix: Settings panes scroll when content is tall, so the Updates "Download & Install" button is always reachable (Windows)
- fix: The "What's new" in Settings → Updates now shows only the latest release's notes instead of the entire changelog history (Windows)

## 0.2.4 – 2026-06-20
- fix: A zoomed-in screenshot now stays inside the editor canvas instead of painting out over the sidebar and the rest of the window (Windows)

## 0.2.3 – 2026-06-20
- feat: Editor zoom & pan — the screenshot now fits the editor window instead of resizing it to the capture; zoom toward the cursor with Ctrl+mouse-wheel, pan with scroll / Shift+scroll or Space-drag, and reset from the toolbar zoom-% indicator (Ctrl 0 = fit, 1 = 100%). Small captures open at 100% and large ones scale to fit, while the window stays a stable, resizable size (Windows)

## 0.2.2 – 2026-06-20
- feat: Video/GIF capture — record full screen or a selected region (Ctrl+Alt+1 / Ctrl+Alt+2),
  trim, and copy an optimized animated GIF (≤1000px, 10fps) that pastes into Teams/Outlook.
  Saved to history; click a video entry to re-copy or save it. Record buttons were also
  added to the editor sidebar.
- feat: Quick-Edit in-place markup overlay — annotate a capture in place (dimmed
  backdrop, framed capture, floating reduced toolbar) without opening the main editor.
  Toggle in Settings → After capture. "Edit in main window" carries annotations over.
- fix: Theming polish — dark title bars on the preview/GIF windows, a readable dark tray
  menu and dark tooltips, a restyled Quick-Edit toolbar with proper icons/buttons, and the
  preview/GIF action buttons no longer clipped at the default window size.

## 0.1.4 – 2026-06-19
- fix: Area capture now lets you drag a selection on the very first click — previously the first click was swallowed to activate the overlay, so a selection only worked on the second click (Windows)
- fix: The resizable left-sidebar handle is grabbable again and no longer draws a stray line across the editor canvas (Windows)
- feat: Automatic updates — DM_Screenshot for Windows now checks for new versions on launch and from Settings → Updates, shows a themed "What's new" from the changelog, and installs the update with one click (Velopack)

## 0.1.3 – 2026-06-19
- fix: Area-capture overlay now shows the crosshair cursor immediately when it appears — no initial click needed to take focus (Windows)
- feat: Delete a single history capture by hovering its thumbnail and clicking the trash button
- feat: The left sidebar is now resizable by dragging its edge; history previews scale with the sidebar width

## 0.1.0 – 2026-06-16
- feat: First native Windows release — full-screen and area capture, annotation editor (arrow, box, ellipse, line, pen, mosaic blur, text, step numbers, highlighter, crop), copy and save, history sidebar, editable shortcuts, launch-at-login, system-tray icon
