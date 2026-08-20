#!/usr/bin/env python3
"""
Search an NVRAM image for the checksum region(s) protecting a byte range.

Why this exists
---------------
Reading scores only needs field offsets.  *Writing* them needs to know whether
the game ROM validates that region on boot, because a stale checksum makes the
ROM throw the record away and restore its compiled-in default -- which is
exactly what happened repeatedly on Stern SAM before the per-record checksum
was found (see research/FINDINGS.md).

tomlogic/pinball-memory-maps describes two schemes, and every checksum found on
this cabinet's tables so far is one of them:

  checksum8   the last byte of [start, end] is set so the low byte of the sum
              of the whole range is 0xFF
  checksum16  the last two bytes of [start, end] hold 0xFFFF minus the sum of
              all prior bytes in the range

This script inverts that: given the offsets you care about, it reports every
(start, end) whose stored checksum already validates.  A single NVRAM image
produces far too many coincidental hits to be useful on its own -- a checksum8
match is a 1-in-256 event, so a few thousand candidate ranges yield dozens of
false positives.  Pass **two or more independent dumps of the same ROM** and a
candidate must validate in all of them, which is what makes the result mean
something.  With two dumps a checksum8 false positive is a 1-in-65536 event.

A negative result is a real result: if nothing validates across several dumps,
the region is most likely not sum-protected at all, and writing to it is a
plain byte write.  That is the current conclusion for the Whitestar and Data
East tables on this cabinet (see research/TABLE-MAPPING.md).

Usage:
    # Whitestar high-score block (scores 0x15DC..0x15F4, initials ..0x1653)
    python3 find_checksums.py --cover 0x15DC-0x1653 --size 0x2000 \
        a/lotr.nv b/lotr.nv

Options:
    --cover LO-HI     byte range that the checksum must protect (required)
    --size N          bytes of the file that are NVRAM (default: whole file
                      minus PinMAME's 46-byte trailer)
    --window N        how far outside `cover` to look (default 0x200)
    --big-endian /    byte order of a 16-bit checksum (default: big, which is
    --little-endian   right for 6809/6808 platforms; SAM is little-endian)
"""
import argparse
import sys

PINMAME_TRAILER = 46


def load(path, size):
    with open(path, "rb") as f:
        data = bytearray(f.read())
    if size is None:
        size = len(data) - PINMAME_TRAILER
    return data[:size]


def prefix_sums(data):
    totals = [0] * (len(data) + 1)
    running = 0
    for i, byte in enumerate(data):
        running += byte
        totals[i + 1] = running
    return totals


def search(images, cover_lo, cover_hi, window, big_endian):
    """Return (checksum8_hits, checksum16_hits) as lists of (start, end)."""
    sums = [prefix_sums(d) for d in images]
    size = min(len(d) for d in images)
    hits8, hits16 = [], []

    start_lo = max(0, cover_lo - window)
    end_hi = min(size - 2, cover_hi + window)

    for end in range(cover_hi + 1, end_hi + 1):
        for start in range(start_lo, cover_lo + 1):
            if all(((totals[end + 1] - totals[start]) & 0xFF) == 0xFF
                   for totals in sums):
                hits8.append((start, end))
            ok16 = True
            for data, totals in zip(images, sums):
                if big_endian:
                    stored = (data[end] << 8) | data[end + 1]
                else:
                    stored = (data[end + 1] << 8) | data[end]
                if ((totals[end] - totals[start]) + stored) & 0xFFFF != 0xFFFF:
                    ok16 = False
                    break
            if ok16:
                hits16.append((start, end + 1))
    return hits8, hits16


def parse_range(text):
    lo, _, hi = text.partition("-")
    return int(lo, 0), int(hi, 0)


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("nvram", nargs="+", help="two or more dumps of the same ROM")
    ap.add_argument("--cover", required=True, help="LO-HI byte range to protect")
    ap.add_argument("--size", type=lambda s: int(s, 0), default=None)
    ap.add_argument("--window", type=lambda s: int(s, 0), default=0x200)
    ap.add_argument("--little-endian", dest="big_endian", action="store_false")
    ap.add_argument("--big-endian", dest="big_endian", action="store_true")
    ap.set_defaults(big_endian=True)
    args = ap.parse_args()

    images = [load(p, args.size) for p in args.nvram]
    if len(images) < 2:
        print("warning: one image only -- expect coincidental matches "
              "(1 in 256 for checksum8). Pass a second dump of the same ROM.",
              file=sys.stderr)

    cover_lo, cover_hi = parse_range(args.cover)
    hits8, hits16 = search(images, cover_lo, cover_hi, args.window, args.big_endian)

    print("checked %d image(s), covering 0x%04X-0x%04X" %
          (len(images), cover_lo, cover_hi))
    for label, hits in (("checksum8", hits8), ("checksum16", hits16)):
        print("%s: %d candidate range(s)" % (label, len(hits)))
        for start, end in hits:
            print('  {"start": "0x%04X", "end": "0x%04X"}' % (start, end))
    if not hits8 and not hits16:
        print("no sum-checksum protects this range in every image supplied")
    return 0


if __name__ == "__main__":
    sys.exit(main())
