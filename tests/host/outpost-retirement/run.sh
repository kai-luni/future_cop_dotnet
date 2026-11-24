#!/bin/bash
# Run the unchanged PS1 C retirement chain of a type-0x24 outpost (8004DF28, 8004CE04, 800623FC,
# 800620D8, 80036624, 8002D664, 8002DEA8, 8002AD58, 8002AA1C, 8002A940, 8003BA50, 8003B65C,
# 8002A810, 8004D664, 8004D958, 8004D3D4, 8004D598, 8004CCDC) with gcc 2.7.2 and qemu-mipsel
# against the shared golden vectors. `run.sh print` prints the C result rows for the vector inputs.
#
# qemu-mipsel cannot map guest addresses >= 0x80000000. Two units reach globals through absolute
# literals instead of symbols: 8002D664.c (D_8009FA0C, D_8009F9EC, D_8009FA94 at :185,187-189,
# 192-193,199-202,208-209; seven sites, the rewrite of tests/host/outpost-message5/run.sh) and
# 8004DF28.c (the 0x800B0000 base of D_800A8F90/D_800A8F94/D_800A8F98 at :110-112,169-171,198-200;
# eight `lui $2,0x800B` sites). Exactly those literals are rebased by -0x70000000 in the compiled
# assembly and every D_ symbol is placed at the same rebased address in a .retailram section. A
# literal address and its rebased form both expand to one lui with the same low halves, so the
# instruction sequence is unchanged.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
DOTNET="$(cd "$HERE/../../.." && pwd)"
DECOMP="${DECOMP:-$HOME/git/future-cop-lapd-ps1}"
T="$DECOMP/.toolchain"
QEMU="${QEMU:-$HOME/.local/share/qemu-user/extracted/usr/bin/qemu-mipsel}"
OUT="$(mktemp -d "${TMPDIR:-/tmp}/outpost-retirement.XXXXXXXX")"
trap 'rm -rf "$OUT"' EXIT
export PATH="$T/mipsel-binutils/root/usr/bin:$PATH"
export LD_LIBRARY_PATH="$T/mipsel-binutils/root/usr/lib/x86_64-linux-gnu${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
GCC="$T/gcc-2.7.2-cdk/gcc"
MASPSX=("$T"/maspsx-*/maspsx.py)
to_s() {
    "$GCC" -B"$T/gcc-2.7.2-cdk/" -I"$DECOMP/include" -O2 -msoft-float -G0 -S "$1" -o "$OUT/tmp.s" 2>/dev/null
    python3 "${MASPSX[0]}" "$OUT/tmp.s" | python3 "$DECOMP/tests/host/fix_align.py" > "$2"
}

UNITS="8004DF28 8004CE04 800623FC 800620D8 80036624 8002D664 8002DEA8 8002AD58 8002AA1C 8002A940 \
8003BA50 8003B65C 8002A810 8004D664 8004D958 8004D3D4 8004D598 8004CCDC"
for u in $UNITS; do to_s "$DECOMP/src/game/$u.c" "$OUT/$u.mas.s"; done

# Rebase the literal addresses; each expected count is the number of sites in the compiled unit.
python3 - "$OUT" <<'PY'
import sys
out = sys.argv[1]
rebase = 0x70000000
units = {
    "8002D664": {
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
    },
    "8004DF28": {
        # base = 0x800B0000 of the active table words at -0x7070/-0x706C/-0x7068 (8004DF28.c:110,169,198)
        "lui\t$2,(-2146762752 >> 16) & 0xFFFF": ("lui\t$2,0x100B", 8),
    },
}
for unit, sites in units.items():
    path = "%s/%s.mas.s" % (out, unit)
    text = open(path).read()
    for old, (new, want) in sites.items():
        got = sum(1 for line in text.splitlines() if line.split("#")[0].rstrip() == old)
        if got != want:
            sys.exit("%s: unexpected count %d (want %d) of literal site %r" % (unit, got, want, old))
        text = "\n".join(new if line.split("#")[0].rstrip() == old else line for line in text.splitlines()) + "\n"
    open(path, "w").write(text)
PY
OBJS=""
for u in $UNITS; do
    mipsel-linux-gnu-as -o "$OUT/$u.o" "$OUT/$u.mas.s"
    OBJS="$OBJS $OUT/$u.o"
done
# No KSEG0 upper half may survive anywhere; 7 + 8 rebased ones must be present.
left=$(mipsel-linux-gnu-objdump -d $OBJS | grep -cE 'lui.*0x80(09|0a|0b)' || true)
moved=$(mipsel-linux-gnu-objdump -d $OBJS | grep -cE 'lui.*0x10(0a|0b)' || true)
[ "$left" = 0 ] && [ "$moved" = 15 ] || { echo "outpost-retirement: literal rebase check failed ($left left, $moved moved)" >&2; exit 2; }

to_s "$HERE/harness.c" "$OUT/harness.mas.s"
mipsel-linux-gnu-as -o "$OUT/harness.o" "$OUT/harness.mas.s"
mipsel-linux-gnu-as -o "$OUT/crt0.o" "$DECOMP/tests/host/crt0.s"
printf '.section .retailram,"aw",@nobits\n.globl D_RAM\nD_RAM:\n.space 0xA0000\n' > "$OUT/ram.s"
mipsel-linux-gnu-as -o "$OUT/ram.o" "$OUT/ram.s"
DEFS=""
for s in $(mipsel-linux-gnu-nm -u $OBJS "$OUT/harness.o" | awk '{print $2}' \
           | grep -E '^D_8[0-9A-F]{7}$' | sort -u); do
    DEFS="$DEFS --defsym=$s=$(printf '0x%X' $((0x${s#D_} - 0x70000000)))"
done
mipsel-linux-gnu-ld -static -e __start --section-start=.retailram=0x10010000 $DEFS \
    -o "$OUT/outpost_retirement" "$OUT/crt0.o" "$OUT/harness.o" $OBJS "$OUT/ram.o"
if [ "${1:-}" = print ]; then
    "$QEMU" "$OUT/outpost_retirement" print < "$DOTNET/tests/golden/outpost-retirement.vectors"
    exit 0
fi
"$QEMU" "$OUT/outpost_retirement" < "$DOTNET/tests/golden/outpost-retirement.vectors"
printf 'outpost-retirement: shared vectors PASS (unchanged C, qemu-mipsel)\n'
