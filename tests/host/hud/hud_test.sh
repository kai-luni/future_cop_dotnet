#!/bin/bash
# Regenerate or verify tests/golden/hud-draw.vectors against the ORIGINAL C HUD routine.
#
#   hud_test.sh            compare the vectors against a fresh run of the retail routine
#   hud_test.sh --emit     print the observed output (this is what regenerates the file)
#
# The C sources stay in the decomp repo and are only READ: 80047F30.c and 8001C214.c are copied
# into a temporary directory with their __asm__("...") register/address attributes stripped (the
# host compiler cannot express them), then compiled against the recording stubs in hud_stubs.c.
# Nothing is written into either repository; every artefact lands in $OUT.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
DECOMP="${DECOMP:-$HOME/git/future-cop-lapd-ps1}"
DOTNET="${DOTNET_REPO:-$(cd "$HERE/../../.." && pwd)}"
OUT="${OUT:-$(mktemp -d "${TMPDIR:-/tmp}/hud-harness.XXXXXX")}"
mkdir -p "$OUT"
VECTORS="${HUD_VECTORS:-$DOTNET/tests/golden/hud-draw.vectors}"
STATES="${HUD_STATES:-$HERE/hud-draw.states}"
CC="${CC:-gcc}"

GAME="$DECOMP/src/game"
for src in 80047F30.c 8001C214.c; do
    [ -f "$GAME/$src" ] || { echo "hud: missing $GAME/$src" >&2; exit 2; }
done
[ -f "$VECTORS" ] || { echo "hud: missing vectors $VECTORS" >&2; exit 2; }
[ -f "$STATES" ] || { echo "hud: missing states $STATES" >&2; exit 2; }

# Translate the retail inline-assembly annotations into plain host C:
#   register s32 x __asm__("$4") = 1;        ->  register s32 x = 1;
#   __asm__ volatile("" : "+r"(a), "+r"(b)); ->  /* register-allocation hint only, drop */
#   __asm__ volatile("addu %0,%1,$zero" : "=r"(one) : "r"(arg), ...);
#                                            ->  one = arg;      (the one real assignment)
# The last form appears once, at 80047F30.c:276, and only copies arg into one. Anything else that
# cannot be translated is a hard error, so a changed retail source fails loudly instead of compiling
# a silently different routine.
python3 - "$GAME" "$OUT" <<'PY'
import re, sys, pathlib

game, out = pathlib.Path(sys.argv[1]), pathlib.Path(sys.argv[2])

# The only non-empty asm body in these two files, and what it does: copy arg into one.
TRANSLATED = {"addu %0,%1,$zero": lambda m: f"{m.group('out')} = {m.group('first').strip()};"}

ASM_ONELINER = re.compile(
    r'__asm__\s+volatile\s*\(\s*"(?P<body>[^"]*)"\s*:\s*"=r"\s*\(\s*(?P<out>\w+)\s*\)'
    r'\s*:\s*"r"\s*\(\s*(?P<first>\w+)\s*\)[^;]*\)\s*;')
ASM_EMPTY = re.compile(r'__asm__\s+volatile\s*\(\s*""\s*:[^;]*\)\s*;')
ASM_DECL = re.compile(r'\s*__asm__\s*\(\s*"[^"]*"\s*\)')

def translate(name, text):
    lines = []
    for raw in text.splitlines():
        # strip the address/register aliases declared right on the symbol: __asm__("D_8009FAA4")
        line = ASM_DECL.sub('', raw)
        if '__asm__' in line:
            # empty statement: a register-allocation hint with no effect on the computation
            line = ASM_EMPTY.sub('/* register hint, dropped */;', line)
        if '__asm__' in line:
            m = ASM_ONELINER.search(line)
            if not m or m.group('body').strip() not in TRANSLATED:
                raise SystemExit(
                    f"hud: {name}: inline asm this harness does not know, refusing to guess: {raw.strip()!r}")
            line = line[:m.start()] + TRANSLATED[m.group('body').strip()](m) + line[m.end():]
        if '__asm__' in line:
            raise SystemExit(f"hud: {name}: leftover inline asm: {raw.strip()!r}")
        lines.append(line)
    return "\n".join(lines) + "\n"

for name in ("80047F30.c", "8001C214.c"):
    (out / name).write_text(translate(name, (game / name).read_text()))
PY

# The retail sources include "game/types.h"; the harness ships the same typedefs under game/.
mkdir -p "$OUT/game"
cp "$HERE/types.h" "$OUT/game/types.h"

"$CC" -std=gnu99 -w -O0 -I"$HERE" -I"$OUT" \
    "$HERE/hud_stubs.c" "$OUT/80047F30.c" "$OUT/8001C214.c" \
    -o "$OUT/hud_draw_gen"
echo "hud: built $OUT/hud_draw_gen"

"$OUT/hud_draw_gen" "$STATES" > "$OUT/observed.txt"
echo "hud: wrote $OUT/observed.txt"

if [ "${1:-}" = "--emit" ] || [ "${1:-}" = "-e" ]; then
    cat "$OUT/observed.txt"
    exit 0
fi

# Compare only the CASE bodies: the vector file carries a long provenance header, the harness
# output does not, so the header is skipped on the vector side.
python3 - "$VECTORS" "$OUT/observed.txt" <<'PY'
import sys, pathlib

def norm_case(line):
    """The vector file omits fields whose value is zero; the harness prints all of them. Drop the
    zero-valued keys on both sides so only meaningful state is compared."""
    parts = line.split()
    fields = []
    for tok in parts[2:]:
        key, _, val = tok.partition("=")
        if val not in ("0", ""):
            fields.append(tok)
    return " ".join([parts[0], parts[1]] + sorted(fields))

def bodies(path, skip_header):
    lines, started = [], False
    for raw in pathlib.Path(path).read_text().splitlines():
        if raw.startswith("CASE "):
            started = True
        elif not started and skip_header:
            continue
        if started:
            if raw.startswith("CASE "):
                lines.append(norm_case(raw))
            elif raw.strip():
                lines.append(raw.rstrip())
    return lines

want = [l for l in bodies(sys.argv[1], True) if l.strip()]
got = [l for l in bodies(sys.argv[2], False) if l.strip()]
if want == got:
    cases = sum(1 for l in want if l.startswith("CASE "))
    print(f"hud: all {cases} vectors PASS")
    sys.exit(0)

count = sum(1 for l in want if l.startswith("CASE "))
print(f"hud: MISMATCH over {count} vectors", file=sys.stderr)
name = "?"
for i, (w, g) in enumerate(zip(want, got)):
    if w != g:
        if w.startswith("CASE "):
            name = w.split()[1]
        print(f"  case {name} line {i}: expected {w!r}\n" + " " * 16 + f"got      {g!r}", file=sys.stderr)
        break
if len(want) != len(got):
    print(f"  line count differs: vectors {len(want)}, observed {len(got)}", file=sys.stderr)
sys.exit(1)
PY
