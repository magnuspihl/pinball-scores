# Data East NVRAM-guard patch (stwr_107, btmn_106)

Removes the coin-insert-triggered anti-tamper check documented in
`dataeast-checksum-NOTES.md`, so the `---`/blank demo-score reset actually
survives on these two tables. Confirmed empirically under MAME (patched ROM +
`research/demo-reset/nvram/{stwr_107,btmn_106}.nv` loaded, simulated coin
insert): the reset value now holds through coin-insert where it previously
reverted to the factory default within 5 frames.

## The patch

One byte each, changing a function's first instruction from `LDA` (`0xB6`) to
`RTS` (`0x39`) — the whole check-and-restore routine becomes a no-op, entered
and immediately returned from, regardless of which of its internal branches
would otherwise have fired.

| Table | File to replace inside the ROM zip | Offset | Before | After |
|---|---|---|---|---|
| Star Wars | `starcpua.107` | `0xC07D` | `B6` (`LDA $1694`) | `39` (`RTS`) |
| Batman | `b5_a106.128` | `0x2C27` | `B6` (`LDA $1DB0`) | `39` (`RTS`) |

Both call sites are single, unambiguous `JSR` instructions to the patched
address (`0x4EF3` for Star Wars, `0x4E59` for Batman — the combined memory
image maps `b5_a106.128` to CPU addresses `0x4000-0x7FFF`, so CPU address
`0x6C27` is file offset `0x6C27-0x4000 = 0x2C27` in the raw `b5_a106.128`
file), confirmed via a full-ROM cross-reference scan finding no other caller.
An `RTS` at the entry point is a clean no-op for any `JSR`-called function on
this CPU family, regardless of what branches exist inside it.

`starcpua.106`'s code is byte-identical to `.107` at this offset (confirmed
by diffing the two — only 120 bytes differ anywhere in the 64KB image, none
of them here), so the same patch applies at the same offset if `.106` is ever
needed instead.

## Deploying it

Each game's ROM is a zip file in VPinMAME's roms folder (e.g. `stwr_107.zip`,
`btmn_106.zip`). **Replace only the one listed file inside that zip** with
the patched version below — every other file in the zip (sound ROMs, display
ROM, etc.) is unchanged and must stay exactly as it is.

Keep a copy of the original zip before replacing anything, the same way
you'd back up before any NVRAM change — this is a bigger step than a save
file edit, since it's your actual game ROM now permanently modified. If
anything looks wrong after deploying, reverting is just putting the original
file back.

## What this does NOT change

- Real gameplay is untouched — the patch only defeats the anti-tamper
  restore, nothing about scoring, rules, or display logic.
- This has been verified under MAME with a simulated coin insert, not yet on
  your actual cabinet — next step is trying it there.
- Not verified whether the game does any *other*, unrelated integrity check
  of its own ROM content at boot (some games do a full self-checksum of their
  own code) — 40 seconds of MAME testing showed no sign of one (booted,
  processed coin-insert, and held the patched value with no crash or "ROM
  error" screen), but that's not a guarantee across longer/different play.
