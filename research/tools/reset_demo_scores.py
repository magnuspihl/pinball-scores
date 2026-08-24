#!/usr/bin/env python3
"""
Generate demo-score-reset NVRAM/STG files: every mapped record's value pushed
to 1, initials set to a single space (blank).

This is the fix for the tables the record-scan work in TABLE-MAPPING.md found
can never show a real score, because the ROM's compiled-in demo default is
higher than anything a player can reach (Star Wars' lowest demo entry is
100,000,000 against ~1-30M real games; Lord of the Rings, Simpsons Pinball
Party and The Addams Family have the same problem). Setting every record --
not just the unreachable ones -- to the same reserved 1/blank marker gives a
single, recognisable "never been played" state across all eighteen tables
rather than a mix of stale factory names and scores.

The marker used to be initials "---", but live-cabinet testing on 2026-08-20
found that Williams WPC's boot-time NVRAM validation rejects "-" (it isn't in
the machine's own selectable initials alphabet) and silently reverts the
record to its factory default -- while a space *is* in that alphabet (Magnus
confirmed by beating a real WPC high score and entering "   ", which survived
a reload and a shutdown). A blank/space marker is also the more natural
"never played" look on every platform's own display than a literal dash.

Reads the committed sample data in ScoresData/ and the maps in
research/nvram-maps and research/stg-maps; writes patched copies to an output
directory without touching the originals. Every NVRAM write is checksum-
verified (recomputed and re-read from disk); every STG write goes through
patch_stg_score.py's same-length stream rewrite and is re-read to confirm.

Usage:
    python3 research/tools/reset_demo_scores.py [--out-dir DIR]
"""
import argparse
import glob
import json
import os
import sys

try:
    import olefile
except ImportError:
    sys.exit("This script requires the 'olefile' package: pip install olefile")

HERE = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, ".."))
from nvmap import NvramMap, PINMAME_TRAILER  # noqa: E402
import patch_stg_score as stg  # noqa: E402

NVRAM_MAPS_DIR = os.path.join(REPO_ROOT, "research", "nvram-maps")
STG_MAPS_DIR = os.path.join(REPO_ROOT, "research", "stg-maps")
DEFAULT_NVRAM_DIR = os.path.join(REPO_ROOT, "ScoresData", "nvram")
DEFAULT_STG = os.path.join(REPO_ROOT, "ScoresData", "User", "VPReg.stg")

MARKER_INITIALS = " "
MARKER_VALUE = 1


def value_field(entry):
    for key in ("score", "counter"):
        if key in entry:
            return key, entry[key]
    raise ValueError("%r has no score or counter field" % entry.get("label"))


def category_for(table, label):
    for category in table.categories():
        if label in category["slots"]:
            return category
    return {}


def blank_value(category):
    """What "no record here" is worth in this category.

    A ranked ROM treats a zeroed record as invalid and restores its compiled-in
    factory default, which is the whole reason the marker is 1 rather than 0.
    A positional category is a log, not a leaderboard, and zero is precisely
    what an untouched machine holds there -- Medieval Madness ships with kings
    #2-#4 at counter 0 -- so zero is both accepted and correct, and it keeps the
    ROM's rule that each king's counter outranks the next.
    """
    return 0 if category.get("order") == "positional" else MARKER_VALUE


def spare_fields(entry, value_key):
    """Numeric fields of a record other than the value.

    Only Medieval Madness' kings have any: the ordinal behind "CROWNED FOR THE
    SECOND TIME". Left alone, a wiped cabinet still announces a coronation that
    the blanked initials no longer name. Clocks are skipped -- there is no zero
    for a date the ROM would accept, and nothing renders one without an ordinal.
    """
    for key, field in entry.items():
        if key in ("initials", value_key) or not isinstance(field, dict):
            continue
        if field.get("encoding") in (None, "wpc_rtc", "ch"):
            continue
        yield key, field


