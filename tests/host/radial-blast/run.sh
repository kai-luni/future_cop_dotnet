#!/bin/bash
# Run unchanged PS1 C func_800410BC (+ func_800413EC, func_80041428 and func_8001A750) with
# gcc 2.7.2 and qemu-mipsel against shared golden vectors.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
DOTNET="$(cd "$HERE/../../.." && pwd)"
DECOMP="${DECOMP:-$HOME/git/future-cop-lapd-ps1}"
T="$DECOMP/.toolchain"
QEMU="${QEMU:-$HOME/.local/share/qemu-user/extracted/usr/bin/qemu-mipsel}"
VECTORS="$DOTNET/tests/golden/radial-blast.vectors"
EXPECTED="$(grep -vc '^#' "$VECTORS")"
OUT="$(mktemp -d "${TMPDIR:-/tmp}/radial-blast.XXXXXXXX")"
trap 'rm -rf "$OUT"' EXIT
export PATH="$T/mipsel-binutils/root/usr/bin:$PATH"
export LD_LIBRARY_PATH="$T/mipsel-binutils/root/usr/lib/x86_64-linux-gnu${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
GCC="$T/gcc-2.7.2-cdk/gcc"
MASPSX=("$T"/maspsx-*/maspsx.py)
compile() {
    "$GCC" -B"$T/gcc-2.7.2-cdk/" -I"$DECOMP/include" -O2 -msoft-float -G0 ${EXTRA_CFLAGS:-} -S "$1" -o "$OUT/tmp.s"
    python3 "${MASPSX[0]}" "$OUT/tmp.s" | python3 "$DECOMP/tests/host/fix_align.py" > "$OUT/tmp.mas.s"
    mipsel-linux-gnu-as -o "$2" "$OUT/tmp.mas.s"
}
compile "$DECOMP/src/game/gameplay-ai/gApplyRadialContactDamage.c" "$OUT/radial.o"
compile "$DECOMP/src/game/gameplay-ai/gApplyBandedRadialDamage.c" "$OUT/banded.o"
compile "$DECOMP/src/game/gameplay-ai/gApplyConeContactDamage.c" "$OUT/cone.o"
compile "$DECOMP/src/game/utilities/gApproximateDistanceXzHalfMin.c" "$OUT/distance.o"
EXTRA_CFLAGS="-DEXPECTED=$EXPECTED" compile "$HERE/harness.c" "$OUT/harness.o"
mipsel-linux-gnu-as -o "$OUT/crt0.o" "$DECOMP/tests/host/crt0.s"
mipsel-linux-gnu-ld -static -e __start -o "$OUT/radial_blast" \
    "$OUT/crt0.o" "$OUT/harness.o" "$OUT/radial.o" "$OUT/banded.o" "$OUT/cone.o" "$OUT/distance.o"
"$QEMU" "$OUT/radial_blast" < "$VECTORS"
printf 'radial-blast: %s shared vectors PASS (unchanged C, qemu-mipsel)\n' "$EXPECTED"
