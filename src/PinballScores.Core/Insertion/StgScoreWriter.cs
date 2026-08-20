using PinballScores.Core.Api;

namespace PinballScores.Core.Insertion;

/// <summary>
/// Stub write-back for Visual Pinball's VPReg.stg.
///
/// Simpler than NVRAM: a Compound File with no checksums and no factory-default
/// fallback, and it has already been proven on the real cabinet (gotg_2020) —
/// patched scores displayed correctly and were later beaten by genuine play.
/// The known constraint is that a stream must keep its byte length, so numeric
/// replacements need zero-padding to the original digit count; growing a stream
/// would mean rewriting the CFB directory and FAT. research/patch_stg_score.py is
/// the working reference.
/// </summary>
public sealed class StgScoreWriter : IScoreWriter
{
    /// <summary>Visual Pinball tables conventionally expose five ranked slots.</summary>
    private const int DefaultSlots = 5;

    private readonly string _path;
    private readonly IReadOnlySet<string> _tables;
    private readonly Placeholder _placeholder;

    public StgScoreWriter(string path, IReadOnlySet<string> tables, Placeholder? placeholder = null)
    {
        _path = path;
        _tables = tables;
        _placeholder = placeholder ?? Placeholder.Default;
    }

    public string Name => "vpx";

    public bool Handles(string table) => _tables.Contains(table);

    public int SlotCount(string table, string? category) => category is null ? DefaultSlots : 1;

    public Task<WriteResult> WriteAsync(
        string table,
        IReadOnlyList<RemoteScore> board,
        CancellationToken cancellationToken = default)
    {
        var ranked = board
            .Where(s => s.Category is null)
            .OrderByDescending(s => s.AsInt64)
            .Take(DefaultSlots)
            .ToList();

        // Slots the API has no score for are blanked, so nothing it doesn't know
        // about can linger on the table.
        var planned = Enumerable.Range(0, DefaultSlots)
            .Select(i => i < ranked.Count
                ? $"HighScore{i + 1} <- {ranked[i].Initials} {ranked[i].AsInt64}"
                : $"HighScore{i + 1} <- {_placeholder.Initials} {_placeholder.Value} (blank)")
            .ToList();

        return Task.FromResult(new WriteResult(table, Applied: false, planned,
            $"write-back not enabled ({Path.GetFileName(_path)})"));
    }
}
