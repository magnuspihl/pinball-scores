#!/usr/bin/env python3
"""
Validate every map in research/nvram-maps against real NVRAM files.

For each cabinet table this checks four things:

  READ       every mapped field decodes, and the values look like a plausible
             leaderboard (descending high scores, printable initials)
  CHECKSUM   every checksum region declared by the map already validates in
             the untouched NVRAM.  This is the strongest evidence available
             offline that a checksum region is described correctly: the game
             ROM wrote those bytes, we only predicted them.
  COVERAGE   which score/initials bytes sit inside a checksum region, i.e.
             what a writer has to recompute.  A field reported as unprotected
             is a claim that writing it is a plain byte write.
  WRITE      a round-trip: change initials and value, recompute checksums,
             read back, and confirm that the bytes that changed are exactly
             the mapped field plus its checksum -- nothing else in the file.
             Nothing is written to disk; this operates on a copy in memory.

Usage:
    python3 validate_maps.py                       # sample data in ScoresData
    python3 validate_maps.py --nvram-dir /path     # a fresh dump from the cab
    python3 validate_maps.py --rom lotr --verbose
"""
import argparse
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from nvmap import NvramMap  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
MAPS_DIR = os.path.normpath(os.path.join(HERE, "..", "nvram-maps"))
DEFAULT_NVRAM = os.path.normpath(os.path.join(HERE, "..", "..", "ScoresData", "nvram"))


def format_value(value):
    if isinstance(value, float):
        return "%.2f" % value
    if isinstance(value, int):
        return "{:,}".format(value)
    return str(value)


def check_read(table, verbose):
    problems = []
    records = list(table.records())
    if not records:
        problems.append("map defines no score records")

    high = [r for r in records if r[0] == "high_scores"]
    values = [r[4] for r in high if isinstance(r[4], (int, float))]
    if values != sorted(values, reverse=True):
        problems.append("high scores are not in descending order: %s" %
                        ", ".join(format_value(v) for v in values))

    for group, label, kind, initials, value, _field in records:
        if initials is not None and any(not (c.isprintable()) for c in initials):
            problems.append("%s: initials contain non-printable characters" % label)
        if verbose:
            print("    %-12s %-30s %-5s %18s" %
                  (group, label, initials, table.display(_field, value)))
    return problems


def check_checksums(table):
    bad = table.verify_checksums()
    total = sum(1 for _ in table.checksum_regions())
    return total, bad


def check_coverage(table):
    protected = table.protected_offsets()
    covered, uncovered = [], []
    for group in ("high_scores", "mode_champions"):
        for entry in table.map.get(group, []):
            for key, field in entry.items():
                if not isinstance(field, dict) or "encoding" not in field:
                    continue
                offsets = set(table.field_offsets(field))
                target = covered if offsets & protected else uncovered
                target.append("%s/%s" % (entry.get("label"), key))
    return covered, uncovered


def check_initials_fidelity(table):
    """Reading initials must not eat a character the machine actually stored.

    A space is one of the characters a player can pick, so ' NF' and 'NF ' are
    different names.  Only the format's own padding may be dropped, and putting
    the value back must reproduce the original bytes exactly.
    """
    problems = []
    padding = table.map.get("_pinballscores", {}).get("initials_padding")
    if padding not in ("none", "space", "null"):
        return ["_pinballscores.initials_padding is %r" % padding]

    for group in ("high_scores", "mode_champions"):
        for entry in table.map.get(group, []):
            field = entry.get("initials")
            if not field:
                continue
            offsets = table.field_offsets(field)
            before = bytes(table.data[o] for o in offsets)
            value = table.read_field(field)

            # Whether ' NF' is a name or a padded 'NF' is not decidable from
            # the bytes -- it follows from how the platform takes initials, and
            # the map declares that.  What is checkable is that the declaration
            # is honoured, so a stray strip() can't creep back in.
            if padding == "none":
                if len(value) != len(offsets):
                    problems.append(
                        "%s: %r is a fixed %d-character entry, read back %d "
                        "characters -- a stored character was dropped"
                        % (entry["label"], before, len(offsets), len(value)))
            elif padding == "null":
                if "\0" in value or "\xff" in value:
                    problems.append("%s: %r kept its terminator/filler"
                                    % (entry["label"], value))
            elif padding == "space":
                if value.endswith(" "):
                    problems.append("%s: %r kept trailing padding"
                                    % (entry["label"], value))
                if not before.decode("latin-1").startswith(value):
                    problems.append("%s: %r is not a prefix of %r"
                                    % (entry["label"], value, before))

            table.write_field(field, value)
            after = bytes(table.data[o] for o in offsets)
            if before != after:
                problems.append("%s: initials %r do not round-trip (%r -> %r)"
                                % (entry["label"], value, before, after))
    return problems


