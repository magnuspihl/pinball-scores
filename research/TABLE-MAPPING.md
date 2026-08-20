# Table mapping — every table on the cabinet (2026-08-20)

The earlier write-back investigation ([FINDINGS.md](FINDINGS.md)) mapped two
tables end to end: `smanve_101` (Stern SAM) and `gotg_2020` (VPX/STG). This
document is the same job done for all eighteen tables on the machine, in one
format that serves both extraction and insertion.

Everything lives in:

- `research/nvram-maps/` — 15 maps, one per VPinMAME table
- `research/stg-maps/` — 3 maps, one per Visual Pinball X table
- `research/tools/` — the tooling used to derive and check them

## What "mapped" means here

Reading a score needs one thing: where the bytes are. Writing one needs a
second: whether the game ROM validates that region when it boots. Getting the
second part wrong is not a subtle failure — on Stern SAM it silently reverted
every write we made for a week, because each record carries a checksum nobody
knew about. So a table only counts as mapped when both are settled:

1. **Fields** — offsets and encodings for every score, counter and set of
   initials, grouped into `high_scores` (a ranked leaderboard) and
   `mode_champions` (named per-mode achievements).
2. **Integrity** — every checksum region covering those fields, or a
   substantiated finding that there isn't one.

The maps are written in
[tomlogic/pinball-memory-maps](https://github.com/tomlogic/pinball-memory-maps)
file format v0.8, the same format our existing maps already used. That buys the
reference parser, the existing `patch_nvram_score.py`, and a path to
contributing fixes back upstream. `checksum8`/`checksum16` sections are part of
that format, so "where the checksum is" is expressed in the map rather than
hardcoded per platform in the app.

## Status per table

`records` = ranked high-score slots + named champion slots. `checksums` =
regions declared by the map, all of which validate against the sample NVRAM
untouched.

| Table | ROM / storage | Platform | Records | Checksums | Write path |
|---|---|---|---|---|---|
| The Avengers | `avs_170` | Stern SAM | 5 + 0 | 5 ✓ | per-record checksum16 |
| Batman | `btmn_106` | Data East v3 | 6 + 0 | none found | plain byte write |
| Indiana Jones | `ij_l7` | Williams WPC | 5 + 0 | 185 ✓ | block checksum16 |
| Lord of the Rings | `lotr` | Whitestar | 5 + 1 | none found | plain byte write |
| Medieval Madness | `mm_109c` | Williams WPC | 5 + 11 | 169 ✓ | block checksum16 |
| Pirates of the Caribbean | `potc_600as` | Stern SAM | 5 + 5 | 10 ✓ | per-record checksum16 |
| Simpsons Pinball Party | `simpprty` | Whitestar | 5 + 0 | none found | plain byte write |
| Spider-Man Vault Ed. | `smanve_101` | Stern SAM | 5 + 7 | 12 ✓ | per-record checksum16 |
| Star Trek: TNG | `sttng_l7` | Williams WPC | 5 + 8 | 143 ✓ | block checksum16 |
| Star Wars | `stwr_107` | Data East v3 | 6 + 0 | none found | plain byte write |
| Terminator 2 | `t2_l8` | Williams WPC | 5 + 0 | 134 ✓ | block checksum16 |
| The Addams Family | `taf_l7` | Williams WPC | 5 + 0 | 136 ✓ | block checksum16 |
| Tales of the Arabian Nights | `totan_14` | Williams WPC | 5 + 0 | 164 ✓ | block checksum16 |
| The Walking Dead | `twd_156h` | Stern SAM | 5 + 14 | 19 ✓ | per-record checksum16 |
| X-Men LE | `xmn_151h` | Stern SAM | 5 + 1 | 6 ✓ | per-record checksum16 |
| Deadpool | `jpsdeadpool` | VPX / STG | 4 + 0 | n/a | rewrite stream |
| Game of Thrones | `gameofthrones` | VPX / STG | 16 + 0 | n/a | rewrite stream |
| Guardians of the Galaxy | `gotg_2020` | VPX / STG | 5 + 4 | n/a | rewrite stream |

All 15 NVRAM maps pass `research/tools/validate_maps.py` — fields decode,
high scores come out in descending order, every declared checksum already
validates in the untouched file, and a simulated write round-trips without
touching a byte outside the field and its own checksum.

**Star Wars had a question mark next to it on the table list ("no scores?").
It does store scores** — six slots, currently reading `CNH` 350,000,000 down to
` NF` 100,000,000 in the sample. If the cabinet shows nothing, that is about
the state of the machine's own file, not about the format; a fresh dump will
say which.

Two things in `ScoresData/` that are deliberately not covered: `spagb_100.nv`
is a 3-byte stub and isn't on the cabinet list, and the Pinball FX3 save format
stays out of scope as agreed — see the end of [FINDINGS.md](FINDINGS.md).

## How each platform was settled

### Williams WPC — already solved upstream, and verified

The six WPC titles were the easy case: upstream maps them *and* documents the
checksum16 regions, including one that covers the high-score table specifically.
Nothing needed deriving. What was needed was confirming it, so all six were
checked field by field and checksum by checksum against two independent dumps
(the sample in `ScoresData/` and a second one from the reference parser's test
corpus). 931 checksum regions across the six tables, all valid.

### Stern SAM — the checksum turns out to be a search key

Every SAM record is a 32-byte struct: initials at `+0x00`, a 4-byte
little-endian value at `+0x18`, and at `+0x1c` a 16-bit checksum equal to
`0xFFFF` minus the sum of the 28 bytes before it. That formula came out of
disassembling the `smanve_101` ROM (see `rom-analysis/NOTES.md`); this round
confirmed it is not game-specific by finding that upstream's Star Trek SAM maps
already describe exactly the same thing as `{"start": <record>, "length": 30}`
`checksum16` entries — an independent corroboration of the ROM work, arrived at
by someone else from a different direction.

That has a useful consequence. A 16-bit checksum over a record's own contents
is a *signature*: scan a 128KB NVRAM image for every offset where it holds and
you get the complete record table, with roughly one false positive per 65536
candidates. `tools/sam_record_scan.py` does this, and it is how the three SAM
tables nobody had mapped were mapped:

```
$ python3 research/tools/sam_record_scan.py ScoresData/nvram/potc_600as.nv
ScoresData/nvram/potc_600as.nv: 10 records
  0x02102948  'MHP'           128,591,800
  0x02102968  'MHP'           100,609,520  (+0x20)
  ...
```

No layout guess needed, and no ROM needed. It also means the maps can be kept
current mechanically: **champion slots that have never been earned are
0xFF-filled and do not appear.** Re-run the scan against a fresh dump from the
machine and add any slot that has since materialised. That is the most likely
reason a SAM table here shows fewer champion slots than the game really has —
`avs_170` reports five records (the leaderboard only), and Avengers certainly
has champion modes; they just have not been set on this cabinet yet.

### Whitestar and Data East — a negative result, argued rather than assumed

Neither platform has a single checksum section in any of upstream's 64 maps for
them. Absence upstream is not evidence of absence, so this was tested directly.

`tools/find_checksums.py` inverts the checksum definitions: given the byte range
you care about, it reports every `(start, end)` whose stored checksum already
validates. Against one NVRAM image that is useless — a checksum8 match is a
1-in-256 event, so thousands of candidate ranges throw off dozens of hits. The
tool therefore takes **two or more independent dumps of the same ROM** and only
reports candidates that validate in all of them.

Positive control first, to show the method can find a checksum that is known to
exist. Pointed at the Addams Family high-score block it rediscovers upstream's
documented region exactly, and only that one:

```
$ python3 research/tools/find_checksums.py --cover 0x1C61-0x1C80 --size 0x2000 \
      ScoresData/nvram/taf_l7.nv <second dump>/taf_l7.nv
checksum16: 1 candidate range(s)
  {"start": "0x1C61", "end": "0x1C82"}
```

Same tool, same two-dump rule, pointed at `lotr`, `simpprty`, `btmn_106` and
`stwr_107` — over the score block, over the initials block, and over both
together with a wide window: **nothing.** Three further lines of evidence say
that is a real absence rather than a search that missed:

- The one candidate set that did survive (two ranges on `btmn_106`, from the
  wide-window pass) validates in 1 of the 24 Data East version-3 sample files —
  i.e. only in the file it was derived from. A genuine platform checksum would
  hold for its siblings too.
- Searching for a checksum region at a *consistent position relative to the
  high-score block* across all 8 Stern Whitestar, 17 Sega Whitestar and 24 Data
  East games that have both a map and a sample file finds no pattern above
  chance, and no checksum16 hit anywhere at all.
- Upstream's Williams System 11 maps — the hardware lineage Data East's board
  is derived from — do carry checksums, but over the audits and the credit
  counter only, never over high scores. Not protecting the leaderboard is the
  house style on that family.

So for these four tables, insertion should be a straight byte write. That is a
prediction, and the honest way to state its strength is: strong enough to try
before spending a day on ROM disassembly, not strong enough to assume. The
cheap confirmation is one patched score on one table — if `lotr` sticks after a
reboot, Whitestar is settled, and `btmn_106` settles Data East.

### VPX / STG — no ROM, no checksum, already proven on hardware

The three Visual Pinball tables keep state in `User/VPReg.stg`, an OLE Compound
File with one storage per table and one UTF-16LE string stream per setting.
There is no checksum and no factory default to revert to, and writing was
already confirmed on the real cabinet for `gotg_2020` back in August.

The maps exist because the *pairing* is table-script convention, not a standard:
`HighScore3` goes with `HighScore3Name`, but champion fields follow no rank
pattern (`HighScoreXandar` / `HighScoreXandarName`). Champion labels are the
table script's own field names, verbatim — inventing a nicer display name would
quietly become a different category in the score database if anyone later
corrected it.

One thing to know before the extractor treats slot number as rank: **VPX tables
do not necessarily keep their slots sorted.** Game of Thrones currently has
152,329,750 sitting in slot 9 above 4,000,000 in slot 13, and Guardians has its
real top score in slot 5. The initials still pair correctly with the values
(the real, non-round scores carry real initials; the untouched defaults carry
`AAA`/`BBB`/…), the list simply is not re-sorted on write. Rank has to be
derived by sorting what you read. That matches the direction already agreed for
the score store — rank is a query, not stored state — so it costs nothing here,
but it would be a real bug for anything that trusted the slot index.

## Two upstream maps are wrong, and one matters

**`twd_156` pairs every score with the wrong initials.** Upstream reads the
Walking Dead record as "score at *X*, initials at *X+8*". The record checksum
disagrees: it covers `X-0x18 … X+3`, which puts the record boundary 0x18 bytes
*before* the score, exactly matching the layout confirmed by ROM disassembly on
`smanve_101` — initials at the start of the record, score at `+0x18`. Upstream's
pairing is shifted by one record, so it reports each score against the *next*
slot's initials, and the last slot's initials read as an empty string sitting
past the end of the table. Upstream's own `twd_160h` map uses the correct
layout, which is a second, independent sign that `twd_156` is the odd one out.

Our `twd_156h.map.json` uses the corrected pairing. Concretely, on this
cabinet's file the Grand Champion is `JDB` with 75,000,000, not `LFS`.

**`smanve_101` gives "Best Combo Champion" a 1-byte counter at `+0x1a`**, which
cannot hold the real value (327,681) and contradicts the checksum. Already
known from the earlier session; the corrected 4-byte field at `+0x18` is
carried into our map. Both fixes are worth sending upstream, along with the
`checksum16` sections for the SAM tables.

## What is still open

- **Champion labels for `potc_600as` (5 slots) and `xmn_151h` (1 slot).** The
  offsets, values and initials are solid; only the names are unknown, and no
  public map covers either ROM. They are currently `Champion 1 (unnamed)` etc.
  Two ways to fix that, whichever is easier: read the names off the machine
  (they appear in the operator menu's high-score reset list and in attract
  mode), or hand over those two ROMs and the labels can be pulled out of the
  display strings the same way the checksum was. Worth doing before the score
  database starts using them, because the label becomes the category key.
- **Confirming the Whitestar / Data East negative result on hardware** — one
  patched score on `lotr` and one on `btmn_106`, as described above.
- **The SAM real-hardware write test is still pending** from the previous
  session. `research/test-output/` holds correctly-checksummed test files for
  `smanve_101` and `xmn_151h` that have never been tried, since the checksum
  was cracked after the last cabinet visit.
- **Champion slots not yet earned won't be in the SAM maps.** Re-run
  `sam_record_scan.py` on the fresh dumps and top the maps up.

When the new NVRAM and STG files arrive, the whole set can be re-checked
against them in one command each — see below. That is also the fastest way to
catch a ROM version on the cabinet that differs from what the sample files came
from.

## Reproducing / re-running

```sh
# validate every NVRAM map against a directory of .nv files
python3 research/tools/validate_maps.py --nvram-dir <dir> --verbose

# re-derive the SAM record tables from a fresh dump
python3 research/tools/sam_record_scan.py <dir>/*.nv

# check the STG maps against a fresh VPReg.stg (and see every field in it)
python3 research/tools/build_stg_maps.py --stg <file> --check
python3 research/tools/build_stg_maps.py --stg <file> --list

# hunt for a checksum protecting a byte range, given two dumps of one ROM
python3 research/tools/find_checksums.py --cover 0x15DC-0x1653 a/lotr.nv b/lotr.nv

# write a score into any mapped table, on any platform (checksums handled)
python3 research/tools/patch_score.py research/nvram-maps/lotr.map.json \
    in.nv out.nv "Grand Champ=ZZZ:123456780" [--dry-run]

# regenerate the NVRAM maps from a pinball-memory-maps checkout
python3 research/tools/build_maps.py --upstream <path> [--check]
```

The second dump used throughout this work is the test corpus in
[tomlogic/py-pinmame-nvmaps](https://github.com/tomlogic/py-pinmame-nvmaps)
(`test/nvram/`), which carries a sample for 12 of the 15 tables here and 451
files in total. The maps were also cross-checked against that project's
reference parser: across all 15 tables, its reading of 124 records agrees with
ours on every label, every set of initials, every value it renders (it does not
print `counter` fields), and on the validity of every checksum region.
