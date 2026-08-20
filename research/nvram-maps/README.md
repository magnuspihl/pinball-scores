# NVRAM maps

One map per VPinMAME table on the cabinet, in
[tomlogic/pinball-memory-maps](https://github.com/tomlogic/pinball-memory-maps)
file format v0.8. See [../TABLE-MAPPING.md](../TABLE-MAPPING.md) for how each
one was derived and verified, and
[../ADDING-A-TABLE.md](../ADDING-A-TABLE.md) to add another.

Each file is named after the ROM as it appears in `ScoresData/nvram/`, so the
lookup from a `.nv` file to its map is just the filename.

## Reading a map

- `high_scores` / `mode_champions` — where the bytes are, in the upstream
  format's own sections. **Don't derive the score API's category from which of
  these two arrays a record is in** — that rule is wrong for the four ranked
  champion groups on this cabinet (`Q Continuum #1..#4` and friends), and it
  would split the Grand Champion off the main board. Use `categories` instead.
- `_pinballscores.categories` — the rollup the score API actually stores. Each
  entry has a stable `key`, a display `name` (`null` for the machine's main
  leaderboard) and the physical slots it owns, **best first**. Reading: take
  the slots, derive rank by sorting. Writing: sort the rows and fill the slots
  in order. `order` is `ranked` for a leaderboard, or `positional` where slot
  order is meaningful and must not be re-sorted (Medieval Madness's King of the
  Realm is a dated history of the last four kings, not a ranking). Slot count
  is a hard ceiling on how much of a category fits on the machine.
  `key` is what dedup and the `/api/admin/categories/{table}/{key}` endpoints
  address; `name` is ROM display text and may be prettified without moving the
  key. `value_type` is the API's enum (`score`, `counter`, `duration`,
  `timestamp`); `value_unit` appears on durations and is the unit of the
  machine's own integer, so the raw value goes across unconverted (Lord of the
  Rings' ring timer submits as `60000` with `value_unit: "cs"`).
  `display_suffix`, where present, is the ROM's own unit wording ("Castles
  Destroyed") and maps onto the API field of the same name.
- `_pinballscores.initials_padding` — what may be stripped from an initials
  field, and nothing else may be. `none`: the field *is* the entry, so every
  character counts (WPC and Data East always take exactly three chosen
  characters — Addams Family's fourth place is `'CG '` and Batman's is `' NF'`).
  `space`: a shorter name inside a wider field, so trailing spaces are padding
  (Whitestar's 10-byte field genuinely holds names of 2–10 characters).
  `null`: NUL-terminated with 0xFF filler (Stern SAM).
- `_pinballscores.map_version` — hash of the map's content. Submitting it with
  each score is what makes a mapping bug recoverable: rows can be traced to the
  map that produced them and retracted. This repo has already shipped two such
  bugs, and under insert-only dedup a corrected value lands as a *new* row
  beside the wrong one rather than replacing it.
- A record's value field is called `score` or `counter` — upstream's own
  distinction, and what `categories[].value_type` is derived from (`counter` →
  `counter`, `units: "seconds"` → `duration`, `encoding: "wpc_rtc"` →
  `timestamp`, otherwise `score`). Read it off the category rather than
  re-deriving it; `build_maps.py` also rejects a category whose slots disagree.
- **`scale` is presentation, never storage.** `read_field()` returns the stored
  integer and `NvramMap.display()` renders it for humans. Submitting a scaled
  value would reintroduce exactly the float rounding that once turned
  130,296,090 into 130,296,088.
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

Two corrections here are not yet upstream and are worth sending back: the
per-record `checksum16` sections for the SAM tables, and the record-alignment
fix that `twd_156h` carries relative to upstream's `twd_156`.
