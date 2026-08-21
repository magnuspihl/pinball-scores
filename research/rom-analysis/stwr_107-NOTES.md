# stwr_107 (Star Wars, Data East) ROM disassembly notes (2026-08-20)

> **Solved.** Star Wars silently reverted every externally-written high score
> because it keeps an undocumented shadow copy of the leaderboard and restores
> from it whenever the live copy looks "worse" than the shadow. The fix is
> implemented in `research/tools/reset_demo_scores.py`'s
> `fix_stwr_107_shadow_copy()` — mirror the shadow bytes to match whatever the
> live table was just set to. Kept here for the toolchain and the ruled-out
> dead ends, same purpose as `NOTES.md` (the smanve_101/Stern SAM writeup).

ROM: `starcpua.107` (from `stwr_107.zip`), provided by Magnus for this
reverse-engineering only (kept outside the repo/git tree — do not commit;
lives at `/home/coder/stwr_107.zip` and extracted copies under
`/tmp/stwr-rom-analysis/`, scratch space, not persisted).

## Confirmed facts

- **CPU is Hitachi HD6309 in native mode, not plain Motorola 6809.** The byte
  immediately after the reset vector's hardware-init calls (`0x40db`) is
  `0x61` — illegal on plain 6809, but a real 6309 bit-manipulation opcode
  (`OIM` indexed; the 6309-exclusive family is `OIM/AIM/EIM/TIM` at direct
  `0x01/0x02/0x05/0x0B`, indexed `0x61/0x62/0x65/0x6B`, extended
  `0x71/0x72/0x75/0x7B` — all illegal/undefined on stock 6809). Confirmed by
  reading the raw ROM bytes directly, not inferred.
- **Ghidra's H6309 language support (12.1.2) does not implement OIM/AIM/EIM/TIM
  at all** — confirmed by reading `Ghidra/Processors/MC6800/data/languages/`'s
  6309 slaspec/sinc sources directly, no such mnemonics anywhere in the
  opcode-construct tables. Its decompiler chokes on functions containing these
  opcodes ("Unable to resolve constructor" pcode errors). **Use Capstone
  (`CS_ARCH_M680X`, `CS_MODE_M680X_6309`) instead** — it has full 6309
  support and disassembles this ROM cleanly. Ghidra is still fine for
  anything that doesn't hit a 6309-only opcode (most of the ROM).
- **The high-score table's real memory layout, confirmed against
  `research/nvram-maps/stwr_107.map.json`:** each of the 6 ranked slots'
  5-byte BCD score is non-contiguous — one "leading digit" byte at
  `$1694`-`$1699` (one per rank) plus a 4-byte body at `$167c`-`$1693`
  (rank-major, 4 bytes each). Initials are 3 contiguous bytes per rank at
  `$16ba`-`$16c9`. This matches the map exactly; no correction needed there.
- **Found and ruled out two false leads before the real mechanism** (worth
  recording so a future session doesn't re-chase them):
  - `FUN_53de`/`FUN_5672` (found via literal-address cross-referencing against
    the map's declared addresses, using Ghidra's *plain 6809* disassembly —
    which, per the point above, silently mis-decodes around any 6309-only
    opcode elsewhere in the ROM, so this scan's results were built on an
    unreliable disassembly from the start). Turned out to be bonus-multiplier
    award-threshold comparison logic (`$1668`/`$166d`/`$1672`/`$1677`, a
    stride-5 table numerically adjacent to but structurally unrelated to the
    real leaderboard), not score validation.
  - A 3-byte state at `$0593`-`$0595`, seeded to `0x7f7f7f` at boot and mixed
    ~26 times by a routine at `0x41d5` (XOR + rotate — checksum-shaped). It
    never reads any *other* memory as input, so it can't be a checksum over
    external data; it's almost certainly a PRNG warm-up (attract-mode
    randomness), not high-score integrity.
