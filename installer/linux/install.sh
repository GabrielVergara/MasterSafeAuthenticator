#!/usr/bin/env bash
# Installs MasterSafe Authenticator for the current user (no root needed).
# Files go to ~/.local/opt/masterSafe-authenticator, with a launcher in ~/.local/bin and a menu entry.
# Uninstall with: ./install.sh --uninstall   (your vault in ~/.local/share/MasterSafe is kept)
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PREFIX="${HOME}/.local"
APP_DIR="$PREFIX/opt/masterSafe-authenticator"
BIN="$PREFIX/bin/masterSafe-authenticator"
DESKTOP="$PREFIX/share/applications/masterSafe-authenticator.desktop"
ICON="$PREFIX/share/icons/hicolor/256x256/apps/masterSafe-authenticator.png"

if [[ "${1:-}" == "--uninstall" ]]; then
  rm -rf "$APP_DIR" "$BIN" "$DESKTOP" "$ICON"
  echo "Removed. Your vault in ~/.local/share/MasterSafe was not touched."
  exit 0
fi

mkdir -p "$APP_DIR" "$PREFIX/bin" "$PREFIX/share/applications" "$(dirname "$ICON")"
rm -rf "$APP_DIR"/*
cp -r "$HERE/app/." "$APP_DIR/"
cp "$HERE/LICENSE" "$APP_DIR/LICENSE"
chmod +x "$APP_DIR/MasterSafe.App"
cp "$HERE/masterSafe-authenticator.png" "$ICON"

ln -sf "$APP_DIR/MasterSafe.App" "$BIN"
sed "s|@EXEC@|$APP_DIR/MasterSafe.App|" "$HERE/masterSafe-authenticator.desktop" > "$DESKTOP"
command -v update-desktop-database >/dev/null && update-desktop-database "$PREFIX/share/applications" || true

echo "Installed. Start it from your applications menu or run: masterSafe-authenticator"