def check_value_round_trip(table):
    """Reading then writing a value back must not change it.

    Catches the asymmetry that a presentation-only attribute causes if it leaks
    into storage: `scale` used to be applied on read but not on write, so Lord
    of the Rings' ring timer read as 600.0 and wrote back as 6.
    """
    problems = []
    for group in ("high_scores", "mode_champions"):
        for entry in table.map.get(group, []):
            for key in ("score", "counter"):
                field = entry.get(key)
                if field is None:
                    continue
                before = table.read_field(field)
                if not isinstance(before, int):
                    problems.append("%s/%s reads as %s, not an integer -- the "
                                    "API stores int64" % (entry["label"], key,
                                                          type(before).__name__))
                    continue
                table.write_field(field, before)
                after = table.read_field(field)
                if before != after:
                    problems.append("%s/%s does not round-trip: %r -> %r"
                                    % (entry["label"], key, before, after))
    return problems


def check_categories(table, verbose):
    """Every record belongs to exactly one category, and ranked ones rank."""
    problems = []
    categories = table.categories()
    if not categories:
        return ["map declares no _pinballscores.categories"]

    labels = [e.get("label") for group in ("high_scores", "mode_champions")
              for e in table.map.get(group, [])]
    claimed = [label for c in categories for label in c["slots"]]
    if sorted(claimed) != sorted(labels):
        missing = set(labels) - set(claimed)
        extra = set(claimed) - set(labels)
        duplicated = {l for l in claimed if claimed.count(l) > 1}
        problems.append("category slots don't match the record list "
                        "(missing %s, unknown %s, duplicated %s)"
                        % (sorted(missing), sorted(extra), sorted(duplicated)))

    names = [c["name"] for c in categories]
    if len(set(names)) != len(names):
        problems.append("duplicate category names: %s" % names)
    keys = [c.get("key") for c in categories]
    if any(not k for k in keys):
        problems.append("category without a key: %s" % names)
    elif len(set(keys)) != len(keys):
        problems.append("duplicate category keys: %s" % keys)
    valid_types = {"score", "counter", "duration", "timestamp"}
    valid_units = {"ms", "cs", "ds", "s", "m", "h"}
    for c in categories:
        if c.get("value_type") not in valid_types:
            problems.append("category %r has value_type %r" % (c["name"], c.get("value_type")))
        if "value_unit" in c and c["value_unit"] not in valid_units:
            problems.append("category %r has value_unit %r" % (c["name"], c["value_unit"]))
        if c.get("value_type") != "duration" and "value_unit" in c:
            problems.append("category %r is not a duration but declares a unit" % c["name"])
    if names and names[0] is not None:
        problems.append("no unnamed category -- nothing maps to the main board")

    for category in categories:
        rows = table.read_category(category)
        if category.get("order") == "positional":
            continue
        # A ranked category read straight off the slots should already be in
        # descending order.  If it isn't, the slots have been grouped wrongly.
        raw = []
        for label in category["slots"]:
            entry = table.entry_by_label(label)
            _, field = table.value_field(entry)
            raw.append(table.read_field(field))
        if raw != sorted(raw, reverse=True):
            problems.append("ranked category %r is not in descending slot "
                            "order: %s" % (category["name"], raw))
        if verbose:
            print("    category %-24s %s" %
                  (repr(category["name"]),
                   ", ".join("%s/%s" % (i, format_value(v)) for i, v in rows)))
    return problems


def check_category_write(table_factory):
    """Round-trip an API-shaped payload through the category slots."""
    problems = []
    table = table_factory()
    category = next((c for c in table.categories() if c["name"] is None), None)
    if category is None:
        return ["no unnamed category to test an insert against"]

    # Replace the whole leaderboard, which is what a Competition Mode sync
    # does.  Deliberately supplied out of order, the way a query result might
    # arrive, so the slot assignment is doing real work.
    slots = category["slots"]
    payload = [("P%02d" % i, (i + 1) * 1_000_000) for i in range(len(slots))]
    scrambled = payload[::2] + payload[1::2]
    table.write_category(category, scrambled)

    want = sorted(payload, key=lambda row: row[1], reverse=True)
    got = table.read_category(category)
    if got != want:
        problems.append("category insert read back as %s, expected %s"
                        % (got, want))

    # The highest score must land in the machine's own top slot, not merely
    # somewhere in the category.
    top_entry = table.entry_by_label(slots[0])
    _, top_field = table.value_field(top_entry)
    if table.read_field(top_field) != want[0][1]:
        problems.append("top score did not land in %r" % slots[0])
    if table.verify_checksums():
        problems.append("checksums invalid after a category insert")
    return problems