- **The actual mechanism, at `0xc07d`-`0xc103` (Capstone/6309 disassembly):**
  ```
  c07d: LDA  $1694        ; live table's rank-1 leading BCD digit
  c080: CMPA $1e37        ; compare against a shadow/reference copy
  c083: BCS  $c0a7         ; if live < shadow (unsigned), go restore from shadow
  ... (a secondary, separate gate on $1e3d/$16d6-$16d9, not fully explored -
       doesn't gate on score value as far as traced)
  c0a6: RTS                ; live >= shadow: keep the live table as-is
  c0a7: JSR  $c0bf         ; live < shadow: restore rank 1 in full...
  c0aa: JSR  $c0e4         ; ...and every rank's leading digit
  ```
  `$c0bf` copies `$1e37→$1694` (leading digit), `$1e1f-$1e22→$167c-$167f`
  (rank-1 score body), and **ROM-constant** `$a327-$a329→$16ba-$16bc` (rank-1
  initials — those three ROM bytes spell `CNH`, which is exactly the
  Grand-Champion-equivalent demo initials `research/TABLE-MAPPING.md` already
  documented for this table: `CNH 350,000,000`, an independent confirmation
  this is the right code). `$c0e4` copies `$1e38-$1e3c→$1695-$1699` (every
  other rank's leading digit only — those ranks don't get their body or
  initials restored by this particular check, just clamped so their leading
  digit can't look lower than the shadow's).
- **The shadow bytes are themselves live, RAM-backed NVRAM, not a ROM
  constant** — confirmed by reading `ScoresData/nvram/stwr_107.nv` directly:
  `$1e37 == $1694 == 3` in the untouched sample (both equal, both encoding the
  leading digit of the ~350,000,000 factory default), while the *ROM binary's*
  raw byte at file-offset `0x1e37` is `0x00` — proving `$1e37` is a real
  memory cell whose content comes from the save file, not baked into the ROM
  image. So this is a genuine on-machine redundancy/anti-corruption feature
  (a second copy of the top score kept elsewhere in NVRAM, checked at boot),
  not something bolted onto the emulator or the map.
- **This explains every prior test failure on this table without exception:**
  the demo-reset marker's value (1, or later 10-60) is always far below the
  shadow's compiled-in factory default (100,000,000-350,000,000 range), so
  `CMPA`/`BCS` fired every single time regardless of initials character,
  tie-ness, or exact value chosen — none of those were ever the actual
  variable. Writing a low value to the live table can never survive on this
  ROM unless the shadow is *also* lowered to match.

## The fix

`research/tools/reset_demo_scores.py`'s `fix_stwr_107_shadow_copy()`: after
writing the normal blank/1 marker to the live table, copy `$1694→$1e37` and
`$167c:$1680→$1e1f:$1e23` (rank 1, full mirror) and `$1695..$1699→$1e38..$1e3c`
(ranks 2-6, leading digit only) so live and shadow always end up equal — the
`BCS` branch requires strict less-than, so equal never triggers a restore.
Verified in `research/demo-reset/nvram/stwr_107.nv`: `validate_maps.py` passes,
and the live/shadow bytes are confirmed byte-identical after the reset.

**Not yet confirmed on real hardware** — this is the first real chance at it
sticking, but it's a prediction from static analysis, not a proven fact until
Magnus tests it on the cabinet.

## What's still open

- The secondary gate referenced at `$1e3d`/`$16d6`-`$16d9` (between the score
  comparison and the `RTS`/restore branches) was not fully traced — it didn't
  block the marker in testing so far as reasoned from the disassembly, but it
  wasn't confirmed to be harmless either. Worth a look if the shadow fix
  doesn't fully hold on real hardware.
- **Update:** `btmn_106` (Batman, same "Data East version3" family) does show
  the same *shape* of duplicate data — `ScoresData/nvram/btmn_106.nv` has a
  byte-for-byte copy of its entire 6-rank score body at `0x1e1d-0x1e34`,
  mirroring the live table at `0x1d98-0x1daf`, found by plain byte comparison
  with no ROM. Mirrored the same way in `fix_btmn_106_shadow_copy()`. Unlike
  the stwr_107 fix above, **this one is not disassembly-confirmed** — no
  Batman ROM was available to verify the actual gating code exists/behaves as
  inferred. Whether it holds on real hardware is the next test. If Star Wars' fix doesn't
  generalize, Batman may need its own ROM and its own trace.

## Reproducing

```sh
# capstone disassembly, correct for this ROM (Ghidra's H6309 support is incomplete)
python3 -m venv /tmp/cs-venv && /tmp/cs-venv/bin/pip install capstone
/tmp/cs-venv/bin/python3 /tmp/stwr-rom-analysis/cs_disasm.py <start_hex> <end_hex> [...]

# regenerate the fixed demo-reset file (applies the shadow fix automatically for stwr_107)
python3 research/tools/reset_demo_scores.py
```
