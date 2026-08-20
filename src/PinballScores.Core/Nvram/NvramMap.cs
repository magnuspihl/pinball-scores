using System.Text.Json;

namespace PinballScores.Core.Nvram;

/// <summary>A region protected by a checksum, used to detect a map/file mismatch.</summary>
/// <param name="Groupings">
/// When set, the range is a series of fixed-size records each carrying its own
/// trailing checksum, rather than one checksum over the whole range. WPC audits
/// are stored this way as 6-byte entries.
/// </param>
public sealed record ChecksumRegion(
    long Start,
    long End,
    long? ChecksumAddress,
    string? Label,
    int Width,
    int? Groupings = null);

/// <summary>
/// One entry on a board: who holds it and what they scored. A slot may have no
/// value descriptor at all (some achievements only record initials).
/// </summary>
public sealed class ScoreSlot
{
    public required string Label { get; init; }
    public Descriptor? Initials { get; init; }

    /// <summary>
    /// The descriptor holding the rankable value, chosen in priority order
    /// score → counter → timestamp. Null when the slot only records initials.
    /// </summary>
    public Descriptor? Value { get; init; }

    /// <summary>Which map key <see cref="Value"/> came from, so the value kind can be inferred.</summary>
    public string? ValueKey { get; init; }
}

/// <summary>
/// A parsed NVRAM map: the machine's own description of where its scores live.
/// </summary>
public sealed class NvramMap
{
    public required string RomId { get; init; }
    public string? Platform { get; init; }
    public string? CharMap { get; init; }

    /// <summary>The main ranked leaderboard. Submitted with a null category.</summary>
    public IReadOnlyList<ScoreSlot> HighScores { get; init; } = [];

    /// <summary>Named achievement boards. Submitted with the slot's label as category.</summary>
    public IReadOnlyList<ScoreSlot> ModeChampions { get; init; } = [];

    public IReadOnlyList<ChecksumRegion> Checksums { get; init; } = [];

    /// <summary>
    /// Value descriptors are looked up in this order. A game's rankable number is
    /// its score if it has one; failing that a counter (Medieval Madness' King of
    /// the Realm ranks by counter, with the timestamp as colour); failing that the
    /// timestamp itself.
    /// </summary>
    private static readonly string[] ValueKeyPriority = ["score", "counter", "timestamp"];

    public static NvramMap Parse(string romId, JsonDocument doc)
    {
        var root = doc.RootElement;
        var metadata = root.Prop("_metadata");

        return new NvramMap
        {
            RomId = romId,
            Platform = metadata?.Prop("platform").Str(),
            CharMap = metadata?.Prop("char_map").Str(),
            HighScores = ParseSlots(root.Prop("high_scores")),
            ModeChampions = ParseSlots(root.Prop("mode_champions")),
            Checksums = [.. ParseChecksums(root.Prop("checksum8"), 1), .. ParseChecksums(root.Prop("checksum16"), 2)],
        };
    }

    private static List<ScoreSlot> ParseSlots(JsonElement? section)
    {
        var slots = new List<ScoreSlot>();
        if (section is not { ValueKind: JsonValueKind.Array } array) return slots;

        foreach (var entry in array.EnumerateArray())
        {
            var label = entry.Prop("label").Str() ?? entry.Prop("short_label").Str();
            if (label is null) continue;

            Descriptor? value = null;
            string? valueKey = null;
            foreach (var key in ValueKeyPriority)
            {
                value = Descriptor.From(entry.Prop(key));
                if (value is not null) { valueKey = key; break; }
            }

            slots.Add(new ScoreSlot
            {
                Label = label,
                Initials = Descriptor.From(entry.Prop("initials")),
                Value = value,
                ValueKey = valueKey,
            });
        }

        return slots;
    }

    private static List<ChecksumRegion> ParseChecksums(JsonElement? section, int width)
    {
        var regions = new List<ChecksumRegion>();
        if (section is not { ValueKind: JsonValueKind.Array } array) return regions;

        foreach (var entry in array.EnumerateArray())
        {
            var start = entry.Prop("start").Number();
            if (start is null) continue;

            var end = entry.Prop("end").Number();
            var length = entry.Prop("length").Number();
            if (end is null && length is not null) end = start + length - 1;
            if (end is null) continue;

            regions.Add(new ChecksumRegion(
                start.Value,
                end.Value,
                entry.Prop("checksum").Number(),
                entry.Prop("label").Str(),
                width,
                (int?)entry.Prop("groupings").Number()));
        }

        return regions;
    }
}
