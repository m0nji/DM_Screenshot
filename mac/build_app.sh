#!/bin/bash
# Build DMShot and assemble a runnable .app bundle (ad-hoc signed for local dev).
set -euo pipefail

cd "$(dirname "$0")"
CONFIG="${1:-release}"

echo "==> swift build -c $CONFIG"
swift build -c "$CONFIG"

BIN="$(swift build -c "$CONFIG" --show-bin-path)/DMShot"
APP="build/DM_Screenshot.app"

echo "==> assembling $APP"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN" "$APP/Contents/MacOS/DMShot"
cp Info.plist "$APP/Contents/Info.plist"
cp ../CHANGELOG.md "$APP/Contents/Resources/CHANGELOG.md"
[ -f Resources/AppIcon.icns ] && cp Resources/AppIcon.icns "$APP/Contents/Resources/AppIcon.icns" || true

# Embed Sparkle.framework so the app can run + auto-update.
BIN_DIR="$(swift build -c "$CONFIG" --show-bin-path)"
mkdir -p "$APP/Contents/Frameworks"
cp -R "$BIN_DIR/Sparkle.framework" "$APP/Contents/Frameworks/"
# Ensure the executable can find embedded frameworks.
install_name_tool -add_rpath "@executable_path/../Frameworks" "$APP/Contents/MacOS/DMShot" 2>/dev/null || true

# Prefer a STABLE self-signed identity (keeps macOS Screen Recording permission across
# rebuilds). Falls back to ad-hoc. Create the identity once with ./make_cert.sh.
SIGN_ID="${DMSHOT_SIGN_ID:-DMShot Dev}"
if security find-identity -v -p codesigning 2>/dev/null | grep -q "$SIGN_ID"; then
    echo "==> codesign with stable identity: $SIGN_ID"
    codesign --force --deep --sign "$SIGN_ID" "$APP"
else
    echo "==> ad-hoc codesign (no '$SIGN_ID' identity; run ./make_cert.sh for a persistent permission)"
    codesign --force --deep --sign - "$APP"
    # Ad-hoc hash changes every build -> macOS keeps a stale Screen Recording grant
    # that can't be re-toggled. Reset it so the next capture prompts fresh (1 click).
    tccutil reset ScreenCapture de.dmscreenshot.app >/dev/null 2>&1 \
        && echo "==> reset Screen Recording permission (re-grant on next capture)"
fi

echo "==> done: $APP"
