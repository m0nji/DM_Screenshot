# Changelog

All notable changes to DM_Screenshot. Newest version first. Always written in English.

## [Unreleased]

## 0.8.7 – 2026-07-25
- fix: The size shown while dragging a capture selection is the real pixel size again on macOS — on a Retina display it reported half the actual value, so a selection saved as 800 × 600 pixels was labelled 400 × 300 while dragging (macOS)
- fix: Clicking the dock icon of the running app reopens the editor on macOS — after closing the editor window the app kept running in the menu bar, but the dock icon no longer responded to anything, which made it look dead; a click during a capture or recording still leaves that alone (macOS)
- fix: Saving now reports a problem instead of failing silently on macOS — if the file could not be written (read-only folder, full disk, a file held by another program) you picked a location, pressed Save and got nothing at all, no file and no message; the same applies to saving and copying a GIF (macOS)
- new: The editor finally has keyboard shortcuts on macOS — ⌘C copies, ⌘S saves, ⌘Z and ⇧⌘Z undo and redo, ⌫ deletes the selected annotation; they live in a proper menu bar, so they are visible instead of hidden, and ⌘W, ⌘M and ⌘Q work as expected too. While typing an inline text annotation, ⌘C and ⌫ still act on the text, not on the screenshot (macOS)

## 0.8.6 – 2026-07-25
- fix: Recorded GIFs no longer turn into a black rectangle with scattered specks wherever the screen was moving — everything that stayed still was kept, everything that moved was lost, so a recording of a video or a scrolling window was unusable; the moving area is now reproduced correctly (Windows)
- fix: Longer screen recordings no longer run the machine out of memory — the recording kept every frame at full display resolution (about 18 GB for one minute on a 4K screen), although the GIF is created at a much smaller size anyway; frames are now reduced right at capture time, which uses about 15× less memory without changing the resulting GIF (Windows)
- fix: A damaged history file no longer prevents the app from starting — a screenshot history that was written incompletely (for example after a power loss) made the app fail on every launch; it now starts with an empty history instead (Windows)
- fix: Screenshots whose files were removed outside the app (cleanup tools, sync software) no longer crash the app when the sidebar refreshes or the entry is opened — the affected entry is dropped instead (Windows)
- fix: Saving now reports a problem instead of closing the app when the file cannot be written (read-only folder, full disk) — applies to Save in the editor, in the Quick-Edit bar and in the GIF window (Windows)
- fix: A failed GIF conversion no longer closes the app — the error is shown and the recording stays available (Windows)
- fix: Changing a setting no longer closes the app when the settings file cannot be written; "Launch at login" reports it when company policy blocks the change (Windows)
- fix: Version shown in Settings matches the released version again (macOS)

## 0.8.5 – 2026-07-22
- fix: Shortcut fields in Windows Settings now visibly enter recording mode when clicked and accept a new key combination; Escape or focus loss cancels without losing the saved shortcut (Windows)

## 0.8.4 – 2026-07-19
- fix: Recorded GIFs no longer show dark rounded rectangles where a context menu and its drop shadow had been — the screen capture delivers premultiplied colors for translucent content (menu shadows, acrylic), which were baked darkened into the GIF; the capture now restores the true colors, so text under a former menu/shadow stays fully readable (Windows)

## 0.8.3 – 2026-07-12
- fix: Automatic update on macOS could report "The update is improperly signed and could not be validated" and refuse to install — the release pipeline now keeps the update signature in lockstep with the published download, so updating works again (macOS)

## 0.8.2 – 2026-07-12
- fix: Recorded GIFs no longer come out near-black — a GIF-encoder library bug collapsed the color palette to a single color; both the preview and the copied/saved file were affected (Windows)
- fix: Creating a GIF from a recording longer than ~30 seconds no longer freezes the app — the preview is now prepared in the background while the window stays responsive ("Preparing preview…") (Windows)
- fix: Clicking the pinned taskbar icon (or launching the app again any other way) now reopens the main window after it was closed — previously the second launch exited silently and nothing happened (Windows)
- new: After installation the app now confirms it is ready — the main window opens and a tray notification appears instead of the app silently disappearing into the system tray (Windows)
- fix: Editor toolbar matches the macOS layout — uniform spacing between tools instead of glued-together buttons, and selected dropdown items show the correct dark label on the sand fill (Windows)

