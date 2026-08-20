#!/usr/bin/env python3
"""
Build (and validate) the maps for the cabinet's Visual Pinball X tables.

VPX-native tables don't emulate a ROM, so there is no NVRAM and no memory map.
They persist their own state through VPX's `VPReg.stg`, an OLE Compound File
where every table gets a storage named after the table and every setting is a
stream holding a UTF-16LE string.  Scores are decimal strings, initials are
plain strings; there is no checksum and no factory-default fallback.

So the "map" for an STG table is a list of stream names, and it exists for the
same reason the NVRAM maps do: to give the extractor one place to learn which
streams are scores, which are initials, and how they pair up -- because the
naming convention is per-table-author, not a standard.  `HighScore3` pairs with
`HighScore3Name` on all three of this cabinet's tables, but the champion fields
do not follow a rank pattern at all (`HighScoreXandar`/`HighScoreXandarName`).

Field names are taken verbatim from the table script rather than prettified:
"Xandar" is what Guardians of the Galaxy calls that champion internally, and an
invented display name would silently become a different category in the score
database if anyone ever corrected it.

Requires olefile (read-only is enough): pip install olefile

Usage:
    python3 build_stg_maps.py --stg ScoresData/User/VPReg.stg
    python3 build_stg_maps.py --stg <file> --check
    python3 build_stg_maps.py --stg <file> --list
"""
import argparse
import hashlib
import json
import os
import re
import sys

try:
    import olefile
except ImportError:
    sys.exit("This script requires the 'olefile' package: pip install olefile")

HERE = os.path.dirname(os.path.abspath(__file__))
OUT_DIR = os.path.normpath(os.path.join(HERE, "..", "stg-maps"))

# The three VPX-native tables on the cabinet, and the title to record for each.
CABINET_TABLES = {
    "jpsdeadpool": "Deadpool (VPX)",
    "gameofthrones": "Game of Thrones (VPX)",
    "gotg_2020": "Guardians of the Galaxy (VPX)",
}

RANKED = re.compile(r"^HighScore(\d+)$")
CHAMPION = re.compile(r"^HighScore([A-Za-z]\w*)$")

NOTES = [
    "Visual Pinball X native table. State lives in User/VPReg.stg, an OLE",
    "Compound File: one storage per table, one stream per setting, each stream",
    "holding a UTF-16LE string. Scores are decimal strings, initials are plain",
    "strings. There is no checksum and no ROM to reject a written value, so",
    "insertion is a straight stream rewrite -- confirmed on the real cabinet in",
    "2026-08-17 for gotg_2020 (patched scores displayed, and were then beaten",
    "and overwritten by real play).",
    "Rewriting a stream in place only works when the new value is the same",
    "length; a longer or shorter value needs the CFB directory and FAT updated.",
    "research/patch_stg_score.py takes the same-length shortcut (zero-pad the",
    "numeric string). A production writer should use a real CFB writer instead --",
    "the app already talks to ole32's IStorage via PinballScores/MSStorage, which",
    "supports resizing streams natively.",
]


def read_storage(ole, storage):
    values = {}
    for entry in ole.listdir(streams=True):
        if entry[0] != storage:
            continue
        name = entry[-1]
        data = ole.openstream("/".join(entry)).read()
        try:
            values[name] = data.decode("utf-16-le")
        except UnicodeDecodeError:
            values[name] = None
    return values


def build_map(storage, title, values):
    ranked, champions = [], []
    for name in sorted(values):
        match = RANKED.match(name)
        if match:
            rank = int(match.group(1))
            if name + "Name" not in values:
                continue
            ranked.append((rank, {
                "label": "High Score #%d" % rank,
                "initials": {"stream": name + "Name", "encoding": "string"},
                "score": {"stream": name, "encoding": "decimal_string"},
            }))
            continue
        match = CHAMPION.match(name)
        if match and name + "Name" in values:
            field = match.group(1)
            champions.append({
                "label": field,
                "_note": "field name taken verbatim from the table script",
                "initials": {"stream": name + "Name", "encoding": "string"},
                "score": {"stream": name, "encoding": "decimal_string"},
            })

    ranked.sort()
    # Same category rollup the NVRAM maps carry: the numbered slots are the
    # machine's main leaderboard, each champion field is its own category.
    def key_for(name):
        if name is None:
            return "main"
        return re.sub(r"[^a-z0-9]+", "_", name.lower()).strip("_")

    categories = []
    if ranked:
        categories.append({"key": "main", "name": None, "order": "ranked",
                           "slots": [e["label"] for _, e in ranked]})
    categories += [{"key": key_for(e["label"]), "name": e["label"],
                    "order": "ranked", "slots": [e["label"]]}
                   for e in champions]

    game_map = {
        "_fileformat": "pinballscores-stg-0.1",
        "_notes": [title] + NOTES,
        "_pinballscores": {
            "cabinet_table": storage,
            "title": title,
            "status": "generated from ScoresData/User/VPReg.stg and validated",
            "source_file": "User/VPReg.stg",
            # VPX stores the string the table script wrote, with no padding of
            # its own, so nothing may be stripped from it.
            "initials_padding": "none",
            "categories": categories,
        },
        "_metadata": {
            "version": 1,
            "platform": "vpx-stg",
            "storage": storage,
        },
        "high_scores": [entry for _, entry in ranked],
    }
    if champions:
        game_map["mode_champions"] = champions
    body = {k: v for k, v in game_map.items() if k != "_pinballscores"}
    game_map["_pinballscores"]["map_version"] = hashlib.sha256(
        json.dumps(body, sort_keys=True).encode()).hexdigest()[:12]
    return game_map


