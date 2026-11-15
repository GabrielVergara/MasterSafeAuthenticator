#!/usr/bin/env bash
# Installs MasterSafe Authenticator for the current user (no root needed).
# Files go to ~/.local/opt/masterSafe-authenticator, with a launcher in ~/.local/bin and a menu entry.
# Uninstall with: masterSafe-authenticator-uninstall   (or ./install.sh --uninstall; your vault in ~/.local/share/MasterSafe is kept)
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PREFIX="${HOME}/.local"
APP_DIR="$PREFIX/opt/masterSafe-authenticator"
BIN="$PREFIX/bin/masterSafe-authenticator"
UNINSTALL="$PREFIX/bin/masterSafe-authenticator-uninstall"
DESKTOP="$PREFIX/share/applications/masterSafe-authenticator.desktop"
ICON="$PREFIX/share/icons/hicolor/256x256/apps/masterSafe-authenticator.png"

# True when a copy installed here is running. It is found by the program it was started as (the launcher or the program
# file): the process name is the start name cut to 15 letters, and the app hides /proc/<pid>/exe (it is not dumpable).
is_running() {
  local first
  for proc in /proc/[0-9]*; do
    first="$(tr '\0' '\n' < "$proc/cmdline" 2>/dev/null | head -n 1)" || continue
    [[ "$first" == "$APP_DIR/MasterSafe.App" || "$first" == "$BIN" ]] && return 0
  done
  return 1
}

if [[ "${1:-}" == "--uninstall" ]]; then
  if is_running; then
    echo "MasterSafe is running. Choose Exit in its tray menu or window first, then run this again."
    exit 1
  fi
  rm -rf "$APP_DIR" "$BIN" "$UNINSTALL" "$DESKTOP" "$ICON"
  command -v update-desktop-database >/dev/null && update-desktop-database "$PREFIX/share/applications" || true
  echo "Removed. Your vault in ~/.local/share/MasterSafe was not touched."
  exit 0
fi

# Replacing the files under a running copy leaves that old copy in charge: a new start only wakes it up.
if is_running; then
  echo "MasterSafe is running. Choose Exit in its tray menu or window first (closing the window keeps it in the tray), then run this again."
  exit 1
fi

mkdir -p "$APP_DIR" "$PREFIX/bin" "$PREFIX/share/applications" "$(dirname "$ICON")"
rm -rf "$APP_DIR"/*
cp -r "$HERE/app/." "$APP_DIR/"
cp "$HERE/LICENSE" "$APP_DIR/LICENSE"
# A copy of this script stays installed, so it can be uninstalled without the downloaded folder.
cp "$HERE/install.sh" "$APP_DIR/uninstall.sh"
chmod +x "$APP_DIR/uninstall.sh"
printf '#!/usr/bin/env bash\nexec bash "%s/uninstall.sh" --uninstall\n' "$APP_DIR" > "$UNINSTALL"
chmod +x "$UNINSTALL"
chmod +x "$APP_DIR/MasterSafe.App"
cp "$HERE/masterSafe-authenticator.png" "$ICON"

ln -sf "$APP_DIR/MasterSafe.App" "$BIN"
sed "s|@EXEC@|$APP_DIR/MasterSafe.App|" "$HERE/masterSafe-authenticator.desktop" > "$DESKTOP"
command -v update-desktop-database >/dev/null && update-desktop-database "$PREFIX/share/applications" || true

echo "Installed. Start it from your applications menu or run: masterSafe-authenticator"
echo "To remove it later: masterSafe-authenticator-uninstall"
