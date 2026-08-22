# Adding a table to the cabinet

A runbook. [TABLE-MAPPING.md](TABLE-MAPPING.md) records what we found and why;
this is what to actually do when a new table shows up. Most tables take about
ten minutes — the long paths exist for the rare ones.

Everything below runs from the repo root.

**Prerequisites.** The NVRAM tools need nothing but Python 3 — no third-party
packages, deliberately, so they work on a bare box. The two STG tools need
`olefile` to read the Compound File. If `pip` isn't available (it isn't in the
Coder workspace), fetch it by hand:

```sh
mkdir -p /tmp/pylibs && cd /tmp/pylibs
curl -sL -o olefile.zip https://files.pythonhosted.org/packages/source/o/olefile/olefile-0.47.zip
python3 -c "import zipfile; zipfile.ZipFile('olefile.zip').extractall('.')"
mv olefile-0.47/olefile ./olefile
# then prefix STG commands with PYTHONPATH=/tmp/pylibs
```

---

## Step 0 — work out which kind of table it is

```sh
ls ScoresData/nvram/            # VPinMAME tables write a <rom>.nv here
python3 research/tools/build_stg_maps.py --stg <VPReg.stg> --list | cut -d/ -f1 | sort -u
```

| what you see | what it is | go to |
|---|---|---|
| a storage in `VPReg.stg` | VPX-native table, scores in VBScript | [Path A](#path-a) |
| a `<rom>.nv` that grows and changes | VPinMAME table | [Path B](#step-1--find-out-if-it-is-already-mapped) |
| a `<rom>.nv` that is empty or 0 bytes | **stop — check the hardware is emulated** | below |
| neither | Pinball FX3 or similar; out of scope | — |

**An empty `.nv` usually means PinMAME cannot run that game at all.** This is
what happened with Ghostbusters: the table script drives it through VPinMAME
(`cGameName = "spagb_100"`), but Stern Ghostbusters is SPIKE hardware and
PinMAME has no SPIKE driver — its `src/wpc/` tops out at `sam.c`. No emulated
CPU means no NVRAM, so there is nothing to map and no amount of work here will
change that. Check before spending time:

```sh
curl -s "https://api.github.com/repos/vpinball/pinmame/contents/src/wpc" \
  | python3 -c "import json,sys; print(' '.join(sorted(x['name'] for x in json.load(sys.stdin))))"
```

If the platform isn't in there, the only fix is a different table file — a
VPX-native recreation that scores in VBScript and saves to `VPReg.stg`, which
is why Deadpool works on this cabinet and Ghostbusters doesn't. Then it's
Path A.

---

## Path A — VPX-native (STG)

The easy one. There is no ROM, no memory map and no checksum.

1. Play a game on the table so it writes its scores at least once.
2. See what it stores:
   ```sh
   python3 research/tools/build_stg_maps.py --stg <VPReg.stg> --list | grep '^<storage>/'
   ```
3. Add the storage name to `CABINET_TABLES` in
   `research/tools/build_stg_maps.py`, with a human title.
4. Rebuild and check:
   ```sh
   python3 research/tools/build_stg_maps.py --stg <VPReg.stg>
   python3 research/tools/build_stg_maps.py --stg <VPReg.stg> --check
   ```

The generator handles `HighScoreN` / `HighScoreNName` pairs and champion fields
like `HighScoreXandar` automatically. If the table uses some other naming, teach
`RANKED` / `CHAMPION` in that script about it rather than hand-writing the map.

Two things to leave alone: champion labels stay as the table script's own field
names (`CB`, `IMMO`), because a prettier invented name becomes a different
category key later; and don't assume slot order is rank, because these scripts
don't always re-sort.

Skip to [Step 5](#step-5--finish).

---

## Step 1 — find out if it is already mapped

Most VPinMAME tables are. Check upstream before doing any work:

```sh
git clone --depth 1 https://github.com/tomlogic/pinball-memory-maps /tmp/pbmm
python3 -c "import json;print(json.load(open('/tmp/pbmm/index.json')).get('<rom>'))"
```

**Hit → Path B. Miss → Path C or D.**

Also grab the reference test corpus now — you will want its second dump of your
ROM later, and it covers 450+ games:

```sh
git clone --depth 1 https://github.com/tomlogic/py-pinmame-nvmaps /tmp/pynv
ls /tmp/pynv/test/nvram/<rom>.nv
```

---

## Path B — upstream already maps it

1. Add an entry to `UPSTREAM_TABLES` in `research/tools/build_maps.py`: the ROM
   name as the key, `source` being the path inside the upstream repo, a `title`,
   and a `status` line saying what you verified.
2. Build and validate:
   ```sh
   python3 research/tools/build_maps.py --upstream /tmp/pbmm
   python3 research/tools/validate_maps.py --rom <rom> --verbose
   ```

Then [Step 2](#step-2--integrity-can-you-write-to-it), because upstream maps
are reliable about *fields* and patchy about *checksums* — that is the single
most common thing missing from them.

---

## Path C — Stern SAM, not mapped upstream

Stern SAM is the happy case: the per-record checksum doubles as a signature, so
the whole record table falls out of a scan with no ROM and no guessing.

```sh
python3 research/tools/sam_record_scan.py ScoresData/nvram/<rom>.nv
```

You get every record's address, initials and value. Then add an entry to
`SAM_TABLES` in `research/tools/build_maps.py`:

- `base` — the first record's address from the scan
- `records` — one `(group, label, value_key)` per record, **in scan order**.
  First five are normally `high_scores` (Grand Champion, First…Fourth Place);
  the rest are `mode_champions`. `value_key` is `"counter"` for things that
  count (combos, kills) and `"score"` otherwise.
- `roms` — every ROM revision this layout covers

Two things the scan will not tell you:

- **Champion labels.** See [Step 3](#step-3--naming-the-champion-slots).
- **Slots nobody has earned yet.** Unwritten records are 0xFF-filled and don't
  appear. Re-run the scan whenever fresh dumps arrive and add what shows up.

Watch for a record whose value isn't a plain 4-byte integer — Spider-Man's
"Best Combo Champion" packs two 16-bit fields into those bytes and only the
second one counts combos. If a value looks absurd (458,753 combos), that's the
tell. Use an `overrides` entry, as that table does.

---

## Path D — some other platform, not mapped upstream

Rare, and the only genuinely hard path. You need two dumps of the same ROM with
**different scores in them** — one from the machine now, one after somebody
plays, or one from the reference corpus.

Locate the fields by elimination:

```sh
cmp -l a/<rom>.nv b/<rom>.nv | wc -l          # how much moved at all
```

Then look for the score block: on the 6809/6808 platforms scores are BCD (each
byte holds two decimal digits, so `0x35 0x00 0x00 0x00` reads as 35,000,000),
initials are plain ASCII in a separate block, and both blocks are contiguous
with a fixed stride. A sibling game's map from the same platform directory
upstream is the best possible starting point — the layout is usually identical
and only the base address moves.

Sanity check before believing anything: the values must come out in descending
order, and the initials must be printable. `validate_maps.py` checks both.

---

## Step 2 — integrity: can you write to it?

Reading needs field offsets. **Writing needs to know whether the ROM validates
that region on boot**, because a stale checksum makes the ROM throw the record
away and restore its factory default — silently, and only for the record you
touched. That cost a week the first time.

What's known so far:

| platform | scores protected? | how it's expressed |
|---|---|---|
| Williams WPC | yes | `checksum16` block over the ranked slots, another over the Grand Champion |
| Stern SAM | yes | `checksum16` per record, `{start: <record>, length: 30}` |
| Whitestar | no sum checksum found | — |
| Data East v3 | no sum checksum found | — |
| VPX / STG | none | — |

For a platform not in that table, hunt for it — with **two or more dumps**,
because a single image throws off dozens of coincidental matches:

```sh
python3 research/tools/find_checksums.py --cover 0x1C61-0x1C80 --size 0x2000 \
    a/<rom>.nv b/<rom>.nv
```

Run a **positive control first** on a table whose checksum is already known, to
prove the search works before trusting a negative:

```sh
python3 research/tools/find_checksums.py --cover 0x1C61-0x1C80 --size 0x2000 \
    ScoresData/nvram/taf_l7.nv /tmp/pynv/test/nvram/taf_l7.nv
# expect exactly: {"start": "0x1C61", "end": "0x1C82"}
```

A negative result is a real result, but only if the bytes inside the range
actually differ between your dumps — otherwise the test had no power. Confirm a
"no checksum" finding on hardware before relying on it: patch one score, reboot
the machine, see if it sticks.

Whatever you find goes in the map as a `checksum8` / `checksum16` section.
`validate_maps.py` then verifies it against untouched NVRAM, which is strong
evidence you got it right: the ROM wrote those bytes, you only predicted them.

---

## Step 3 — naming the champion slots

In order of preference:

1. **An upstream map for a sibling ROM of the same game.** Free, and usually
   right — but check the alignment, because upstream's `twd_156` pairs every
   score with the next slot's initials.
2. **The game ROM.** Reliable, and quick — see below.
3. **The machine itself.** The operator menu's high-score reset list and attract
   mode both show the names.

Do not invent a label. It becomes the category key in the score database, and
correcting it later splits that category's history in two.

### Reading labels out of a Stern SAM ROM

Stern SAM ROMs carry the labels as plain ASCII, next to a second table of the
default initials each slot is seeded with — **both in NVRAM record order**.
That second table is what lets you verify the ordering instead of assuming it.

```sh
python3 - <<'EOF'
import re
d = open('<rom>.bin','rb').read()
for m in re.finditer(rb'GRAND CHAMPION', d):
    lo = m.start()
    for s in re.finditer(rb'[\x20-\x7e]{4,}', d[lo:lo+0x400]):
        print("0x%08X  %s" % (lo+s.start(), s.group().decode()))
    print('---')
EOF
```

Look for the run that reads `GRAND CHAMPION`, `HIGH SCORE #1..#4`, then the
champion names, then a list of surnames. The surnames are the seed initials.

**Verify before believing it:** compare that surname list against the initials
still sitting in slots nobody has beaten. On X-Men every slot was untouched, so
all six read `G S, L R, J R, D T, P P, YAN` against the ROM's `STERN, ROPP,
ROTHARMEL, THORNE, POWERS, YANCY` — a 1:1 match that pins the ordering. If the
lists don't line up, your record order is wrong.

Keep ranked slots named `Grand Champion` / `First…Fourth Place` like the other
maps, and use the ROM's own wording for champions.

---

## Step 4 — categories and the submission contract

`build_maps.py` derives `_pinballscores.categories` automatically: the ranked
slots become one unnamed category (the main board), slots sharing a base label
(`Q Continuum #1..#4`) become one named category, and anything else gets its
own. Usually there is nothing to do.

Three cases need help:

- **A group that isn't a ranking.** Medieval Madness's `King of the Realm` is a
  dated log of the last four kings, newest first. Add it to
  `POSITIONAL_CATEGORIES` and it keeps its slot order; that also blanks its
  empty slots with zero rather than the marker value, which is what an untouched
  machine holds there.
- **A record that is more than one number.** A king carries the date he was
  crowned and how many times he has been, in fields beside the value. Declare
  them in `CATEGORY_EXTRAS` as `metadata_fields` (API field name → map key) and
  they are read, submitted and written back with the row; `value_field` pins
  which descriptor the value itself comes from when the default priority
  (score → counter → timestamp) would pick the wrong one. Write only part of a
  record and the cabinet shows the new holder's name over the old holder's date.
- **A group whose labels don't share a base** but which is really one
  leaderboard. Nothing here needs it yet; you'd extend `derive_categories`.

`value_type` and `value_unit` are derived from the field (`counter` → counter,
`units: seconds` → duration, `wpc_rtc` → timestamp). If a duration turns up in
a unit other than seconds, add it to `DURATION_UNITS` so the raw integer can be
submitted with its own unit rather than being scaled.

Remember: **`scale` is presentation only.** Values are read and submitted as the
machine's own integer. Anything else reintroduces the float rounding that turned
130,296,090 into 130,296,088.

---

## Step 5 — finish

```sh
python3 research/tools/validate_maps.py --nvram-dir <dir> --verbose   # or --check for STG
```

Green means: fields decode, high scores are descending, initials survive a byte
round-trip, every declared checksum already validates in untouched NVRAM, every
value round-trips as an integer, every record belongs to exactly one category,
and a full out-of-order leaderboard insert lands in the right physical slots
with the checksums recomputed. That is the definition of done.

Optionally prove the write path end to end on a copy:

```sh
python3 research/tools/patch_score.py research/nvram-maps/<rom>.map.json \
    in.nv out.nv "Grand Champion=ZZZ:123456" --dry-run
```

Then register it with the score API (`POST /api/admin/tables`), and if the ROM
gets upgraded later use a board alias rather than a new table id, so the
leaderboard's history doesn't fork.

---

## When you actually need to disassemble a ROM

Almost never, now. The Stern SAM checksum is solved and generic across that
platform, so the remaining reasons are:

- a **new platform** whose score checksum resists `find_checksums.py`
- **labels** on a game with no upstream map — but that's string search, not
  disassembly (Step 3)

If you do need it, `rom-analysis/NOTES.md` has the full trail from the
Spider-Man work: the SAM memory map, the link-time base address, the checksum
utility functions, and the dead ends already ruled out. It also lists the
toolchain (`binutils-arm-none-eabi`, `radare2`, `capstone`, Ghidra headless).

**Handling rule: ROMs never go in git.** They are Magnus's own legally-owned
copies, supplied for reverse engineering only — keep them in `/home/coder/` or
`/tmp/`, and don't commit extracted binaries, however small.
