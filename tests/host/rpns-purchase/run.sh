#!/bin/bash
# Run unchanged PS1 C func_80069D68 (80069D68.c) with gcc 2.7.2 and qemu-mipsel
# against the shared CONFT.MIS purchase script golden vectors.
#
# qemu-mipsel cannot map guest addresses >= 0x80000000. 80069D68.c references all
# globals via extern __asm__("D_800...") symbols, so --defsym relocates the entire
# PS1 RAM window by -0x70000000 into .retailram at 0x10010000 with zero literal
# rewrites needed (verified by objdump check below).
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
DOTNET="$(cd "$HERE/../../.." && pwd)"
DECOMP="${DECOMP:-$HOME/git/future-cop-lapd-ps1}"
T="$DECOMP/.toolchain"
QEMU="${QEMU:-$HOME/.local/share/qemu-user/extracted/usr/bin/qemu-mipsel}"
OUT="$(mktemp -d "${TMPDIR:-/tmp}/rpns-purchase.XXXXXXXX")"
trap 'rm -rf "$OUT"' EXIT
export PATH="$T/mipsel-binutils/root/usr/bin:$PATH"
export LD_LIBRARY_PATH="$T/mipsel-binutils/root/usr/lib/x86_64-linux-gnu${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
GCC="$T/gcc-2.7.2-cdk/gcc"
MASPSX=("$T"/maspsx-*/maspsx.py)
to_s() {
    "$GCC" -B"$T/gcc-2.7.2-cdk/" -I"$DECOMP/include" -O2 -msoft-float -G0 -S "$1" -o "$OUT/tmp.s" 2>/dev/null
    python3 "${MASPSX[0]}" "$OUT/tmp.s" | python3 "$DECOMP/tests/host/fix_align.py" > "$2"
}

to_s "$DECOMP/src/game/80069D68.c" "$OUT/original.mas.s"
mipsel-linux-gnu-as -o "$OUT/original.o" "$OUT/original.mas.s"
# Verify zero KSEG0 0x800x literal upper halves exist in the compiled VM unit.
left=$(mipsel-linux-gnu-objdump -d "$OUT/original.o" | grep -c 'lui.*0x800' || true)
[ "$left" = 0 ] || { echo "rpns-purchase: unexpected KSEG0 literal ($left)" >&2; exit 2; }

to_s "$HERE/harness.c" "$OUT/harness.mas.s"
mipsel-linux-gnu-as -o "$OUT/harness.o" "$OUT/harness.mas.s"
mipsel-linux-gnu-as -o "$OUT/crt0.o" "$DECOMP/tests/host/crt0.s"
printf '.section .retailram,"aw",@nobits\n.globl D_RAM\nD_RAM:\n.space 0xA0000\n' > "$OUT/ram.s"
mipsel-linux-gnu-as -o "$OUT/ram.o" "$OUT/ram.s"
DEFS=""
for s in $(mipsel-linux-gnu-nm -u "$OUT/original.o" "$OUT/harness.o" | awk '{print $2}' \
           | grep -E '^D_8[0-9A-F]{7}$' | sort -u); do
    DEFS="$DEFS --defsym=$s=$(printf '0x%X' $((0x${s#D_} - 0x70000000)))"
done
mipsel-linux-gnu-ld -static -e __start --section-start=.retailram=0x10010000 $DEFS \
    -o "$OUT/rpns_purchase" "$OUT/crt0.o" "$OUT/harness.o" "$OUT/original.o" "$OUT/ram.o"
"$QEMU" "$OUT/rpns_purchase" < "$DOTNET/tests/golden/rpns-purchase.vectors"
printf 'rpns-purchase: shared vectors PASS (unchanged C, qemu-mipsel)\n'
