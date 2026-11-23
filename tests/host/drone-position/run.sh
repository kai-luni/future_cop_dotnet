#!/bin/bash
# Run unchanged PS1 C with gcc 2.7.2 and qemu-mipsel against shared synthetic vectors.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
DOTNET="$(cd "$HERE/../../.." && pwd)"
DECOMP="${DECOMP:-$HOME/git/future-cop-lapd-ps1}"
T="$DECOMP/.toolchain"
QEMU="${QEMU:-$HOME/.local/share/qemu-user/extracted/usr/bin/qemu-mipsel}"
OUT="$(mktemp -d "${TMPDIR:?Set TMPDIR to a scratch directory}/drone-position.XXXXXXXX")"
trap 'rm -rf "$OUT"' EXIT
export PATH="$T/mipsel-binutils/root/usr/bin:$PATH"
export LD_LIBRARY_PATH="$T/mipsel-binutils/root/usr/lib/x86_64-linux-gnu${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
GCC="$T/gcc-2.7.2-cdk/gcc"
MASPSX=("$T"/maspsx-*/maspsx.py)
compile() {
    "$GCC" -B"$T/gcc-2.7.2-cdk/" -I"$DECOMP/include" -O1 -G0 -S "$1" -o "$OUT/tmp.s"
    python3 "${MASPSX[0]}" "$OUT/tmp.s" | python3 "$DECOMP/tests/host/fix_align.py" > "$OUT/tmp.mas.s"
    mipsel-linux-gnu-as -o "$2" "$OUT/tmp.mas.s"
}
compile "$DECOMP/src/game/800330E8.c" "$OUT/original.o"
compile "$HERE/harness.c" "$OUT/harness.o"
mipsel-linux-gnu-as -o "$OUT/crt0.o" "$DECOMP/tests/host/crt0.s"
mipsel-linux-gnu-ld -static -e __start -o "$OUT/drone" "$OUT/crt0.o" "$OUT/harness.o" "$OUT/original.o"
"$QEMU" "$OUT/drone" < "$DOTNET/tests/golden/drone-position.vectors"
printf 'drone-position: 4 shared vectors PASS (unchanged C, qemu-mipsel)\n'
