#!/bin/bash
# Compile unchanged decomp units in the isolated C checkout. For regeneration:
# DECOMP=/path/to/isolated/copy VECTORS=/path/to/input-only.rows ./drone_strategy_test.sh --emit
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
if (( $# > 1 )) || [[ $# == 1 && $1 != --emit ]]; then
    echo "Usage: $0 [--emit]" >&2
    exit 2
fi
DECOMP="${DECOMP:-$(cd "$HERE/../../.." && pwd)}"
HOST="$DECOMP/tests/host"
if [[ ! -f "$DECOMP/src/game/800336DC.c" ]]; then
    echo "Set DECOMP to an isolated copy of the C reference." >&2
    exit 2
fi
T="$DECOMP/.toolchain"
GCC="$T/gcc-2.7.2-cdk/gcc"
MASPSX=("$T"/maspsx-*/maspsx.py)
QEMU="${QEMU:-$HOME/.local/share/qemu-user/extracted/usr/bin/qemu-mipsel}"
VECTORS="${VECTORS:-$HERE/../../golden/drone-strategy.vectors}"
if [[ ! -r "$VECTORS" ]]; then
    echo "Cannot read vectors: $VECTORS" >&2
    exit 2
fi
OUT="${OUT:-$HOST/build-drone-strategy}"
mkdir -p "$OUT"
export PATH="$T/mipsel-binutils/root/usr/bin:$PATH"
export LD_LIBRARY_PATH="$T/mipsel-binutils/root/usr/lib/x86_64-linux-gnu${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"

compile() {
    local src="$1" obj="$2"
    if [[ -n "${QEMU_I386:-}" ]]; then
        # Run the same compiler's stages in user-mode emulation on hosts that cannot run i386 ELF.
        "$QEMU_I386" "$T/gcc-2.7.2-cdk/cpp" -lang-c -I"$HERE" -I"$DECOMP/include" "$src" "$obj.i"
        "$QEMU_I386" "$T/gcc-2.7.2-cdk/cc1" -quiet -O2 -msoft-float -G0 "$obj.i" -o "$obj.s"
    else
        "$GCC" -B"$T/gcc-2.7.2-cdk/" -I"$HERE" -I"$DECOMP/include" -O2 -msoft-float -G0 -S "$src" -o "$obj.s"
    fi
    python3 "${MASPSX[0]}" "$obj.s" | python3 "$HOST/fix_align.py" > "$obj.mas.s"
    mipsel-linux-gnu-as -o "$obj" "$obj.mas.s"
}

objects=()
for unit in 800336DC 80033608 800336A8 800330E8; do
    compile "$DECOMP/src/game/$unit.c" "$OUT/$unit.o"
    objects+=("$OUT/$unit.o")
done
compile "$HERE/drone_strategy_harness.c" "$OUT/main.o"
mipsel-linux-gnu-as -o "$OUT/crt0.o" "$HOST/crt0.s"
mipsel-linux-gnu-ld -static -e __start -o "$OUT/drone_strategy" "$OUT/crt0.o" "$OUT/main.o" "${objects[@]}"
"$QEMU" "$OUT/drone_strategy" "$VECTORS" "$@"
