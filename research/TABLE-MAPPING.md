# Table mapping — every table on the cabinet (2026-08-20)

The earlier write-back investigation ([FINDINGS.md](FINDINGS.md)) mapped two
tables end to end: `smanve_101` (Stern SAM) and `gotg_2020` (VPX/STG). This
document is the same job done for all eighteen tables on the machine, in one
format that serves both extraction and insertion.

**Adding a table later? Start with [ADDING-A-TABLE.md](ADDING-A-TABLE.md)** —
this file explains what was found and why; that one is the step-by-step
procedure, including when a ROM is needed and how to read labels out of it.

Everything lives in:

- `research/ADDING-A-TABLE.md` — the runbook for adding a nineteenth table
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

### Validated against the live cabinet (2026-08-20)

The maps above were derived from the committed sample data, which is mostly
factory defaults. Magnus then supplied a full set of current files off the
machine. **All 15 NVRAM maps and all 3 STG maps validate against them
unchanged** — every checksum region still holds on data the ROMs wrote
themselves, with real player scores in it.

Nine tables have genuine play data in them now, and it reads correctly:
`RAT` 130,296,090 and 126,207,970 on Spider-Man, `FRY` 213,747,750 on Pirates,
`RAT` 76,492,350 on Walking Dead, `KAS` 43,841,700 on Avengers, `RAT`
693,512,940 and `MHP` 1,492,343,940 on Star Trek, `RAR` 17,228,210 on Batman,
plus Deadpool and Game of Thrones on the VPX side. Lord of the Rings, Simpsons,
Star Wars, Addams Family, Indiana Jones, T2, Tales of the Arabian Nights and
X-Men are still sitting on factory defaults — nobody has beaten them.

Two things came out of that data, one confirming a fix and one reversing one;
both are in "One upstream map is wrong" below. The record scan also found **no
new champion slots** on any SAM table, so nothing needed adding: Avengers still
has only its leaderboard, Pirates' five champion counters are still at their
factory values (25 / 15 / 10 / 5 / 5), and X-Men is untouched.

### Champion labels, from the ROMs (2026-08-20)

The last unknowns were the champion labels on Pirates of the Caribbean and
X-Men, which no public map covers. Magnus supplied both ROMs, and they carry
the labels as plain text:

- **Pirates of the Caribbean** — `Pirate King`, `Gauntlet Champ 1`,
  `Gauntlet Champ 2`, `Gauntlet Champ 3`, `Davy Jones Champion`
- **X-Men** — `Combo Champion`

Ten labels for the ten records the scan found, and six for six. The ordering is
confirmed rather than assumed: each ROM carries a second table, the default
initials that seed each slot, in the same record order. X-Men has never been
beaten, so all six of its slots still hold `G S`, `L R`, `J R`, `D T`, `P P`,
`YAN` — exactly the ROM's `STERN, ROPP, ROTHARMEL, THORNE, POWERS, YANCY`, in
order, a clean 1:1 match. On Pirates the five champion slots still hold `KEF`,
`J B`, `G S`, `M S`, `XAQ`, matching the ROM's last five names; the ranked slots
hold `NORDMAN` and `OCONNOR`'s defaults pushed down to third and fourth, which
is exactly where the top two defaults land once three real scores arrive above
them.

One judgement call for whoever wires this into the score database: Pirates'
`Gauntlet Champ 1/2/3` are one three-deep leaderboard (15/10/5 at factory
defaults), not three unrelated achievements. They probably want collapsing into
a single category with rank derived, the same treatment the ranked high scores
get.

Pinball FX3 stays out of scope as agreed — see the end of
[FINDINGS.md](FINDINGS.md).

## The two tables that never worked

### Star Wars — mapped fine, but nobody can reach the demo scores

Star Wars reads correctly: six slots, `CNH` 350,000,000 down to ` NF`
100,000,000. Those are the ROM's compiled-in demo scores, and they are
identical in all three dumps — the committed sample, the reference corpus, and
the current cabinet file. Nothing has ever displaced them.

