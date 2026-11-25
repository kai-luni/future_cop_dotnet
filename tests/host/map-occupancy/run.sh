#!/bin/bash
# Run unchanged PS1 C map-occupancy units (8003F79C, 8003FA34, 8003B838, 8003B7D8, 8003BC0C,
# 8003BF50, 8003FAD4, 8003BC7C, 8003C404, 8003C590, 8003C500, 8003C4C4, 8003B3D8) with gcc 2.7.2
# and qemu-mipsel against shared golden vectors.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
DOTNET="$(cd "$HERE/../../.." && pwd)"
DECOMP="${DECOMP:-$HOME/git/future-cop-lapd-ps1}"
T="$DECOMP/.toolchain"
QEMU="${QEMU:-$HOME/.local/share/qemu-user/extracted/usr/bin/qemu-mipsel}"
OUT="$(mktemp -d "${TMPDIR:-/tmp}/map-occupancy.XXXXXXXX")"
trap 'rm -rf "$OUT"' EXIT
export PATH="$T/mipsel-binutils/root/usr/bin:$PATH"
export LD_LIBRARY_PATH="$T/mipsel-binutils/root/usr/lib/x86_64-linux-gnu${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
GCC="$T/gcc-2.7.2-cdk/gcc"
MASPSX=("$T"/maspsx-*/maspsx.py)
compile() {
    "$GCC" -B"$T/gcc-2.7.2-cdk/" -I"$DECOMP/include" -O2 -msoft-float -G0 -S "$1" -o "$OUT/tmp.s" 2>/dev/null
    python3 "${MASPSX[0]}" "$OUT/tmp.s" | python3 "$DECOMP/tests/host/fix_align.py" > "$OUT/tmp.mas.s"
    mipsel-linux-gnu-as -o "$2" "$OUT/tmp.mas.s"
}

UNITS=(8003F79C 8003FA34 8003B838 8003B7D8 8003BC0C 8003BF50 8003FAD4 8003BC7C 8003C404 8003C590 8003C500 8003C4C4 8003B3D8)
OBJS=()
for u in "${UNITS[@]}"; do
    compile "$DECOMP/src/game/$u.c" "$OUT/$u.o"
    OBJS+=("$OUT/$u.o")
done
compile "$HERE/harness.c" "$OUT/harness.o"
mipsel-linux-gnu-as -o "$OUT/crt0.o" "$DECOMP/tests/host/crt0.s"
printf '.section .retailram,"aw",@nobits\n.globl D_RAM\nD_RAM:\n.space 0xA0000\n' > "$OUT/ram.s"
mipsel-linux-gnu-as -o "$OUT/ram.o" "$OUT/ram.s"

DEFS=""
for s in $(mipsel-linux-gnu-nm -u "${OBJS[@]}" "$OUT/harness.o" | awk '{print $2}' \
           | grep -E '^D_8[0-9A-F]{7}$' | sort -u); do
    DEFS="$DEFS --defsym=$s=$(printf '0x%X' $((0x${s#D_} - 0x70000000)))"
done

mipsel-linux-gnu-ld -static -e __start --section-start=.retailram=0x10010000 $DEFS \
    -o "$OUT/map_occupancy" "$OUT/crt0.o" "$OUT/harness.o" "${OBJS[@]}" "$OUT/ram.o"
"$QEMU" "$OUT/map_occupancy" < "$DOTNET/tests/golden/map-occupancy.vectors"
printf 'map-occupancy: shared vectors PASS (unchanged C, qemu-mipsel)\n'
