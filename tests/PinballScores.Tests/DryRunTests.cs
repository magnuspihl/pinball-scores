using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using PinballScores.Core;
using Xunit;

namespace PinballScores.Tests;

/// <summary>
/// A dry run is a read-only rehearsal, used to check a cabinet's state before and
/// after clearing it. It must never submit or write anything.
/// </summary>
public class DryRunTests
{
    private static SyncOptions Options(bool dryRun) => new()
    {
        NvramPath = TestData.NvramDirectory,
        VpRegPath = TestData.VpRegPath,
        ApiBaseUrl = "https://example.test/api",
        ApiKey = "secret",
        DryRun = dryRun,
    };

    [Fact]
    public async Task DryRunNeverPostsToTheApi()
    {
        var handler = new StubHandler("[]");
        var runner = ScoreSyncRunner.Create(Options(dryRun: true), NullLoggerFactory.Instance, new HttpClient(handler));

        var report = await runner.RunAsync();

        // Scores were read, but nothing was sent.
        Assert.True(report.ScoresFound > 100);
        Assert.Equal(0, report.Inserted);
        Assert.Equal(0, report.Duplicates);
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task DryRunStillReadsBoardsSoAPlanCanBeReported()
    {
        var handler = new StubHandler("[]");
        var runner = ScoreSyncRunner.Create(Options(dryRun: true), NullLoggerFactory.Instance, new HttpClient(handler));

        await runner.RunAsync();

        // Plan reporting is the whole point, and it needs the API's current board.
        Assert.Contains(handler.Requests, r => r.Method == HttpMethod.Get);
    }

    [Fact]
    public async Task DryRunAppliesNoWrites()
    {
        var handler = new StubHandler("[]");
        var runner = ScoreSyncRunner.Create(Options(dryRun: true), NullLoggerFactory.Instance, new HttpClient(handler));

        var report = await runner.RunAsync();

        Assert.Equal(0, report.TablesWritten);
    }

    [Fact]
    public async Task ANormalRunDoesPost()
    {
        var handler = new StubHandler("""{"received":1,"inserted":1,"duplicates":0,"rejected":0}""");
        var runner = ScoreSyncRunner.Create(Options(dryRun: false), NullLoggerFactory.Instance, new HttpClient(handler));

        await runner.RunAsync();

        Assert.Contains(handler.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task AFullRunWritesTheApiBoardOntoTheMachine()
    {
        // The whole loop through the runner: read, submit, fetch the board, write it
        // back. Uses a stub API so it never touches the real one.
        var nvram = Directory.CreateTempSubdirectory("e2e-nv-").FullName;
        File.WriteAllBytes(Path.Combine(nvram, "smanve_101.nv"), TestData.Nvram("smanve_101"));

        var handler = new RoutingHandler(
            post: """{"received":1,"inserted":0,"duplicates":1,"rejected":0}""",
            get: """[{"table":"smanve_101","category":null,"initials":"WIN","value":"777000000","rank":1}]""");

        var options = new SyncOptions
        {
            NvramPath = nvram,
            ApiBaseUrl = "https://example.test/api",
            EnableWriteBack = true,
        };

        var report = await ScoreSyncRunner
            .Create(options, NullLoggerFactory.Instance, new HttpClient(handler))
            .RunAsync();

        Assert.Equal(1, report.TablesWritten);

        // The machine now holds what the API said, and nothing else.
        var after = new PinballScores.Core.Extraction.NvramScoreSource(nvram, TestData.Catalog)
            .Extract().Single();
        var board = after.Scores.Where(s => s.Category is null).ToList();
        Assert.Equal(("WIN", 777_000_000L), (board[0].Player, board[0].Value));
        Assert.Single(board);
    }

    [Fact]
    public async Task WriteBackIsSkippedWhileAGameIsRunning()
    {
        // A game flushes its own save data on exit and would discard the write.
        var nvram = Directory.CreateTempSubdirectory("e2e-busy-").FullName;
        File.WriteAllBytes(Path.Combine(nvram, "smanve_101.nv"), TestData.Nvram("smanve_101"));
        var before = File.ReadAllBytes(Path.Combine(nvram, "smanve_101.nv"));

        var options = new SyncOptions
        {
            NvramPath = nvram,
            ApiBaseUrl = "https://example.test/api",
            EnableWriteBack = true,
            // Whatever is hosting these tests is, by definition, running.
            BlockingProcesses = [System.Diagnostics.Process.GetCurrentProcess().ProcessName],
        };

        var handler = new RoutingHandler(
            post: """{"received":1,"inserted":0,"duplicates":1,"rejected":0}""",
            get: """[{"table":"smanve_101","category":null,"initials":"WIN","value":"777000000","rank":1}]""");

        var report = await ScoreSyncRunner
            .Create(options, NullLoggerFactory.Instance, new HttpClient(handler))
            .RunAsync();

        Assert.Equal(0, report.TablesWritten);
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(nvram, "smanve_101.nv")));
    }

    [Fact]
    public async Task PlaceholderScoresAreNeverSubmitted()
    {
        // Guards the clearing procedure: once a machine is blanked, running the tool
        // must not refill the database with its own filler.
        var options = Options(dryRun: false);
        options.PlaceholderInitials = ["SSR", "FRY"]; // stand in for "---" using initials present in the samples
        var handler = new StubHandler("""{"received":1,"inserted":0,"duplicates":1,"rejected":0}""");
        var runner = ScoreSyncRunner.Create(options, NullLoggerFactory.Instance, new HttpClient(handler));

        await runner.RunAsync();

        var body = handler.Bodies.FirstOrDefault(b => b is not null);
        Assert.NotNull(body);
        Assert.DoesNotContain("\"SSR\"", body);
        Assert.DoesNotContain("\"FRY\"", body);
    }
}

/// <summary>Answers GET and POST differently, so one run can do both.</summary>
internal sealed class RoutingHandler(string post, string get) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(
                request.Method == HttpMethod.Post ? post : get,
                System.Text.Encoding.UTF8,
                "application/json"),
        });
}

