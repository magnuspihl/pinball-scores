using System.Net;
using System.Text;
using System.Text.Json;
using PinballScores.Core.Api;
using PinballScores.Core.Models;
using Xunit;

namespace PinballScores.Tests;

internal sealed class StubHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _status;
    private readonly string _body;

    public List<HttpRequestMessage> Requests { get; } = [];
    public List<string?> Bodies { get; } = [];

    public HttpRequestMessage? LastRequest => Requests.Count == 0 ? null : Requests[^1];
    public string? LastBody => Bodies.Count == 0 ? null : Bodies[^1];

    public StubHandler(string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        _body = body;
        _status = status;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        Bodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));

        return new HttpResponseMessage(_status)
        {
            Content = new StringContent(_body, Encoding.UTF8, "application/json"),
        };
    }
}

public class PinballApiClientTests
{
    private static PinballApiClient Client(StubHandler handler) => new(
        new PinballApiOptions { BaseUrl = "https://example.test/api", ApiKey = "secret", Source = "test" },
        new HttpClient(handler));

    [Fact]
    public async Task SendsApiKeyAndSourceHeaders()
    {
        var handler = new StubHandler("""{"received":1,"inserted":1}""");
        using var client = Client(handler);

        await client.SubmitAsync([new ScoreEntry("t", null, "MHP", 1)]);

        Assert.Equal("secret", handler.LastRequest!.Headers.GetValues("X-API-Key").Single());
        Assert.Equal("test", handler.LastRequest.Headers.GetValues("X-Source").Single());
    }

    [Fact]
    public async Task SendsInt64ValuesAsStringsToSurviveJson()
    {
        var handler = new StubHandler("""{"received":1,"inserted":1}""");
        using var client = Client(handler);

        // Above 2^53 a JSON number would lose precision in a JavaScript server.
        await client.SubmitAsync([new ScoreEntry("sttng_l7", null, "TEX", 16_000_000_000)]);

        using var sent = JsonDocument.Parse(handler.LastBody!);
        var entry = sent.RootElement.GetProperty("scores")[0];
        Assert.Equal(JsonValueKind.String, entry.GetProperty("value").ValueKind);
        Assert.Equal("16000000000", entry.GetProperty("value").GetString());
    }

    [Fact]
    public async Task SendsNullCategoryForTheMainBoard()
    {
        var handler = new StubHandler("""{"received":1,"inserted":1}""");
        using var client = Client(handler);

        await client.SubmitAsync([new ScoreEntry("mm_109c", null, "FRY", 89_407_420)]);

        using var sent = JsonDocument.Parse(handler.LastBody!);
        var entry = sent.RootElement.GetProperty("scores")[0];
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("category").ValueKind);
    }

    [Fact]
    public async Task MapsValueKindOntoTheApiEnum()
    {
        var handler = new StubHandler("""{"received":4,"inserted":4}""");
        using var client = Client(handler);

        await client.SubmitAsync([
            new ScoreEntry("t", null, "AAA", 1, ScoreValueKind.Score),
            new ScoreEntry("t", "C", "BBB", 2, ScoreValueKind.Counter, "Castles Destroyed"),
            new ScoreEntry("t", "D", "CCC", 3, ScoreValueKind.Duration),
            new ScoreEntry("t", "E", "DDD", 4, ScoreValueKind.Timestamp),
        ]);

        using var sent = JsonDocument.Parse(handler.LastBody!);
        var entries = sent.RootElement.GetProperty("scores");
        Assert.Equal("score", entries[0].GetProperty("value_type").GetString());
        Assert.Equal("counter", entries[1].GetProperty("value_type").GetString());
        Assert.Equal("Castles Destroyed", entries[1].GetProperty("display_suffix").GetString());
        Assert.Equal("duration", entries[2].GetProperty("value_type").GetString());
        Assert.Equal("timestamp", entries[3].GetProperty("value_type").GetString());
    }

    [Fact]
    public async Task ParsesPerEntryResults()
    {
        var handler = new StubHandler("""
            {"received":3,"inserted":1,"duplicates":1,"rejected":1,"new_tables":[],
             "results":[
               {"index":0,"status":"inserted","initials":"AAA","value":"10"},
               {"index":1,"status":"duplicate","initials":"BBB","value":"20"},
               {"index":2,"status":"rejected","reason":"no initials"}]}
            """);
        using var client = Client(handler);

        var response = await client.SubmitAsync([new ScoreEntry("t", null, "AAA", 10)]);

        Assert.NotNull(response);
        Assert.Equal(1, response.Inserted);
        Assert.Equal(1, response.Duplicates);
        Assert.Single(response.Results, r => r.WasInserted);
        Assert.Single(response.Results, r => r.WasRejected && r.Reason == "no initials");
    }

    [Fact]
    public async Task EmptyBatchDoesNotCallTheApi()
    {
        var handler = new StubHandler("""{"received":0}""");
        using var client = Client(handler);

        var response = await client.SubmitAsync([]);

        Assert.NotNull(response);
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task FailureResponseRaisesAnApiException()
    {
        var handler = new StubHandler("""{"error":"invalid or missing API key"}""", HttpStatusCode.Unauthorized);
        using var client = Client(handler);

        var ex = await Assert.ThrowsAsync<PinballApiException>(
            () => client.SubmitAsync([new ScoreEntry("t", null, "AAA", 1)]));
        Assert.Contains("401", ex.Message);
    }

    [Fact]
    public async Task ReadsBoardPreservingInt64Precision()
    {
        var handler = new StubHandler("""
            [{"table":"sttng_l7","category":null,"initials":"PMK","value":"738778270","rank":1},
             {"table":"sttng_l7","category":"Q CONTINUUM","initials":"TEX","value":"16000000000","rank":1}]
            """);
        using var client = Client(handler);

        var board = await client.GetBoardAsync("sttng_l7", 5);

        Assert.Equal(738_778_270L, board[0].AsInt64);
        Assert.Null(board[0].Category);
        Assert.Equal(16_000_000_000L, board[1].AsInt64);
        Assert.Contains("limit=5", handler.LastRequest!.RequestUri!.Query);
    }
}
