# Competition Mode & score write-back — findings (2026-08-17)

> **Follow-up:** this document covers the two tables the investigation started
> with (`smanve_101` and `gotg_2020`). All eighteen tables on the cabinet have
> since been mapped and verified — see [TABLE-MAPPING.md](TABLE-MAPPING.md),
> which supersedes the "Tooling inventory" section below.

Written for whoever picks up the CLI reimplementation and/or the new score-ingestion
API, so the context from this investigation doesn't have to be rediscovered. Everything
here came out of one long working session; see `research/rom-analysis/NOTES.md` and
`research/nvram-maps/*.json` for the deepest technical detail on the NVRAM side.

## The original ask

Magnus wants to run short (~month-long) high-score competitions per table at his
office cab: wipe a table's leaderboard to only show competition-period scores, then
restore the combined (pre-competition + competition) leaderboard afterward, with
whoever legitimately held a spot before getting it back if nobody beat them.

## Infra is being replaced, not just patched

`PinballScores/Services/DatabaseService.cs` and `NotificationService.cs` have a live
Firebase service-account private key and Slack webhook URLs hardcoded and committed to
git. These are **stale credentials from a company Magnus used to own, since sold to his
current workplace** — not just a secret to rotate in place, the integrations themselves
are being replaced. Planned replacement: an internal platform called **Foundry**, backed
by a shared Postgres database. Don't design new backend code around Firestore concepts
(documents, `SetAsync`/`MergeAll`) — think relational/Postgres.

## A real, live bug found along the way

`ScoreModel.Score` is a 32-bit `float` (`ScoreModel.cs:15`), and `PINemHiExtractor.CleanScore`
parses into `float` too (`PINemHiExtractor.cs:111,116,127-128`). Single-precision floats
can't exactly represent every integer past ~16.7 million. Confirmed directly: a real
130,296,090 score got stored/displayed as 130,296,088 — not a typo, not a display glitch,
an actual precision bug that silently perturbs any score over ~16.7M. **The new
implementation should store scores as an integer type (`bigint`/`int64`), never
float/single-precision.**

## Master-store / API design direction

Agreed direction (not yet implemented — this is what the new CLI + API should target):

- **Insert-only event log**, not a "current state" mirror. The current app's Firestore
  sync blindly overwrites a `scores` field on every run, mirroring whatever the machine
  currently shows — no history, nothing to build Competition Mode on. Instead: every
  observed score becomes an immutable record. The displayed leaderboard is a *query*
  (top-N per table+category, optionally scoped to a date range), not stored state.
  This is what makes Competition Mode nearly free: "wipe the leaderboard" = filter the
  view by `inserted_at >= competition_start`; "restore" = drop the filter. Nothing is
  ever deleted, so nothing is ever at risk of being lost by a competition.
