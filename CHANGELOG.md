# Changelog

All notable changes to DM_Screenshot. Newest version first. Always written in English.

## [Unreleased]

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
