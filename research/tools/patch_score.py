#!/usr/bin/env python3
"""
Write a score into any mapped NVRAM table, on any of the cabinet's platforms.

`research/patch_nvram_score.py` predates the full mapping round and only knows
the Stern SAM record layout. This one is driven entirely by the map, so it
works for Williams WPC (BCD scores behind a block checksum), Stern SAM
(little-endian ints behind a per-record checksum) and Whitestar / Data East
(BCD scores with no checksum) without knowing which is which -- the map already
says where the bytes are and what protects them.

Checksums are recomputed for every region the write lands in. Nothing is
written unless every patch applies cleanly, and the result is re-read and
re-verified before the output file is saved.

Usage:
    python3 patch_score.py <map.json> <input.nv> <output.nv> \
        "Grand Champion=TST:987654321" "First Place=ABC:12345"

Each patch is "<label>=<initials>:<value>", where <label> matches a `label` in
the map's high_scores or mode_champions list. Pass --dry-run to see what would
change without writing a file.
"""
import argparse
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from nvmap import NvramMap, PINMAME_TRAILER  # noqa: E402


def value_field(entry):
    for key in ("score", "counter"):
        if key in entry:
            return key, entry[key]
    raise ValueError("%r has no score or counter field" % entry.get("label"))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("map")
    ap.add_argument("nvram")
    ap.add_argument("output")
    ap.add_argument("patch", nargs="+", help='"<label>=<initials>:<value>"')
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    table = NvramMap.load(args.map, args.nvram)
    with open(args.nvram, "rb") as f:
        raw = bytearray(f.read())
    trailer = raw[len(raw) - PINMAME_TRAILER:]
    before = bytes(table.data)

    by_label = {}
    for group in ("high_scores", "mode_champions"):
        for entry in table.map.get(group, []):
            by_label[entry["label"]] = entry

    for patch in args.patch:
        label, _, rest = patch.partition("=")
        initials, _, value = rest.partition(":")
        if label not in by_label:
            raise SystemExit("unknown label %r; known labels: %s"
                             % (label, ", ".join(sorted(by_label))))
        entry = by_label[label]
        key, field = value_field(entry)

        old_initials = table.read_field(entry["initials"]) if "initials" in entry else None
        old_value = table.read_field(field)

        if "initials" in entry:
            width = len(table.field_offsets(entry["initials"]))
            limit = width - 1 if entry["initials"].get("null") == "terminate" else width
            if len(initials) > limit:
                raise SystemExit("initials %r too long for %r (max %d)"
                                 % (initials, label, limit))
            table.write_field(entry["initials"], initials)
        table.write_field(field, int(value))

        print("%s: %r %s -> %r %s"
              % (label, old_initials, "{:,}".format(int(old_value)),
                 initials, "{:,}".format(int(value))))

    bad = table.verify_checksums()
    if bad:
        raise SystemExit("refusing to write: %d checksum region(s) still "
                         "invalid after patching" % len(bad))

    changed = [i for i in range(len(before)) if before[i] != table.data[i]]
    print("%d byte(s) changed%s" %
          (len(changed),
           "" if not changed else " (0x%04X-0x%04X)" % (changed[0], changed[-1])))

    if args.dry_run:
        print("--dry-run: nothing written")
        return 0

    with open(args.output, "wb") as f:
        f.write(table.data + trailer)
    print("wrote %s (%d bytes)" % (args.output, len(table.data) + len(trailer)))

    # Re-read from disk rather than trusting the in-memory copy.
    check = NvramMap.load(args.map, args.output)
    bad = check.verify_checksums()
    print("verify: %d/%d checksum region(s) valid"
          % (sum(1 for _ in check.checksum_regions()) - len(bad),
             sum(1 for _ in check.checksum_regions())))
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
