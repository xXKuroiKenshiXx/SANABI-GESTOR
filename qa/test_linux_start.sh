#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
TEST_DIR="$(mktemp -d "$ROOT/mods/_qa/linux-window-XXXXXX")"
case "$TEST_DIR" in "$ROOT/mods/_qa/linux-window-"*) ;; *) exit 1;; esac
trap 'rm -rf "$TEST_DIR"' EXIT
cp "$ROOT/dist/SanabiGestor-x86_64.AppImage" "$TEST_DIR/gestor.AppImage"
chmod +x "$TEST_DIR/gestor.AppImage"
set +e
APPIMAGE_EXTRACT_AND_RUN=1 timeout 6s xvfb-run -a "$TEST_DIR/gestor.AppImage" > "$TEST_DIR/startup.log" 2>&1
RESULT=$?
set -e
if [[ "$RESULT" != 124 ]]; then cat "$TEST_DIR/startup.log"; echo "AppImage terminó inesperadamente: $RESULT" >&2; exit 1; fi
if grep -qi 'Unhandled exception\|Fatal error\|segmentation fault' "$TEST_DIR/startup.log"; then cat "$TEST_DIR/startup.log"; exit 1; fi
echo 'PASS: final AppImage native window stayed running under Xvfb without fatal startup errors; isolated portable settings.'
