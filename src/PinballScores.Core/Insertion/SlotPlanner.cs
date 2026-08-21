using PinballScores.Core.Api;
using PinballScores.Core.Nvram;

namespace PinballScores.Core.Insertion;

/// <summary>One machine slot and the score that should occupy it.</summary>
/// <param name="IsPlaceholder">
/// True when this slot is being blanked rather than filled from the API.
/// </param>
public sealed record SlotAssignment(
    string SlotLabel,
    string? Category,
    string Initials,
    long Value,
    bool IsPlaceholder = false);

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

            assignments.AddRange(Assign(ordered, board, category.ApiCategory, placeholder));
        }

        return assignments;
    }

    private static IEnumerable<SlotAssignment> Assign(
        IReadOnlyList<ScoreSlot> slots,
        IReadOnlyList<RemoteScore> board,
        string? category,
        Placeholder placeholder)
    {
        var ranked = board
            .Where(s => string.Equals(s.Category, category, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(s => s.AsInt64)
            .Take(slots.Count)
            .ToList();

        for (var i = 0; i < slots.Count; i++)
        {
            yield return i < ranked.Count
                ? new SlotAssignment(slots[i].Label, category, ranked[i].Initials, ranked[i].AsInt64)
                : new SlotAssignment(slots[i].Label, category, placeholder.Initials, placeholder.Value, IsPlaceholder: true);
        }
    }
}