That is not a mapping problem. `game_state` in the same file shows the machine
being played normally — the current cabinet dump was taken *mid-game*, ball 2,
player 1 on 1,182,520, and the previous sample caught a finished game of
29,625,010. **The lowest demo score is 100,000,000, so a 30M game never gets
near the board.** Star Wars will keep showing six untouched demo entries
forever unless something lowers them.

Same story on three other tables, worth knowing before wondering why they look
static:

| table | last games seen | lowest demo score |
|---|---|---|
| Star Wars | 1,182,520 / 29,625,010 | 100,000,000 |
| Lord of the Rings | 7,043,630 / 4,812,940 | 40,000,000 |
| Simpsons Pinball Party | 620,890 / 845,900 | 25,000,000 |
| The Addams Family | 12,152,080 / 47,652,740 | 105,000,000 |

This is the concrete case for the write-back plan already agreed — pushing the
ROM's demo scores down to the reserved `---` / 1 marker rather than blanking
them. On these four tables it is the difference between a leaderboard that
records real play and one that can't.

### Ghostbusters — the hardware isn't emulated

`spagb_100.nv` has been sitting in `ScoresData/nvram/` unexplained. It is Stern
**Ghostbusters (2016)**, and it cannot work, for a reason no amount of mapping
fixes.

The file is empty — three bytes in the committed copy, and those three bytes
are a UTF-8 BOM, not NVRAM; zero bytes in the current dump. Nothing has ever
written it. The Ghostbusters VPX table script drives it through VPinMAME
(`Const cGameName = "spagb_100"`, then `vpmInit`) and leaves scoring entirely to
the emulated ROM — but Stern Ghostbusters runs on **SPIKE**, and PinMAME has no
SPIKE driver. Its `src/wpc/` tops out at `sam.c`; there is no `spike.c`. With no
emulated CPU there is no NVRAM, so there is nothing to read and nothing to map.

The way out is a table swap, not a code change: several Stern SPIKE-era games
have VPX-native recreations that score in VBScript and persist to `VPReg.stg`.
**Deadpool on this cabinet is exactly that** — also a SPIKE game, working fine,
because `jpsdeadpool` is a script-based table. A script-based Ghostbusters would
land in `VPReg.stg` alongside it and be picked up by the existing STG tooling
with no new work. Worth confirming which table file is installed before acting
on this, since the diagnosis rests on that `.vbs` using VPinMAME.

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

- The only candidates that survived the two-dump rule were two ranges on
  `btmn_106`, and they are now dead too. Re-running with the cabinet's current
  file as a third image — which has a real score (`RAR` 17,228,210) in the
  Batman high-score table, so the bytes genuinely vary — leaves nothing. The
  same three-image run still rediscovers Addams Family's and Star Trek's
  documented regions, so the search had not gone blind.
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
do not necessarily keep their slots sorted.** Game of Thrones has 152,329,750
sitting in slot 9 above 4,000,000 in slot 13, in both the sample and the current
cabinet file. The initials still pair correctly with the values (the real,
non-round scores carry real initials; the untouched defaults carry `AAA`/`BBB`/…),
the list simply is not re-sorted on write. Rank has to be derived by sorting
what you read. That matches the direction already agreed for the score store —
rank is a query, not stored state — so it costs nothing here, but it would be a
real bug for anything that trusted the slot index.

Worth knowing when the score database starts ingesting: Guardians' leaderboard
still carries `TS1`/500, `TS2`/400 and `TS3`/300 from the August write-back
test, with two real games (2,536,740 and 2,426,040) above them. Those three are
test artefacts, not scores anyone set.

## Categories: what the API stores vs. what the machine has slots for

Extraction flattens: the ranked slots all become `category: null` and rank is
derived by sorting, because a stored rank goes stale the moment the order
shifts. Insertion has to go the other way — take a list of scores for a
category and decide which physical slot each one lands in — and *that* is where
"Grand Champion lobbed in with the high scores" could bite.

