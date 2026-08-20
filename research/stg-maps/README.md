# STG maps

One map per Visual Pinball X table on the cabinet. See
[../TABLE-MAPPING.md](../TABLE-MAPPING.md) for context.

VPX-native tables don't emulate a ROM, so there is no NVRAM and nothing to map
in memory. They persist state through `User/VPReg.stg`, an OLE Compound File:
one storage per table, one stream per setting, each stream a UTF-16LE string.
Scores are decimal strings and initials are plain strings — no checksum, no
factory default to revert to, so a write is just a stream rewrite.

These maps exist because the naming is per-table-script convention rather than
a standard. `HighScore3` pairs with `HighScore3Name` on all three tables, but
champion fields follow no rank pattern (`HighScoreXandar` /
`HighScoreXandarName`), and the number of ranked slots varies (4 on Deadpool,
5 on Guardians, 16 on Game of Thrones).

Two things to know:

- **Champion labels are the table script's own field names, verbatim** — `CB`,
  `IMMO`, `Xandar`. A prettier display name would be a guess, and because the
  label becomes the category key in the score database, correcting a guess
  later would split one category into two.
- **Slot number is not rank.** These table scripts do not necessarily re-sort
  on write; Game of Thrones currently holds 152,329,750 in slot 9 and
  4,000,000 in slot 13. Derive rank by sorting the values you read.

Regenerate or re-check against a fresh file with:

```sh
python3 ../tools/build_stg_maps.py --stg <VPReg.stg>          # rebuild
python3 ../tools/build_stg_maps.py --stg <VPReg.stg> --check  # verify only
python3 ../tools/build_stg_maps.py --stg <VPReg.stg> --list   # dump everything
```

`--list` prints every storage and stream in the file, which is also how to spot
a table that has been added to the cabinet but not to `CABINET_TABLES` in that
script.
