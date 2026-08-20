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
/// </summary>
public sealed class StgScoreSource : IScoreSource
{
    private const string ScorePrefix = "HighScore";
    private const string NameSuffix = "Name";

    private readonly string _path;

    public StgScoreSource(string path) => _path = path;

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
            foreach (var entry in root.EnumerateEntries())
            {
                if (entry.Type != EntryType.Storage) continue;

                ExtractionResult result;
                try
                {
                    result = ReadTable(root, entry.Name);
                }
                catch (Exception ex) when (ex is IOException or FormatException or KeyNotFoundException)
                {
                    result = ExtractionResult.Skip(entry.Name, $"unreadable table storage: {ex.Message}");
                }

                yield return result;
            }
        }
    }

    private static ExtractionResult ReadTable(RootStorage root, string table)
    {
        var storage = root.OpenStorage(table);
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in storage.EnumerateEntries())
        {
            if (entry.Type != EntryType.Stream) continue;
            variables[entry.Name] = ReadString(storage, entry.Name);
        }

        var scores = new List<ScoreEntry>();
        foreach (var (key, raw) in variables)
        {
            if (!key.StartsWith(ScorePrefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (key.EndsWith(NameSuffix, StringComparison.OrdinalIgnoreCase)) continue;

            // Skips Credits, ReplayValue, TotalGamesPlayed and friends by construction,
            // and any HighScore* variable that doesn't hold a number.
            if (!long.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                continue;

            var player = variables.GetValueOrDefault(key + NameSuffix, "").Trim();
            if (CategoryRules.IsUnusedSlot(player)) continue;

            scores.Add(new ScoreEntry(table, CategoryFor(key), player, value));
        }

        return new ExtractionResult(table, scores);
    }

    /// <summary>
    /// "HighScore1".."HighScore8" are the ranked main board, so they carry no category.
    /// A non-numeric suffix ("HighScoreCombo", "HighScoreXandar") names a separate board.
    /// </summary>
    private static string? CategoryFor(string key)
    {
        var suffix = key[ScorePrefix.Length..].Trim();
        if (suffix.Length == 0 || suffix.All(char.IsDigit)) return null;
        return CategoryRules.Normalise(suffix);
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