Two things had to be settled before this was safe. Neither was encoded in the
maps until now.

### Is the Grand Champion part of the main board, or its own thing?

It depends on the platform, and the machines answer it themselves.

**Stern SAM: one five-deep list, demotion included.** Between the two Walking
Dead dumps, `RAT`/76,492,350 arrived and took the top slot — and the previous
holder, `JDB`/75,000,000, moved *down* into the slot below rather than being
discarded. Pirates shows the same thing: `FRY`/213,747,750 and `RAT`/171,779,720
came in above the old top score, which slid from slot 0 to slot 2. The ROM
treats the Grand Champion as slot 0 of one sorted list.

That settles it, and not just as a nicety. If Grand Champion were its own
category, `JDB`/75,000,000 would have been recorded once as a Grand Champion and
then again as a main-board score — one achievement, two rows, and the dedup key
`(table, category, player, score)` would not catch it because the category
differs. Flattening is what *prevents* the duplicate.

**Williams WPC: structurally separate, but it doesn't matter.** The ROM
checksums them apart — on Addams Family the four ranked slots are one
checksum16 block (`0x1C61-0x1C82`) and the Grand Champion is its own
(`0x1C83-0x1C8C`). No dump here has a WPC Grand Champion being beaten, so
there's no demotion evidence either way. But since a WPC score doesn't migrate
between the two, treating them as one category can't produce the duplicate
above, and writing the top score to the Grand Champion slot with the next four
below produces a board the machine is happy with. One rule across all fifteen
tables beats mirroring an internal split that changes nothing downstream.

### The rule "high_scores → null, mode_champions → its label" was wrong

Not for Grand Champion — for the champions. Four groups on this cabinet are
ranked sub-leaderboards wearing per-slot labels:

| table | slots | should be |
|---|---|---|
| Star Trek: TNG | `Officer's Club #1..#4` | one category |
| Star Trek: TNG | `Q Continuum #1..#4` | one category |
| Medieval Madness | `King of the Realm #1..#4` | one category |
| Pirates | `Gauntlet Champ 1..3` | one category |

Applying the old rule would have created 15 categories where there are 4, each
one a leaderboard of exactly one entry, permanently frozen at whatever was in
that slot. Same bug as the Gauntlet, three more times.

And `King of the Realm` is a fifth case again: it is not ranked at all. All four
of its slots hold `KOP` with the *same* timestamp and counters of 1, 0, 0, 0 —
it's a dated history of the last four kings, so sorting it by value would be
meaningless. It needs its slot order preserved.

### So the maps now say it explicitly

Every map carries a `_pinballscores.categories` block: for each category, its
name (`null` for the main board) and the physical slots it owns, best first.

```json
{"name": null, "order": "ranked",
 "slots": ["Grand Champion", "First Place", "Second Place",
           "Third Place", "Fourth Place"]},
{"name": "Q Continuum", "order": "ranked",
 "slots": ["Q Continuum #1", "Q Continuum #2",
           "Q Continuum #3", "Q Continuum #4"]},
{"name": "King of the Realm", "order": "positional",
 "slots": ["King of the Realm #1", "..."]}
```

Extraction reads a category's slots and emits them with rank derived.
Insertion takes the category's rows from the API, sorts them (unless
`positional`), and fills the slots in order. `order: "ranked"` also says the
slot list is safe to re-sort; `positional` says it isn't.

`research/tools/nvmap.py` implements both directions — `read_category()` and
`write_category()` — and `validate_maps.py` now checks, on every table:

- every record belongs to exactly one category, exactly once
- every table has an unnamed category, so something maps to the main board
- ranked categories really are in descending slot order in real NVRAM (a
  mis-grouped set shows up immediately)
- a **full leaderboard insert** round-trips: hand `write_category` an
  out-of-order payload, confirm it reads back sorted, that the highest score
  landed in the machine's own top slot specifically, and that every checksum
  still validates

