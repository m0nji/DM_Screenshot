# DM_Screenshot — Working Notes for Claude

Native macOS screenshot & annotation app (`mac/`, Swift / SwiftUI / AppKit) plus a
Windows port (`windows/`, C# / .NET / WPF). **macOS is the behavioral source of truth.**

## BrandDesign App Icons

- App icons must use the shared DM BrandDesign system from /Users/thomas/Projects/DM_BrandDesign.
- Check /Users/thomas/Projects/DM_BrandDesign/packages/assets/icons/screenshot-color-modern-source.svg before changing icon geometry, colors, or effects.
- Keep the editable macOS/source art at 1024x1024 with the BrandDesign safe area: the squircle is x=100, y=100, width=824, height=824.
- Windows exports must be full-bleed compared with the macOS source. Keep WIN_FILL at about 1.214 and include explicit .ico frames for taskbar sizes.
- Do not redraw the icon locally. Update the BrandDesign source first, then regenerate app assets from that source.
- macOS source: mac/Resources/AppIcon.svg, regenerated with mac/make_icon.sh.
- Windows app icon: windows/DMShot/Resources/AppIcon.ico, regenerated from the same SVG source with windows/tools/make-app-icon.mjs or windows/tools/make-app-icon.ps1.

## Git workflow (IMPORTANT — read before committing)

- **One branch per feature or change.** Branch off `main` (`feat/…`, `fix/…`,
  `chore/…`), do the work there, and merge back to `main` **only when it is
  complete and verified** (builds + tests pass, and — for behavior changes —
  manually checked). Do **not** commit work-in-progress directly to `main`.
- **Never run two Claude/agent sessions in the same working copy at the same
  time.** Concurrent sessions sharing one checkout switch the branch under each
  other, so commits silently land on the wrong branch (this has happened). Each
  concurrent session MUST use its own **git worktree** (a separate directory):
  `git worktree add ../dmshot-<topic> -b <branch>`. A single session can just use
  a normal branch.
- `main` is **protected** on GitLab (no force-push). **Push only when the user
  asks.** When pushing, never overwrite others' work — prefer fast-forward.

## Build & test (macOS)

- Fast syntax check: `cd mac && swift build`
- Unit tests: `cd mac && swift test`  (run before every commit)
- Runnable app: `cd mac && ./build_app.sh release` → `mac/build/DM_Screenshot.app`
  (ad-hoc signed; this **resets the Screen Recording grant** each build, so the
  next capture re-prompts — run `mac/make_cert.sh` once for a stable identity).
- The agent cannot grant Screen Recording or see capture output; the **user must
  verify** recording / GIF / paste behavior on a real machine.

## Parity

Every user-facing behavior change must land on **both** `mac/` and `windows/` in
the same change, or be explicitly deferred with a TODO referencing
`docs/PARITY.md`. macOS is the source of truth; Windows mirrors it.

## Localization

The app ships **English (default)** and **German**. **No user-facing string literal
may live directly in a view / menu / tooltip / alert** — route it through `L`/`tr`
(macOS, `mac/Sources/DMShot/Localization.swift`) or `Loc`/`{loc:Tr}` (Windows,
`windows/DMShot/Localization/Loc.cs`). Every new string must add **both** languages:
macOS enforces this at compile time (non-exhaustive `switch` in `Localizer`), Windows
via the `LocTests` key-parity test. Language display names (`English` / `Deutsch`) are
never translated. First-run default is always English regardless of OS language.
Switching is live (no restart).

## Repo & docs

- Source-of-truth repo: self-hosted **GitLab** (`origin`). Access token lives in
  the repo-root **`.secrets`** (gitignored). Push via
  `oauth2:$TOKEN@gitlab.schwabe.info/...` and **sanitize the token from any
  output**.
- Design specs: `docs/superpowers/specs/`. Implementation plans:
  `docs/superpowers/plans/`.
