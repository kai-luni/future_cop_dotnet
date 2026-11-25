#!/bin/bash
# Run unchanged PS1 C func_8002D664 message 5 (8002D664.c:179-225) with gcc 2.7.2 and qemu-mipsel
# against the shared golden vectors.
#
# qemu-mipsel cannot map guest addresses >= 0x80000000. 8002D664.c reaches D_8009FA0C, D_8009F9EC
# and D_8009FA94 through absolute literals (8002D664.c:185,187-189,192-193,199-202,208-209), not
# through symbols, so --defsym alone cannot move them. The compiled assembly therefore has exactly
# those literals rebased by -0x70000000, and every D_ symbol is placed at the same rebased address in
# a .retailram section - the relocation of the decomp's map_objects_harness.c:18-22 plus the literal
# rewrite of its precinct_assault_test.sh:31-37. The instruction sequence is unchanged: a literal
# address and its rebased form both expand to one lui and one load with the same low half.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
DOTNET="$(cd "$HERE/../../.." && pwd)"
DECOMP="${DECOMP:-$HOME/git/future-cop-lapd-ps1}"
T="$DECOMP/.toolchain"
QEMU="${QEMU:-$HOME/.local/share/qemu-user/extracted/usr/bin/qemu-mipsel}"
OUT="$(mktemp -d "${TMPDIR:-/tmp}/outpost-message5.XXXXXXXX")"
trap 'rm -rf "$OUT"' EXIT
export PATH="$T/mipsel-binutils/root/usr/bin:$PATH"
export LD_LIBRARY_PATH="$T/mipsel-binutils/root/usr/lib/x86_64-linux-gnu${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
GCC="$T/gcc-2.7.2-cdk/gcc"
MASPSX=("$T"/maspsx-*/maspsx.py)
to_s() {
    "$GCC" -B"$T/gcc-2.7.2-cdk/" -I"$DECOMP/include" -O2 -msoft-float -G0 -S "$1" -o "$OUT/tmp.s" 2>/dev/null
    python3 "${MASPSX[0]}" "$OUT/tmp.s" | python3 "$DECOMP/tests/host/fix_align.py" > "$2"
}

to_s "$DECOMP/src/game/8002D664.c" "$OUT/original.mas.s"
# Rebase the literal addresses; each expected count is the number of sites in the compiled unit.
python3 - "$OUT/original.mas.s" <<'EOF'
import sys
path = sys.argv[1]
text = open(path).read()
rebase = 0x70000000
sites = {
    # lw rX,<0x8009FA0C>: gPlayerCount (8002D664.c:185)
    "lw\t$3,-2146829812": ("lw\t$3,0x%X" % (0x8009FA0C - rebase), 1),
    # lw rX,<0x8009FA94>: gGameSession (8002D664.c:192-193, 208)
    "lw\t$4,-2146829676": ("lw\t$4,0x%X" % (0x8009FA94 - rebase), 1),
    "lw\t$6,-2146829676": ("lw\t$6,0x%X" % (0x8009FA94 - rebase), 1),
    # lw rX,<0x8009F9EC>: gSinglePlayerEntity (8002D664.c:199, 209)
    "lw\t$2,-2146829844": ("lw\t$2,0x%X" % (0x8009F9EC - rebase), 1),
    "lw\t$7,-2146829844": ("lw\t$7,0x%X" % (0x8009F9EC - rebase), 1),
    # base = 0x800A0000 (8002D664.c:187, 200)
    "lui\t$5,(-2146828288 >> 16) & 0xFFFF": ("lui\t$5,0x100A", 1),
    "lui\t$3,(-2146828288 >> 16) & 0xFFFF": ("lui\t$3,0x100A", 1),
}
for old, (new, want) in sites.items():
    got = sum(1 for line in text.splitlines() if line.split("#")[0].rstrip() == old)
    if got != want:
        sys.exit("unexpected count %d (want %d) of literal site %r" % (got, want, old))
    text = "\n".join(new if line.split("#")[0].rstrip() == old else line for line in text.splitlines()) + "\n"
open(path, "w").write(text)
EOF
mipsel-linux-gnu-as -o "$OUT/original.o" "$OUT/original.mas.s"
# No KSEG0 upper half may survive, and all seven rebased ones must be present.
left=$(mipsel-linux-gnu-objdump -d "$OUT/original.o" | grep -c 'lui.*0x800a' || true)
moved=$(mipsel-linux-gnu-objdump -d "$OUT/original.o" | grep -c 'lui.*0x100a' || true)
[ "$left" = 0 ] && [ "$moved" = 7 ] || { echo "outpost-message5: literal rebase check failed ($left left, $moved moved)" >&2; exit 2; }

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
    -o "$OUT/outpost_message5" "$OUT/crt0.o" "$OUT/harness.o" "$OUT/original.o" "$OUT/ram.o"
"$QEMU" "$OUT/outpost_message5" < "$DOTNET/tests/golden/outpost-message5.vectors"
printf 'outpost-message5: shared vectors PASS (unchanged C, qemu-mipsel)\n'
