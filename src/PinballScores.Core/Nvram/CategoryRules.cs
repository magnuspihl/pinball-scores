using System.Text.RegularExpressions;

namespace PinballScores.Core.Nvram;

/// <summary>
/// Helpers for turning a machine's slots into the API's category model.
///
/// Category identity itself comes from the map's <c>_pinballscores.categories</c>
/// block, not from parsing labels — see <see cref="CategoryDefinition"/>. What
/// remains here is the fallback slug for a slot no map places, and the rule for
/// recognising an unoccupied slot.
///
/// The underlying reason a machine's slot labels are not categories: the machine
/// stores only (initials, value) per record. The checksum covers the record's bytes
/// and not its address, so a record is byte-identical whichever slot it occupies,
/// and the game freely re-sorts records between slots. "Grand Champion" and "First
/// Place" are positional names for rank, not separate boards.
/// </summary>
public static partial class CategoryRules
{
    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonSlug();

    /// <summary>
    /// Fallback category key for a slot the map does not place, in the same
    /// snake_case shape the maps use so it cannot be told apart downstream.
    /// </summary>
    public static string Slugify(string label)
    {
        var slug = NonSlug().Replace(label.Trim().ToLowerInvariant(), "_").Trim('_');
        return slug.Length > 0 ? slug : "unknown";
    }

    /// <summary>
    /// Initials that mean "nobody holds this slot". WPC leaves blanks, Stern SAM
    /// leaves 0xFF padding which decodes to empty, Gottlieb leaves nulls.
    ///
    /// Also covers the blanking marker written when a machine is reset. That marker
    /// is a space, so the whitespace check does the work; the punctuation check is
    /// kept because the marker used to be "---", which still survives in records
    /// written before WPC was found to reject dashes.
    /// </summary>
    public static bool IsUnusedSlot(string? initials)
    {
        if (string.IsNullOrWhiteSpace(initials)) return true;
        var trimmed = initials.Trim();
        return trimmed.All(c => c is '-' or '.' or '_' or '?');
    }
}
