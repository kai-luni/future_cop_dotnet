#!/bin/bash
# Regenerate or verify tests/golden/slots-bar.vectors against the ORIGINAL C status bar routines.
#
#   slots_test.sh            compare the vectors against a fresh run of the retail routines
#   slots_test.sh --emit     print the observed output (this is what regenerates the body)
#
# The C sources stay in the decomp repo and are only READ: 8006C080.c, 8006BF44.c and 800491F8.c
# are copied into a temporary directory with their __asm__("...") register/address attributes
# stripped and (u32) pointer casts widened to (unsigned long) so they run on 64-bit hosts, then
# compiled against the recording harness h.c. Nothing is written into either repository; every
# artefact lands in $OUT.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
DECOMP="${DECOMP:-$HOME/git/future-cop-lapd-ps1}"
DOTNET="${DOTNET_REPO:-$(cd "$HERE/../../.." && pwd)}"
OUT="${OUT:-$(mktemp -d "${TMPDIR:-/tmp}/slots-harness.XXXXXX")}"
mkdir -p "$OUT"
VECTORS="${SLOTS_VECTORS:-$DOTNET/tests/golden/slots-bar.vectors}"
STATES="${SLOTS_STATES:-$HERE/states.txt}"
CC="${CC:-gcc}"

GAME="$DECOMP/src/game"
for src in 8006C080.c 8006BF44.c 800491F8.c; do
    [ -f "$GAME/$src" ] || { echo "slots: missing $GAME/$src" >&2; exit 2; }
done
[ -f "$VECTORS" ] || { echo "slots: missing vectors $VECTORS" >&2; exit 2; }
[ -f "$STATES" ] || { echo "slots: missing states $STATES" >&2; exit 2; }

# Translate the retail inline-assembly annotations and 32-bit pointer casts into plain host C:
#   register s32 flag __asm__("$16");        ->  register s32 flag;
#   __asm__ volatile("" : "+r"(a), ...);     ->  /* register hint, dropped */;
#   __asm__("" : "=r"(a0) : "0"(a0));        ->  /* register hint, dropped */;
#   (u32)base / (u32)arr / (u32)c / (u32)d->fieldC1
#                                            ->  (unsigned long) / (unsigned), see below
# The original uses __asm__ with AND without the volatile keyword, and with an input-only,
# an in/out and no constraint list at all; all of them are register-allocation hints that carry no
# computation, so all of them are dropped. Anything else that uses __asm__ is a hard error, so a
# changed retail source fails loudly instead of compiling a silently different routine.
python3 - "$GAME" "$OUT" <<'PY'
import re, sys, pathlib

game, out = pathlib.Path(sys.argv[1]), pathlib.Path(sys.argv[2])

# An empty asm body with any constraint list: no volatile, "+r", "=r" with "0", or nothing.
ASM_EMPTY = re.compile(r'__asm__\s*(?:volatile\s*)?\(\s*""\s*(?::[^;]*?)?\)\s*;')
ASM_DECL = re.compile(r'\s*__asm__\s*\(\s*"[^"]*"\s*\)')

def translate(name, text):
    lines = []
    for raw in text.splitlines():
        line = ASM_DECL.sub('', raw)
        if '__asm__' in line:
            line = ASM_EMPTY.sub('/* hint */;', line)
        if '__asm__' in line:
            raise SystemExit(
                f"slots: {name}: inline asm this harness does not know, refusing to guess: {raw.strip()!r}")
        # The retail targets MIPS32, where a pointer fits in u32. On a 64-bit host the (u32) casts
        # would truncate the pointer, so widen exactly those casts and nothing else.
        line = line.replace('(u32)d->fieldC1', '(unsigned)d->fieldC1')
        line = line.replace('(u32)c', '(unsigned)c')
        line = line.replace('(u32)base', '(unsigned long)base')
        line = line.replace('(u32)arr', '(unsigned long)arr')
        if '(u32)' in line:
            raise SystemExit(
                f"slots: {name}: u32 cast this harness does not know: {raw.strip()!r}")
        lines.append(line)
    return "\n".join(lines) + "\n"

for name in ("8006C080.c", "8006BF44.c", "800491F8.c"):
    (out / name).write_text(translate(name, (game / name).read_text()))
PY

# The retail sources include "game/types.h"; the harness ships the same typedefs under game/.
mkdir -p "$OUT/game"
cp "$HERE/types.h" "$OUT/game/types.h"

"$CC" -std=gnu99 -w -O0 -I"$HERE" -I"$OUT" \
    "$HERE/h.c" "$OUT/8006C080.c" "$OUT/8006BF44.c" "$OUT/800491F8.c" \
    -o "$OUT/slots_gen"
echo "slots: built $OUT/slots_gen"

"$OUT/slots_gen" < "$STATES" > "$OUT/observed.txt"
echo "slots: wrote $OUT/observed.txt"

if [ "${1:-}" = "--emit" ] || [ "${1:-}" = "-e" ]; then
    cat "$OUT/observed.txt"
    exit 0
fi

# Compare only from the first CASE line onward: the vector file carries a provenance header, the
# harness output does not.
python3 - "$VECTORS" "$OUT/observed.txt" <<'PY'
import sys, pathlib

def bodies(path, skip_header):
    lines, started = [], False
    for raw in pathlib.Path(path).read_text().splitlines():
        if raw.startswith("CASE "):
            started = True
        elif not started and skip_header:
            continue
        if started and raw.strip():
            lines.append(raw.rstrip())
    return lines

want = bodies(sys.argv[1], True)
got = bodies(sys.argv[2], False)
if want == got:
    cases = sum(1 for l in want if l.startswith("CASE "))
    print(f"slots: all {cases} vectors PASS")
    sys.exit(0)

count = sum(1 for l in want if l.startswith("CASE "))
print(f"slots: MISMATCH over {count} vectors", file=sys.stderr)
case_line = "?"
for i, (w, g) in enumerate(zip(want, got)):
    if w.startswith("CASE "):
        case_line = w
    if w != g:
        print(f"  {case_line} line {i}: expected {w!r}\n" + " " * 18 + f"got      {g!r}", file=sys.stderr)
        break
if len(want) != len(got):
    print(f"  line count differs: vectors {len(want)}, observed {len(got)}", file=sys.stderr)
sys.exit(1)
PY
