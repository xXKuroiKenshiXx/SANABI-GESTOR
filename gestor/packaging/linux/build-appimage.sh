#!/usr/bin/env bash
# Genera dist/SanabiGestor-x86_64.AppImage (necesita el SDK de .NET 10).
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../../.." && pwd)"
HERE="$ROOT/gestor/packaging/linux"
OUT="$ROOT/dist"
APPDIR="$OUT/AppDir"
TOOL_VERSION="1.9.1"

case "$APPDIR" in "$ROOT/dist/AppDir") ;; *) echo "Destino de empaquetado inválido" >&2; exit 1;; esac
rm -rf "$APPDIR"
mkdir -p "$APPDIR/usr/bin"
if [[ "${1:-}" == "--from-publish" ]]; then
  PUBLISHED="$(realpath "${2:?Falta carpeta publicada}")"
  test -f "$PUBLISHED/SanabiGestor" && test -f "$PUBLISHED/SanabiGestor.dll"
  cp -a "$PUBLISHED/." "$APPDIR/usr/bin/"
  rm -f "$APPDIR/usr/bin/LEEME.txt"
else
  VERSION_ARGS=()
  if [[ "${GITHUB_REF_TYPE:-}" == "tag" ]]; then VERSION_ARGS=(-p:Version="${GITHUB_REF_NAME#v}"); fi
  dotnet publish -p:GitHubClientId="${SANABI_GESTOR_GITHUB_CLIENT_ID:-}" "$ROOT/gestor" -c Release -r linux-x64 --self-contained -o "$APPDIR/usr/bin" "${VERSION_ARGS[@]}"
fi
chmod +x "$APPDIR/usr/bin/SanabiGestor"

install -m 755 "$HERE/AppRun" "$APPDIR/AppRun"
cp "$HERE/sanabi-gestor.desktop" "$APPDIR/sanabi-gestor.desktop"
cp "$ROOT/gestor/Assets/icon.png" "$APPDIR/sanabi-gestor.png"
ln -sf sanabi-gestor.png "$APPDIR/.DirIcon"

TOOL="$OUT/appimagetool-$TOOL_VERSION"
if [ ! -x "$TOOL" ]; then
  curl -fsSL -o "$TOOL" \
    "https://github.com/AppImage/appimagetool/releases/download/$TOOL_VERSION/appimagetool-x86_64.AppImage"
  chmod +x "$TOOL"
fi

ARCH=x86_64 "$TOOL" --appimage-extract-and-run --no-appstream "$APPDIR" "$OUT/SanabiGestor-x86_64.AppImage"
echo "Listo: $OUT/SanabiGestor-x86_64.AppImage"
