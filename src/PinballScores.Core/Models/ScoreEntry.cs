namespace PinballScores.Core.Models;

/// <summary>
/// How the API should interpret <see cref="ScoreEntry.Value"/>. Mirrors the
/// <c>value_type</c> enum in the Foundry API.
/// </summary>
public enum ScoreValueKind
{
    /// <summary>A points total. The default.</summary>
    Score,

    /// <summary>A count of things (castles destroyed, spiders squashed).</summary>
    Counter,

    /// <summary>A length of time, in seconds.</summary>
    Duration,

    /// <summary>A point in time, as UTC epoch seconds.</summary>
    Timestamp,
}

/// <summary>
/// One score as read off a machine, already normalised into the shape the API
/// accepts. Immutable: extractors produce these, nothing mutates them later.
/// </summary>
/// <param name="Table">Machine id — the ROM name, or the VPX table storage name.</param>
/// <param name="Category">
/// <c>null</c> for the main ranked leaderboard. The machine's own slot labels
/// ("Grand Champion", "First Place", "#1", "Honor Roll") are positional names for
/// rank, not distinct categories, so they all collapse to null and rank is derived
/// from score order. A non-null value is a genuinely separate achievement board
/// such as "SPIDER CHAMPION".
/// </param>
/// <param name="Player">Initials as stored on the machine.</param>
/// <param name="Value">
/// Always an integer. Never floating point — single-precision silently perturbs
/// any value above ~16.7 million, which corrupted real scores in the old CLI.
/// </param>
public sealed record ScoreEntry(
    string Table,
    string? Category,
    string Player,
    long Value,
    ScoreValueKind ValueKind = ScoreValueKind.Score,
    string? DisplaySuffix = null)
{
    public override string ToString()
    {
        var category = Category ?? "(main board)";
        var suffix = string.IsNullOrEmpty(DisplaySuffix) ? "" : " " + DisplaySuffix;
        return $"{Table} | {category} | {Player} | {Value}{suffix} [{ValueKind}]";
    }
}
