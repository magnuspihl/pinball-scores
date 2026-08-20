using System.Text.Json;

namespace PinballScores.Core.Nvram;

/// <summary>
/// A single field in a memory map: where it lives and how to decode it.
/// See the "Descriptors" section of the pinball-memory-maps README.
/// </summary>
public sealed class Descriptor
{
    public required string Encoding { get; init; }

    /// <summary>CPU address of the first byte (map format v0.7+), or a raw file offset on older maps.</summary>
    public long? Start { get; init; }

    /// <summary>Inclusive address of the last byte.</summary>
    public long? End { get; init; }

    public int? Length { get; init; }

    /// <summary>Non-contiguous addresses, used instead of Start/Length.</summary>
    public long[]? Offsets { get; init; }

    /// <summary>Overrides the platform endianness.</summary>
    public string? Endian { get; init; }

    /// <summary>Null-byte handling for "ch": ignore (default), truncate, terminate.</summary>
    public string? Null { get; init; }

    /// <summary>Applied to every byte before decoding, e.g. 0x7F to strip a high bit.</summary>
    public byte? Mask { get; init; }

    public double? Scale { get; init; }

    /// <summary>Added after Scale.</summary>
    public long? Offset { get; init; }

    /// <summary>Display-only trailing text, e.g. " Castles Destroyed".</summary>
    public string? Suffix { get; init; }

    /// <summary>"seconds" or "minutes" — marks the field as a duration.</summary>
    public string? Units { get; init; }

    /// <summary>Value meaning "slot unused". For initials this is typically blanks or nulls.</summary>
    public JsonElement? Default { get; init; }

    /// <summary>Number of bytes this descriptor covers, resolving End/Length/Offsets.</summary>
    public int ByteCount
    {
        get
        {
            if (Offsets is { Length: > 0 }) return Offsets.Length;
            if (Length is { } l) return l;
            if (Start is { } s && End is { } e && e >= s) return (int)(e - s + 1);
            return 1;
        }
    }

    /// <summary>Every address this descriptor reads, in order.</summary>
    public IEnumerable<long> Addresses()
    {
        if (Offsets is { Length: > 0 })
        {
            foreach (var o in Offsets) yield return o;
            yield break;
        }

        var start = Start ?? 0;
        var count = ByteCount;
        for (var i = 0; i < count; i++) yield return start + i;
    }

    public static Descriptor? From(JsonElement? element)
    {
        if (element is not { ValueKind: JsonValueKind.Object } e) return null;
        var encoding = e.Prop("encoding").Str();
        if (encoding is null) return null;

        var mask = e.Prop("mask").Number();

        return new Descriptor
        {
            Encoding = encoding,
            Start = e.Prop("start").Number(),
            End = e.Prop("end").Number(),
            Length = (int?)e.Prop("length").Number(),
            Offsets = e.Prop("offsets").Longs(),
            Endian = e.Prop("endian").Str(),
            Null = e.Prop("null").Str(),
            Mask = mask is null ? null : (byte)(mask.Value & 0xFF),
            Scale = e.Prop("scale").Double(),
            Offset = e.Prop("offset").Number(),
            Suffix = e.Prop("suffix").Str(),
            Units = e.Prop("units").Str(),
            Default = e.Prop("default"),
        };
    }
}