public class BlankedTableWriteBackTests
{
    [Fact]
    public async Task ABlankedMachineStillReceivesTheApiBoard()
    {
        // After a cabinet reset every table is blank, so extraction yields nothing
        // for it. Write-back must still run, or the API's scores can never reach a
        // freshly cleared machine — which is the normal state at go-live.
        var nvram = Directory.CreateTempSubdirectory("blank-nv-").FullName;
        var path = Path.Combine(nvram, "smanve_101.nv");
        File.WriteAllBytes(path, TestData.Nvram("smanve_101"));

        // Blank it first, exactly as the reset tool would leave it.
        await new PinballScores.Core.Insertion.NvramScoreWriter(TestData.Catalog, nvram)
            .WriteAsync("smanve_101", []);
        Assert.Empty(new PinballScores.Core.Extraction.NvramScoreSource(nvram, TestData.Catalog)
            .Extract().Single().Scores);

        var handler = new RoutingHandler(
            post: """{"received":0,"inserted":0,"duplicates":0,"rejected":0}""",
            get: """[{"table":"smanve_101","category":null,"initials":"NEW","value":"424242000","rank":1}]""");

        var report = await ScoreSyncRunner.Create(
            new SyncOptions
            {
                NvramPath = nvram,
                ApiBaseUrl = "https://example.test/api",
                EnableWriteBack = true,
            },
            NullLoggerFactory.Instance,
            new HttpClient(handler)).RunAsync();

        Assert.Equal(1, report.TablesWritten);

        var after = new PinballScores.Core.Extraction.NvramScoreSource(nvram, TestData.Catalog)
            .Extract().Single();
        Assert.Contains(after.Scores, s => s is { Player: "NEW", Value: 424_242_000 });
    }
}

public class BlockingProcessTests
{
    private static async Task<int> RunWith(IList<string> blocking)
    {
        var nvram = Directory.CreateTempSubdirectory("block-").FullName;
        File.WriteAllBytes(Path.Combine(nvram, "smanve_101.nv"), TestData.Nvram("smanve_101"));

        var handler = new RoutingHandler(
            post: """{"received":0,"inserted":0,"duplicates":0,"rejected":0}""",
            get: """[{"table":"smanve_101","category":null,"initials":"AAA","value":"1000","rank":1}]""");

        var report = await ScoreSyncRunner.Create(
            new SyncOptions
            {
                NvramPath = nvram,
                ApiBaseUrl = "https://example.test/api",
                EnableWriteBack = true,
                BlockingProcesses = blocking,
            },
            NullLoggerFactory.Instance,
            new HttpClient(handler)).RunAsync();

        return report.TablesWritten;
    }

    [Fact]
    public async Task APrefixMatchesTheRealExecutableName()
    {
        // "VPinballX" must catch "VPinballX64" — an exact-match miss would let
        // write-back run while a table is open, and be overwritten on exit.
        var self = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
        var prefix = self.Length > 3 ? self[..3] : self;

        Assert.Equal(0, await RunWith([prefix]));
    }

    [Fact]
    public async Task AnUnrelatedNameDoesNotBlock()
    {
        Assert.Equal(1, await RunWith(["definitely-not-running-xyz"]));
    }

    [Fact]
    public async Task AnEmptyListDoesNotBlock()
    {
        // A launcher that never exits must not be listed; an empty list is valid.
        Assert.Equal(1, await RunWith([]));
    }

    [Fact]
    public async Task BlankEntriesAreIgnoredRatherThanMatchingEverything()
    {
        // A stray "" would prefix-match every process and disable write-back.
        Assert.Equal(1, await RunWith([""]));
    }
}
