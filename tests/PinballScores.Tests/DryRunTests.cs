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