def validate(game_map, values):
    """Return (problems, notes) for a map against the streams it describes."""
    problems, notes = [], []
    ranked_values = []
    for group in ("high_scores", "mode_champions"):
        for entry in game_map.get(group, []):
            for key in ("initials", "score"):
                stream = entry[key]["stream"]
                if stream not in values:
                    problems.append("%s: stream %r missing" % (entry["label"], stream))
                    continue
                text = values[stream]
                if key == "score":
                    if text is None or not text.strip().isdigit():
                        problems.append("%s: score stream %r is not a decimal "
                                        "string (%r)" % (entry["label"], stream, text))
                    elif group == "high_scores":
                        ranked_values.append(int(text))
                elif not text:
                    notes.append("%s: initials stream %r is empty"
                                 % (entry["label"], stream))

    # Unlike the ROM tables, VPX table scripts do not necessarily keep their
    # HighScoreN slots sorted -- some overwrite a slot in place.  That is not a
    # mapping error, but it does mean slot number is not rank: the extractor
    # has to derive rank by sorting the values it read.
    if ranked_values != sorted(ranked_values, reverse=True):
        out_of_order = [i + 1 for i in range(1, len(ranked_values))
                        if ranked_values[i] > ranked_values[i - 1]]
        notes.append("HighScoreN slots are not in descending order (slot %s "
                     "beats the slot above it) -- slot number is not rank"
                     % ", ".join(str(s) for s in out_of_order))
    return problems, notes


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--stg", required=True)
    ap.add_argument("--check", action="store_true")
    ap.add_argument("--list", action="store_true",
                    help="dump every storage/stream in the file and exit")
    args = ap.parse_args()

    ole = olefile.OleFileIO(args.stg)
    if args.list:
        for entry in ole.listdir(streams=True):
            data = ole.openstream("/".join(entry)).read()
            try:
                text = data.decode("utf-16-le")
            except UnicodeDecodeError:
                text = "<binary %d bytes>" % len(data)
            print("%s = %s" % ("/".join(entry), text))
        return 0

    storages = {e[0] for e in ole.listdir(streams=True)}
    extra = storages - set(CABINET_TABLES)
    if extra:
        print("note: %s also present in this VPReg.stg but not on the cabinet "
              "table list" % ", ".join(sorted(extra)), file=sys.stderr)

    os.makedirs(OUT_DIR, exist_ok=True)
    failures = 0
    for storage, title in sorted(CABINET_TABLES.items()):
        if storage not in storages:
            print("MISSING: %s has no storage in %s" % (storage, args.stg))
            failures += 1
            continue
        values = read_storage(ole, storage)
        game_map = build_map(storage, title, values)
        problems, notes = validate(game_map, values)

        path = os.path.join(OUT_DIR, "%s.map.json" % storage)
        text = json.dumps(game_map, indent=2) + "\n"
        current = open(path).read() if os.path.exists(path) else None

        ranked = len(game_map["high_scores"])
        champs = len(game_map.get("mode_champions", []))
        print("== %s: %d ranked slot(s), %d champion slot(s)"
              % (storage, ranked, champs))
        for note in notes:
            print("    note: %s" % note)
        for problem in problems:
            print("    FAIL: %s" % problem)
        failures += bool(problems)

        if args.check:
            if current != text:
                print("    DIFFERS from %s" % os.path.relpath(path))
                failures += 1
        else:
            with open(path, "w") as f:
                f.write(text)
            print("    %s %s" % ("updated" if current != text else "unchanged",
                                 os.path.relpath(path)))
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
