using PinballScores.Core.Api;

namespace PinballScores.Core.Insertion;

/// <summary>What a write-back attempt did, or would have done.</summary>
/// <param name="Planned">Human-readable description of each slot assignment.</param>
public sealed record WriteResult(string Table, bool Applied, IReadOnlyList<string> Planned, string? Skipped = null)
{
    public static WriteResult Skip(string table, string reason) => new(table, false, [], reason);
}

/// <summary>
/// Writes the API's authoritative board back into a machine's own save data, so the
/// cabinet's attract mode agrees with the website.
///
/// Not yet implemented — the write path is being proven out separately (see
/// research/FINDINGS.md). The interface and the slot-assignment logic are live so
/// the run loop has its final shape; only the byte-level write is stubbed.
/// </summary>
public interface IScoreWriter
{
    string Name { get; }

    /// <summary>Whether this writer handles the given machine.</summary>
    bool Handles(string table);

    /// <summary>
    /// How many slots the machine has for a category, which is the number of rows
    /// worth requesting from the API.
    /// </summary>
    int SlotCount(string table, string? category);

    /// <summary>
    /// Applies the board to the machine's save data.
    /// </summary>
    /// <param name="dryRun">
    /// Compute and report the plan without touching a single byte. This is what
    /// makes --plan safe to run on a live cabinet, so it must be honoured before
    /// any file is opened for writing.
    /// </param>
    Task<WriteResult> WriteAsync(
        string table,
        IReadOnlyList<RemoteScore> board,
        bool dryRun = false,
        CancellationToken cancellationToken = default);
}