All 15 tables pass that against both the committed samples and the live cabinet
dumps. So: yes, confident — but it needed the categories block, and the answer
to your question would have been "no" for four tables without it.

One caveat that is not about the maps. Slot count is a hard ceiling: Star Trek's
Q Continuum has four slots, so a competition leaderboard of ten entries can only
put its top four on the machine. `write_category` refuses a payload longer than
the category rather than silently dropping the tail — the website can show all
ten, the cabinet shows four.

### What the maps hand the API (2026-08-20)

Checked against the live staging spec at `/openapi.json`, which already covers
more than expected: `value` is int64 on the wire (string above 2^53, never a
float), category keys and `merge-into` already exist, board aliases handle ROM
upgrades, `GET /api/scores?limit=` takes the machine's slot count directly and
returns `rank` computed server-side. So the CLI needs no catalog endpoint — the
maps are the catalog, and the API sorts the rest out. What the maps now carry
for it:

| field | why |
|---|---|
| `categories[].key` | stable identity; the `name` is ROM text and may be prettified later |
| `categories[].value_type` | the API's enum: `score`, `counter`, `duration`, `timestamp` |
| `categories[].value_unit` | durations only — the unit of the machine's own integer |
| `categories[].display_suffix` | the ROM's own unit wording, e.g. "Castles Destroyed" |
| `initials_padding` | what may be stripped from initials — see below |
| `map_version` | content hash, so rows from a buggy map can be found and retracted |

**Values are submitted as the machine's own integer.** The API takes durations
in whole milliseconds but accepts a `value_unit`, so Lord of the Rings' ring
timer — a 16-bit count of hundredths of a second — goes across as the raw
`60000` with `value_unit: "cs"` and is never scaled, converted or rounded on
the way in. `scale` in a map is presentation only: `read_field` returns the
stored integer and `NvramMap.display()` renders "600 seconds" for humans.

That distinction is load-bearing. `scale` used to be applied on read but not on
write, so the ring timer read as `600.0` and wrote back as `6` — the same shape
of bug as the float32 rounding that turned 130,296,090 into 130,296,088.
`validate_maps.py` now round-trips every value field and rejects any that reads
back as a float.

**Initials are exactly what the machine stored.** A space is one of the
characters a player can pick, so `' NF'` and `'NF '` are different names, and
the reader must not tidy either away. It used to: Addams Family's fourth place
is `'CG '` in NVRAM and was being read as `'CG'`. Staging shows the same damage
already done — Batman's fourth place is `' NF'` on the cabinet and `'NF'` in the
database.

The one genuine exception is Whitestar, whose 10-byte field holds names of any
length (the reference corpus has entries from 2 to 10 characters), so trailing
spaces after a short name are padding and are not recoverable anyway. Hence
three declared conventions rather than one blanket rule. Which convention a
platform uses follows from how it takes initials and cannot be inferred from
the bytes — `validate_maps.py` checks the declaration is *honoured* (a stray
`strip()` reappearing is caught), not that it is correct.

**Both gaps raised against the API have since been closed** (verified against
the live spec, not taken on trust):

- Durations are now whole milliseconds with an optional per-entry `value_unit`
  (`ms`/`cs`/`ds`/`s`/`m`/`h`), and anything finer than a millisecond is
  rejected rather than rounded. The one existing duration row was migrated
  (`lotr` / Destroy Ring Champion / `EYE`, 600 → 600000).
- `map_version`, `extractor_version` and `source_slot` are accepted on
  submission and explicitly documented as **never part of the dedup key** — so
  resubmitting a board under a corrected map is still a batch of duplicates,
  not a new board. `DELETE /api/admin/scores` retracts a run in bulk, dry-run
  by default (`confirm=false`) and requiring a `reason`, with
  `GET /api/admin/provenance` to list runs, `GET /api/admin/retractions` as an
  audit log and a `restore` endpoint to undo one. That is more than was asked
  for; the restore path in particular makes a mistaken retraction survivable.

