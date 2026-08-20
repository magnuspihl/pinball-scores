# smanve_101 ROM disassembly notes (2026-08-17)

> The checksum this documents is **solved and generic to Stern SAM**, so adding
> a table almost never needs any of it — see
> [../ADDING-A-TABLE.md](../ADDING-A-TABLE.md), which explains the two cases
> that still do. Kept for the memory map, the toolchain and the ruled-out dead
> ends, in case a genuinely new platform turns up.

ROM: smanve_101.bin (Spider-Man Vault Edition), 66,243,212 bytes, provided by Magnus
(kept outside the repo/git tree - do not commit; lives at /home/coder/smanve_101.zip
and extracted copies under /tmp/rom-analysis/ which is scratch space, not persisted).

## Confirmed facts

- ARM7TDMI, little-endian. File starts with a genuine ARM exception vector table
  (`e59ff018` = `LDR PC,[PC,#0x18]` repeated) - byte 0 of the file corresponds to
  the CPU's reset alias at address 0x00000000.
- The file's TRUE link-time base address is 0x04000000 (the documented SAM "fixed"
  game-ROM window). This was confirmed by finding a literal `0x040d0160` (base +
  file_offset) referenced in the file, matching the SAM memory map's Flash ROM
  region rather than the naive 0x0 base the reset vector implies. Standard ARM
  "flash remap at reset" pattern: flash aliases to 0x0 briefly after reset, then
  moves to its real address once boot code disables the remap.
- Found the exact 13-entry pointer table of every known score/achievement record
  address, at file offset 0xd0160-0xd02e0 (runtime 0x040d0160-0x040d02e0), values
  in order: 0x02102b60 (unidentified, 1 before Grand Champion), then Grand Champion
  through Best Bonus Champion, exactly matching research/nvram-maps/smanve_101.map.json.
- **Found the generic checksum utility used throughout this ROM for NVRAM integrity:**
  - `sub_1b30(addr, size)` - the raw checksum primitive (not yet fully reversed itself,
    but its round-trip behavior is proven).
  - `sub_1c010(addr)` - calls `sub_1b30(addr, 0x220)`, stores `~result` as a 16-bit
    halfword at `[addr + 0x220]`.
  - `sub_1c07c(addr, flag)` - verify wrapper: calls `sub_1b30(addr,0x220)`, compares
    against the stored halfword at `[addr+0x220]`, and calls `sub_1c010(addr)` again
    (regenerate) if `flag` is set and the check fails.
  - Pattern: **checksum a block, store the complement as a 2-byte tag immediately
    after the block, verify by recompute+compare.** This is almost certainly the
    same kind of mechanism protecting the high-score table's 2-byte "tag" field we
    found empirically (see project_competition_mode_design memory) - not proven to
    be the literal same function instance for OUR table, but strong evidence this
    general approach is how Stern SAM protects NVRAM blocks on this platform generally.
  - Found and catalogued **63 call sites** of the raw primitive `sub_1b30` across the
    whole 63MB file (see checksum_call_contexts_raw.txt in the same scratch dir,
    not committed). Extracted the size (r1) argument for each - values seen: 0xc,
    0x1a, 2, 4, 0x1c, 8, 0x220, 0x128. **None of these 63 call sites match our
    actual score table's address or size** (13 records * 0x20 = 0x1a0 bytes, or
    5*0x20=0xa0 for main leaderboard alone) - so this exact generic-utility instance
    protects OTHER nearby settings/state blocks, not proven to be our table directly.
  - One specific call (r0=0x02102d48, size 0x220) is close to but NOT overlapping our
    table (Best Bonus Champion ends at 0x02102d20; 0x02102d48 starts 0x28 bytes later).

## Dead ends / ruled out

- File offset ~0x98b5b4 (value 0x02102c18, exactly Fourth Place's score sub-field) -
  checked, this is inside asset/audio data (garbage disassembly), a coincidental
  byte match, not real code.
- File offset ~0xdf874-0xdfa60 (9 sequential-ish NVRAM addresses just past our table,
  0x02102d00 through 0x02102d40) - also confirmed to be data, not code.
- ~13 other scattered single-hit "NVRAM-range value" matches across the 63MB file
  (2MB to 61MB) all look like coincidental matches inside large asset blobs (each is
  a few bytes off a clean record boundary, e.g. 0x02102d06, 0x02102d29, 0x02102da9 -
  a real code reference would hit an exact 0x20-aligned boundary).
- PinMAME's own emulator source (sam.c) does ZERO NVRAM validation - confirmed the
  checksum logic lives in the game ROM's own code, not the emulator.
- Ran Ghidra's automatic cross-reference search (FindScoreRefs.py) against the first
  1.5MB - found ZERO references TO the pointer table's 13 record addresses. This is
  consistent with the real "for each record, checksum it" loop reading addresses
  OUT of the table at runtime (register-indirect) rather than using literal-pool
  constants per record, which would not show up in a literal-value scan at all.

## SOLVED (2026-08-17)

While a deeper Ghidra pass ran in the background, kept manually tracing the raw
disassembly and found the actual checksum by hand:

- `sub_1b30(addr, count)` (file offset 0x1b30) is the raw primitive: a plain 16-bit
  running sum of `count` bytes starting at `addr` (`acc = (acc + byte) & 0xFFFF`
  per byte, done via a shift-based truncation trick rather than a direct AND, but
  functionally a simple byte sum mod 65536).
- `sub_1c010(addr)` (0x1c010) calls `sub_1b30(addr, 0x220)` and stores `~result`
  (i.e. `0xFFFF - result` truncated to 16 bits) as the halfword at `[addr+0x220]`.
  This is a *different, larger* block (used elsewhere in NVRAM, e.g. base
  0x02102d48) - not our score table directly, but it revealed the exact algorithm.
- Applying the same formula to OUR score records - `tag = 0xFFFF - sum(bytes[0:0x1c])`
  where bytes[0:0x1c] is the 11-byte initials field + 13 filler bytes + 4-byte
  score, all as one 28-byte span - matched **24/24** known real/factory-default
  records collected over the course of this investigation. This is almost
  certainly the exact mechanism (or an identical sibling instance of the same
  generic library routine) protecting the actual high-score table, even though
  the *specific* 63 call sites of `sub_1b30` found by direct-callsite search don't
  include an exact match for our table's own address/size - the checksum FORMULA
  itself is confirmed correct regardless of which code path calls it.

`research/patch_nvram_score.py` was updated to compute and write this tag
automatically. Regenerated test patches for both `smanve_101` and `xmn_151h`
with self-verified checksums, ready for the next real-hardware test.

A parallel deeper Ghidra auto-analysis (4MB slice, multi-reference scan) was also
left running in the background as a bonus cross-check for the exact caller - not
required for the fix, since the checksum formula itself is already confirmed
correct against real data independent of finding its specific caller.

## Reproducing this analysis

Toolchain installed via apt: `binutils-arm-none-eabi`, `python3-capstone`, `radare2`,
`openjdk-21-jdk-headless`. Ghidra 12.1.2 downloaded to /tmp/ghidra-install (not
persisted - re-download from
https://github.com/NationalSecurityAgency/ghidra/releases/latest if needed again).