def fix_stwr_107_shadow_copy(table):
    """Star Wars keeps a second, undocumented copy of the top-slot score
    (and a leading-digit-only copy of every rank) elsewhere in NVRAM, and
    the boot code (disassembled 2026-08-20, see research/rom-analysis/) at
    0xc07d compares the live table's rank-1 leading BCD digit ($1694)
    against this shadow's ($1e37); if live < shadow, it overwrites the
    *entire* live table from the shadow copy. This is why every previous
    reset attempt reverted on this table alone, regardless of marker
    initials, value, or tie-ness -- the marker's value (1) is always lower
    than the shadow's compiled-in factory default, so the "corruption"
    check always fired. The fix is to mirror the marker into the shadow
    bytes too, so live == shadow and the check never triggers.
    """
    table.data[0x1e37] = table.data[0x1694]
    table.data[0x1e1f:0x1e23] = table.data[0x167c:0x1680]
    for live in (0x1695, 0x1696, 0x1697, 0x1698, 0x1699):
        table.data[0x1e37 + (live - 0x1694)] = table.data[live]


def fix_btmn_106_shadow_copy(table):
    """Guess, not a confirmed finding like stwr_107's (no Batman ROM to check
    the actual gating logic against) -- but the untouched sample NVRAM has a
    byte-for-byte duplicate of the entire 6-rank score body sitting at
    0x1e1d-0x1e34, mirroring the live table at 0x1d98-0x1daf exactly (found
    by direct byte comparison, see research/rom-analysis/). Same shape as
    Star Wars' proven shadow-copy mechanism, so mirror it the same way on the
    chance Batman's boot code does the same live-vs-shadow comparison.
    """
    table.data[0x1e1d:0x1e35] = table.data[0x1d98:0x1db0]


def reset_nvram(rom, map_path, nvram_path, out_path):
    table = NvramMap.load(map_path, nvram_path)
    with open(nvram_path, "rb") as f:
        raw = bytearray(f.read())
    trailer = raw[len(raw) - PINMAME_TRAILER:]

    records = 0
    for group in ("high_scores", "mode_champions"):
        for entry in table.map.get(group, []):
            if "initials" in entry:
                table.write_field(entry["initials"], MARKER_INITIALS)
            value_key, field = value_field(entry)
            category = category_for(table, entry.get("label"))
            table.write_field(field, blank_value(category))
            for _, spare in spare_fields(entry, value_key):
                table.write_field(spare, 0)
            records += 1

    if rom == "stwr_107":
        fix_stwr_107_shadow_copy(table)
    elif rom == "btmn_106":
        fix_btmn_106_shadow_copy(table)

    bad = table.verify_checksums()
    if bad:
        raise SystemExit("%s: %d checksum region(s) invalid after reset: %s"
                         % (map_path, len(bad), bad))

    with open(out_path, "wb") as f:
        f.write(table.data + trailer)

    # Re-read from disk and confirm every record actually landed.
    check = NvramMap.load(map_path, out_path)
    if check.verify_checksums():
        raise SystemExit("%s: checksums invalid on re-read" % out_path)
    for group in ("high_scores", "mode_champions"):
        for entry in check.map.get(group, []):
            if "initials" in entry:
                got = check.read_field(entry["initials"])
                if got.strip(" "):
                    raise SystemExit("%s: %r initials read back as %r, expected blank"
                                     % (out_path, entry.get("label"), got))
            value_key, field = value_field(entry)
            expected = blank_value(category_for(check, entry.get("label")))
            got = check.read_field(field)
            if got != expected:
                raise SystemExit("%s: %r value read back as %r, expected %r"
                                 % (out_path, entry.get("label"), got, expected))
            for key, spare in spare_fields(entry, value_key):
                got = check.read_field(spare)
                if got != 0:
                    raise SystemExit("%s: %r %s read back as %r, expected 0"
                                     % (out_path, entry.get("label"), key, got))
    return records


