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

Both one-shot modes print to the terminal they were launched from, by attaching
to the parent console. That cannot create a window — when there is no parent
console, which is exactly the service case, output stays silent and only the log
file is written.

Focus stealing is prevented structurally rather than by care: the project is built
as `WinExe` so no console is ever allocated, and a service runs in session 0 with
no desktop at all.

## Each run

1. Read every table from every source.
2. Submit everything found, **and report every table that was read**, empty ones
   included. Submission is insert-only and idempotent — the API deduplicates on
   `(table, category, initials, value)`, so resubmitting the current board every
   run is the normal case, not an error.
3. Read the authoritative board back and write it onto the machines.

Submission happens before write-back so a score set since the last run is banked
before anything overwrites the machine.

Step 3 is off by default — see [Write-back](#write-back).

### Why the table list matters

A native save file carries no timestamps, so nothing on this end can tell a score
that was *just achieved* from one that has simply **not been overwritten yet**.
That distinction is the difference between a new high score and the old board
coming back, and it decides whether clearing a table is possible at all.

It matters because the API's board can move *down* — a competition starts, a row
is deleted, the database is wiped. Until write-back lands, the machine still holds
the higher scores it had before, and step 2 posts them first. Without more
information the server has to read those as newly achieved, and the clear is undone
by the very run meant to apply it.

The missing information is what this cabinet was showing last time it reported in,
so the server keeps it and the run reports enough to maintain it: every board that
was read, whether or not it held anything. A board identical to the last report
cannot contain anything new, whatever the API currently holds — those rows come
back as `echo` rather than `inserted`, and the run summary counts them.

This is why a blanked cabinet still POSTs, with an empty `scores` array and a full
`tables` array. "I read this board and it is empty" is what tells the server a
clear reached the machine; silence would be indistinguishable from a run that never
happened.

The same reasoning in reverse: **a table that could not be read is left out of the
list entirely.** A missing file, a locked file or an unmapped ROM is not an empty
board, and reporting it as one would tell the server a clear had landed while the
machine still holds every score.

## Data formats

| Source | Where | How it's read |
| --- | --- | --- |
| VPinMAME | `nvram/*.nv`, one file per table | Bundled JSON memory maps |
| Visual Pinball | shared `User/VPReg.stg` | Bundled STG maps, managed Compound File reader |

Both extractors are pure managed code with no external binaries, so the entire
suite — including decoding real `.nv` and `.stg` files — runs on Linux in CI.

**A table without a bundled map is not read.** That is the rule for both formats.
`VPReg.stg` in particular is shared and accumulates storages for tables the
cabinet does not track; those are not cleared when the cabinet is blanked, so
discovering them by convention would feed stale scores into a freshly wiped
database. `leprechaun` is the current example, and it is simply not mapped.

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
value order. Only genuinely distinct achievements keep a category, and ranked
named sets collapse into one: `Gauntlet Champ 1/2/3` → `gauntlet_champ`,
`Officer's Club #1..#4` → `officer_s_club`.

**Which slot belongs to which category is declared by the map**, in its
`_pinballscores.categories` block — not derived from slot labels. Each entry
gives a stable `key`, a display `name`, the ordered `slots` it covers, and a
`value_type`. The category with a null name is the main board.

**The `key` is what gets submitted, not the label.** Labels are the website's to
change; sending them would mean renaming a category silently splits it in two and
strands every row stored under the old spelling.

This also makes write-back trivial: rank and slot are the same axis, so assigning
the API's board to the machine's slots is an index-for-index zip, in the slot
order the map declares.

Values are always `int64`. Never floating point — single-precision silently
perturbs anything above ~16.7 million, and did: a real `738,778,270` was recorded
by the previous implementation as `738,778,240`.

Not every value is a points total, so each carries a `value_type`: `score`,
`counter` (`6 Castles Destroyed`), `duration` (`0:10:00`) or `timestamp`. This is
taken from the map's category block verbatim — inferring it here would put the
CLI and the maps into disagreement, which is what declared categories exist to
prevent. A `display_suffix` is sent alongside for rendering.

Because the type is never inferred, a wrong type in a map is a wrong type on the
website. A test therefore asserts that any field with a counting suffix is typed
`counter`, so a map declaring "Castles Destroyed" as a score fails the build.

## Configuration

`appsettings.json` next to the executable, overridden by
`%ProgramData%\PinballScores\appsettings.json`, then `PINBALLSCORES_`
environment variables, then command line.

**Edit the `%ProgramData%` copy, never the packaged one.** The file next to the
executable is part of the install and is *replaced by every update*, so changes
there are silently lost the first time the app updates itself. The installer
creates the `%ProgramData%` copy from the packaged defaults if it does not
already exist, and never overwrites it.

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
| `PlaceholderInitials` | Historical markers ignored on extraction, default `---` |
| `PlaceholderMarker` | Initials written when blanking a slot, default a space |
| `PlaceholderValue` | Value written when blanking a slot, default `1` |
| `IgnoredTables` | Mapped tables to skip anyway (rarely needed) |

Logs go to `%ProgramData%\PinballScores\logs`, one file per day, kept 14 days,
plus the Windows Event Log when running as a service.

### When the service will not start

Windows reports only `Cannot start service PinballScores on computer '.'`, which
says nothing. Look here:

```
C:\ProgramData\PinballScores\logs\startup-error.log
```

Anything that fails before normal logging is available — most often a malformed
`appsettings.json`, since `optional` covers a *missing* file but not an *invalid*
one — is written there with the offending file and the parse position.

## Write-back

Implemented for both formats, and **off by default** — set `EnableWriteBack`.

Each Stern SAM record carries a 2-byte integrity tag equal to
`0xFFFF - sum(first 28 bytes)`, stored little-endian at `record+0x1c`; WPC games
checksum their own regions. A record written without recomputing these is rejected
on boot and replaced with the ROM's factory default, which is why earlier attempts
appeared to work and then reverted. Checksums are recomputed for the whole image
after the last field is written — and reading and writing share one implementation
(`NvramChecksum`), so a writer that disagreed with the verifier is not possible.

Every write is defended three ways:

- **Verified before it lands.** The updated image is re-read and every non-blank
  assignment must decode back to what was intended, checksums included. A mismatch
  means nothing is written.
- **Atomic.** Written to a temporary file and swapped in, so an interrupted write
  cannot leave an image the ROM would reject wholesale. `VPReg.stg` matters most
  here — it is shared by every VPX table.
- **Refuses rather than truncates.** A value too large for its field, or initials
  outside the machine's alphabet, fails the write instead of silently storing
  something wrong that would then be read back as a real score.

`--plan` never writes a byte, which is what makes it safe on a live cabinet.

**Cross-checked against an independent implementation.** Files written by this
code were verified with `research/tools/nvmap.py`: all checksums valid and all
values decoded back correctly on `smanve_101`, `twd_156h` (Stern SAM, `int`,
little-endian), `mm_109c`, `sttng_l7` (WPC 12K) and `taf_l7` (WPC 8K, `bcd`,
big-endian).

Unlike `research/patch_stg_score.py`, which could only replace a value with one of
exactly the same byte length, the STG writer resizes streams properly — no
zero-padding to the previous digit count.

### Tables that refuse to be written

`stwr_107` (Star Wars) is excluded. Its boot code restores the entire table from
an undocumented shadow copy when rank 1 looks too low, so a write reverts
everything in a way that looks like the writer failing. Mapping the shadow would
lift the exclusion.

**Blanking and placeholders.** Every slot is planned on every write, not just the
ones the API has a score for. A slot the API doesn't fill is blanked, so a score
the API doesn't know about can never linger on the machine — the cabinet ends up
showing exactly what the API holds, including when that is nothing.

This is what makes competitions work. A competition starts from an empty or
near-empty board, and the machine has to reflect that rather than keeping the
pre-competition leaderboard underneath. Two API scores on a five-slot table give:

```
Grand Champion   BBB  9000000
First Place      AAA  5000000
Second Place     '   '      1
Third Place      '   '      1
Fourth Place     '   '      1
```

The marker is space-padded to whatever the field width is — three spaces on WPC's
fixed-width field, a single space plus the terminator on Stern SAM's 11-byte
null-terminated one. Both match what `research/tools/reset_demo_scores.py` writes,
which is the form proven on the cabinet. Named categories are blanked
independently, so a competition on the main board doesn't leave stale champions.

Blanking writes **blank initials with a value of `1`** rather than clearing the
record: a ROM treats a cleared record as invalid and restores its compiled-in
factory default in its place, so "empty" does not stay empty, but a valid record
with a token value does. `1` is low enough that any real play beats it
immediately.

The marker is a space, not a dash. Live-cabinet testing on 2026-08-20 found that
Williams WPC's boot-time validation rejects `-`, because it is not in the
machine's own selectable initials alphabet, and silently reverts the record to its
factory default. A space is in that alphabet and survives a reload, and reads as
"never played" on every platform's display. This matches `MARKER_INITIALS` in
`research/tools/reset_demo_scores.py`; the two must agree.

Blank initials are already treated as an unused slot, so a reset machine submits
nothing. `PlaceholderInitials` additionally ignores the older `---` marker, which
still exists in records written before the WPC finding.

**Unmapped tables are never written to either**, for the same reason they are
never read: the blanking tool is map-driven, so an unmapped table is outside the
system entirely.

**Never write while a game is running** — a game holds its save data in memory and
flushes it on exit, so a write landing mid-session is simply overwritten. Runs
check `BlockingProcesses` first.

List the **emulators**, not the front end. PinUp Popper and similar launchers stay
running the whole time the cabinet is on, so including one would disable write-back
permanently. Names are matched by prefix, so `VPinballX` also covers `VPinballX64`
and `VPinballX_GL` — an exact match that quietly missed would be worse than no
check at all. Confirm yours with `Get-Process` while a table is open.

Nothing is lost if the check does miss: the emulator's own scores are read and
submitted on the next run, so the API stays correct and the write is simply retried.

## Deployment and updates

Tag a commit and GitHub Actions publishes a [Velopack](https://velopack.io)
package to a GitHub Release:

```
git tag v1.2.3 && git push origin v1.2.3
```

The service checks for updates between runs — never during one, so a swap cannot
land mid-write — stages the new version, schedules its own restart, and stops.
Velopack applies the update while nothing is running, and handles delta
downloads, atomic swap and rollback.

A check is two small HTTPS requests and costs nothing when there is no new
version, so `Updates:CheckInterval` can be short; it defaults to an hour and is
floored at a minute. The only real constraint is GitHub's unauthenticated rate
limit of 60 requests an hour per IP.

**Mind the TimeSpan format.** .NET reads a leading component above 23 as *days*,
so `"24:00:00"` is twenty-four **days**, not one — silently, and the only symptom
is updates never arriving. Write an hour as `"01:00:00"` and a day as
`"1.00:00:00"`.

The restart is **deliberately not** left to Windows Service Manager recovery
actions. Recovery only fires when a service terminates without reporting
`SERVICE_STOPPED`, or (with the failure-actions flag set) when it stops with a
non-zero exit code. Stopping cleanly for an update reports `SERVICE_STOPPED` with
exit code 0, which Windows treats as a normal shutdown — so SCM would never bring
the service back and the cabinet would stop collecting scores after its first
update. The updater spawns a detached helper that starts the service again
instead. If that helper cannot be spawned, the update is left staged and the
service keeps running on the current version rather than stopping.

If the repository is public no credential is needed on the cabinet at all. For a
private repository set `Updates:AccessToken`.

### Installing

Cut a release first — there is no binary until a tag is pushed:

```sh
git tag v1.0.0 && git push origin v1.0.0
```

That runs the Release workflow and attaches the artifacts to a GitHub Release.
Download `PinballScores-win-Setup.exe` onto the cabinet — that single file is
everything you need — then, from an elevated PowerShell prompt:

```powershell
.\PinballScores-win-Setup.exe --installto C:\PinballScores
C:\PinballScores\current\Install-PinballScores.ps1
```

The install scripts are packaged with the app, so they land beside the executable
and pick it up automatically. There is no need to clone the repo onto the cabinet.

**`--installto` is not optional.** Velopack's installer is per-user and defaults
to `%LocalAppData%\PinballScores`, which is the wrong place for a service running
as LocalSystem — it would resolve a different profile's LocalAppData and fail to
find its own install. A fixed path keeps the service binary and the update root
in one place both accounts agree on.

Velopack keeps the running version at `<root>\current`, so the service's binary
path stays valid across updates.

The install script configures recovery actions for genuine crashes, sets a delayed
automatic start so it isn't competing with the cabinet's front end at boot, and
creates `C:\ProgramData\PinballScores`. Put the cabinet's real settings in
`C:\ProgramData\PinballScores\appsettings.json`, which overrides the copy next to
the executable — that way an update never overwrites them.

Check it without touching anything:

```powershell
& 'C:\PinballScores\current\PinballScores.exe' --plan
```

To remove the service again, `Install-PinballScores.ps1` has a counterpart beside
it — add `-Purge` to also delete configuration and logs:

```powershell
C:\PinballScores\current\Uninstall-PinballScores.ps1
```

The **first** install is necessarily manual: auto-update needs an existing
Velopack install to update from, so a version has to be on the machine before
updates do anything at all.

## Development

```
dotnet test          # 158 tests, no Windows required
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
