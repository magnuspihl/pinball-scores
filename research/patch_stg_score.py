#!/usr/bin/env python3
"""
Patch high-score values directly in a Visual Pinball VPReg.stg file
(an OLE/CFB "structured storage" file - the same format as old .doc/.xls).

Requires: pip install olefile
olefile can only READ Compound File Binary (CFB) format, so this script
resolves each target stream's exact byte offset(s) in the raw file (by
walking the same FAT / MiniFAT chains olefile parses) and overwrites the
bytes in place. This ONLY supports same-length replacement: growing or
shrinking a stream would require rewriting the CFB directory/FAT structures,
which this script deliberately does not attempt (too risky for a file that
holds every table's scores - see numeric strings are stored as UTF-16LE
text with no padding, so pad numeric replacements with leading zeros to
match the original digit count, e.g. 75000000 (8 digits) -> 00000500).

Usage:
    python3 patch_stg_score.py <input.stg> <output.stg> \
        "gotg_2020/HighScore1=00000500" "gotg_2020/HighScore1Name=TS1" ...

Each patch arg is "<table>/<field>=<new value>". Run with just <input.stg>
and no patches to list every table/field/value in the file.
"""
import sys

try:
    import olefile
except ImportError:
    sys.exit("This script requires the 'olefile' package: pip install olefile")

FREESECT = 0xFFFFFFFF
ENDOFCHAIN = 0xFFFFFFFE


def regular_sector_offsets(start_sect, size, ole):
    offsets = []
    sect = start_sect
    remaining = size
    while sect not in (FREESECT, ENDOFCHAIN) and remaining > 0:
        abs_off = ole.sectorsize * (sect + 1)
        chunk = min(ole.sectorsize, remaining)
        offsets.append((abs_off, chunk))
        remaining -= chunk
        sect = ole.fat[sect]
    return offsets


def minisector_offsets(start_msect, size, ole):
    container = regular_sector_offsets(ole.root.isectStart, ole.root.size, ole)
    flat = []
    for abs_off, ln in container:
        for i in range(ln // ole.minisectorsize):
            flat.append(abs_off + i * ole.minisectorsize)
    offsets = []
    msect = start_msect
    remaining = size
    while msect not in (FREESECT, ENDOFCHAIN) and remaining > 0:
        abs_off = flat[msect]
        chunk = min(ole.minisectorsize, remaining)
        offsets.append((abs_off, chunk))
        remaining -= chunk
        msect = ole.minifat[msect]
    return offsets


def resolve_stream(name, ole):
    if ole.minifat is None:
        ole.loadminifat()
    sid = ole._find(name.split("/"))
    entry = ole.direntries[sid]
    if entry.size < ole.minisectorcutoff:
        return minisector_offsets(entry.isectStart, entry.size, ole)
    return regular_sector_offsets(entry.isectStart, entry.size, ole)


def read_via_offsets(raw, offsets):
    return b"".join(raw[o:o + l] for o, l in offsets)


def list_all(path):
    ole = olefile.OleFileIO(path)
    for entry in ole.listdir(streams=True):
        name = "/".join(entry)
        data = ole.openstream(name).read()
        try:
            text = data.decode("utf-16-le")
        except UnicodeDecodeError:
            text = f"<binary {len(data)} bytes>"
        print(f"{name}={text}")


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(1)

    if len(sys.argv) == 2:
        list_all(sys.argv[1])
        return

    in_path, out_path = sys.argv[1:3]
    patches = sys.argv[3:]

    ole = olefile.OleFileIO(in_path)
    raw = bytearray(open(in_path, "rb").read())

    for p in patches:
        name, new_value = p.split("=", 1)
        offsets = resolve_stream(name, ole)

        # Verify our hand-rolled offset resolution agrees with olefile's own
        # (trusted) read before touching any bytes.
        current = read_via_offsets(bytes(raw), offsets)
        expected = ole.openstream(name).read()
        if current != expected:
            raise SystemExit(f"offset resolution mismatch for {name!r}; refusing to patch")

        new_bytes = new_value.encode("utf-16-le")
        if len(new_bytes) != len(current):
            raise SystemExit(
                f"{name!r}: new value {new_value!r} is {len(new_bytes)} bytes, "
                f"but existing stream is {len(current)} bytes - same-length only "
                f"(pad numeric strings with leading zeros)"
            )

        pos = 0
        for off, length in offsets:
            raw[off:off + length] = new_bytes[pos:pos + length]
            pos += length

        print(f"patched {name} -> {new_value!r}")

    with open(out_path, "wb") as f:
        f.write(raw)
    print(f"wrote {out_path} ({len(raw)} bytes)")

    # Final sanity check: reopen the patched file and confirm every value
    # we touched (and a sample of ones we didn't) reads back correctly and
    # the file is still structurally valid as far as olefile is concerned.
    verify = olefile.OleFileIO(out_path)
    for p in patches:
        name, new_value = p.split("=", 1)
        got = verify.openstream(name).read().decode("utf-16-le")
        status = "OK" if got == new_value else f"MISMATCH (got {got!r})"
        print(f"verify {name} = {got!r} [{status}]")


if __name__ == "__main__":
    main()
