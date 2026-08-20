using System.Text.Json;

namespace PinballScores.Core.Nvram;

/// <summary>
/// Hardware description shared by every ROM on a platform. Supplies the byte
/// order and the CPU address the .nv file's first byte corresponds to.
/// </summary>
public sealed class PlatformDefinition
{
    public required string Name { get; init; }
    public bool LittleEndian { get; init; }

    /// <summary>
    /// CPU address of the NVRAM region. Map addresses are CPU addresses from
    /// format v0.7 onward, so the file offset is address minus this base.
    /// Zero on the 6809/6808 platforms, 0x02100000 on Stern SAM.
    /// </summary>
    public long NvramBaseAddress { get; init; }

    public long NvramSize { get; init; }

    /// <summary>Fallback for maps with no platform file: plain offsets, big endian.</summary>
    public static readonly PlatformDefinition Default = new()
    {
        Name = "(none)",
        LittleEndian = false,
        NvramBaseAddress = 0,
        NvramSize = 0,
    };

    public static PlatformDefinition Parse(string name, JsonDocument doc)
    {
        var root = doc.RootElement;
        long baseAddress = 0, size = 0;

        if (root.Prop("memory_layout") is { ValueKind: JsonValueKind.Array } layout)
        {
            foreach (var region in layout.EnumerateArray())
            {
                if (!string.Equals(region.Prop("type").Str(), "nvram", StringComparison.OrdinalIgnoreCase))
                    continue;
                baseAddress = region.Prop("address").Number() ?? 0;
                size = region.Prop("size").Number() ?? 0;
                break;
            }
        }

        return new PlatformDefinition
        {
            Name = name,
            LittleEndian = string.Equals(root.Prop("endian").Str(), "little", StringComparison.OrdinalIgnoreCase),
            NvramBaseAddress = baseAddress,
            NvramSize = size,
        };
    }
}
