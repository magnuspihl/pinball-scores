# Data East checksum investigation (stwr_107 + btmn_106), in progress 2026-08-20

> **Not solved yet.** This documents a real structural breakthrough (found via
> the Batman ROM, cross-referenced back to Star Wars) that overturns the
> earlier `stwr_107-NOTES.md` "shadow copy" theory — that theory was real but
> incomplete, and doesn't explain why the shadow itself got reset on real
> hardware. The actual mechanism looks like a genuine checksum, not found by
> the original `find_checksums.py` black-box search. Algorithm not yet
> cracked. Read `stwr_107-NOTES.md` first for the shadow-copy background this
> builds on.

## Why the "mirror the shadow" fix failed on real hardware

Magnus tested the `research/tools/reset_demo_scores.py` fix (mirror shadow to
match live) on `stwr_107` — it still reverted, and crucially **the shadow
bytes we wrote also got reset back to factory values**, not just the live
table. That ruled out the original theory (shadow = simple externally-settable
reference) and meant something re-derives or re-validates the shadow itself.

## The Batman ROM cross-reference (2026-08-20, Magnus supplied `btmn_106.zip`)

Combined image: `b5_a106.128` (16KB) mapped at `0x4000-0x7FFF`, `c5_a106.256`
(32KB) mapped at `0x8000-0xFFFF`. Reset/SWI/NMI vectors all point to `0x4002`
(same "shared cold-start" pattern as Star Wars' `0x40a0`). Confirmed HD6309
native mode again (same CPU family, same Capstone tooling from `stwr_107`'s
investigation).

**Found Batman's shadow-copy writes easily** (unlike Star Wars) via a wide
literal-address scan: six repeated blocks at `0xb868`, `0xb8ef`, `0xb976`,
`0xb9fd`, `0xba84`, `0xbb0b`, each writing a 12-address run
(`$1e1d,$1e1f,$1e21,...,$1e33`) — one block per rank. **Traced backward to
find these are called from `0x515b` (boot-time cold-start init) and
`0xb824`** (a 6-way dispatcher selecting one of the six blocks based on a
counter at `$1e44`), which is itself called from **two places**: once right
after the `0x515b` cold-start sequence (`0x5179`), and once from what looks
like a menu/command dispatcher (`0x6548`, gated on `cmpx #$1e44`) — plausibly
the "Restore High Score" operator-menu adjustment the PinballInfo forum thread
mentioned.

**The real breakthrough: found a genuine multi-region checksum dispatcher**
at `0x50ae-0x50fa`, called from `0x513b` (`bsr $5140`, itself part of the
`0x5100-0x513b` boot sequence, gated behind a `$1eff` RAM self-test — this
whole chain is very likely the "cold start / factory init" path, either for
genuinely blank NVRAM or an explicit operator reset, not necessarily every
normal boot — not yet confirmed which). The dispatcher calls a shared checker
(`0x50fb`, tail-jumping into `0xd50f`) **six times**, once per protected
region, setting a start-pointer at `$c9` before each call:

| region start (`$c9`) | what it is |
|---|---|
| `0x1d88` | unknown, just above the live score body |
| `0x1d98` | **the live score table body** (First's score, per the map) |
| `0x1db6` | unknown, near the leading-digit bytes |
| `0x1dca` | **the live initials** (First's initials, per the map) |
| `0x1e00` | unknown |
| `0x1e64` | unknown, conditionally checked (gated on a ROM byte at `$b37a`) |

**This is almost certainly the actual validation Magnus's test tripped** —
not (only) the shadow-copy value comparison from `stwr_107-NOTES.md`, but a
real checksum over the live table's own bytes, which our external write never
recomputed because we didn't know it existed. This would also explain why the
shadow got reset: the checksum failing triggers the same cold-start chain that
reloads the shadow from ROM, as a side effect of a full "everything's
suspect, start over" recovery — not because the shadow itself is separately
checksummed.

## Correction (2026-08-20, later same session)

The "genuine multi-region checksum at `0xd50f`" claim above is **retracted** —
traced its callees (`0xdd53`, `0xdd5d`, etc.) further and they're clearly
loading string pointers and jumping into a shared error/diagnostic *message
display* routine (`LDU #<string ptr>; JSR $f4bc; ...; LDB #<error code>`), not
computing a checksum. Misread dense, unfamiliar 6309 bit-manipulation code as
checksum arithmetic when it wasn't. Leaving this in the record as a caution:
manual disassembly reading of this density of code is error-prone, and this
session produced at least one confident-but-wrong claim before this
correction.

## Switching to empirical testing: MAME, not just static reading (2026-08-20)

Given the demonstrated error risk above, switched approach. Stock Ubuntu MAME
0.264 (`apt-get install mame`) **includes native, complete drivers for both
`btmn_106` (exact ROM match to Magnus's dump) and `stwr_106`** (Magnus's zip
has `starcpua.106` alongside `.107`; MAME's driver wants `.106` specifically).
Both need `bsmt2000.bin` (a shared, generic sound-chip firmware blob, not
part of either game dump) — used an 8192-byte all-zero dummy file to satisfy
MAME's file-presence check (not the real copyrighted firmware; sound doesn't
matter for this investigation, only whether the CPU side boots, which it
does with a checksum-mismatch warning that doesn't block execution).

**Confirmed MAME's raw NVRAM file format is byte-identical to PinMAME's `.nv`
format minus the 46-byte trailer** — verified by loading
`research/demo-reset/nvram/stwr_107.nv`'s first 8192 bytes directly into
MAME's `~/.mame/nvram/stwr_106/decocpu_nvram` and reading it back through
`research/tools/nvmap.py` with the existing `stwr_107.map.json`: decoded
perfectly (`CNH 350000000`, `BEN 300000000`, ... — the known factory
defaults), confirming `.106` and `.107` share the identical NVRAM layout for
the high-score table, so testing against `.106` is valid for this purpose.

**Lua scripting** (`-autoboot_script`) can poll arbitrary memory addresses
every emulated frame via `manager.machine.devices[":decocpu:maincpu"].spaces["program"]:read_u8(addr)`
and log changes — far more reliable than guessing from static disassembly.
(A `debug.wpset`-based approach that would also capture the PC of each write
was attempted but hit Lua API friction under time pressure; polling every
frame is coarser — misses same-frame read-modify-write sequences — but
reliable and sufficient to answer "does this value change, and roughly
when.")

**First empirical result, 60 real seconds of emulated `btmn_106` boot with
our patched (shadow-mirrored) NVRAM loaded: zero changes to any byte in the
live table, initials, or shadow region.** Nothing reverted. This directly
contradicts what Magnus observed on the real cabinet. Extending to a full
10-minute observation for both `btmn_106` and `stwr_106` in parallel
(background, `setsid`-detached, see `/tmp/mame_poll5.log` and
`/tmp/mame_stwr_poll.log`) to rule out a slow attract-mode cycle being the
trigger, before concluding anything from this discrepancy. If it holds at 10
minutes too, the leading hypothesis becomes: **VPinMAME (what's actually
running on Magnus's cabinet) and stock MAME's Data East driver have
diverged in NVRAM-validation behavior** — plausible given they're related
but independently-maintained codebases, and would mean this specific
static-analysis path (reading MAME's driver source, if available, or tracing
MAME's execution) may not directly explain what VPinMAME does. Worth checking
whether MAME's own driver source (`src/mame/dataeast/de2.cpp` or similar, if
inspectable) documents an NVRAM validation scheme, and separately whether
VPinMAME's own source (not installed here) is available to compare against.

## SOLVED: the trigger is coin insertion, not boot (2026-08-20)

The 10-minute idle-boot MAME tests (both `btmn_106` and `stwr_106`) completed
with **zero byte changes** — confirms the revert does not happen from booting
or sitting in attract mode, however long. Extending the test to simulate
inserting a coin (MAME Lua: `manager.machine.ioport.ports[":X0"].fields["Coin 1"]:set_value(1)`)
found it immediately: **the entire live table and the shadow region both
revert to factory defaults within 5 emulated frames of a single coin
insertion** — before `P1 Start` was even pressed. Directly observed, not
inferred from disassembly:

```
initial:     1D98=00 00 00 01 ...  1DCA-CC="   " (space)  1E1D=00 00 00 01 ...
after_coin:  1D98=30 00 00 00 ...  1DCA-CC="TIM"           1E1D=30 00 00 00 ...
             (= 30,000,000, the exact factory default, on BOTH live and shadow)
```

This is conclusive and explains every failed test across this entire
investigation at once — WPC's charset fix, the shadow-mirror fix, all of it —
because **the shadow/reference value itself is not being read from the file
at all at the moment that matters; it's re-derived from ROM-embedded
constants as part of coin-insert processing**, then compared against live,
and live gets forced to match if it looks lower. There is no persisted
"reference" byte in the `.nv` file we can set to avoid this — whatever we put
in the shadow bytes only matters until the next coin goes in, at which point
it's recomputed fresh from the ROM regardless.

**Practical conclusion: lowering Data East's demo high scores via NVRAM file
editing alone is not achievable.** The moment anyone inserts a coin to
actually play — which has to happen before the lowered score could ever be
beaten — the table reverts to factory defaults first. This isn't a bug in our
approach to find and fix; it's how these two ROMs are built. The "never
played" `---`/blank marker works on every other platform on this cabinet
(Stern SAM, Whitestar, Williams WPC) but not Data East (`stwr_107`,
`btmn_106`), and there is no further fix to chase within the file-editing
approach — the same coin-insert mechanism would reject any value lower than
its own compiled defaults, including a genuinely-earned low score from a
short/bad game, which is presumably *why* the game does this (protecting its
own high-score table's integrity against exactly this kind of tampering,
deliberately, by design).

What this doesn't change: real play still works completely normally on both
machines (scores well above the demo defaults get recorded properly, as
already established from the live-cabinet data captured earlier in this
project — `RAR`/17,228,210 on Batman, etc.). The only thing not achievable is
artificially *lowering* the bar via file tampering so real (smaller) scores
can register. Star Wars in particular will likely keep showing its demo
board indefinitely on this cabinet, since real play (1-30M) is far below its
100-350M compiled defaults and there's no way around the coin-insert
reset.

## ROM patch (2026-08-21): ---/blank marker now survives coin insertion

Magnus asked whether patching the ROM was an option, given this is a virtual
cabinet (VPinMAME loading a ROM *file*, not real EPROMs) — low-risk, since
reverting is just restoring the original file. Found the exact check-and-
restore function for each game (Star Wars: `0xc07d`, single caller at
`0x4ef3`; Batman: `0x6c27` in the combined image / `0x2c27` in the raw
`b5_a106.128` file, single caller at `0x4e59` — found the same way as Star
Wars' `c07d`, a direct `CMPA` against the shadow-region addresses). Patched
each function's first byte from `LDA` (`0xB6`) to `RTS` (`0x39`) — the entire
check-and-restore routine becomes a no-op regardless of which internal branch
would otherwise fire, since it's entered via `JSR` and a bare `RTS` cleanly
unwinds back to the caller.

**Verified empirically under MAME, both games**: loaded the patched ROM +
`research/demo-reset/nvram/{stwr_107,btmn_106}.nv`, simulated a coin insert
the same way that reliably reproduced the revert before — this time the
value held. Full writeup and deployment instructions:
`research/rom-analysis/dataeast-nvram-patch-README.md`. Patched files handed
to Magnus as `dataeast-nvram-patch.zip`; not yet tested on the real cabinet.

## What's NOT yet known

- **The exact checksum algorithm.** `0xd50f` is dense, unfamiliar code —
  confirmed genuine (not a disassembly-alignment artifact: re-disassembled
  fresh from exactly `0xd50f`, gets the same result; uses real 6309
  bit-manipulation opcodes like `TIM`/`OIM`/`SEXW` throughout, consistent with
  a real checksum/CRC routine, not garbage). Not hand-decoded yet — this is
  the next concrete step.
- **Where the computed checksum gets compared against a stored value.** Not
  located yet. By analogy with `nvmap.py`'s existing `checksum8`/`checksum16`
  model it's probably a fixed offset from each region's start (or from `$c9`),
  but this needs tracing through `0xd50f` and whatever it calls
  (`0xdd56`,`0xdb5f`,`0xdb62`,`0xdd65`,`0xdb6e`,`0xdd71`,`0xdd7f`,`0xdd8d`,`0xdd93`
  — a cluster of sub-calls in the `0xdb00-0xde00` area, not yet examined).
- **Whether the `0x513b`/checksum-dispatcher chain runs on every boot, or
  only on detected corruption / explicit operator reset.** The `$1eff`
  self-test that gates entry is a pure RAM read/write sanity check (content-
  independent), so it should always *pass* in emulation — meaning either the
  checksum-dispatcher chain has some OTHER, more commonly-hit entry point
  not yet found, or genuinely does run unconditionally as part of every cold
  boot (in which case the checksum has to be recomputed correctly on *every*
  write for it to ever hold, no exceptions).
- **Star Wars' equivalent of this checksum dispatcher.** Not located yet —
  the cross-reference approach that worked for Batman (wide literal scan for
  writes to the shadow region) found Batman's dispatcher and shadow-writers
  easily; Star Wars' literal-address scan for the *shadow* found nothing,
  consistent with "the real gate is a checksum, not a simple shadow
  comparison" — but the checksum dispatcher itself hasn't been located in
  `stwr_107`'s disassembly yet. Given both ROMs share so much structure
  (reset vector convention, shadow-copy shape, 6309 native mode), it's a
  reasonable bet Star Wars has an analogous `0x50ae`-style dispatcher and
  `0xd50f`-style checksum core — worth searching for a similarly-shaped
  "six/many calls to the same subroutine, each preceded by a different start
  address load" pattern in `starcpua.107`.

## Next steps, in priority order

1. Hand-decode `0xd50f` (and its callees in `0xdb00-0xde00`) to get the exact
   checksum algorithm, the same way `sub_1b30`/`sub_1c010` were decoded for
   Stern SAM (see `NOTES.md`).
2. Find where the computed value is compared/stored for at least one region
   (`0x1d98`/live score is the most useful, since we can verify a candidate
   formula against real sample data immediately).
3. Once the formula is known, verify it against `ScoresData/nvram/btmn_106.nv`
   (should validate on the untouched factory data, the same sanity check used
   for every other checksum in this project) before trusting it.
4. Reimplement in Python, confirmed-tested against real data, then land as
   `checksum8`/`checksum16`-style support in `nvmap.py` if the algorithm fits
   that model, or as a bespoke function in `reset_demo_scores.py` if it
   doesn't (Data East's `_metadata.platform` in the maps would need a new
   declared checksum region either way).
5. Search `starcpua.107` for the analogous dispatcher, using the "many calls
   to one shared subroutine with a changing start-address setup" shape as the
   search pattern rather than literal target addresses (which didn't find it).

## Tooling

Combined-image disassembler for Batman:
`/tmp/btmn-rom-analysis/cs_disasm.py <start_hex> <end_hex> [...]` (same
Capstone/6309 approach as `stwr_107`'s, see `stwr_107-NOTES.md`). ROM kept
outside git at `/home/coder/btmn_106.zip`, extracted/combined image at
`/tmp/btmn-rom-analysis/extracted/btmn_combined.bin` — same handling rules as
every other ROM in this investigation (Magnus's own dump, RE only, never
committed).
