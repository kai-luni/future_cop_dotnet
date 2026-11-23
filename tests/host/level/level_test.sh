#!/bin/bash
# Run the unchanged terrain units of future-cop-lapd-ps1 against the vectors shared
# with the .NET port (future_cop_dotnet/tests/golden/level.vectors).
#   level_test.sh            compare
#   level_test.sh --emit     print every vector line with the observed outputs
# Nothing is written into either repository: OUT defaults to a temporary directory.
# This harness stays in the .NET repo; it reads the reference C repo and toolchain read-only.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
DECOMP="${DECOMP:-$HOME/git/future-cop-lapd-ps1}"
DOTNET="${DOTNET_REPO:-$HOME/git/future_cop_dotnet}"
T="$DECOMP/.toolchain"
OUT="${OUT:-$(mktemp -d "${TMPDIR:-/tmp}/level-harness.XXXXXX")}"
QEMU="${QEMU:-$HOME/.local/share/qemu-user/extracted/usr/bin/qemu-mipsel}"
VECTORS="${LEVEL_VECTORS:-$DOTNET/tests/golden/level.vectors}"
DISC="${DISC:-$DOTNET/build/disc/files}"
SLUS="${SLUS:-$DECOMP/baseroms/us/SLUS_007.39}"
GCC="$T/gcc-2.7.2-cdk/gcc"
MASPSX="$(echo "$T"/maspsx-*/maspsx.py)"
mkdir -p "$OUT"
export PATH="$T/mipsel-binutils/root/usr/bin:$PATH"
export LD_LIBRARY_PATH="$T/mipsel-binutils/root/usr/lib/x86_64-linux-gnu${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"

MIS_REL="$(awk '$1 == "MIS" { print $2; exit }' "$VECTORS")"
[ -n "$MIS_REL" ] || { echo "level: vectors name no MIS" >&2; exit 2; }
[ -f "$DISC/$MIS_REL" ] || { echo "level: retail mission $DISC/$MIS_REL missing" >&2; exit 2; }
python3 -I "$HERE/extract_level.py" "$DISC/$MIS_REL" "$OUT/level.blob"

compile_s() {
    "$GCC" -B"$T/gcc-2.7.2-cdk/" -I"$DECOMP/include" -O1 -G0 -S "$1" -o "$OUT/tmp.s"
    python3 "$MASPSX" "$OUT/tmp.s" | python3 "$DECOMP/tests/host/fix_align.py" > "$2"
}

UNITS="80063DC8 800639D4 800646BC 80065134 800648E8 800648BC 80066F78 80067480 80064460 8006429C 800640D8 80063FF8 8006411C 800833A8"
OBJS=""
for u in $UNITS; do
    compile_s "$DECOMP/src/game/$u.c" "$OUT/$u.mas.s"
    mipsel-linux-gnu-as -o "$OUT/$u.o" "$OUT/$u.mas.s" 2>"$OUT/$u.as.log"
    OBJS="$OBJS $OUT/$u.o"
done
compile_s "$HERE/level_harness.c" "$OUT/harness.mas.s"
mipsel-linux-gnu-as -o "$OUT/harness.o" "$OUT/harness.mas.s" 2>"$OUT/harness.as.log"
mipsel-linux-gnu-as -o "$OUT/crt0.o" "$DECOMP/tests/host/crt0.s" 2>/dev/null
printf '.section .retailram,"aw",@nobits\n.globl D_RAM\nD_RAM:\n.space 0xA0000\n' > "$OUT/ram.s"
printf '.section .scratch,"aw",@nobits\n.globl SCRATCH\nSCRATCH:\n.space 0x400\n' > "$OUT/scratch.s"
mipsel-linux-gnu-as -o "$OUT/ram.o" "$OUT/ram.s"
mipsel-linux-gnu-as -o "$OUT/scratch.o" "$OUT/scratch.s"

# Every retail data symbol lands at (retail address - 0x70000000) inside .retailram.
DEFS=""
for s in $(mipsel-linux-gnu-nm -u $OBJS "$OUT/harness.o" | awk '{print $2}' | grep -E '^D_8[0-9A-F]{7}$' | sort -u); do
    DEFS="$DEFS --defsym=$s=$(printf '0x%X' $((0x${s#D_} - 0x70000000)))"
done
mipsel-linux-gnu-ld -static -e __start --section-start=.retailram=0x10010000 \
    --section-start=.scratch=0x1F800000 $DEFS \
    -o "$OUT/level_harness" "$OUT/crt0.o" "$OUT/harness.o" $OBJS "$OUT/ram.o" "$OUT/scratch.o"
"$QEMU" "$OUT/level_harness" "$VECTORS" "$OUT/level.blob" "$SLUS" "$@"
