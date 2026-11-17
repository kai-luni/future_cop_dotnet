#!/usr/bin/env bash
# Copies the Tizen assemblies the TV build compiles against into tizen/framework/.
# They are read from the TV itself (the signed system framework), never committed.
#
#   scripts/fetch-tizen-refs.sh [agent-client]
#
# The default agent client talks to the persistent TV root agent on 127.0.0.1:43175.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
AGENT="${1:-$HOME/hidrive/linux_sync/operations/samsung_tv/tvagent.py}"
SRC="${TV_FRAMEWORK_DIR:-/opt/usr/home/owner/share/tmp/sdk_tools}"
DEST="$ROOT/tizen/framework"

mkdir -p "$DEST"
for dll in Tizen.NUI.dll Tizen.dll Tizen.Applications.Common.dll Tizen.Tracer.dll Tizen.System.dll; do
  python3 "$AGENT" get "$SRC/$dll" "$DEST/$dll"
done
ls -l "$DEST"