## 0.8.1 – 2026-07-11
- fix: Settings switches now use the DM-family size (32×18, like DM Voice) — they were oversized on macOS and clipped in the Windows settings rows
- fix: Windows accent buttons ("Check for Updates" etc.) and the selected settings-nav label now use the correct dark text on the sand fill instead of white

## 0.8.0 – 2026-07-11
- new: Graphite Sand design — the DM Apps brand look (graphite gradient background, flat calm surfaces, sand accent fills with dark labels, matching DM Voice/Workspace) joins Standard and Black Utility as the new default theme; every install is moved to it once, after which the design choice is respected again (macOS + Windows)
- change: Updates are now re-checked every hour while the app is running (like DM Workspace), so an available update shows up in the tray without opening Settings (macOS + Windows)

## 0.7.5 – 2026-07-10
- fix: The annotation editor no longer lags when dragging arrows or typing step comments — a redraw feedback loop, where the editor and quick-edit canvases fought over the shared zoom indicator and repainted ~100×/second even at rest, is eliminated; the canvas now stays idle when nothing changes (macOS, M4 Pro / external 120 Hz displays)

## 0.7.4 – 2026-07-08
- change: App icon mark reduced further (~62% tile fill) — macOS 26 and the Windows taskbar render the tile edge-to-edge, so the previous size still crowded the edge (per DM BrandDesign v1.0.3)

## 0.7.3 – 2026-07-07
- change: App icon refined — the capture mark is slightly smaller so it no longer crowds the squircle edge (macOS + Windows, per DM BrandDesign v1.0.1)

## 0.7.2 – 2026-07-07
- change: New app icon in the DM "Graphite Sand" brand design — the familiar capture mark in the warm base-metal gradient (macOS + Windows)

## 0.7.1 – 2026-07-05
- feat: When an update is available, the app now says so actively — an accent dot appears on the menu-bar/tray icon and the menu gains a first item "Update to X available…" that opens Settings; both disappear once the update is installed (macOS + Windows)
- fix: The silent update check at launch actually reports its result now — previously a found update was dropped, so even Settings only showed it after clicking "Check for Updates" manually (macOS)

## 0.7.0 – 2026-07-05
- feat: The recording preview trims with a single QuickTime-style timeline — two drag handles, a playhead marker, and playback that loops only the kept range; dragging a handle pauses on the exact cut frame, and a time readout over the video shows the position within the trimmed range (macOS + Windows)
- feat: GIFs can be created in a new "Small" quality (5 fps, max 800 px — roughly a quarter of the size) via a Standard | Small picker in the preview; the size estimate follows the selection (macOS + Windows)
- feat: Existing GIFs can be converted to Small afterwards ("Convert to Small" in the GIF viewer) — the history entry and clipboard copy are replaced, so no duplicate versions pile up (macOS + Windows)
- feat: Creating a GIF shows visible progress ("Creating GIF…" with a spinner) instead of appearing to do nothing on long clips (macOS; Windows gains the same label next to its busy state)
- fix: The crosshair cursor appears again — and immediately, without moving the mouse — when starting an area capture or section recording; it broke in 0.6.0 when the capture overlay stopped activating the app (macOS)
- fix: Tooltips in the Quick-Edit bar and on the recording control work again (same 0.6.0 regression) (macOS)
- fix: Hovering the area-selection overlay no longer redraws the whole frozen screen per mouse move when the zoom loupe is off (macOS)
- fix: Recorder start-up state is published race-free to the capture stream's queue (macOS)
- change: A divider now separates the Background button from the color picker in the editor and Quick-Edit toolbars (macOS + Windows)

## 0.6.1 – 2026-07-02
- fix: Moving, resizing and drawing annotations in the Quick-Edit overlay no longer stutters, especially on busy systems (e.g. during a video call) — dragging no longer re-renders the whole overlay per mouse move, and blur regions show a lightweight preview while dragging that snaps to full quality on release; exported images are unchanged (macOS)

