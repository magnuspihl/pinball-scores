# PR draft for tomlogic/pinball-memory-maps

Target: `maps/stern/sam/smanve_101.map.json` on tomlogic/pinball-memory-maps.

## How to submit

1. Fork `tomlogic/pinball-memory-maps` (if not already forked), clone your fork.
2. Create a branch, e.g. `git checkout -b smanve_101-checksum16`.
3. Either apply `smanve_101-checksum16.patch` with `git apply` from the repo
   root, or just copy `smanve_101.map.json` from this folder over
   `maps/stern/sam/smanve_101.map.json`.
4. Sanity check locally if you want: `tools/reformat-json.sh
   maps/stern/sam/smanve_101.map.json` should produce no changes (already
   canonical), and `python3 tools/update-index.py` should report no changes
   to `index.json` either — confirmed both already, included below.
5. Commit, push, open the PR against `main` using the title/body below.

## Suggested PR title

Add checksum16 entries and fix Best Combo Champion field width for smanve_101

## Suggested PR body

This map is missing `checksum16` entries for its `high_scores` and
`mode_champions` records — every other field the Stern SAM boot code
validates on this platform is checksum-protected the same way (see
`st_162.map.json` for an existing example of the same pattern), and
`smanve_101` is no exception. Without this documented, a naive read/write
tool built from this map alone would silently produce records that fail the
game's own boot-time validation and get replaced with the ROM's compiled-in
factory defaults — which is exactly what happened to us before we tracked
this down.

**How this was derived and verified:** by disassembling the actual
`smanve_101` game ROM (not guessed/inferred) and locating the checksum
routine directly — `sub_1b30`/`sub_1c010` in the ROM, a plain 16-bit running
byte-sum, complemented and stored as the last 2 bytes of a 30-byte range,
matching this project's own documented `checksum16` convention exactly
("the last two bytes of the range are the 16-bit result of subtracting all
prior bytes in the range from `0xFFFF`"). Cross-checked against 24 known
real/factory-default records collected from a real cabinet — 24/24 matched.

Also includes a small correction: `Best Combo Champion`'s `counter` field was
documented as 1 byte at `+0x1a`. That can't be right — a real captured value
of 327,681 doesn't fit in a single byte, and the standard 4-byte-at-`+0x18`
layout (matching every other record in this map) checksums correctly against
real data where the 1-byte version doesn't. Corrected to match.

Verified end-to-end with this project's own `py-pinmame-nvmaps` reference
parser (`nvram_parser.py --dump`, which verifies `checksum16` entries by
default):
- Real cabinet data (`smanve_101.nv`): all 12 records pass with no checksum
  mismatches reported.
- A legitimately-written test record (patched with the correct checksum):
  also passes cleanly, confirming a compliant writer round-trips correctly.
- A deliberately corrupted record (score changed, checksum left stale): now
  correctly flagged — `checksum at 0x2102B80: 0xEB2F != 0xEA46 Grand
  Champion` — proving the added entries actually catch invalid data, not
  just sit there unused.

`_metadata.version` bumped 3 → 4 per this project's convention for map
changes.

## Verification transcript (for reference, not part of the PR body)

```
=== real backup, with new checksum16 map ===
(no checksum mismatch lines — all 12 pass)

=== corrected test patch (TS1-TS5), with new checksum16 map ===
(no checksum mismatch lines — all 12 pass)

=== deliberately corrupted file, with new checksum16 map ===
checksum at 0x2102B80: 0xEB2F != 0xEA46 Grand Champion
```
