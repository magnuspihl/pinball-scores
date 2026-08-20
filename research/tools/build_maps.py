#!/usr/bin/env python3
"""
Regenerate research/nvram-maps/ -- one map per NVRAM table on the cabinet.

Every map is emitted in tomlogic/pinball-memory-maps file format v0.8 so the
same file drives both extraction and insertion, and so anything we fix here can
be contributed back upstream unchanged.

Three kinds of table:

1. Tables upstream already maps correctly (all six Williams WPC titles, both
   Data East titles, both Whitestar titles).  Copied verbatim from the pinned
   upstream commit, with a `_pinballscores` block recording provenance and what
   we verified.  Copying rather than referencing keeps this repo self-contained
   and offline-usable; `--check` re-diffs against upstream so drift is visible.

2. Stern SAM tables upstream maps but without the per-record `checksum16`
   (avs_170, smanve_101).  Upstream fields plus the checksum section, plus the
   smanve_101 "Best Combo Champion" width fix.

3. Stern SAM tables upstream does not map at all (potc_600as, twd_156h,
   xmn_151h).  Built from SAM_TABLES below, which was derived by running
   tools/sam_record_scan.py over the sample NVRAM.

Usage:
    python3 build_maps.py --upstream /path/to/pinball-memory-maps
    python3 build_maps.py --upstream /path/to/pinball-memory-maps --check
"""
import argparse
import hashlib
import json
import os
import re
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
OUT_DIR = os.path.normpath(os.path.join(HERE, "..", "nvram-maps"))

# Stern SAM record layout: initials at +0x00, score at +0x18, checksum16 at
# +0x1c covering the 28 bytes before it, next record 0x20 bytes later.
SAM_STRIDE = 0x20
SAM_SCORE_OFFSET = 0x18
SAM_CHECKSUM_LENGTH = 30  # 28 summed bytes + the 2 checksum bytes

UPSTREAM_COMMIT = "aab556e3059f4463740293e01531b2f9d817ed89"

# Tables upstream already covers.  `source` is relative to the upstream repo.
UPSTREAM_TABLES = {
    "ij_l7": dict(
        source="maps/williams/wpc/ij_l7.map.json",
        title="Indiana Jones: The Pinball Adventure (L-7)",
        status="upstream map, checksums verified against two independent dumps",
    ),
    "mm_109c": dict(
        source="maps/williams/wpc/mm_109.map.json",
        title="Medieval Madness (1.09C)",
        status="upstream map, checksums verified against two independent dumps",
    ),
    "sttng_l7": dict(
        source="maps/williams/wpc/sttng_l7.map.json",
        title="Star Trek: The Next Generation (L-7)",
        status="upstream map, checksums verified against two independent dumps",
    ),
    "t2_l8": dict(
        source="maps/williams/wpc/t2_l8.map.json",
        title="Terminator 2: Judgment Day (L-8)",
        status="upstream map, checksums verified against two independent dumps",
    ),
    "taf_l7": dict(
        source="maps/williams/wpc/taf_l7.map.json",
        title="The Addams Family (L-7)",
        status="upstream map, checksums verified against two independent dumps",
    ),
    "totan_14": dict(
        source="maps/williams/wpc/totan_14.map.json",
        title="Tales of the Arabian Nights (1.4)",
        status="upstream map, checksums verified against two independent dumps",
    ),
    "btmn_106": dict(
        source="maps/dataeast/version3/btmn_106.map.json",
        title="Batman (Data East, 1.06)",
        status="upstream map, no score checksum found (see TABLE-MAPPING.md)",
    ),
    "stwr_107": dict(
        source="maps/dataeast/version3/stwr_103.map.json",
        title="Star Wars (Data East, 1.07)",
        status="upstream map, no score checksum found (see TABLE-MAPPING.md)",
    ),
    "lotr": dict(
        source="maps/stern/whitestar/lotr.map.json",
        title="The Lord of the Rings (Stern, Whitestar)",
        status="upstream map, no score checksum found (see TABLE-MAPPING.md)",
    ),
    "simpprty": dict(
        source="maps/stern/whitestar/simpprty.map.json",
        title="The Simpsons Pinball Party (Stern, Whitestar)",
        status="upstream map, no score checksum found (see TABLE-MAPPING.md)",
    ),
}