## 0.6.0 – 2026-07-02
- feat: Video recordings now include the mouse cursor, and section recordings show a thin accent frame around exactly the region being captured (Windows)
- feat: Settings flags shortcut problems like macOS — combos without a modifier or already used by another action are rejected with an inline error, system-wide conflicts are shown, and a new "Reset to defaults" button restores the stock hotkeys (Windows)
- feat: The tray menu shows each capture action's current hotkey, and video entries in the history sidebar carry a play badge; thumbnails are sharper (up to 320 px, small captures no longer upscaled) (Windows)
- change: The default annotation color is now the same red as macOS, the editor and Quick-Edit share macOS's 7-color palette, the default stroke is 4 px and the blur strength range is 2–60 (Windows)
- fix: Creating a GIF no longer freezes the app — rendering runs in the background while the preview shows a busy state (Windows)
- fix: Dragging and drawing annotations stays smooth on large captures, even with several blur/mosaic regions (Windows)
- fix: Text annotations keep exactly the size shown while typing when committed, and the step badge matches macOS sizing (existing saved text renders slightly smaller — that is the corrected size) (Windows)
- fix: The recording preview plays at real speed (a 10-second recording takes 10 seconds) with a linear scrubber (Windows)
- fix: Memory is properly released after captures, Quick-Edit sessions and recordings (previously ~100 MB per 4K capture accumulated) (Windows)
- fix: The recording pill is centered on the recording display and never sits behind the taskbar on mixed-DPI setups; only one app instance runs at a time; the crosshair cursor no longer sticks after abnormally closing the capture overlay; deleting the history entry that is currently open in the editor works; Esc deselects the selected annotation; the area-selection dimming matches macOS (Windows)

## 0.5.3 – 2026-06-29
- fix: The Windows Quick-Edit Background flyout is now only as wide as its controls and sits as a separate panel centred under the toolbar, instead of stretching a dark band across the full toolbar width (Windows)

## 0.5.2 – 2026-06-29
- fix: Windows Background "Blur" fill now shows the real blurred screenshot live in the editor and Quick-Edit preview, instead of a flat grey placeholder — matching what Copy / Save produce and the macOS preview (Windows)
- fix: The Windows Quick-Edit Background flyout no longer gets clipped off the bottom of the screen; the toolbar repositions so the whole panel (padding, corners and fill swatches) stays on-screen (Windows)

## 0.5.1 – 2026-06-29
- fix: Windows no longer crashes when you open the new Background tool — the panel's button styles failed to load on first open in 0.5.0 (Windows)

## 0.5.0 – 2026-06-29
- feat: New "Background" tool wraps a screenshot in a presentable frame — add padding (Small / Medium / Large) and rounded corners (None / Soft / Round), and put a backdrop behind the shot: a solid colour, a gradient, or a blurred enlargement of the screenshot itself. It's a live preview in both the editor and the Quick-Edit overlay, and it's baked into Copy / Save and the history thumbnail (macOS + Windows)
- feat: The Background style is remembered across launches and shared by the editor and Quick-Edit; it starts off, and Blur is the preselected fill when you switch it on (macOS + Windows)

## 0.4.24 – 2026-06-28
- fix: Windows Black Utility design now paints the window title bar pure black — matching the app background and the macOS look — instead of leaving the Windows 11 dark-gray caption; it also repaints live when you switch design.
- fix: Windows controls render the shared BrandDesign faded chrome frame on a softened 50% base edge at rest, matching macOS, instead of a flat hard outline; hover and active states keep their crisp orange border.

## 0.4.23 – 2026-06-27
- fix: Windows now uses the shared BrandDesign layered control chrome for toolbar, sidebar, Settings navigation, combo boxes, text boxes and switches instead of flat bordered buttons.
- fix: Windows Standard Design and Black Utility now update loaded UI through dynamic theme resources, including Quick-Edit and the editor canvas, so hard-coded black/gray surfaces no longer override the selected design.

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
