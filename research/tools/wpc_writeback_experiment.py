#!/usr/bin/env python3
"""
One-round, multi-table experiment to isolate why the plain ---/1 demo-score
reset reverts on 6/6 Williams WPC tables and on stwr_107 (Data East), while
it stuck on every Stern SAM, Whitestar table and btmn_106 (Data East).

The ---/1 reset changed two things from a normal leaderboard at once: every
record in a checksum-protected block became identical (a tie a real board
never has), and every set of initials used a dash, a character that may not
be in the machine's own selectable alphabet. There's also a third, unrelated
possibility Magnus raised: a minimum-plausible-score floor, similar to what
was ruled out on Stern SAM's smanve_101 early in this investigation (see
FINDINGS.md) but never tested on WPC/Data East specifically.

Rather than one variable per cabinet visit, this generates one differently-
treated file per failing table so a single test round separates all three:

  t2_l8      distinct values (50/40/30/20/10), dash initials   -- tie alone
  taf_l7     distinct values (50/40/30/20/10), letter initials -- tie+charset
  totan_14   tied value 1 (as before), letter initials         -- charset alone
  ij_l7      factory initials/values, score += 100             -- minimum-value
  mm_109c    combined best-guess fix, generalised to its 11
             champion/King-of-the-Realm records
  sttng_l7   combined best-guess fix, generalised to its two
             4-slot ranked champion groups
  stwr_107   combined best-guess fix (Data East, first real
             variation ever written to this ROM's score block)

"Combined best-guess fix" = distinct descending values (Grand Champion
highest, then the ranked block, then any multi-slot champion group scaled
down separately) plus plain-letter initials -- the shape a real, if
suspiciously tidy, leaderboard would have.

Usage:
    python3 research/tools/wpc_writeback_experiment.py [--out-dir DIR]
"""
import argparse
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, HERE)
from nvmap import NvramMap, PINMAME_TRAILER  # noqa: E402

MAPS_DIR = os.path.join(REPO_ROOT, "research", "nvram-maps")
NVRAM_DIR = os.path.join(REPO_ROOT, "ScoresData", "nvram")

LETTERS = "ZZZ"
DASH = "---"

# rom -> {"initials": {label: str} or None to leave untouched,
#         "values": {label: int} or None,
#         "increment": int (add to the factory value instead of a fixed value)}
PLANS = {
    "t2_l8": {
        "initials": {l: DASH for l in
                     ["Grand Champion", "First Place", "Second Place",
                      "Third Place", "Fourth Place"]},
        "values": {"Grand Champion": 50, "First Place": 40,
                   "Second Place": 30, "Third Place": 20, "Fourth Place": 10},
    },
    "taf_l7": {
        "initials": {l: LETTERS for l in
                     ["Grand Champion", "First Place", "Second Place",
                      "Third Place", "Fourth Place"]},
        "values": {"Grand Champion": 50, "First Place": 40,
                   "Second Place": 30, "Third Place": 20, "Fourth Place": 10},
    },
    "totan_14": {
        "initials": {l: LETTERS for l in
                     ["Grand Champion", "1st", "2nd", "3rd", "4th"]},
        "values": {"Grand Champion": 1, "1st": 1, "2nd": 1, "3rd": 1, "4th": 1},
    },
    "ij_l7": {
        "initials": None,  # leave factory initials untouched
        "increment": 100,  # add to whatever the factory value currently is
    },
    "mm_109c": {
        "initials": {l: LETTERS for l in [
            "Grand Champion", "First Place", "Second Place", "Third Place",
            "Fourth Place", "Castle Champion", "Joust Champion",
            "Catapult Champion", "Peasant Champion", "Damsel Champion",
            "Troll Champion", "Madness Champion",
            "King of the Realm #1", "King of the Realm #2",
            "King of the Realm #3", "King of the Realm #4"]},
        "values": {
            "Grand Champion": 50, "First Place": 40, "Second Place": 30,
            "Third Place": 20, "Fourth Place": 10,
            "Castle Champion": 1, "Joust Champion": 1, "Catapult Champion": 1,
            "Peasant Champion": 1, "Damsel Champion": 1, "Troll Champion": 1,
            "Madness Champion": 1,
            "King of the Realm #1": 4, "King of the Realm #2": 3,
            "King of the Realm #3": 2, "King of the Realm #4": 1,
        },
    },
    "sttng_l7": {
        "initials": {l: LETTERS for l in [
            "Grand Champion", "First Place", "Second Place", "Third Place",
            "Fourth Place",
            "Officer's Club #1", "Officer's Club #2", "Officer's Club #3",
            "Officer's Club #4",
            "Q Continuum #1", "Q Continuum #2", "Q Continuum #3",
            "Q Continuum #4"]},
        "values": {
            "Grand Champion": 50, "First Place": 40, "Second Place": 30,
            "Third Place": 20, "Fourth Place": 10,
            "Officer's Club #1": 4, "Officer's Club #2": 3,
            "Officer's Club #3": 2, "Officer's Club #4": 1,
            "Q Continuum #1": 4, "Q Continuum #2": 3,
            "Q Continuum #3": 2, "Q Continuum #4": 1,
        },
    },
    "stwr_107": {
        "initials": {l: LETTERS for l in
                     ["First", "Second", "Third", "Fourth", "Fifth", "Sixth"]},
        "values": {"First": 60, "Second": 50, "Third": 40,
                   "Fourth": 30, "Fifth": 20, "Sixth": 10},
    },
}


