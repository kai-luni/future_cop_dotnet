#!/usr/bin/env python3
"""Build decomp units exactly as the matching gate does and prove they are retail-identical.

Imports future-cop-lapd-ps1/tools/verify_matching_units.py read-only and runs its compile_unit()
for every requested unit (cpp + cc1 + maspsx + every recorded post-maspsx rewrite + as + the
gate's link at the retail address). The linked .text must equal the retail bytes of
SLUS_007.39 at the unit's address (the gate's own compare_bytes rule); only then is the gate's
processed assembly copied to <out>/<addr>.proc.s together with its assembler flags, so the
harness links machine code that is byte-identical to the retail function (relocations aside).
Nothing is written into the decomp checkout: every work directory lives under <out>.
Usage: build_units.py <decomp root> <out dir> <addr> [<addr> ...]
"""
import json
import shutil
import sys
from pathlib import Path


def strip_small_data(text, name):
    """maspsx turns each small-data .extern into a local .sbss slot at the end of the file; the
    gate later resolves those names to their retail addresses. The harness does the same through
    --defsym, so the trailing .sbss block (only .align/.space/label lines) is removed here."""
    lines = text.splitlines()
    if '.section .sbss' not in [l.strip() for l in lines]:
        return text
    cut = [l.strip() for l in lines].index('.section .sbss')
    for line in lines[cut + 1:]:
        s = line.strip()
        if s and not (s.startswith('.align') or s.startswith('.space') or (s.endswith(':') and s.startswith('D_'))):
            raise SystemExit('build_units: unexpected line after .sbss in %s: %r' % (name, line))
    return '\n'.join(lines[:cut]) + '\n'


def main():
    root, out = Path(sys.argv[1]), Path(sys.argv[2])
    sys.path.insert(0, str(root / 'tools'))
    import verify_matching_units as gate  # noqa: E402  (read-only use of the gate)

    config = json.loads((root / 'config/us/matching_units.json').read_text())
    executable = (root / 'baseroms/us/SLUS_007.39').read_bytes()
    by_source = {u['source']: u for u in config['units']}
    out.mkdir(parents=True, exist_ok=True)
    for addr in sys.argv[3:]:
        unit = by_source.get('src/game/%s.c' % addr.upper())
        if unit is None:
            raise SystemExit('build_units: %s is not a matching unit' % addr)
        work = out / ('work_' + addr)
        if work.exists():
            shutil.rmtree(work)
        work.mkdir()
        actual = gate.compile_unit(unit, config, 'mipsel-linux-gnu-', work)
        address, size = gate.number(unit['address']), gate.number(unit['size'])
        _, expected = gate.target_range(executable, address, size, unit['name'])
        cmp = gate.compare_bytes(actual, expected)
        if not cmp['exact']:
            raise SystemExit('build_units: %s does not reproduce the retail bytes' % unit['name'])
        (out / ('%s.proc.s' % addr)).write_text(strip_small_data(
            (work / 'processed.s').read_text(), unit['name']))
        flags = gate.maspsx_stage_flags(unit, config, list(unit.get('compiler_flags', config['compiler_flags'])))
        (out / ('%s.asflags' % addr)).write_text(' '.join(gate.assembler_stage_flags(flags)) + '\n')
        print('%s %s %d bytes retail-identical' % (addr, unit['name'], size))


if __name__ == '__main__':
    main()
