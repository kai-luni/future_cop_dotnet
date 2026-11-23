#!/usr/bin/env python3
"""Generate chained WALK case inputs for mech-walk.cases.

Every scenario starts from a hand-written state and then feeds the state printed by the UNCHANGED
decomp walker (func_800549F0 under qemu, the harness binary built by mech_test.sh) back in as the
input of the next tick, so consecutive WALK lines form a real control sequence. Only inputs are
written here; regen_vectors.sh re-runs every line to produce the expectations.

Usage: gen_walk_cases.py <built mech_harness dir (OUT of mech_test.sh)> <cases file> <out file>
The cases file supplies the MIS and the first CLIPS line (the retail walker clip table).
"""
import os
import subprocess
import sys
import tempfile

OUT, CASES, DEST = sys.argv[1], sys.argv[2], sys.argv[3]
QEMU = os.environ.get('QEMU', os.path.expanduser('~/.local/share/qemu-user/extracted/usr/bin/qemu-mipsel'))
DECOMP = os.environ.get('DECOMP', os.path.expanduser('~/git/future-cop-lapd-ps1'))
SLUS = os.environ.get('SLUS', DECOMP + '/baseroms/us/SLUS_007.39')

head = []
for line in open(CASES):
    if line.startswith('MIS ') or (line.startswith('CLIPS ') and not any(h.startswith('CLIPS ') for h in head)):
        head.append(line.rstrip('\n'))

SPECIAL = {'tclip', 'flags', 'steps', 'seed', 'padtype', 'padx', 'pady', 'mode0', 'mode1', 'inv0', 'inv1', 'ret',
           'evn', 'ev0', 'ev1', 'ev2', 'ev3', 'msgn', 'msgcode', 'msgid', 'msgval', 'msgkind', 'framen', 'frame',
           'hudr', 'hudl', 'a0', 'a1', 'a2', 'a3', 'o0', 'o1', 'o2', 'selfaddr'}
CARRY_SPECIAL = {'tclip', 'hudr', 'hudl'}


def kv(d):
    return ' '.join('%s=%d' % (k, v) for k, v in d.items())


def run(inputs, kind='WALK'):
    with tempfile.NamedTemporaryFile('w', suffix='.vectors', delete=False) as f:
        f.write('\n'.join(head) + '\n' + kind + ' ' + kv(inputs) + '\n')
        path = f.name
    try:
        out = subprocess.run([QEMU, OUT + '/mech_harness', path, OUT + '/level.blob', SLUS, '--emit'],
                             check=True, capture_output=True, text=True).stdout
    finally:
        os.unlink(path)
    line = [l for l in out.splitlines() if l.startswith(kind + ' ')][-1]
    res = {}
    for tok in line.split('->', 1)[1].split():
        k, v = tok.split('=')
        res[k] = int(v)
    return res


def carry(res):
    return {k: v for k, v in res.items() if v != 0 and (k not in SPECIAL or k in CARRY_SPECIAL)}


SIN = None


def footprint():
    d = {}
    for i, (x, y, z) in enumerate([(-0x50, 174, 0x50), (0x50, 174, 0x50), (0x50, 174, -0x50), (-0x50, 174, -0x50)]):
        d['f%dx' % i], d['f%dy' % i], d['f%dz' % i] = x, y, z
    return d


def start(x, y, z, angle, ax, az, **extra):
    d = dict(x=x, y=y, z=z, angle=angle, ax=ax, az=az, ny=0xFFFE, health=0x40, **footprint())
    d.update(extra)
    return d


def settle(state):
    """Footprint world corners of the start position: run func_80052708 once (FOOT) with the
    corners at the start position as both previous and new points and keep only its p* output."""
    probe = dict(state)
    res = run(probe, 'FOOT')
    for k in ('p0x', 'p0y', 'p0z', 'p1x', 'p1y', 'p1z', 'p2x', 'p2y', 'p2z', 'p3x', 'p3y', 'p3z'):
        probe[k] = res[k]
    res = run(probe, 'FOOT')
    out = dict(state)
    for k in ('p0x', 'p0y', 'p0z', 'p1x', 'p1y', 'p1z', 'p2x', 'p2y', 'p2z', 'p3x', 'p3y', 'p3z'):
        out[k] = res[k]
    return out


