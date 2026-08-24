# STG maps

One map per Visual Pinball X table on the cabinet. See
[../TABLE-MAPPING.md](../TABLE-MAPPING.md) for context, and
[../ADDING-A-TABLE.md](../ADDING-A-TABLE.md) (Path A) to add another.

VPX-native tables don't emulate a ROM, so there is no NVRAM and nothing to map
in memory. They persist state through `User/VPReg.stg`, an OLE Compound File:
one storage per table, one stream per setting, each stream a UTF-16LE string.
Scores are decimal strings and initials are plain strings — no checksum, no
factory default to revert to, so a write is just a stream rewrite.

These maps exist because the naming is per-table-script convention rather than
a standard. `HighScore3` pairs with `HighScore3Name` on all three tables, but
champion fields follow no rank pattern (`HighScoreXandar` /
`HighScoreXandarName`), and the number of ranked slots varies (4 on Deadpool,
5 on Guardians and on Game of Thrones).

Three things to know:

- **The key is identity; the label is only display text.** A champion's key is
  the slugified label by default, so renaming a label in a map renames the
  category and strands the rows already stored under the old key. When a table's
  real titles are learned after it has been submitting — Guardians' `CB` turned
  out to be "Cherry Bomb Multiball Champion" — declare the key explicitly as the
  third element of the layout entry and let the label change freely. Display
  names can also just be set on the website, which the CLI never reads.
- **A numbered stream is not necessarily a rank.** Game of Thrones names all
  fifteen of its records `HighScoreN`, but only 1–5 are the ranked board:
  `HighScore6`–`HighScore15` are one-slot champions that the attract mode names
  (Stark, Baratheon, … Iron Throne), and `HighScore16` is unused. Nothing in the
  file says so, so the layout is declared in `SLOT_LAYOUTS` in
  `../tools/build_stg_maps.py`; it was mapped on the cabinet by writing a
  sentinel into each slot and reading the name it appeared under.
- **Slot number is not rank on the board either.** These table scripts do not
  necessarily re-sort on write — Guardians holds its best score in slot 5 —
  so derive rank by sorting the values you read.

Like the NVRAM maps, each file carries a `_pinballscores.categories` block
giving the category → ordered-slots rollup the score API stores: the numbered
slots are one unnamed category (the main board), and each champion field is a
category of its own. That is what makes insertion well-defined — see
[../TABLE-MAPPING.md](../TABLE-MAPPING.md).

Regenerate or re-check against a fresh file with:

```sh
python3 ../tools/build_stg_maps.py --stg <VPReg.stg>          # rebuild
python3 ../tools/build_stg_maps.py --stg <VPReg.stg> --check  # verify only
python3 ../tools/build_stg_maps.py --stg <VPReg.stg> --list   # dump everything
```

`--list` prints every storage and stream in the file, which is also how to spot
a table that has been added to the cabinet but not to `CABINET_TABLES` in that
script.
