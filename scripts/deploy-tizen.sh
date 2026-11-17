#!/usr/bin/env bash
# Deploys and controls the Tizen profile on the Samsung TV (GU43U7099F, Tizen 9, armv7l).
#
#   scripts/deploy-tizen.sh deploy     push dist/tizen + the mission archive, refresh deps
#   scripts/deploy-tizen.sh start      launch through Arm32ManualMapper (.NET 10)
#   scripts/deploy-tizen.sh stop       stop the viewer (only mapper-hosted processes)
#   scripts/deploy-tizen.sh log        tail the app log
#   scripts/deploy-tizen.sh shot <png> live screen capture via DrmCapture
#
# Requires the persistent TV root agent (127.0.0.1:43175) and TVAGENT pointing at its client.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
AGENT="${TVAGENT:-$HOME/hidrive/linux_sync/operations/samsung_tv/tvagent.py}"
SHOT="${TV_SHOT:-$HOME/hidrive/linux_sync/operations/samsung_tv/tv-shot.py}"
B=/opt/usr/home/owner/share/tmp
APPDIR="$B/fcview/app"
ASSETS="$APPDIR/assets"
MIS="${MIS:-$ROOT/build/disc/files/CW/M1A1.MIS}"
ENV_VARS="${FC_VIEW_ENV:-FC_VIEW_ASSEMBLY=1 FC_VIEW_TEXTURED=1}"

tv() { python3 "$AGENT" cmd "$1"; }

case "${1:-}" in
  deploy)
    [ -f "$ROOT/dist/tizen/FcModelViewer.Tizen.dll" ] || { echo "run scripts/build-profiles.sh tizen first" >&2; exit 1; }
    [ -f "$MIS" ] || { echo "mission archive missing: $MIS" >&2; exit 1; }
    tv "mkdir -p $APPDIR $ASSETS && rm -f $APPDIR/*.dll $APPDIR/*.json"
    for f in "$ROOT"/dist/tizen/*.dll "$ROOT"/dist/tizen/*.json; do
      python3 "$AGENT" put "$f" "$APPDIR/$(basename "$f")"
    done
    python3 "$AGENT" put "$MIS" "$ASSETS/M1A1.MIS"
    # Runtime dependencies the app needs next to itself: the TV's own Tizen framework.
    tv "cp $B/sdk_tools/Tizen.NUI.dll $B/sdk_tools/Tizen.dll $B/sdk_tools/Tizen.Applications.Common.dll $B/sdk_tools/Tizen.Tracer.dll $B/sdk_tools/Tizen.System.dll $APPDIR/ && cp $B/mech3d-net10/Tizen.Log.dll $APPDIR/ 2>/dev/null; ls -l $APPDIR | tail -12"
    ;;
  start)
    CMD="for p in \$(pgrep -f '^/usr/bin/dotnet ./Arm32ManualMapper'); do kill \$p 2>/dev/null; done; rm -f /tmp/fcview-app.log; sleep 1; "
    CMD+="( cd $B/net8-lab && $ENV_VARS setsid /usr/bin/dotnet ./Arm32ManualMapper.dll --run $B/sdk_tools/coreclr10 $APPDIR/FcModelViewer.Tizen.dll > /tmp/fcview.log 2>&1 < /dev/null & ) > /dev/null 2>&1; echo started"
    tv "$CMD"
    ;;
  stop)
    tv "for p in \$(pgrep -f '^/usr/bin/dotnet ./Arm32ManualMapper'); do kill \$p 2>/dev/null; done; sleep 1; pgrep -f '^/usr/bin/dotnet ./Arm32ManualMapper' || echo stopped"
    ;;
  log)
    tv "tail -40 /tmp/fcview.log"
    ;;
  shot)
    python3 "$SHOT" "${2:-$ROOT/dist/tv-live.png}"
    ;;
  *)
    echo "usage: $0 [deploy|start|stop|log|shot <png>]" >&2
    exit 2
    ;;
esac
