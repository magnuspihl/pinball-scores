# PinballScores

Extracts high scores from the pinball cabinet's tables and submits them to the
Foundry pinball API, then writes the API's authoritative board back onto the
machines so the cabinet and the website agree.

Runs as an invisible Windows service. It has no UI, writes nothing to a console,
and never takes focus.

## How it runs

A single long-lived service handles both triggers through one serialised queue, so
runs can never overlap:

- **on a schedule** (`Interval`, default 15 minutes) — needed regardless of local
  activity, because write-back has to push the API's board onto the machines;
- **on a file change** — a watcher on the nvram folder and `VPReg.stg`, debounced
  so a burst of writes while a game saves collapses into one run after things go
  quiet.

`--once` performs a single pass and exits, for manual runs and diagnostics.

`--plan` is a read-only rehearsal: it reports every score it would submit and
every slot it would write, without POSTing anything or touching a save file. Use
it to check a cabinet's state before and after clearing it.

Focus stealing is prevented structurally rather than by care: the project is built
as `WinExe` so no console is ever allocated, and a service runs in session 0 with
no desktop at all.

## Each run

1. Read every table from every source.
2. Submit everything found. Submission is insert-only and idempotent — the API
   deduplicates on `(table, category, initials, value)`, so resubmitting the
   current board every run is the normal case, not an error.
3. Read the authoritative board back and write it onto the machines.

Submission happens before write-back so a score set since the last run is banked
before anything overwrites the machine.

