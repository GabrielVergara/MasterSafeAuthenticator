#!/usr/bin/env bash
# Builds a self-contained Linux package (tar.gz) for MasterSafe Authenticator.
#   installer/linux/build-linux.sh [version] [rid]     rid: linux-x64 (default) or linux-arm64
# Works on Linux, macOS and Windows (Git Bash). The result is in artifacts/.
set -euo pipefail

VERSION="${1:-1.2.0}"
RID="${2:-linux-x64}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
OUT="$ROOT/artifacts/$RID"
NAME="MasterSafe-Authenticator-$VERSION-$RID"
STAGE="$ROOT/artifacts/$NAME"

rm -rf "$OUT" "$STAGE"
dotnet publish "$ROOT/src/MasterSafe.App/MasterSafe.App.csproj" \
  -c Release -r "$RID" --self-contained \
  -p:MasterSafeTargetOs=linux -p:Version="$VERSION" \
  -o "$OUT"

mkdir -p "$STAGE/app"
cp -r "$OUT"/. "$STAGE/app/"
cp "$ROOT/LICENSE" "$STAGE/LICENSE"
cp "$ROOT/src/MasterSafe.App/Assets/logo-256.png" "$STAGE/masterSafe-authenticator.png"
cp "$ROOT/installer/linux/masterSafe-authenticator.desktop" "$STAGE/"
cp "$ROOT/installer/linux/install.sh" "$STAGE/install.sh"
chmod +x "$STAGE/install.sh" "$STAGE/app/MasterSafe.App"

tar -C "$ROOT/artifacts" -czf "$ROOT/artifacts/$NAME.tar.gz" "$NAME"
( cd "$ROOT/artifacts" && sha256sum "$NAME.tar.gz" > "$NAME.tar.gz.sha256" )
echo "Done: artifacts/$NAME.tar.gz"