# Stern SAM tables, described as an ordered list of records starting at `base`.
# Each entry is (group, label, value_key).  `value_key` is "score" or "counter"
# -- upstream's own way of saying "this is a ranked score" vs "this is a count
# of something", which the extractor uses to pick the API's value_type.
_ROM_LABEL_NOTE = [
    "",
    "Champion labels came out of the game ROM itself (a legally-owned copy",
    "supplied for this purpose only, deliberately kept out of git). Stern SAM",
    "ROMs carry two parallel tables: the slot labels as displayed, and the",
    "default initials each slot is seeded with. Both are in NVRAM record order,",
    "so the seeded initials still sitting in unbeaten slots confirm which label",
    "belongs to which record -- the ordering is checked, not assumed.",
    "The ranked slots keep the Grand Champion / First..Fourth Place naming the",
    "other maps here use rather than the ROM's HIGH SCORE #n, since rank is",
    "derived from the value rather than stored. Champion labels are the ROM's",
    "own wording.",
]

_SAM_HIGH_SCORES = [
    ("high_scores", "Grand Champion", "score"),
    ("high_scores", "First Place", "score"),
    ("high_scores", "Second Place", "score"),
    ("high_scores", "Third Place", "score"),
    ("high_scores", "Fourth Place", "score"),
]

SAM_TABLES = {
    "avs_170": dict(
        title="The Avengers (1.70)",
        roms=["avs_170", "avs_170c"],
        base=0x02102F80,
        upstream_source="maps/stern/sam/avs_170.map.json",
        status="upstream fields confirmed by checksum scan; checksum16 added",
        records=list(_SAM_HIGH_SCORES),
    ),
    "smanve_101": dict(
        title="Spider-Man Vault Edition (1.01)",
        roms=["smanve_100", "smanve_101", "smanve_101c"],
        base=0x02102B80,
        upstream_source="maps/stern/sam/smanve_101.map.json",
        status="verified against real hardware capture; checksum16 added",
        # "Best Combo Champion" is the one record on the cabinet that doesn't
        # hold a plain 4-byte value: its 4 bytes at +0x18 read 01 00 05 00,
        # i.e. two 16-bit fields, and it is the second one that counts combos.
        # Two dumps a year apart show it going 5 -> 7 as a real player took the
        # slot, while a 4-byte read gives 327,681 -> 458,753.  Upstream's
        # 1-byte field at +0x1a is right; a previous session "corrected" it to
        # 4 bytes at +0x18 on the mistaken assumption that 327,681 was real.
        overrides={
            "Best Combo Champion": ("counter", {
                "start": "0x02102C5A",
                "encoding": "int",
                "length": 1,
                "_note": "combo count; the u16 at +0x18 is a separate field, "
                         "always 1 in every dump seen so far",
            }),
        },
        records=_SAM_HIGH_SCORES + [
            ("mode_champions", "Combo Champion", "counter"),
            ("mode_champions", "Best Combo Champion", "counter"),
            ("mode_champions", "Spider Champion", "counter"),
            ("mode_champions", "Spider Sense Champion", "score"),
            ("mode_champions", "Battle Royale Champion", "score"),
            ("mode_champions", "Super Hero Champion", "score"),
            ("mode_champions", "Best Bonus Champion", "score"),
        ],
    ),
    "potc_600as": dict(
        title="Pirates of the Caribbean (Stern, 6.00)",
        roms=["potc_600as"],
        base=0x02102948,
        upstream_source=None,
        status="derived from checksum scan; labels read out of the game ROM",
        extra_notes=_ROM_LABEL_NOTE + [
            "For this ROM the label table reads GRAND CHAMPION, HIGH SCORE #1..#4,",
            "PIRATE KING, GAUNTLET CHAMP 1..3, DAVY JONES CHAMPION -- ten labels for",
            "the ten records found by the scan. The parallel default-initials table",
            "reads NORDMAN, OCONNOR, THEIL, GALVEZ, ROPP, KEEFER, BLACKWELL, STERN,",
            "SCHOENBERG, XAQERY, and the champion slots in NVRAM still hold KEF, J B,",
            "G S, M S, XAQ in that order.",
            "Gauntlet Champ 1/2/3 look like one three-deep leaderboard (15/10/5 at",
            "factory defaults) rather than three unrelated categories -- worth",
            "collapsing to a single category with rank derived, the way the ranked",
            "high scores are handled.",
        ],
        records=_SAM_HIGH_SCORES + [
            ("mode_champions", "Pirate King", "counter"),
            ("mode_champions", "Gauntlet Champ 1", "counter"),
            ("mode_champions", "Gauntlet Champ 2", "counter"),
            ("mode_champions", "Gauntlet Champ 3", "counter"),
            ("mode_champions", "Davy Jones Champion", "counter"),
        ],
    ),
    "twd_156h": dict(
        title="The Walking Dead (1.56 Home)",
        roms=["twd_156h"],
        upstream_source=None,
        base=0x021036D8,
        status="derived from checksum scan; labels follow upstream twd_156",
        records=_SAM_HIGH_SCORES + [
            ("mode_champions", "Walkers Killed Champion", "counter"),
            ("mode_champions", "Combo Champion", "score"),
            ("mode_champions", "Bicycle Girl Champion", "score"),
            ("mode_champions", "Barn Champion", "score"),
            ("mode_champions", "CDC Champion", "score"),
            ("mode_champions", "Riot Champion", "score"),
            ("mode_champions", "Tunnel Champion", "score"),
            ("mode_champions", "Arena Champion", "score"),
            ("mode_champions", "Terminus Champion", "score"),
            ("mode_champions", "Blood Bath Champion", "score"),
            ("mode_champions", "Crossbow Champion", "score"),
            ("mode_champions", "X Champion", "score"),
            ("mode_champions", "Horde Champion", "score"),
            ("mode_champions", "Last Man Standing Champion", "score"),
        ],
    ),
    "xmn_151h": dict(
        title="X-Men LE (1.51 Home)",
        roms=["xmn_151h"],
        upstream_source=None,
        base=0x02103000,
        status="derived from checksum scan; labels read out of the game ROM",
        extra_notes=_ROM_LABEL_NOTE + [
            "For this ROM the label table reads GRAND CHAMPION, HIGH SCORE #1..#4,",
            "COMBO CHAMPION -- six labels for the six records found by the scan. The",
            "parallel default-initials table reads STERN, ROPP, ROTHARMEL, THORNE,",
            "POWERS, YANCY, and this cabinet's X-Men has never been beaten, so all six",
            "slots still hold exactly those initials in exactly that order (G S, L R,",
            "J R, D T, P P, YAN). That is a clean 1:1 confirmation of the ordering.",
        ],
        records=_SAM_HIGH_SCORES + [
            ("mode_champions", "Combo Champion", "counter"),
        ],
    ),
}