def value_field(entry):
    for key in ("score", "counter"):
        if key in entry:
            return key, entry[key]
    raise ValueError("%r has no score or counter field" % entry.get("label"))


def entries_by_label(table):
    out = {}
    for group in ("high_scores", "mode_champions"):
        for entry in table.map.get(group, []):
            out[entry["label"]] = entry
    return out


def apply_plan(rom, plan, out_path):
    map_path = os.path.join(MAPS_DIR, "%s.map.json" % rom)
    nvram_path = os.path.join(NVRAM_DIR, "%s.nv" % rom)
    table = NvramMap.load(map_path, nvram_path)
    with open(nvram_path, "rb") as f:
        raw = bytearray(f.read())
    trailer = raw[len(raw) - PINMAME_TRAILER:]

    by_label = entries_by_label(table)
    report = []
    for label, entry in by_label.items():
        _, field = value_field(entry)
        old_initials = table.read_field(entry["initials"]) if "initials" in entry else None
        old_value = table.read_field(field)

        if "increment" in plan:
            new_value = old_value + plan["increment"]
        else:
            new_value = plan["values"][label]
        table.write_field(field, new_value)

        new_initials = old_initials
        if plan["initials"] is not None and "initials" in entry:
            new_initials = plan["initials"][label]
            table.write_field(entry["initials"], new_initials)

        report.append((label, old_initials, old_value, new_initials, new_value))

    bad = table.verify_checksums()
    if bad:
        raise SystemExit("%s: %d checksum region(s) invalid after edit: %s"
                         % (rom, len(bad), bad))

    with open(out_path, "wb") as f:
        f.write(table.data + trailer)

    check = NvramMap.load(map_path, out_path)
    if check.verify_checksums():
        raise SystemExit("%s: checksums invalid on re-read" % out_path)
    by_label_check = entries_by_label(check)
    for label, old_initials, old_value, new_initials, new_value in report:
        entry = by_label_check[label]
        _, field = value_field(entry)
        got_value = check.read_field(field)
        if got_value != new_value:
            raise SystemExit("%s: %r value read back as %r, expected %r"
                             % (out_path, label, got_value, new_value))
        if new_initials is not None:
            got_initials = check.read_field(entry["initials"])
            if got_initials != new_initials:
                raise SystemExit("%s: %r initials read back as %r, expected %r"
                                 % (out_path, label, got_initials, new_initials))
    return report


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out-dir", default=os.path.join(REPO_ROOT, "research", "wpc-writeback-test"))
    args = ap.parse_args()
    os.makedirs(args.out_dir, exist_ok=True)

    manifest = []
    for rom, plan in PLANS.items():
        out_path = os.path.join(args.out_dir, "%s.nv" % rom)
        report = apply_plan(rom, plan, out_path)
        print("== %s -> %s" % (rom, out_path))
        for label, oi, ov, ni, nv in report:
            print("   %-24s %r %s -> %r %s" %
                  (label, oi, "{:,}".format(ov), ni, "{:,}".format(nv)))
        manifest.append((rom, plan))

    print("\n%d table(s) written to %s" % (len(PLANS), args.out_dir))


if __name__ == "__main__":
    main()
