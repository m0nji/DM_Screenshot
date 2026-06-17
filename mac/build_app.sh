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
[ -f Resources/AppIcon.icns ] && cp Resources/AppIcon.icns "$APP/Contents/Resources/AppIcon.icns" || true

# Prefer a STABLE self-signed identity (keeps macOS Screen Recording permission across
# rebuilds). Falls back to ad-hoc. Create the identity once with ./make_cert.sh.
SIGN_ID="${DMSHOT_SIGN_ID:-DMShot Dev}"
if security find-identity -v -p codesigning 2>/dev/null | grep -q "$SIGN_ID"; then
    echo "==> codesign with stable identity: $SIGN_ID"
    codesign --force --deep --sign "$SIGN_ID" "$APP"
else
    echo "==> ad-hoc codesign (no '$SIGN_ID' identity; run ./make_cert.sh for a persistent permission)"
    codesign --force --deep --sign - "$APP"
fi

echo "==> done: $APP"