*Step 3 is currently stubbed* — see [Write-back](#write-back).

## Data formats

| Source | Where | How it's read |
| --- | --- | --- |
| VPinMAME | `nvram/*.nv`, one file per table | Bundled JSON memory maps |
| Visual Pinball | shared `User/VPReg.stg` | Managed Compound File reader |

Both extractors are pure managed code with no external binaries, so the entire
suite — including decoding real `.nv` and `.stg` files — runs on Linux in CI.

### NVRAM maps

Each ROM has a JSON map describing where its scores live, in the format used by
[pinball-memory-maps](https://github.com/tomlogic/pinball-memory-maps). Maps are
embedded in the assembly, so a deployment is one self-contained artifact.
`MapOverridePath` can supply extra or updated maps without a release.

Adding a table means adding a map, not changing code.

**Maps are verified before use.** A map for a near-miss ROM revision decodes to
plausible garbage rather than failing — the `twd_156` map applied to `twd_156h`
yields `Grand Champion: 0` and `CDC Champion: 4,294,967,295`. Every map carries
its checksum regions, and a table whose checksums don't validate is skipped rather
than published.

The bundled maps are **copies of `research/nvram-maps/`**, which is where maps are
authored — see `research/ADDING-A-TABLE.md` and
`src/PinballScores.Core/Maps/README.md` for the re-sync step.

`spagb_100` (Ghostbusters) has no map and cannot have one: it is SPIKE hardware
and PinMAME has no SPIKE driver, so no CPU is emulated and no NVRAM is ever
written. It is skipped with a reason, as any unmappable table is.

## Categories

Scores are either on the **main ranked leaderboard** or on a **named achievement
board**.

The machine stores only `(initials, value)` per record — the integrity tag covers
the record's bytes and not its address, so a record is byte-identical whichever
slot it occupies, and the game re-sorts records between slots as the board
changes. "Grand Champion", "First Place", "#1", "Honor Roll" and "Sultan's Court"
are therefore positional names for rank, not separate boards.

So the main board is submitted with **`category: null`** and rank is derived from
value order. Only genuinely distinct achievements keep a name, and ranked named
sets collapse the same way: `Gauntlet Champ 1/2/3` → `GAUNTLET CHAMP`,
`Officer's Club #1..#4` → `OFFICER'S CLUB`.

This also makes write-back trivial: rank and slot are the same axis, so assigning
the API's board to the machine's slots is an index-for-index zip.

Values are always `int64`. Never floating point — single-precision silently
perturbs anything above ~16.7 million, and did: a real `738,778,270` was recorded
by the previous implementation as `738,778,240`.

Not every value is a points total, so each carries a `value_type`: `score`,
`counter` (`6 Castles Destroyed`), `duration` (`0:10:00`) or `timestamp`. This
comes from typed map metadata rather than per-table string matching.

## Configuration

`appsettings.json` next to the executable, overridden by
`%ProgramData%\PinballScores\appsettings.json`, then `PINBALLSCORES_`
environment variables, then command line.

Nothing sensitive is compiled in.

| Setting | Meaning |
| --- | --- |
| `NvramPath` | VPinMAME nvram folder |
| `VpRegPath` | Visual Pinball `VPReg.stg` |
| `MapOverridePath` | Extra maps, loaded over the bundled ones |
| `ApiBaseUrl` | API root, including `/api` |
| `ApiKey` | Sent as `X-API-Key` |
| `Source` | Labels this cabinet's submissions |
| `Interval` | Scheduled run interval |
| `DebounceDelay` | Quiet period after a file change |
| `EnableWriteBack` | Write the API's board onto the machines |
| `DryRun` | Report what would happen; submit and write nothing (`--plan`) |
| `BlockingProcesses` | Never write while one of these runs |
| `PlaceholderInitials` | Markers ignored on extraction, default `---` (see below) |
| `PlaceholderValue` | Value written when blanking a slot, default `1` |

Logs go to `%ProgramData%\PinballScores\logs`, one file per day, kept 14 days,
plus the Windows Event Log when running as a service.

## Write-back

Not enabled yet: `NvramScoreWriter` and `StgScoreWriter` compute and log the full
slot plan but do not write bytes. The interface and slot assignment are live so
the run loop has its final shape.

The hard part is already solved. Each Stern SAM record carries a 2-byte integrity
tag equal to `0xFFFF - sum(first 28 bytes)`, stored little-endian at
`record+0x1c`. Writing a record without recomputing it makes the ROM reject the
record on boot and restore the factory default, which is why earlier attempts
silently reverted. That tag is the map format's standard `checksum16` over a
30-byte range, so it is expressed in the bundled maps and can be recomputed
generically. Verified 22/22 across `smanve_101`, `avs_170` and `xmn_151h`,
confirming it is a generic Stern SAM routine rather than per-game logic.

`research/tools/patch_score.py` is the working reference for both formats (with
the older `research/patch_nvram_score.py` and `research/patch_stg_score.py` still
alongside it). The STG path has already been proven on the real cabinet; the
NVRAM path awaits a hardware test.

**Blanking and placeholders.** Every slot is planned on every write, not just the
ones the API has a score for. A slot the API doesn't fill is blanked, so a score
the API doesn't know about can never linger on the machine — the cabinet ends up
showing exactly what the API holds.

Blanking writes `---` with a value of `1` rather than clearing the record: a ROM
treats a cleared record as invalid and restores its compiled-in factory default in
its place, so "empty" does not stay empty, but a valid record with a token value
does. `1` is low enough that any real play beats it immediately.

`---` is reserved. Extraction ignores it, so blanking a board never refills the
API with its own filler. Change it with `PlaceholderInitials` if it ever collides
with real initials.

**Never write while a game is running** — a game flushes its own save data on exit
and would discard the write. Runs check `BlockingProcesses` first.

## Deployment and updates

Tag a commit and GitHub Actions publishes a [Velopack](https://velopack.io)
package to a GitHub Release:

```
git tag v1.2.3 && git push origin v1.2.3
```

The service checks for updates between runs — never during one, so a swap cannot
land mid-write — stages the new version, and stops so the Windows Service Manager
restarts it. Set the service's recovery action to **Restart the Service**.
Velopack handles delta downloads, atomic swap and rollback.

If the repository is public no credential is needed on the cabinet at all. For a
private repository set `Updates:AccessToken`.

## Development

```
dotnet test          # 78 tests, no Windows required
dotnet run --project src/PinballScores.Service -- --once \
  --PinballScores:NvramPath=ScoresData/nvram \
  --PinballScores:VpRegPath=ScoresData/User/VPReg.stg \
  --PinballScores:ApiBaseUrl=... --PinballScores:ApiKey=...
```

`ScoresData/` holds genuine captures from the cabinet, so the tests assert against
real machine bytes rather than invented fixtures.

Note that valueless switches such as `--once` are stripped before the
configuration parser sees them; `AddCommandLine` would otherwise treat `--once` as
a key awaiting a value and silently swallow the next setting.
