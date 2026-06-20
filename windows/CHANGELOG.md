# Changelog

All notable changes to DM_Screenshot for Windows. Newest version first. Always written in English.

## [Unreleased]

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
