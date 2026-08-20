using System.Net.Http.Json;
using System.Text.Json;
using PinballScores.Core.Models;

namespace PinballScores.Core.Api;

public sealed class PinballApiOptions
{
    /// <summary>Base URL including the /api prefix.</summary>
    public required string BaseUrl { get; init; }

    public string? ApiKey { get; init; }

    /// <summary>Labels submitted rows so the server can tell cabinets apart.</summary>
    public string Source { get; init; } = "pinballscores-cli";

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Talks to the Foundry pinball API. Submission is insert-only and idempotent —
/// resubmitting the current board is the normal case, and the server deduplicates
/// on (table, category, initials, value).
/// </summary>
public sealed class PinballApiClient : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    public PinballApiClient(PinballApiOptions options, HttpClient? http = null)
    {
        _ownsHttp = http is null;
        _http = http ?? new HttpClient();
        _http.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
        _http.Timeout = options.Timeout;

        if (!string.IsNullOrWhiteSpace(options.ApiKey))
            _http.DefaultRequestHeaders.TryAddWithoutValidation("X-API-Key", options.ApiKey);
        _http.DefaultRequestHeaders.TryAddWithoutValidation("X-Source", options.Source);
    }

    /// <summary>
    /// Submits a batch covering any number of tables. Returns null when the request
    /// could not be delivered at all, as distinct from a delivered batch whose rows
    /// were duplicates or rejections.
    /// </summary>
    public async Task<SubmitResponse?> SubmitAsync(
        IReadOnlyList<ScoreEntry> scores,
        CancellationToken cancellationToken = default)
    {
        if (scores.Count == 0) return new SubmitResponse();

        var request = new SubmitRequest { Scores = [.. scores.Select(ScoreSubmission.From)] };
        using var response = await _http.PostAsJsonAsync("scores", request, Json, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new PinballApiException($"submit failed with {(int)response.StatusCode}: {Trim(body)}");
        }

        return await response.Content.ReadFromJsonAsync<SubmitResponse>(Json, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the authoritative board for one table, newest state first. The limit
    /// should be the machine's slot count — that is exactly what gets written back.
    /// </summary>
    public async Task<IReadOnlyList<RemoteScore>> GetBoardAsync(
        string table,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var url = $"scores?table={Uri.EscapeDataString(table)}&limit={limit}&format=json";
        using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new PinballApiException($"read failed with {(int)response.StatusCode}: {Trim(body)}");
        }

        return await response.Content.ReadFromJsonAsync<List<RemoteScore>>(Json, cancellationToken)
            .ConfigureAwait(false) ?? [];
    }

    private static string Trim(string body) => body.Length <= 300 ? body : body[..300] + "…";

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }
}

public sealed class PinballApiException : Exception
{
    public PinballApiException(string message) : base(message) { }
}
