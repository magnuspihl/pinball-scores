# NVRAM maps

One map per VPinMAME table on the cabinet, in
[tomlogic/pinball-memory-maps](https://github.com/tomlogic/pinball-memory-maps)
file format v0.8. See [../TABLE-MAPPING.md](../TABLE-MAPPING.md) for how each
one was derived and verified.

Each file is named after the ROM as it appears in `ScoresData/nvram/`, so the
lookup from a `.nv` file to its map is just the filename.

## Reading a map

- `high_scores` — the ranked leaderboard. Rank is *positional*, and should not
  be stored as part of a score's identity: sort by value instead.
- `mode_champions` — named per-mode achievements. The label is the category.
- A record's value field is called `score` or `counter`. That distinction is
  upstream's, and it is the one the extractor should use to pick the score
  API's `value_type`: `counter` → `counter`, `score` → `score`, plus
  `units: "seconds"` → `duration` and `encoding: "wpc_rtc"` → `timestamp`.
  There is no need for a separate `value_type` key in the map; it is derivable.
- `checksum8` / `checksum16` — regions the game ROM validates. **Anything
  written into a covered region must have its checksum recomputed** or the ROM
  discards the record on next boot and restores its own factory default. The
  format's rules: a `checksum8` region's last byte makes the low byte of the
  sum `0xFF`; a `checksum16` region's last two bytes hold `0xFFFF` minus the
  sum of everything before them, in platform byte order.
- `_pinballscores` — our own provenance block: which cabinet ROM this is, where
  the map came from, and how far it has been verified. Keys starting with `_`
  are metadata and are ignored by the parser.

## Provenance and licensing

Ten of these maps are copied from upstream at commit `aab556e`, unmodified apart
from the added `_pinballscores` block; five Stern SAM maps are ours, two of them
building on upstream fields. Every file keeps its original `_metadata.copyright`
and `_metadata.license` (LGPL-3.0). Upstream's map data is additionally offered
under the ODbL / DbCL — see `LICENSE-ODbL.md` and `LICENSE-DbCL` in that repo.

They are copied rather than referenced so this repo stays self-contained and
works offline. `python3 ../tools/build_maps.py --upstream <checkout> --check`
re-diffs them against upstream so drift stays visible.

Three corrections here are not yet upstream and are worth sending back: the
per-record `checksum16` sections for the SAM tables, the record-alignment fix
that `twd_156h` carries relative to upstream's `twd_156`, and the
`smanve_101` "Best Combo Champion" field width.