- **Dedup key: `(table, category, player, score)`.** No true timestamps exist in the
  source data (NVRAM/STG don't record *when* a score was set), so this tuple is the
  only practical identity. Known accepted limitation: a player hitting the *exact same*
  score twice on a low-cardinality field (e.g. a small mode-achievement counter) reads
  as one event, not two — decided this is fine, duplicate values aren't interesting to
  distinguish. `inserted_at` (server-side, at submission time) is the closest thing to a
  timestamp, and it's an upper bound / proxy for achievement time, not exact — it lags by
  however often the extractor runs. Competition boundaries will be approximate to that
  cadence, not to the second — worth stating up front rather than assuming otherwise.
- **`category`: `NULL`/sentinel for the ranked main leaderboard, a name for distinct
  achievements** (e.g. `"Spider Champion"`, `"Best Bonus Champion"`). This collapses
  "HIGH SCORE #1/#2/.../GRAND CHAMPION/FIRST PLACE" (today's separate, rank-baked-into-
  identity labels) into one undifferentiated pool where rank is *derived* (`ORDER BY
  score DESC`), not stored — avoids storing a rank that goes stale the moment score
  order shifts. Already has precedent: `StgExtractor` already regexes `HighScore` +
  digits down to one bare title, and our own NVRAM maps already separate `high_scores`
  (→ null/sentinel) from `mode_champions` (→ named) as distinct arrays. **Pitfall for
  whoever builds this in Postgres: don't make the *stored* dedup-key column genuinely
  nullable** — `NULL <> NULL` in SQL, so a `UNIQUE`/`ON CONFLICT` constraint on a
  nullable `category` column will not correctly dedup rows that are otherwise identical
  with `category IS NULL`. Use a `NOT NULL` sentinel (empty string or similar) for the
  stored column; translate to/from `null` at the API boundary if desired.
- **This normalization (raw title → category null-or-name) belongs in the extractor/CLI,
  not the server.** It requires knowing source-format-specific quirks (STG's `HighScoreN`
  naming convention, which array a field lives in in our NVRAM maps) that the server
  shouldn't need to understand. Keeps the server format-agnostic and means a future FX3
  extractor can do its own collapsing without server changes.
- **Batch/idempotent submission semantics.** The extractor will resubmit the same
  currently-visible scores on most runs — duplicates are the *normal* case, not an edge
  case. The insert endpoint needs to be a real upsert (`INSERT ... ON CONFLICT DO
  NOTHING`-style), and a batch submission should return **per-entry** results (inserted /
  duplicate / rejected+why), not fail the whole batch for one bad entry.
- **No update/delete in the normal ingestion flow** — keeps with insert-only. If a real
  correction is ever needed, make it a separate, clearly-audited admin path, not part of
  routine ingestion.
- **Decide where "new score" notification logic lives.** Today the CLI compares old vs.
  new client-side and pings Slack itself. If the server now owns dedup, "was this
  genuinely new" naturally belongs there too — have the insert endpoint return whether a
  submission was newly inserted, and drive notifications off that, rather than having
  client and server maintain separate, possibly-disagreeing ideas of what's new.
- **Open question, not yet decided:** table identity across ROM upgrades. Table keys
  today are just the ROM filename (`smanve_101`, `xmn_151h`). If a cabinet ROM gets
  upgraded (`smanve_101` → `smanve_102`), is that the same logical table with continued
  history, or a fresh one? Decide intentionally.
- **Auth on the write endpoint** — even a shared secret, so it's not open to anyone on
  the network to spoof scores.

## NVRAM write-back — the deep technical thread

Read side (`PINemHiExtractor`/existing maps) has always worked. The open question all
session was whether Magnus could *write* competition-adjusted scores back into the
physical machine's own NVRAM so the cabinet's own attract-mode display reflects
Competition Mode too, not just the website.

**Record format (Stern SAM platform, confirmed):** each score/achievement record is 32
(`0x20`) bytes: 11-byte null-terminated ASCII initials (0xFF-padded), 13 bytes of 0xFF
filler, a 4-byte little-endian score/counter, **a 2-byte checksum tag**, then 2 more
genuine 0xFF filler bytes.

**The checksum (this was the actual blocker, now solved):** every write attempt all
session reverted silently to the ROM's factory-default value for that slot on next boot
— not because of "value too low" (ruled out with a +100k test on real values), not a
whole-block checksum (ruled out — editing one field only reverted that field), but
because **each record has its own integrity tag we didn't know existed and never
recomputed.** Confirmed by disassembling the actual game ROM (`smanve_101.bin`, provided
by Magnus for this purpose only — his own legally-owned copy, not to be shared or
redistributed, deliberately kept out of git):

```
tag (2 bytes, at record_offset + 0x1c)
  = 0xFFFF - (sum of the 28 bytes from record start through the end of the
    score field, as plain unsigned bytes, mod 65536)
```

Verified 24/24 against every known real/factory-default record collected during the
investigation (initial evidence came from Magnus sending the real `smanve_101.nv` off
the cabinet). `research/patch_nvram_score.py` now computes and writes this automatically
— every patch it produces is self-verified (recomputes the tag on read-back and confirms
it matches what was written).

**Confirmed for `smanve_101` (Spider-Man Vault Edition) against real hardware data.
Presumed — not independently confirmed — for `xmn_151h` (X-Men LE) and other Stern SAM
titles:** it's a generic runtime-library checksum routine, not per-game logic, and spot
checks against the X-Men sample data are consistent with it, but there's no real captured
X-Men player data to fully cross-verify the way there was for Spider-Man.

**Not yet done: a real-hardware test of the corrected write.** Every prior test used the
*old*, tag-unaware patch tool and predictably reverted. `research/test-output/` has
fresh, correctly-checksummed test files for both tables (`smanve_101-fixed.nv`,
`xmn_151h-fixed.nv` — main leaderboard set to TS1-TS5 / 500-100), ready for the next time
Magnus has cabinet access. This is the thing to watch for — if it holds, NVRAM write-back
is real and Competition Mode can extend to the physical display, not just the website.

**Other things ruled out / learned along the way, for context:**
- The `.nv` sample files committed in `ScoresData/` are genuine ROM factory-default
  captures, not fabricated demo data — confirmed by an exact byte match between a
  live-observed ROM fallback value and the git sample's value for the same field.
- PinMAME (the emulator) does zero NVRAM validation of its own (`src/wpc/sam.c`'s
  `nvram_handler` is a dumb 128KB read/write passthrough) — the checksum is entirely the
  *game ROM's own* logic, running via CPU emulation, not something the emulator adds.
  This is why finding it required disassembling the actual ROM, not the open-source
  emulator.
- A deeper automated Ghidra pass (full auto-analysis + a script hunting for functions
  referencing multiple NVRAM addresses) was left running as a bonus cross-check but
  timed out inconclusively (Ghidra 12.1.2 also dropped legacy Jython script support,
  which the post-script relied on) — not needed in the end, since the checksum formula
  was found and confirmed by hand first.

## STG (Visual Pinball X) write-back — already confirmed working

Simpler platform: OLE Compound File, no checksum, no factory-default fallback observed.
**Already tested successfully on the real cabinet** (Guardians of the Galaxy /
`gotg_2020`) — patched test scores displayed correctly and were legitimately beaten and
overwritten by real play. `research/patch_stg_score.py` handles this; current limitation
is same-length-only writes (numeric replacements must zero-pad to match the original
digit count) since growing/shrinking a CFB stream needs directory/FAT surgery this
deliberately doesn't attempt.

## Tooling inventory (`research/`)

*(Superseded by [TABLE-MAPPING.md](TABLE-MAPPING.md), which covers all eighteen
tables and adds `tools/`. The map files listed here have been regenerated in
file format v0.8 with their `checksum16` sections.)*

- `nvram-maps/` — one map per NVRAM table, `stg-maps/` — one per VPX table.
- `tools/validate_maps.py` — checks every map against real NVRAM, including a
  simulated write.
- `tools/sam_record_scan.py` — rediscovers a Stern SAM table's record layout
  from any dump, using the checksum as a signature.
- `tools/find_checksums.py` — finds (or rules out) a checksum protecting a byte
  range, given two dumps of one ROM.
- `tools/patch_score.py` — map-driven writer for *any* platform (WPC BCD behind
  a block checksum, SAM ints behind a per-record checksum, Whitestar/Data East
  with no checksum), superseding the SAM-only `patch_nvram_score.py` below.
- `patch_nvram_score.py` — patches NVRAM records with correct checksums, self-verifying.
- `patch_stg_score.py` — patches STG (VPX) records, same-length-only, self-verifying.
- `rom-analysis/NOTES.md` — the detailed ROM-disassembly trail (memory map, function
  addresses, dead ends ruled out) if anyone needs to pick the reverse-engineering back up
  (e.g. to find the *exact* caller for our specific table, or to tackle a different Stern
  SAM title from scratch).
- `test-output/` — the current (checksum-corrected) test patches awaiting a real-hardware
  test, for both tables.

## Not started / explicitly out of scope for now

- FX3 (Pinball FX3) save format — README already flags it as possibly encrypted, unknown
  even for reading. Magnus wants this as a separate future task, not part of this thread.
- The actual new API/server implementation — being designed in Foundry (a different
  Claude-based interface), not built here. This document is the context to hand it.
- The CLI reimplementation to match that new API — planned as a separate CPM task.
