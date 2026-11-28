#!/bin/bash
# Run unchanged, retail-identical PS1 unit-mover code under qemu-mipsel:
#   Type-9 flyer:   80032180 (gInitHoverEnemy), 800324E0 (gUpdateHoverEnemy), 800320E4
#   Type-5/28 rail: 80050DAC, 80050E0C, 8004F330, 8004F764, 8004FFD0, 8005027C, 80050D14, 8003802C
# Each unit is compiled through tests/host/mech/build_units.py and verified byte-identical
# to SLUS_007.39 before linking.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
DOTNET="$(cd "$HERE/../../.." && pwd)"
DECOMP="${DECOMP:-$HOME/git/future-cop-lapd-ps1}"
SLUS="${SLUS:-$DECOMP/baseroms/us/SLUS_007.39}"
T="$DECOMP/.toolchain"
QEMU="${QEMU:-$HOME/.local/share/qemu-user/extracted/usr/bin/qemu-mipsel}"
OUT="$(mktemp -d "${TMPDIR:-/tmp}/unit-movers.XXXXXXXX")"
trap 'rm -rf "$OUT"' EXIT
export PATH="$T/mipsel-binutils/root/usr/bin:$PATH"
export LD_LIBRARY_PATH="$T/mipsel-binutils/root/usr/lib/x86_64-linux-gnu${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
MASPSX=("$T"/maspsx-*/maspsx.py)

UNITS=(
    80032180 800324E0 800320E4
    80050DAC 80050E0C 8004F330 8004F764 8004FFD0 8005027C 80050D14 8003802C
    8001A750 8001A644 80019F54 8001AB78 8001A6F8 8001ACA4
    80019310 800193F8 8001A074 8001A100 8001A180
    80019828 80018818 800187C8 8001846C 80095C30
    80019668 80019720 800197A8
)

python3 -I "$HERE/../mech/build_units.py" "$DECOMP" "$OUT/units" "${UNITS[@]}" > "$OUT/units.log" 2>"$OUT/units.err" \
    || { cat "$OUT/units.err" >&2; exit 2; }
# 8004F330.c emits its local jump table as label_table.N in .rdata and references jtbl_800119A8.
# Bind jtbl_800119A8 to that local table so indirect branches land inside our linked .text.
python3 -c '
import re, sys
p = sys.argv[1]
txt = open(p).read()
m = re.search(r"^(label_table\.\d+):", txt, re.M)
assert m, "label_table not found in 8004F330.proc.s"
txt += "\n.globl jtbl_800119A8\njtbl_800119A8 = " + m.group(1) + "\n"
open(p, "w").write(txt)
' "$OUT/units/8004F330.proc.s"

OBJS=()
for u in "${UNITS[@]}"; do
    # shellcheck disable=SC2046
    mipsel-linux-gnu-as $(cat "$OUT/units/$u.asflags") -o "$OUT/$u.o" "$OUT/units/$u.proc.s"
    OBJS+=("$OUT/$u.o")
done

"$T/gcc-2.7.2-cdk/cpp" -lang-c -I "$DECOMP/include" "$HERE/harness.c" "$OUT/harness.i"
"$T/gcc-2.7.2-cdk/cc1" -quiet -msoft-float -mgas -G0 -fgnu-linker -O1 "$OUT/harness.i" -o "$OUT/harness.s"
python3 "${MASPSX[0]}" -G0 "$OUT/harness.s" | python3 "$DECOMP/tests/host/fix_align.py" > "$OUT/harness.mas.s"
mipsel-linux-gnu-as -EL -march=r3000 -mabi=32 -G0 -o "$OUT/harness.o" "$OUT/harness.mas.s"

mipsel-linux-gnu-as -o "$OUT/crt0.o" "$DECOMP/tests/host/crt0.s" 2>/dev/null
printf '.section .retailram,"aw",@nobits\n.globl D_RAM\nD_RAM:\n.space 0xA0000\n' > "$OUT/ram.s"
printf '.section .scratch,"aw",@nobits\n.globl SCRATCH\nSCRATCH:\n.space 0x400\n' > "$OUT/scratch.s"
mipsel-linux-gnu-as -o "$OUT/ram.o" "$OUT/ram.s"
mipsel-linux-gnu-as -o "$OUT/scratch.o" "$OUT/scratch.s"

DEFS=""
for s in $(mipsel-linux-gnu-nm -u "${OBJS[@]}" "$OUT/harness.o" | awk '{print $2}' \
           | grep -E '^D_8[0-9A-F]{7}$' | sort -u); do
    DEFS="$DEFS --defsym=$s=$(printf '0x%X' $((0x${s#D_} - 0x70000000)))"
done

mipsel-linux-gnu-ld -static -T "$HERE/../mech/mech.ld" $DEFS \
    -o "$OUT/unit_movers" "$OUT/crt0.o" "$OUT/harness.o" "${OBJS[@]}" "$OUT/ram.o" "$OUT/scratch.o"
"$QEMU" "$OUT/unit_movers" "$SLUS" > "$OUT/actual"
test -s "$OUT/actual"
if [ "${1:-}" = print ]; then
    cat "$OUT/actual"
else
    diff -u "${UNIT_MOVER_VECTORS:-$DOTNET/tests/golden/unit-movers.vectors}" "$OUT/actual"
    printf 'unit-movers: golden vectors PASS (%d retail-identical units, qemu-mipsel)\n' "$(grep -c 'retail-identical' "$OUT/units.log")"
fi
