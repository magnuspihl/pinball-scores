namespace PinballScores.Core.Nvram;

/// <summary>
/// One checksummed span, resolved to file offsets: the bytes covered, and where
/// the checksum over them is stored.
/// </summary>
public readonly record struct ChecksumSpan(
    int DataStart,
    int DataEnd,
    int ChecksumOffset,
    int Width,
    long Address,
    string? Label);

/// <summary>
/// Resolves and computes the integrity checksums a map declares.
///
/// Reading and writing share this deliberately. A ROM rejects a record whose
/// checksum does not match and silently restores its factory default, so a writer
/// that computed these even slightly differently from the verifier would produce
/// files that pass our own checks and then revert on the machine — the exact
/// failure that made NVRAM write-back look impossible for so long.
/// </summary>
public static class NvramChecksum
{
    /// <summary>Every checksummed span in the map, in file-offset terms.</summary>
    public static IEnumerable<ChecksumSpan> Spans(NvramMap map, PlatformDefinition platform, int dataLength)
    {
        foreach (var region in map.Checksums)
        {
            foreach (var (start, end) in Subranges(region))
            {
                var startOffset = (int)(start - platform.NvramBaseAddress);
                var endOffset = (int)(end - platform.NvramBaseAddress);
                if (startOffset < 0 || endOffset >= dataLength || endOffset < startOffset) continue;

                // Without an explicit checksum address the stored value occupies the
                // last Width bytes of the range and the sum covers everything before it.
                var checksumOffset = region.ChecksumAddress is { } address
                    ? (int)(address - platform.NvramBaseAddress)
                    : endOffset - region.Width + 1;
                var dataEnd = region.ChecksumAddress is null ? checksumOffset - 1 : endOffset;

                if (checksumOffset < 0 || checksumOffset + region.Width > dataLength) continue;
                if (dataEnd < startOffset) continue;

                yield return new ChecksumSpan(
                    startOffset, dataEnd, checksumOffset, region.Width, start, region.Label);
            }
        }
    }

    /// <summary>
    /// A region is normally one checksummed range. With "groupings" it is a series
    /// of fixed-size records, each ending in its own checksum.
    /// </summary>
    private static IEnumerable<(long Start, long End)> Subranges(ChecksumRegion region)
    {
        if (region.Groupings is not { } size || size <= 0)
        {
            yield return (region.Start, region.End);
            yield break;
        }

        for (var start = region.Start; start + size - 1 <= region.End; start += size)
            yield return (start, start + size - 1);
    }

    /// <summary>The bytes that should be stored at the span's checksum offset.</summary>
    public static byte[] Expected(byte[] data, ChecksumSpan span, bool littleEndian)
    {
        long sum = 0;
        for (var i = span.DataStart; i <= span.DataEnd; i++) sum += data[i];

        if (span.Width == 1)
        {
            // checksum8: the low byte of the total, including the stored byte, is 0xFF.
            return [(byte)((0xFF - sum) & 0xFF)];
        }

        // checksum16: 0xFFFF minus the sum of all preceding bytes, stored in the
        // platform's byte order (big on the 6809/6808 games, little on Stern SAM).
        var value = (int)((0xFFFF - sum) & 0xFFFF);
        return littleEndian
            ? [(byte)(value & 0xFF), (byte)(value >> 8)]
            : [(byte)(value >> 8), (byte)(value & 0xFF)];
    }

    /// <summary>Recomputes every checksum in place, after the data has changed.</summary>
    public static void UpdateAll(byte[] data, NvramMap map, PlatformDefinition platform)
    {
        foreach (var span in Spans(map, platform, data.Length))
        {
            var expected = Expected(data, span, platform.LittleEndian);
            for (var i = 0; i < expected.Length; i++) data[span.ChecksumOffset + i] = expected[i];
        }
    }
}