SAM_NOTES = [
    "Stern SAM platform. ARM AT91 CPU, 128KB NVRAM mapped at 0x02100000;",
    "file offset = CPU address - 0x02100000.",
    "Each score record is a 32-byte (0x20) struct:",
    "  +0x00 char initials[] (ASCII, NUL-terminated, 0xFF-padded, 11 bytes mapped)",
    "  +0x18 uint32 score/counter, little-endian",
    "  +0x1c uint16 checksum, little-endian = 0xFFFF - sum(bytes +0x00..+0x1b)",
    "  +0x1e 0xFFFF filler",
    "The checksum is the ROM's own boot-time record validation: a record written",
    "without recomputing it is discarded and replaced by the ROM's compiled-in",
    "factory default for that slot. It was recovered by disassembling the",
    "smanve_101 game ROM (research/rom-analysis/NOTES.md) and is expressed here",
    "with the same `checksum16` convention the upstream Star Trek SAM maps use.",
    "Record addresses were confirmed by scanning the sample NVRAM for offsets",
    "where the checksum holds (research/tools/sam_record_scan.py). Champion",
    "slots that have never been earned are 0xFF-filled and do not appear -- re-run",
    "the scan against a fresh dump and add any slots that have since appeared.",
]


# Slot labels that look like "<base> #3" / "<base> 3" belong to one ranked
# category, not three.  Categories listed here are ordered lists rather than
# leaderboards, so their slot order is meaningful and must not be re-sorted.
POSITIONAL_CATEGORIES = {
    "mm_109c": {"King of the Realm"},
}

RANK_SUFFIX = re.compile(r"^(.*?)[ ]*#?(\d+)$")

# How a platform pads the initials field.  This decides what may be stripped
# when reading, and nothing else may be: a space is a character players pick.
#   none   the field is exactly the entry (WPC and Data East take 3 characters,
#          always, so 'CG ' is a name ending in a space)
#   space  a shorter name inside a wider field (Whitestar's 10-byte field holds
#          names of any length up to 10, so trailing spaces are padding)
#   null   NUL-terminated with 0xFF filler (Stern SAM)
INITIALS_PADDING = {
    "williams-wpc-8K": "none",
    "williams-wpc-12K": "none",
    "dataeast": "none",
    "whitestar": "space",
    "stern-sam": "null",
}


