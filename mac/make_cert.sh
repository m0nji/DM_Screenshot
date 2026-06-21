#!/bin/bash
# Create a STABLE self-signed code-signing identity so macOS TCC (Screen Recording)
# grants persist across rebuilds. Ad-hoc signing changes the code hash every build,
# which invalidates the permission. Run once.
set -euo pipefail

NAME="${1:-DMShot Dev}"
KEYCHAIN="$HOME/Library/Keychains/login.keychain-db"

if security find-identity -v -p codesigning | grep -q "$NAME"; then
    echo "code-signing identity '$NAME' already exists"
    exit 0
fi

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

cat > "$TMP/cfg" <<EOF
[req]
distinguished_name = dn
x509_extensions = v3
prompt = no
[dn]
CN = $NAME
[v3]
keyUsage = critical, digitalSignature
extendedKeyUsage = critical, codeSigning
basicConstraints = critical, CA:false
EOF

openssl req -x509 -newkey rsa:2048 -nodes -days 3650 \
    -keyout "$TMP/key.pem" -out "$TMP/cert.pem" -config "$TMP/cfg" >/dev/null 2>&1
openssl pkcs12 -export -out "$TMP/id.p12" -inkey "$TMP/key.pem" -in "$TMP/cert.pem" \
    -name "$NAME" -passout pass:dmshot >/dev/null 2>&1

# -A: allow all apps (incl. codesign) to use the key without a per-use prompt.
security import "$TMP/id.p12" -k "$KEYCHAIN" -P dmshot -A

echo "created code-signing identity '$NAME'"
security find-identity -v -p codesigning | grep "$NAME" || true
