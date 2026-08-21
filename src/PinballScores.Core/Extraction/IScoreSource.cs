using PinballScores.Core.Models;

namespace PinballScores.Core.Extraction;

/// <summary>Result of reading one machine, including why it was skipped if it was.</summary>
/// <param name="SkipIsRoutine">
/// True when the skip is expected and uninteresting — an unmapped ROM, a table not
/// installed. A VPinMAME nvram folder can hold dozens of ROMs the cabinet does not
/// use, so logging each one at information level buries everything else.
/// False marks a skip worth noticing, such as a map that fails its checksums.
/// </param>
public sealed record ExtractionResult(
    string Table,
    IReadOnlyList<ScoreEntry> Scores,
    string? Skipped = null,
    bool SkipIsRoutine = false)
{
    /// <summary>A skip worth reporting: something is wrong and scores are being lost.</summary>
    public static ExtractionResult Skip(string table, string reason) => new(table, [], reason);

    /// <summary>An expected skip: this file was never one of ours.</summary>
    public static ExtractionResult NotOurs(string table, string reason) => new(table, [], reason, SkipIsRoutine: true);
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