def reset_stg(stg_maps, in_path, out_path):
    ole = olefile.OleFileIO(in_path)
    raw = bytearray(open(in_path, "rb").read())

    def current_value(name):
        offsets = stg.resolve_stream(name, ole)
        current = stg.read_via_offsets(bytes(raw), offsets)
        expected = ole.openstream(name).read()
        if current != expected:
            raise SystemExit("offset resolution mismatch for %r" % name)
        return offsets, current.decode("utf-16-le")

    patches = {}  # stream name -> new value (str)
    records = 0
    for map_path in stg_maps:
        d = json.load(open(map_path))
        table_name = d["_metadata"]["storage"]
        for group in ("high_scores", "mode_champions"):
            for entry in d.get(group, []):
                if "initials" in entry:
                    name = "%s/%s" % (table_name, entry["initials"]["stream"])
                    _, cur = current_value(name)
                    patches[name] = MARKER_INITIALS.ljust(len(cur))
                _, field = value_field(entry)
                name = "%s/%s" % (table_name, field["stream"])
                _, cur = current_value(name)
                patches[name] = str(MARKER_VALUE).rjust(len(cur), "0")
                records += 1

    for name, new_value in patches.items():
        offsets, current = current_value(name)
        new_bytes = new_value.encode("utf-16-le")
        if len(new_bytes) != len(b"".join(raw[o:o + l] for o, l in offsets)):
            raise SystemExit("%s: %r is not the same byte length as %r"
                             % (name, new_value, current))
        pos = 0
        for off, length in offsets:
            raw[off:off + length] = new_bytes[pos:pos + length]
            pos += length

    with open(out_path, "wb") as f:
        f.write(raw)

    verify = olefile.OleFileIO(out_path)
    for name, new_value in patches.items():
        got = verify.openstream(name).read().decode("utf-16-le")
        if got != new_value:
            raise SystemExit("%s: re-read as %r, expected %r" % (name, got, new_value))
    return records, len(patches)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--nvram-dir", default=DEFAULT_NVRAM_DIR)
    ap.add_argument("--stg", default=DEFAULT_STG)
    ap.add_argument("--out-dir", default=os.path.join(REPO_ROOT, "research", "demo-reset"))
    args = ap.parse_args()

    nvram_out = os.path.join(args.out_dir, "nvram")
    os.makedirs(nvram_out, exist_ok=True)

    total_records = 0
    tables_done = 0
    for map_path in sorted(glob.glob(os.path.join(NVRAM_MAPS_DIR, "*.map.json"))):
        rom = os.path.basename(map_path)[:-len(".map.json")]
        nvram_path = os.path.join(args.nvram_dir, "%s.nv" % rom)
        if not os.path.exists(nvram_path):
            print("skip %s: no NVRAM sample at %s" % (rom, nvram_path))
            continue
        out_path = os.path.join(nvram_out, "%s.nv" % rom)
        records = reset_nvram(rom, map_path, nvram_path, out_path)
        print("%s: %d record(s) reset -> %s" % (rom, records, out_path))
        total_records += records
        tables_done += 1

    stg_maps = sorted(glob.glob(os.path.join(STG_MAPS_DIR, "*.map.json")))
    stg_out = os.path.join(args.out_dir, "VPReg.stg")
    if stg_maps and os.path.exists(args.stg):
        records, streams = reset_stg(stg_maps, args.stg, stg_out)
        print("VPReg.stg: %d record(s) / %d stream(s) reset -> %s"
              % (records, streams, stg_out))
        total_records += records
        tables_done += len(stg_maps)

    print("\n%d table(s), %d record(s) total reset to %r / %d"
          " (positional categories to %r / 0, which is their own empty state)"
          % (tables_done, total_records, MARKER_INITIALS, MARKER_VALUE,
             MARKER_INITIALS))


if __name__ == "__main__":
    main()
