using PinballScores.Core.Models;

namespace PinballScores.Core.Extraction;

/// <summary>Result of reading one machine, including why it was skipped if it was.</summary>
public sealed record ExtractionResult(string Table, IReadOnlyList<ScoreEntry> Scores, string? Skipped = null)
{
    public static ExtractionResult Skip(string table, string reason) => new(table, [], reason);
}

/// <summary>
/// Somewhere scores can be read from. One implementation per save format.
/// </summary>
public interface IScoreSource
{
    string Name { get; }

    /// <summary>
    /// Reads every machine this source knows about. Implementations report a
    /// per-machine skip rather than throwing, so one unreadable table never costs
    /// us the rest of the run.
    /// </summary>
    IEnumerable<ExtractionResult> Extract();
}
