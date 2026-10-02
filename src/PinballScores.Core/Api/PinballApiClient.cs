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
/// Talks to the pinball scores API (self-hosted, pinball-scores-web). Submission is insert-only and idempotent —
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
    /// <param name="observedTables">
    /// Every table read this run, empty ones included. Sent even when there are no
    /// scores at all: a cabinet reporting "I read these eighteen boards and they are
    /// blank" is how the server learns a clear reached the machine, and a run that
    /// silently posted nothing would look identical to a run that never happened.
    /// </param>
    public async Task<SubmitResponse?> SubmitAsync(
        IReadOnlyList<string> observedTables,
        IReadOnlyList<ScoreEntry> scores,
        CancellationToken cancellationToken = default)
    {
        // Only a run that read nothing whatsoever has nothing to say.
        if (observedTables.Count == 0 && scores.Count == 0) return new SubmitResponse();

        var request = new SubmitRequest
        {
            Tables = observedTables,
            Scores = [.. scores.Select(ScoreSubmission.From)],
        };
        using var response = await _http.PostAsJsonAsync("scores", request, Json, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new PinballApiException($"submit failed with {(int)response.StatusCode}: {Trim(body)}");
        }

        await EnsureJsonAsync(response, "submit", cancellationToken).ConfigureAwait(false);
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

        await EnsureJsonAsync(response, "read", cancellationToken).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<List<RemoteScore>>(Json, cancellationToken)
            .ConfigureAwait(false) ?? [];
    }

    /// <summary>
    /// A 2xx that isn't JSON is almost always a login page or web front end that
    /// HttpClient reached by silently following a redirect. Name where it ended up
    /// instead of letting the deserializer fail on the first '&lt;'.
    /// </summary>
    private static async Task EnsureJsonAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType is not null && mediaType.Contains("json", StringComparison.OrdinalIgnoreCase)) return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        throw new PinballApiException(
            $"{operation} got {(int)response.StatusCode} {mediaType ?? "with no content type"} instead of JSON " +
            $"from {response.RequestMessage?.RequestUri}. Is the API behind a login proxy, or is ApiBaseUrl " +
            $"pointing at the website rather than the API? {Trim(body)}");
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
