# FX3 save file encryption — research notes

Findings from an attempt to reverse-engineer the format of `Profile.dat` and
the other `ScoresData/FX3/*.dat` files. Short version: these files are
genuinely encrypted (not just obfuscated), and cracking them requires
material this repo doesn't have — see "What's actually needed" below.

## What was tested

All four top-level `.dat` files (`Profile.dat`, `Misc.dat`,
`GameStateCache.dat`, `SystemConfigCache.dat`) were analyzed statistically:

- **Byte entropy**: 7.99–8.00 bits/byte for the three larger files (max
  possible is 8.0). `Misc.dat`'s slightly lower raw entropy (6.63) is fully
  explained by its small sample size (148 bytes) via the birthday bound —
  not a sign of weaker encoding.
- **Block repetition**: no repeated 8-byte or 16-byte blocks anywhere in any
  file. This rules out ECB-mode block reuse (which real save data — lots of
  zero padding, repeated player-name strings, etc. — would normally produce
  if ECB were used).
- **Repeating-key XOR (Kasiski/autocorrelation)**: tested key lengths 1–256
  on `Profile.dat`; no length showed a coincidence rate above the ~1/256
  baseline expected from random data. Rules out a static repeating-key XOR
  "obfuscation" scheme.
- **Single-byte XOR brute force**: best result recovered only ~45% printable
  ASCII (real text should be >90%+). Rules out trivial single-byte XOR.
- **Compression signatures**: no zlib/gzip/LZ4 magic bytes at the start of
  any file.

Conclusion: this is consistent with a real block/stream cipher (most likely
AES, given what's importable — see below), not a home-grown obfuscation
scheme. Ciphertext-only cryptanalysis against real AES is not feasible.

## Header structure observed

`Misc.dat`, `GameStateCache.dat`, and `SystemConfigCache.dat` all:
- start with the byte `0x02`
- have a total size ≡ 4 (mod 16) — i.e. `size - 4` is an exact multiple of
  16 bytes

That's consistent with a 4-byte unencrypted header (`0x02` = format/version,
+3 bytes unknown) followed by block-aligned ciphertext with no separate
stored IV (or an IV derived from something outside the file, e.g. a fixed
value or per-install key material).

`Profile.dat` doesn't fit this pattern — it starts with `0xDD` and its total
size is an exact multiple of 16 (no 4-byte remainder). Plausible
explanations: it uses a different wrapper (e.g. the first 16 bytes are an
embedded random IV rather than a 4-byte plaintext header), or it's a
different/newer format version than the cache files. Not resolved here.

## What's actually needed to go further

Nothing in this ciphertext points to a recoverable key — that's expected;
that's what encryption is for. Zen Studios' PinFX engine binary
(`Pinball FX3.exe` and its DLLs) is not present in this repo or this
environment, only the sample encrypted data. Static analysis of a
public malware-sandbox report for the game binary shows it links
`LIBEAY32.dll`/`SSLEAY32.dll` (OpenSSL), which is consistent with AES being
available to the game for local file encryption (in addition to its more
obvious use for networking), but that alone doesn't yield the key or mode.

No public prior art was found (searched Zen Studios/ZenHAX forums, GitHub,
cheat-engine communities) — nobody appears to have published a working
decryptor for these files.

To make real progress from here, someone would need to do one of:

1. **Static RE of the actual game binary** — pull `Pinball FX3.exe` +
   associated DLLs from an install (or the depot via SteamCmd) and load them
   in a disassembler (Ghidra is available in this dev environment) to find
   the encrypt/decrypt call sites and the embedded key/IV handling.
2. **Dynamic analysis** — run the real game with a debugger or a tool like
   Frida attached, break on the file-write for `Profile.dat`, and capture
   the plaintext buffer (and/or the key) directly out of memory before it's
   encrypted.
3. **Known-plaintext attack** — if (1) or (2) establishes the mode (e.g.
   AES-CBC with a fixed/derivable IV) but not the key, having several save
   files with a *known* score/name change between them (e.g. save before and
   after getting one specific new high score) could narrow down key search
   space — but this still requires knowing the mode/algorithm first.

None of these are possible with what's checked into this repo today. This
file is meant as a starting point/checkpoint for whoever picks this up next
rather than a finished decoder.