## One upstream map is wrong — and one of ours was

**`twd_156` pairs every score with the wrong initials.** Upstream reads the
Walking Dead record as "score at *X*, initials at *X+8*". The record checksum
disagrees: it covers `X-0x18 … X+3`, which puts the record boundary 0x18 bytes
*before* the score, exactly matching the layout confirmed by ROM disassembly on
`smanve_101` — initials at the start of the record, score at `+0x18`. Upstream's
pairing is shifted by one record, so it reports each score against the *next*
slot's initials, and the last slot's initials read as an empty string sitting
past the end of the table. Upstream's own `twd_160h` map uses the correct
layout, which is a second, independent sign that `twd_156` is the odd one out.

The fresh cabinet dump settles it from a third direction. Three new scores have
landed on Walking Dead since the sample was taken, pushing the board down. Take
the two scores present in *both* dumps and ask who owns them:

| pairing | 75,000,000 | 55,000,000 |
|---|---|---|
| ours | `JDB` → `JDB` | `LFS` → `LFS` |
| upstream `twd_156` | `LFS` → `LFS` | `T` → `RAT` |

A score already sitting on the leaderboard cannot change owner without being
re-earned, and 55,000,000 is not a score anyone re-earned to the digit. Under
upstream's pairing it changes hands anyway; under ours nothing moves that
shouldn't. (Upstream also has `DAV` — who is the Walkers Killed champion with
25 kills — simultaneously holding a high score that changes value between dumps.)
On this cabinet the Grand Champion is now `RAT` with 76,492,350, and `JDB`
still owns the 75,000,000 that was top last time.

**We had `smanve_101`'s "Best Combo Champion" wrong, and upstream had it
right.** An earlier session widened that field from upstream's 1 byte at `+0x1a`
to 4 bytes at `+0x18`, reasoning that a 1-byte counter could not hold the value
327,681. The reasoning was circular — it assumed 327,681 was the value. The
record's four bytes at `+0x18` read `01 00 05 00`: two 16-bit fields, and it is
the second one that counts combos. The new dump shows a real player (`FRY`)
taking the slot and the count going **5 → 7**, while the 4-byte reading goes
327,681 → 458,753. Five combos then seven is a combo count; 458,753 is not.
Reverted to upstream's field, with a note recording what the other half of
those bytes is. The drafted upstream PR from that session should drop this
change and keep only the `checksum16` sections.

## What is still open

- **A decision on Ghostbusters** — swap in a script-based table, or drop it
  from the cabinet's table list. Nothing here can be done in code.
- **Lowering the demo scores on Star Wars, Lord of the Rings, Simpsons and
  Addams Family**, so real play can actually reach the board. Blocked on the
  same hardware write test as everything else below.
- **Confirming the Whitestar / Data East negative result on hardware** — one
  patched score on `lotr` and one on `btmn_106`, as described above. This is
  now the only unverified claim in the whole set.
- **The SAM real-hardware write test is still pending** from the previous
  session. `research/test-output/` holds correctly-checksummed test files for
  `smanve_101` and `xmn_151h` that have never been tried, since the checksum
  was cracked after the last cabinet visit. Note that Spider-Man has picked up
  real scores since those were generated — regenerate against the current file
  rather than deploying the old ones, or a week of `RAT`'s play goes back.
- **Champion slots not yet earned won't be in the SAM maps.** Re-running
  `sam_record_scan.py` on the current dumps found none, but it is worth
  repeating whenever fresh files show up — a first-ever champion on Avengers
  would need a new map entry.

The committed sample data in `ScoresData/nvram/` was deliberately left as-is;
it is what the existing solution's fixtures point at, and swapping in the live
dumps is a separate call. If you'd rather the default `validate_maps.py` run
exercised real play data instead of factory defaults, say so and they can be
replaced.

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
