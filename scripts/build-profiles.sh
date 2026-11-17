#!/usr/bin/env bash
# Builds the two viewer profiles from the same renderer:
#
#   scripts/build-profiles.sh linux   -> dist/linux   (X11 window + BMP capture, net10.0)
#   scripts/build-profiles.sh tizen   -> dist/tizen   (Tizen NUI app for the TV, net10.0)
#   scripts/build-profiles.sh all     -> both
#
# The Tizen profile needs tizen/framework/*.dll (scripts/fetch-tizen-refs.sh) and is
# deployed with scripts/deploy-tizen.sh.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DOTNET="${DOTNET:-$HOME/.dotnet/dotnet}"
CONFIG="${CONFIG:-Release}"
TARGET="${1:-all}"

stage() {
  local project="$1" dest="$2" out="$1/bin/$CONFIG/net10.0"
  rm -rf "$dest"
  mkdir -p "$dest"
  cp "$ROOT"/"$out"/*.dll "$ROOT"/"$out"/*.json "$dest/"
  echo "$project -> ${dest#"$ROOT"/}"
}

build_linux() {
  "$DOTNET" build "$ROOT/tools/FcModelViewer" -c "$CONFIG" -v q --nologo
  stage tools/FcModelViewer "$ROOT/dist/linux"
}

build_tizen() {
  if [ ! -f "$ROOT/tizen/framework/Tizen.NUI.dll" ]; then
    echo "tizen/framework is empty - run scripts/fetch-tizen-refs.sh first" >&2
    exit 1
  fi
  "$DOTNET" build "$ROOT/tools/FcModelViewer.Tizen" -c "$CONFIG" -v q --nologo
  stage tools/FcModelViewer.Tizen "$ROOT/dist/tizen"
}

case "$TARGET" in
  linux) build_linux ;;
  tizen) build_tizen ;;
  all) build_linux; build_tizen ;;
  *) echo "usage: $0 [linux|tizen|all]" >&2; exit 2 ;;
esac
