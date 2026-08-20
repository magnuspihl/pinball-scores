using PinballScores.Core.Api;
using PinballScores.Core.Nvram;

namespace PinballScores.Core.Insertion;

/// <summary>
/// Stub write-back for VPinMAME .nv files.
///
/// The remaining work is byte-level, and the hard part is already solved: each
/// Stern SAM record carries a 2-byte integrity tag equal to
/// <c>0xFFFF - sum(first 28 bytes)</c>, stored little-endian at record+0x1c. Writing
/// a record without recomputing it makes the ROM reject the record on boot and
/// restore the factory default, which is why every earlier write attempt silently
/// reverted. That tag is now expressed as a standard checksum16 region in the
/// bundled maps, so a real implementation can recompute it generically rather than
/// per game. research/patch_nvram_score.py is the working reference.
///
/// Not enabled until it has been proven against the physical cabinet.
/// </summary>
public sealed class NvramScoreWriter : IScoreWriter
{
    private readonly MapCatalog _catalog;
    private readonly Placeholder _placeholder;

    public NvramScoreWriter(MapCatalog catalog, Placeholder? placeholder = null)
    {
        _catalog = catalog;
        _placeholder = placeholder ?? Placeholder.Default;
    }

    public string Name => "nvram";

    public bool Handles(string table) => _catalog.Find(table) is not null;

    public int SlotCount(string table, string? category)
    {
        var map = _catalog.Find(table);
        if (map is null) return 0;

        if (category is null) return map.HighScores.Count;
        return map.ModeChampions.Count(s => CategoryRules.Normalise(s.Label) == category);
    }

    public Task<WriteResult> WriteAsync(
        string table,
        IReadOnlyList<RemoteScore> board,
        CancellationToken cancellationToken = default)
    {
        var map = _catalog.Find(table);
        if (map is null) return Task.FromResult(WriteResult.Skip(table, "no bundled map"));

        // The plan is computed for real so the pipeline and its logging are exercised;
        // only the byte write is withheld.
        var planned = SlotPlanner.Plan(map, board, _placeholder)
            .Select(a => $"{a.SlotLabel} <- {a.Initials} {a.Value}{(a.IsPlaceholder ? " (blank)" : "")}")
            .ToList();

        return Task.FromResult(new WriteResult(table, Applied: false, planned,
            "write-back not enabled — pending real-hardware verification"));
    }
}
