# Bundled memory maps

These are **copies**, embedded into the assembly so a deployment stays a single
self-contained artifact. They are not the place to author a map.

| What | Canonical source |
| --- | --- |
| `*.map.json` | `research/nvram-maps/` in this repo |
| `platforms/*.json` | [tomlogic/pinball-memory-maps](https://github.com/tomlogic/pinball-memory-maps) |

To add or change a table, follow `research/ADDING-A-TABLE.md`, which writes to
`research/nvram-maps/`, then re-sync:

```sh
cp research/nvram-maps/*.map.json src/PinballScores.Core/Maps/
dotnet test
```

The tests decode the committed samples in `ScoresData/nvram/` and verify every
map's checksum regions, so a bad or mismatched copy fails the build rather than
reaching a cabinet.

`MapOverridePath` loads maps from disk over these at runtime, which is the way to
support a new table without cutting a release.

## Why the copy exists

The CLI must not depend on `research/` being present in a deployed artifact, and
embedding resources from outside the project directory couples the library build
to a folder owned by the mapping workflow. The copy is cheap (~240KB) and the
checksum tests make drift loud.

## Platform files

`platforms/*.json` describe hardware shared by every ROM on a platform: byte order
and the CPU address the `.nv` file's first byte corresponds to (0x0000 on the
6809/6808 games, 0x02100000 on Stern SAM). The maps reference them by
`_metadata.platform`. They come from upstream rather than `research/`, which
documents per-ROM layouts rather than per-platform hardware.
