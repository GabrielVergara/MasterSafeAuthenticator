#!/usr/bin/env bash
# Builds a .deb (Debian, Ubuntu, Linux Mint) from the release tar.gz made by build-linux.sh, so it holds the same files.
#   installer/linux/build-deb.sh <MasterSafe-Authenticator-X.Y.Z-linux-x64|arm64.tar.gz>
# The result is next to the archive. Needs dpkg-deb; on Windows/macOS run it in a Debian container (see docs/Distribution.md).
set -euo pipefail

ARCHIVE="$(cd "$(dirname "$1")" && pwd)/$(basename "$1")"
base="$(basename "$ARCHIVE" .tar.gz)"            # MasterSafe-Authenticator-1.7.1-linux-x64
case "$base" in
  *-linux-x64)   ARCH=amd64 ;;
  *-linux-arm64) ARCH=arm64 ;;
  *) echo "Unexpected archive name: $base" >&2; exit 1 ;;
esac
VERSION="${base#MasterSafe-Authenticator-}"; VERSION="${VERSION%-linux-*}"
PKG=mastersafe-authenticator   # Debian package names are lowercase
NAME=masterSafe-authenticator   # same file names as the tar.gz install
WORK="$(mktemp -d)"; trap 'rm -rf "$WORK"' EXIT
tar -xzf "$ARCHIVE" -C "$WORK"
SRC="$WORK/$base"
ROOT="$WORK/deb"

install -d "$ROOT/DEBIAN" "$ROOT/opt/$NAME" "$ROOT/usr/bin" "$ROOT/usr/share/applications" \
  "$ROOT/usr/share/icons/hicolor/256x256/apps" "$ROOT/usr/share/doc/$PKG"
cp -r "$SRC/app/." "$ROOT/opt/$NAME/"
chmod 755 "$ROOT/opt/$NAME/MasterSafe.App"
ln -s "/opt/$NAME/MasterSafe.App" "$ROOT/usr/bin/$NAME"
sed 's|^Exec=.*|Exec=/usr/bin/masterSafe-authenticator|' "$SRC/masterSafe-authenticator.desktop" \
  > "$ROOT/usr/share/applications/$NAME.desktop"
cp "$SRC/masterSafe-authenticator.png" "$ROOT/usr/share/icons/hicolor/256x256/apps/$NAME.png"
cp "$SRC/LICENSE" "$ROOT/usr/share/doc/$PKG/copyright"

SIZE=$(du -sk "$ROOT" | cut -f1)
cat > "$ROOT/DEBIAN/control" <<CTL
Package: $PKG
Version: $VERSION
Section: utils
Priority: optional
Architecture: $ARCH
Installed-Size: $SIZE
Depends: libc6, libx11-6, libfontconfig1, libice6, libsm6
Recommends: libicu74 | libicu72 | libicu70 | libicu76
Maintainer: Gabriel Vergara <noreply@users.noreply.github.com>
Homepage: https://github.com/GabrielVergara/MasterSafeAuthenticator
Description: Privacy-first, offline-first authenticator and password vault
 MasterSafe Authenticator keeps TOTP codes and passwords in an encrypted vault
 on your computer. Nothing is sent anywhere unless you turn a feature on.
 Your vault in ~/.local/share/MasterSafe is kept when the package is removed.
CTL
cat > "$ROOT/DEBIAN/postinst" <<'S'
#!/bin/sh
set -e
command -v update-desktop-database >/dev/null && update-desktop-database -q /usr/share/applications || true
command -v gtk-update-icon-cache >/dev/null && gtk-update-icon-cache -q -t /usr/share/icons/hicolor || true
S
cp "$ROOT/DEBIAN/postinst" "$ROOT/DEBIAN/postrm"
chmod 755 "$ROOT/DEBIAN/postinst" "$ROOT/DEBIAN/postrm"

OUT="$(dirname "$ARCHIVE")/${PKG}_${VERSION}_${ARCH}.deb"
dpkg-deb --root-owner-group --build "$ROOT" "$OUT" >/dev/null
( cd "$(dirname "$OUT")" && sha256sum "$(basename "$OUT")" > "$(basename "$OUT").sha256" )
echo "Done: $OUT"
