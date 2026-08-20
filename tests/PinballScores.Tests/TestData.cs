using PinballScores.Core.Nvram;

namespace PinballScores.Tests;

/// <summary>
/// Locates the sample cabinet data committed under ScoresData/. These are genuine
/// captures, so the tests below assert against real machine bytes rather than
/// fixtures we invented.
/// </summary>
public static class TestData
{
    public static string RepoRoot { get; } = FindRoot();

    public static string NvramDirectory => Path.Combine(RepoRoot, "ScoresData", "nvram");

    public static string VpRegPath => Path.Combine(RepoRoot, "ScoresData", "User", "VPReg.stg");

    public static MapCatalog Catalog { get; } = MapCatalog.Load();

    public static byte[] Nvram(string rom) => File.ReadAllBytes(Path.Combine(NvramDirectory, rom + ".nv"));

    public static NvramReader ReaderFor(string rom, byte[]? data = null)
    {
        var map = Catalog.Find(rom) ?? throw new InvalidOperationException($"no bundled map for {rom}");
        return new NvramReader(data ?? Nvram(rom), map, Catalog.PlatformFor(map));
    }

    private static string FindRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir, "ScoresData"))) return dir;
            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException("could not locate repository root containing ScoresData");
    }
}
