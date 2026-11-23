#!/bin/bash
# Compile unchanged decomp units in the isolated C checkout. For regeneration:
# DECOMP=/path/to/isolated/copy VECTORS=/path/to/input-only.rows ./lead_direction_test.sh --emit
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
if (( $# > 1 )) || [[ $# == 1 && $1 != --emit ]]; then
    echo "Usage: $0 [--emit]" >&2
    exit 2
fi
DECOMP="${DECOMP:-$(cd "$HERE/../../.." && pwd)}"
HOST="$DECOMP/tests/host"
if [[ ! -f "$DECOMP/src/game/8005E2A0.c" ]]; then
    echo "Set DECOMP to an isolated copy of the C reference." >&2
    exit 2
fi
T="$DECOMP/.toolchain"
GCC="$T/gcc-2.7.2-cdk/gcc"
MASPSX=("$T"/maspsx-*/maspsx.py)
QEMU="${QEMU:-$HOME/.local/share/qemu-user/extracted/usr/bin/qemu-mipsel}"
DEFAULT_VECTORS="$HERE/../../golden/lead-direction.vectors"
if [[ ! -f "$DEFAULT_VECTORS" ]]; then
    DEFAULT_VECTORS="$DECOMP/../net/tests/golden/lead-direction.vectors"
fi
VECTORS="${VECTORS:-$DEFAULT_VECTORS}"
if [[ ! -r "$VECTORS" ]]; then
    echo "Cannot read vectors: $VECTORS" >&2
    exit 2
fi
OUT="${OUT:-$HOST/build-lead-direction}"
mkdir -p "$OUT"
export PATH="$T/mipsel-binutils/root/usr/bin:$PATH"
export LD_LIBRARY_PATH="$T/mipsel-binutils/root/usr/lib/x86_64-linux-gnu${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"

compile() {
    local src="$1" obj="$2"
    if [[ -n "${QEMU_I386:-}" ]]; then
        # Run the same compiler's stages in user-mode emulation on hosts that cannot run i386 ELF.
        "$QEMU_I386" "$T/gcc-2.7.2-cdk/cpp" -lang-c -I"$HERE" -I"$DECOMP/include" "$src" "$obj.i"
        "$QEMU_I386" "$T/gcc-2.7.2-cdk/cc1" -quiet -O1 -G0 "$obj.i" -o "$obj.s"
    else
        "$GCC" -B"$T/gcc-2.7.2-cdk/" -I"$HERE" -I"$DECOMP/include" -O1 -G0 -S "$src" -o "$obj.s"
    fi
    python3 "${MASPSX[0]}" "$obj.s" | python3 "$HOST/fix_align.py" > "$obj.mas.s"
    mipsel-linux-gnu-as -o "$obj" "$obj.mas.s"
}

objects=()
for unit in 8005E2A0 8001A4E8 8001A180 80019828 800187C8 80018818; do
    compile "$DECOMP/src/game/$unit.c" "$OUT/$unit.o"
    objects+=("$OUT/$unit.o")
done
compile "$HERE/lead_direction_harness.c" "$OUT/main.o"
mipsel-linux-gnu-as -o "$OUT/crt0.o" "$HOST/crt0.s"
mipsel-linux-gnu-ld -static -e __start -o "$OUT/lead_direction" "$OUT/crt0.o" "$OUT/main.o" "${objects[@]}"
"$QEMU" "$OUT/lead_direction" "$VECTORS" "$@"
