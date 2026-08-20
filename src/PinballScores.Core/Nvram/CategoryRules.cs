using System.Text.RegularExpressions;

namespace PinballScores.Core.Nvram;

/// <summary>
/// Turns a machine's own slot labels into the API's category model.
///
/// The machine stores only (initials, value) per record — the checksum covers the
/// record's bytes and not its address, so a record is byte-identical whichever slot
/// it occupies, and the game freely re-sorts records between slots. "Grand Champion"
/// and "First Place" are therefore positional names for rank, not separate boards.
/// Rank is derived from value order; only genuinely distinct achievements keep a name.
///
/// This lives in the extractor rather than the server because it needs
/// source-format knowledge the API shouldn't have to carry.
/// </summary>
public static partial class CategoryRules
{
    /// <summary>Trailing rank markers: "#1", " 1", "1st", "No. 2".</summary>
    [GeneratedRegex(@"\s*(?:#|No\.?\s*)?\d+(?:st|nd|rd|th)?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingIndex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>
    /// Collapses a ranked set into one category name. "Gauntlet Champ 1/2/3" become
    /// "GAUNTLET CHAMP" with rank derived from the score, matching how the main board
    /// is handled one level up.
    /// </summary>
    public static string Normalise(string label)
    {
        var trimmed = Whitespace().Replace(label.Trim(), " ");
        var withoutIndex = TrailingIndex().Replace(trimmed, "").Trim();
        // Don't let a label that is *only* a number collapse to nothing.
        var result = withoutIndex.Length > 0 ? withoutIndex : trimmed;
        return result.ToUpperInvariant();
    }

    /// <summary>
    /// Initials that mean "nobody holds this slot". WPC leaves blanks, Stern SAM
    /// leaves 0xFF padding which decodes to empty, Gottlieb leaves nulls.
    /// </summary>
    public static bool IsUnusedSlot(string? initials)
    {
        if (string.IsNullOrWhiteSpace(initials)) return true;
        var trimmed = initials.Trim();
        return trimmed.All(c => c is '-' or '.' or '_' or '?');
    }
}
