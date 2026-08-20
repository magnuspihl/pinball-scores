#!/usr/bin/env python3
"""
Minimal, dependency-free reader/writer for pinball-memory-maps v0.8 maps.

This is a reference implementation of exactly the subset of the format the
cabinet's tables use, written so the read path and the write path share one
definition of every field.  It is deliberately small: the point is that
porting it to C# for PinballScores is a mechanical exercise, and that the
validator in validate_maps.py has something independent of the upstream Python
parser to cross-check against.

Supported encodings: ch, int, bcd, wpc_rtc.
Supported field attributes: start, length, offsets, null, scale, units, suffix.
Supported integrity sections: checksum8, checksum16 (both with `groupings` and
the v0.8 non-adjacent `checksum` address).
"""
import datetime
import json

PINMAME_TRAILER = 46


def to_int(value):
    if isinstance(value, str):
        return int(value, 16)
    return value


class Platform:
    """The bits of a platform definition that affect decoding."""

    def __init__(self, name, big_endian, nvram_base):
        self.name = name
        self.big_endian = big_endian
        self.nvram_base = nvram_base


PLATFORMS = {
    "stern-sam": Platform("stern-sam", False, 0x02100000),
    "whitestar": Platform("whitestar", True, 0x0000),
    "dataeast": Platform("dataeast", True, 0x0000),
    "williams-wpc-8K": Platform("williams-wpc-8K", True, 0x0000),
    "williams-wpc-12K": Platform("williams-wpc-12K", True, 0x0000),
}


