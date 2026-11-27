#!/bin/bash
# Run unchanged PS1 C radar marker units (8004CAE4, 8004CC68, 8005A740, 8005A828, 8005A934, 8005A9C4,
# 8001846C, 80095C30) with gcc 2.7.2 and qemu-mipsel against tests/golden/radar-markers.vectors.
#
# qemu-mipsel cannot map guest addresses >= 0x80000000. All eight units reach PS1 ROM tables
# (D_8009A234, D_8009AFB4, D_8009E0A0) and RAM/BSS globals (D_8009FA90, D_8009F9C0, D_8009FA0C,
# D_8009FA94, D_8009FA58, D_8009FAA4, D_800A6F60, ...) through D_800xxxxx linker symbols, so
# --defsym relocates the entire PS1 RAM window by -0x70000000 into .retailram at 0x10010000,
# seeded directly with the retail SLUS_007.39 executable payload (bytes 0x800..0x90000) followed
# by zeroed BSS. `run.sh print` prints the C result rows for the vector inputs.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
DOTNET="$(cd "$HERE/../../.." && pwd)"
DECOMP="${DECOMP:-$HOME/git/future-cop-lapd-ps1}"
SLUS="${SLUS:-$DECOMP/baseroms/us/SLUS_007.39}"
T="$DECOMP/.toolchain"
QEMU="${QEMU:-$HOME/.local/share/qemu-user/extracted/usr/bin/qemu-mipsel}"
OUT="$(mktemp -d "${TMPDIR:-/tmp}/radar-markers.XXXXXXXX")"
trap 'rm -rf "$OUT"' EXIT

if [ ! -f "$SLUS" ]; then
    echo "radar-markers: missing retail executable: $SLUS" >&2
    exit 2
fi

export PATH="$T/mipsel-binutils/root/usr/bin:$PATH"
export LD_LIBRARY_PATH="$T/mipsel-binutils/root/usr/lib/x86_64-linux-gnu${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
GCC="$T/gcc-2.7.2-cdk/gcc"
MASPSX=("$T"/maspsx-*/maspsx.py)
compile() {
    "$GCC" -B"$T/gcc-2.7.2-cdk/" -I"$DECOMP/include" -O2 -msoft-float -G0 -S "$1" -o "$OUT/tmp.s" 2>/dev/null
    python3 "${MASPSX[0]}" "$OUT/tmp.s" | python3 "$DECOMP/tests/host/fix_align.py" > "$OUT/tmp.mas.s"
    mipsel-linux-gnu-as -o "$2" "$OUT/tmp.mas.s"
}

UNITS=(8004CAE4 8004CC68 8005A740 8005A828 8005A934 8005A9C4 8001846C 80095C30)
OBJS=()
for u in "${UNITS[@]}"; do
    compile "$DECOMP/src/game/$u.c" "$OUT/$u.o"
    OBJS+=("$OUT/$u.o")
done

# Verify zero KSEG0 0x800x literal upper halves exist in any compiled retail unit.
left=$(mipsel-linux-gnu-objdump -d "${OBJS[@]}" | grep -c 'lui.*0x800' || true)
[ "$left" = 0 ] || { echo "radar-markers: unexpected KSEG0 literal ($left)" >&2; exit 2; }

compile "$HERE/harness.c" "$OUT/harness.o"
mipsel-linux-gnu-as -o "$OUT/crt0.o" "$DECOMP/tests/host/crt0.s"

# Populate .retailram (0x10010000..0x100AFFFF) with the retail SLUS_007.39 image (0x80010000..0x8009F7FF)
# followed by zeroed BSS (0x8009F800..0x800AFFFF).
cat > "$OUT/ram.s" <<EOF
.section .retailram,"aw",@progbits
.globl D_RAM
D_RAM:
.incbin "$SLUS", 0x800, 0x8F800
.space 0x10800
EOF
mipsel-linux-gnu-as -o "$OUT/ram.o" "$OUT/ram.s"

DEFS=""
for s in $(mipsel-linux-gnu-nm -u "${OBJS[@]}" "$OUT/harness.o" | awk '{print $2}' \
           | grep -E '^D_8[0-9A-F]{7}$' | sort -u); do
    DEFS="$DEFS --defsym=$s=$(printf '0x%X' $((0x${s#D_} - 0x70000000)))"
done

mipsel-linux-gnu-ld -static -e __start --section-start=.retailram=0x10010000 $DEFS \
    -o "$OUT/radar_markers" "$OUT/crt0.o" "$OUT/harness.o" "${OBJS[@]}" "$OUT/ram.o"

if [ "${1:-}" = print ]; then
    "$QEMU" "$OUT/radar_markers" print < "$DOTNET/tests/golden/radar-markers.vectors"
    exit 0
fi
"$QEMU" "$OUT/radar_markers" < "$DOTNET/tests/golden/radar-markers.vectors"
printf 'radar-markers: 183 shared vectors PASS (unchanged C, qemu-mipsel)\n'
