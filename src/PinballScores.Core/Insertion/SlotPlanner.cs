using PinballScores.Core.Api;
using PinballScores.Core.Nvram;

namespace PinballScores.Core.Insertion;

/// <summary>One machine slot and the score that should occupy it.</summary>
/// <param name="IsPlaceholder">
/// True when this slot is being blanked rather than filled from the API.
/// </param>
/// <param name="Fields">
/// Extra fields of the record, as map key → text, for the categories whose rows
/// are more than one number (Medieval Madness' king records carry a date and an
/// ordinal). Empty for everything else.
/// </param>
public sealed record SlotAssignment(
    string SlotLabel,
    string? Category,
    string Initials,
    long Value,
    bool IsPlaceholder = false,
    IReadOnlyDictionary<string, string>? Fields = null);

/// <summary>
/// The filler written into a slot the API has no score for.
///
/// Machines are blanked by writing a very low score rather than an empty record:
/// a ROM treats a cleared record as invalid and restores its compiled-in factory
/// default, so "empty" does not stay empty. A valid record with a token value does.
///
/// The marker is a blank, not a dash. Live-cabinet testing on 2026-08-20 found
/// that Williams WPC's boot-time validation rejects "-" — it is not in the
/// machine's own selectable initials alphabet — and silently reverts the record to
/// its factory default. A space is in that alphabet and survives a reload, and it
/// reads as "never played" on every platform's display. Matches
/// <c>MARKER_INITIALS</c> in research/tools/reset_demo_scores.py.
/// </summary>
/// <param name="Initials">Reserved marker, ignored on extraction so placeholders never round-trip into the API.</param>
public sealed record Placeholder(string Initials, long Value)
{
    public static readonly Placeholder Default = new(" ", 1);
}

/// <summary>
/// Decides which score goes in which physical slot.
///
/// The assignment itself is deliberately trivial, and that is the point: because
/// the machine stores only (initials, value) per record and re-sorts records
/// between slots itself, rank and slot are the same axis. Mapping the API's board
/// onto the machine's slots is an index-for-index zip, with no lookup table from
/// category names to slots.
/// </summary>
public static class SlotPlanner
{
    /// <summary>
    /// Plans every slot on the machine. Slots the API has no score for are filled
    /// with <paramref name="placeholder"/> rather than left alone, so a score the
    /// API doesn't know about cannot linger on the machine — the goal is that the
    /// cabinet always shows exactly what the API holds.
    /// </summary>
    public static IReadOnlyList<SlotAssignment> Plan(
        NvramMap map,
        IReadOnlyList<RemoteScore> board,
        Placeholder? placeholder = null)
    {
        placeholder ??= Placeholder.Default;
        var assignments = new List<SlotAssignment>();
        var slots = map.HighScores.Concat(map.ModeChampions).ToList();

        // Driven entirely by the map's category block, which also fixes the order
        // slots are filled in.
        foreach (var category in map.Categories)
        {
            var ordered = category.Slots
                .Select(label => slots.FirstOrDefault(s => string.Equals(s.Label, label, StringComparison.OrdinalIgnoreCase)))
                .OfType<ScoreSlot>()
                .ToList();

            assignments.AddRange(Assign(ordered, board, category, placeholder));
        }

        return assignments;
    }

    private static IEnumerable<SlotAssignment> Assign(
        IReadOnlyList<ScoreSlot> slots,
        IReadOnlyList<RemoteScore> board,
        CategoryDefinition category,
        Placeholder placeholder)
    {
        var key = category.ApiCategory;
        var ranked = board
            .Where(s => string.Equals(s.Category, key, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(s => s.AsInt64)
            .Take(slots.Count)
            .ToList();

        for (var i = 0; i < slots.Count; i++)
        {
            if (i < ranked.Count)
            {
                yield return new SlotAssignment(slots[i].Label, key, ranked[i].Initials, ranked[i].AsInt64,
                    Fields: Fields(category, ranked[i]));
                continue;
            }

            // A positional category is a log, and the machine's own encoding for
            // "nothing happened here" is zero — Medieval Madness' untouched king
            // slots read counter 0, ordinal 0. The low-but-nonzero placeholder
            // exists because a ranked ROM treats a zeroed score record as invalid
            // and restores its factory default; for a log, zero *is* the factory
            // default, so writing it keeps the machine's own invariant that each
            // slot's counter outranks the next.
            var blank = category.Positional ? 0 : placeholder.Value;
            yield return new SlotAssignment(slots[i].Label, key, placeholder.Initials, blank,
                IsPlaceholder: true, Fields: Blanks(category, slots[i]));
        }
    }

    /// <summary>The row's extra fields, translated from API field names to map keys.</summary>
    private static IReadOnlyDictionary<string, string>? Fields(CategoryDefinition category, RemoteScore score)
    {
        if (category.Metadata.Count == 0 || score.Metadata is null) return null;

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, mapKey) in category.Metadata)
            if (score.Metadata.TryGetValue(name, out var text) && !string.IsNullOrWhiteSpace(text))
                fields[mapKey] = text;

        return fields.Count == 0 ? null : fields;
    }

    /// <summary>
    /// What a blanked slot's extra fields become. Only the numeric ones are zeroed:
    /// there is no zero for a clock — year 0 is not a date the ROM would accept —
    /// so a blanked slot keeps whatever date was there, which nothing renders once
    /// the initials and the ordinal are gone.
    /// </summary>
    private static IReadOnlyDictionary<string, string>? Blanks(CategoryDefinition category, ScoreSlot slot)
    {
        if (!category.Positional || category.Metadata.Count == 0) return null;

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mapKey in category.Metadata.Values)
            if (slot.Field(mapKey) is { } descriptor &&
                !descriptor.Encoding.Equals("wpc_rtc", StringComparison.OrdinalIgnoreCase))
                fields[mapKey] = "0";

        return fields.Count == 0 ? null : fields;
    }
}