def scenario(name, state, ticks, lines):
    """ticks: list of (flags, steps, extra specials dict)."""
    lines.append('# scenario: %s' % name)
    state = settle(state)
    for flags, steps, extra in ticks:
        inp = dict(state)
        inp.update(dict(flags=flags, steps=steps))
        inp.update(extra)
        lines.append('WALK ' + kv(inp) + ' ->')
        res = run(inp)
        state = carry(res)


def main():
    lines = ['# --- walker tick func_800549F0: chained control sequences (gen_walk_cases.py) ---']
    UP, DOWN, LEFT, RIGHT = 0x40000000, 0x80000000, 0x10000000, 0x20000000
    TRI, L1, L2, R2 = 0x08000000, 0x02000000, 0x00100000, 0x00200000
    D = dict(padtype=4)  # digital pad: the stick is not read
    G = -0x7900          # flat ground of CW/M1A1.MIS around (0xD0000, 0xE8000)
    flat = start(0xD0000, G, 0xE8000, 0, 0, 0x10000)
    scenario('idle on flat ground', flat, [(0, 1, D)] * 6, lines)
    scenario('run forward (up) and stop', flat, [(UP, 1, D)] * 24 + [(0, 1, D)] * 8, lines)
    scenario('walk forward with L1 (move 1) two sub-steps', flat, [(UP | L1, 2, D)] * 12, lines)
    scenario('back (down)', flat, [(DOWN, 1, D)] * 10, lines)
    scenario('turn left then right', flat, [(LEFT, 1, D)] * 8 + [(RIGHT, 1, D)] * 8, lines)
    scenario('run and turn', flat, [(UP | RIGHT, 1, D)] * 16, lines)
    scenario('strafe L2 then R2', flat, [(L2, 1, D)] * 8 + [(R2, 1, D)] * 8, lines)
    scenario('L1 strafe (moves 5/6) and back-walk (move 2)', flat, [(L1 | L2, 1, D)] * 5 + [(L1 | R2, 1, D)] * 5 + [(L1 | DOWN, 1, D)] * 5, lines)
    scenario('crouch L1+triangle', flat, [(L1 | TRI, 1, D)] * 10 + [(0, 1, D)] * 6, lines)
    scenario('jump on flat ground', flat, [(TRI, 1, D)] * 2 + [(0, 1, D)] * 60, lines)
    scenario('running jump', flat, [(UP, 1, D)] * 10 + [(UP | TRI, 1, D)] * 2 + [(UP, 1, D)] * 50, lines)
    scenario('analog stick forward-right', flat, [(0, 1, dict(padtype=7, padx=0xC0, pady=0x20))] * 16, lines)
    scenario('analog stick small deflection back-left', flat, [(0, 1, dict(padtype=7, padx=0x50, pady=0xB0))] * 10, lines)
    scenario('knockback impulse while idle', dict(flat, ix=0x60000, iy=0x20000, iz=-0x40000), [(0, 1, D)] * 12, lines)
    scenario('slow surface level and grab flag', dict(flat, slow=2, grab=1), [(UP, 1, D)] * 6, lines)
    # cliff / building edges (CW/M1A1 height map): walk at buildings and off ledges
    scenario('run into the building east of the plaza', start(0xE8000, G, 0xE4000, 0, 0, 0x10000), [(UP, 1, D)] * 40, lines)
    scenario('run north into terrain step', start(0xE0000, G, 0xD8000, 0xC00, -0x10000, 0), [(UP, 1, D)] * 40, lines)
    scenario('walk up the ramp', start(0xD8000, G, 0xC8000, 0x400, 0x10000, 0), [(UP, 1, D)] * 40, lines)
    scenario('jump at the building (probe ahead)', start(0xE8000, G, 0xEA000, 0, 0, 0x10000), [(TRI, 1, D)] * 2 + [(0, 1, D)] * 40, lines)
    roof = start(0xEC000, 0x300, 0xF8000, 0x800, 0, -0x10000)
    scenario('run off a roof and land hard', roof, [(UP, 1, D)] * 70, lines)
    scenario('run off a roof, invulnerable, session mode 2', roof, [(UP, 1, dict(D, inv0=1, mode0=2))] * 70, lines)
    open(DEST, 'w').write('\n'.join(lines) + '\n')
    print('%d lines' % len(lines))


if __name__ == '__main__':
    main()
