using System.Reflection;
using System.Text.Json;

namespace PinballScores.Core.Nvram;

/// <summary>
/// The bundled NVRAM maps. Embedded in the assembly so a deployment stays a single
/// artifact; an optional override directory lets a new table be supported without
/// waiting for a release.
/// </summary>
public sealed class MapCatalog
{
    private const string MapResourcePrefix = "PinballScores.Core.Maps.";
    private const string PlatformSegment = "platforms.";

    private readonly Dictionary<string, NvramMap> _maps = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PlatformDefinition> _platforms = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> KnownRoms => _maps.Keys;

    public static MapCatalog Load(string? overrideDirectory = null)
    {
        var catalog = new MapCatalog();
        var assembly = typeof(MapCatalog).Assembly;

        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(MapResourcePrefix, StringComparison.Ordinal)) continue;
            using var stream = assembly.GetManifestResourceStream(name);
            if (stream is null) continue;
            using var doc = JsonDocument.Parse(stream);

            var relative = name[MapResourcePrefix.Length..];
            if (relative.StartsWith(PlatformSegment, StringComparison.OrdinalIgnoreCase))
            {
                var platformName = TrimSuffix(relative[PlatformSegment.Length..], ".json");
                catalog._platforms[platformName] = PlatformDefinition.Parse(platformName, doc);
            }
            else
            {
                var romId = TrimSuffix(relative, ".map.json");
                catalog._maps[romId] = NvramMap.Parse(romId, doc);
            }
        }

        if (overrideDirectory is not null && Directory.Exists(overrideDirectory))
            catalog.LoadFrom(overrideDirectory);

        return catalog;
    }

    private void LoadFrom(string directory)
    {
        foreach (var path in Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories))
        {
            using var stream = File.OpenRead(path);
            JsonDocument doc;
            try { doc = JsonDocument.Parse(stream); }
            catch (JsonException) { continue; }

            using (doc)
            {
                var isPlatform = Path.GetDirectoryName(path)?
                    .EndsWith("platforms", StringComparison.OrdinalIgnoreCase) ?? false;
                var name = TrimSuffix(Path.GetFileName(path), isPlatform ? ".json" : ".map.json");

                if (isPlatform) _platforms[name] = PlatformDefinition.Parse(name, doc);
                else _maps[name] = NvramMap.Parse(name, doc);
            }
        }
    }

    private static string TrimSuffix(string value, string suffix) =>
        value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? value[..^suffix.Length] : value;

    public NvramMap? Find(string romId) => _maps.GetValueOrDefault(romId);

    public PlatformDefinition PlatformFor(NvramMap map) =>
        map.Platform is { } name && _platforms.TryGetValue(name, out var platform)
            ? platform
            : PlatformDefinition.Default;
}
