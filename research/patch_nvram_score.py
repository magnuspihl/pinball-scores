#!/usr/bin/env python3
"""
Patch high-score slots directly in a VPinMAME NVRAM (.nv) file, using a
pinmame-nvram-maps-style JSON map (see research/nvram-maps/).

Targets the Stern SAM platform record layout: 11-byte null-terminated ASCII
initials (0xFF-padded), 13 bytes of 0xFF filler, a 4-byte little-endian
score/counter, then a 2-byte checksum tag, then 2 more 0xFF filler bytes -
32 (0x20) bytes total per record.

CHECKSUM (reverse-engineered 2026-08-17 from the actual smanve_101 game ROM,
confirmed against 24 known-real records - see research/rom-analysis/NOTES.md):
tag = 0xFFFF - (sum of the 28 bytes from the record start through the end of
the score field, as plain unsigned bytes, mod 65536). Without the correct tag
a record fails the game's own boot-time validation and gets silently replaced
with the ROM's compiled-in factory default for that slot - this is why every
earlier test patch (which left the old, now-stale tag in place) got reverted.

Usage:
    python3 patch_nvram_score.py <map.json> <input.nv> <output.nv> \
        "Grand Champion=TS1:500" "First Place=TS2:400" ...

Each patch arg is "<label>=<initials>:<score>". <label> must match a
"label" in the map's high_scores list.
"""
import json
import sys

CHECKSUM_SPAN = 0x1c  # bytes from record start through end of the score field
TAG_OFFSET = 0x1c      # 2-byte tag immediately follows the checksummed span


def parse_addr(s):
    return int(s, 16) if isinstance(s, str) else int(s)


def load_map(path):
    with open(path) as f:
        return json.load(f)


def compute_tag(record_bytes):
    checksum = sum(record_bytes[:CHECKSUM_SPAN]) & 0xFFFF
    return (0xFFFF - checksum) & 0xFFFF


def patch_slot(data, entry, initials, score, base_addr=0x02100000):
    ini_field = entry["initials"]
    score_field = entry.get("score") or entry["counter"]
    ini_len = ini_field["length"]
    score_len = score_field["length"]

    if len(initials) > ini_len - 1:
        raise ValueError(f"initials {initials!r} too long for {ini_len}-byte field")
    if not (0 <= score < 2 ** (8 * score_len)):
        raise ValueError(f"score {score} does not fit in {score_len} bytes")

    ini_off = parse_addr(ini_field["start"]) - base_addr
    score_off = parse_addr(score_field["start"]) - base_addr
    if score_off != ini_off + 0x18 or score_len != 4:
        raise ValueError(
            f"slot at 0x{ini_off:x} has a non-standard score offset/length "
            f"({score_off - ini_off:#x}/{score_len}); the checksum span assumes "
            f"the standard 0x18-offset 4-byte layout - verify before patching"
        )

    # Sanity check: the field we're about to overwrite should currently look
    # like a previously-parsed initials/score slot (ASCII + NUL then 0xFF
    # padding), not something unrelated - refuse to write over unexpected data.
    existing = data[ini_off:ini_off + ini_len]
    nul = existing.find(b"\x00")
    if nul < 0 or any(b != 0xFF for b in existing[nul + 1:]):
        raise ValueError(
            f"slot at 0x{ini_off:x} doesn't look like an initials field "
            f"(expected ASCII+NUL+0xFF padding, got {existing.hex()}); refusing to patch"
        )

    new_ini = initials.encode("ascii") + b"\x00" + b"\xff" * (ini_len - len(initials) - 1)
    new_score = score.to_bytes(score_len, "little")

    data[ini_off:ini_off + ini_len] = new_ini
    data[score_off:score_off + score_len] = new_score

    tag = compute_tag(data[ini_off:ini_off + CHECKSUM_SPAN])
    tag_off = ini_off + TAG_OFFSET
    data[tag_off:tag_off + 2] = tag.to_bytes(2, "little")


def main():
    if len(sys.argv) < 4:
        print(__doc__)
        sys.exit(1)

    map_path, in_path, out_path = sys.argv[1:4]
    patches = sys.argv[4:]

    game_map = load_map(map_path)
    by_label = {e["label"]: e for e in game_map.get("high_scores", []) + game_map.get("mode_champions", [])}

    data = bytearray(open(in_path, "rb").read())

    for p in patches:
        label, rest = p.split("=", 1)
        initials, score = rest.split(":", 1)
        if label not in by_label:
            raise SystemExit(f"unknown label {label!r}; known: {list(by_label)}")
        patch_slot(data, by_label[label], initials, int(score))
        print(f"patched {label!r} -> {initials} : {score}")

    with open(out_path, "wb") as f:
        f.write(data)
    print(f"wrote {out_path} ({len(data)} bytes)")


if __name__ == "__main__":
    main()
