# DM_Screenshot — Working Notes for Claude

Native macOS screenshot & annotation app (`mac/`, Swift / SwiftUI / AppKit) plus a
Windows port (`windows/`, C# / .NET / WPF). **macOS is the behavioral source of truth.**

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

## Repo & docs

- Source-of-truth repo: self-hosted **GitLab** (`origin`). Access token lives in
  the repo-root **`.secrets`** (gitignored). Push via
  `oauth2:$TOKEN@gitlab.schwabe.info/...` and **sanitize the token from any
  output**.
- Design specs: `docs/superpowers/specs/`. Implementation plans:
  `docs/superpowers/plans/`.
