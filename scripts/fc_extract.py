#!/usr/bin/env python3
"""Extract a PS1 disc (raw MODE2/2352 .bin or cooked 2048 .iso) into a file tree.

Stdlib only. Usage:
    python3 scripts/fc_extract.py <disc.bin|disc.iso> <output-dir>

Raw MODE2 tracks are first converted to a 2048-byte-sector ISO view (same
logic as the decomp repo's tools/extract_mode2.py), then the ISO9660 primary
volume is walked and every file written out. XA Form 2 payloads (movies,
XA audio) are not reconstructed faithfully - only the ISO9660 logical view
is extracted, which is exactly what the asset tools need (.MIS etc.).
"""
from __future__ import annotations

import argparse
import struct
import sys
import tempfile
from pathlib import Path

RAW_SECTOR = 2352
USER_OFFSET = 24
USER_SIZE = 2048
SYNC = bytes.fromhex("00ffffffffffffffffffff00")


def log(msg: str) -> None:
    print(msg, flush=True)


def mode2_to_iso(source: Path, destination: Path) -> int:
    size = source.stat().st_size
    if size % RAW_SECTOR:
        raise ValueError(f"input size {size} is not a multiple of {RAW_SECTOR}")
    sectors = size // RAW_SECTOR
    form2 = 0
    with source.open("rb") as src, destination.open("wb") as dst:
        for index in range(sectors):
            sector = src.read(RAW_SECTOR)
            if sector[:12] != SYNC:
                raise ValueError(f"sector {index}: invalid CD sync pattern")
            if sector[16:20] != sector[20:24]:
                raise ValueError(f"sector {index}: XA subheader copies differ")
            form2 += bool(sector[18] & 0x20)
            dst.write(sector[USER_OFFSET:USER_OFFSET + USER_SIZE])
    log(f"mode2: {sectors} sectors converted (form2: {form2})")
    return sectors


def read_sector(fh, lba: int) -> bytes:
    fh.seek(lba * USER_SIZE)
    data = fh.read(USER_SIZE)
    if len(data) != USER_SIZE:
        raise ValueError(f"short read at sector {lba}")
    return data


def dir_records(data: bytes):
    """Yield (name, flags, extent_lba, size) for one directory sector."""
    offset = 0
    while offset < len(data):
        length = data[offset]
        if length == 0:
            # zero padding runs to the end of the sector
            break
        rec = data[offset:offset + length]
        extent = struct.unpack_from("<I", rec, 2)[0]
        size = struct.unpack_from("<I", rec, 10)[0]
        flags = rec[25]
        name_len = rec[32]
        raw_name = rec[33:33 + name_len]
        offset += length
        if raw_name in (b"\x00", b"\x01"):
            continue  # '.' and '..'
        name = raw_name.decode("ascii", "replace").split(";")[0]
        yield name, flags, extent, size


def walk(fh, lba: int, size: int, dest: Path, stats: dict) -> None:
    dest.mkdir(parents=True, exist_ok=True)
    remaining = size
    while remaining > 0:
        data = read_sector(fh, lba)
        for name, flags, extent, fsize in dir_records(data):
            target = dest / name
            if flags & 0x02:
                walk(fh, extent, fsize, target, stats)
            else:
                with target.open("wb") as out:
                    left = fsize
                    cur = extent
                    while left > 0:
                        chunk = read_sector(fh, cur)[:min(left, USER_SIZE)]
                        out.write(chunk)
                        left -= len(chunk)
                        cur += 1
                stats["files"] += 1
                stats["bytes"] += fsize
        remaining -= USER_SIZE
        lba += 1


def extract_iso(iso: Path, dest: Path) -> None:
    with iso.open("rb") as fh:
        pvd = read_sector(fh, 16)
        if pvd[0] != 1 or pvd[1:6] != b"CD001":
            raise ValueError("sector 16 is not an ISO9660 primary volume descriptor")
        root = pvd[156:156 + 34]
        extent = struct.unpack_from("<I", root, 2)[0]
        size = struct.unpack_from("<I", root, 10)[0]
        stats = {"files": 0, "bytes": 0}
        walk(fh, extent, size, dest, stats)
    log(f"iso9660: {stats['files']} files, {stats['bytes']} bytes -> {dest}")


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("source", type=Path)
    ap.add_argument("destination", type=Path)
    args = ap.parse_args()
    src = args.source
    size = src.stat().st_size
    if size % RAW_SECTOR == 0:
        with src.open("rb") as fh:
            head = fh.read(12)
        if head == SYNC:
            tmp = Path(tempfile.mkstemp(suffix=".iso")[1])
            try:
                mode2_to_iso(src, tmp)
                extract_iso(tmp, args.destination)
            finally:
                tmp.unlink(missing_ok=True)
            return
    if size % USER_SIZE == 0:
        extract_iso(src, args.destination)
        return
    raise ValueError(f"{src}: neither MODE2/2352 nor 2048-byte ISO (size {size})")


if __name__ == "__main__":
    main()