def check_write(table_factory):
    """Round-trip a write and report anything unexpected."""
    problems = []
    original = table_factory()
    entry = (original.map.get("high_scores") or [None])[0]
    if entry is None:
        return ["no high_scores entry to test a write against"]
    value_key = next((k for k in ("score", "counter") if k in entry), None)
    if value_key is None:
        return ["first high_scores entry has no score/counter field"]

    modified = table_factory()
    before = bytes(modified.data)

    new_initials = "ZZZ"
    old_value = modified.read_field(entry[value_key])
    field = entry[value_key]
    if field["encoding"] == "bcd":
        new_value = 12345600
    else:
        new_value = 123456
    if "scale" in field:
        new_value = new_value * field["scale"]

    if "initials" in entry:
        raw_initials = entry["initials"]
        fit = len(modified.field_offsets(raw_initials))
        modified.write_field(raw_initials, new_initials[:fit])
    modified.write_field(field, int(new_value / field.get("scale", 1)))

    read_initials = modified.read_field(entry["initials"]) if "initials" in entry else None
    read_value = modified.read_field(field)
    if "initials" in entry and read_initials != new_initials:
        problems.append("initials read back as %r, expected %r" %
                        (read_initials, new_initials))
    if read_value != new_value:
        problems.append("value read back as %r, expected %r" % (read_value, new_value))

    bad = modified.verify_checksums()
    if bad:
        problems.append("%d checksum region(s) invalid after write" % len(bad))

    changed = {i for i in range(len(before)) if before[i] != modified.data[i]}
    expected = set(modified.field_offsets(field))
    if "initials" in entry:
        expected |= set(modified.field_offsets(entry["initials"]))
    for _, data_offsets, checksum_offsets, _ in modified.checksum_regions():
        if expected & set(data_offsets):
            expected |= set(checksum_offsets)
    stray = changed - expected
    if stray:
        problems.append("write touched %d byte(s) outside the field and its "
                        "checksum: %s" % (len(stray),
                                          ", ".join("0x%04X" % o for o in sorted(stray)[:8])))

    # Nothing should have been written to disk, and the original object must
    # still hold the untouched value.
    if original.read_field(field) != old_value:
        problems.append("original image was mutated by the write test")
    return problems


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--nvram-dir", default=DEFAULT_NVRAM)
    ap.add_argument("--maps-dir", default=MAPS_DIR)
    ap.add_argument("--rom", action="append",
                    help="only validate these ROMs (repeatable)")
    ap.add_argument("--verbose", action="store_true", help="print every record")
    args = ap.parse_args()

    roms = sorted(f[:-len(".map.json")] for f in os.listdir(args.maps_dir)
                  if f.endswith(".map.json"))
    if args.rom:
        roms = [r for r in roms if r in args.rom]

    failures = 0
    skipped = []
    for rom in roms:
        map_path = os.path.join(args.maps_dir, "%s.map.json" % rom)
        nvram_path = os.path.join(args.nvram_dir, "%s.nv" % rom)
        if not os.path.exists(nvram_path):
            skipped.append(rom)
            continue

        print("== %s" % rom)
        table = NvramMap.load(map_path, nvram_path)
        problems = check_read(table, args.verbose)

        total, bad = check_checksums(table)
        if total == 0:
            print("    checksums: none declared")
        elif bad:
            problems.append("%d/%d checksum region(s) do not validate: %s" %
                            (len(bad), total,
                             ", ".join(str(b[0]) for b in bad[:4])))
        else:
            print("    checksums: %d/%d valid" % (total, total))

        covered, uncovered = check_coverage(table)
        print("    score fields protected by a checksum: %d, unprotected: %d"
              % (len(covered), len(uncovered)))
        if args.verbose and uncovered:
            print("      unprotected: %s" % ", ".join(uncovered))

        problems += check_initials_fidelity(NvramMap.load(map_path, nvram_path))
        problems += check_value_round_trip(NvramMap.load(map_path, nvram_path))
        problems += check_categories(table, args.verbose)
        problems += check_write(lambda: NvramMap.load(map_path, nvram_path))
        problems += check_category_write(lambda: NvramMap.load(map_path, nvram_path))

        if problems:
            failures += 1
            for problem in problems:
                print("    FAIL: %s" % problem)
        else:
            print("    OK")

    if skipped:
        print("\nno NVRAM sample available, not validated: %s" % ", ".join(skipped))
    print("\n%d/%d table(s) validated clean" % (len(roms) - len(skipped) - failures,
                                                len(roms) - len(skipped)))
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
