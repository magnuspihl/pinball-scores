using System.Text;

namespace PinballScores.Core.Nvram;

/// <summary>
/// Encodes values back into a PinMAME .nv image, mirroring <see cref="NvramReader"/>.
///
/// Every write is followed by recomputing the map's checksums, because a ROM
/// validates each record on boot and silently replaces one that fails with its
/// compiled-in factory default. That is why earlier write attempts appeared to work
/// and then reverted.
/// </summary>
public sealed class NvramWriter
{
    private readonly byte[] _data;
    private readonly NvramMap _map;
    private readonly PlatformDefinition _platform;

    public NvramWriter(byte[] data, NvramMap map, PlatformDefinition platform)
    {
        // Work on a copy: nothing reaches disk unless the whole write succeeds.
        _data = (byte[])data.Clone();
        _map = map;
        _platform = platform;
    }

    /// <summary>The modified image. Checksums are already up to date.</summary>
    public byte[] Data => _data;

    /// <summary>
    /// Characters a machine will accept in initials. Williams WPC validates against
    /// its own selectable alphabet on boot and reverts any record containing
    /// something else — which is why the blanking marker is a space and not a dash.
    /// </summary>
    public static bool IsWritableInitial(char c) => c is ' ' or (>= 'A' and <= 'Z') or (>= '0' and <= '9');

    /// <summary>
    /// Coerces initials into something the machine will store: upper-cased, with
    /// anything outside its alphabet replaced by a space, trimmed to the field width.
    /// </summary>
    public static string SanitiseInitials(string initials, int width)
    {
        var upper = initials.ToUpperInvariant();
        var builder = new StringBuilder(width);

        foreach (var c in upper)
        {
            if (builder.Length == width) break;
            builder.Append(IsWritableInitial(c) ? c : ' ');
        }

        return builder.ToString();
    }

    public void WriteChars(Descriptor descriptor, string value)
    {
        var offsets = Offsets(descriptor);
        var raw = Encoding.Latin1.GetBytes(SanitiseInitials(value, offsets.Count));

        var bytes = new byte[offsets.Count];
        if (string.Equals(descriptor.Null, "terminate", StringComparison.OrdinalIgnoreCase))
        {
            // Stern SAM: NUL terminator then 0xFF out to the end of the field.
            Array.Fill(bytes, (byte)0xFF);
            var length = Math.Min(raw.Length, offsets.Count - 1);
            Array.Copy(raw, bytes, length);
            bytes[length] = 0x00;
        }
        else
        {
            // Everything else stores fixed-width, space-padded initials.
            Array.Fill(bytes, (byte)' ');
            Array.Copy(raw, bytes, Math.Min(raw.Length, offsets.Count));
        }

        Apply(offsets, bytes);
    }

    public void WriteValue(Descriptor descriptor, long value)
    {
        var offsets = Offsets(descriptor);
        var bytes = descriptor.Encoding.ToLowerInvariant() switch
        {
            "bcd" => EncodeBcd(value, offsets.Count),
            "int" => EncodeInt(value, offsets.Count, LittleEndian(descriptor)),
            _ => throw new NotSupportedException($"writing '{descriptor.Encoding}' fields is not implemented"),
        };

        Apply(offsets, bytes);
    }

    /// <summary>Recomputes every checksum. Must run after the last field is written.</summary>
    public void UpdateChecksums() => NvramChecksum.UpdateAll(_data, _map, _platform);

    private bool LittleEndian(Descriptor descriptor) => descriptor.Endian is { } e
        ? e.Equals("little", StringComparison.OrdinalIgnoreCase)
        : _platform.LittleEndian;

    private static byte[] EncodeBcd(long value, int width)
    {
        var digits = width * 2;
        var text = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (text.Length > digits)
            throw new ArgumentOutOfRangeException(nameof(value), $"{value} does not fit in {digits} BCD digits");

        text = text.PadLeft(digits, '0');
        var bytes = new byte[width];
        for (var i = 0; i < width; i++)
            bytes[i] = (byte)(((text[i * 2] - '0') << 4) | (text[i * 2 + 1] - '0'));
        return bytes;
    }

    private static byte[] EncodeInt(long value, int width, bool littleEndian)
    {
        if (width < 8)
        {
            var limit = 1L << (width * 8);
            if (value < 0 || value >= limit)
                throw new ArgumentOutOfRangeException(nameof(value), $"{value} does not fit in {width} bytes");
        }

        var bytes = new byte[width];
        for (var i = 0; i < width; i++) bytes[width - 1 - i] = (byte)((value >> (i * 8)) & 0xFF);
        if (littleEndian) Array.Reverse(bytes);
        return bytes;
    }

    private List<int> Offsets(Descriptor descriptor)
    {
        var offsets = new List<int>();
        foreach (var address in descriptor.Addresses())
        {
            var offset = (int)(address - _platform.NvramBaseAddress);
            if (offset < 0 || offset >= _data.Length)
                throw new ArgumentOutOfRangeException(nameof(descriptor),
                    $"address 0x{address:X} is outside this {_data.Length}-byte image");
            offsets.Add(offset);
        }

        return offsets;
    }

    private void Apply(List<int> offsets, byte[] bytes)
    {
        for (var i = 0; i < offsets.Count && i < bytes.Length; i++) _data[offsets[i]] = bytes[i];
    }
}
