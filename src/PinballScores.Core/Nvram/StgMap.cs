using System.Text.Json;

namespace PinballScores.Core.Nvram;

/// <summary>One saved slot on a Visual Pinball table, and the streams holding it.</summary>
public sealed record StgSlot(string Label, string? InitialsStream, string ValueStream);

/// <summary>
/// A Visual Pinball table's layout inside the shared VPReg.stg.
///
/// These maps exist because VPX field naming is per-table-script convention rather
/// than a standard: champion fields follow no rank pattern (<c>HighScoreXandar</c>),
/// and the number of ranked slots varies from four to sixteen. Guessing from stream
/// names would eventually guess wrong, and a category key is expensive to correct
/// once rows are stored under it.
/// </summary>
public sealed class StgMap
{
    /// <summary>Name of the storage inside VPReg.stg, which is also the table id.</summary>
    public required string Storage { get; init; }

    public IReadOnlyList<StgSlot> Slots { get; init; } = [];

    public IReadOnlyList<CategoryDefinition> Categories { get; init; } = [];

    public CategoryDefinition? CategoryForSlot(string slotLabel) =>
        Categories.FirstOrDefault(c => c.Slots.Contains(slotLabel, StringComparer.OrdinalIgnoreCase));

    public static StgMap? Parse(JsonDocument doc)
    {
        var root = doc.RootElement;
        var storage = root.Prop("_metadata")?.Prop("storage").Str()
            ?? root.Prop("_pinballscores")?.Prop("cabinet_table").Str();
        if (storage is null) return null;

        var slots = new List<StgSlot>();
        foreach (var section in new[] { "high_scores", "mode_champions" })
        {
            if (root.Prop(section) is not { ValueKind: JsonValueKind.Array } array) continue;

            foreach (var entry in array.EnumerateArray())
            {
                var label = entry.Prop("label").Str();
                var value = entry.Prop("score")?.Prop("stream").Str()
                    ?? entry.Prop("counter")?.Prop("stream").Str();
                if (label is null || value is null) continue;

                slots.Add(new StgSlot(label, entry.Prop("initials")?.Prop("stream").Str(), value));
            }
        }

        return new StgMap
        {
            Storage = storage,
            Slots = slots,
            Categories = CategoryDefinition.Parse(root),
        };
    }
}
