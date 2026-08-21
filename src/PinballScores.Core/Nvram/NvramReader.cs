using System.Text;
using PinballScores.Core.Models;

namespace PinballScores.Core.Nvram;

/// <summary>
/// Decodes a PinMAME .nv file using a memory map. Pure managed code — this is
/// what replaces shelling out to PINemHi.exe and parsing its console output.
/// </summary>
public sealed class NvramReader
{
    private readonly byte[] _data;
    private readonly NvramMap _map;
    private readonly PlatformDefinition _platform;

    public NvramReader(byte[] data, NvramMap map, PlatformDefinition platform)
    {
        _data = data;
        _map = map;
        _platform = platform;
    }

    /// <summary>
    /// True when every checksummed region in the map validates against the file.
    /// A map for a near-miss ROM revision decodes to plausible garbage rather than
    /// failing, so this is the guard against silently publishing nonsense.
    /// Maps with no checksum regions return true — nothing to check.
    /// </summary>
    public bool ChecksumsValid(out string? failure)
    {
        foreach (var span in NvramChecksum.Spans(_map, _platform, _data.Length))
        {
            var expected = NvramChecksum.Expected(_data, span, _platform.LittleEndian);
            for (var i = 0; i < expected.Length; i++)
            {
                if (_data[span.ChecksumOffset + i] == expected[i]) continue;

                failure = $"checksum{span.Width * 8} mismatch at 0x{span.Address:X} " +
                          $"({span.Label ?? "unlabelled"})";
                return false;
            }
        }

        failure = null;
        return true;
    }

    /// <summary>Reads every populated slot on the machine, already categorised.</summary>
    public IEnumerable<ScoreEntry> ReadScores(string tableId)
    {
        foreach (var slot in _map.HighScores.Concat(_map.ModeChampions))
            if (Read(tableId, slot) is { } entry)
                yield return entry;
    }

    private ScoreEntry? Read(string tableId, ScoreSlot slot)
    {
        var initials = slot.Initials is null ? null : ReadChars(slot.Initials);
        if (string.IsNullOrWhiteSpace(initials)) return null;
        if (CategoryRules.IsUnusedSlot(initials)) return null;

        long value = 0;
        var kind = ScoreValueKind.Counter;

        if (slot.Value is { } descriptor)
        {
            var raw = ReadValue(descriptor);
            if (raw is null) return null;
            value = raw.Value;
            kind = KindOf(slot.ValueKey, descriptor);
        }

        // The map's own rollup decides the category and how the value reads. Falling
        // back to a slug keeps a slot the map forgot to place from vanishing; a test
        // asserts every bundled map places all of its slots.
        var category = _map.CategoryForSlot(slot.Label);
        var apiCategory = category is not null ? category.ApiCategory : CategoryRules.Slugify(slot.Label);
        if (category is not null) kind = category.ValueKind;

        return new ScoreEntry(tableId, apiCategory, initials.Trim(), value, kind, Clean(slot.Value?.Suffix));
    }

    private static string? Clean(string? suffix) =>
        string.IsNullOrWhiteSpace(suffix) ? null : suffix.Trim();

    private static ScoreValueKind KindOf(string? valueKey, Descriptor descriptor)
    {
        if (descriptor.Encoding.Equals("wpc_rtc", StringComparison.OrdinalIgnoreCase)) return ScoreValueKind.Timestamp;
        if (descriptor.Units is not null) return ScoreValueKind.Duration;
        if (valueKey == "counter") return ScoreValueKind.Counter;
        // A trailing "  Castles Destroyed" means the number counts things, not points.
        if (!string.IsNullOrWhiteSpace(descriptor.Suffix)) return ScoreValueKind.Counter;
        return ScoreValueKind.Score;
    }

    private long ToOffset(long address) => address - _platform.NvramBaseAddress;