# The API's value_type enum, and (for durations) the unit of the raw NVRAM
# integer.  Sending the raw integer with its own unit means nothing is scaled
# or rounded on the way in -- Lord of the Rings' ring timer counts hundredths
# of a second, so it goes as-is with value_unit "cs".
DURATION_UNITS = {1: "s", 0.1: "ds", 0.01: "cs", 0.001: "ms"}


def value_typing(entry):
    """Return (value_type, value_unit) for a record, per the score API's enum."""
    if "timestamp" in entry and "counter" not in entry and "score" not in entry:
        return "timestamp", None
    for key in ("score", "counter"):
        field = entry.get(key)
        if field is None:
            continue
        if field.get("encoding") == "wpc_rtc":
            return "timestamp", None
        if field.get("units") == "seconds":
            return "duration", DURATION_UNITS.get(field.get("scale", 1))
        return ("counter" if key == "counter" else "score"), None
    return "score", None


def category_key(name):
    """Stable, immutable identifier for a category.

    The label is ROM display text and may well get prettified later ('CB' ->
    'Cherry Bomb'); the key must not move when it does, because it is what
    dedup and the admin endpoints address.
    """
    if name is None:
        return "main"
    return re.sub(r"[^a-z0-9]+", "_", name.lower()).strip("_")


def derive_categories(game_map, positional=()):
    """Group a map's records into the categories the score API stores.

    The main leaderboard is one category with no name; every other group of
    slots that shares a base label is one named category; anything left over is
    a category of its own.  Slots are listed best-first, which is the order an
    insert has to fill them in.
    """
    categories = []

    def suffix_for(labels):
        for group in ("high_scores", "mode_champions"):
            for entry in game_map.get(group, []):
                if entry.get("label") not in labels:
                    continue
                for value in entry.values():
                    if isinstance(value, dict) and value.get("suffix"):
                        return value["suffix"].strip()
        return None

    def make(name, slots, order):
        category = {"key": category_key(name), "name": name,
                    "order": order, "slots": slots}
        typing = {value_typing(e) for e in
                  (entry for group in ("high_scores", "mode_champions")
                   for entry in game_map.get(group, [])
                   if entry.get("label") in set(slots))}
        if len(typing) != 1:
            raise ValueError("category %r mixes value types: %s" % (name, typing))
        value_type, value_unit = typing.pop()
        category["value_type"] = value_type
        if value_unit:
            category["value_unit"] = value_unit
        suffix = suffix_for(set(slots))
        if suffix:
            category["display_suffix"] = suffix
        return category

    main = [e["label"] for e in game_map.get("high_scores", [])]
    if main:
        categories.append(make(None, main, "ranked"))

    champions = [e["label"] for e in game_map.get("mode_champions", [])]
    bases = {}
    for label in champions:
        match = RANK_SUFFIX.match(label)
        bases.setdefault(match.group(1) if match else label, []).append(label)

    for label in champions:
        match = RANK_SUFFIX.match(label)
        base = match.group(1) if match else label
        group = bases[base]
        if group[0] != label:
            continue  # already emitted with the first slot of its group
        name = base if len(group) > 1 else label
        categories.append(
            make(name, group, "positional" if name in positional else "ranked"))
    return categories


def stamp(game_map):
    """Add the fields the CLI needs at submission time.

    `initials_padding` says what may be stripped from an initials field and
    nothing more.  `map_version` is a hash of the map's actual content, so a
    submitted score can be traced back to the exact map that read it -- which
    is the only way to find and retract rows produced by a mapping bug, and
    this repo has already shipped two.
    """
    block = game_map["_pinballscores"]
    platform = game_map.get("_metadata", {}).get("platform")
    block["initials_padding"] = INITIALS_PADDING.get(platform, "none")
    body = {k: v for k, v in game_map.items() if k != "_pinballscores"}
    block["map_version"] = hashlib.sha256(
        json.dumps(body, sort_keys=True).encode()).hexdigest()[:12]
    return game_map


def load_json(path):
    with open(path) as f:
        return json.load(f)


def dump_json(obj):
    return json.dumps(obj, indent=2) + "\n"


def provenance(rom, title, status, source, extra=None):
    block = {
        "cabinet_rom": rom,
        "title": title,
        "status": status,
        "upstream": {
            "repo": "https://github.com/tomlogic/pinball-memory-maps",
            "commit": UPSTREAM_COMMIT,
            "path": source,
        } if source else {
            "repo": "https://github.com/tomlogic/pinball-memory-maps",
            "commit": UPSTREAM_COMMIT,
            "path": None,
            "note": "no upstream map exists for this ROM",
        },
    }
    if extra:
        block.update(extra)
    return block


