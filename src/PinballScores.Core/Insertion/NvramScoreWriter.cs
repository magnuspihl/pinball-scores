using PinballScores.Core.Api;
using PinballScores.Core.Nvram;

namespace PinballScores.Core.Insertion;

/// <summary>
/// Writes the API's board into VPinMAME .nv files.
///
/// Each Stern SAM record carries a 2-byte integrity tag equal to
/// <c>0xFFFF - sum(first 28 bytes)</c>; WPC games checksum their own regions. A
/// record written without recomputing these is rejected on boot and replaced with
/// the ROM's compiled-in factory default, which is why earlier attempts appeared to
/// succeed and then reverted. Checksums are recomputed for the whole image after
/// the last field is written, and the result is verified by re-reading it before
/// anything reaches disk.
///
/// Two platform quirks, both found by live-cabinet testing:
///
/// - <b>Williams WPC rejects initials outside its own selectable alphabet.</b> A
///   "-" is not in it. <see cref="NvramWriter.SanitiseInitials"/> coerces anything
///   unwritable to a space.
/// - <b>Star Wars (stwr_107) keeps an undocumented shadow copy</b> of the top
///   slot's score and restores the whole table from it when rank 1 looks too low.
///   Writing low values there reverts everything; that table is excluded until the
///   shadow is mapped.
/// </summary>
public sealed class NvramScoreWriter : IScoreWriter
{
    /// <summary>
    /// ROMs known to undo our writes through a mechanism the maps do not describe.
    /// Writing to these produces a confusing partial revert rather than an error.
    /// </summary>
    private static readonly HashSet<string> UnsafeToWrite = new(StringComparer.OrdinalIgnoreCase)
    {
        "stwr_107",
    };

    private readonly MapCatalog _catalog;
    private readonly string _directory;
    private readonly Placeholder _placeholder;

    public NvramScoreWriter(MapCatalog catalog, string directory, Placeholder? placeholder = null)
    {
        _catalog = catalog;
        _directory = directory;
        _placeholder = placeholder ?? Placeholder.Default;
    }

    public string Name => "nvram";

    public bool Handles(string table) => _catalog.Find(table) is not null;

    public int SlotCount(string table, string? category)
    {
        var map = _catalog.Find(table);
        if (map is null) return 0;

        return map.Categories.FirstOrDefault(c => c.Matches(category))?.Slots.Count ?? 0;
    }

    public Task<WriteResult> WriteAsync(
        string table,
        IReadOnlyList<RemoteScore> board,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        var map = _catalog.Find(table);
        if (map is null) return Task.FromResult(WriteResult.Skip(table, "no bundled map"));

        if (UnsafeToWrite.Contains(table))
            return Task.FromResult(WriteResult.Skip(table, "ROM restores this table from an unmapped shadow copy"));

        var path = Path.Combine(_directory, table + ".nv");
        if (!File.Exists(path)) return Task.FromResult(WriteResult.Skip(table, "no .nv file"));

        var plan = SlotPlanner.Plan(map, board, _placeholder);
        var planned = plan
            .Select(a => $"{a.SlotLabel} <- {a.Initials} {a.Value}{(a.IsPlaceholder ? " (blank)" : "")}")
            .ToList();

        if (dryRun) return Task.FromResult(new WriteResult(table, Applied: false, planned, "dry run"));

        try
        {
            var original = File.ReadAllBytes(path);
            var updated = Apply(map, original, plan);

            // Refuse to write anything we cannot read back correctly. A map that is
            // subtly wrong would otherwise leave the machine in a state neither we
            // nor the ROM agrees with.
            if (Verify(map, updated, plan) is { } mismatch)
                return Task.FromResult(WriteResult.Skip(table, $"write verification failed: {mismatch}"));

            if (updated.AsSpan().SequenceEqual(original))
                return Task.FromResult(new WriteResult(table, Applied: false, planned, "already up to date"));

            SafeWrite(path, updated);
            return Task.FromResult(new WriteResult(table, Applied: true, planned));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or ArgumentOutOfRangeException or NotSupportedException)
        {
            return Task.FromResult(WriteResult.Skip(table, $"write failed: {ex.Message}"));
        }
    }

    private byte[] Apply(NvramMap map, byte[] original, IReadOnlyList<SlotAssignment> plan)
    {
        var writer = new NvramWriter(original, map, _catalog.PlatformFor(map));
        var slots = map.HighScores.Concat(map.ModeChampions).ToList();

        foreach (var assignment in plan)
        {
            var slot = slots.FirstOrDefault(s =>
                string.Equals(s.Label, assignment.SlotLabel, StringComparison.OrdinalIgnoreCase));
            if (slot is null) continue;

            if (slot.Initials is { } initials) writer.WriteChars(initials, assignment.Initials);
            if (slot.Value is { } value) writer.WriteValue(value, assignment.Value);
        }

        // Once, after every field: a per-write update would checksum stale bytes.
        writer.UpdateChecksums();
        return writer.Data;
    }

    private string? Verify(NvramMap map, byte[] updated, IReadOnlyList<SlotAssignment> plan)
    {
        var reader = new NvramReader(updated, map, _catalog.PlatformFor(map));
        if (!reader.ChecksumsValid(out var failure)) return failure;

        var written = reader.ReadScores("verify").ToList();

        foreach (var assignment in plan.Where(a => !a.IsPlaceholder))
        {
            var expectedInitials = NvramWriter.SanitiseInitials(assignment.Initials, 32).TrimEnd();
            var match = written.FirstOrDefault(s =>
                s.Value == assignment.Value &&
                string.Equals(s.Player, expectedInitials.Trim(), StringComparison.OrdinalIgnoreCase));

            if (match is null)
                return $"'{assignment.Initials} {assignment.Value}' did not read back from {assignment.SlotLabel}";
        }

        return null;
    }

    /// <summary>
    /// Writes via a temporary file and replaces, so an interrupted write cannot
    /// leave a half-updated image that the ROM would reject wholesale.
    /// </summary>
    private static void SafeWrite(string path, byte[] data)
    {
        var temporary = path + ".tmp";
        File.WriteAllBytes(temporary, data);

        try
        {
            File.Replace(temporary, path, destinationBackupFileName: null);
        }
        catch (PlatformNotSupportedException)
        {
            File.Copy(temporary, path, overwrite: true);
            File.Delete(temporary);
        }
        catch (FileNotFoundException)
        {
            File.Move(temporary, path, overwrite: true);
        }
    }
}