    private bool TryByte(long address, Descriptor descriptor, out byte value)
    {
        var offset = ToOffset(address);
        if (offset < 0 || offset >= _data.Length) { value = 0; return false; }
        value = _data[offset];
        if (descriptor.Mask is { } mask) value &= mask;
        return true;
    }

    /// <summary>Decodes the numeric encodings: int, bcd and the WPC clock.</summary>
    public long? ReadValue(Descriptor descriptor)
    {
        long value;
        switch (descriptor.Encoding.ToLowerInvariant())
        {
            case "int":
            case "bits":
            case "bool":
                value = ReadInt(descriptor);
                break;
            case "bcd":
                value = ReadBcd(descriptor);
                break;
            case "wpc_rtc":
                return ReadClock(descriptor);
            default:
                return null;
        }

        if (descriptor.Scale is { } scale) value = (long)Math.Round(value * scale);
        if (descriptor.Offset is { } offset) value += offset;

        // "minutes" is the only non-second time unit the format defines.
        if (string.Equals(descriptor.Units, "minutes", StringComparison.OrdinalIgnoreCase)) value *= 60;

        return value;
    }

    private long ReadInt(Descriptor descriptor)
    {
        var bytes = new List<byte>();
        foreach (var address in descriptor.Addresses())
            if (TryByte(address, descriptor, out var b)) bytes.Add(b);

        var littleEndian = descriptor.Endian is { } e
            ? e.Equals("little", StringComparison.OrdinalIgnoreCase)
            : _platform.LittleEndian;

        if (littleEndian) bytes.Reverse();

        long value = 0;
        foreach (var b in bytes) value = (value << 8) | b;
        return value;
    }

    private long ReadBcd(Descriptor descriptor)
    {
        long value = 0;
        foreach (var address in descriptor.Addresses())
        {
            if (!TryByte(address, descriptor, out var b)) continue;
            // Nibbles 0xA-0xF are blanks, worth zero numerically.
            var high = (b >> 4) & 0x0F;
            var low = b & 0x0F;
            if (high > 9) high = 0;
            if (low > 9) low = 0;
            value = value * 100 + high * 10 + low;
        }
        return value;
    }

    /// <summary>WPC real-time clock: 2-byte year, month, day, weekday, hour, minute.</summary>
    private long? ReadClock(Descriptor descriptor)
    {
        var bytes = new List<byte>();
        foreach (var address in descriptor.Addresses())
            if (TryByte(address, descriptor, out var b)) bytes.Add(b);
        if (bytes.Count < 7) return null;

        int year = (bytes[0] << 8) | bytes[1], month = bytes[2], day = bytes[3], hour = bytes[5], minute = bytes[6];
        if (year is < 1900 or > 2999 || month is < 1 or > 12 || day is < 1 or > 31 || hour > 23 || minute > 59)
            return null;

        try
        {
            var moment = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc);
            return new DateTimeOffset(moment).ToUnixTimeSeconds();
        }
        catch (ArgumentOutOfRangeException)
        {
            // Day out of range for the month, e.g. 31 February in a corrupt slot.
            return null;
        }
    }

    /// <summary>Decodes the "ch" encoding: 7-bit ASCII with configurable null handling.</summary>
    public string ReadChars(Descriptor descriptor)
    {
        var builder = new StringBuilder();
        var mode = descriptor.Null?.ToLowerInvariant() ?? "ignore";

        foreach (var address in descriptor.Addresses())
        {
            if (!TryByte(address, descriptor, out var b)) break;

            if (_map.CharMap is { } charMap)
            {
                // With a char_map every byte, including 0x00, is an index into it.
                if (b < charMap.Length) builder.Append(charMap[b]);
                continue;
            }

            if (b == 0x00)
            {
                if (mode is "truncate" or "terminate") break;
                continue; // "ignore"
            }

            // 0xFF is padding on Stern SAM; anything outside printable ASCII is not a letter.
            if (b is < 0x20 or > 0x7E) continue;
            builder.Append((char)b);
        }

        return builder.ToString();
    }
}
