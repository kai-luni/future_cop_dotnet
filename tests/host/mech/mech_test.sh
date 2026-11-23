#!/bin/bash
# Run the controller slice by default; set MECH_VECTORS=tests/golden/mech-walk.vectors for
# the bounded one-player static-terrain walker and collision parity suite.
#   mech_test.sh            compare
#   mech_test.sh --emit     print every vector line with the observed outputs
# Each unit is built by the decomp's matching gate (tools/verify_matching_units.py compile_unit,
# imported read-only by build_units.py) and must equal the retail bytes; $gp is the retail value
# mapped by -0x70000000 (_gp = 0x1009F214), so gp-relative small-data accesses resolve as on PS1.
# Nothing is written into either repository: OUT defaults to a temporary directory.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
DECOMP="${DECOMP:-$HOME/git/future-cop-lapd-ps1}"
DOTNET="${DOTNET_REPO:-$(cd "$HERE/../../.." && pwd)}"
T="$DECOMP/.toolchain"
OUT="${OUT:-$(mktemp -d "${TMPDIR:-/tmp}/mech-harness.XXXXXX")}"
QEMU="${QEMU:-$HOME/.local/share/qemu-user/extracted/usr/bin/qemu-mipsel}"
# Default remains the separately verified controller slice. Pass MECH_VECTORS explicitly for
# locomotion; those vectors use a fixed host entity and a PS1 $s1 mapping at the collision entry.
VECTORS="${MECH_VECTORS:-$DOTNET/tests/golden/mech-input.vectors}"
DISC="${DISC:-$DOTNET/build/disc/files}"
SLUS="${SLUS:-$DECOMP/baseroms/us/SLUS_007.39}"
MASPSX="$(echo "$T"/maspsx-*/maspsx.py)"
mkdir -p "$OUT"
export PATH="$T/mipsel-binutils/root/usr/bin:$PATH"
export LD_LIBRARY_PATH="$T/mipsel-binutils/root/usr/lib/x86_64-linux-gnu${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"

MIS_REL="$(awk '$1 == "MIS" { print $2; exit }' "$VECTORS")"
[ -n "$MIS_REL" ] || { echo "mech: vectors name no MIS" >&2; exit 2; }
[ -f "$DISC/$MIS_REL" ] || { echo "mech: retail mission $DISC/$MIS_REL missing" >&2; exit 2; }
[ -f "$SLUS" ] || { echo "mech: retail executable $SLUS missing" >&2; exit 2; }
python3 -I "$HERE/../level/extract_level.py" "$DISC/$MIS_REL" "$OUT/level.blob" >&2
rm -f "$OUT"/*.o

# The harness itself (test code, not retail) is plain C.
compile_harness() {
    "$T/gcc-2.7.2-cdk/cpp" -lang-c -I "$DECOMP/include" "$1" "$OUT/tmp.i"
    "$T/gcc-2.7.2-cdk/cc1" -quiet -msoft-float -mgas -G0 -fgnu-linker -O1 "$OUT/tmp.i" -o "$OUT/tmp.s"
    python3 "$MASPSX" -G0 "$OUT/tmp.s" 2>>"$OUT/maspsx.log" | python3 "$DECOMP/tests/host/fix_align.py" > "$2"
}

# walker + callees (movement, ground, collision, animation, input) + level loader
UNITS="800549F0 800538C8 8001E7B8 8001846C 80054590 80053730 8001A7AC 8001A030 8001ABE0 80095900
80052550 8001A180 80095C14 80019904 8001AB00 8001AC28 80019828 80018818 800187C8 800197A8 80019720
80019668 8006943C 800684AC 80067914 80066F78 80067480 8006320C 80053514 80053188 8001A6F8 80019FCC
8001A314 8005220C 80052708 80052410 80051C50 80010974 800188D0 80010750 80010234 80052F54 80052E28
80051AC8 800448D8 80044B34 800449F8 8002A5B4 80044AE8
8001E588 8001E620 8001EF8C 800576A8 80057864
80063DC8 800639D4"
# Every unit is built by the decomp's own matching gate (build_units.py) and must reproduce the
# retail bytes before its gate assembly is linked here; the harness runs that exact code.
# shellcheck disable=SC2086
python3 -I "$HERE/build_units.py" "$DECOMP" "$OUT/units" $UNITS > "$OUT/units.log" 2>"$OUT/units.err" \
    || { cat "$OUT/units.err" >&2; exit 2; }
echo "mech: $(grep -c 'retail-identical' "$OUT/units.log") units built retail-identical by the matching gate" >&2
OBJS=""
for u in $UNITS; do
    # shellcheck disable=SC2046
    mipsel-linux-gnu-as $(cat "$OUT/units/$u.asflags") -o "$OUT/$u.o" "$OUT/units/$u.proc.s" 2>"$OUT/$u.as.log"
    OBJS="$OBJS $OUT/$u.o"
done
compile_harness "$HERE/mech_harness.c" "$OUT/harness.mas.s"
mipsel-linux-gnu-as -EL -march=r3000 -mabi=32 -G0 -o "$OUT/harness.o" "$OUT/harness.mas.s" 2>"$OUT/harness.as.log"
mipsel-linux-gnu-as -o "$OUT/crt0.o" "$DECOMP/tests/host/crt0.s" 2>/dev/null
printf '.section .retailram,"aw",@nobits\n.globl D_RAM\nD_RAM:\n.space 0xA0000\n' > "$OUT/ram.s"
printf '.section .scratch,"aw",@nobits\n.globl SCRATCH\nSCRATCH:\n.space 0x400\n' > "$OUT/scratch.s"
mipsel-linux-gnu-as -o "$OUT/ram.o" "$OUT/ram.s"
mipsel-linux-gnu-as -o "$OUT/scratch.o" "$OUT/scratch.s"

# Every retail data symbol lands at (retail address - 0x70000000) inside .retailram; the harness
# copies the whole retail executable image there, so ROM tables sit at their mapped addresses.
DEFS=""
for s in $(mipsel-linux-gnu-nm -u $OBJS "$OUT/harness.o" | awk '{print $2}' | grep -E '^D_8[0-9A-F]{7}$' | sort -u); do
    DEFS="$DEFS --defsym=$s=$(printf '0x%X' $((0x${s#D_} - 0x70000000)))"
done
mipsel-linux-gnu-ld -static -T "$HERE/mech.ld" $DEFS \
    -o "$OUT/mech_harness" "$OUT/crt0.o" "$OUT/harness.o" $OBJS "$OUT/ram.o" "$OUT/scratch.o"
"$QEMU" "$OUT/mech_harness" "$VECTORS" "$OUT/level.blob" "$SLUS" "$@"
