#!/usr/bin/env python3
"""
Discover every high-score / mode-champion record in a Stern SAM .nv file.

Stern SAM stores each score record as a 32-byte (0x20) struct:

      +0x00  char     initials[]     ASCII, NUL-terminated, 0xFF-padded
      +0x18  uint32   score          little-endian
      +0x1c  uint16   checksum       little-endian, = 0xFFFF - sum(bytes +0x00..+0x1b)
      +0x1e  uint16   0xFFFF         filler

The checksum was recovered by disassembling the smanve_101 game ROM (see
research/rom-analysis/NOTES.md) and matches the `checksum16` convention that
tomlogic/pinball-memory-maps already uses for the Star Trek SAM maps:
"the last two bytes of the range are 0xFFFF minus the sum of all prior bytes".

Because a record's checksum is a 16-bit function of its own 28 content bytes,
a valid checksum is a *signature*: scanning a whole 128KB NVRAM image for
offsets where it holds finds the complete record table with roughly one false
positive per 65536 candidates.  That is what this script does, so a map can be
rebuilt from scratch for any SAM title -- including ones nobody has published
a map for -- without knowing its layout in advance.

Records that have never been written are 0xFF-filled (checksum included) and
therefore do NOT show up.  Re-run this against a fresh dump from the machine
after more of the game's champion slots have been earned; any new slots that
appear should be added to that table's map.

Usage:
    python3 sam_record_scan.py <file.nv> [<file.nv> ...]
    python3 sam_record_scan.py --json <file.nv>      # machine-readable
"""
import json
import sys

NVRAM_BASE = 0x02100000
NVRAM_SIZE = 0x20000
RECORD_SIZE = 0x20
SCORE_OFFSET = 0x18
CHECKSUM_OFFSET = 0x1C
CHECKSUM_COVERAGE = 0x1C  # bytes +0x00 .. +0x1b are summed


def sam_checksum(record):
    """0xFFFF minus the sum of the record's first 28 bytes."""
    return (0xFFFF - sum(record[:CHECKSUM_COVERAGE])) & 0xFFFF


def read_nvram(path):
    with open(path, "rb") as f:
        return bytearray(f.read())[:NVRAM_SIZE]


def scan(data, step=4):
    """Yield (cpu_address, initials, score) for every checksum-valid record.

    `step` of 4 (rather than 0x20) means the scan does not assume where the
    record table starts, only that records are 4-byte aligned.
    """
    for offset in range(0, len(data) - RECORD_SIZE, step):
        record = data[offset:offset + RECORD_SIZE]
        stored = record[CHECKSUM_OFFSET] | (record[CHECKSUM_OFFSET + 1] << 8)
        if stored != sam_checksum(record):
            continue
        # An all-0xFF or all-0x00 block that happens to carry a matching
        # checksum is an unwritten slot, not a record.
        if record[0] in (0x00, 0xFF):
            continue
        initials = bytes(record[:11]).split(b"\0")[0].decode("latin-1")
        score = int.from_bytes(record[SCORE_OFFSET:SCORE_OFFSET + 4], "little")
        yield NVRAM_BASE + offset, initials, score


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("-")]
    as_json = "--json" in sys.argv
    if not args:
        print(__doc__)
        return 1

    for path in args:
        records = list(scan(read_nvram(path)))
        if as_json:
            print(json.dumps({
                "file": path,
                "records": [{"address": "0x%08X" % a, "initials": i, "value": v}
                            for a, i, v in records],
            }, indent=2))
            continue

        print("%s: %d records" % (path, len(records)))
        previous = None
        for address, initials, value in records:
            gap = "" if previous is None else "  (+0x%02X)" % (address - previous)
            print("  0x%08X  %-11r %15s%s" % (address, initials, "{:,}".format(value), gap))
            previous = address
        print()
    return 0


if __name__ == "__main__":
    sys.exit(main())
