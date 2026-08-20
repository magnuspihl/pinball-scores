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
import json
import os
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
        status="derived from checksum scan; champion labels UNCONFIRMED",
        records=_SAM_HIGH_SCORES + [
            ("mode_champions", "Champion 1 (unnamed)", "counter"),
            ("mode_champions", "Champion 2 (unnamed)", "counter"),
            ("mode_champions", "Champion 3 (unnamed)", "counter"),
            ("mode_champions", "Champion 4 (unnamed)", "counter"),
            ("mode_champions", "Champion 5 (unnamed)", "counter"),
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
        status="derived from checksum scan; champion label UNCONFIRMED",
        records=_SAM_HIGH_SCORES + [
            ("mode_champions", "Champion 1 (unnamed)", "counter"),
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
    return ordered


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
    for index, (group, label, value_key) in enumerate(spec["records"]):
        address = spec["base"] + index * SAM_STRIDE
        entry = sam_record_entry(label, value_key, address)
        (high_scores if group == "high_scores" else champions).append(entry)
        checksums.append({
            "start": "0x%08X" % address,
            "length": SAM_CHECKSUM_LENGTH,
            "label": label,
        })

    game_map = {
        "_fileformat": 0.8,
        "_notes": [spec["title"] + " -- Stern SAM."] + SAM_NOTES,
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
    return game_map


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