class NvramMap:
    def __init__(self, game_map, data):
        self.map = game_map
        platform_name = game_map["_metadata"]["platform"]
        if platform_name not in PLATFORMS:
            raise ValueError("unsupported platform %r" % platform_name)
        self.platform = PLATFORMS[platform_name]
        self.data = bytearray(data)

    @classmethod
    def load(cls, map_path, nvram_path):
        with open(map_path) as f:
            game_map = json.load(f)
        with open(nvram_path, "rb") as f:
            raw = bytearray(f.read())
        # PinMAME appends a 46-byte trailer (the last six bytes are DIP
        # switches) after the emulated NVRAM contents.
        return cls(game_map, raw[:len(raw) - PINMAME_TRAILER])

    # -- addressing -------------------------------------------------------

    def offset(self, address):
        return to_int(address) - self.platform.nvram_base

    def field_offsets(self, field):
        if "offsets" in field:
            return [self.offset(o) for o in field["offsets"]]
        start = self.offset(field["start"])
        return list(range(start, start + field.get("length", 1)))

    # -- reading ----------------------------------------------------------

    def read_field(self, field):
        encoding = field["encoding"]
        raw = bytes(self.data[o] for o in self.field_offsets(field))

        if encoding == "ch":
            # Initials are whatever the player picked, and a space is one of
            # the characters they can pick: Batman's fourth place is ' NF' and
            # Addams Family's is 'CG '.  So only the format's own padding may
            # be removed, never a character the machine stored.
            #   null    NUL-terminated, 0xFF filler after it (Stern SAM)
            #   none    the field is exactly the entry; nothing to strip (WPC,
            #           Data East -- always 3 chosen characters)
            #   space   variable-length entry in a wider field, space-padded
            #           (Whitestar: 10 bytes holding anything up to 10 chars,
            #           so trailing spaces after a short name are padding and
            #           are indistinguishable from a chosen trailing space)
            text = raw.decode("latin-1")
            if field.get("null") == "terminate":
                return text.split("\0")[0].rstrip("\xff")
            padding = self.map.get("_pinballscores", {}).get(
                "initials_padding", "none")
            text = text.rstrip("\xff")
            return text.rstrip(" ") if padding == "space" else text

        if encoding == "wpc_rtc":
            year = (raw[0] << 8) | raw[1]
            try:
                return datetime.datetime(year, raw[2], raw[3], raw[5], raw[6])
            except ValueError:
                return None

        if encoding == "bcd":
            value = 0
            for byte in raw:
                value = value * 100 + (byte >> 4) * 10 + (byte & 0x0F)
            return value

        if encoding == "int":
            order = "big" if self.platform.big_endian else "little"
            return int.from_bytes(raw, order)

        if encoding == "bool":
            value = any(raw)
            return not value if field.get("invert") else value

        raise ValueError("unsupported encoding %r" % encoding)

    @staticmethod
    def display(field, value):
        """Human-readable rendering. Scale is presentation, never storage.

        Reading returns the raw stored integer so that read and write are
        symmetric and so the score API can be handed the machine's own number
        with its own unit (Lord of the Rings' ring timer counts hundredths of a
        second, submitted as-is with value_unit "cs"), rather than a scaled
        float -- which is how a real score once became 130,296,088.
        """
        if "scale" in field:
            value = value * field["scale"]
            text = ("%.2f" % value).rstrip("0").rstrip(".")
        else:
            text = "{:,}".format(value) if isinstance(value, int) else str(value)
        for extra in (field.get("units"), field.get("suffix")):
            if extra:
                text += " " + extra.strip()
        return text

    def records(self):
        """Yield (group, label, value_kind, initials, value, field) per record."""
        for group in ("high_scores", "mode_champions"):
            for entry in self.map.get(group, []):
                initials = None
                if "initials" in entry:
                    initials = self.read_field(entry["initials"])
                for kind in ("score", "counter", "timestamp"):
                    if kind in entry:
                        yield (group, entry.get("label"), kind, initials,
                               self.read_field(entry[kind]), entry[kind])
                        break

    # -- writing ----------------------------------------------------------

    def write_field(self, field, value):
        offsets = self.field_offsets(field)
        encoding = field["encoding"]

        if encoding == "ch":
            raw = value.encode("latin-1")
            if field.get("null") == "terminate":
                # Stern SAM: NUL terminator, 0xFF out to the end of the field.
                raw = (raw + b"\0").ljust(len(offsets), b"\xff")
            else:
                # Everything else stores fixed-width, space-padded initials.
                raw = raw.ljust(len(offsets), b" ")
            raw = raw[:len(offsets)]
        elif encoding == "bcd":
            width = len(offsets) * 2
            digits = str(int(value)).rjust(width, "0")
            if len(digits) > width:
                raise ValueError("%d does not fit in %d BCD digits" % (value, width))
            raw = bytes((int(digits[i]) << 4) | int(digits[i + 1])
                        for i in range(0, width, 2))
        elif encoding == "int":
            order = "big" if self.platform.big_endian else "little"
            raw = int(value).to_bytes(len(offsets), order)
        else:
            raise ValueError("writing %r fields is not implemented" % encoding)

        for offset, byte in zip(offsets, raw):
            self.data[offset] = byte
        self.update_checksums()

    # -- integrity --------------------------------------------------------

    def checksum_regions(self):
        """Yield (bits, data_offsets, checksum_offsets, label)."""
        for section, bits in (("checksum8", 8), ("checksum16", 16)):
            for region in self.map.get(section, []):
                start = self.offset(region["start"])
                if "end" in region:
                    end = self.offset(region["end"])
                else:
                    end = start + to_int(region.get("length", 1)) - 1
                grouping = region.get("groupings", end - start + 1)
                width = bits // 8
                while start <= end:
                    group_end = start + grouping - 1
                    if "checksum" in region:
                        checksum_offsets = list(range(
                            self.offset(region["checksum"]),
                            self.offset(region["checksum"]) + width))
                        data_offsets = list(range(start, group_end + 1))
                    else:
                        checksum_offsets = list(range(group_end - width + 1,
                                                      group_end + 1))
                        data_offsets = list(range(start, group_end - width + 1))
                    yield bits, data_offsets, checksum_offsets, region.get("label")
                    start = group_end + 1

    def expected_checksum(self, bits, data_offsets):
        total = sum(self.data[o] for o in data_offsets)
        if bits == 8:
            return [(0xFF - total) & 0xFF]
        value = (0xFFFF - total) & 0xFFFF
        if self.platform.big_endian:
            return [value >> 8, value & 0xFF]
        return [value & 0xFF, value >> 8]

    def verify_checksums(self):
        """Return a list of (label, offsets, stored, expected) for bad regions."""
        bad = []
        for bits, data_offsets, checksum_offsets, label in self.checksum_regions():
            stored = [self.data[o] for o in checksum_offsets]
            expected = self.expected_checksum(bits, data_offsets)
            if stored != expected:
                bad.append((label, checksum_offsets, stored, expected))
        return bad

    def update_checksums(self):
        for bits, data_offsets, checksum_offsets, _ in self.checksum_regions():
            for offset, byte in zip(checksum_offsets,
                                    self.expected_checksum(bits, data_offsets)):
                self.data[offset] = byte

    # -- categories -------------------------------------------------------
    #
    # A map's sections say where bytes live; `_pinballscores.categories` says
    # how those slots roll up into what the score API stores.  The machine's
    # main leaderboard is one category with no name, spread across the Grand
    # Champion slot and the ranked slots below it.  Ranked champion groups
    # ("Q Continuum #1..#4") are one category too, not four.  Rank is derived
    # from the value, so it is never stored -- which is exactly why insertion
    # needs this: to put a list of scores back on a machine you have to know
    # which physical slots a category owns, and in what order to fill them.

    def entry_by_label(self, label):
        for group in ("high_scores", "mode_champions"):
            for entry in self.map.get(group, []):
                if entry.get("label") == label:
                    return entry
        raise KeyError("no record labelled %r" % label)

    @staticmethod
    def value_field(entry):
        for key in ("score", "counter"):
            if key in entry:
                return key, entry[key]
        raise ValueError("%r has no score or counter field" % entry.get("label"))

    def categories(self):
        return self.map.get("_pinballscores", {}).get("categories", [])

    def read_category(self, category):
        """Read one category as the API stores it: [(initials, value), ...].

        Ranked categories come back sorted by value, because slot order is not
        rank on every platform -- VPX tables in particular do not re-sort.
        """
        rows = []
        for label in category["slots"]:
            entry = self.entry_by_label(label)
            _, field = self.value_field(entry)
            rows.append((self.read_field(entry["initials"]), self.read_field(field)))
        if category.get("order") != "positional":
            rows.sort(key=lambda row: row[1], reverse=True)
        return rows

    def write_category(self, category, rows):
        """Lay a category's scores back into its physical slots, best first.

        `rows` is [(initials, value), ...] straight off the API.  Supplying
        fewer rows than the category has slots leaves the remaining slots
        alone; supplying more is an error rather than a silent truncation.
        """
        slots = category["slots"]
        if len(rows) > len(slots):
            raise ValueError("category %r has %d slot(s), got %d row(s)"
                             % (category["name"], len(slots), len(rows)))
        if category.get("order") != "positional":
            rows = sorted(rows, key=lambda row: row[1], reverse=True)
        for label, (initials, value) in zip(slots, rows):
            entry = self.entry_by_label(label)
            _, field = self.value_field(entry)
            if "initials" in entry:
                self.write_field(entry["initials"], initials)
            self.write_field(field, value)

    def protected_offsets(self):
        covered = set()
        for _, data_offsets, checksum_offsets, _ in self.checksum_regions():
            covered.update(data_offsets)
            covered.update(checksum_offsets)
        return covered
