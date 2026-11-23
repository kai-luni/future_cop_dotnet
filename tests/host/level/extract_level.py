#!/usr/bin/env python3
"""Extract the Ctil and Cptc payloads of a retail .MIS into a flat blob for level_harness.

Independent of the .NET reader: chunk tags are byte-reversed ASCII in the file, records are
streamed as SHOC/SHDR header + SHOC/SDAT packets, FILL ends the current 0x6000 read block
(func_8002902C:173-175). Blob entries: u32 kind (1 = Ctil, 2 = Cptc), u32 id, u32 size,
payload padded to 4 bytes.
Usage: extract_level.py <mission.MIS> <out.blob>
"""
import struct
import sys


def tag(name):
    return struct.unpack('<I', name.encode()[::-1])[0]


def reassemble(m, header, size):
    pk = header + struct.unpack_from('<i', m, header + 4)[0]
    out = bytearray()
    while len(out) < size:
        t, length = struct.unpack_from('<II', m, pk)
        if t == tag('FILL'):
            pk = (pk // 0x6000 + 1) * 0x6000
            continue
        if t != tag('SHOC') or struct.unpack_from('<I', m, pk + 16)[0] != tag('SDAT'):
            raise ValueError('unexpected packet at 0x%x' % pk)
        out += m[pk + 20:pk + length]
        pk += length
    if len(out) != size:
        raise ValueError('payload overrun')
    return bytes(out)


def main():
    m = open(sys.argv[1], 'rb').read()
    kinds = {tag('Ctil'): 1, tag('Cptc'): 2}
    blob = bytearray()
    count = {1: 0, 2: 0}
    for p in range(24, len(m) - 12, 4):
        t = struct.unpack_from('<I', m, p)[0]
        if t not in kinds:
            continue
        if struct.unpack_from('<I', m, p - 24)[0] != tag('SHOC') or struct.unpack_from('<I', m, p - 8)[0] != tag('SHDR'):
            continue
        rid, size = struct.unpack_from('<iI', m, p + 4)
        payload = reassemble(m, p - 24, size)
        blob += struct.pack('<III', kinds[t], rid, size) + payload + b'\0' * ((-size) & 3)
        count[kinds[t]] += 1
    open(sys.argv[2], 'wb').write(blob)
    print('extract: %d Ctil, %d Cptc, %d bytes' % (count[1], count[2], len(blob)))


if __name__ == '__main__':
    main()
