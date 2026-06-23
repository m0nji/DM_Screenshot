# DM_Screenshot Agent Instructions

These instructions mirror `CLAUDE.md` for Codex and other agents. `CLAUDE.md`
remains the project source that these instructions were copied from.

## Project Overview

DM_Screenshot is a native macOS screenshot and annotation app in `mac/`
(Swift / SwiftUI / AppKit) plus a Windows port in `windows/` (C# / .NET / WPF).
macOS is the behavioral source of truth.

## BrandDesign App Icons

- App icons must use the shared DM BrandDesign system from /Users/thomas/Projects/DM_BrandDesign.
- Check /Users/thomas/Projects/DM_BrandDesign/packages/assets/icons/screenshot-color-modern-source.svg before changing icon geometry, colors, or effects.
- Keep the editable macOS/source art at 1024x1024 with the BrandDesign safe area: the squircle is x=100, y=100, width=824, height=824.
- Windows exports must be full-bleed compared with the macOS source. Keep WIN_FILL at about 1.214 and include explicit .ico frames for taskbar sizes.
- Do not redraw the icon locally. Update the BrandDesign source first, then regenerate app assets from that source.
- macOS source: mac/Resources/AppIcon.svg, regenerated with mac/make_icon.sh.
- Windows app icon: windows/DMShot/Resources/AppIcon.ico, regenerated from the same SVG source with windows/tools/make-app-icon.mjs or windows/tools/make-app-icon.ps1.

## Git Workflow

- Use one branch per feature or change. Branch off `main` using names such as
  `feat/...`, `fix/...`, or `chore/...`.
- Merge back to `main` only when the work is complete and verified with builds,
  tests, and manual checks for behavior changes.
- Do not commit work in progress directly to `main`.
- Never run two agent sessions in the same working copy at the same time.
  Concurrent sessions must use separate git worktrees, for example:
  `git worktree add ../dmshot-<topic> -b <branch>`.
- `main` is protected on GitLab. Push only when the user asks.
- Never overwrite others' work. Prefer fast-forward pushes.
- `origin` is the self-hosted GitLab source of truth.
- The `github` remote is a mirror/secondary remote. Do not push commits that
  add or modify this `AGENTS.md` file to GitHub. It may be included in GitLab.

## Build And Test

### macOS

- Fast syntax check: `cd mac && swift build`
- Unit tests: `cd mac && swift test`
- Runnable app: `cd mac && ./build_app.sh release`
- The release build creates `mac/build/DM_Screenshot.app`.
- The app is ad-hoc signed by default. This resets the Screen Recording grant on
  each build, so the next capture re-prompts.
- Run `mac/make_cert.sh` once for a stable signing identity.
- Agents cannot grant Screen Recording or verify real capture output. The user
  must manually verify recording, GIF, and paste behavior on a real machine.

## Parity

- Every user-facing behavior change must land on both `mac/` and `windows/` in
  the same change.
- If parity is deferred, add an explicit TODO that references `docs/PARITY.md`.
- macOS is the behavioral source of truth and Windows mirrors it.

## Localization

- The app ships English as default plus German.
- No user-facing string literal may live directly in a view, menu, tooltip, or
  alert.
- macOS strings go through `L` / `tr` in
  `mac/Sources/DMShot/Localization.swift`.
- Windows strings go through `Loc` / `{loc:Tr}` in
  `windows/DMShot/Localization/Loc.cs`.
- Every new string must include both English and German.
- macOS enforces localization completeness at compile time via the non-exhaustive
  `switch` in `Localizer`.
- Windows enforces localization parity via the `LocTests` key-parity test.
- Language display names (`English` / `Deutsch`) are never translated.
- First-run default language is always English, regardless of OS language.
- Language switching is live and must not require restart.

## Repo And Docs

- Source-of-truth repo: self-hosted GitLab at `origin`.
- Access tokens live in repo-root `.secrets`, which is gitignored.
- When pushing with a token, sanitize the token from any output.
- Design specs live in `docs/superpowers/specs/`.
- Implementation plans live in `docs/superpowers/plans/`.