def build_upstream_table(rom, spec, upstream_root):
    src = os.path.join(upstream_root, spec["source"])
    game_map = load_json(src)
    ordered = {"_fileformat": game_map.get("_fileformat", 0.8)}
    if "_notes" in game_map:
        ordered["_notes"] = game_map["_notes"]
    ordered["_pinballscores"] = provenance(rom, spec["title"], spec["status"],
                                           spec["source"],
                                           {"modified_from_upstream": False})
    for key, value in game_map.items():
        if key not in ordered:
            ordered[key] = value
    ordered["_pinballscores"]["categories"] = derive_categories(
        ordered, POSITIONAL_CATEGORIES.get(rom, ()))
    return stamp(ordered)


def sam_record_entry(label, value_key, address):
    return {
        "label": label,
        "initials": {
            "start": "0x%08X" % address,
            "encoding": "ch",
            "null": "terminate",
            "length": 11,
        },
        value_key: {
            "start": "0x%08X" % (address + SAM_SCORE_OFFSET),
            "encoding": "int",
            "length": 4,
        },
    }


def build_sam_table(rom, spec):
    high_scores, champions, checksums = [], [], []
    overrides = spec.get("overrides", {})
    for index, (group, label, value_key) in enumerate(spec["records"]):
        address = spec["base"] + index * SAM_STRIDE
        entry = sam_record_entry(label, value_key, address)
        if label in overrides:
            # Same record and same checksum, but the value isn't where the
            # standard layout puts it.
            entry.pop(value_key)
            override_key, field = overrides[label]
            entry[override_key] = field
        (high_scores if group == "high_scores" else champions).append(entry)
        checksums.append({
            "start": "0x%08X" % address,
            "length": SAM_CHECKSUM_LENGTH,
            "label": label,
        })

    game_map = {
        "_fileformat": 0.8,
        "_notes": ([spec["title"] + " -- Stern SAM."] + SAM_NOTES
                   + spec.get("extra_notes", [])),
        "_pinballscores": provenance(
            rom, spec["title"], spec["status"], spec.get("upstream_source"),
            {"modified_from_upstream": True,
             "record_base": "0x%08X" % spec["base"],
             "record_count": len(spec["records"])},
        ),
        "_metadata": {
            "version": 1,
            "license": "GNU Lesser General Public License v3.0",
            "platform": "stern-sam",
            "roms": spec["roms"],
        },
        "high_scores": high_scores,
    }
    if champions:
        game_map["mode_champions"] = champions
    game_map["checksum16"] = checksums
    game_map["_pinballscores"]["categories"] = derive_categories(
        game_map, POSITIONAL_CATEGORIES.get(rom, ()))
    return stamp(game_map)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--upstream", required=True,
                    help="path to a pinball-memory-maps checkout")
    ap.add_argument("--check", action="store_true",
                    help="report differences instead of writing files")
    args = ap.parse_args()

    head = subprocess.run(["git", "-C", args.upstream, "rev-parse", "HEAD"],
                          capture_output=True, text=True).stdout.strip()
    if head and head != UPSTREAM_COMMIT:
        print("note: upstream checkout is at %s, maps were built from %s"
              % (head[:12], UPSTREAM_COMMIT[:12]), file=sys.stderr)

    os.makedirs(OUT_DIR, exist_ok=True)
    built = {}
    for rom, spec in UPSTREAM_TABLES.items():
        built[rom] = build_upstream_table(rom, spec, args.upstream)
    for rom, spec in SAM_TABLES.items():
        built[rom] = build_sam_table(rom, spec)

    differences = 0
    for rom in sorted(built):
        path = os.path.join(OUT_DIR, "%s.map.json" % rom)
        text = dump_json(built[rom])
        current = open(path).read() if os.path.exists(path) else None
        if args.check:
            if current != text:
                differences += 1
                print("DIFFERS: %s" % os.path.relpath(path))
            continue
        with open(path, "w") as f:
            f.write(text)
        print("%s %s" % ("updated" if current != text else "unchanged",
                         os.path.relpath(path)))

    if args.check:
        print("%d map(s) differ from what this script would generate" % differences)
        return 1 if differences else 0
    return 0


if __name__ == "__main__":
    sys.exit(main())
