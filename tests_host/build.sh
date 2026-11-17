#!/bin/bash
# Build the freestanding MIPS harness against the ORIGINAL decomp sources and
# run it under qemu-mipsel-static with the shared golden vectors.
# Env: FCOP_DECOMP (default ~/git/future-cop-lapd-ps1), QEMU (default user-local extract).
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
DECOMP="${FCOP_DECOMP:-$HOME/git/future-cop-lapd-ps1}"
T="$DECOMP/.toolchain"
GCC="$T/gcc-2.7.2-cdk/gcc"
AS="$T/mipsel-binutils/root/usr/bin/mipsel-linux-gnu-as"
LD="$T/mipsel-binutils/root/usr/bin/mipsel-linux-gnu-ld"
MASPSX="$(echo "$T"/maspsx-*/maspsx.py)"
QEMU="${QEMU:-$HOME/.local/share/qemu-user/extracted/usr/bin/qemu-mipsel}"
OUT="$HERE/build"
mkdir -p "$OUT"
export PATH="$T/mipsel-binutils/root/usr/bin:$PATH"
export LD_LIBRARY_PATH="$T/mipsel-binutils/root/usr/lib/x86_64-linux-gnu${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"

compile() { # compile <src.c> <obj> [extra flags...]
    local src="$1" obj="$2"; shift 2
    "$GCC" -B"$T/gcc-2.7.2-cdk/" -I"$DECOMP/include" -O1 -G0 -S "$src" -o "$OUT/tmp.s" "$@"
    python3 "$MASPSX" "$OUT/tmp.s" | python3 "$HERE/fix_align.py" > "$OUT/tmp.mas.s"
    "$AS" -o "$obj" "$OUT/tmp.mas.s"
}

for f in 80012E1C 80012E48 8001306C 800131A0 80063C5C; do
    compile "$DECOMP/src/game/$f.c" "$OUT/$f.o"
done
compile "$HERE/stubs.c" "$OUT/stubs.o"
compile "$HERE/harness.c" "$OUT/harness.o"
"$AS" -o "$OUT/crt0.o" "$HERE/crt0.s"
"$LD" -static -e __start -o "$OUT/harness" \
    "$OUT/crt0.o" "$OUT/harness.o" "$OUT/stubs.o" \
    "$OUT/80012E1C.o" "$OUT/80012E48.o" "$OUT/8001306C.o" "$OUT/800131A0.o" "$OUT/80063C5C.o"
echo "built $OUT/harness"
"$QEMU" "$OUT/harness" "$HERE/../tests/golden/3dtl.vectors"