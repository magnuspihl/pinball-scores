using System.Globalization;
using System.Text;
using OpenMcdf;
using PinballScores.Core.Api;
using PinballScores.Core.Extraction;
using PinballScores.Core.Nvram;

namespace PinballScores.Core.Insertion;

/// <summary>
/// Writes the API's board into Visual Pinball's shared VPReg.stg.
///
/// The simpler of the two platforms: a Compound File with no checksums, no ROM
/// validation and no factory-default fallback, and already proven on the real
/// cabinet. Values are decimal strings and initials plain strings, both UTF-16LE.
///
/// Unlike research/patch_stg_score.py, which could only replace a value with one of
/// exactly the same byte length, this resizes streams properly — so a score does
/// not have to be zero-padded to the previous digit count.
///
/// The whole file is shared by every VPX table, so it is modified through a
/// temporary copy and swapped in. A half-written VPReg.stg would take every table's
/// scores with it, not just the one being updated.
/// </summary>
public sealed class StgScoreWriter : IScoreWriter
{
    private readonly string _path;
    private readonly MapCatalog _catalog;
    private readonly Placeholder _placeholder;

    public StgScoreWriter(string path, MapCatalog catalog, Placeholder? placeholder = null)
    {
        _path = path;
        _catalog = catalog;
        _placeholder = placeholder ?? Placeholder.Default;
    }

    public string Name => "vpx";

    public bool Handles(string table) => Find(table) is not null;

    public int SlotCount(string table, string? category)
    {
        var map = Find(table);
        if (map is null) return 0;

        return map.Categories.FirstOrDefault(c => c.Matches(category))?.Slots.Count ?? 0;
    }

    private StgMap? Find(string table) =>
        _catalog.StgMaps.FirstOrDefault(m => string.Equals(m.Storage, table, StringComparison.OrdinalIgnoreCase));

    public Task<WriteResult> WriteAsync(
        string table,
        IReadOnlyList<RemoteScore> board,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        var map = Find(table);
        if (map is null) return Task.FromResult(WriteResult.Skip(table, "no bundled STG map"));
        if (!File.Exists(_path)) return Task.FromResult(WriteResult.Skip(table, "VPReg.stg not found"));

        var plan = Plan(map, board);
        var planned = plan
            .Select(a => $"{a.SlotLabel} <- {a.Initials} {a.Value}{(a.IsPlaceholder ? " (blank)" : "")}")
            .ToList();

        if (dryRun) return Task.FromResult(new WriteResult(table, Applied: false, planned, "dry run"));

        var temporary = _path + ".tmp";
        try
        {
            File.Copy(_path, temporary, overwrite: true);

            if (!ApplyTo(temporary, map, plan))
                return Task.FromResult(new WriteResult(table, Applied: false, planned, "already up to date"));

            if (Verify(temporary, map, plan) is { } mismatch)
                return Task.FromResult(WriteResult.Skip(table, $"write verification failed: {mismatch}"));

            File.Copy(temporary, _path, overwrite: true);
            return Task.FromResult(new WriteResult(table, Applied: true, planned));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return Task.FromResult(WriteResult.Skip(table, $"write failed: {ex.Message}"));
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { /* a leftover .tmp is harmless */ }
        }
    }

    /// <summary>Assigns the board to this table's declared slots, blanking the rest.</summary>
    private IReadOnlyList<SlotAssignment> Plan(StgMap map, IReadOnlyList<RemoteScore> board)
    {
        var assignments = new List<SlotAssignment>();

        foreach (var category in map.Categories)
        {
            var slots = category.Slots
                .Select(label => map.Slots.FirstOrDefault(s =>
                    string.Equals(s.Label, label, StringComparison.OrdinalIgnoreCase)))
                .OfType<StgSlot>()
                .ToList();

            var ranked = board
                .Where(s => category.Matches(s.Category))
                .OrderByDescending(s => s.AsInt64)
                .Take(slots.Count)
                .ToList();

            for (var i = 0; i < slots.Count; i++)
            {
                assignments.Add(i < ranked.Count
                    ? new SlotAssignment(slots[i].Label, category.ApiCategory, ranked[i].Initials, ranked[i].AsInt64)
                    : new SlotAssignment(slots[i].Label, category.ApiCategory,
                        _placeholder.Initials, _placeholder.Value, IsPlaceholder: true));
            }
        }

        return assignments;
    }

    /// <summary>Returns false when nothing needed changing.</summary>
    private static bool ApplyTo(string path, StgMap map, IReadOnlyList<SlotAssignment> plan)
    {
        var changed = false;
        using var root = RootStorage.Open(path, FileMode.Open);
        var storage = root.OpenStorage(map.Storage);

        var streams = storage.EnumerateEntries()
            .Where(e => e.Type == EntryType.Stream)
            .Select(e => e.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var assignment in plan)
        {
            var slot = map.Slots.FirstOrDefault(s =>
                string.Equals(s.Label, assignment.SlotLabel, StringComparison.OrdinalIgnoreCase));
            if (slot is null) continue;

            if (streams.Contains(slot.ValueStream))
                changed |= Write(storage, slot.ValueStream,
                    assignment.Value.ToString(CultureInfo.InvariantCulture));

            if (slot.InitialsStream is { } initialsStream && streams.Contains(initialsStream))
                changed |= Write(storage, initialsStream, assignment.Initials);
        }

        return changed;
    }

    private static bool Write(Storage storage, string name, string value)
    {
        var desired = Encoding.Unicode.GetBytes(value);

        using var stream = storage.OpenStream(name);
        var existing = new byte[stream.Length];
        stream.ReadExactly(existing, 0, existing.Length);
        if (existing.AsSpan().SequenceEqual(desired)) return false;

        stream.SetLength(desired.Length);
        stream.Position = 0;
        stream.Write(desired, 0, desired.Length);
        return true;
    }

    private string? Verify(string path, StgMap map, IReadOnlyList<SlotAssignment> plan)
    {
        var written = new StgScoreSource(path, _catalog).Extract()
            .FirstOrDefault(r => string.Equals(r.Table, map.Storage, StringComparison.OrdinalIgnoreCase));

        if (written is null) return "table disappeared after writing";

        foreach (var assignment in plan.Where(a => !a.IsPlaceholder))
        {
            var match = written.Scores.FirstOrDefault(s =>
                s.Value == assignment.Value &&
                string.Equals(s.Player, assignment.Initials.Trim(), StringComparison.OrdinalIgnoreCase));

            if (match is null)
                return $"'{assignment.Initials} {assignment.Value}' did not read back from {assignment.SlotLabel}";
        }

        return null;
    }
}
