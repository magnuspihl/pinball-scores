using PinballScores.Core.Nvram;

namespace PinballScores.Core.Extraction;

/// <summary>
/// Reads VPinMAME .nv files directly, using bundled memory maps.
///
/// Replaces the old PINemHi.exe subprocess: no 645KB third-party binary shipped to
/// the cabinet, no per-table console-output parsing, no ini-relative working
/// directory, and no process launch per table on a machine where stealing focus is
/// unacceptable.
/// </summary>
public sealed class NvramScoreSource : IScoreSource
{
    private readonly string _directory;
    private readonly MapCatalog _catalog;

    public NvramScoreSource(string directory, MapCatalog catalog)
    {
        _directory = directory;
        _catalog = catalog;
    }

    public string Name => "nvram";

    public IEnumerable<ExtractionResult> Extract()
    {
        if (!Directory.Exists(_directory)) yield break;

        foreach (var path in Directory.EnumerateFiles(_directory, "*.nv").OrderBy(p => p, StringComparer.Ordinal))
        {
            var rom = Path.GetFileNameWithoutExtension(path);
            ExtractionResult result;
            try
            {
                result = ReadFile(path, rom);
            }
            catch (IOException ex)
            {
                result = ExtractionResult.Skip(rom, $"could not read file: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                result = ExtractionResult.Skip(rom, $"access denied: {ex.Message}");
            }

            yield return result;
        }
    }

    private ExtractionResult ReadFile(string path, string rom)
    {
        var map = _catalog.Find(rom);
        if (map is null) return ExtractionResult.NotOurs(rom, "no memory map bundled for this ROM");

        var data = File.ReadAllBytes(path);
        // A 3-byte placeholder is not a machine; PinMAME writes real files at region size.
        if (data.Length < 512) return ExtractionResult.NotOurs(rom, $"file too small ({data.Length} bytes)");

        var reader = new NvramReader(data, map, _catalog.PlatformFor(map));

        // A map for a near-miss ROM revision decodes to plausible garbage rather than
        // erroring, so refuse the file instead of publishing invented scores.
        if (!reader.ChecksumsValid(out var failure))
            return ExtractionResult.Skip(rom, $"map does not match this file — {failure}");

        return new ExtractionResult(rom, reader.ReadScores(rom).ToList());
    }
}
