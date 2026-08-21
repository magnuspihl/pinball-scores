using System.Globalization;
using System.Text;
using OpenMcdf;
using PinballScores.Core.Models;
using PinballScores.Core.Nvram;

namespace PinballScores.Core.Extraction;

/// <summary>
/// Reads Visual Pinball's shared VPReg.stg, an OLE Compound File holding one
/// sub-storage per table and one stream per saved variable.
///
/// Uses the managed OpenMcdf reader rather than ole32 P/Invoke, which keeps the
/// format logic portable and unit-testable instead of Windows-only.
///
/// Only tables with a bundled map are read. VPReg.stg is shared and accumulates
/// storages for tables that are not part of the cabinet's tracked set; those are
/// not reset when the cabinet is blanked, so discovering them by convention would
/// feed stale scores back into a freshly wiped database.
/// </summary>
public sealed class StgScoreSource : IScoreSource
{
    private readonly string _path;
    private readonly MapCatalog _catalog;

    public StgScoreSource(string path, MapCatalog catalog)
    {
        _path = path;
        _catalog = catalog;
    }

    public string Name => "vpx";

    public IEnumerable<ExtractionResult> Extract()
    {
        if (!File.Exists(_path)) yield break;

        RootStorage root;
        string? openFailure = null;
        try
        {
            // Share the file: Visual Pinball may hold it open.
            root = RootStorage.OpenRead(_path);
        }
        catch (Exception ex) when (ex is IOException or FormatException or NotSupportedException)
        {
            root = null!;
            openFailure = ex.Message;
        }

        if (openFailure is not null)
        {
            yield return ExtractionResult.Skip(Path.GetFileName(_path), $"could not open: {openFailure}");
            yield break;
        }

        using (root)
        {
            var present = root.EnumerateEntries()
                .Where(e => e.Type == EntryType.Storage)
                .Select(e => e.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var map in _catalog.StgMaps.OrderBy(m => m.Storage, StringComparer.Ordinal))
            {
                if (!present.Contains(map.Storage))
                {
                    yield return ExtractionResult.Skip(map.Storage, "table not present in VPReg.stg");
                    continue;
                }

                ExtractionResult result;
                try
                {
                    result = ReadTable(root, map);
                }
                catch (Exception ex) when (ex is IOException or FormatException or KeyNotFoundException)
                {
                    result = ExtractionResult.Skip(map.Storage, $"unreadable table storage: {ex.Message}");
                }

                yield return result;
            }
        }
    }

    private static ExtractionResult ReadTable(RootStorage root, StgMap map)
    {
        var storage = root.OpenStorage(map.Storage);
        var streams = storage.EnumerateEntries()
            .Where(e => e.Type == EntryType.Stream)
            .Select(e => e.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var scores = new List<ScoreEntry>();

        foreach (var slot in map.Slots)
        {
            if (!streams.Contains(slot.ValueStream)) continue;

            var raw = ReadString(storage, slot.ValueStream);
            if (!long.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                continue;

            var player = slot.InitialsStream is not null && streams.Contains(slot.InitialsStream)
                ? ReadString(storage, slot.InitialsStream).Trim()
                : "";
            if (CategoryRules.IsUnusedSlot(player)) continue;

            var category = map.CategoryForSlot(slot.Label);
            scores.Add(new ScoreEntry(
                map.Storage,
                category is not null ? category.ApiCategory : CategoryRules.Slugify(slot.Label),
                player,
                value,
                category?.ValueKind ?? ScoreValueKind.Score));
        }

        return new ExtractionResult(map.Storage, scores);
    }

    private static string ReadString(Storage storage, string name)
    {
        using var stream = storage.OpenStream(name);
        var buffer = new byte[stream.Length];
        stream.ReadExactly(buffer, 0, buffer.Length);
        // Visual Pinball stores these as UTF-16LE, sometimes null-padded.
        return Encoding.Unicode.GetString(buffer).TrimEnd('\0');
    }
}
