#!/usr/bin/env bash
# Publish the current `main` source to the public GitHub repo as a source-only
# snapshot (internal docs/ and CLAUDE.md excluded, fresh single-commit history) and optionally
# cut a release tag that triggers the notarization CI.
#
# Usage (from anywhere inside the repo):
#   scripts/sync-to-github.sh            # update github:main with the latest source
#   scripts/sync-to-github.sh v0.2.0     # ...and tag v0.2.0 to build + notarize + publish a release
#
# GitLab `origin` stays the private source of truth (full history + docs);
# GitHub `github` is the public source mirror + releases.
# See project memory: dm-screenshot-release.
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"

GITHUB_REMOTE="github"
EXCLUDE=("docs" "CLAUDE.md") # paths kept OUT of the public repo (and its history)
TMP_BRANCH="_github_snapshot"
TAG="${1:-}"

[ "$(git branch --show-current)" = "main" ] \
  || { echo "error: switch to 'main' first"; exit 1; }
git diff --quiet && git diff --cached --quiet \
  || { echo "error: commit or stash your changes on main first"; exit 1; }
git remote get-url "$GITHUB_REMOTE" >/dev/null 2>&1 \
  || { echo "error: no '$GITHUB_REMOTE' remote configured"; exit 1; }

SRC="$(git rev-parse --short HEAD)"

# Build a fresh orphan snapshot of main's tree minus the excluded paths, so the
# excluded files never appear in any GitHub commit or in its history.
git checkout --orphan "$TMP_BRANCH" >/dev/null 2>&1
for p in "${EXCLUDE[@]}"; do
  git rm -r --cached --quiet "$p" >/dev/null 2>&1 || true
done
git commit -q -m "DM_Screenshot source snapshot (main $SRC)"

git push -f "$GITHUB_REMOTE" "$TMP_BRANCH:main"

if [ -n "$TAG" ]; then
  git tag -f "$TAG"
  git push -f "$GITHUB_REMOTE" "$TAG"
fi

# Back to main; clean up the temp branch and any leftover untracked excluded files.
git checkout -f main >/dev/null 2>&1
git branch -D "$TMP_BRANCH" >/dev/null 2>&1 || true

echo "✓ synced main ($SRC) → $GITHUB_REMOTE:main (source-only; excluded: ${EXCLUDE[*]})"
[ -n "$TAG" ] && echo "✓ tagged $TAG → release CI: https://github.com/m0nji/DM_Screenshot/actions"
exit 0
