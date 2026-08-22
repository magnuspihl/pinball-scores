using PinballScores.Core;
using PinballScores.Core.Extraction;
using Xunit;

namespace PinballScores.Tests;

/// <summary>
/// Runs the extractor against the real reset files produced by
/// research/tools/reset_demo_scores.py. A freshly blanked cabinet must submit
/// nothing at all — otherwise the first run after a database wipe refills it with
/// the machine's own filler and the blank slate is lost.
/// </summary>
public class ResetStateTests
{
    private static string ResetDirectory => Path.Combine(TestData.RepoRoot, "research", "demo-reset");

    private static string NvramDirectory => Path.Combine(ResetDirectory, "nvram");

    private static string VpRegPath => Path.Combine(ResetDirectory, "VPReg.stg");

    [Fact]
    public void ResetFixturesArePresent()
    {
        Assert.True(Directory.Exists(NvramDirectory), $"missing {NvramDirectory}");
        Assert.True(File.Exists(VpRegPath), $"missing {VpRegPath}");
    }

    [Fact]
    public void ABlankedNvramMachineYieldsNoScores()
    {
        var results = new NvramScoreSource(NvramDirectory, TestData.Catalog).Extract().ToList();

        // Tables are still recognised — they are just empty.
        Assert.True(results.Count(r => r.Skipped is null) >= 15);

        var leaked = results.SelectMany(r => r.Scores).ToList();
        Assert.Empty(leaked);
    }

    [Fact]
    public void BlankInitialsAreTheMarkerNotDashes()
    {
        // Guards the 2026-08-20 finding: Williams WPC reverts any record whose
        // initials contain a dash, so the reset writes blanks. If a future reset
        // went back to "---" on a WPC table, that table would quietly return to its
        // factory demo scores instead of reading as blank.
        var wpc = new NvramScoreSource(NvramDirectory, TestData.Catalog).Extract()
            .Single(r => r.Table == "taf_l7");

        Assert.Null(wpc.Skipped);
        Assert.Empty(wpc.Scores);
    }

    [Fact]
    public void MappedVisualPinballTablesAreBlankedToo()
    {
        var results = new StgScoreSource(VpRegPath, TestData.Catalog).Extract().ToList();

        foreach (var table in new[] { "gotg_2020", "jpsdeadpool", "gameofthrones" })
        {
            var reset = Assert.Single(results, r => r.Table == table);
            Assert.Empty(reset.Scores);
        }
    }

    [Fact]
    public void UnmappedVisualPinballStoragesAreNotRead()
    {
        // leprechaun still holds scores in the reset VPReg.stg, because the reset
        // tool is map-driven and there is no map for it. Reading it would put those
        // stale scores straight back into a freshly wiped database, so extraction is
        // map-driven for exactly the same reason.
        var stg = new StgScoreSource(VpRegPath, TestData.Catalog).Extract().ToList();

        Assert.DoesNotContain(stg, r => r.Table == "leprechaun");
    }

    [Fact]
    public async Task AFullyBlankedCabinetSubmitsNoScoresButStillReportsItsTables()
    {
        // No exclusions needed: only mapped tables are read at all.
        var options = new SyncOptions
        {
            NvramPath = NvramDirectory,
            VpRegPath = VpRegPath,
            ApiBaseUrl = "https://example.test/api",
        };

        var handler = new StubHandler("""{"received":0,"inserted":0,"duplicates":0,"rejected":0}""");
        var runner = ScoreSyncRunner.Create(options, Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance, new HttpClient(handler));

        var report = await runner.RunAsync();

        Assert.Equal(0, report.ScoresFound);

        // The blanked cabinet must still POST, carrying no scores and every table it
        // read. That report is the whole point: it is how the server learns the clear
        // reached the machine, so that a score appearing later is recognised as newly
        // achieved rather than as the old board coming back.
        var post = Assert.Single(handler.Requests, r => r.Method == HttpMethod.Post);
        var body = handler.Bodies[handler.Requests.IndexOf(post)];

        using var sent = System.Text.Json.JsonDocument.Parse(body!);
        Assert.Empty(sent.RootElement.GetProperty("scores").EnumerateArray());
        Assert.True(sent.RootElement.GetProperty("tables").GetArrayLength() >= 15);
    }

    [Fact]
    public async Task AnUnreadableTableIsNotReportedAsBlank()
    {
        // The dangerous confusion in the other direction. A table whose file is
        // missing, locked or unmapped is skipped, and it must not appear in tables:
        // the server reads a reported table with no scores as "the machine's board is
        // empty", so a failed read that claimed to be a blank board would tell it a
        // clear had landed when the machine still holds every score.
        var nvram = Directory.CreateTempSubdirectory("unreadable-").FullName;
        File.WriteAllBytes(Path.Combine(nvram, "smanve_101.nv"), TestData.Nvram("smanve_101"));
        File.WriteAllBytes(Path.Combine(nvram, "taf_l7.nv"), [1, 2, 3]); // truncated, cannot be read

        // taf_l7 is a mapped table, so it is genuinely skipped for being unreadable —
        // not quietly absent because nothing knows about it. Without this the test
        // below would pass whatever the runner did.
        var taf = Assert.Single(
            new NvramScoreSource(nvram, TestData.Catalog).Extract(),
            r => r.Table == "taf_l7");
        Assert.NotNull(taf.Skipped);

        var handler = new StubHandler("""{"received":0,"inserted":0,"duplicates":0,"rejected":0}""");
        var runner = ScoreSyncRunner.Create(
            new SyncOptions { NvramPath = nvram, ApiBaseUrl = "https://example.test/api" },
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,
            new HttpClient(handler));

        await runner.RunAsync();

        var body = handler.Bodies.First(b => b is not null);
        using var sent = System.Text.Json.JsonDocument.Parse(body!);
        var tables = sent.RootElement.GetProperty("tables").EnumerateArray().Select(t => t.GetString()).ToList();

        Assert.Contains("smanve_101", tables);
        Assert.DoesNotContain("taf_l7", tables);
    }
}
