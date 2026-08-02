#!/usr/bin/env bash
# Bump the release version everywhere it is written down.
#
#   scripts/bump-version.sh 0.9.0
#
# The version lives in FOUR places. Windows reads `VERSION` at build time
# (DMShot.csproj), so it always agrees. macOS carries two hand-maintained
# copies — `mac/Info.plist` and the fallback in `mac/Sources/DMShot/App.swift` —
# which only `swift test` catches (VersionConsistencyTests). Releases 0.8.4 and
# 0.8.5 touched only VERSION + CHANGELOG, were cut from a Windows device where
# the macOS suite never runs, and shipped with `main` red.
#
# This script exists so that cannot depend on anyone remembering all four. The
# CI gate in .github/workflows/release.yml is the second half of the same fix.
#
# It does NOT commit, tag or push — review the diff, then commit as usual.
set -euo pipefail
cd "$(cd "$(dirname "$0")/.." && pwd)"

NEW="${1:-}"
[ -n "$NEW" ] || { echo "usage: scripts/bump-version.sh X.Y.Z" >&2; exit 2; }

# Reject anything that is not a bare X.Y.Z — a typo like "v0.9" must not
# half-apply a bump across four files.
if ! printf '%s' "$NEW" | grep -Eq '^[0-9]+\.[0-9]+\.[0-9]+$'; then
  echo "error: '$NEW' is not a version of the form X.Y.Z (no leading 'v')" >&2
  exit 2
fi

OLD="$(tr -d '[:space:]' < VERSION)"
if [ "$NEW" = "$OLD" ]; then
  echo "error: VERSION is already $OLD" >&2
  exit 2
fi

# Refuse to go backwards: Sparkle would never offer the result as an update, so
# a "release" that goes down is a mistake every time.
if [ "$(printf '%s\n%s\n' "$OLD" "$NEW" | sort -V | tail -1)" != "$NEW" ]; then
  echo "error: $NEW is older than the current $OLD" >&2
  exit 2
fi

PLIST="mac/Info.plist"
APP_SWIFT="mac/Sources/DMShot/App.swift"
CHANGELOG="CHANGELOG.md"
for f in "$PLIST" "$APP_SWIFT" "$CHANGELOG"; do
  [ -f "$f" ] || { echo "error: $f not found" >&2; exit 1; }
done

printf '%s\n' "$NEW" > VERSION

# Both CFBundleShortVersionString and CFBundleVersion. PlistBuddy addresses the
# keys by name, so this cannot hit an unrelated string that happens to match.
/usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString $NEW" "$PLIST"
/usr/libexec/PlistBuddy -c "Set :CFBundleVersion $NEW" "$PLIST"

# The fallback used by `swift run` (unbundled), where there is no Info.plist.
perl -0pi -e "s/\Q?? \"$OLD\"\E/?? \"$NEW\"/g" "$APP_SWIFT"

# Move the pending entries under a dated heading and leave [Unreleased] empty
# for the next change.
TODAY="$(date +%Y-%m-%d)"
perl -0pi -e "s/^## \[Unreleased\]\n/## [Unreleased]\n\n## $NEW – $TODAY\n/m" "$CHANGELOG"

echo "==> $OLD -> $NEW"
echo "    VERSION, $PLIST (2 keys), $APP_SWIFT, $CHANGELOG"
echo "    review the diff, then commit; nothing was committed or tagged"
