#!/bin/bash
# Run unchanged PS1 elevator init, step, and command queue code under qemu-mipsel.
# Run with "print" to capture the trace; the default invocation diffs it against the golden trace.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
DOTNET="$(cd "$HERE/../../.." && pwd)"
DECOMP="${DECOMP:-$HOME/git/future-cop-lapd-ps1}"
T="$DECOMP/.toolchain"
QEMU="${QEMU:-$HOME/.local/share/qemu-user/extracted/usr/bin/qemu-mipsel}"
OUT="$(mktemp -d "${TMPDIR:-/tmp}/elevator.XXXXXXXX")"
trap 'rm -rf "$OUT"' EXIT
export PATH="$T/mipsel-binutils/root/usr/bin:$PATH"
export LD_LIBRARY_PATH="$T/mipsel-binutils/root/usr/lib/x86_64-linux-gnu${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
CDK="$T/gcc-2.7.2-cdk"
GCC=("$CDK/gcc")
GCC_PREFIX="$CDK/"
# The pinned compiler is i386. Use companion user-mode QEMU when present so an
# x86-64 host without native IA32 support can run the same driver and compiler.
QEMU_I386="${QEMU_I386:-$(dirname "$QEMU")/qemu-i386}"
if [ -x "$QEMU_I386" ]; then
    for stage in cpp cc1; do
        printf '#!/bin/bash\nexec %q %q "$@"\n' "$QEMU_I386" "$CDK/$stage" > "$OUT/$stage"
        chmod +x "$OUT/$stage"
    done
    GCC=("$QEMU_I386" "$CDK/gcc")
    GCC_PREFIX="$OUT/"
fi
MASPSX=("$T"/maspsx-*/maspsx.py)
to_o() {
    "${GCC[@]}" -B"$GCC_PREFIX" -I"$DECOMP/include" -I"$DECOMP/tests/host" \
        -O2 -msoft-float -G0 -S "$1" -o "$OUT/tmp.s"
    python3 "${MASPSX[0]}" "$OUT/tmp.s" | python3 "$DECOMP/tests/host/fix_align.py" > "$OUT/tmp.mas.s"
    mipsel-linux-gnu-as -o "$2" "$OUT/tmp.mas.s"
}

OBJS=()
for unit in 80041488 80041728 80041880 8002A5B4; do
    to_o "$DECOMP/src/game/$unit.c" "$OUT/$unit.o"
    OBJS+=("$OUT/$unit.o")
done
to_o "$HERE/harness.c" "$OUT/harness.o"
mipsel-linux-gnu-as -o "$OUT/crt0.o" "$DECOMP/tests/host/crt0.s"
# Both C and the fixture use the D_8009FA58 symbol, at retail address - 0x70000000.
printf '.section .players,"aw",@nobits\n.globl D_8009FA58\nD_8009FA58:\n.space 8\n' > "$OUT/players.s"
mipsel-linux-gnu-as -o "$OUT/players.o" "$OUT/players.s"
mipsel-linux-gnu-ld -static -e __start --section-start=.players=0x1009FA58 \
    -o "$OUT/elevator" "$OUT/crt0.o" "$OUT/harness.o" "${OBJS[@]}" "$OUT/players.o"
"$QEMU" "$OUT/elevator" > "$OUT/actual"
test -s "$OUT/actual"
if [ "${1:-}" = print ]; then
    cat "$OUT/actual"
else
    diff -u "${ELEVATOR_VECTORS:-$DOTNET/tests/golden/elevator.vectors}" "$OUT/actual"
    printf 'elevator: golden vectors PASS (unchanged C, qemu-mipsel)\n'
fi
